using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentChunks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ChunkIndex = table.Column<int>(type: "int", nullable: false),
                    Embedding = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentChunks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItems", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "WorkItems",
                columns: new[] { "Id", "CreatedAt", "Description", "DueDate", "Priority", "Status", "Title" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Verify development environment, install packages, and configure CORS policy.", new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc), 2, 2, "Set up .NET 10 & Angular 19 Environment" },
                    { 2, new DateTime(2026, 8, 4, 0, 0, 0, 0, DateTimeKind.Utc), "Refactor components to use signal(), computed(), and httpResource pattern.", new DateTime(2026, 8, 7, 0, 0, 0, 0, DateTimeKind.Utc), 3, 1, "Implement Angular Signal State Management" },
                    { 3, new DateTime(2026, 8, 5, 0, 0, 0, 0, DateTimeKind.Utc), "Integrate structured logging into Minimal APIs for production observability.", new DateTime(2026, 8, 10, 0, 0, 0, 0, DateTimeKind.Utc), 1, 0, "Add OpenTelemetry Logging & Tracing" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentChunks");

            migrationBuilder.DropTable(
                name: "WorkItems");
        }
    }
}
