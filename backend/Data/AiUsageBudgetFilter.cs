using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Data;

public sealed class AiUsageBudgetFilter(string capability) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var options = httpContext.RequestServices.GetRequiredService<IOptions<AiUsageBudgetOptions>>().Value;

        if (!options.Enabled || httpContext.User.Identity?.IsAuthenticated != true)
        {
            return await next(context);
        }

        var db = httpContext.RequestServices.GetRequiredService<AppDbContext>();
        var userName = httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? httpContext.User.Identity.Name
            ?? "unknown";
        var role = httpContext.User.FindFirstValue(ClaimTypes.Role) ?? "User";
        var userLimit = await db.AppUsers
            .Where(user => user.UserName == userName && user.IsActive)
            .Select(user => (int?)user.DailyAiRequestLimit)
            .FirstOrDefaultAsync(httpContext.RequestAborted);
        var dailyLimit = userLimit
            ?? (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                ? options.AdminDailyRequestLimit
                : options.UserDailyRequestLimit);

        var startedAt = DateTime.UtcNow;
        var dayStart = startedAt.Date;
        var usedToday = await db.AiUsageLogs
            .CountAsync(log => log.UserName == userName
                && log.StartedAt >= dayStart
                && !log.BudgetWasExceeded,
                httpContext.RequestAborted);

        if (usedToday >= dailyLimit)
        {
            await SaveUsageLogAsync(
                db,
                httpContext,
                userName,
                role,
                startedAt,
                StatusCodes.Status429TooManyRequests,
                budgetWasExceeded: true);

            return Results.Json(new
            {
                Type = "https://httpstatuses.com/429",
                Title = "AI usage budget exceeded.",
                Status = StatusCodes.Status429TooManyRequests,
                Detail = $"The daily AI request budget of {dailyLimit} has been exhausted for this user."
            }, statusCode: StatusCodes.Status429TooManyRequests, contentType: "application/problem+json");
        }

        var stopwatch = Stopwatch.StartNew();
        object? result;
        var statusCode = StatusCodes.Status200OK;

        try
        {
            result = await next(context);
            statusCode = result is IStatusCodeHttpResult statusCodeResult
                ? statusCodeResult.StatusCode ?? StatusCodes.Status200OK
                : StatusCodes.Status200OK;
        }
        catch
        {
            statusCode = StatusCodes.Status500InternalServerError;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            await SaveUsageLogAsync(
                db,
                httpContext,
                userName,
                role,
                startedAt,
                statusCode,
                budgetWasExceeded: false,
                durationMs: (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue));
        }

        return result;
    }

    private async Task SaveUsageLogAsync(
        AppDbContext db,
        HttpContext httpContext,
        string userName,
        string role,
        DateTime startedAt,
        int statusCode,
        bool budgetWasExceeded,
        int? durationMs = null)
    {
        var finishedAt = DateTime.UtcNow;
        db.AiUsageLogs.Add(new AiUsageLog
        {
            UserName = userName,
            Role = role,
            Capability = capability,
            Endpoint = httpContext.Request.Path.Value ?? string.Empty,
            HttpMethod = httpContext.Request.Method,
            StatusCode = statusCode,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            DurationMs = durationMs ?? (int)Math.Min((finishedAt - startedAt).TotalMilliseconds, int.MaxValue),
            BudgetWasExceeded = budgetWasExceeded
        });

        await db.SaveChangesAsync(httpContext.RequestAborted);
    }
}