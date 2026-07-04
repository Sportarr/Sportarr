using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexerOptionsSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IndexerRetention",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RssSyncInterval",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<bool>(
                name: "PreferIndexerFlags",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "SearchCacheDuration",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 120);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IndexerRetention",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RssSyncInterval",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "PreferIndexerFlags",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "SearchCacheDuration",
                table: "AppSettings");
        }
    }
}
