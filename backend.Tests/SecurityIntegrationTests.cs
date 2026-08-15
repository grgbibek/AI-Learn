using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Api.Data;

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