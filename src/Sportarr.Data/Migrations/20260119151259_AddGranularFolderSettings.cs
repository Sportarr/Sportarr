using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddGranularFolderSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add new columns with proper defaults for existing installations
            // CreateLeagueFolders and CreateSeasonFolders default to true to maintain existing behavior
            migrationBuilder.AddColumn<bool>(
                name: "CreateLeagueFolders",
                table: "MediaManagementSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CreateSeasonFolders",
                table: "MediaManagementSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "LeagueFolderFormat",
                table: "MediaManagementSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "{Series}");

            migrationBuilder.AddColumn<string>(
                name: "SeasonFolderFormat",
                table: "MediaManagementSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "Season {Season}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreateLeagueFolders",
                table: "MediaManagementSettings");

            migrationBuilder.DropColumn(
                name: "CreateSeasonFolders",
                table: "MediaManagementSettings");

            migrationBuilder.DropColumn(
                name: "LeagueFolderFormat",
                table: "MediaManagementSettings");

            migrationBuilder.DropColumn(
                name: "SeasonFolderFormat",
                table: "MediaManagementSettings");
        }
    }
}
