using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityDefinitionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop existing table if it exists (safe for both existing and new databases)
            migrationBuilder.Sql("DROP TABLE IF EXISTS QualityDefinitions;");

            // Create the table
            migrationBuilder.CreateTable(
                name: "QualityDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    MinSize = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    MaxSize = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: true),
                    PreferredSize = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityDefinitions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QualityDefinitions_Quality",
                table: "QualityDefinitions",
                column: "Quality",
                unique: true);

            // Seed all 22 quality definitions
            var seedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            migrationBuilder.InsertData(
                table: "QualityDefinitions",
                columns: new[] { "Id", "Created", "LastModified", "MaxSize", "MinSize", "PreferredSize", "Quality", "Title" },
                values: new object[,]
                {
                    // Unknown quality
                    { 1, seedDate, null, 199.9m, 1m, 194.9m, 0, "Unknown" },

                    // SD qualities
                    { 2, seedDate, null, 100m, 2m, 95m, 1, "SDTV" },
                    { 3, seedDate, null, 100m, 2m, 95m, 8, "WEBRip-480p" },
                    { 4, seedDate, null, 100m, 2m, 95m, 2, "WEBDL-480p" },
                    { 5, seedDate, null, 100m, 2m, 95m, 4, "DVD" },
                    { 6, seedDate, null, 100m, 2m, 95m, 9, "Bluray-480p" },
                    { 7, seedDate, null, 100m, 2m, 95m, 16, "Bluray-576p" },

                    // HD 720p qualities
                    { 8, seedDate, null, 1000m, 10m, 995m, 5, "HDTV-720p" },
                    { 9, seedDate, null, 1000m, 15m, 995m, 6, "HDTV-1080p" },
                    { 10, seedDate, null, 1000m, 4m, 995m, 20, "Raw-HD" },
                    { 11, seedDate, null, 1000m, 10m, 995m, 10, "WEBRip-720p" },
                    { 12, seedDate, null, 1000m, 10m, 995m, 3, "WEBDL-720p" },
                    { 13, seedDate, null, 1000m, 17.1m, 995m, 7, "Bluray-720p" },

                    // HD 1080p qualities
                    { 14, seedDate, null, 1000m, 15m, 995m, 14, "WEBRip-1080p" },
                    { 15, seedDate, null, 1000m, 15m, 995m, 15, "WEBDL-1080p" },
                    { 16, seedDate, null, 1000m, 50.4m, 995m, 11, "Bluray-1080p" },
                    { 17, seedDate, null, 1000m, 69.1m, 995m, 12, "Bluray-1080p Remux" },

                    // UHD 4K qualities
                    { 18, seedDate, null, 1000m, 25m, 995m, 17, "HDTV-2160p" },
                    { 19, seedDate, null, 1000m, 25m, 995m, 18, "WEBRip-2160p" },
                    { 20, seedDate, null, 1000m, 25m, 995m, 19, "WEBDL-2160p" },
                    { 21, seedDate, null, 1000m, 94.6m, 995m, 13, "Bluray-2160p" },
                    { 22, seedDate, null, 1000m, 187.4m, 995m, 21, "Bluray-2160p Remux" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QualityDefinitions");
        }
    }
}
