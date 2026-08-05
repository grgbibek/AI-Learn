using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

public record IngestDocumentRequest(string Title, string Content);
public record AskKnowledgeBaseRequest(string Question, int TopK = 3);

public static class RagEndpoints
{
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
            [FromServices] IChatClient chatClient,
            CancellationToken ct) =>
        {
            var allChunks = await db.DocumentChunks.AsNoTracking().ToListAsync(ct);
            if (allChunks.Count == 0)
            {
                return Results.BadRequest(new { Message = "Knowledge base is empty. Ingest a document first via /api/rag/ingest." });
            }

            var questionEmbedding = (await embeddingGenerator.GenerateAsync(request.Question, cancellationToken: ct)).Vector.ToArray();

            var topK = request.TopK <= 0 ? 3 : request.TopK;
            var topChunks = allChunks
                .Select(chunk => new
                {
                    Chunk = chunk,
                    Score = vectorMath.CalculateCosineSimilarity(questionEmbedding, chunk.Embedding)
                })
                .OrderByDescending(x => x.Score)
                .Take(topK)
                .ToList();

            var context = string.Join(
                "\n\n---\n\n",
                topChunks.Select(x => $"[Source: {x.Chunk.SourceTitle}]\n{x.Chunk.Content}"));

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
                Sources = topChunks.Select(x => new
                {
                    x.Chunk.SourceTitle,
                    x.Chunk.ChunkIndex,
                    SimilarityScore = Math.Round(x.Score, 4)
                })
            });
        });

        return routes;
    }
}
