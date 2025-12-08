using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityGroupSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Items",
                value: "[{\"Name\":\"1080p\",\"Quality\":1080,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"720p\",\"Quality\":720,\"Allowed\":false,\"Items\":null,\"Id\":null},{\"Name\":\"480p\",\"Quality\":480,\"Allowed\":false,\"Items\":null,\"Id\":null}]");

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "Items",
                value: "[{\"Name\":\"1080p\",\"Quality\":1080,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"720p\",\"Quality\":720,\"Allowed\":true,\"Items\":null,\"Id\":null},{\"Name\":\"480p\",\"Quality\":480,\"Allowed\":true,\"Items\":null,\"Id\":null}]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Items",
                value: "[{\"Name\":\"1080p\",\"Quality\":1080,\"Allowed\":true},{\"Name\":\"720p\",\"Quality\":720,\"Allowed\":false},{\"Name\":\"480p\",\"Quality\":480,\"Allowed\":false}]");

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "Items",
                value: "[{\"Name\":\"1080p\",\"Quality\":1080,\"Allowed\":true},{\"Name\":\"720p\",\"Quality\":720,\"Allowed\":true},{\"Name\":\"480p\",\"Quality\":480,\"Allowed\":true}]");
        }
    }
}
