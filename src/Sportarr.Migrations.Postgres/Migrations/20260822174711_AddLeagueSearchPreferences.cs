using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueSearchPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AliasSearchOrder",
                table: "Leagues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SearchEarlyStopMatchScoreOverride",
                table: "Leagues",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAliases",
                table: "Leagues",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AliasSearchOrder",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "SearchEarlyStopMatchScoreOverride",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "UserAliases",
                table: "Leagues");
        }
    }
}
