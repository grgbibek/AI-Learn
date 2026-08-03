using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WorkItem>().HasData(
            new WorkItem
            {
                Id = 1,
                Title = "Set up .NET 10 & Angular 19 Environment",
                Description = "Verify development environment, install packages, and configure CORS policy.",
                Priority = WorkItemPriority.High,
                Status = WorkItemStatus.Done,
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                DueDate = DateTime.UtcNow.AddDays(-1)
            },
            new WorkItem
            {
                Id = 2,
                Title = "Implement Angular Signal State Management",
                Description = "Refactor components to use signal(), computed(), and httpResource pattern.",
                Priority = WorkItemPriority.Critical,
                Status = WorkItemStatus.InProgress,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                DueDate = DateTime.UtcNow.AddDays(2)
            },
            new WorkItem
            {
                Id = 3,
                Title = "Add OpenTelemetry Logging & Tracing",
                Description = "Integrate structured logging into Minimal APIs for production observability.",
                Priority = WorkItemPriority.Medium,
                Status = WorkItemStatus.Todo,
                CreatedAt = DateTime.UtcNow,
                DueDate = DateTime.UtcNow.AddDays(5)
            }
        );
    }
}
