namespace TaskFlow.Api.Models;

// Durable audit trail for the multi-agent pipeline (Planner -> Developer -> Reviewer),
// including rejected revision attempts, not just the final approved result.
public class AgentAuditLog
{
    public int Id { get; set; }
    public required string FeatureRequest { get; set; }
    public required string Subtask { get; set; }
    public int AttemptNumber { get; set; }
    public required string TechnicalApproach { get; set; }
    public bool Approved { get; set; }
    public required string Feedback { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
