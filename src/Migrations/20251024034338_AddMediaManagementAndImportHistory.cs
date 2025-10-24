using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaManagementAndImportHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Created",
                table: "RootFolders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<long>(
                name: "TotalSpace",
                table: "RootFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ImportHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    DownloadQueueItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DestinationPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Decision = table.Column<int>(type: "INTEGER", nullable: false),
                    Warnings = table.Column<string>(type: "TEXT", nullable: false),
                    Errors = table.Column<string>(type: "TEXT", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportHistories_DownloadQueue_DownloadQueueItemId",
                        column: x => x.DownloadQueueItemId,
                        principalTable: "DownloadQueue",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ImportHistories_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaManagementSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RootFolders = table.Column<string>(type: "TEXT", nullable: false),
                    RenameEvents = table.Column<bool>(type: "INTEGER", nullable: false),
                    RenameFiles = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReplaceIllegalCharacters = table.Column<bool>(type: "INTEGER", nullable: false),
                    StandardEventFormat = table.Column<string>(type: "TEXT", nullable: false),
                    StandardFileFormat = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreateEventFolders = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreateEventFolder = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventFolderFormat = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DeleteEmptyFolders = table.Column<bool>(type: "INTEGER", nullable: false),
                    CopyFiles = table.Column<bool>(type: "INTEGER", nullable: false),
                    SkipFreeSpaceCheck = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinimumFreeSpace = table.Column<long>(type: "INTEGER", nullable: false),
                    UseHardlinks = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImportExtraFiles = table.Column<bool>(type: "INTEGER", nullable: false),
                    ExtraFileExtensions = table.Column<string>(type: "TEXT", nullable: false),
                    SetPermissions = table.Column<bool>(type: "INTEGER", nullable: false),
                    FileChmod = table.Column<string>(type: "TEXT", nullable: false),
                    ChmodFolder = table.Column<string>(type: "TEXT", nullable: false),
                    ChownUser = table.Column<string>(type: "TEXT", nullable: false),
                    ChownGroup = table.Column<string>(type: "TEXT", nullable: false),
                    RemoveCompletedDownloads = table.Column<bool>(type: "INTEGER", nullable: false),
                    RemoveFailedDownloads = table.Column<bool>(type: "INTEGER", nullable: false),
                    ChangeFileDate = table.Column<string>(type: "TEXT", nullable: false),
                    RecycleBin = table.Column<string>(type: "TEXT", nullable: false),
                    RecycleBinCleanup = table.Column<int>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaManagementSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportHistories_DownloadQueueItemId",
                table: "ImportHistories",
                column: "DownloadQueueItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportHistories_EventId",
                table: "ImportHistories",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportHistories_ImportedAt",
                table: "ImportHistories",
                column: "ImportedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportHistories");

            migrationBuilder.DropTable(
                name: "MediaManagementSettings");

            migrationBuilder.DropColumn(
                name: "Created",
                table: "RootFolders");

            migrationBuilder.DropColumn(
                name: "TotalSpace",
                table: "RootFolders");
        }
    }
}
