using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMediaManagementDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Update existing media management settings to use new Plex/Sonarr-style format
            migrationBuilder.Sql(@"
                UPDATE MediaManagementSettings
                SET EventFolderFormat = '{Series}/Season {Season}',
                    StandardFileFormat = '{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}'
                WHERE EventFolderFormat = '{Event Title}'
                   OR EventFolderFormat = '{League}/{Event Title}'
                   OR StandardFileFormat = '{Event Title} - {Air Date} - {Quality Full}'
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert to old format
            migrationBuilder.Sql(@"
                UPDATE MediaManagementSettings
                SET EventFolderFormat = '{Event Title}',
                    StandardFileFormat = '{Event Title} - {Air Date} - {Quality Full}'
                WHERE EventFolderFormat = '{Series}/Season {Season}'
            ");
        }
    }
}
