using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded so a legacy database that already has the table from
            // the startup safety net does not fail on this step.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "SearchHistory" (
                "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                "EventId" INTEGER NOT NULL,
                "Part" TEXT NULL,
                "SearchedAt" TEXT NOT NULL,
                "IsManual" INTEGER NOT NULL,
                "Outcome" TEXT NOT NULL,
                "Message" TEXT NOT NULL,
                "ReleasesFound" INTEGER NOT NULL,
                "ReleasesPassed" INTEGER NOT NULL,
                "SelectedRelease" TEXT NULL,
                "QualityProfile" TEXT NULL,
                "ReasonsJson" TEXT NOT NULL,
                "ReleasesJson" TEXT NULL,
                "IndexersJson" TEXT NOT NULL,
                CONSTRAINT "FK_SearchHistory_Events_EventId" FOREIGN KEY ("EventId") REFERENCES "Events" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_SearchHistory_EventId_SearchedAt" ON "SearchHistory" ("EventId", "SearchedAt");
                CREATE INDEX IF NOT EXISTS "IX_SearchHistory_SearchedAt" ON "SearchHistory" ("SearchedAt");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchHistory");
        }
    }
}
