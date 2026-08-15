using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<AgentAuditLog> AgentAuditLogs => Set<AgentAuditLog>();
    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AiUsageLog>()
            .HasIndex(log => new { log.UserName, log.StartedAt });

        modelBuilder.Entity<AiUsageLog>()
            .Property(log => log.EstimatedCostUsd)
            .HasPrecision(18, 6);

        modelBuilder.Entity<AppUser>()
            .HasIndex(user => user.UserName)
            .IsUnique();

        modelBuilder.Entity<AppUser>()
            .HasIndex(user => user.Email)
            .IsUnique();

        // SQL Server 2025 native `vector` column (EF Core 10+) - nomic-embed-text produces 768-dim
        // embeddings. Similarity search runs via EF.Functions.VectorDistance() inside SQL Server
        // instead of loading every row's embedding into app memory for manual cosine similarity.
        modelBuilder.Entity<DocumentChunk>()
            .Property(c => c.Embedding)
            .HasColumnType("vector(768)");

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

