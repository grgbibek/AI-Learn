using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics");

        group.MapGet("/metrics", async (AppDbContext db) =>
        {
            var workItems = await db.WorkItems.ToListAsync();
            var auditLogs = await db.AgentAuditLogs.ToListAsync();
            var docChunks = await db.DocumentChunks.ToListAsync();

            var totalTasks = workItems.Count;
            var todoCount = workItems.Count(w => w.Status == WorkItemStatus.Todo);
            var inProgressCount = workItems.Count(w => w.Status == WorkItemStatus.InProgress);
            var doneCount = workItems.Count(w => w.Status == WorkItemStatus.Done);

            var lowPriorityCount = workItems.Count(w => w.Priority == WorkItemPriority.Low);
            var medPriorityCount = workItems.Count(w => w.Priority == WorkItemPriority.Medium);
            var highPriorityCount = workItems.Count(w => w.Priority == WorkItemPriority.High);
            var criticalPriorityCount = workItems.Count(w => w.Priority == WorkItemPriority.Critical);

            var totalAgentRuns = auditLogs.Count;
            var approvedAgentRuns = auditLogs.Count(a => a.Approved);
            var rejectedAgentRuns = auditLogs.Count(a => !a.Approved);
            var approvalRate = totalAgentRuns > 0 ? (double)approvedAgentRuns / totalAgentRuns * 100 : 100.0;

            var totalChunks = docChunks.Count;
            var totalDocuments = docChunks.Select(c => c.SourceTitle).Distinct().Count();

            var completionRate = totalTasks > 0 ? (double)doneCount / totalTasks * 100 : 0.0;

            var response = new AnalyticsMetricsResponse(
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
