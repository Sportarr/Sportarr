using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
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
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlayoffTeamMode",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PreseasonTeamMode",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // The backfill from MonitorFinals / MonitorPlayoffs / MonitorPreseason
            // runs in DatabaseInitializer once this migration has been applied.
            // On SQLite those columns are added by the initializer, not by a
            // migration, so on a fresh database they don't exist yet at this
            // point. Older SQLite builds read an unknown "MonitorFinals" as a
            // string literal and skipped the UPDATE without complaint. The build
            // that ships with EF Core 10 disables that, and the UPDATE fails.
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
