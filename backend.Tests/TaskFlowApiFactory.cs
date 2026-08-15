using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TaskFlow.Api.Data;

namespace Backend.Tests;

public sealed class TaskFlowApiFactory : WebApplicationFactory<Program>
{
    private const string TestIssuer = "TaskFlow.Api.Tests";
    private const string TestAudience = "TaskFlow.Tests";
    private const string TestSigningKey = "integration-test-only-taskflow-jwt-signing-key";

    private readonly string environmentName;

    public TaskFlowApiFactory()
        : this("Development")
    {
    }

    private TaskFlowApiFactory(string environmentName)
    {
        this.environmentName = environmentName;
        Environment.SetEnvironmentVariable("TaskFlow__SkipMigrations", "true");
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestAudience);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", TestSigningKey);
        Environment.SetEnvironmentVariable("Jwt__ExpirationMinutes", "30");
    }

    public static TaskFlowApiFactory ForEnvironment(string environmentName) => new(environmentName);

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
                ["Jwt:ExpirationMinutes"] = "30"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase($"TaskFlowTests-{Guid.NewGuid():N}"));
        });
    }
}