using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenCostBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DailyAiTokenLimit",
                table: "AppUsers",
                type: "int",
                nullable: false,
                defaultValue: 100000);

            migrationBuilder.Sql("UPDATE [AppUsers] SET [DailyAiTokenLimit] = 500000 WHERE [Role] = N'Admin'");

            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "AiUsageLogs",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedInputTokens",
                table: "AiUsageLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedOutputTokens",
                table: "AiUsageLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedTotalTokens",
                table: "AiUsageLogs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ModelName",
                table: "AiUsageLogs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderName",
                table: "AiUsageLogs",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyAiTokenLimit",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "EstimatedCostUsd",
                table: "AiUsageLogs");

            migrationBuilder.DropColumn(
                name: "EstimatedInputTokens",
                table: "AiUsageLogs");

            migrationBuilder.DropColumn(
                name: "EstimatedOutputTokens",
                table: "AiUsageLogs");

            migrationBuilder.DropColumn(
                name: "EstimatedTotalTokens",
                table: "AiUsageLogs");

            migrationBuilder.DropColumn(
                name: "ModelName",
                table: "AiUsageLogs");

            migrationBuilder.DropColumn(
                name: "ProviderName",
                table: "AiUsageLogs");
        }
    }
}
