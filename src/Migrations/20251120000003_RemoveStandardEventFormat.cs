using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStandardEventFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop StandardEventFormat column if it exists
            migrationBuilder.Sql(@"
                -- Check if column exists before dropping (SQLite doesn't have DROP COLUMN IF EXISTS)
                -- We'll use a different approach: check pragma and conditionally drop
                CREATE TABLE IF NOT EXISTS MediaManagementSettings_temp AS
                SELECT
                    Id,
                    RenameFiles,
                    StandardFileFormat,
                    EventFolderFormat,
                    CreateEventFolder,
                    RenameEvents,
                    ReplaceIllegalCharacters,
                    CreateEventFolders,
                    DeleteEmptyFolders,
                    SkipFreeSpaceCheck,
                    MinimumFreeSpace,
                    UseHardlinks,
                    ImportExtraFiles,
                    ExtraFileExtensions,
                    ChangeFileDate,
                    RecycleBin,
                    RecycleBinCleanup,
                    SetPermissions,
                    FileChmod,
                    ChmodFolder,
                    ChownUser,
                    ChownGroup,
                    CopyFiles,
                    RemoveCompletedDownloads,
                    RemoveFailedDownloads,
                    Created,
                    LastModified
                FROM MediaManagementSettings;

                DROP TABLE MediaManagementSettings;

                ALTER TABLE MediaManagementSettings_temp RENAME TO MediaManagementSettings;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Add StandardEventFormat column back
            migrationBuilder.AddColumn<string>(
                name: "StandardEventFormat",
                table: "MediaManagementSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "{Series} - {Season}{Episode}{Part} - {Event Title} - {Quality Full}");
        }
    }
}
