using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelTeamMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PendingReleases_Status_ReleasableAt",
                table: "PendingReleases");

            migrationBuilder.DropIndex(
                name: "IX_DvrRecordings_Method",
                table: "DvrRecordings");

            migrationBuilder.DropIndex(
                name: "IX_DownloadQueue_EventId",
                table: "DownloadQueue");

            migrationBuilder.DropIndex(
                name: "IX_ChannelLeagueMappings_LeagueId",
                table: "ChannelLeagueMappings");

            migrationBuilder.AlterColumn<int>(
                name: "DownloadClientId",
                table: "PendingImports",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Notifications",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "EnableEventRetention",
                table: "MediaManagementSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EventRetentionDays",
                table: "MediaManagementSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "MonitorFinals",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MonitorPlayoffs",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MonitorPreseason",
                table: "Leagues",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Leagues",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DownloadId",
                table: "GrabHistory",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Directory",
                table: "DownloadClients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "DownloadClients",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "HubChangesCursor",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ChannelTeamMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPreferred = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelTeamMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelTeamMappings_IptvChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "IptvChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelTeamMappings_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventFileHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceTitle = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Part = table.Column<string>(type: "TEXT", nullable: true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventFileHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventFileHistory_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RestoreReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BackupFileName = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalEventFiles = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesFound = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesMissing = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesSkippedUnreachableRoot = table.Column<int>(type: "INTEGER", nullable: false),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: true),
                    PathRemapsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    SourceHost = table.Column<string>(type: "TEXT", nullable: true),
                    SourceSportarrVersion = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeasonPosters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LeagueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Season = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PosterUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonPosters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeasonPosters_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportHistories_DestinationPath",
                table: "ImportHistories",
                column: "DestinationPath");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Monitored",
                table: "Events",
                column: "Monitored");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_EventId_Status",
                table: "DownloadQueue",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelTeamMappings_ChannelId_TeamId",
                table: "ChannelTeamMappings",
                columns: new[] { "ChannelId", "TeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ChannelTeamMappings_PreferredPerTeam",
                table: "ChannelTeamMappings",
                column: "TeamId",
                unique: true,
                filter: "\"IsPreferred\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_EventFileHistory_Date",
                table: "EventFileHistory",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_EventFileHistory_EventId",
                table: "EventFileHistory",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_SeasonPosters_LeagueId_Season",
                table: "SeasonPosters",
                columns: new[] { "LeagueId", "Season" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelTeamMappings");

            migrationBuilder.DropTable(
                name: "EventFileHistory");

            migrationBuilder.DropTable(
                name: "RestoreReports");

            migrationBuilder.DropTable(
                name: "SeasonPosters");

            migrationBuilder.DropIndex(
                name: "IX_ImportHistories_DestinationPath",
                table: "ImportHistories");

            migrationBuilder.DropIndex(
                name: "IX_Events_Monitored",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_DownloadQueue_EventId_Status",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EnableEventRetention",
                table: "MediaManagementSettings");

            migrationBuilder.DropColumn(
                name: "EventRetentionDays",
                table: "MediaManagementSettings");

            migrationBuilder.DropColumn(
                name: "MonitorFinals",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "MonitorPlayoffs",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "MonitorPreseason",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "DownloadId",
                table: "GrabHistory");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Directory",
                table: "DownloadClients");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "DownloadClients");

            migrationBuilder.DropColumn(
                name: "HubChangesCursor",
                table: "AppSettings");

            migrationBuilder.AlterColumn<int>(
                name: "DownloadClientId",
                table: "PendingImports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PendingReleases_Status_ReleasableAt",
                table: "PendingReleases",
                columns: new[] { "Status", "ReleasableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_Method",
                table: "DvrRecordings",
                column: "Method");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_EventId",
                table: "DownloadQueue",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelLeagueMappings_LeagueId",
                table: "ChannelLeagueMappings",
                column: "LeagueId");
        }
    }
}
