using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class FixEventFolderFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fix EventFolderFormat for existing users who have the old incorrect default
            // The old default was '{Series}/Season {Season}' which produces folder names like
            // "English Premier League Season 2025" instead of the actual event title
            //
            // The correct format should be '{Event Title}' which uses the event name
            // (e.g., "Arsenal vs Chelsea" for team sports, "Valencia GP" for motorsport)
            migrationBuilder.Sql(@"
                UPDATE MediaManagementSettings
                SET EventFolderFormat = '{Event Title}'
                WHERE EventFolderFormat = '{Series}/Season {Season}'
                   OR EventFolderFormat LIKE '%Season {Season}%';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert to old format (not recommended)
            migrationBuilder.Sql(@"
                UPDATE MediaManagementSettings
                SET EventFolderFormat = '{Series}/Season {Season}'
                WHERE EventFolderFormat = '{Event Title}';
            ");
        }
    }
}
