using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileFormatScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "FormatItems",
                value: "[{\"Id\":0,\"FormatId\":1,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":2,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":3,\"Format\":null,\"Score\":5},{\"Id\":0,\"FormatId\":4,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":5,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":6,\"Format\":null,\"Score\":0},{\"Id\":0,\"FormatId\":7,\"Format\":null,\"Score\":10}]");

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "FormatItems",
                value: "[{\"Id\":0,\"FormatId\":1,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":2,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":3,\"Format\":null,\"Score\":5},{\"Id\":0,\"FormatId\":4,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":5,\"Format\":null,\"Score\":-10000},{\"Id\":0,\"FormatId\":6,\"Format\":null,\"Score\":0},{\"Id\":0,\"FormatId\":7,\"Format\":null,\"Score\":10}]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "FormatItems",
                value: "[]");

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "FormatItems",
                value: "[]");
        }
    }
}
