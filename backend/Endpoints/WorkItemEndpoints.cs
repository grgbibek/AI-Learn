using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Api.Data;
using TaskFlow.Api.Models;

namespace TaskFlow.Api.Endpoints;

public static class WorkItemEndpoints
{
    public static IEndpointRouteBuilder MapWorkItemEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/workitems").WithTags("WorkItems");

        // GET all work items
        group.MapGet("/", async (AppDbContext db) =>
        {
            var items = await db.WorkItems.OrderByDescending(w => w.CreatedAt).ToListAsync();
            return Results.Ok(items);
        });

        // GET single work item by ID
        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var item = await db.WorkItems.FindAsync(id);
            return item is not null ? Results.Ok(item) : Results.NotFound();
        });

        // POST create work item
        group.MapPost("/", async ([FromBody] CreateWorkItemRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title))
            {
                return Results.BadRequest("Title is required.");
            }

            var item = new WorkItem
            {
                Title = req.Title.Trim(),
                Description = req.Description,
                Priority = req.Priority,
                Status = WorkItemStatus.Todo,
                CreatedAt = DateTime.UtcNow,
                DueDate = req.DueDate
            };

            db.WorkItems.Add(item);
            await db.SaveChangesAsync();

            return Results.Created($"/api/workitems/{item.Id}", item);
        });

        // PUT update work item
        group.MapPut("/{id:int}", async (int id, [FromBody] UpdateWorkItemRequest req, AppDbContext db) =>
        {
            var item = await db.WorkItems.FindAsync(id);
            if (item is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(req.Title))
            {
                return Results.BadRequest("Title is required.");
            }

            item.Title = req.Title.Trim();
            item.Description = req.Description;
            item.Priority = req.Priority;
            item.Status = req.Status;
            item.DueDate = req.DueDate;

            await db.SaveChangesAsync();
            return Results.Ok(item);
        });

        // DELETE work item
        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var item = await db.WorkItems.FindAsync(id);
            if (item is null) return Results.NotFound();

            db.WorkItems.Remove(item);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        return routes;
    }
}
