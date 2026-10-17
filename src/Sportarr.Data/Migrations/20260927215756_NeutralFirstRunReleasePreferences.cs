using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class NeutralFirstRunReleasePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "QualityProfiles"
                SET "FormatItems" = '[{"Id":0,"FormatId":1,"Format":null,"Score":0},{"Id":0,"FormatId":2,"Format":null,"Score":0},{"Id":0,"FormatId":3,"Format":null,"Score":0},{"Id":0,"FormatId":4,"Format":null,"Score":0},{"Id":0,"FormatId":5,"Format":null,"Score":0},{"Id":0,"FormatId":6,"Format":null,"Score":0},{"Id":0,"FormatId":7,"Format":null,"Score":0}]'
                WHERE "Id" IN (1, 2)
                  AND "Name" IN ('WEB-1080p (Alternative)', 'WEB-2160p (Alternative)')
                  AND NOT "IsCustomized"
                  AND NOT EXISTS (SELECT 1 FROM "AppSettings");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Keep profile scores chosen after installation.
        }
    }
}
