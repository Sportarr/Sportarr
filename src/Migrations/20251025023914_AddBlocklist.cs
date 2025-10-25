using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBlocklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Blocklist",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TorrentInfoHash = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Indexer = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Reason = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    BlockedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocklist", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Blocklist_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.UpdateData(
                table: "DelayProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 867, DateTimeKind.Utc).AddTicks(8860));

            migrationBuilder.UpdateData(
                table: "MetadataProviders",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 870, DateTimeKind.Utc).AddTicks(386));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 868, DateTimeKind.Utc).AddTicks(9276));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 2,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(34));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 3,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(36));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 4,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(77));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 5,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(79));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 6,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(81));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 7,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(89));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 8,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(91));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 9,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(93));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 10,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(94));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 11,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(95));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 12,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(97));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 13,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(98));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 14,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(100));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 15,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(101));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 16,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 39, 13, 869, DateTimeKind.Utc).AddTicks(109));

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_BlockedAt",
                table: "Blocklist",
                column: "BlockedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_EventId",
                table: "Blocklist",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Blocklist_TorrentInfoHash",
                table: "Blocklist",
                column: "TorrentInfoHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Blocklist");

            migrationBuilder.UpdateData(
                table: "DelayProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 633, DateTimeKind.Utc).AddTicks(5593));

            migrationBuilder.UpdateData(
                table: "MetadataProviders",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 636, DateTimeKind.Utc).AddTicks(1100));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 634, DateTimeKind.Utc).AddTicks(9746));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 2,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(597));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 3,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(600));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 4,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(602));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 5,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(604));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 6,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(606));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 7,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(608));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 8,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(622));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 9,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(624));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 10,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(627));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 11,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(629));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 12,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(632));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 13,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(635));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 14,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(637));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 15,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(640));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 16,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(642));
        }
    }
}
