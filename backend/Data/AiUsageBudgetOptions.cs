namespace TaskFlow.Api.Data;

public sealed class AiUsageBudgetOptions
{
    public bool Enabled { get; set; } = true;
    public int UserDailyRequestLimit { get; set; } = 100;
    public int AdminDailyRequestLimit { get; set; } = 500;
}