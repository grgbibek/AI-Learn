using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TaskFlow.Api.Data;

namespace TaskFlow.Api.Endpoints;

public record DevTokenRequest(string? UserName, string? Role);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/auth").WithTags("Auth");

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
            var expires = DateTime.UtcNow.AddMinutes(jwt.ExpirationMinutes);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userName),
                new Claim(JwtRegisteredClaimNames.UniqueName, userName),
                new Claim(ClaimTypes.Name, userName),
                new Claim(ClaimTypes.Role, role)
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

            return Results.Ok(new
            {
                AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
                TokenType = "Bearer",
                ExpiresAt = expires,
                UserName = userName,
                Role = role
            });
        });

        return routes;
    }
}