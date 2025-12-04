using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIndexerStatusAndRateLimiting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GrabLimit",
                table: "Indexers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QueryLimit",
                table: "Indexers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RequestDelayMs",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "IndexerStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFailure = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFailureReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DisabledUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QueriesThisHour = table.Column<int>(type: "INTEGER", nullable: false),
                    GrabsThisHour = table.Column<int>(type: "INTEGER", nullable: false),
                    HourResetTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSuccess = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastRssSyncAttempt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RateLimitedUntil = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexerStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IndexerStatuses_Indexers_IndexerId",
                        column: x => x.IndexerId,
                        principalTable: "Indexers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IndexerStatuses_DisabledUntil",
                table: "IndexerStatuses",
                column: "DisabledUntil");

            migrationBuilder.CreateIndex(
                name: "IX_IndexerStatuses_IndexerId",
                table: "IndexerStatuses",
                column: "IndexerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IndexerStatuses");

            migrationBuilder.DropColumn(
                name: "GrabLimit",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "QueryLimit",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "RequestDelayMs",
                table: "Indexers");
        }
    }
}
