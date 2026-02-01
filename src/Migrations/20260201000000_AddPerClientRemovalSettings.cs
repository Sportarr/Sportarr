using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPerClientRemovalSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RemoveCompletedDownloads",
                table: "DownloadClients",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // Default ON for backwards compatibility

            migrationBuilder.AddColumn<bool>(
                name: "RemoveFailedDownloads",
                table: "DownloadClients",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoveCompletedDownloads",
                table: "DownloadClients");

            migrationBuilder.DropColumn(
                name: "RemoveFailedDownloads",
                table: "DownloadClients");
        }
    }
}
