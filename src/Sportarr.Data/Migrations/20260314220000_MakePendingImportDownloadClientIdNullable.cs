using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakePendingImportDownloadClientIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite doesn't support ALTER COLUMN, so we rebuild the table.
            // DownloadClientId changes from NOT NULL to NULL for disk-discovered files.

            // Step 1: Create new table with nullable DownloadClientId
            migrationBuilder.Sql(@"
                CREATE TABLE ""PendingImports_new"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PendingImports"" PRIMARY KEY AUTOINCREMENT,
                    ""DownloadClientId"" INTEGER NULL,
                    ""DownloadId"" TEXT NOT NULL,
                    ""Title"" TEXT NOT NULL,
                    ""FilePath"" TEXT NOT NULL,
                    ""Size"" INTEGER NOT NULL DEFAULT 0,
                    ""Quality"" TEXT NULL,
                    ""QualityScore"" INTEGER NOT NULL DEFAULT 0,
                    ""Status"" INTEGER NOT NULL DEFAULT 0,
                    ""ErrorMessage"" TEXT NULL,
                    ""SuggestedEventId"" INTEGER NULL,
                    ""SuggestedPart"" TEXT NULL,
                    ""SuggestionConfidence"" INTEGER NOT NULL DEFAULT 0,
                    ""Detected"" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""ResolvedAt"" TEXT NULL,
                    ""Protocol"" TEXT NULL,
                    ""TorrentInfoHash"" TEXT NULL,
                    ""IsPack"" INTEGER NOT NULL DEFAULT 0,
                    ""FileCount"" INTEGER NOT NULL DEFAULT 0,
                    ""MatchedEventsCount"" INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT ""FK_PendingImports_DownloadClients_DownloadClientId"" FOREIGN KEY (""DownloadClientId"") REFERENCES ""DownloadClients"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_PendingImports_Events_SuggestedEventId"" FOREIGN KEY (""SuggestedEventId"") REFERENCES ""Events"" (""Id"") ON DELETE SET NULL
                );
            ");

            // Step 2: Copy data (convert DownloadClientId 0 to NULL)
            migrationBuilder.Sql(@"
                INSERT INTO ""PendingImports_new"" (""Id"", ""DownloadClientId"", ""DownloadId"", ""Title"", ""FilePath"", ""Size"", ""Quality"", ""QualityScore"", ""Status"", ""ErrorMessage"", ""SuggestedEventId"", ""SuggestedPart"", ""SuggestionConfidence"", ""Detected"", ""ResolvedAt"", ""Protocol"", ""TorrentInfoHash"", ""IsPack"", ""FileCount"", ""MatchedEventsCount"")
                SELECT ""Id"", CASE WHEN ""DownloadClientId"" = 0 THEN NULL ELSE ""DownloadClientId"" END, ""DownloadId"", ""Title"", ""FilePath"", ""Size"", ""Quality"", ""QualityScore"", ""Status"", ""ErrorMessage"", ""SuggestedEventId"", ""SuggestedPart"", ""SuggestionConfidence"", ""Detected"", ""ResolvedAt"", ""Protocol"", ""TorrentInfoHash"", ""IsPack"", ""FileCount"", ""MatchedEventsCount""
                FROM ""PendingImports"";
            ");

            // Step 3: Drop old table
            migrationBuilder.Sql(@"DROP TABLE ""PendingImports"";");

            // Step 4: Rename new table
            migrationBuilder.Sql(@"ALTER TABLE ""PendingImports_new"" RENAME TO ""PendingImports"";");

            // Step 5: Recreate indexes
            migrationBuilder.Sql(@"CREATE INDEX ""IX_PendingImports_DownloadClientId"" ON ""PendingImports"" (""DownloadClientId"");");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_PendingImports_SuggestedEventId"" ON ""PendingImports"" (""SuggestedEventId"");");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_PendingImports_Status"" ON ""PendingImports"" (""Status"");");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_PendingImports_DownloadId"" ON ""PendingImports"" (""DownloadId"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rebuild with NOT NULL DownloadClientId (original schema)
            migrationBuilder.Sql(@"
                CREATE TABLE ""PendingImports_new"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PendingImports"" PRIMARY KEY AUTOINCREMENT,
                    ""DownloadClientId"" INTEGER NOT NULL DEFAULT 0,
                    ""DownloadId"" TEXT NOT NULL,
                    ""Title"" TEXT NOT NULL,
                    ""FilePath"" TEXT NOT NULL,
                    ""Size"" INTEGER NOT NULL DEFAULT 0,
                    ""Quality"" TEXT NULL,
                    ""QualityScore"" INTEGER NOT NULL DEFAULT 0,
                    ""Status"" INTEGER NOT NULL DEFAULT 0,
                    ""ErrorMessage"" TEXT NULL,
                    ""SuggestedEventId"" INTEGER NULL,
                    ""SuggestedPart"" TEXT NULL,
                    ""SuggestionConfidence"" INTEGER NOT NULL DEFAULT 0,
                    ""Detected"" TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""ResolvedAt"" TEXT NULL,
                    ""Protocol"" TEXT NULL,
                    ""TorrentInfoHash"" TEXT NULL,
                    ""IsPack"" INTEGER NOT NULL DEFAULT 0,
                    ""FileCount"" INTEGER NOT NULL DEFAULT 0,
                    ""MatchedEventsCount"" INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT ""FK_PendingImports_DownloadClients_DownloadClientId"" FOREIGN KEY (""DownloadClientId"") REFERENCES ""DownloadClients"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_PendingImports_Events_SuggestedEventId"" FOREIGN KEY (""SuggestedEventId"") REFERENCES ""Events"" (""Id"") ON DELETE SET NULL
                );
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""PendingImports_new"" (""Id"", ""DownloadClientId"", ""DownloadId"", ""Title"", ""FilePath"", ""Size"", ""Quality"", ""QualityScore"", ""Status"", ""ErrorMessage"", ""SuggestedEventId"", ""SuggestedPart"", ""SuggestionConfidence"", ""Detected"", ""ResolvedAt"", ""Protocol"", ""TorrentInfoHash"", ""IsPack"", ""FileCount"", ""MatchedEventsCount"")
                SELECT ""Id"", COALESCE(""DownloadClientId"", 0), ""DownloadId"", ""Title"", ""FilePath"", ""Size"", ""Quality"", ""QualityScore"", ""Status"", ""ErrorMessage"", ""SuggestedEventId"", ""SuggestedPart"", ""SuggestionConfidence"", ""Detected"", ""ResolvedAt"", ""Protocol"", ""TorrentInfoHash"", ""IsPack"", ""FileCount"", ""MatchedEventsCount""
                FROM ""PendingImports"";
            ");

            migrationBuilder.Sql(@"DROP TABLE ""PendingImports"";");
            migrationBuilder.Sql(@"ALTER TABLE ""PendingImports_new"" RENAME TO ""PendingImports"";");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_PendingImports_DownloadClientId"" ON ""PendingImports"" (""DownloadClientId"");");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_PendingImports_SuggestedEventId"" ON ""PendingImports"" (""SuggestedEventId"");");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_PendingImports_Status"" ON ""PendingImports"" (""Status"");");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_PendingImports_DownloadId"" ON ""PendingImports"" (""DownloadId"");");
        }
    }
}
