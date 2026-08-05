using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OllamaSharp;
using TaskFlow.Api.Data;
using TaskFlow.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// ─── Ollama Client (Real Local LLM) ──────────────────────────────────────────
// Requires Ollama running on http://localhost:11434
// Pull models first:  ollama pull llama3.2   &&   ollama pull nomic-embed-text
//
// OllamaSharp 5.x: OllamaApiClient directly implements IChatClient & IEmbeddingGenerator
var ollamaUri = new Uri(builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434");
var ollamaChatModel  = builder.Configuration["Ollama:ChatModel"]      ?? "llama3.2";
var ollamaEmbedModel = builder.Configuration["Ollama:EmbeddingModel"] ?? "nomic-embed-text";

// IChatClient → real local Llama model
// UseFunctionInvocation() auto-executes tool calls (e.g. workload-assistant) and loops until final text is produced.
// Cast to IChatClient first since OllamaApiClient also implements IEmbeddingGenerator, making AsBuilder() ambiguous.
builder.Services.AddSingleton<IChatClient>(
    ((IChatClient)new OllamaApiClient(ollamaUri, ollamaChatModel))
        .AsBuilder()
        .UseFunctionInvocation()
        .Build());

// Register Vector Math & Embeddings Services (Phase 3)
builder.Services.AddSingleton<VectorMathService>();
builder.Services.AddSingleton<TextChunkingService>();

// IEmbeddingGenerator → real local nomic-embed-text model (768 dimensions)
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new OllamaApiClient(ollamaUri, ollamaEmbedModel));

// ─── Swap to mock clients if Ollama is not running ───────────────────────────
// builder.Services.AddSingleton<IChatClient, DevMockChatClient>();
// builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, DevMockEmbeddingGenerator>();

// Configure SQL Server Database (SQL Server 2025 LocalDB)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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

// Apply pending EF Core Migrations (creates the database on first run)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
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
app.MapRagEndpoints();

app.Run();
