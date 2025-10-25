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
            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MaxSize", "PreferredSize" },
                values: new object[] { 199.9m, 194.9m });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "MaxSize", "PreferredSize", "Quality" },
                values: new object[] { 100m, 95m, 1 });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "MaxSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 100m, 95m, 8, "WEBRip-480p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "MaxSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 100m, 95m, 2, "WEBDL-480p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "MaxSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 100m, 95m, 4, "DVD" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 100m, 2m, 95m, 9, "Bluray-480p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 100m, 2m, 95m, 16, "Bluray-576p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 1000m, 10m, 995m, 5, "HDTV-720p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality" },
                values: new object[] { 1000m, 15m, 995m, 6 });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 1000m, 4m, 995m, 20, "Raw-HD" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 1000m, 10m, 995m, 10, "WEBRip-720p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 1000m, 10m, 995m, 3, "WEBDL-720p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 1000m, 17.1m, 995m, 7, "Bluray-720p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 1000m, 15m, 995m, 14, "WEBRip-1080p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 1000m, 15m, 995m, 15, "WEBDL-1080p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 1000m, 50.4m, 995m, 11, "Bluray-1080p" });

            migrationBuilder.InsertData(
                table: "QualityDefinitions",
                columns: new[] { "Id", "Created", "LastModified", "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[,]
                {
                    { 17, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 69.1m, 995m, 12, "Bluray-1080p Remux" },
                    { 18, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 25m, 995m, 17, "HDTV-2160p" },
                    { 19, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 25m, 995m, 18, "WEBRip-2160p" },
                    { 20, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 25m, 995m, 19, "WEBDL-2160p" },
                    { 21, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 94.6m, 995m, 13, "Bluray-2160p" },
                    { 22, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, 1000m, 187.4m, 995m, 21, "Bluray-2160p Remux" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 22);

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "MaxSize", "PreferredSize" },
                values: new object[] { 199m, 95m });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "MaxSize", "PreferredSize", "Quality" },
                values: new object[] { 25m, 6m, 3 });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "MaxSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 25m, 6m, 4, "DVD" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "MaxSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 30m, 8m, 5, "Bluray-480p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "MaxSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 30m, 6m, 6, "WEB 480p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 60m, 4m, 15m, 7, "Raw-HD" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 60m, 8m, 15m, 8, "Bluray-720p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 60m, 5m, 12m, 9, "WEB 720p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality" },
                values: new object[] { 80m, 6m, 20m, 11 });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 300m, 20m, 80m, 12, "HDTV-2160p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 120m, 20m, 40m, 13, "Bluray-1080p Remux" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 100m, 15m, 30m, 14, "Bluray-1080p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 100m, 10m, 25m, 15, "WEB 1080p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 500m, 35m, 120m, 17, "Bluray-2160p Remux" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 400m, 35m, 95m, 18, "Bluray-2160p" });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[] { 400m, 35m, 95m, 19, "WEB 2160p" });
        }
    }
}
