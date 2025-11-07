using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class SetAnyProfileAsDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsDefault",
                value: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsDefault",
                value: false);
        }
    }
}
