using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TaskFlow.Api.Data;

namespace Backend.Tests;

public sealed class TaskFlowApiFactory : WebApplicationFactory<Program>
{
    private const string TestIssuer = "TaskFlow.Api.Tests";
    private const string TestAudience = "TaskFlow.Tests";
    private const string TestSigningKey = "integration-test-only-taskflow-jwt-signing-key";

    private readonly string environmentName;
    private readonly int userDailyRequestLimit;
    private readonly int adminDailyRequestLimit;
    private readonly string databaseName = $"TaskFlowTests-{Guid.NewGuid():N}";

    public TaskFlowApiFactory()
        : this("Development", userDailyRequestLimit: 100, adminDailyRequestLimit: 500)
    {
    }

    private TaskFlowApiFactory(string environmentName, int userDailyRequestLimit, int adminDailyRequestLimit)
    {
        this.environmentName = environmentName;
        this.userDailyRequestLimit = userDailyRequestLimit;
        this.adminDailyRequestLimit = adminDailyRequestLimit;
        Environment.SetEnvironmentVariable("TaskFlow__SkipMigrations", "true");
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestAudience);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", TestSigningKey);
        Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", "30");
    }

    public static TaskFlowApiFactory ForEnvironment(string environmentName) =>
        new(environmentName, userDailyRequestLimit: 100, adminDailyRequestLimit: 500);

    public static TaskFlowApiFactory ForBudgets(int userDailyRequestLimit, int adminDailyRequestLimit) =>
        new("Development", userDailyRequestLimit, adminDailyRequestLimit);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environmentName);
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TaskFlow:SkipMigrations"] = "true",
                ["Jwt:Issuer"] = TestIssuer,
                ["Jwt:Audience"] = TestAudience,
                ["Jwt:SigningKey"] = TestSigningKey,
                ["Jwt:ExpirationMinutes"] = "30",
                ["AiUsageBudget:Enabled"] = "true",
                ["AiUsageBudget:UserDailyRequestLimit"] = userDailyRequestLimit.ToString(),
                ["AiUsageBudget:AdminDailyRequestLimit"] = adminDailyRequestLimit.ToString()
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName));

            services.PostConfigure<AiUsageBudgetOptions>(options =>
            {
                options.Enabled = true;
                options.UserDailyRequestLimit = userDailyRequestLimit;
                options.AdminDailyRequestLimit = adminDailyRequestLimit;
            });
        });
    }
}