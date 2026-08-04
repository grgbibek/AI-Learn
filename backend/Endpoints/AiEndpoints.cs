using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

public record SubtaskAnalysisResponse(
    int WorkItemId,
    string OriginalTitle,
    List<string> Subtasks,
    int EstimatedTotalHours,
    string ComplexityLevel // "Low" | "Medium" | "High"
);

public record CompareSemanticSimilarityRequest(string Text1, string Text2);

public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/ai").WithTags("AI Features");

        // 1. Unstructured Endpoint (Lesson 1)
        group.MapPost("/suggest-subtasks/{id:int}", async (
            int id, 
            [FromServices] AppDbContext db, 
            [FromServices] IChatClient chatClient,
            CancellationToken ct) =>
        {
            var item = await db.WorkItems.FindAsync(new object[] { id }, ct);
            if (item is null)
            {
                return Results.NotFound(new { Message = $"WorkItem {id} not found." });
            }

            var prompt = $"""
                You are an expert Agile Scrum Assistant.
                Break down the following work item title and description into 3 actionable subtasks:
                Title: {item.Title}
                Description: {item.Description}

                Format output as a clean bulleted list.
                """;

            var response = await chatClient.GetResponseAsync(prompt, cancellationToken: ct);

            return Results.Ok(new
            {
                WorkItemId = item.Id,
                OriginalTitle = item.Title,
                SuggestedSubtasks = response.Text
            });
        });

        // 2. Structured JSON Output Endpoint (Lesson 2 - Pattern A)
        group.MapPost("/structured-analysis/{id:int}", async (
            int id,
            [FromServices] AppDbContext db,
            [FromServices] IChatClient chatClient,
            CancellationToken ct) =>
        {
            var item = await db.WorkItems.FindAsync(new object[] { id }, ct);
            if (item is null)
            {
                return Results.NotFound(new { Message = $"WorkItem {id} not found." });
            }

            var prompt = $"""
                Analyze the following work item and return a structured analysis:
                Title: {item.Title}
                Description: {item.Description}
                Priority: {item.Priority}
                """;

            var options = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema<SubtaskAnalysisResponse>()
            };

            var response = await chatClient.GetResponseAsync(prompt, options, ct);

            try
            {
                var structuredResult = JsonSerializer.Deserialize<SubtaskAnalysisResponse>(
                    response.Text, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return Results.Ok(structuredResult);
            }
            catch (JsonException)
            {
                return Results.Ok(new SubtaskAnalysisResponse(
                    item.Id,
                    item.Title,
                    new List<string> { "Analyze requirements", "Implement endpoint", "Add unit tests" },
                    8,
                    "Medium"
                ));
            }
        });

        // 3. Native C# Function Calling / Tools Endpoint (Lesson 2 - Pattern B)
        group.MapPost("/workload-assistant", async (
            [FromBody] WorkloadQueryRequest request,
            [FromServices] AppDbContext db,
            [FromServices] IChatClient chatClient,
            CancellationToken ct) =>
        {
            [Description("Gets the list of work items filtered by priority (1=High, 2=Medium, 3=Low)")]
            async Task<List<string>> GetWorkItemsByPriority(int priority)
            {
                var items = await db.WorkItems
                    .Where(w => (int)w.Priority == priority)
                    .Select(w => w.Title)
                    .ToListAsync(ct);
                return items;
            }

            var priorityTool = AIFunctionFactory.Create(GetWorkItemsByPriority, "GetWorkItemsByPriority");

            var options = new ChatOptions
            {
                Tools = [priorityTool]
            };

            var response = await chatClient.GetResponseAsync(request.UserPrompt, options, ct);

            return Results.Ok(new
            {
                Prompt = request.UserPrompt,
                ToolRegistered = priorityTool.Name,
                Response = response.Text
            });
        });

        // 4. Semantic Similarity Search Endpoint (Phase 3 - Lesson 1)
        group.MapPost("/semantic-similarity", async (
            [FromBody] CompareSemanticSimilarityRequest request,
            [FromServices] IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            [FromServices] VectorMathService vectorMath,
            CancellationToken ct) =>
        {
            // Generate 128-dimensional embedding vectors for both input texts
            var embeddings = await embeddingGenerator.GenerateAsync([request.Text1, request.Text2], cancellationToken: ct);

            var vector1 = embeddings[0].Vector.Span;
            var vector2 = embeddings[1].Vector.Span;

            // Compute Cosine Similarity using SIMD TensorPrimitives in .NET 10
            float similarityScore = vectorMath.CalculateCosineSimilarity(vector1, vector2);

            string interpretation = similarityScore switch
            {
                >= 0.85f => "High Semantic Match (Identical domain concepts)",
                >= 0.50f => "Moderate Semantic Match (Related technical topic)",
                _ => "Low Semantic Match (Distinct concepts)"
            };

            return Results.Ok(new
            {
                Text1 = request.Text1,
                Text2 = request.Text2,
                CosineSimilarityScore = Math.Round(similarityScore, 4),
                Interpretation = interpretation,
                VectorDimensions = vector1.Length
            });
        });

        return routes;
    }
}

public record WorkloadQueryRequest(string UserPrompt);
