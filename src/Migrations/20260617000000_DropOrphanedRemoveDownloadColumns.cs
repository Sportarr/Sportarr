using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <summary>
    /// Drops the orphaned RemoveCompletedDownloads / RemoveFailedDownloads columns
    /// from MediaManagementSettings.
    ///
    /// These settings were moved to per-download-client configuration (see the
    /// MediaManagementSettings model — the properties were removed and replaced with
    /// a "now per-client" note), and the model snapshot was updated to match. But no
    /// migration ever dropped the underlying columns. On databases created before the
    /// move, the two columns linger as NOT NULL with no default. Because EF's INSERT
    /// no longer includes them (the entity stopped mapping them), saving Media
    /// Management settings — and the import pipeline's GetMediaManagementSettingsAsync
    /// get-or-create — fails with:
    ///
    ///   SQLite Error 19: 'NOT NULL constraint failed: MediaManagementSettings.RemoveCompletedDownloads'
    ///
    /// which blocks both the Settings page save and completed-download imports.
    ///
    /// The actual drop is performed by the raw-SQL safety net in DatabaseInitializer
    /// (guarded by pragma_table_info checks), matching how recent schema changes are
    /// applied in this project; this migration documents the change.
    /// </summary>
    public partial class DropOrphanedRemoveDownloadColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoveCompletedDownloads",
                table: "MediaManagementSettings");

            migrationBuilder.DropColumn(
                name: "RemoveFailedDownloads",
                table: "MediaManagementSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RemoveCompletedDownloads",
                table: "MediaManagementSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RemoveFailedDownloads",
                table: "MediaManagementSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
