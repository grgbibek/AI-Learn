namespace TaskFlow.Api.Models;

public class AiUsageLog
{
    public long Id { get; set; }
    public required string UserName { get; set; }
    public required string Role { get; set; }
    public required string Capability { get; set; }
    public required string Endpoint { get; set; }
    public required string HttpMethod { get; set; }
    public required string ProviderName { get; set; } = "Ollama";
    public required string ModelName { get; set; } = "unknown";
    public int StatusCode { get; set; }
    public int EstimatedInputTokens { get; set; }
    public int EstimatedOutputTokens { get; set; }
    public int EstimatedTotalTokens { get; set; }
    public string TokenUsageSource { get; set; } = "Estimated";
    public decimal EstimatedCostUsd { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public int DurationMs { get; set; }
    public bool BudgetWasExceeded { get; set; }
}