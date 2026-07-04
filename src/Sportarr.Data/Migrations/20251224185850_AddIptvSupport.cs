using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIptvSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EpgSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ProgramCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpgSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IptvSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Password = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MaxStreams = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChannelCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IptvSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EpgPrograms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EpgSourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IconUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsSportsProgram = table.Column<bool>(type: "INTEGER", nullable: false),
                    MatchedEventId = table.Column<int>(type: "INTEGER", nullable: true),
                    MatchConfidence = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpgPrograms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpgPrograms_EpgSources_EpgSourceId",
                        column: x => x.EpgSourceId,
                        principalTable: "EpgSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EpgPrograms_Events_MatchedEventId",
                        column: x => x.MatchedEventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "IptvChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ChannelNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    StreamUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    LogoUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Group = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TvgId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TvgName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsSportsChannel = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LastChecked = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IptvChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IptvChannels_IptvSources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "IptvSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelLeagueMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    LeagueId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPreferred = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelLeagueMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelLeagueMappings_IptvChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "IptvChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChannelLeagueMappings_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DvrRecordings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: true),
                    ChannelId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ScheduledStart = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ScheduledEnd = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PrePadding = table.Column<int>(type: "INTEGER", nullable: false),
                    PostPadding = table.Column<int>(type: "INTEGER", nullable: false),
                    ActualStart = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActualEnd = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    OutputPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    CurrentBitrate = table.Column<long>(type: "INTEGER", nullable: true),
                    AverageBitrate = table.Column<long>(type: "INTEGER", nullable: true),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    PartName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DvrRecordings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DvrRecordings_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DvrRecordings_IptvChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "IptvChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelLeagueMappings_ChannelId_LeagueId",
                table: "ChannelLeagueMappings",
                columns: new[] { "ChannelId", "LeagueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelLeagueMappings_IsPreferred",
                table: "ChannelLeagueMappings",
                column: "IsPreferred");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelLeagueMappings_LeagueId",
                table: "ChannelLeagueMappings",
                column: "LeagueId");

            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_ChannelId",
                table: "DvrRecordings",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_EventId",
                table: "DvrRecordings",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_ScheduledEnd",
                table: "DvrRecordings",
                column: "ScheduledEnd");

            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_ScheduledStart",
                table: "DvrRecordings",
                column: "ScheduledStart");

            migrationBuilder.CreateIndex(
                name: "IX_DvrRecordings_Status",
                table: "DvrRecordings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_ChannelId",
                table: "EpgPrograms",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_EndTime",
                table: "EpgPrograms",
                column: "EndTime");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_EpgSourceId",
                table: "EpgPrograms",
                column: "EpgSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_IsSportsProgram",
                table: "EpgPrograms",
                column: "IsSportsProgram");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_MatchedEventId",
                table: "EpgPrograms",
                column: "MatchedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgPrograms_StartTime",
                table: "EpgPrograms",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_EpgSources_IsActive",
                table: "EpgSources",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_Group",
                table: "IptvChannels",
                column: "Group");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_IsSportsChannel",
                table: "IptvChannels",
                column: "IsSportsChannel");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_Name",
                table: "IptvChannels",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_SourceId",
                table: "IptvChannels",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_Status",
                table: "IptvChannels",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_TvgId",
                table: "IptvChannels",
                column: "TvgId");

            migrationBuilder.CreateIndex(
                name: "IX_IptvSources_IsActive",
                table: "IptvSources",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_IptvSources_Name",
                table: "IptvSources",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelLeagueMappings");

            migrationBuilder.DropTable(
                name: "DvrRecordings");

            migrationBuilder.DropTable(
                name: "EpgPrograms");

            migrationBuilder.DropTable(
                name: "IptvChannels");

            migrationBuilder.DropTable(
                name: "EpgSources");

            migrationBuilder.DropTable(
                name: "IptvSources");
        }
    }
}
