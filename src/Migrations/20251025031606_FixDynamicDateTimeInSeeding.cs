using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixDynamicDateTimeInSeeding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 2,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 3,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 4,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 5,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 6,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 7,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 8,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 9,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 10,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 11,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 12,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 13,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 14,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 15,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 16,
                column: "Created",
                value: new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(8845));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 2,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9540));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 3,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9543));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 4,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9544));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 5,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9546));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 6,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9547));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 7,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9558));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 8,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9560));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 9,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9561));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 10,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9563));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 11,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9564));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 12,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9565));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 13,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9567));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 14,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9568));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 15,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9569));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 16,
                column: "Created",
                value: new DateTime(2025, 10, 25, 3, 14, 38, 228, DateTimeKind.Utc).AddTicks(9577));
        }
    }
}
