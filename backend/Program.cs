using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using TaskFlow.Api.Data;
using TaskFlow.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// Register IChatClient from Microsoft.Extensions.AI
// Defaults to DevMockChatClient for local dev without external API keys.
// To use Ollama or OpenAI, simply swap the registration:
// builder.Services.AddSingleton<IChatClient>(new OllamaChatClient("http://localhost:11434", "llama3.3"));
builder.Services.AddSingleton<IChatClient, DevMockChatClient>();

// Configure In-Memory Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseInMemoryDatabase("TaskFlowDb"));

// Configure CORS for Angular App (default http://localhost:4200)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://localhost:4201")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Seed In-Memory Database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Configure HTTP pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowAngularDev");

// Map endpoints
app.MapWorkItemEndpoints();
app.MapAiEndpoints();

app.Run();
