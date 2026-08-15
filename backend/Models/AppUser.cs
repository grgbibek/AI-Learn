namespace TaskFlow.Api.Models;

public static class AppRoles
{
    public const string User = nameof(User);
    public const string Admin = nameof(Admin);

    public static bool IsValid(string role) =>
        string.Equals(role, User, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string role) =>
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase) ? Admin : User;
}

public class AppUser
{
    public int Id { get; set; }
    public required string UserName { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string Role { get; set; } = AppRoles.User;
    public int DailyAiRequestLimit { get; set; } = 100;
    public int DailyAiTokenLimit { get; set; } = 100_000;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}