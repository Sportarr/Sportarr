using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDvrProfileEstimatedScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EstimatedCustomFormatScore",
                table: "DvrQualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedQualityScore",
                table: "DvrQualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedFormatDescription",
                table: "DvrQualityProfiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedQualityName",
                table: "DvrQualityProfiles",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedCustomFormatScore",
                table: "DvrQualityProfiles");

            migrationBuilder.DropColumn(
                name: "EstimatedQualityScore",
                table: "DvrQualityProfiles");

            migrationBuilder.DropColumn(
                name: "ExpectedFormatDescription",
                table: "DvrQualityProfiles");

            migrationBuilder.DropColumn(
                name: "ExpectedQualityName",
                table: "DvrQualityProfiles");
        }
    }
}
