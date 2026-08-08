using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace McpServer;

// Write tools — scoped to create and field-level status/priority updates only.
// No delete, no bulk update, no free-form title edits: least-privilege, same principle
// as the existing read-only tools. Each tool returns the affected item as JSON so the
// calling AI agent can immediately confirm the result without a follow-up read call.
[McpServerToolType]
public static class WriteWorkItemTools
{
    [McpServerTool, Description(
        "Creates a new work item in TaskFlow. " +
        "Valid priorities: Low, Medium, High, Critical. " +
        "DueDate must be an ISO 8601 UTC string (e.g. '2026-08-15T00:00:00Z') or null. " +
        "Returns the created item as JSON including the assigned Id.")]
    public static async Task<string> CreateWorkItem(
        AppDbContext db,
        [Description("Required. The title of the work item.")] string title,
        [Description("Optional. A short description of the work.")] string? description,
        [Description("One of: Low, Medium, High, Critical. Defaults to Medium if omitted or invalid.")] string? priority,
        [Description("Optional ISO 8601 UTC due date string, e.g. '2026-08-15T00:00:00Z'.")] DateTime? dueDate)
    {
        if (string.IsNullOrWhiteSpace(title))
            return JsonSerializer.Serialize(new { error = "title is required and cannot be blank." });

        var parsedPriority = Enum.TryParse<WorkItemPriority>(priority, ignoreCase: true, out var p)
            ? p
            : WorkItemPriority.Medium;

        var item = new WorkItem
        {
            Title = title.Trim(),
            Description = description,
            Priority = parsedPriority,
            Status = WorkItemStatus.Todo,
            CreatedAt = DateTime.UtcNow,
            DueDate = dueDate
        };

        db.WorkItems.Add(item);
        await db.SaveChangesAsync();

        return JsonSerializer.Serialize(new
        {
            item.Id,
            item.Title,
            item.Description,
            Priority = item.Priority.ToString(),
            Status = item.Status.ToString(),
            item.CreatedAt,
            item.DueDate
        });
    }

    [McpServerTool, Description(
        "Moves a work item to a new status. " +
        "Valid statuses: Todo, InProgress, Done. " +
        "Returns the updated item as JSON, or an error object if the Id is not found.")]
    public static async Task<string> UpdateWorkItemStatus(
        AppDbContext db,
        [Description("The integer Id of the work item to update.")] int id,
        [Description("New status. One of: Todo, InProgress, Done.")] string status)
    {
        if (!Enum.TryParse<WorkItemStatus>(status, ignoreCase: true, out var parsedStatus))
            return JsonSerializer.Serialize(new { error = $"'{status}' is not a valid status. Use one of: Todo, InProgress, Done." });

        var item = await db.WorkItems.FindAsync(id);
        if (item is null)
            return JsonSerializer.Serialize(new { error = $"Work item with Id {id} was not found." });

        item.Status = parsedStatus;
        await db.SaveChangesAsync();

        return JsonSerializer.Serialize(new
        {
            item.Id,
            item.Title,
            Priority = item.Priority.ToString(),
            Status = item.Status.ToString(),
            item.DueDate
        });
    }

    [McpServerTool, Description(
        "Changes the priority of a work item. " +
        "Valid priorities: Low, Medium, High, Critical. " +
        "Returns the updated item as JSON, or an error object if the Id is not found.")]
    public static async Task<string> UpdateWorkItemPriority(
        AppDbContext db,
        [Description("The integer Id of the work item to update.")] int id,
        [Description("New priority. One of: Low, Medium, High, Critical.")] string priority)
    {
        if (!Enum.TryParse<WorkItemPriority>(priority, ignoreCase: true, out var parsedPriority))
            return JsonSerializer.Serialize(new { error = $"'{priority}' is not a valid priority. Use one of: Low, Medium, High, Critical." });

        var item = await db.WorkItems.FindAsync(id);
        if (item is null)
            return JsonSerializer.Serialize(new { error = $"Work item with Id {id} was not found." });

        item.Priority = parsedPriority;
        await db.SaveChangesAsync();

        return JsonSerializer.Serialize(new
        {
            item.Id,
            item.Title,
            Priority = item.Priority.ToString(),
            Status = item.Status.ToString(),
            item.DueDate
        });
    }
}
