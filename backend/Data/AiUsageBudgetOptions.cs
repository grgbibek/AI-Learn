namespace TaskFlow.Api.Data;

public sealed class AiUsageBudgetOptions
{
    public bool Enabled { get; set; } = true;
    public int UserDailyRequestLimit { get; set; } = 100;
    public int AdminDailyRequestLimit { get; set; } = 500;
    public int UserDailyTokenLimit { get; set; } = 100_000;
    public int AdminDailyTokenLimit { get; set; } = 500_000;
    public decimal EstimatedCostPerThousandTokensUsd { get; set; } = 0m;
}