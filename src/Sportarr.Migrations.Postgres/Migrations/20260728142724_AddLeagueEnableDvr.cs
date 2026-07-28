using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueEnableDvr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill default is TRUE - DVR scheduling stays on for every
            // existing league; the toggle is an explicit opt-out.
            migrationBuilder.AddColumn<bool>(
                name: "EnableDvr",
                table: "Leagues",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableDvr",
                table: "Leagues");
        }
    }
}
