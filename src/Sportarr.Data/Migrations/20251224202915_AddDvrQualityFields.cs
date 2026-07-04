using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDvrQualityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AudioChannels",
                table: "DvrRecordings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AudioCodec",
                table: "DvrRecordings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomFormatScore",
                table: "DvrRecordings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualityScore",
                table: "DvrRecordings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoCodec",
                table: "DvrRecordings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoHeight",
                table: "DvrRecordings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoWidth",
                table: "DvrRecordings",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioChannels",
                table: "DvrRecordings");

            migrationBuilder.DropColumn(
                name: "AudioCodec",
                table: "DvrRecordings");

            migrationBuilder.DropColumn(
                name: "CustomFormatScore",
                table: "DvrRecordings");

            migrationBuilder.DropColumn(
                name: "QualityScore",
                table: "DvrRecordings");

            migrationBuilder.DropColumn(
                name: "VideoCodec",
                table: "DvrRecordings");

            migrationBuilder.DropColumn(
                name: "VideoHeight",
                table: "DvrRecordings");

            migrationBuilder.DropColumn(
                name: "VideoWidth",
                table: "DvrRecordings");
        }
    }
}
