using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace McpServer;

// Read-only query tools exposing the TaskFlow WorkItems database to any MCP client (VS Code Copilot,
// Claude Desktop, Cursor, etc). Scoped to safe read operations only — no writes here.
// Write operations (create, status change, priority change) live in WriteWorkItemTools.cs.
[McpServerToolType]
public static class WorkItemTools
{
    [McpServerTool, Description("Gets all work items that are overdue (past their due date) and have High or Critical priority, excluding items already marked Done.")]
    public static async Task<string> GetOverdueHighPriorityItems(AppDbContext db)
    {
        var items = await db.WorkItems
            .Where(w => w.DueDate != null
                     && w.DueDate < DateTime.UtcNow
                     && w.Status != WorkItemStatus.Done
                     && (w.Priority == WorkItemPriority.High || w.Priority == WorkItemPriority.Critical))
            .Select(w => new { w.Id, w.Title, Priority = w.Priority.ToString(), Status = w.Status.ToString(), w.DueDate })
            .ToListAsync();

        return JsonSerializer.Serialize(items);
    }

    [McpServerTool, Description("Gets a summary count of work items grouped by status (Todo, InProgress, Done).")]
    public static async Task<string> GetWorkloadSummary(AppDbContext db)
    {
        var summary = await db.WorkItems
            .GroupBy(w => w.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        return JsonSerializer.Serialize(summary);
    }

    [McpServerTool, Description("Gets all work items matching an optional status filter (Todo, InProgress, or Done). Omit the status to get every work item.")]
    public static async Task<string> GetWorkItemsByStatus(AppDbContext db, [Description("One of: Todo, InProgress, Done. Leave null for all items.")] string? status = null)
    {
        var query = db.WorkItems.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<WorkItemStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            query = query.Where(w => w.Status == parsedStatus);
        }

        var items = await query
            .Select(w => new { w.Id, w.Title, w.Description, Priority = w.Priority.ToString(), Status = w.Status.ToString(), w.DueDate })
            .ToListAsync();

        return JsonSerializer.Serialize(items);
    }
}
