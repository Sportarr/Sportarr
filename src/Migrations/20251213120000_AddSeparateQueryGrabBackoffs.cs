using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <summary>
    /// Adds separate query/grab backoff tracking to IndexerStatus (Sonarr #3132 pattern)
    /// - QueryFailures/QueryDisabledUntil: Backoff for search/RSS failures
    /// - GrabFailures/GrabDisabledUntil: Backoff for download failures (separate from query)
    /// - ConnectionErrors: Track DNS/network errors without escalating backoff
    ///
    /// This allows searching to continue even when grab operations are failing,
    /// which is important for indexer health monitoring.
    /// </summary>
    public partial class AddSeparateQueryGrabBackoffs : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Query failure tracking (search/RSS failures)
            migrationBuilder.AddColumn<int>(
                name: "QueryFailures",
                table: "IndexerStatuses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "QueryDisabledUntil",
                table: "IndexerStatuses",
                type: "TEXT",
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

            // Grab failure tracking (download failures) - separate from query failures
            migrationBuilder.AddColumn<int>(
                name: "GrabFailures",
                table: "IndexerStatuses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "GrabDisabledUntil",
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

            // Connection error tracking (DNS/network issues don't escalate backoff)
            migrationBuilder.AddColumn<int>(
                name: "ConnectionErrors",
                table: "IndexerStatuses",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastConnectionError",
                table: "IndexerStatuses",
                type: "TEXT",
                nullable: true);

            // Add indexes for new disabled columns
            migrationBuilder.CreateIndex(
                name: "IX_IndexerStatuses_QueryDisabledUntil",
                table: "IndexerStatuses",
                column: "QueryDisabledUntil");

            migrationBuilder.CreateIndex(
                name: "IX_IndexerStatuses_GrabDisabledUntil",
                table: "IndexerStatuses",
                column: "GrabDisabledUntil");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IndexerStatuses_QueryDisabledUntil",
                table: "IndexerStatuses");

            migrationBuilder.DropIndex(
                name: "IX_IndexerStatuses_GrabDisabledUntil",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "QueryFailures",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "QueryDisabledUntil",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "LastQueryFailure",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "LastQueryFailureReason",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "GrabFailures",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "GrabDisabledUntil",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "LastGrabFailure",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "LastGrabFailureReason",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "ConnectionErrors",
                table: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "LastConnectionError",
                table: "IndexerStatuses");
        }
    }
}
