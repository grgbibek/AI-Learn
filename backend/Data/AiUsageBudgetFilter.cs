using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Data;

public sealed class AiUsageBudgetFilter(string capability) : IEndpointFilter
{
    private static readonly JsonSerializerOptions TokenEstimateJsonOptions = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 4
    };

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var options = httpContext.RequestServices.GetRequiredService<IOptions<AiUsageBudgetOptions>>().Value;

        if (!options.Enabled || httpContext.User.Identity?.IsAuthenticated != true)
        {
            return await next(context);
        }

        var db = httpContext.RequestServices.GetRequiredService<AppDbContext>();
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var userName = httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? httpContext.User.Identity.Name
            ?? "unknown";
        var role = httpContext.User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userBudget = await db.AppUsers
            .Where(user => user.UserName == userName && user.IsActive)
            .Select(user => new { user.DailyAiRequestLimit, user.DailyAiTokenLimit })
            .FirstOrDefaultAsync(httpContext.RequestAborted);
        var dailyRequestLimit = userBudget?.DailyAiRequestLimit
            ?? (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                ? options.AdminDailyRequestLimit
                : options.UserDailyRequestLimit);
        var dailyTokenLimit = userBudget?.DailyAiTokenLimit
            ?? (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                ? options.AdminDailyTokenLimit
                : options.UserDailyTokenLimit);
        var providerName = configuration["Ollama:ProviderName"] ?? "Ollama";
        var modelName = configuration["Ollama:ChatModel"] ?? "unknown";
        var usageRecorder = httpContext.RequestServices.GetRequiredService<AiUsageRecorder>();
        var estimatedInputTokens = EstimateInputTokens(context);
        var estimatedCostUsd = EstimateCost(estimatedInputTokens, options.EstimatedCostPerThousandTokensUsd);

        var startedAt = DateTime.UtcNow;
        var dayStart = startedAt.Date;
        var usedToday = await db.AiUsageLogs
            .CountAsync(log => log.UserName == userName
                && log.StartedAt >= dayStart
                && !log.BudgetWasExceeded,
                httpContext.RequestAborted);
        var usedTokensToday = await db.AiUsageLogs
            .Where(log => log.UserName == userName
                && log.StartedAt >= dayStart
                && !log.BudgetWasExceeded)
            .SumAsync(log => (int?)log.EstimatedTotalTokens, httpContext.RequestAborted) ?? 0;

        if (usedToday >= dailyRequestLimit)
        {
            await SaveUsageLogAsync(
                db,
                httpContext,
                usageRecorder,
                userName,
                role,
                providerName,
                modelName,
                startedAt,
                StatusCodes.Status429TooManyRequests,
                budgetWasExceeded: true,
                estimatedInputTokens,
                estimatedOutputTokens: 0,
                estimatedCostUsd);

            return Results.Json(new
            {
                Type = "https://httpstatuses.com/429",
                Title = "AI usage budget exceeded.",
                Status = StatusCodes.Status429TooManyRequests,
                Detail = $"The daily AI request budget of {dailyRequestLimit} has been exhausted for this user."
            }, statusCode: StatusCodes.Status429TooManyRequests, contentType: "application/problem+json");
        }

        if (usedTokensToday + estimatedInputTokens > dailyTokenLimit)
        {
            await SaveUsageLogAsync(
                db,
                httpContext,
                usageRecorder,
                userName,
                role,
                providerName,
                modelName,
                startedAt,
                StatusCodes.Status429TooManyRequests,
                budgetWasExceeded: true,
                estimatedInputTokens,
                estimatedOutputTokens: 0,
                estimatedCostUsd);

            return Results.Json(new
            {
                Type = "https://httpstatuses.com/429",
                Title = "AI token budget exceeded.",
                Status = StatusCodes.Status429TooManyRequests,
                Detail = $"The daily AI token budget of {dailyTokenLimit} has been exhausted for this user."
            }, statusCode: StatusCodes.Status429TooManyRequests, contentType: "application/problem+json");
        }

        var stopwatch = Stopwatch.StartNew();
        object? result;
        var statusCode = StatusCodes.Status200OK;

        try
        {
            result = await next(context);
            statusCode = result is IStatusCodeHttpResult statusCodeResult
                ? statusCodeResult.StatusCode ?? StatusCodes.Status200OK
                : StatusCodes.Status200OK;
        }
        catch
        {
            statusCode = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            await SaveUsageLogAsync(
                db,
                httpContext,
                usageRecorder,
                userName,
                role,
                providerName,
                modelName,
                startedAt,
                statusCode,
                budgetWasExceeded: false,
                estimatedInputTokens,
                estimatedOutputTokens: 0,
                estimatedCostUsd,
                durationMs: (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue));
        }

        return result;
    }

    private async Task SaveUsageLogAsync(
        AppDbContext db,
        HttpContext httpContext,
        AiUsageRecorder usageRecorder,
        string userName,
        string role,
        string providerName,
        string modelName,
        DateTime startedAt,
        int statusCode,
        bool budgetWasExceeded,
        int estimatedInputTokens,
        int estimatedOutputTokens,
        decimal estimatedCostUsd,
        int? durationMs = null)
    {
        var finishedAt = DateTime.UtcNow;
        var tokenUsageSource = usageRecorder.HasProviderReportedUsage ? "ProviderReported" : "Estimated";
        var loggedInputTokens = usageRecorder.InputTokens ?? estimatedInputTokens;
        var loggedOutputTokens = usageRecorder.OutputTokens ?? estimatedOutputTokens;
        var estimatedTotalTokens = usageRecorder.TotalTokens ?? (loggedInputTokens + loggedOutputTokens);
        var loggedModelName = usageRecorder.ModelName ?? modelName;
        db.AiUsageLogs.Add(new AiUsageLog
        {
            UserName = userName,
            Role = role,
            Capability = capability,
            Endpoint = httpContext.Request.Path.Value ?? string.Empty,
            HttpMethod = httpContext.Request.Method,
            ProviderName = providerName,
            ModelName = loggedModelName,
            StatusCode = statusCode,
            EstimatedInputTokens = loggedInputTokens,
            EstimatedOutputTokens = loggedOutputTokens,
            EstimatedTotalTokens = estimatedTotalTokens,
            TokenUsageSource = tokenUsageSource,
            EstimatedCostUsd = EstimateCost(estimatedTotalTokens, httpContext.RequestServices.GetRequiredService<IOptions<AiUsageBudgetOptions>>().Value.EstimatedCostPerThousandTokensUsd),
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            DurationMs = durationMs ?? (int)Math.Min((finishedAt - startedAt).TotalMilliseconds, int.MaxValue),
            BudgetWasExceeded = budgetWasExceeded
        });

        await db.SaveChangesAsync(httpContext.RequestAborted);
        httpContext.RequestServices.GetRequiredService<IMemoryCache>().Remove(AppCacheKeys.AnalyticsMetrics);
    }

    private static int EstimateInputTokens(EndpointFilterInvocationContext context)
    {
        var builder = new StringBuilder();
        builder.Append(context.HttpContext.Request.Method).Append(' ');
        builder.Append(context.HttpContext.Request.Path).Append(' ');
        builder.Append(context.HttpContext.Request.QueryString);

        foreach (var argument in context.Arguments)
        {
            if (argument is null) continue;
            var type = argument.GetType();
            if (argument is string text)
            {
                builder.Append(' ').Append(text);
            }
            else if (type.IsPrimitive || type.IsEnum || argument is decimal || argument is DateTime || argument is DateTimeOffset)
            {
                builder.Append(' ').Append(argument);
            }
            else if (type.Namespace?.StartsWith("TaskFlow.Api.Endpoints", StringComparison.Ordinal) == true
                || type.Namespace?.StartsWith("TaskFlow.Api.Models", StringComparison.Ordinal) == true)
            {
                try
                {
                    builder.Append(' ').Append(JsonSerializer.Serialize(argument, type, TokenEstimateJsonOptions));
                }
                catch
                {
                    // Token estimates are best-effort; never fail the request because metering could not serialize an argument.
                }
            }
        }

        return Math.Max(1, (int)Math.Ceiling(builder.Length / 4.0));
    }

    private static decimal EstimateCost(int estimatedTokens, decimal costPerThousandTokensUsd) =>
        Math.Round(estimatedTokens / 1000m * costPerThousandTokensUsd, 6);
}