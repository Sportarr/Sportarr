using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEventFileMissingSince : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tracks when a file first transitioned from existing to missing on
            // disk. The disk scanner reads this against Config.EventFileMissingDeleteAfterDays
            // to decide whether to hard-delete the row, so the column must be
            // populated as part of the existence-flip transition rather than
            // backfilled. New rows default to NULL (file is currently present).
            migrationBuilder.AddColumn<DateTime>(
                name: "MissingSince",
                table: "EventFiles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MissingSince", table: "EventFiles");
        }
    }
}
