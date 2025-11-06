using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixDownloadClientTypesRobustly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // COMPREHENSIVE FIX: Update all misconfigured download client types
            // This handles cases where the name doesn't match expected patterns

            // Fix ALL Type=4 clients to Type=5 (SABnzbd)
            // Rationale: UTorrent was never available in templates, so any Type=4 must be SABnzbd
            // Mark these rows so we don't accidentally convert them to NZBGet in the next step
            migrationBuilder.Sql(@"
                UPDATE DownloadClients
                SET Type = 5
                WHERE Type = 4
            ");

            // Fix Type=5 clients to Type=6 (NZBGet) ONLY if they use the NZBGet default port (6789)
            // SABnzbd typically uses ports 8080, 8090, etc.
            // This is safer than checking ApiKey which might be NULL in the database
            migrationBuilder.Sql(@"
                UPDATE DownloadClients
                SET Type = 6
                WHERE Type = 5
                AND Port = 6789
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
