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
            migrationBuilder.Sql(@"
                UPDATE DownloadClients
                SET Type = 5
                WHERE Type = 4
            ");

            // Fix Type=5 clients to Type=6 (NZBGet) IF they don't use API keys
            // SABnzbd uses ApiKey, NZBGet uses Username/Password
            // This distinguishes between the two usenet clients
            migrationBuilder.Sql(@"
                UPDATE DownloadClients
                SET Type = 6
                WHERE Type = 5
                AND (ApiKey IS NULL OR ApiKey = '')
                AND (Username IS NOT NULL AND Username != '')
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
