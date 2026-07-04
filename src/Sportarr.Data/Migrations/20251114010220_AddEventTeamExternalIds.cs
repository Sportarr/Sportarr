using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEventTeamExternalIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AwayTeamExternalId",
                table: "Events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AwayTeamName",
                table: "Events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeTeamExternalId",
                table: "Events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeTeamName",
                table: "Events",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwayTeamExternalId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "AwayTeamName",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "HomeTeamExternalId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "HomeTeamName",
                table: "Events");
        }
    }
}
