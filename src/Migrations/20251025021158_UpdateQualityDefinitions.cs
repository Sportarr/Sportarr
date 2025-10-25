using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateQualityDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QualityDefinitions_Name",
                table: "QualityDefinitions");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "QualityDefinitions");

            migrationBuilder.RenameColumn(
                name: "Weight",
                table: "QualityDefinitions",
                newName: "Quality");

            migrationBuilder.AlterColumn<decimal>(
                name: "PreferredSize",
                table: "QualityDefinitions",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MinSize",
                table: "QualityDefinitions",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "MaxSize",
                table: "QualityDefinitions",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: true,
                oldClrType: typeof(double),
                oldType: "REAL",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Created",
                table: "QualityDefinitions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModified",
                table: "QualityDefinitions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "DelayProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 420, DateTimeKind.Utc).AddTicks(9891));

            migrationBuilder.InsertData(
                table: "QualityDefinitions",
                columns: new[] { "Id", "Created", "LastModified", "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(597), null, 199m, 1m, 95m, 0, "Unknown" },
                    { 2, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1379), null, 25m, 2m, 6m, 3, "SDTV" },
                    { 3, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1382), null, 25m, 2m, 6m, 4, "DVD" },
                    { 4, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1383), null, 30m, 2m, 8m, 5, "Bluray-480p" },
                    { 5, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1385), null, 30m, 2m, 6m, 6, "WEB 480p" },
                    { 6, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1387), null, 60m, 4m, 15m, 7, "Raw-HD" },
                    { 7, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1405), null, 60m, 8m, 15m, 8, "Bluray-720p" },
                    { 8, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1407), null, 60m, 5m, 12m, 9, "WEB 720p" },
                    { 9, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1408), null, 80m, 6m, 20m, 11, "HDTV-1080p" },
                    { 10, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1409), null, 300m, 20m, 80m, 12, "HDTV-2160p" },
                    { 11, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1411), null, 120m, 20m, 40m, 13, "Bluray-1080p Remux" },
                    { 12, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1412), null, 100m, 15m, 30m, 14, "Bluray-1080p" },
                    { 13, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1413), null, 100m, 10m, 25m, 15, "WEB 1080p" },
                    { 14, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1415), null, 500m, 35m, 120m, 17, "Bluray-2160p Remux" },
                    { 15, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1416), null, 400m, 35m, 95m, 18, "Bluray-2160p" },
                    { 16, new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1425), null, 400m, 35m, 95m, 19, "WEB 2160p" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_QualityDefinitions_Quality",
                table: "QualityDefinitions",
                column: "Quality",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QualityDefinitions_Quality",
                table: "QualityDefinitions");

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DropColumn(
                name: "Created",
                table: "QualityDefinitions");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "QualityDefinitions");

            migrationBuilder.RenameColumn(
                name: "Quality",
                table: "QualityDefinitions",
                newName: "Weight");

            migrationBuilder.AlterColumn<double>(
                name: "PreferredSize",
                table: "QualityDefinitions",
                type: "REAL",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AlterColumn<double>(
                name: "MinSize",
                table: "QualityDefinitions",
                type: "REAL",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AlterColumn<double>(
                name: "MaxSize",
                table: "QualityDefinitions",
                type: "REAL",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "TEXT",
                oldPrecision: 10,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "QualityDefinitions",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "DelayProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 1, 49, 51, 473, DateTimeKind.Utc).AddTicks(3763));

            migrationBuilder.CreateIndex(
                name: "IX_QualityDefinitions_Name",
                table: "QualityDefinitions",
                column: "Name",
                unique: true);
        }
    }
}
