using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialEventTeamModes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FinalTeamMode",
                table: "Leagues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlayoffTeamMode",
                table: "Leagues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreseasonTeamMode",
                table: "Leagues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE \"Leagues\" SET \"FinalTeamMode\" = 2 WHERE \"MonitorFinals\" = TRUE");
            migrationBuilder.Sql("UPDATE \"Leagues\" SET \"PlayoffTeamMode\" = 2 WHERE \"MonitorPlayoffs\" = TRUE");
            migrationBuilder.Sql("UPDATE \"Leagues\" SET \"PreseasonTeamMode\" = 2 WHERE \"MonitorPreseason\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FinalTeamMode",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "PlayoffTeamMode",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "PreseasonTeamMode",
                table: "Leagues");
        }
    }
}
