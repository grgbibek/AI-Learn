using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace McpServer;

// Read-only telemetry tools that query the local standalone Aspire Dashboard directly.
// These do not talk to production telemetry backends or mutate application state.
[McpServerToolType]
public static class TelemetryTools
{
    private const string DefaultDashboardUrl = "http://localhost:18888";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [McpServerTool, Description("Gets recent OpenTelemetry traces from the local Aspire Dashboard as JSON. Use this to see recent TaskFlow API requests and their spans.")]
    public static async Task<string> GetRecentTelemetryTraces(
        [Description("Aspire Dashboard URL. Defaults to http://localhost:18888.")] string? dashboardUrl = null,
        [Description("Maximum number of traces to return. Defaults to 10, capped at 50.")] int limit = 10,
        [Description("Optional case-insensitive search query to filter trace names, span names, attributes, routes, or IDs.")] string? search = null)
    {
        var traces = await FetchTracesAsync(dashboardUrl);
        var filtered = string.IsNullOrWhiteSpace(search)
            ? traces
            : traces.Where(trace => TraceMatches(trace, search.Trim()));

        return JsonSerializer.Serialize(
            filtered
                .OrderByDescending(trace => trace.Timestamp)
                .Take(Math.Clamp(limit, 1, 50))
                .Select(ToTraceSummary),
            JsonOptions);
    }

    [McpServerTool, Description("Gets failed OpenTelemetry traces from the local Aspire Dashboard as JSON. Use this to find requests with error status.")]
    public static async Task<string> GetFailedTelemetryTraces(
        [Description("Aspire Dashboard URL. Defaults to http://localhost:18888.")] string? dashboardUrl = null,
        [Description("Maximum number of failed traces to return. Defaults to 10, capped at 50.")] int limit = 10)
    {
        var traces = await FetchTracesAsync(dashboardUrl);
        return JsonSerializer.Serialize(
            traces
                .Where(trace => trace.HasError)
                .OrderByDescending(trace => trace.Timestamp)
                .Take(Math.Clamp(limit, 1, 50))
                .Select(ToTraceSummary),
            JsonOptions);
    }

    [McpServerTool, Description("Gets the slowest recent OpenTelemetry traces from the local Aspire Dashboard as JSON, sorted by duration descending.")]
    public static async Task<string> GetSlowestTelemetryTraces(
        [Description("Aspire Dashboard URL. Defaults to http://localhost:18888.")] string? dashboardUrl = null,
        [Description("Maximum number of slow traces to return. Defaults to 5, capped at 25.")] int limit = 5)
    {
        var traces = await FetchTracesAsync(dashboardUrl);
        return JsonSerializer.Serialize(
            traces
                .OrderByDescending(trace => trace.DurationMs)
                .Take(Math.Clamp(limit, 1, 25))
                .Select(ToTraceSummary),
            JsonOptions);
    }

    [McpServerTool, Description("Gets all spans for a specific OpenTelemetry trace ID from the local Aspire Dashboard as JSON.")]
    public static async Task<string> GetTelemetrySpansForTrace(
        [Description("The OpenTelemetry traceId to inspect.")] string traceId,
        [Description("Aspire Dashboard URL. Defaults to http://localhost:18888.")] string? dashboardUrl = null,
        [Description("Maximum number of spans to return. Defaults to 100, capped at 500.")] int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(traceId))
        {
            return JsonSerializer.Serialize(new { error = "traceId is required." }, JsonOptions);
        }

        var normalizedTraceId = traceId.Trim();
        var traces = await FetchTracesAsync(dashboardUrl);
        var trace = traces.FirstOrDefault(item => string.Equals(item.TraceId, normalizedTraceId, StringComparison.OrdinalIgnoreCase));

        if (trace is null)
        {
            return JsonSerializer.Serialize(new { error = $"Trace '{normalizedTraceId}' was not found in the Aspire Dashboard in-memory store." }, JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            trace.TraceId,
            trace.Title,
            trace.DurationMs,
            trace.HasError,
            trace.Timestamp,
            trace.DashboardUrl,
            Spans = trace.Spans
                .OrderBy(span => span.Timestamp)
                .Take(Math.Clamp(limit, 1, 500))
        }, JsonOptions);
    }

    private static async Task<List<TelemetryTrace>> FetchTracesAsync(string? dashboardUrl)
    {
        var baseUrl = NormalizeDashboardUrl(dashboardUrl);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await httpClient.GetAsync($"{baseUrl}/api/telemetry/traces");

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Aspire Dashboard returned HTTP {(int)response.StatusCode}: {body}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("resourceSpans", out var resourceSpans))
        {
            return [];
        }

        var spans = new List<TelemetrySpan>();

        foreach (var resourceSpan in resourceSpans.EnumerateArray())
        {
            var serviceName = ExtractServiceName(resourceSpan);
            if (!resourceSpan.TryGetProperty("scopeSpans", out var scopeSpans))
            {
                continue;
            }

            foreach (var scopeSpan in scopeSpans.EnumerateArray())
            {
                var scopeName = scopeSpan.TryGetProperty("scope", out var scope)
                    && scope.TryGetProperty("name", out var scopeNameElement)
                        ? scopeNameElement.GetString()
                        : null;

                if (!scopeSpan.TryGetProperty("spans", out var spanElements))
                {
                    continue;
                }

                foreach (var spanElement in spanElements.EnumerateArray())
                {
                    spans.Add(ParseSpan(spanElement, serviceName, scopeName, baseUrl));
                }
            }
        }

        return spans
            .GroupBy(span => span.TraceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildTrace(baseUrl, group.ToList()))
            .ToList();
    }

    private static TelemetrySpan ParseSpan(JsonElement span, string? serviceName, string? scopeName, string dashboardUrl)
    {
        var traceId = span.GetProperty("traceId").GetString() ?? string.Empty;
        var spanId = span.GetProperty("spanId").GetString() ?? string.Empty;
        var parentSpanId = span.TryGetProperty("parentSpanId", out var parent) ? parent.GetString() : null;
        var name = span.GetProperty("name").GetString() ?? string.Empty;
        var kind = span.TryGetProperty("kind", out var kindElement) ? SpanKindName(kindElement.GetInt32()) : "Unspecified";
        var startNs = ParseInt64(span.GetProperty("startTimeUnixNano").GetString());
        var endNs = ParseInt64(span.GetProperty("endTimeUnixNano").GetString());
        var attributes = ExtractAttributes(span);

        return new TelemetrySpan(
            traceId,
            spanId,
            parentSpanId,
            name,
            kind,
            serviceName,
            scopeName,
            Math.Round((endNs - startNs) / 1_000_000d, 2),
            DateTimeOffset.FromUnixTimeMilliseconds(startNs / 1_000_000),
            SpanHasError(attributes),
            attributes,
            $"{dashboardUrl}/traces/detail/{traceId}?spanId={spanId}");
    }

    private static TelemetryTrace BuildTrace(string dashboardUrl, List<TelemetrySpan> spans)
    {
        var root = spans.FirstOrDefault(span => span.ParentSpanId is null && span.Kind == "Server")
            ?? spans.FirstOrDefault(span => span.ParentSpanId is null)
            ?? spans.OrderBy(span => span.Timestamp).First();

        var startedAt = spans.Min(span => span.Timestamp);
        var durationMs = spans.Max(span => span.Timestamp.AddMilliseconds(span.DurationMs).ToUnixTimeMilliseconds())
            - startedAt.ToUnixTimeMilliseconds();

        return new TelemetryTrace(
            root.TraceId,
            root.Name,
            Math.Round((double)durationMs, 2),
            spans.Any(span => span.HasError),
            startedAt,
            $"{dashboardUrl}/traces/detail/{root.TraceId}",
            spans);
    }

    private static object ToTraceSummary(TelemetryTrace trace) => new
    {
        trace.TraceId,
        trace.Title,
        trace.DurationMs,
        trace.HasError,
        trace.Timestamp,
        trace.DashboardUrl,
        SpanCount = trace.Spans.Count,
        SlowestSpans = trace.Spans
            .OrderByDescending(span => span.DurationMs)
            .Take(5)
            .Select(span => new
            {
                span.SpanId,
                span.ParentSpanId,
                span.Name,
                span.Kind,
                span.DurationMs,
                span.ScopeName,
                span.DashboardUrl
            })
    };

    private static bool TraceMatches(TelemetryTrace trace, string search)
    {
        var comparison = StringComparison.OrdinalIgnoreCase;
        return trace.TraceId.Contains(search, comparison)
            || trace.Title.Contains(search, comparison)
            || trace.Spans.Any(span => span.Name.Contains(search, comparison)
                || span.SpanId.Contains(search, comparison)
                || span.Attributes.Any(attribute => attribute.Key.Contains(search, comparison) || attribute.Value.Contains(search, comparison)));
    }

    private static Dictionary<string, string> ExtractAttributes(JsonElement span)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!span.TryGetProperty("attributes", out var attributeElements))
        {
            return attributes;
        }

        foreach (var attribute in attributeElements.EnumerateArray())
        {
            var key = attribute.GetProperty("key").GetString();
            if (key is null || !attribute.TryGetProperty("value", out var value))
            {
                continue;
            }

            attributes[key] = ExtractAttributeValue(value);
        }

        return attributes;
    }

    private static string ExtractAttributeValue(JsonElement value)
    {
        foreach (var propertyName in new[] { "stringValue", "intValue", "doubleValue", "boolValue" })
        {
            if (value.TryGetProperty(propertyName, out var property))
            {
                return property.ToString();
            }
        }

        return value.GetRawText();
    }

    private static string? ExtractServiceName(JsonElement resourceSpan)
    {
        if (!resourceSpan.TryGetProperty("resource", out var resource)
            || !resource.TryGetProperty("attributes", out var attributes))
        {
            return null;
        }

        foreach (var attribute in attributes.EnumerateArray())
        {
            if (attribute.TryGetProperty("key", out var key)
                && key.GetString() == "service.name"
                && attribute.TryGetProperty("value", out var value))
            {
                return ExtractAttributeValue(value);
            }
        }

        return null;
    }

    private static bool SpanHasError(Dictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("http.response.status_code", out var statusCode)
            && int.TryParse(statusCode.Split(' ')[0], out var parsedStatus)
            && parsedStatus >= 400)
        {
            return true;
        }

        return attributes.ContainsKey("error.type")
            || attributes.ContainsKey("exception.type");
    }

    private static long ParseInt64(string? value) => long.TryParse(value, out var parsed) ? parsed : 0;

    private static string SpanKindName(int kind) => kind switch
    {
        1 => "Internal",
        2 => "Server",
        3 => "Client",
        4 => "Producer",
        5 => "Consumer",
        _ => "Unspecified"
    };

    private static string NormalizeDashboardUrl(string? dashboardUrl) =>
        (string.IsNullOrWhiteSpace(dashboardUrl) ? DefaultDashboardUrl : dashboardUrl).TrimEnd('/');

    private sealed record TelemetryTrace(
        string TraceId,
        string Title,
        double DurationMs,
        bool HasError,
        DateTimeOffset Timestamp,
        string DashboardUrl,
        List<TelemetrySpan> Spans);

    private sealed record TelemetrySpan(
        string TraceId,
        string SpanId,
        string? ParentSpanId,
        string Name,
        string Kind,
        string? ServiceName,
        string? ScopeName,
        double DurationMs,
        DateTimeOffset Timestamp,
        bool HasError,
        Dictionary<string, string> Attributes,
        string DashboardUrl);
}
