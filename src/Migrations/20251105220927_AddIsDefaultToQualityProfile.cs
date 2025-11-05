using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIsDefaultToQualityProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "IsDefault",
                value: false);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "IsDefault",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "QualityProfiles");
        }
    }
}
