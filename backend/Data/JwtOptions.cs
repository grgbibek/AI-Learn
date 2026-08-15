namespace TaskFlow.Api.Data;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "TaskFlow.Api";
    public string Audience { get; init; } = "TaskFlow.Angular";
    public string SigningKey { get; init; } = string.Empty;
    public int ExpirationMinutes { get; init; } = 120;
}