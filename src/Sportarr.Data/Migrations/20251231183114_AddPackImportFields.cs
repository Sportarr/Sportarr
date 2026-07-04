using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddPackImportFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FileCount",
                table: "PendingImports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsPack",
                table: "PendingImports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MatchedEventsCount",
                table: "PendingImports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileCount",
                table: "PendingImports");

            migrationBuilder.DropColumn(
                name: "IsPack",
                table: "PendingImports");

            migrationBuilder.DropColumn(
                name: "MatchedEventsCount",
                table: "PendingImports");
        }
    }
}
