using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCatchupSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Catchup/timeshift recording support.
            //
            // Xtream providers expose per-channel archive flags
            // (tv_archive / tv_archive_duration) that the client already
            // parsed but never persisted. Storing them lets the scheduler
            // prefer downloading a finished event from the provider's
            // archive over capturing it live: no start/end guessing, no
            // missed recordings during app downtime, and failures are
            // retryable for as long as the archive retains the window.
            //
            // DvrRecordings.Method tags each row as Live (0, the previous
            // implicit behavior) or Catchup (1). The live scheduler and
            // watchdog filter on it: their wall-clock rules (missed-window
            // failure, overrun stop) assume a live recording window and
            // would instantly kill catchup rows, whose window is always in
            // the past by design.
            //
            // Catchup/timeshift download method ported from timeshifter by
            // scottrobertson (github.com/scottrobertson/timeshifter).

            migrationBuilder.AddColumn<bool>(
                name: "HasArchive",
                table: "IptvChannels",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ArchiveDays",
                table: "IptvChannels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Method",
                table: "DvrRecordings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Which timeshift URL style the provider's catchup endpoint
            // actually serves ("path" or "php"), learned from the first
            // successful download so auto mode stops probing. Nullable:
            // null means not yet detected.
            migrationBuilder.AddColumn<string>(
                name: "DetectedCatchupMode",
                table: "IptvSources",
                type: "TEXT",
                nullable: true);

            // The catchup downloader polls for "Scheduled catchup rows
            // whose window has closed" on every tick; the live scheduler
            // polls the complementary set. Indexing Method keeps both
            // filters cheap as recording history grows.
            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_Method",
                table: "DvrRecordings",
                column: "Method");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_DvrRecordings_Method", table: "DvrRecordings");
            migrationBuilder.DropColumn(name: "DetectedCatchupMode", table: "IptvSources");
            migrationBuilder.DropColumn(name: "Method", table: "DvrRecordings");
            migrationBuilder.DropColumn(name: "ArchiveDays", table: "IptvChannels");
            migrationBuilder.DropColumn(name: "HasArchive", table: "IptvChannels");
        }
    }
}
