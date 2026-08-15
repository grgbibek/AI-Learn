using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

public record DevTokenRequest(string? UserName, string? Role);
public record LoginRequest(string UserName, string Password);
public record AuthResponse(string AccessToken, string TokenType, DateTime ExpiresAt, string UserName, string Role, int DailyAiRequestLimit, int DailyAiTokenLimit);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            AppDbContext db,
            IPasswordHasher<AppUser> passwordHasher,
            IOptions<JwtOptions> options,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { Message = "Username and password are required." });
            }

            var userName = request.UserName.Trim();
            var user = await db.AppUsers.FirstOrDefaultAsync(
                item => item.UserName == userName || item.Email == userName,
                ct);

            if (user is null || !user.IsActive)
            {
                return Results.Unauthorized();
            }

            var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verification == PasswordVerificationResult.Failed)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(CreateAuthResponse(user.UserName, user.Email, user.Role, user.DailyAiRequestLimit, user.DailyAiTokenLimit, options.Value));
        });

        group.MapPost("/dev-token", (DevTokenRequest request, IOptions<JwtOptions> options, IWebHostEnvironment environment) =>
        {
            if (!environment.IsDevelopment())
            {
                return Results.NotFound();
            }

            var jwt = options.Value;
            if (string.IsNullOrWhiteSpace(jwt.SigningKey) || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
            {
                return Results.Problem("Jwt:SigningKey must be configured with at least 32 bytes.");
            }

            var userName = string.IsNullOrWhiteSpace(request.UserName) ? "local-admin" : request.UserName.Trim();
            var role = string.Equals(request.Role, "User", StringComparison.OrdinalIgnoreCase) ? "User" : "Admin";
            var dailyLimit = role == AppRoles.Admin ? 500 : 100;
            var dailyTokenLimit = role == AppRoles.Admin ? 500_000 : 100_000;

            return Results.Ok(CreateAuthResponse(userName, $"{userName}@dev.local", role, dailyLimit, dailyTokenLimit, jwt));
        });

        return routes;
    }

    private static AuthResponse CreateAuthResponse(string userName, string email, string role, int dailyAiRequestLimit, int dailyAiTokenLimit, JwtOptions jwt)
    {
        var expires = DateTime.UtcNow.AddMinutes(jwt.ExpirationMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userName),
            new Claim(JwtRegisteredClaimNames.UniqueName, userName),
            new Claim(ClaimTypes.Name, userName),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role, role),
            new Claim("daily_ai_request_limit", dailyAiRequestLimit.ToString()),
            new Claim("daily_ai_token_limit", dailyAiTokenLimit.ToString())
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new AuthResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            "Bearer",
            expires,
            userName,
            role,
            dailyAiRequestLimit,
            dailyAiTokenLimit);
    }
}