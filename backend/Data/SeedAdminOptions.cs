namespace TaskFlow.Api.Data;

public sealed class SeedAdminOptions
{
    public string UserName { get; set; } = "admin";
    public string Email { get; set; } = "admin@taskflow.local";
    public string Password { get; set; } = "Admin123!";
    public int DailyAiRequestLimit { get; set; } = 500;
    public int DailyAiTokenLimit { get; set; } = 500_000;
}