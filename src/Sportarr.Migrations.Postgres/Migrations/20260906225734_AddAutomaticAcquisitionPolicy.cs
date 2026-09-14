using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticAcquisitionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(name: "IsPack", table: "PendingReleases", type: "boolean", nullable: true);
            migrationBuilder.AddColumn<bool>(
                name: "AutomaticMissingEnabled",
                table: "Leagues",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "AutomaticMissingMaxAgeDays",
                table: "Leagues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AutomaticUpgradeMaxAgeDays",
                table: "Leagues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AutomaticUpgradesEnabled",
                table: "Leagues",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutomatic",
                table: "DvrRecordings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsPack", table: "PendingReleases");
            migrationBuilder.DropColumn(
                name: "AutomaticMissingEnabled",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "AutomaticMissingMaxAgeDays",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "AutomaticUpgradeMaxAgeDays",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "AutomaticUpgradesEnabled",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "IsAutomatic",
                table: "DvrRecordings");
        }
    }
}
