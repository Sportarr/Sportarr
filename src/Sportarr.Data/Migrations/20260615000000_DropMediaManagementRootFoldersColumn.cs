using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <summary>
    /// Removes the denormalized RootFolders JSON column from MediaManagementSettings.
    /// Root folders are stored solely in the RootFolders table (the source of
    /// truth the UI writes to); the JSON copy was a cache that only synced as a
    /// side effect of a successful import, so it went stale right after a folder
    /// was added and produced false "No root folders configured" errors.
    ///
    /// The actual drop is performed by the raw-SQL safety net in
    /// DatabaseInitializer (guarded by a pragma_table_info check), matching how
    /// recent schema changes are applied in this project; this migration documents
    /// the change.
    /// </summary>
    public partial class DropMediaManagementRootFoldersColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RootFolders",
                table: "MediaManagementSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RootFolders",
                table: "MediaManagementSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }
    }
}
