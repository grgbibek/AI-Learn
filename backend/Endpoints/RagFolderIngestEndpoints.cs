using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.KernelMemory;
using Qdrant.Client;
using TaskFlow.Api.Data;

namespace TaskFlow.Api.Endpoints;

public record IngestFolderRequest(string FolderPath, string? ProjectName);
public record IngestedFolderFileResult(string RelativePath, int ChunksCreated);
public record SkippedFolderFileResult(string RelativePath, string Reason);
public record IngestFolderResponse(
    string FolderPath,
    string? ProjectName,
    int FilesScanned,
    int FilesIngested,
    int ChunksCreated,
    IReadOnlyList<IngestedFolderFileResult> Ingested,
    IReadOnlyList<SkippedFolderFileResult> Skipped,
    double ElapsedSeconds,
    string? Store = null);

// Indexes a whole project folder in one call instead of pasting/uploading files one at a time -
// walks the tree, skips build/dependency noise, and reuses the same per-document ingest logic
// as the manual ingest endpoints so both paths stay consistent.
public static class RagFolderIngestEndpoints
{
    private static readonly HashSet<string> TargetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".cs", ".ts", ".html", ".css", ".json", ".sql",
        ".cshtml", ".razor", ".yml", ".yaml", ".config"
    };

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".angular", "packages",
        "dist", "TestResults", "testPackages", "coverage", ".idea"
    };

    public static IEndpointRouteBuilder MapRagFolderIngestEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/rag")
            .WithTags("RAG Knowledge Base (Folder Ingestion)")
            .RequireAuthorization(AuthPolicies.CanUseRag);

        group.MapPost("/ingest-folder", async (
            [FromBody] IngestFolderRequest request,
            [FromServices] AppDbContext db,
            [FromServices] TextChunkingService chunker,
            [FromServices] DataSanitizationService dataSanitizer,
            [FromServices] IMemoryCache cache,
            [FromServices] IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            [FromServices] QdrantClient qdrant,
            [FromServices] IOptions<RagFolderIngestionOptions> folderOptions,
            CancellationToken ct) =>
        {
            if (!TryValidateFolder(request.FolderPath, folderOptions.Value, out var rootPath, out var errorResult))
            {
                return errorResult!;
            }

            var skipped = new List<SkippedFolderFileResult>();
            var files = CollectFiles(rootPath, folderOptions.Value, skipped);
            var ingested = new List<IngestedFolderFileResult>();
            var stopwatch = Stopwatch.StartNew();

            foreach (var (fullPath, relativePath) in files)
            {
                var content = await TryReadFileAsync(fullPath, relativePath, skipped, ct);
                if (content is null)
                {
                    continue;
                }

                try
                {
                    var title = BuildTitle(request.ProjectName, relativePath);
                    var result = await RagEndpoints.IngestSqlDocumentAsync(
                        title, content, db, chunker, dataSanitizer, cache, embeddingGenerator, qdrant, ct);
                    ingested.Add(new IngestedFolderFileResult(relativePath, result.ChunksCreated));
                }
                catch (InvalidOperationException ex)
                {
                    skipped.Add(new SkippedFolderFileResult(relativePath, ex.Message));
                }
            }

            return Results.Ok(new IngestFolderResponse(
                rootPath,
                request.ProjectName,
                files.Count,
                ingested.Count,
                ingested.Sum(f => f.ChunksCreated),
                ingested,
                skipped,
                Math.Round(stopwatch.Elapsed.TotalSeconds, 2)));
        })
        .RequireAuthorization(AuthPolicies.CanIngestKnowledge)
        .RequireRateLimiting(RateLimitPolicies.KnowledgeIngest)
        .AddEndpointFilter(new AiUsageBudgetFilter(RateLimitPolicies.KnowledgeIngest));

        group.MapPost("/kernel-memory/ingest-folder", async (
            [FromBody] IngestFolderRequest request,
            [FromServices] IKernelMemory memory,
            [FromServices] DataSanitizationService dataSanitizer,
            [FromServices] IOptions<RagFolderIngestionOptions> folderOptions,
            CancellationToken ct) =>
        {
            if (!TryValidateFolder(request.FolderPath, folderOptions.Value, out var rootPath, out var errorResult))
            {
                return errorResult!;
            }

            var skipped = new List<SkippedFolderFileResult>();
            var files = CollectFiles(rootPath, folderOptions.Value, skipped);
            var ingested = new List<IngestedFolderFileResult>();
            var stopwatch = Stopwatch.StartNew();

            foreach (var (fullPath, relativePath) in files)
            {
                var content = await TryReadFileAsync(fullPath, relativePath, skipped, ct);
                if (content is null)
                {
                    continue;
                }

                try
                {
                    var title = BuildTitle(request.ProjectName, relativePath);
                    await KernelMemoryRagEndpoints.IngestKernelMemoryDocumentAsync(
                        title, content, memory, dataSanitizer, ct);
                    ingested.Add(new IngestedFolderFileResult(relativePath, 1));
                }
                catch (InvalidOperationException ex)
                {
                    skipped.Add(new SkippedFolderFileResult(relativePath, ex.Message));
                }
            }

            return Results.Ok(new IngestFolderResponse(
                rootPath,
                request.ProjectName,
                files.Count,
                ingested.Count,
                ingested.Count,
                ingested,
                skipped,
                Math.Round(stopwatch.Elapsed.TotalSeconds, 2),
                Store: "Kernel Memory serverless in-memory store"));
        })
        .RequireAuthorization(AuthPolicies.CanIngestKnowledge)
        .RequireRateLimiting(RateLimitPolicies.KnowledgeIngest)
        .AddEndpointFilter(new AiUsageBudgetFilter(RateLimitPolicies.KnowledgeIngest));

        return routes;
    }

    private static string BuildTitle(string? projectName, string relativePath) =>
        string.IsNullOrWhiteSpace(projectName) ? relativePath : $"{projectName}/{relativePath}";

    private static bool TryValidateFolder(
        string folderPath,
        RagFolderIngestionOptions options,
        out string normalizedPath,
        out IResult? errorResult)
    {
        normalizedPath = string.Empty;
        errorResult = null;

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            errorResult = Results.BadRequest(new { Message = "FolderPath is required." });
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(folderPath);
        }
        catch (Exception)
        {
            errorResult = Results.BadRequest(new { Message = "FolderPath is not a valid path." });
            return false;
        }

        if (!Directory.Exists(fullPath))
        {
            errorResult = Results.BadRequest(new { Message = $"Folder not found: {fullPath}" });
            return false;
        }

        if (options.AllowedRoots.Count == 0)
        {
            errorResult = Results.BadRequest(new
            {
                Message = "Folder ingestion is disabled. Configure RagFolderIngestion:AllowedRoots in appsettings to allow specific project folders."
            });
            return false;
        }

        var isUnderAllowedRoot = options.AllowedRoots.Any(root =>
        {
            string normalizedRoot;
            try
            {
                normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return false;
            }

            return fullPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });

        if (!isUnderAllowedRoot)
        {
            errorResult = Results.BadRequest(new
            {
                Message = "FolderPath is outside the configured allowed roots for folder ingestion."
            });
            return false;
        }

        normalizedPath = fullPath;
        return true;
    }

    // Walks the tree manually (rather than Directory.EnumerateFiles with recursion) so excluded
    // directories like node_modules/bin/obj are skipped entirely instead of just filtered after the fact.
    private static List<(string FullPath, string RelativePath)> CollectFiles(
        string rootPath,
        RagFolderIngestionOptions options,
        List<SkippedFolderFileResult> skipped)
    {
        var included = new List<(string, string)>();
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0 && included.Count < options.MaxFilesPerRequest)
        {
            var currentDir = pending.Pop();
            IEnumerable<string> subDirectories;
            IEnumerable<string> filesInDir;

            try
            {
                subDirectories = Directory.EnumerateDirectories(currentDir);
                filesInDir = Directory.EnumerateFiles(currentDir);
            }
            catch (Exception) when (currentDir != rootPath)
            {
                continue; // skip directories we can't read (permissions, junctions, etc.)
            }

            foreach (var dir in subDirectories)
            {
                if (!ExcludedDirectoryNames.Contains(Path.GetFileName(dir)))
                {
                    pending.Push(dir);
                }
            }

            foreach (var file in filesInDir)
            {
                if (included.Count >= options.MaxFilesPerRequest)
                {
                    break;
                }

                if (!TargetExtensions.Contains(Path.GetExtension(file)))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(rootPath, file);
                var length = new FileInfo(file).Length;

                if (length <= 0)
                {
                    skipped.Add(new SkippedFolderFileResult(relativePath, "File is empty."));
                    continue;
                }

                if (length > options.MaxFileSizeBytes)
                {
                    skipped.Add(new SkippedFolderFileResult(relativePath, $"File exceeds {options.MaxFileSizeBytes / 1024} KB limit."));
                    continue;
                }

                included.Add((file, relativePath));
            }
        }

        return included;
    }

    private static async Task<string?> TryReadFileAsync(
        string fullPath,
        string relativePath,
        List<SkippedFolderFileResult> skipped,
        CancellationToken ct)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(fullPath, ct);
        }
        catch (Exception ex)
        {
            skipped.Add(new SkippedFolderFileResult(relativePath, $"Could not read file: {ex.Message}"));
            return null;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            skipped.Add(new SkippedFolderFileResult(relativePath, "File has no readable content."));
            return null;
        }

        return content;
    }
}
