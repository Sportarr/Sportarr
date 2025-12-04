using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class MakeImportHistoryEventIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImportHistories_DownloadQueue_DownloadQueueItemId",
                table: "ImportHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_ImportHistories_Events_EventId",
                table: "ImportHistories");

            migrationBuilder.AlterColumn<int>(
                name: "EventId",
                table: "ImportHistories",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AddColumn<string>(
                name: "StatusMessages",
                table: "DownloadQueue",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportHistories_DownloadQueue_DownloadQueueItemId",
                table: "ImportHistories",
                column: "DownloadQueueItemId",
                principalTable: "DownloadQueue",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ImportHistories_Events_EventId",
                table: "ImportHistories",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImportHistories_DownloadQueue_DownloadQueueItemId",
                table: "ImportHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_ImportHistories_Events_EventId",
                table: "ImportHistories");

            migrationBuilder.DropColumn(
                name: "StatusMessages",
                table: "DownloadQueue");

            migrationBuilder.AlterColumn<int>(
                name: "EventId",
                table: "ImportHistories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ImportHistories_DownloadQueue_DownloadQueueItemId",
                table: "ImportHistories",
                column: "DownloadQueueItemId",
                principalTable: "DownloadQueue",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportHistories_Events_EventId",
                table: "ImportHistories",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
