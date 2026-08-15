namespace TaskFlow.Api.Models;

public class AiUsageLog
{
    public long Id { get; set; }
    public required string UserName { get; set; }
    public required string Role { get; set; }
    public required string Capability { get; set; }
    public required string Endpoint { get; set; }
    public required string HttpMethod { get; set; }
    public int StatusCode { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public int DurationMs { get; set; }
    public bool BudgetWasExceeded { get; set; }
}