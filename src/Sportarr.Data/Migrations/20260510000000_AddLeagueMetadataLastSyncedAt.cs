using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueMetadataLastSyncedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Timestamp the auto-sync pipeline uses to decide whether a
            // league's metadata (AlternateName, LogoUrl, Description,
            // Website, etc.) is due for a re-pull from upstream. Null
            // means it's never been refreshed since the column landed
            // — first auto-sync cycle will force-refresh those rows
            // and stamp them.
            migrationBuilder.AddColumn<System.DateTime>(
                name: "MetadataLastSyncedAt",
                table: "Leagues",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MetadataLastSyncedAt", table: "Leagues");
        }
    }
}
