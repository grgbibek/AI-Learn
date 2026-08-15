using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

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