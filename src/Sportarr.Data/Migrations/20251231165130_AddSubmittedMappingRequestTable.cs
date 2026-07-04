using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmittedMappingRequestTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubmittedMappingRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RemoteRequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    SportType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LeagueName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReleaseNames = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ReviewNotes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UserNotified = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmittedMappingRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubmittedMappingRequests_RemoteRequestId",
                table: "SubmittedMappingRequests",
                column: "RemoteRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubmittedMappingRequests_Status",
                table: "SubmittedMappingRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubmittedMappingRequests_UserNotified",
                table: "SubmittedMappingRequests",
                column: "UserNotified");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubmittedMappingRequests");
        }
    }
}
