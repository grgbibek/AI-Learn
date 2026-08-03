namespace TaskFlow.Api.Models;

public enum WorkItemPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum WorkItemStatus
{
    Todo,
    InProgress,
    Done
}

public class WorkItem
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public WorkItemPriority Priority { get; set; } = WorkItemPriority.Medium;
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Todo;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
}

// Record DTOs for type-safe requests/responses
public record CreateWorkItemRequest(
    string Title,
    string? Description,
    WorkItemPriority Priority,
    DateTime? DueDate
);

public record UpdateWorkItemRequest(
    string Title,
    string? Description,
    WorkItemPriority Priority,
    WorkItemStatus Status,
    DateTime? DueDate
);
