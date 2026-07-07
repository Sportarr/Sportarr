using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBlackholeDownloadClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlackholeFolder",
                table: "DownloadClients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReadOnly",
                table: "DownloadClients",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SaveMagnetFiles",
                table: "DownloadClients",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WatchFolder",
                table: "DownloadClients",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlackholeFolder",
                table: "DownloadClients");

            migrationBuilder.DropColumn(
                name: "ReadOnly",
                table: "DownloadClients");

            migrationBuilder.DropColumn(
                name: "SaveMagnetFiles",
                table: "DownloadClients");

            migrationBuilder.DropColumn(
                name: "WatchFolder",
                table: "DownloadClients");
        }
    }
}
