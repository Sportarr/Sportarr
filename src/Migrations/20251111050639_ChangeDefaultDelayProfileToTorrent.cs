using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class ChangeDefaultDelayProfileToTorrent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Update the default delay profile to use Torrent protocol instead of Usenet
            // Torrent is more commonly used for sports content and doesn't require indexer credentials
            migrationBuilder.Sql(@"
                UPDATE DelayProfiles
                SET PreferredProtocol = 'Torrent'
                WHERE Id = 1 AND ""Order"" = 1
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert back to Usenet
            migrationBuilder.Sql(@"
                UPDATE DelayProfiles
                SET PreferredProtocol = 'Usenet'
                WHERE Id = 1 AND ""Order"" = 1
            ");
        }
    }
}
