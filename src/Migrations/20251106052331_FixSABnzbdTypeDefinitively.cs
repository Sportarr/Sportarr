using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixSABnzbdTypeDefinitively : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix NZBGet clients (Type=6) that don't use port 6789 - they're actually SABnzbd
            // This corrects any clients that were incorrectly converted by the previous migration
            // NZBGet uses port 6789 by default, SABnzbd typically uses 8080, 8090, etc.
            migrationBuilder.Sql(@"
                UPDATE DownloadClients
                SET Type = 5
                WHERE Type = 6
                AND Port != 6789
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
