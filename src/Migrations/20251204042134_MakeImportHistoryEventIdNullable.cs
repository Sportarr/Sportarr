using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakeImportHistoryEventIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite doesn't support altering foreign keys directly.
            // Instead of using DropForeignKey/AddForeignKey (which generates PRAGMA foreign_keys warnings),
            // we rebuild the table with the new schema.

            // Step 1: Create new table with nullable EventId and new FK constraints
            migrationBuilder.Sql(@"
                CREATE TABLE ""ImportHistories_new"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ImportHistories"" PRIMARY KEY AUTOINCREMENT,
                    ""EventId"" INTEGER NULL,
                    ""DownloadQueueItemId"" INTEGER NULL,
                    ""SourcePath"" TEXT NOT NULL,
                    ""DestinationPath"" TEXT NOT NULL,
                    ""Quality"" TEXT NOT NULL,
                    ""Size"" INTEGER NOT NULL,
                    ""Decision"" INTEGER NOT NULL,
                    ""Warnings"" TEXT NOT NULL,
                    ""Errors"" TEXT NOT NULL,
                    ""ImportedAt"" TEXT NOT NULL,
                    CONSTRAINT ""FK_ImportHistories_Events_EventId"" FOREIGN KEY (""EventId"") REFERENCES ""Events"" (""Id"") ON DELETE SET NULL,
                    CONSTRAINT ""FK_ImportHistories_DownloadQueue_DownloadQueueItemId"" FOREIGN KEY (""DownloadQueueItemId"") REFERENCES ""DownloadQueue"" (""Id"") ON DELETE SET NULL
                );
            ");

            // Step 2: Copy data from old table
            migrationBuilder.Sql(@"
                INSERT INTO ""ImportHistories_new"" (""Id"", ""EventId"", ""DownloadQueueItemId"", ""SourcePath"", ""DestinationPath"", ""Quality"", ""Size"", ""Decision"", ""Warnings"", ""Errors"", ""ImportedAt"")
                SELECT ""Id"", ""EventId"", ""DownloadQueueItemId"", ""SourcePath"", ""DestinationPath"", ""Quality"", ""Size"", ""Decision"", ""Warnings"", ""Errors"", ""ImportedAt""
                FROM ""ImportHistories"";
            ");

            // Step 3: Drop old table
            migrationBuilder.Sql(@"DROP TABLE ""ImportHistories"";");

            // Step 4: Rename new table
            migrationBuilder.Sql(@"ALTER TABLE ""ImportHistories_new"" RENAME TO ""ImportHistories"";");

            // Step 5: Recreate indexes
            migrationBuilder.Sql(@"CREATE INDEX ""IX_ImportHistories_EventId"" ON ""ImportHistories"" (""EventId"");");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_ImportHistories_DownloadQueueItemId"" ON ""ImportHistories"" (""DownloadQueueItemId"");");

            // Add StatusMessages column to DownloadQueue
            migrationBuilder.AddColumn<string>(
                name: "StatusMessages",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusMessages",
                table: "DownloadQueue");

            // Rebuild table with original schema (non-nullable EventId, CASCADE delete)
            migrationBuilder.Sql(@"
                CREATE TABLE ""ImportHistories_new"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ImportHistories"" PRIMARY KEY AUTOINCREMENT,
                    ""EventId"" INTEGER NOT NULL DEFAULT 0,
                    ""DownloadQueueItemId"" INTEGER NULL,
                    ""SourcePath"" TEXT NOT NULL,
                    ""DestinationPath"" TEXT NOT NULL,
                    ""Quality"" TEXT NOT NULL,
                    ""Size"" INTEGER NOT NULL,
                    ""Decision"" INTEGER NOT NULL,
                    ""Warnings"" TEXT NOT NULL,
                    ""Errors"" TEXT NOT NULL,
                    ""ImportedAt"" TEXT NOT NULL,
                    CONSTRAINT ""FK_ImportHistories_Events_EventId"" FOREIGN KEY (""EventId"") REFERENCES ""Events"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_ImportHistories_DownloadQueue_DownloadQueueItemId"" FOREIGN KEY (""DownloadQueueItemId"") REFERENCES ""DownloadQueue"" (""Id"")
                );
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""ImportHistories_new"" (""Id"", ""EventId"", ""DownloadQueueItemId"", ""SourcePath"", ""DestinationPath"", ""Quality"", ""Size"", ""Decision"", ""Warnings"", ""Errors"", ""ImportedAt"")
                SELECT ""Id"", COALESCE(""EventId"", 0), ""DownloadQueueItemId"", ""SourcePath"", ""DestinationPath"", ""Quality"", ""Size"", ""Decision"", ""Warnings"", ""Errors"", ""ImportedAt""
                FROM ""ImportHistories"";
            ");

            migrationBuilder.Sql(@"DROP TABLE ""ImportHistories"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ImportHistories_new"" RENAME TO ""ImportHistories"";");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_ImportHistories_EventId"" ON ""ImportHistories"" (""EventId"");");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_ImportHistories_DownloadQueueItemId"" ON ""ImportHistories"" (""DownloadQueueItemId"");");
        }
    }
}
