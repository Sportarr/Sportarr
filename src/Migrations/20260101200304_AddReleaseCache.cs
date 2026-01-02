using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReleaseCache",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SearchTerms = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Guid = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DownloadUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    InfoUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Indexer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TorrentInfoHash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Seeders = table.Column<int>(type: "INTEGER", nullable: true),
                    Leechers = table.Column<int>(type: "INTEGER", nullable: true),
                    PublishDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IndexerFlags = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CachedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FromRss = table.Column<bool>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Month = table.Column<int>(type: "INTEGER", nullable: true),
                    Day = table.Column<int>(type: "INTEGER", nullable: true),
                    RoundNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    SportPrefix = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    IsPack = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseCache", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_CachedAt",
                table: "ReleaseCache",
                column: "CachedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_ExpiresAt",
                table: "ReleaseCache",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_Guid",
                table: "ReleaseCache",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_Indexer",
                table: "ReleaseCache",
                column: "Indexer");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_NormalizedTitle",
                table: "ReleaseCache",
                column: "NormalizedTitle");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_PublishDate",
                table: "ReleaseCache",
                column: "PublishDate");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_RoundNumber",
                table: "ReleaseCache",
                column: "RoundNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_SportPrefix",
                table: "ReleaseCache",
                column: "SportPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_SportPrefix_Year_RoundNumber",
                table: "ReleaseCache",
                columns: new[] { "SportPrefix", "Year", "RoundNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseCache_Year",
                table: "ReleaseCache",
                column: "Year");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReleaseCache");
        }
    }
}
