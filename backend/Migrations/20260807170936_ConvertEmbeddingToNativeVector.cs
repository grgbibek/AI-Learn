using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class ConvertEmbeddingToNativeVector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<SqlVector<float>>(
                name: "Embedding",
                table: "DocumentChunks",
                type: "vector(768)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Embedding",
                table: "DocumentChunks",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(SqlVector<float>),
                oldType: "vector(768)");
        }
    }
}
