using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRootFolderDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Per-root-folder defaults the user can pin under Media
            // Management. DefaultQualityProfileId is consulted by the Add
            // League modal and POST /api/leagues — when the user picks a
            // root and hasn't manually picked a profile, the league
            // inherits the root's pin. DefaultDownloadClientCategory is
            // consulted at grab time so leagues under "fast SSD" can
            // route through one client category and "archive HDD" another.
            // Both nullable: empty means "no opinion, use the global
            // default" so existing setups carry over unchanged.
            migrationBuilder.AddColumn<int>(
                name: "DefaultQualityProfileId",
                table: "RootFolders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultDownloadClientCategory",
                table: "RootFolders",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DefaultDownloadClientCategory", table: "RootFolders");
            migrationBuilder.DropColumn(name: "DefaultQualityProfileId", table: "RootFolders");
        }
    }
}
