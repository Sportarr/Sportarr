using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueMonitoringOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MonitorType",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SearchForCutoffUnmetEvents",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SearchForMissingEvents",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MonitorType",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "SearchForCutoffUnmetEvents",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "SearchForMissingEvents",
                table: "Leagues");
        }
    }
}
