using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddDateToEventFolderFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Update EventFolderFormat to include the full date and episode number to prevent conflicts
            // when teams play each other multiple times in the same season (including double headers).
            // Old format: '{Event Title}' → e.g., "Arsenal vs Chelsea"
            // New format: '{Event Title} ({Year}-{Month}-{Day}) E{Episode}' → e.g., "Arsenal vs Chelsea (2025-03-15) E23"
            // The episode number ensures uniqueness even for double headers on the same day.
            migrationBuilder.Sql(@"
                UPDATE MediaManagementSettings
                SET EventFolderFormat = '{Event Title} ({Year}-{Month}-{Day}) E{Episode}'
                WHERE EventFolderFormat = '{Event Title}';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert to old format without date
            migrationBuilder.Sql(@"
                UPDATE MediaManagementSettings
                SET EventFolderFormat = '{Event Title}'
                WHERE EventFolderFormat = '{Event Title} ({Year}-{Month}-{Day}) E{Episode}';
            ");
        }
    }
}
