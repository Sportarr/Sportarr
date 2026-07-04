using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferredChannelPerLeagueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Filtered unique index: at most one preferred channel per
            // league. Concurrent writes to /api/iptv/leagues/{id}/preferred-channel
            // could otherwise leave duplicate preferred rows that the
            // event-DVR scheduler would resolve non-deterministically.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""UX_ChannelLeagueMappings_PreferredPerLeague""
                ON ""ChannelLeagueMappings"" (""LeagueId"")
                WHERE ""IsPreferred"" = 1;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""UX_ChannelLeagueMappings_PreferredPerLeague"";");
        }
    }
}
