using Microsoft.AspNetCore.Mvc;
using Microsoft.KernelMemory;
using TaskFlow.Api.Data;

namespace TaskFlow.Api.Endpoints;

public record AskKernelMemoryRequest(string Question);

public static class KernelMemoryRagEndpoints
{
    public static IEndpointRouteBuilder MapKernelMemoryRagEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/rag/kernel-memory").WithTags("RAG Knowledge Base (Kernel Memory)");

        group.MapPost("/ingest", async (
            [FromBody] IngestDocumentRequest request,
            [FromServices] IKernelMemory memory,
            [FromServices] DataSanitizationService dataSanitizer,
            CancellationToken ct) =>
        {
            var titleSanitization = dataSanitizer.Sanitize(request.Title);
            var contentSanitization = dataSanitizer.Sanitize(request.Content);
            if (string.IsNullOrWhiteSpace(contentSanitization.SanitizedText))
            {
                return Results.BadRequest(new { Message = "No content to ingest." });
            }

            var suspiciousPhrases = PromptGuard.ScanForInjectionAttempt(request.Content);
            var documentId = await memory.ImportTextAsync(
                $"Title: {titleSanitization.SanitizedText}\n\n{contentSanitization.SanitizedText}",
                documentId: CreateDocumentId(titleSanitization.SanitizedText),
                cancellationToken: ct);

            return Results.Ok(new
            {
                Title = titleSanitization.SanitizedText,
                DocumentId = documentId,
                Store = "Kernel Memory serverless in-memory store",
                FlaggedSuspicious = suspiciousPhrases.Count > 0,
                SuspiciousPhrases = suspiciousPhrases,
                Sanitization = BuildSanitizationSummary(titleSanitization, contentSanitization)
            });
        });

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
        });

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