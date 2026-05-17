using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddScoredMappingsAndDvrFallback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 1: scored channel-league mappings + admin override learning loop.
            //
            // The old auto-mapper produced binary (mapped / not-mapped) rows
            // from name-keyword guessing alone. With multiple signals stacking
            // (tvg-id, iptv-org, EPG programming, country match, network
            // keywords) we want a 0-100 confidence so downstream consumers
            // (event resolver, DVR scheduler) can prefer high-confidence
            // mappings over weak ones. IsManual sticks across auto-map
            // re-runs so admin overrides aren't clobbered. MappingSignals
            // carries the JSON-encoded list of contributing signals so the
            // "Why is this mapped?" explain endpoint can show its work.

            migrationBuilder.AddColumn<int>(
                name: "Confidence",
                table: "ChannelLeagueMappings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsManual",
                table: "ChannelLeagueMappings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MappingSignals",
                table: "ChannelLeagueMappings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<System.DateTime>(
                name: "LastAutoMapped",
                table: "ChannelLeagueMappings",
                type: "TEXT",
                nullable: true);

            // Phase 3: DVR fallback channels + auto-retry counter.
            //
            // When a recording fails partway through (stream drops, channel
            // 404s, decoder errors), the scheduler can re-schedule on the
            // next-best channel from the event-resolver's candidate list.
            // FallbackChannelIds is the JSON-encoded ranked backup list
            // captured at scheduling time so we don't have to re-resolve
            // candidates under failure pressure. AutoRetryCount stops the
            // fallback loop from rotating indefinitely if every backup
            // also fails.

            migrationBuilder.AddColumn<string>(
                name: "FallbackChannelIds",
                table: "DvrRecordings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AutoRetryCount",
                table: "DvrRecordings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Index Confidence so explain / coverage reports can sort by it
            // quickly. Index IsManual so the auto-mapper's "skip rows the
            // admin already locked" query stays fast on large channel sets.
            migrationBuilder.CreateIndex(
                name: "IX_ChannelLeagueMappings_Confidence",
                table: "ChannelLeagueMappings",
                column: "Confidence");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelLeagueMappings_IsManual",
                table: "ChannelLeagueMappings",
                column: "IsManual");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_ChannelLeagueMappings_IsManual", table: "ChannelLeagueMappings");
            migrationBuilder.DropIndex(name: "IX_ChannelLeagueMappings_Confidence", table: "ChannelLeagueMappings");
            migrationBuilder.DropColumn(name: "AutoRetryCount", table: "DvrRecordings");
            migrationBuilder.DropColumn(name: "FallbackChannelIds", table: "DvrRecordings");
            migrationBuilder.DropColumn(name: "LastAutoMapped", table: "ChannelLeagueMappings");
            migrationBuilder.DropColumn(name: "MappingSignals", table: "ChannelLeagueMappings");
            migrationBuilder.DropColumn(name: "IsManual", table: "ChannelLeagueMappings");
            migrationBuilder.DropColumn(name: "Confidence", table: "ChannelLeagueMappings");
        }
    }
}
