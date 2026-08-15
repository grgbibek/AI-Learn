using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics")
            .RequireAuthorization(AuthPolicies.CanViewAnalytics);

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

                var today = DateTime.UtcNow.Date;
                var aiUsageToday = db.AiUsageLogs.Where(log => log.StartedAt >= today);
                var aiRequestsToday = await aiUsageToday.CountAsync(ct);
                var aiBudgetExceededToday = await aiUsageToday.CountAsync(log => log.BudgetWasExceeded, ct);
                var aiEstimatedTokensToday = await aiUsageToday.SumAsync(log => (int?)log.EstimatedTotalTokens, ct) ?? 0;
                var aiEstimatedCostToday = await aiUsageToday.SumAsync(log => (decimal?)log.EstimatedCostUsd, ct) ?? 0m;
                var aiUniqueUsersToday = await aiUsageToday
                    .Select(log => log.UserName)
                    .Distinct()
                    .CountAsync(ct);

                var aiUsageByCapabilityRows = await aiUsageToday
                    .GroupBy(log => log.Capability)
                    .Select(group => new
                    {
                        Capability = group.Key,
                        Requests = group.Count(),
                        BudgetExceeded = group.Count(log => log.BudgetWasExceeded),
                        EstimatedTokens = group.Sum(log => log.EstimatedTotalTokens),
                        EstimatedCostUsd = group.Sum(log => log.EstimatedCostUsd)
                    })
                    .OrderByDescending(item => item.Requests)
                    .ToListAsync(ct);
                var aiUsageByCapability = aiUsageByCapabilityRows
                    .Select(item => new AiUsageByCapability(item.Capability, item.Requests, item.BudgetExceeded, item.EstimatedTokens, Math.Round(item.EstimatedCostUsd, 6)))
                    .ToList();

                var aiTopUserRows = await aiUsageToday
                    .GroupBy(log => new { log.UserName, log.Role })
                    .Select(group => new
                    {
                        group.Key.UserName,
                        group.Key.Role,
                        Requests = group.Count(),
                        BudgetExceeded = group.Count(log => log.BudgetWasExceeded),
                        EstimatedTokens = group.Sum(log => log.EstimatedTotalTokens),
                        EstimatedCostUsd = group.Sum(log => log.EstimatedCostUsd)
                    })
                    .OrderByDescending(item => item.Requests)
                    .ThenBy(item => item.UserName)
                    .Take(5)
                    .ToListAsync(ct);
                var aiTopUsers = aiTopUserRows
                    .Select(item => new AiUsageByUser(item.UserName, item.Role, item.Requests, item.BudgetExceeded, item.EstimatedTokens, Math.Round(item.EstimatedCostUsd, 6)))
                    .ToList();

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
                    ),

                    AiUsage: new AiUsageMetrics(
                        RequestsToday: aiRequestsToday,
                        BudgetExceededToday: aiBudgetExceededToday,
                        EstimatedTokensToday: aiEstimatedTokensToday,
                        EstimatedCostUsdToday: Math.Round(aiEstimatedCostToday, 6),
                        UniqueUsersToday: aiUniqueUsersToday,
                        ByCapability: aiUsageByCapability,
                        TopUsers: aiTopUsers
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
public record AiUsageByCapability(string Capability, int Requests, int BudgetExceeded, int EstimatedTokens, decimal EstimatedCostUsd);
public record AiUsageByUser(string UserName, string Role, int Requests, int BudgetExceeded, int EstimatedTokens, decimal EstimatedCostUsd);
public record AiUsageMetrics(
    int RequestsToday,
    int BudgetExceededToday,
    int EstimatedTokensToday,
    decimal EstimatedCostUsdToday,
    int UniqueUsersToday,
    List<AiUsageByCapability> ByCapability,
    List<AiUsageByUser> TopUsers);

public record AnalyticsMetricsResponse(
    int TotalWorkItems,
    int CompletedWorkItems,
    int PendingWorkItems,
    double CompletionRate,
    StatusDistribution StatusDistribution,
    PriorityDistribution PriorityDistribution,
    AgentPipelineMetrics AgentMetrics,
    KnowledgeBaseMetrics KnowledgeBaseMetrics,
    AiUsageMetrics AiUsage
);
