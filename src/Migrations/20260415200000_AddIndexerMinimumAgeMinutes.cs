using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexerMinimumAgeMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mirrors Sonarr's "Minimum Age" indexer setting. Default 0 means
            // existing users get the current behavior (no delay) without any
            // configuration on their end.
            migrationBuilder.AddColumn<int>(
                name: "IndexerMinimumAgeMinutes",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IndexerMinimumAgeMinutes",
                table: "AppSettings");
        }
    }
}
