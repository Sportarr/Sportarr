using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGrabHistorySuperseded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GrabHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Indexer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: true),
                    DownloadUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Guid = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Protocol = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TorrentInfoHash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    QualityScore = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomFormatScore = table.Column<int>(type: "INTEGER", nullable: false),
                    PartName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    GrabbedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WasImported = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FileExists = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastRegrabAttempt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RegrabCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DownloadClientId = table.Column<int>(type: "INTEGER", nullable: true),
                    Superseded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrabHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrabHistory_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GrabHistory_EventId",
                table: "GrabHistory",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_GrabHistory_FileExists",
                table: "GrabHistory",
                column: "FileExists");

            migrationBuilder.CreateIndex(
                name: "IX_GrabHistory_GrabbedAt",
                table: "GrabHistory",
                column: "GrabbedAt");

            migrationBuilder.CreateIndex(
                name: "IX_GrabHistory_Guid",
                table: "GrabHistory",
                column: "Guid");

            migrationBuilder.CreateIndex(
                name: "IX_GrabHistory_WasImported",
                table: "GrabHistory",
                column: "WasImported");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GrabHistory");
        }
    }
}
