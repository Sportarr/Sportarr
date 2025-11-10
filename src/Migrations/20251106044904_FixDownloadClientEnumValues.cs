using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixDownloadClientEnumValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix SABnzbd clients that were saved with Type=4 (should be 5)
            // This bug occurred because the frontend was missing UTorrent in the enum mapping
            // Since UTorrent was never available in templates, all Type=4 clients are SABnzbd
            migrationBuilder.Sql(@"
                UPDATE DownloadClients
                SET Type = 5
                WHERE Type = 4
            ");

            // Fix NZBGet clients that were saved with Type=5 (should be 6)
            // Only update if ApiKey is NULL (NZBGet uses username/password, SABnzbd uses apiKey)
            migrationBuilder.Sql(@"
                UPDATE DownloadClients
                SET Type = 6
                WHERE Type = 5
                AND ApiKey IS NULL
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
