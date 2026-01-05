using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Fix migration for users who had a partial AddBlocklistUsenetSupport migration.
    /// Some users may have the Protocol column added but TorrentInfoHash still NOT NULL.
    /// This migration ensures TorrentInfoHash is nullable (required for Usenet).
    /// </summary>
    public partial class FixBlocklistUsenetSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This migration fixes the Blocklist table for users who had a partial migration.
            // It's idempotent - if the table is already correct, this just recreates it with the same schema.
            //
            // The fix: Make TorrentInfoHash nullable (SQLite requires table recreation for this)

            // This migration ensures the Blocklist table has the correct schema.
            // It handles multiple scenarios:
            // 1. Users who had AddBlocklistUsenetSupport applied correctly
            // 2. Users who had a partial AddBlocklistUsenetSupport (Protocol column exists but TorrentInfoHash still NOT NULL)
            // 3. Users with older databases that never had these migrations
            //
            // Strategy: Create new table, copy only the columns that definitely exist in all versions,
            // then drop and rename.

            migrationBuilder.Sql(@"
                -- Create temp table with correct schema
                CREATE TABLE IF NOT EXISTS ""Blocklist_new"" (
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

                -- Copy data using only columns that exist in all versions of the table
                -- Protocol and Part are left NULL for older records (they'll be set when new blocklist entries are created)
                INSERT OR IGNORE INTO ""Blocklist_new"" (""Id"", ""EventId"", ""Title"", ""TorrentInfoHash"", ""Indexer"", ""Reason"", ""Message"", ""BlockedAt"")
                SELECT ""Id"", ""EventId"", ""Title"", ""TorrentInfoHash"", ""Indexer"", ""Reason"", ""Message"", ""BlockedAt""
                FROM ""Blocklist"";

                -- Drop old table
                DROP TABLE IF EXISTS ""Blocklist"";

                -- Rename temp table
                ALTER TABLE ""Blocklist_new"" RENAME TO ""Blocklist"";

                -- Recreate indexes
                CREATE INDEX IF NOT EXISTS ""IX_Blocklist_BlockedAt"" ON ""Blocklist"" (""BlockedAt"");
                CREATE INDEX IF NOT EXISTS ""IX_Blocklist_EventId"" ON ""Blocklist"" (""EventId"");
                CREATE INDEX IF NOT EXISTS ""IX_Blocklist_TorrentInfoHash"" ON ""Blocklist"" (""TorrentInfoHash"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op - we don't want to revert the fix
        }
    }
}
