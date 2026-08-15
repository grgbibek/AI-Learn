using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

public record UserUsageToday(int RequestsUsed, int RequestLimit, int TokensUsed, int TokenLimit, int BudgetBlocks);
public record UserResponse(int Id, string UserName, string Email, string Role, int DailyAiRequestLimit, int DailyAiTokenLimit, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, UserUsageToday UsageToday);
public record CreateUserRequest(string UserName, string Email, string Password, string Role, int DailyAiRequestLimit, int DailyAiTokenLimit, bool IsActive = true);
public record UpdateUserRequest(string Email, string? Password, string Role, int DailyAiRequestLimit, int DailyAiTokenLimit, bool IsActive);

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization(AuthPolicies.CanManageUsers);

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var today = DateTime.UtcNow.Date;
            var users = await db.AppUsers
                .OrderBy(user => user.UserName)
                .ToListAsync(ct);

            var usageRows = await db.AiUsageLogs
                .Where(log => log.StartedAt >= today)
                .GroupBy(log => log.UserName)
                .Select(group => new
                {
                    UserName = group.Key,
                    RequestsUsed = group.Count(log => !log.BudgetWasExceeded),
                    TokensUsed = group.Where(log => !log.BudgetWasExceeded).Sum(log => log.EstimatedTotalTokens),
                    BudgetBlocks = group.Count(log => log.BudgetWasExceeded)
                })
                .ToListAsync(ct);
            var usageByUser = usageRows.ToDictionary(row => row.UserName, StringComparer.OrdinalIgnoreCase);

            var response = users.Select(user =>
            {
                usageByUser.TryGetValue(user.UserName, out var usage);
                return ToResponse(
                    user,
                    new UserUsageToday(
                        usage?.RequestsUsed ?? 0,
                        user.DailyAiRequestLimit,
                        usage?.TokensUsed ?? 0,
                        user.DailyAiTokenLimit,
                        usage?.BudgetBlocks ?? 0));
            });

            return Results.Ok(response);
        });

        group.MapPost("/", async (
            [FromBody] CreateUserRequest request,
            AppDbContext db,
            IPasswordHasher<AppUser> passwordHasher,
            IMemoryCache cache,
            CancellationToken ct) =>
        {
            var validation = ValidateUserFields(request.UserName, request.Email, request.Role, request.DailyAiRequestLimit, request.DailyAiTokenLimit, requirePassword: true, request.Password);
            if (validation is not null) return validation;

            var userName = request.UserName.Trim();
            var email = request.Email.Trim();
            if (await db.AppUsers.AnyAsync(user => user.UserName == userName || user.Email == email, ct))
            {
                return Results.Conflict(new { Message = "A user with that username or email already exists." });
            }

            var user = new AppUser
            {
                UserName = userName,
                Email = email,
                PasswordHash = string.Empty,
                Role = AppRoles.Normalize(request.Role),
                DailyAiRequestLimit = request.DailyAiRequestLimit,
                DailyAiTokenLimit = request.DailyAiTokenLimit,
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

            db.AppUsers.Add(user);
            await db.SaveChangesAsync(ct);
            cache.Remove(AppCacheKeys.AnalyticsMetrics);

            return Results.Created($"/api/users/{user.Id}", ToResponse(user, EmptyUsage(user)));
        });

        group.MapPut("/{id:int}", async (
            int id,
            [FromBody] UpdateUserRequest request,
            AppDbContext db,
            IPasswordHasher<AppUser> passwordHasher,
            IMemoryCache cache,
            CancellationToken ct) =>
        {
            var validation = ValidateUserFields("unchanged", request.Email, request.Role, request.DailyAiRequestLimit, request.DailyAiTokenLimit, requirePassword: false, request.Password);
            if (validation is not null) return validation;

            var user = await db.AppUsers.FindAsync([id], ct);
            if (user is null) return Results.NotFound();

            var email = request.Email.Trim();
            if (await db.AppUsers.AnyAsync(other => other.Id != id && other.Email == email, ct))
            {
                return Results.Conflict(new { Message = "A user with that email already exists." });
            }

            user.Email = email;
            user.Role = AppRoles.Normalize(request.Role);
            user.DailyAiRequestLimit = request.DailyAiRequestLimit;
            user.DailyAiTokenLimit = request.DailyAiTokenLimit;
            user.IsActive = request.IsActive;
            user.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            }

            await db.SaveChangesAsync(ct);
            cache.Remove(AppCacheKeys.AnalyticsMetrics);

            return Results.Ok(ToResponse(user, EmptyUsage(user)));
        });

        return routes;
    }

    private static UserResponse ToResponse(AppUser user, UserUsageToday usageToday) => new(
        user.Id,
        user.UserName,
        user.Email,
        user.Role,
        user.DailyAiRequestLimit,
        user.DailyAiTokenLimit,
        user.IsActive,
        user.CreatedAt,
        user.UpdatedAt,
        usageToday);

    private static UserUsageToday EmptyUsage(AppUser user) => new(0, user.DailyAiRequestLimit, 0, user.DailyAiTokenLimit, 0);

    private static IResult? ValidateUserFields(string userName, string email, string role, int dailyLimit, int dailyTokenLimit, bool requirePassword, string? password)
    {
        if (string.IsNullOrWhiteSpace(userName)) return Results.BadRequest(new { Message = "Username is required." });
        if (string.IsNullOrWhiteSpace(email)) return Results.BadRequest(new { Message = "Email is required." });
        if (!AppRoles.IsValid(role)) return Results.BadRequest(new { Message = "Role must be User or Admin." });
        if (dailyLimit <= 0) return Results.BadRequest(new { Message = "Daily AI request limit must be greater than zero." });
        if (dailyTokenLimit <= 0) return Results.BadRequest(new { Message = "Daily AI token limit must be greater than zero." });
        if (requirePassword && string.IsNullOrWhiteSpace(password)) return Results.BadRequest(new { Message = "Password is required." });
        if (!string.IsNullOrWhiteSpace(password) && password.Length < 8) return Results.BadRequest(new { Message = "Password must be at least 8 characters." });
        return null;
    }
}