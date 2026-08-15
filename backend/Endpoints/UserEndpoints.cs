using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

public record UserResponse(int Id, string UserName, string Email, string Role, int DailyAiRequestLimit, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);
public record CreateUserRequest(string UserName, string Email, string Password, string Role, int DailyAiRequestLimit, bool IsActive = true);
public record UpdateUserRequest(string Email, string? Password, string Role, int DailyAiRequestLimit, bool IsActive);

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization(AuthPolicies.CanManageUsers);

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var users = await db.AppUsers
                .OrderBy(user => user.UserName)
                .Select(user => ToResponse(user))
                .ToListAsync(ct);

            return Results.Ok(users);
        });

        group.MapPost("/", async (
            [FromBody] CreateUserRequest request,
            AppDbContext db,
            IPasswordHasher<AppUser> passwordHasher,
            IMemoryCache cache,
            CancellationToken ct) =>
        {
            var validation = ValidateUserFields(request.UserName, request.Email, request.Role, request.DailyAiRequestLimit, requirePassword: true, request.Password);
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
                IsActive = request.IsActive,
                CreatedAt = DateTime.UtcNow
            };
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

            db.AppUsers.Add(user);
            await db.SaveChangesAsync(ct);
            cache.Remove(AppCacheKeys.AnalyticsMetrics);

            return Results.Created($"/api/users/{user.Id}", ToResponse(user));
        });

        group.MapPut("/{id:int}", async (
            int id,
            [FromBody] UpdateUserRequest request,
            AppDbContext db,
            IPasswordHasher<AppUser> passwordHasher,
            IMemoryCache cache,
            CancellationToken ct) =>
        {
            var validation = ValidateUserFields("unchanged", request.Email, request.Role, request.DailyAiRequestLimit, requirePassword: false, request.Password);
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
            user.IsActive = request.IsActive;
            user.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            }

            await db.SaveChangesAsync(ct);
            cache.Remove(AppCacheKeys.AnalyticsMetrics);

            return Results.Ok(ToResponse(user));
        });

        return routes;
    }

    private static UserResponse ToResponse(AppUser user) => new(
        user.Id,
        user.UserName,
        user.Email,
        user.Role,
        user.DailyAiRequestLimit,
        user.IsActive,
        user.CreatedAt,
        user.UpdatedAt);

    private static IResult? ValidateUserFields(string userName, string email, string role, int dailyLimit, bool requirePassword, string? password)
    {
        if (string.IsNullOrWhiteSpace(userName)) return Results.BadRequest(new { Message = "Username is required." });
        if (string.IsNullOrWhiteSpace(email)) return Results.BadRequest(new { Message = "Email is required." });
        if (!AppRoles.IsValid(role)) return Results.BadRequest(new { Message = "Role must be User or Admin." });
        if (dailyLimit <= 0) return Results.BadRequest(new { Message = "Daily AI request limit must be greater than zero." });
        if (requirePassword && string.IsNullOrWhiteSpace(password)) return Results.BadRequest(new { Message = "Password is required." });
        if (!string.IsNullOrWhiteSpace(password) && password.Length < 8) return Results.BadRequest(new { Message = "Password must be at least 8 characters." });
        return null;
    }
}