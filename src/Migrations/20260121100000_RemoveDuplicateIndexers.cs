using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDuplicateIndexers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove duplicate indexers, keeping the one with the lowest ID for each unique URL
            // This cleans up duplicates created by Prowlarr sync issues
            migrationBuilder.Sql(@"
                DELETE FROM Indexers
                WHERE Id NOT IN (
                    SELECT MIN(Id)
                    FROM Indexers
                    WHERE Url IS NOT NULL AND Url != ''
                    GROUP BY LOWER(RTRIM(Url, '/'))
                )
                AND Url IS NOT NULL AND Url != '';
            ");

            // Log how many were deleted (SQLite doesn't support this directly, but the query above handles it)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot restore deleted duplicates - this is a one-way migration
            // The duplicates were unintended and caused issues
        }
    }
}
