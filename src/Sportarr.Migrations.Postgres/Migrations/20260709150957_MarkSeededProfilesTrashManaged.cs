using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class MarkSeededProfilesTrashManaged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsSynced",
                value: true);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsSynced",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsSynced",
                value: false);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsSynced",
                value: false);
        }
    }
}
