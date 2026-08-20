using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.KernelMemory;
using TaskFlow.Api.Data;

namespace TaskFlow.Api.Endpoints;

public record AskKernelMemoryRequest(string Question);
public record KernelMemoryIngestFileResult(string Title, string DocumentId, bool FlaggedSuspicious, IReadOnlyList<string> SuspiciousPhrases, object Sanitization);

public static class KernelMemoryRagEndpoints
{
    private const long MaxMarkdownFileBytes = 2 * 1024 * 1024;

    public static IEndpointRouteBuilder MapKernelMemoryRagEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/rag/kernel-memory")
            .WithTags("RAG Knowledge Base (Kernel Memory)")
            .RequireAuthorization(AuthPolicies.CanUseRag);

        group.MapPost("/ingest", async (
            [FromBody] IngestDocumentRequest request,
            [FromServices] IKernelMemory memory,
            [FromServices] DataSanitizationService dataSanitizer,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return Results.BadRequest(new { Message = "No content to ingest." });
            }

            var result = await IngestKernelMemoryDocumentAsync(request.Title, request.Content, memory, dataSanitizer, ct);

            return Results.Ok(new
            {
                result.Title,
                result.DocumentId,
                Store = "Kernel Memory serverless in-memory store",
                result.FlaggedSuspicious,
                result.SuspiciousPhrases,
                result.Sanitization
            });
        })
        .RequireAuthorization(AuthPolicies.CanIngestKnowledge)
        .RequireRateLimiting(RateLimitPolicies.KnowledgeIngest)
        .AddEndpointFilter(new AiUsageBudgetFilter(RateLimitPolicies.KnowledgeIngest));

        group.MapPost("/ingest-files", async (
            HttpRequest request,
            [FromServices] IKernelMemory memory,
            [FromServices] DataSanitizationService dataSanitizer,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { Message = "Upload Markdown files as multipart/form-data." });
            }

            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count == 0)
            {
                return Results.BadRequest(new { Message = "Choose at least one Markdown file to ingest." });
            }

            var ingested = new List<KernelMemoryIngestFileResult>();
            var rejected = new List<object>();

            foreach (var file in form.Files)
            {
                if (!IsMarkdownFile(file.FileName))
                {
                    rejected.Add(new { file.FileName, Reason = "Only .md Markdown files are supported." });
                    continue;
                }

                if (file.Length <= 0)
                {
                    rejected.Add(new { file.FileName, Reason = "File is empty." });
                    continue;
                }

                if (file.Length > MaxMarkdownFileBytes)
                {
                    rejected.Add(new { file.FileName, Reason = $"File is larger than {MaxMarkdownFileBytes / 1024 / 1024} MB." });
                    continue;
                }

                await using var stream = file.OpenReadStream();
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync(ct);

                if (string.IsNullOrWhiteSpace(content))
                {
                    rejected.Add(new { file.FileName, Reason = "File has no readable Markdown content." });
                    continue;
                }

                ingested.Add(await IngestKernelMemoryDocumentAsync(
                    Path.GetFileName(file.FileName),
                    content,
                    memory,
                    dataSanitizer,
                    ct));
            }

            if (ingested.Count == 0)
            {
                return Results.BadRequest(new { Message = "No Markdown files were ingested.", Rejected = rejected });
            }

            return Results.Ok(new
            {
                FilesIngested = ingested.Count,
                Ingested = ingested,
                Rejected = rejected,
                Store = "Kernel Memory serverless in-memory store"
            });
        })
        .DisableAntiforgery()
        .RequireAuthorization(AuthPolicies.CanIngestKnowledge)
        .RequireRateLimiting(RateLimitPolicies.KnowledgeIngest)
        .AddEndpointFilter(new AiUsageBudgetFilter(RateLimitPolicies.KnowledgeIngest));

        group.MapPost("/ask", async (
            [FromBody] AskKernelMemoryRequest request,
            [FromServices] IKernelMemory memory,
            [FromServices] DataSanitizationService dataSanitizer,
            CancellationToken ct) =>
        {
            var questionSanitization = dataSanitizer.Sanitize(request.Question);
            var answer = await memory.AskAsync(questionSanitization.SanitizedText, cancellationToken: ct);
            var answerSanitization = dataSanitizer.Sanitize(answer.Result);

            var sources = answer.RelevantSources.Select(source => new
            {
                SourceName = dataSanitizer.Sanitize(source.SourceName),
                Partitions = source.Partitions.Select(partition => new
                {
                    Text = dataSanitizer.Sanitize(partition.Text),
                    partition.Relevance
                }).ToList()
            }).ToList();

            return Results.Ok(new
            {
                Question = questionSanitization.SanitizedText,
                Answer = answerSanitization.SanitizedText,
                Store = "Kernel Memory serverless in-memory store",
                Sanitization = BuildSanitizationSummary(
                    [questionSanitization, answerSanitization, .. sources.SelectMany(source => new[] { source.SourceName }.Concat(source.Partitions.Select(partition => partition.Text))) ]),
                Sources = sources.Select(source => new
                {
                    SourceName = source.SourceName.SanitizedText,
                    Partitions = source.Partitions.Select(partition => new
                    {
                        partition.Text.SanitizedText,
                        Relevance = Math.Round(partition.Relevance, 4)
                    })
                })
            });
        }).RequireRateLimiting(RateLimitPolicies.AiChat)
        .AddEndpointFilter(new AiUsageBudgetFilter(RateLimitPolicies.AiChat));

        return routes;
    }

    private static string CreateDocumentId(string title)
    {
        var safeTitle = new string(title
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');

        return $"km-{(string.IsNullOrWhiteSpace(safeTitle) ? "document" : safeTitle)}-{Guid.NewGuid():N}";
    }

    private static bool IsMarkdownFile(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".md", StringComparison.OrdinalIgnoreCase);

    internal static async Task<KernelMemoryIngestFileResult> IngestKernelMemoryDocumentAsync(
        string title,
        string content,
        IKernelMemory memory,
        DataSanitizationService dataSanitizer,
        CancellationToken ct)
    {
        var titleSanitization = dataSanitizer.Sanitize(title);
        var contentSanitization = dataSanitizer.Sanitize(content);
        if (string.IsNullOrWhiteSpace(contentSanitization.SanitizedText))
        {
            throw new InvalidOperationException("No content to ingest.");
        }

        var suspiciousPhrases = PromptGuard.ScanForInjectionAttempt(content);
        var documentId = await memory.ImportTextAsync(
            $"Title: {titleSanitization.SanitizedText}\n\n{contentSanitization.SanitizedText}",
            documentId: CreateDocumentId(titleSanitization.SanitizedText),
            cancellationToken: ct);

        return new KernelMemoryIngestFileResult(
            titleSanitization.SanitizedText,
            documentId,
            suspiciousPhrases.Count > 0,
            suspiciousPhrases,
            BuildSanitizationSummary(titleSanitization, contentSanitization));
    }

    private static object BuildSanitizationSummary(params SanitizationResult[] results)
    {
        var detectedTypes = results
            .SelectMany(result => result.DetectedTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new
        {
            WasSanitized = results.Any(result => result.WasSanitized),
            DetectedTypes = detectedTypes
        };
    }
}