using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFormatScoreToFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomFormatScore",
                table: "EventFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CustomFormatScore",
                table: "DownloadQueue",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QualityScore",
                table: "DownloadQueue",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomFormatScore",
                table: "EventFiles");

            migrationBuilder.DropColumn(
                name: "CustomFormatScore",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "QualityScore",
                table: "DownloadQueue");
        }
    }
}
