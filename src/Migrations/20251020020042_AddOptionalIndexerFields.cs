using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOptionalIndexerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnimeCategories",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EarlyReleaseLimit",
                table: "Indexers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SeedRatio",
                table: "Indexers",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeedTime",
                table: "Indexers",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnimeCategories",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "EarlyReleaseLimit",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "SeedRatio",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "SeedTime",
                table: "Indexers");
        }
    }
}
