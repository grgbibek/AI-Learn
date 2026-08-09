using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics");

        group.MapGet("/metrics", async (
            AppDbContext db,
            IMemoryCache cache,
            CancellationToken ct) =>
        {
            var response = await cache.GetOrCreateAsync(AppCacheKeys.AnalyticsMetrics, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);

                var totalTasks = await db.WorkItems.CountAsync(ct);
                var todoCount = await db.WorkItems.CountAsync(w => w.Status == WorkItemStatus.Todo, ct);
                var inProgressCount = await db.WorkItems.CountAsync(w => w.Status == WorkItemStatus.InProgress, ct);
                var doneCount = await db.WorkItems.CountAsync(w => w.Status == WorkItemStatus.Done, ct);

                var lowPriorityCount = await db.WorkItems.CountAsync(w => w.Priority == WorkItemPriority.Low, ct);
                var medPriorityCount = await db.WorkItems.CountAsync(w => w.Priority == WorkItemPriority.Medium, ct);
                var highPriorityCount = await db.WorkItems.CountAsync(w => w.Priority == WorkItemPriority.High, ct);
                var criticalPriorityCount = await db.WorkItems.CountAsync(w => w.Priority == WorkItemPriority.Critical, ct);

                var totalAgentRuns = await db.AgentAuditLogs.CountAsync(ct);
                var approvedAgentRuns = await db.AgentAuditLogs.CountAsync(a => a.Approved, ct);
                var rejectedAgentRuns = totalAgentRuns - approvedAgentRuns;
                var approvalRate = totalAgentRuns > 0 ? (double)approvedAgentRuns / totalAgentRuns * 100 : 100.0;

                var totalChunks = await db.DocumentChunks.CountAsync(ct);
                var totalDocuments = await db.DocumentChunks
                    .Select(c => c.SourceTitle)
                    .Distinct()
                    .CountAsync(ct);

                var completionRate = totalTasks > 0 ? (double)doneCount / totalTasks * 100 : 0.0;

                return new AnalyticsMetricsResponse(
                    TotalWorkItems: totalTasks,
                    CompletedWorkItems: doneCount,
                    PendingWorkItems: todoCount + inProgressCount,
                    CompletionRate: Math.Round(completionRate, 1),

                    StatusDistribution: new StatusDistribution(
                        Todo: todoCount,
                        InProgress: inProgressCount,
                        Done: doneCount
                    ),

                    PriorityDistribution: new PriorityDistribution(
                        Low: lowPriorityCount,
                        Medium: medPriorityCount,
                        High: highPriorityCount,
                        Critical: criticalPriorityCount
                    ),

                    AgentMetrics: new AgentPipelineMetrics(
                        TotalRuns: totalAgentRuns,
                        ApprovedRuns: approvedAgentRuns,
                        RejectedRuns: rejectedAgentRuns,
                        ApprovalRate: Math.Round(approvalRate, 1)
                    ),

                    KnowledgeBaseMetrics: new KnowledgeBaseMetrics(
                        TotalDocuments: totalDocuments,
                        TotalChunks: totalChunks
                    )
                );
            });

            return Results.Ok(response);
        })
        .WithName("GetAnalyticsMetrics");
    }
}

public record StatusDistribution(int Todo, int InProgress, int Done);
public record PriorityDistribution(int Low, int Medium, int High, int Critical);
public record AgentPipelineMetrics(int TotalRuns, int ApprovedRuns, int RejectedRuns, double ApprovalRate);
public record KnowledgeBaseMetrics(int TotalDocuments, int TotalChunks);

public record AnalyticsMetricsResponse(
    int TotalWorkItems,
    int CompletedWorkItems,
    int PendingWorkItems,
    double CompletionRate,
    StatusDistribution StatusDistribution,
    PriorityDistribution PriorityDistribution,
    AgentPipelineMetrics AgentMetrics,
    KnowledgeBaseMetrics KnowledgeBaseMetrics
);
