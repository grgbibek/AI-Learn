using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskFlow.Api.Data;

// MCP clients (VS Code, Claude Desktop, the inspector) launch this process with an unpredictable
// working directory, so appsettings.json must be resolved relative to the built binary itself,
// not Directory.GetCurrentDirectory() (the default content root), or config silently comes back empty.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Stdio transport uses stdout exclusively for JSON-RPC protocol messages - all logs must go to
// stderr instead, or they'd corrupt the protocol stream and break every connected MCP client.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
