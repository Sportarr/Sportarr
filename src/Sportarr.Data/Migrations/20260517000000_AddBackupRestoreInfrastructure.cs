using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupRestoreInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // RestoreReports table captures the state of each restore + the
            // reconciliation pass that runs immediately after it. The admin UI
            // reads this table to surface "what happened to my files" without
            // having to dig through the disk-scan logs. Path remap operations
            // applied during the same restore session are logged into
            // PathRemapsJson so the user can see/undo what was rewritten.
            migrationBuilder.CreateTable(
                name: "RestoreReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BackupFileName = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<System.DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<System.DateTime>(type: "TEXT", nullable: true),
                    // Reconciliation counts. All four resolve to integer columns
                    // even when the disk scan was skipped (in which case they're
                    // 0). The total of Found + Missing + SkippedUnreachable should
                    // equal TotalEventFiles when the scan completed.
                    TotalEventFiles = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    FilesFound = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    FilesMissing = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    FilesSkippedUnreachableRoot = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    // Manifest captured from the backup zip's manifest.json if
                    // the backup was produced by a sportarr that wrote one;
                    // older backups land here as null and the reconciliation
                    // proceeds without a source-of-truth comparison.
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: true),
                    // Path remaps applied during this restore session. JSON
                    // array of { from, to, affected_rows } objects so the admin
                    // can audit and revert.
                    PathRemapsJson = table.Column<string>(type: "TEXT", nullable: true),
                    // Notes / errors captured during the run.
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    // Source host (from manifest) so the user can tell at a
                    // glance whether the backup came from a different machine,
                    // which is the common cause of path drift.
                    SourceHost = table.Column<string>(type: "TEXT", nullable: true),
                    SourceSportarrVersion = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RestoreReports_StartedAt",
                table: "RestoreReports",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RestoreReports");
        }
    }
}
