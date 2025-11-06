using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixDownloadClientEnumValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix SABnzbd clients that were saved with Type=4 (should be 5)
            // This bug occurred because the frontend was missing UTorrent in the enum mapping
            migrationBuilder.Sql(@"
                UPDATE DownloadClients
                SET Type = 5
                WHERE Type = 4
                AND (Name LIKE '%SAB%' OR Name LIKE '%sabnzbd%')
            ");

            // Fix NZBGet clients that were saved with Type=5 (should be 6)
            migrationBuilder.Sql(@"
                UPDATE DownloadClients
                SET Type = 6
                WHERE Type = 5
                AND (Name LIKE '%NZB%' OR Name LIKE '%nzbget%')
                AND NOT (Name LIKE '%SAB%')
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
