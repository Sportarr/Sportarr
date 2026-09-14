using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomaticAcquisitionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "PendingReleases" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_PendingReleases" PRIMARY KEY AUTOINCREMENT,
                    "EventId" INTEGER NOT NULL REFERENCES "Events" ("Id") ON DELETE CASCADE,
                    "Title" TEXT NOT NULL, "Guid" TEXT NOT NULL, "DownloadUrl" TEXT NOT NULL,
                    "InfoUrl" TEXT NULL, "Indexer" TEXT NOT NULL, "IndexerId" INTEGER NULL,
                    "TorrentInfoHash" TEXT NULL, "Protocol" TEXT NOT NULL, "Size" INTEGER NOT NULL,
                    "Quality" TEXT NULL, "Source" TEXT NULL, "Codec" TEXT NULL, "Language" TEXT NULL,
                    "ReleaseGroup" TEXT NULL, "QualityScore" INTEGER NOT NULL, "CustomFormatScore" INTEGER NOT NULL,
                    "Score" INTEGER NOT NULL, "MatchScore" INTEGER NOT NULL, "Part" TEXT NULL,
                    "Seeders" INTEGER NULL, "Leechers" INTEGER NULL, "PublishDate" TEXT NOT NULL,
                    "AddedToPendingAt" TEXT NOT NULL, "ReleasableAt" TEXT NOT NULL, "Reason" TEXT NOT NULL,
                    "Status" INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_PendingReleases_EventId" ON "PendingReleases" ("EventId");
                CREATE INDEX IF NOT EXISTS "IX_PendingReleases_Status_ReleasableAt" ON "PendingReleases" ("Status", "ReleasableAt");
                """);
            migrationBuilder.AddColumn<bool>(name: "IsPack", table: "PendingReleases", type: "INTEGER", nullable: true);
            migrationBuilder.AddColumn<bool>(
                name: "AutomaticMissingEnabled",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "AutomaticMissingMaxAgeDays",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AutomaticUpgradeMaxAgeDays",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AutomaticUpgradesEnabled",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAutomatic",
                table: "DvrRecordings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsPack", table: "PendingReleases");
            migrationBuilder.DropColumn(
                name: "AutomaticMissingEnabled",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "AutomaticMissingMaxAgeDays",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "AutomaticUpgradeMaxAgeDays",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "AutomaticUpgradesEnabled",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "IsAutomatic",
                table: "DvrRecordings");
        }
    }
}
