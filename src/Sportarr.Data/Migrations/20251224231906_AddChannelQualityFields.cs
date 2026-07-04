using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelQualityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetectedNetwork",
                table: "IptvChannels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedQuality",
                table: "IptvChannels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QualityScore",
                table: "IptvChannels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetectedNetwork",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "DetectedQuality",
                table: "IptvChannels");

            migrationBuilder.DropColumn(
                name: "QualityScore",
                table: "IptvChannels");
        }
    }
}
