using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

// Strongly-typed DTO for Structured AI Output
public record SubtaskAnalysisResponse(
    int WorkItemId,
    string OriginalTitle,
    List<string> Subtasks,
    int EstimatedTotalHours,
    string ComplexityLevel // "Low" | "Medium" | "High"
);

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

            // Instruct LLM to format response strictly as JSON matching SubtaskAnalysisResponse schema
            var options = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema<SubtaskAnalysisResponse>()
            };

            var response = await chatClient.GetResponseAsync(prompt, options, ct);

            // Deserialize strongly-typed C# record directly from JSON output
            try
            {
                var structuredResult = JsonSerializer.Deserialize<SubtaskAnalysisResponse>(
                    response.Text, 
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return Results.Ok(structuredResult);
            }
            catch (JsonException)
            {
                // Fallback structured record if parsing raw string
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
            // Define C# function as an AI Tool using AIFunctionFactory
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

        return routes;
    }
}

public record WorkloadQueryRequest(string UserPrompt);
