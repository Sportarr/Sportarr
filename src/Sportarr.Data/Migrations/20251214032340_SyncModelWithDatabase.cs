using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelWithDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConnectionErrors",
                table: "IndexerStatuses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "GrabDisabledUntil",
                table: "IndexerStatuses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GrabFailures",
                table: "IndexerStatuses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastConnectionError",
                table: "IndexerStatuses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastGrabFailure",
                table: "IndexerStatuses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastGrabFailureReason",
                table: "IndexerStatuses",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastQueryFailure",
                table: "IndexerStatuses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastQueryFailureReason",
                table: "IndexerStatuses",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QueryDisabledUntil",
                table: "IndexerStatuses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QueryFailures",
                table: "IndexerStatuses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CheckForFinishedDownloadInterval",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "EnableCompletedDownloadHandling",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableFailedDownloadHandling",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxDownloadQueueSize",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RedownloadFailedDownloads",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RemoveCompletedDownloads",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RemoveFailedDownloads",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SearchSleepDuration",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_IndexerStatuses_GrabDisabledUntil",
                table: "IndexerStatuses",
                column: "GrabDisabledUntil");

            migrationBuilder.CreateIndex(
                name: "IX_IndexerStatuses_QueryDisabledUntil",
                table: "IndexerStatuses",
                column: "QueryDisabledUntil");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IndexerStatuses_GrabDisabledUntil",
                table: "IndexerStatuses");

            migrationBuilder.DropIndex(
                name: "IX_IndexerStatuses_QueryDisabledUntil",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "ConnectionErrors",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "GrabDisabledUntil",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "GrabFailures",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "LastConnectionError",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "LastGrabFailure",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "LastGrabFailureReason",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "LastQueryFailure",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "LastQueryFailureReason",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "QueryDisabledUntil",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "QueryFailures",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "CheckForFinishedDownloadInterval",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "EnableCompletedDownloadHandling",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "EnableFailedDownloadHandling",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "MaxDownloadQueueSize",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RedownloadFailedDownloads",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RemoveCompletedDownloads",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RemoveFailedDownloads",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "SearchSleepDuration",
                table: "AppSettings");
        }
    }
}
