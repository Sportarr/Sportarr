using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <summary>
    /// This migration consolidates several schema changes that were previously in
    /// orphaned migrations (without Designer files):
    /// - MonitoredParts on Leagues and Events
    /// - DisableSslCertificateValidation on DownloadClients
    /// - PendingImports table
    /// - EventFiles table
    /// - EnableMultiPartEpisodes on MediaManagementSettings
    /// - MonitoredSessionTypes on Leagues (original migration)
    /// </summary>
    public partial class AddMonitoredSessionTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // === From AddMonitoredPartsToLeague ===
            migrationBuilder.AddColumn<string>(
                name: "MonitoredParts",
                table: "Leagues",
                type: "TEXT",
                nullable: true);

            // === From AddMonitoredPartsToEvent ===
            migrationBuilder.AddColumn<string>(
                name: "MonitoredParts",
                table: "Events",
                type: "TEXT",
                nullable: true);

            // === From AddDisableSslCertificateValidation ===
            migrationBuilder.AddColumn<bool>(
                name: "DisableSslCertificateValidation",
                table: "DownloadClients",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // === From AddPendingImports ===
            migrationBuilder.CreateTable(
                name: "PendingImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DownloadClientId = table.Column<int>(type: "INTEGER", nullable: false),
                    DownloadId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", nullable: true),
                    QualityScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestedEventId = table.Column<int>(type: "INTEGER", nullable: true),
                    SuggestedPart = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestionConfidence = table.Column<int>(type: "INTEGER", nullable: false),
                    Detected = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", nullable: true),
                    TorrentInfoHash = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingImports_DownloadClients_DownloadClientId",
                        column: x => x.DownloadClientId,
                        principalTable: "DownloadClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PendingImports_Events_SuggestedEventId",
                        column: x => x.SuggestedEventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingImports_DownloadClientId",
                table: "PendingImports",
                column: "DownloadClientId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingImports_SuggestedEventId",
                table: "PendingImports",
                column: "SuggestedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingImports_Status",
                table: "PendingImports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PendingImports_DownloadId",
                table: "PendingImports",
                column: "DownloadId");

            // === From AddEventFiles ===
            migrationBuilder.CreateTable(
                name: "EventFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PartName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PartNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastVerified = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Exists = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventFiles_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventFiles_EventId",
                table: "EventFiles",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventFiles_PartNumber",
                table: "EventFiles",
                column: "PartNumber");

            migrationBuilder.CreateIndex(
                name: "IX_EventFiles_Exists",
                table: "EventFiles",
                column: "Exists");

            // === From AddEnableMultiPartEpisodesToMediaManagementSettings ===
            migrationBuilder.AddColumn<bool>(
                name: "EnableMultiPartEpisodes",
                table: "MediaManagementSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            // === Original migration: AddMonitoredSessionTypes ===
            migrationBuilder.AddColumn<string>(
                name: "MonitoredSessionTypes",
                table: "Leagues",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop in reverse order
            migrationBuilder.DropColumn(
                name: "MonitoredSessionTypes",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "EnableMultiPartEpisodes",
                table: "MediaManagementSettings");

            migrationBuilder.DropTable(
                name: "EventFiles");

            migrationBuilder.DropTable(
                name: "PendingImports");

            migrationBuilder.DropColumn(
                name: "DisableSslCertificateValidation",
                table: "DownloadClients");

            migrationBuilder.DropColumn(
                name: "MonitoredParts",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "MonitoredParts",
                table: "Leagues");
        }
    }
}
