using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

public record IngestDocumentRequest(string Title, string Content);
public record AskKnowledgeBaseRequest(string Question, int TopK = 3);

// LLM re-ranker contract: the model only has to reason about relevance and hand back an order.
public record RerankResult(List<string> RankedIds);

// Shared shape returned by the retrieval+rerank pipeline, reused by both the plain and streaming /ask endpoints.
public record RetrievedChunk(DocumentChunk Chunk, double VectorScore, double KeywordScore, double FusedScore, int Position);

public static class RagEndpoints
{
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapRagEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/rag").WithTags("RAG Knowledge Base");

        // 1. Ingest: chunk the document, embed each chunk, and store it in the vector store.
        group.MapPost("/ingest", async (
            [FromBody] IngestDocumentRequest request,
            [FromServices] AppDbContext db,
            [FromServices] TextChunkingService chunker,
            [FromServices] IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            CancellationToken ct) =>
        {
            var chunks = chunker.ChunkText(request.Content);
            if (chunks.Count == 0)
            {
                return Results.BadRequest(new { Message = "No content to ingest." });
            }

            var embeddings = await embeddingGenerator.GenerateAsync(chunks, cancellationToken: ct);

            var documentChunks = chunks.Select((text, index) => new DocumentChunk
            {
                SourceTitle = request.Title,
                Content = text,
                ChunkIndex = index,
                Embedding = embeddings[index].Vector.ToArray()
            }).ToList();

            db.DocumentChunks.AddRange(documentChunks);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                request.Title,
                ChunksCreated = documentChunks.Count
            });
        });

        // 2. Ask: embed the question, retrieve the top-K most similar chunks, then let the LLM
        //    answer grounded strictly in that retrieved context (classic RAG).
        group.MapPost("/ask", async (
            [FromBody] AskKnowledgeBaseRequest request,
            [FromServices] AppDbContext db,
            [FromServices] IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            [FromServices] VectorMathService vectorMath,
            [FromServices] HybridSearchService hybridSearch,
            [FromServices] IChatClient chatClient,
            CancellationToken ct) =>
        {
            var allChunks = await db.DocumentChunks.AsNoTracking().ToListAsync(ct);
            if (allChunks.Count == 0)
            {
                return Results.BadRequest(new { Message = "Knowledge base is empty. Ingest a document first via /api/rag/ingest." });
            }

            var topK = request.TopK <= 0 ? 3 : request.TopK;
            var (finalChunks, rerankMethod) = await RetrieveAndRerankAsync(
                request.Question, topK, allChunks, embeddingGenerator, vectorMath, hybridSearch, chatClient, ct);

            var context = string.Join(
                "\n\n---\n\n",
                finalChunks.Select(x => $"[Source: {x.Chunk.SourceTitle}]\n{x.Chunk.Content}"));

            var prompt = $"""
                You are a helpful assistant answering questions using ONLY the provided context.
                If the answer isn't contained in the context, say you don't know.

                Context:
                {context}

                Question: {request.Question}
                """;

            var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);

            return Results.Ok(new
            {
                request.Question,
                Answer = response.Text,
                RerankMethod = rerankMethod,
                Sources = finalChunks.Select(x => new
                {
                    x.Chunk.SourceTitle,
                    x.Chunk.ChunkIndex,
                    VectorScore = Math.Round(x.VectorScore, 4),
                    KeywordScore = Math.Round(x.KeywordScore, 4),
                    FusedScore = Math.Round(x.FusedScore, 4),
                    RerankPosition = x.Position
                })
            });
        });

        // 3. Ask (streaming): identical retrieval pipeline, but the final answer is streamed to the
        //    client token-by-token over Server-Sent Events instead of waiting for the full response.
        group.MapPost("/ask-stream", async (
            HttpContext httpContext,
            [FromBody] AskKnowledgeBaseRequest request,
            [FromServices] AppDbContext db,
            [FromServices] IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            [FromServices] VectorMathService vectorMath,
            [FromServices] HybridSearchService hybridSearch,
            [FromServices] IChatClient chatClient,
            CancellationToken ct) =>
        {
            var allChunks = await db.DocumentChunks.AsNoTracking().ToListAsync(ct);
            if (allChunks.Count == 0)
            {
                return Results.BadRequest(new { Message = "Knowledge base is empty. Ingest a document first via /api/rag/ingest." });
            }

            var topK = request.TopK <= 0 ? 3 : request.TopK;
            var (finalChunks, rerankMethod) = await RetrieveAndRerankAsync(
                request.Question, topK, allChunks, embeddingGenerator, vectorMath, hybridSearch, chatClient, ct);

            // SSE: a plain HTTP response kept open, written to in small "event/data" frames as they become available.
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers["Cache-Control"] = "no-cache";
            httpContext.Response.Headers["X-Accel-Buffering"] = "no"; // disable reverse-proxy buffering (e.g. nginx)

            async Task WriteEventAsync(string eventName, object payload)
            {
                // Match the camelCase naming Results.Ok(...) uses elsewhere (ASP.NET Core's web JSON defaults),
                // since JsonSerializer.Serialize on its own defaults to PascalCase property names.
                var json = JsonSerializer.Serialize(payload, SseJsonOptions);
                await httpContext.Response.WriteAsync($"event: {eventName}\ndata: {json}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }

            // Send retrieval metadata first - the UI can render the sources table before any answer text exists.
            await WriteEventAsync("sources", new
            {
                RerankMethod = rerankMethod,
                Sources = finalChunks.Select(x => new
                {
                    x.Chunk.SourceTitle,
                    x.Chunk.ChunkIndex,
                    VectorScore = Math.Round(x.VectorScore, 4),
                    KeywordScore = Math.Round(x.KeywordScore, 4),
                    FusedScore = Math.Round(x.FusedScore, 4),
                    RerankPosition = x.Position
                })
            });

            var context = string.Join(
                "\n\n---\n\n",
                finalChunks.Select(x => $"[Source: {x.Chunk.SourceTitle}]\n{x.Chunk.Content}"));

            var prompt = $"""
                You are a helpful assistant answering questions using ONLY the provided context.
                If the answer isn't contained in the context, say you don't know.

                Context:
                {context}

                Question: {request.Question}
                """;

            await foreach (var update in chatClient.GetStreamingResponseAsync(prompt, cancellationToken: ct))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    await WriteEventAsync("token", new { Text = update.Text });
                }
            }

            await WriteEventAsync("done", new { });

            return Results.Empty;
        });

        return routes;
    }

    // Stages 1-3 of hybrid search: vector + BM25 retrieval, Reciprocal Rank Fusion, then LLM re-ranking
    // of the fused candidate pool down to the final TopK. Shared by both the plain and streaming /ask endpoints.
    private static async Task<(List<RetrievedChunk> FinalChunks, string RerankMethod)> RetrieveAndRerankAsync(
        string question,
        int topK,
        List<DocumentChunk> allChunks,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        VectorMathService vectorMath,
        HybridSearchService hybridSearch,
        IChatClient chatClient,
        CancellationToken ct)
    {
        // ── Stage 1: two independent retrievers ──────────────────────────
        var questionEmbedding = (await embeddingGenerator.GenerateAsync(question, cancellationToken: ct)).Vector.ToArray();
        var vectorScores = allChunks.Select(c => vectorMath.CalculateCosineSimilarity(questionEmbedding, c.Embedding)).ToList();
        var bm25Scores = hybridSearch.ScoreBm25(question, allChunks.Select(c => c.Content).ToList());

        var vectorRanking = Enumerable.Range(0, allChunks.Count).OrderByDescending(i => vectorScores[i]);
        var keywordRanking = Enumerable.Range(0, allChunks.Count).OrderByDescending(i => bm25Scores[i]);

        // ── Stage 2: fuse the two rankings by rank position (Reciprocal Rank Fusion) ──
        var fusedScores = HybridSearchService.ReciprocalRankFusion(60, vectorRanking, keywordRanking);
        var candidatePoolSize = Math.Min(allChunks.Count, Math.Max(topK * 3, 6));
        var candidates = fusedScores
            .OrderByDescending(kv => kv.Value)
            .Take(candidatePoolSize)
            .Select(kv => new RetrievedChunk(allChunks[kv.Key], vectorScores[kv.Key], bm25Scores[kv.Key], kv.Value, 0))
            .ToList();

        // ── Stage 3: LLM re-ranks the fused candidate pool down to the final TopK ──
        var rerankIds = candidates.Select((_, i) => $"C{i}").ToList();
        var rerankedOrder = Enumerable.Range(0, candidates.Count).ToList(); // fallback = fused order
        var rerankMethod = "fused-order-fallback";

        if (candidates.Count > topK)
        {
            var candidateBlock = string.Join("\n\n", candidates.Select((x, i) =>
                $"[{rerankIds[i]}] (Source: {x.Chunk.SourceTitle})\n{x.Chunk.Content}"));

            var rerankPrompt = $"""
                Rank the following passages by how relevant they are to answering the question,
                from MOST relevant to LEAST relevant. Return every passage id exactly once.

                Question: {question}

                Passages:
                {candidateBlock}
                """;

            var rerankOptions = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema<RerankResult>() };
            var rerankResponse = await chatClient.GetResponseAsync(rerankPrompt, rerankOptions, ct);

            try
            {
                var parsed = JsonSerializer.Deserialize<RerankResult>(
                    rerankResponse.Text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var llmOrder = parsed?.RankedIds?
                    .Select(id => rerankIds.IndexOf(id))
                    .Where(i => i >= 0)
                    .Distinct()
                    .ToList() ?? [];

                if (llmOrder.Count > 0)
                {
                    // Local models often omit a few candidates rather than ranking the full set.
                    // Trust what the LLM ranked, then append anything it missed in fused order.
                    var missing = Enumerable.Range(0, candidates.Count).Except(llmOrder);
                    rerankedOrder = llmOrder.Concat(missing).ToList();
                    rerankMethod = llmOrder.Count == candidates.Count ? "llm" : "llm-partial";
                }
            }
            catch (JsonException)
            {
                // keep the fused-order fallback
            }
        }

        var finalChunks = rerankedOrder
            .Take(topK)
            .Select((candidateIndex, position) => candidates[candidateIndex] with { Position = position + 1 })
            .ToList();

        return (finalChunks, rerankMethod);
    }
}
