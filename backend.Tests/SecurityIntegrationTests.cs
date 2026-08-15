using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace Backend.Tests;

public sealed class SecurityIntegrationTests : IClassFixture<TaskFlowApiFactory>
{
    private readonly TaskFlowApiFactory factory;

    public SecurityIntegrationTests(TaskFlowApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task WorkItemsRequireAuthentication()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/workitems/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserCanReadWorkItemsButCannotCreateThem()
    {
        using var client = factory.CreateClient();
        await AuthorizeAsync(client, "test-user", "User");

        var readResponse = await client.GetAsync("/api/workitems/");
        var writeResponse = await client.PostAsJsonAsync("/api/workitems/", new
        {
            title = "Unauthorized write attempt",
            description = "Normal users should not create work items.",
            priority = 1,
            dueDate = (DateTime?)null
        });

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, writeResponse.StatusCode);
    }

    [Fact]
    public async Task AdminCanViewAnalytics()
    {
        using var client = factory.CreateClient();
        await AuthorizeAsync(client, "test-admin", "Admin");

        var response = await client.GetAsync("/api/analytics/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnalyticsMetricsIncludeAiUsageSummaries()
    {
        await using var analyticsFactory = TaskFlowApiFactory.ForBudgets(userDailyRequestLimit: 10, adminDailyRequestLimit: 10);
        using (var scope = analyticsFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var today = DateTime.UtcNow.Date.AddHours(1);
            db.AiUsageLogs.AddRange(
                new AiUsageLog
                {
                    UserName = "alice",
                    Role = "User",
                    Capability = RateLimitPolicies.AiChat,
                    Endpoint = "/api/rag/ask",
                    HttpMethod = "POST",
                    ProviderName = "Ollama",
                    ModelName = "llama3.2",
                    StatusCode = 200,
                    EstimatedInputTokens = 100,
                    EstimatedOutputTokens = 0,
                    EstimatedTotalTokens = 100,
                    EstimatedCostUsd = 0m,
                    StartedAt = today,
                    FinishedAt = today.AddMilliseconds(120),
                    DurationMs = 120,
                    BudgetWasExceeded = false
                },
                new AiUsageLog
                {
                    UserName = "alice",
                    Role = "User",
                    Capability = RateLimitPolicies.AiChat,
                    Endpoint = "/api/rag/ask",
                    HttpMethod = "POST",
                    ProviderName = "Ollama",
                    ModelName = "llama3.2",
                    StatusCode = 429,
                    EstimatedInputTokens = 50,
                    EstimatedOutputTokens = 0,
                    EstimatedTotalTokens = 50,
                    EstimatedCostUsd = 0m,
                    StartedAt = today.AddMinutes(1),
                    FinishedAt = today.AddMinutes(1).AddMilliseconds(2),
                    DurationMs = 2,
                    BudgetWasExceeded = true
                },
                new AiUsageLog
                {
                    UserName = "admin",
                    Role = "Admin",
                    Capability = RateLimitPolicies.AgentPipeline,
                    Endpoint = "/api/agents/audit-log",
                    HttpMethod = "GET",
                    ProviderName = "Ollama",
                    ModelName = "llama3.2",
                    StatusCode = 200,
                    EstimatedInputTokens = 25,
                    EstimatedOutputTokens = 0,
                    EstimatedTotalTokens = 25,
                    EstimatedCostUsd = 0m,
                    StartedAt = today.AddMinutes(2),
                    FinishedAt = today.AddMinutes(2).AddMilliseconds(30),
                    DurationMs = 30,
                    BudgetWasExceeded = false
                });
            await db.SaveChangesAsync();
        }

        using var client = analyticsFactory.CreateClient();
        await AuthorizeAsync(client, "analytics-admin", "Admin");

        var response = await client.GetAsync("/api/analytics/metrics");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var aiUsage = document.RootElement.GetProperty("aiUsage");
        var byCapability = aiUsage.GetProperty("byCapability").EnumerateArray().ToList();
        var topUsers = aiUsage.GetProperty("topUsers").EnumerateArray().ToList();

        Assert.Equal(3, aiUsage.GetProperty("requestsToday").GetInt32());
        Assert.Equal(1, aiUsage.GetProperty("budgetExceededToday").GetInt32());
        Assert.Equal(175, aiUsage.GetProperty("estimatedTokensToday").GetInt32());
        Assert.Equal(2, aiUsage.GetProperty("uniqueUsersToday").GetInt32());
        Assert.Contains(byCapability, item => item.GetProperty("capability").GetString() == RateLimitPolicies.AiChat
            && item.GetProperty("requests").GetInt32() == 2
            && item.GetProperty("budgetExceeded").GetInt32() == 1
            && item.GetProperty("estimatedTokens").GetInt32() == 150);
        Assert.Contains(topUsers, item => item.GetProperty("userName").GetString() == "alice"
            && item.GetProperty("requests").GetInt32() == 2
            && item.GetProperty("budgetExceeded").GetInt32() == 1
            && item.GetProperty("estimatedTokens").GetInt32() == 150);
    }

    [Fact]
    public async Task AgentEndpointReturnsTooManyRequestsAfterLimitIsExceeded()
    {
        using var client = factory.CreateClient();
        await AuthorizeAsync(client, "rate-limit-admin", "Admin");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var response = await client.GetAsync("/api/agents/audit-log?take=1");
            statuses.Add(response.StatusCode);
        }

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], statuses);
    }

    [Fact]
    public async Task AgentEndpointWritesAiUsageLogsForAcceptedAndBudgetExceededRequests()
    {
        await using var budgetFactory = TaskFlowApiFactory.ForBudgets(userDailyRequestLimit: 1, adminDailyRequestLimit: 1);
        using var client = budgetFactory.CreateClient();
        await AuthorizeAsync(client, "budget-admin", "Admin");

        var firstResponse = await client.GetAsync("/api/agents/audit-log?take=1");
        var secondResponse = await client.GetAsync("/api/agents/audit-log?take=1");

        using var scope = budgetFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logs = await db.AiUsageLogs
            .Where(log => log.UserName == "budget-admin")
            .OrderBy(log => log.Id)
            .ToListAsync();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
        Assert.Equal(2, logs.Count);
        Assert.Equal(RateLimitPolicies.AgentPipeline, logs[0].Capability);
        Assert.Equal((int)HttpStatusCode.OK, logs[0].StatusCode);
        Assert.False(logs[0].BudgetWasExceeded);
        Assert.Equal((int)HttpStatusCode.TooManyRequests, logs[1].StatusCode);
        Assert.True(logs[1].BudgetWasExceeded);
    }

    [Fact]
    public async Task AgentEndpointReturnsTooManyRequestsWhenDailyTokenBudgetIsExceeded()
    {
        await using var tokenBudgetFactory = TaskFlowApiFactory.ForTokenBudgets(userDailyTokenLimit: 1, adminDailyTokenLimit: 1);
        using var client = tokenBudgetFactory.CreateClient();
        await AuthorizeAsync(client, "token-budget-admin", "Admin");

        var response = await client.GetAsync("/api/agents/audit-log?take=1");

        using var scope = tokenBudgetFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var log = await db.AiUsageLogs.SingleAsync(item => item.UserName == "token-budget-admin");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(log.BudgetWasExceeded);
        Assert.True(log.EstimatedTotalTokens > 1);
        Assert.Equal(RateLimitPolicies.AgentPipeline, log.Capability);
    }

    [Fact]
    public async Task AdminCanCreateUserAndUserCanLogin()
    {
        using var client = factory.CreateClient();
        await AuthorizeAsync(client, "user-admin", "Admin");

        var createResponse = await client.PostAsJsonAsync("/api/users/", new
        {
            userName = "new-user",
            email = "new-user@taskflow.local",
            password = "Password123!",
            role = "User",
            dailyAiRequestLimit = 25,
            dailyAiTokenLimit = 25000,
            isActive = true
        });
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "new-user",
            password = "Password123!"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        await using var stream = await loginResponse.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal("new-user", document.RootElement.GetProperty("userName").GetString());
        Assert.Equal("User", document.RootElement.GetProperty("role").GetString());
        Assert.Equal(25, document.RootElement.GetProperty("dailyAiRequestLimit").GetInt32());
        Assert.Equal(25000, document.RootElement.GetProperty("dailyAiTokenLimit").GetInt32());
    }

    [Fact]
    public async Task UserManagementRequiresAdminRole()
    {
        using var client = factory.CreateClient();
        await AuthorizeAsync(client, "normal-manager", "User");

        var response = await client.GetAsync("/api/users/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UserListIncludesTodayUsageForEachUser()
    {
        await using var usageFactory = TaskFlowApiFactory.ForBudgets(userDailyRequestLimit: 100, adminDailyRequestLimit: 100);
        using var client = usageFactory.CreateClient();
        await AuthorizeAsync(client, "setup-admin", "Admin");

        var createResponse = await client.PostAsJsonAsync("/api/users/", new
        {
            userName = "usage-admin",
            email = "usage-admin@taskflow.local",
            password = "Password123!",
            role = "Admin",
            dailyAiRequestLimit = 500,
            dailyAiTokenLimit = 500000,
            isActive = true
        });
        createResponse.EnsureSuccessStatusCode();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "usage-admin",
            password = "Password123!"
        });
        loginResponse.EnsureSuccessStatusCode();

        await using (var loginStream = await loginResponse.Content.ReadAsStreamAsync())
        {
            using var loginDocument = await JsonDocument.ParseAsync(loginStream);
            var token = loginDocument.RootElement.GetProperty("accessToken").GetString();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        await client.GetAsync("/api/agents/audit-log?take=1");

        var response = await client.GetAsync("/api/users/");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var usageAdmin = document.RootElement.EnumerateArray()
            .Single(user => user.GetProperty("userName").GetString() == "usage-admin");
        var usage = usageAdmin.GetProperty("usageToday");

        Assert.Equal(1, usage.GetProperty("requestsUsed").GetInt32());
        Assert.True(usage.GetProperty("tokensUsed").GetInt32() > 0);
        Assert.True(usage.GetProperty("requestLimit").GetInt32() > 0);
        Assert.True(usage.GetProperty("tokenLimit").GetInt32() > 0);
    }
    [Fact]
    public async Task DevTokenEndpointIsUnavailableOutsideDevelopment()
    {
        await using var productionFactory = TaskFlowApiFactory.ForEnvironment("Production");
        using var client = productionFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/dev-token", new
        {
            userName = "prod-user",
            role = "Admin"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task AuthorizeAsync(HttpClient client, string userName, string role)
    {
        var response = await client.PostAsJsonAsync("/api/auth/dev-token", new
        {
            userName,
            role
        });
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var token = document.RootElement.GetProperty("accessToken").GetString();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}