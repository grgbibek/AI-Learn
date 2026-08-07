using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<AgentAuditLog> AgentAuditLogs => Set<AgentAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Persist the embedding as JSON text rather than SQL Server's native `vector` column type,
        // since EF Core doesn't yet translate VECTOR_DISTANCE via LINQ - similarity search stays in VectorMathService for now.
        modelBuilder.Entity<DocumentChunk>()
            .Property(c => c.Embedding)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<float[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<float>(),
                new ValueComparer<float[]>(
                    (a, b) => (a ?? Array.Empty<float>()).SequenceEqual(b ?? Array.Empty<float>()),
                    v => v.Aggregate(0, (hash, f) => HashCode.Combine(hash, f)),
                    v => v.ToArray()))
            .HasColumnType("nvarchar(max)");

        modelBuilder.Entity<WorkItem>().HasData(
            new WorkItem
            {
                Id = 1,
                Title = "Set up .NET 10 & Angular 19 Environment",
                Description = "Verify development environment, install packages, and configure CORS policy.",
                Priority = WorkItemPriority.High,
                Status = WorkItemStatus.Done,
                CreatedAt = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc),
                DueDate = new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc)
            },
            new WorkItem
            {
                Id = 2,
                Title = "Implement Angular Signal State Management",
                Description = "Refactor components to use signal(), computed(), and httpResource pattern.",
                Priority = WorkItemPriority.Critical,
                Status = WorkItemStatus.InProgress,
                CreatedAt = new DateTime(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc),
                DueDate = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc)
            },
            new WorkItem
            {
                Id = 3,
                Title = "Add OpenTelemetry Logging & Tracing",
                Description = "Integrate structured logging into Minimal APIs for production observability.",
                Priority = WorkItemPriority.Medium,
                Status = WorkItemStatus.Todo,
                CreatedAt = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
                DueDate = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}

