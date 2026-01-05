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
            // Add Protocol column
            migrationBuilder.AddColumn<string>(
                name: "Protocol",
                table: "Blocklist",
                type: "TEXT",
                nullable: true);

            // SQLite doesn't support ALTER COLUMN, so we need to recreate the table
            // to make TorrentInfoHash nullable
            migrationBuilder.Sql(@"
                -- Create temp table with new schema
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

                -- Copy data
                INSERT INTO ""Blocklist_temp"" (""Id"", ""EventId"", ""Title"", ""TorrentInfoHash"", ""Indexer"", ""Protocol"", ""Reason"", ""Message"", ""BlockedAt"", ""Part"")
                SELECT ""Id"", ""EventId"", ""Title"", ""TorrentInfoHash"", ""Indexer"", ""Protocol"", ""Reason"", ""Message"", ""BlockedAt"", ""Part""
                FROM ""Blocklist"";

                -- Drop old table
                DROP TABLE ""Blocklist"";

                -- Rename temp table
                ALTER TABLE ""Blocklist_temp"" RENAME TO ""Blocklist"";

                -- Recreate index
                CREATE INDEX ""IX_Blocklist_EventId"" ON ""Blocklist"" (""EventId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate table with NOT NULL constraint on TorrentInfoHash
            migrationBuilder.Sql(@"
                -- Create temp table with old schema
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

                -- Copy data (only rows with TorrentInfoHash)
                INSERT INTO ""Blocklist_temp"" (""Id"", ""EventId"", ""Title"", ""TorrentInfoHash"", ""Indexer"", ""Reason"", ""Message"", ""BlockedAt"", ""Part"")
                SELECT ""Id"", ""EventId"", ""Title"", COALESCE(""TorrentInfoHash"", 'unknown'), ""Indexer"", ""Reason"", ""Message"", ""BlockedAt"", ""Part""
                FROM ""Blocklist"";

                -- Drop old table
                DROP TABLE ""Blocklist"";

                -- Rename temp table
                ALTER TABLE ""Blocklist_temp"" RENAME TO ""Blocklist"";

                -- Recreate index
                CREATE INDEX ""IX_Blocklist_EventId"" ON ""Blocklist"" (""EventId"");
            ");
        }
    }
}
