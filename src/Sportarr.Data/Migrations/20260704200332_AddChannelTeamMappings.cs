using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <summary>
    /// Adds the ChannelTeamMappings table (per-team DVR channel preference,
    /// checked by the resolver before the league mapping).
    ///
    /// This migration was originally scaffolded with `dotnet ef migrations add`
    /// against a model snapshot that had not been regenerated since long before
    /// the recent hand-written migrations, so EF emitted dozens of unrelated
    /// catch-up operations (index drops, column adds, re-creations of tables
    /// that earlier migrations and the DatabaseInitializer safety nets already
    /// create). Those operations failed on real databases — legacy
    /// EnsureCreated()-era installs don't have some of the indexes being
    /// dropped, and fresh installs already have the tables being created — so
    /// the body was rewritten to contain only the genuinely new schema, as
    /// guarded raw SQL mirroring the DatabaseInitializer safety net exactly.
    /// The regenerated snapshot (which now matches the full current model) was
    /// intentionally kept, so future scaffolded migrations diff cleanly.
    /// </summary>
    public partial class AddChannelTeamMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""ChannelTeamMappings"" (
                    ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""ChannelId"" INTEGER NOT NULL,
                    ""TeamId"" INTEGER NOT NULL,
                    ""IsPreferred"" INTEGER NOT NULL DEFAULT 1,
                    ""Priority"" INTEGER NOT NULL DEFAULT 1,
                    ""Created"" TEXT NOT NULL,
                    CONSTRAINT ""FK_ChannelTeamMappings_IptvChannels_ChannelId"" FOREIGN KEY (""ChannelId"") REFERENCES ""IptvChannels"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_ChannelTeamMappings_Teams_TeamId"" FOREIGN KEY (""TeamId"") REFERENCES ""Teams"" (""Id"") ON DELETE CASCADE
                )");

            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ChannelTeamMappings_ChannelId_TeamId"" ON ""ChannelTeamMappings"" (""ChannelId"", ""TeamId"")");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""UX_ChannelTeamMappings_PreferredPerTeam"" ON ""ChannelTeamMappings"" (""TeamId"") WHERE ""IsPreferred"" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""ChannelTeamMappings""");
        }
    }
}
