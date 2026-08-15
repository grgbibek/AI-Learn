using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
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

// Shape requested from the LLM. WorkItemId/OriginalTitle are deliberately excluded -
// we already know them, so we never let the model guess/hallucinate them.
public record AiSubtaskAnalysis(
    [property: Description("Exactly 3 concrete, actionable subtasks")] List<string> Subtasks,
    int EstimatedTotalHours,
    [property: Description("Must be exactly one of: Low, Medium, High")] string ComplexityLevel
);

public record CompareSemanticSimilarityRequest(string Text1, string Text2);

public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/ai")
            .WithTags("AI Features")
            .RequireAuthorization(AuthPolicies.CanUseAi)
            .RequireRateLimiting(RateLimitPolicies.AiChat)
            .AddEndpointFilter(new AiUsageBudgetFilter(RateLimitPolicies.AiChat));

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
                Analyze the following work item and break it down into exactly 3 actionable subtasks:
                Title: {item.Title}
                Description: {item.Description}
                Priority: {item.Priority}
                """;

            var options = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema<AiSubtaskAnalysis>()
            };

            var response = await chatClient.GetResponseAsync(prompt, options, ct);

            try
            {
                var aiResult = JsonSerializer.Deserialize<AiSubtaskAnalysis>(
                    response.Text, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (aiResult is null)
                {
                    throw new JsonException("Deserialized result was null.");
                }

                // WorkItemId/OriginalTitle always come from the DB, never from the model.
                return Results.Ok(new SubtaskAnalysisResponse(
                    item.Id,
                    item.Title,
                    aiResult.Subtasks,
                    aiResult.EstimatedTotalHours,
                    aiResult.ComplexityLevel
                ));
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

        // 3b. Same tool-calling task, rebuilt with Semantic Kernel's Kernel + Plugin +
        // FunctionChoiceBehavior.Auto() instead of a hand-built IChatClient + ChatOptions.Tools.
        // Direct comparison target for endpoint #3 above (Phase 3 gap: Semantic Kernel).
        group.MapPost("/workload-assistant-sk", async (
            [FromBody] WorkloadQueryRequest request,
            [FromServices] AppDbContext db,
            [FromServices] IConfiguration config,
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

            var ollamaUri = new Uri(config["Ollama:BaseUrl"] ?? "http://localhost:11434");
            var chatModel = config["Ollama:ChatModel"] ?? "llama3.2";

            // Ollama chat completion for SK is still experimental (SKEXP0070) as of SK 1.79.
            // (Under the hood this connector wraps OllamaSharp as an IChatClient and reuses
            // Microsoft.Extensions.AI's own FunctionInvokingChatClient to run the tool-call loop.)
#pragma warning disable SKEXP0070
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOllamaChatCompletion(modelId: chatModel, endpoint: ollamaUri);
#pragma warning restore SKEXP0070

            var kernel = kernelBuilder.Build();

            // A "Plugin" is SK's reusable grouping of one or more callable functions -
            // the same native C# method as endpoint #3, just registered SK's way.
            var priorityFunction = KernelFunctionFactory.CreateFromMethod(
                GetWorkItemsByPriority, functionName: "GetWorkItemsByPriority");
            kernel.Plugins.AddFromFunctions("WorkItems", [priorityFunction]);

            var executionSettings = new OllamaPromptExecutionSettings
            {
                // Auto = the LLM decides whether/which plugin function(s) to invoke, then SK
                // executes them and feeds results back in, looping until a final answer -
                // this is what replaced SK's older Planner classes (now obsolete).
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

            var result = await kernel.InvokePromptAsync(
                request.UserPrompt, new KernelArguments(executionSettings), cancellationToken: ct);

            return Results.Ok(new
            {
                Prompt = request.UserPrompt,
                PluginRegistered = "WorkItems.GetWorkItemsByPriority",
                Response = result.ToString()
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
