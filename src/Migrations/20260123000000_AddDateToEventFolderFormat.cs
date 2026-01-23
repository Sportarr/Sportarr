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
            // Update EventFolderFormat to include the full date to prevent conflicts
            // when teams play each other multiple times in the same season.
            // Old format: '{Event Title}' → e.g., "Arsenal vs Chelsea"
            // New format: '{Event Title} ({Year}-{Month}-{Day})' → e.g., "Arsenal vs Chelsea (2025-03-15)"
            migrationBuilder.Sql(@"
                UPDATE MediaManagementSettings
                SET EventFolderFormat = '{Event Title} ({Year}-{Month}-{Day})'
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
                WHERE EventFolderFormat = '{Event Title} ({Year}-{Month}-{Day})';
            ");
        }
    }
}
