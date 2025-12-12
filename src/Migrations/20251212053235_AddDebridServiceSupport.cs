using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDebridServiceSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseSymlinks",
                table: "MediaManagementSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FirstAndLastFirst",
                table: "DownloadClients",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SequentialDownload",
                table: "DownloadClients",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseSymlinks",
                table: "MediaManagementSettings");

            migrationBuilder.DropColumn(
                name: "FirstAndLastFirst",
                table: "DownloadClients");

            migrationBuilder.DropColumn(
                name: "SequentialDownload",
                table: "DownloadClients");
        }
    }
}
