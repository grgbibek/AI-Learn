using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

public record PlanFeatureRequest(string FeatureRequest);

public record PlannerOutput(List<string> Subtasks);

public record DeveloperOutput(string Subtask, string TechnicalApproach);

public record ReviewerOutput(string Subtask, bool Approved, string Feedback);

public record AgentPipelineResult(string Subtask, string TechnicalApproach, bool Approved, string Feedback);

public static class AgentEndpoints
{
    private static readonly JsonSerializerOptions AgentJsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Caps how many Developer<->Reviewer rounds run per subtask before accepting whatever the
    // last attempt produced - prevents an endless loop if the two agents never agree.
    private const int MaxAttempts = 2;

    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/agents").WithTags("Multi-Agent Orchestration");

        // Step 1 of 3: Planner Agent - breaks a feature request into concrete subtasks.
        group.MapPost("/plan-feature", async (
            [FromBody] PlanFeatureRequest request,
            [FromServices] AppDbContext db,
            [FromServices] IChatClient chatClient,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("AgentEndpoints");
            var plan = await RunPlannerAgent(request.FeatureRequest, chatClient, ct);

            // Steps 2+3 run sequentially per subtask (not Task.WhenAll) - EF Core's DbContext
            // isn't thread-safe, and each subtask now writes audit rows as it goes.
            var results = new List<AgentPipelineResult>();
            foreach (var subtask in plan.Subtasks)
            {
                var result = await RunDeveloperReviewLoop(request.FeatureRequest, subtask, chatClient, db, logger, ct);
                results.Add(result);
            }

            return Results.Ok(new { request.FeatureRequest, Results = results });
        });

        // Read-only view of the audit trail - browse past pipeline runs, including rejected
        // revision attempts, most recent first. Optional ?take= caps how many rows come back.
        group.MapGet("/audit-log", async (
            [FromServices] AppDbContext db,
            int take,
            CancellationToken ct) =>
        {
            var logs = await db.AgentAuditLogs
                .OrderByDescending(l => l.CreatedAt)
                .ThenByDescending(l => l.Id)
                .Take(take <= 0 ? 50 : take)
                .ToListAsync(ct);

            return Results.Ok(logs);
        });

        return routes;
    }

    // Runs the Developer -> Reviewer handoff for one subtask, retrying with the Reviewer's
    // feedback if rejected, up to MaxAttempts. Every attempt (approved or not) is persisted
    // to AgentAuditLog for later inspection.
    private static async Task<AgentPipelineResult> RunDeveloperReviewLoop(
        string featureRequest,
        string subtask,
        IChatClient chatClient,
        AppDbContext db,
        ILogger logger,
        CancellationToken ct)
    {
        string? previousFeedback = null;
        DeveloperOutput developerOutput;
        ReviewerOutput reviewerOutput;

        var attempt = 1;
        while (true)
        {
            developerOutput = await RunDeveloperAgent(subtask, previousFeedback, chatClient, ct);
            reviewerOutput = await RunReviewerAgent(subtask, developerOutput.TechnicalApproach, chatClient, ct);

            logger.LogInformation(
                "Agent pipeline attempt {Attempt}/{MaxAttempts} for subtask '{Subtask}': Approved={Approved}",
                attempt, MaxAttempts, subtask, reviewerOutput.Approved);

            db.AgentAuditLogs.Add(new AgentAuditLog
            {
                FeatureRequest = featureRequest,
                Subtask = subtask,
                AttemptNumber = attempt,
                TechnicalApproach = developerOutput.TechnicalApproach,
                Approved = reviewerOutput.Approved,
                Feedback = reviewerOutput.Feedback
            });

            if (reviewerOutput.Approved || attempt >= MaxAttempts)
            {
                break;
            }

            previousFeedback = reviewerOutput.Feedback;
            attempt++;
        }

        await db.SaveChangesAsync(ct);

        return new AgentPipelineResult(subtask, developerOutput.TechnicalApproach, reviewerOutput.Approved, reviewerOutput.Feedback);
    }

    private static async Task<PlannerOutput> RunPlannerAgent(string featureRequest, IChatClient chatClient, CancellationToken ct)
    {
        var prompt = $"""
            You are a technical planning agent. Break the following feature request into
            3-5 concrete, independently implementable subtasks. Do not write code or
            implementation details - just the subtask breakdown.

            Feature request: {featureRequest}
            """;

        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema<PlannerOutput>() };
        var response = await chatClient.GetResponseAsync(prompt, options, ct);

        try
        {
            return JsonSerializer.Deserialize<PlannerOutput>(response.Text, AgentJsonOptions) ?? new PlannerOutput([]);
        }
        catch (JsonException)
        {
            return new PlannerOutput([]);
        }
    }

    private static async Task<DeveloperOutput> RunDeveloperAgent(string subtask, string? previousFeedback, IChatClient chatClient, CancellationToken ct)
    {
        var revisionNote = previousFeedback is null
            ? ""
            : $"\n\nA previous proposal was rejected by review with this feedback - revise your approach to address it:\n{previousFeedback}";

        var prompt = $"""
            You are a senior developer agent. Propose a short, concrete technical approach
            (2-4 sentences) for implementing the following subtask. Be specific about
            technologies/patterns where relevant (e.g. Angular Signals, EF Core).

            Subtask: {subtask}{revisionNote}
            """;

        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema<DeveloperOutput>() };
        var response = await chatClient.GetResponseAsync(prompt, options, ct);

        try
        {
            var parsed = JsonSerializer.Deserialize<DeveloperOutput>(response.Text, AgentJsonOptions);
            // Never trust the model to echo the subtask back correctly - use the real input.
            return parsed is null ? new DeveloperOutput(subtask, "(no approach generated)") : parsed with { Subtask = subtask };
        }
        catch (JsonException)
        {
            return new DeveloperOutput(subtask, "(failed to parse developer agent response)");
        }
    }

    private static async Task<ReviewerOutput> RunReviewerAgent(string subtask, string technicalApproach, IChatClient chatClient, CancellationToken ct)
    {
        var prompt = $"""
            You are a senior code reviewer agent. Critique the following proposed technical
            approach for a subtask. This project uses .NET 10 Minimal APIs and Angular 19
            with Signals/Standalone Components - flag anything inconsistent with that stack
            (e.g. React, RxJS BehaviorSubjects, NgModules). Set Approved to true only if the
            approach is sound and stack-appropriate; otherwise explain what's wrong in Feedback.

            Subtask: {subtask}
            Proposed approach: {technicalApproach}
            """;

        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema<ReviewerOutput>() };
        var response = await chatClient.GetResponseAsync(prompt, options, ct);

        try
        {
            var parsed = JsonSerializer.Deserialize<ReviewerOutput>(response.Text, AgentJsonOptions);
            return parsed is null ? new ReviewerOutput(subtask, false, "(no review generated)") : parsed with { Subtask = subtask };
        }
        catch (JsonException)
        {
            return new ReviewerOutput(subtask, false, "(failed to parse reviewer agent response)");
        }
    }
}
