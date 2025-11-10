using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityProfileUpgradesAndIncrement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FormatScoreIncrement",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "UpgradesAllowed",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "FormatScoreIncrement", "UpgradesAllowed" },
                values: new object[] { 1, true });

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "FormatScoreIncrement", "UpgradesAllowed" },
                values: new object[] { 1, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FormatScoreIncrement",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "UpgradesAllowed",
                table: "QualityProfiles");
        }
    }
}
