using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddBlocklistUsenetSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite doesn't support ALTER COLUMN, so we use table recreation.
            // This migration:
            // 1. Adds Protocol column
            // 2. Makes TorrentInfoHash nullable (required for Usenet which has no info hash)
            //
            // Note: For users who already had a partial version of this migration,
            // a separate fix migration (FixBlocklistUsenetSupport) handles the repair.

            migrationBuilder.Sql(@"
                -- Create temp table with new schema (TorrentInfoHash now nullable, Protocol added)
                CREATE TABLE ""Blocklist_temp"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Blocklist"" PRIMARY KEY AUTOINCREMENT,
                    ""EventId"" INTEGER NULL,
                    ""Title"" TEXT NOT NULL,
                    ""TorrentInfoHash"" TEXT NULL,
                    ""Indexer"" TEXT NULL,
                    ""Protocol"" TEXT NULL,
                    ""Reason"" INTEGER NOT NULL,
                    ""Message"" TEXT NULL,
                    ""BlockedAt"" TEXT NOT NULL,
                    ""Part"" TEXT NULL,
                    CONSTRAINT ""FK_Blocklist_Events_EventId"" FOREIGN KEY (""EventId"") REFERENCES ""Events"" (""Id"") ON DELETE SET NULL
                );

                -- Copy existing data (Protocol will be NULL for existing records)
                INSERT INTO ""Blocklist_temp"" (""Id"", ""EventId"", ""Title"", ""TorrentInfoHash"", ""Indexer"", ""Reason"", ""Message"", ""BlockedAt"", ""Part"")
                SELECT ""Id"", ""EventId"", ""Title"", ""TorrentInfoHash"", ""Indexer"", ""Reason"", ""Message"", ""BlockedAt"", ""Part""
                FROM ""Blocklist"";

                -- Drop old table
                DROP TABLE ""Blocklist"";

                -- Rename temp table
                ALTER TABLE ""Blocklist_temp"" RENAME TO ""Blocklist"";

                -- Recreate indexes
                CREATE INDEX ""IX_Blocklist_BlockedAt"" ON ""Blocklist"" (""BlockedAt"");
                CREATE INDEX ""IX_Blocklist_EventId"" ON ""Blocklist"" (""EventId"");
                CREATE INDEX ""IX_Blocklist_TorrentInfoHash"" ON ""Blocklist"" (""TorrentInfoHash"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate table with NOT NULL constraint on TorrentInfoHash (original schema)
            migrationBuilder.Sql(@"
                CREATE TABLE ""Blocklist_temp"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Blocklist"" PRIMARY KEY AUTOINCREMENT,
                    ""EventId"" INTEGER NULL,
                    ""Title"" TEXT NOT NULL,
                    ""TorrentInfoHash"" TEXT NOT NULL,
                    ""Indexer"" TEXT NULL,
                    ""Reason"" INTEGER NOT NULL,
                    ""Message"" TEXT NULL,
                    ""BlockedAt"" TEXT NOT NULL,
                    ""Part"" TEXT NULL,
                    CONSTRAINT ""FK_Blocklist_Events_EventId"" FOREIGN KEY (""EventId"") REFERENCES ""Events"" (""Id"") ON DELETE SET NULL
                );

                INSERT INTO ""Blocklist_temp"" (""Id"", ""EventId"", ""Title"", ""TorrentInfoHash"", ""Indexer"", ""Reason"", ""Message"", ""BlockedAt"", ""Part"")
                SELECT ""Id"", ""EventId"", ""Title"", COALESCE(""TorrentInfoHash"", 'unknown'), ""Indexer"", ""Reason"", ""Message"", ""BlockedAt"", ""Part""
                FROM ""Blocklist"";

                DROP TABLE ""Blocklist"";
                ALTER TABLE ""Blocklist_temp"" RENAME TO ""Blocklist"";

                CREATE INDEX ""IX_Blocklist_BlockedAt"" ON ""Blocklist"" (""BlockedAt"");
                CREATE INDEX ""IX_Blocklist_EventId"" ON ""Blocklist"" (""EventId"");
                CREATE INDEX ""IX_Blocklist_TorrentInfoHash"" ON ""Blocklist"" (""TorrentInfoHash"");
            ");
        }
    }
}
