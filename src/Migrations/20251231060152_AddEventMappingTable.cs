using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddEventMappingTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop SceneMappings if it exists (was never released to production, so may not exist)
            migrationBuilder.Sql("DROP TABLE IF EXISTS SceneMappings;");

            migrationBuilder.CreateTable(
                name: "EventMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RemoteId = table.Column<int>(type: "INTEGER", nullable: true),
                    SportType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LeagueId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    LeagueName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReleaseNames = table.Column<string>(type: "TEXT", nullable: false),
                    SessionPatternsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    QueryConfigJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventMappings_IsActive",
                table: "EventMappings",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_EventMappings_Priority",
                table: "EventMappings",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_EventMappings_RemoteId",
                table: "EventMappings",
                column: "RemoteId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMappings_Source",
                table: "EventMappings",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_EventMappings_SportType_LeagueId",
                table: "EventMappings",
                columns: new[] { "SportType", "LeagueId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventMappings");

            migrationBuilder.CreateTable(
                name: "SceneMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LeagueId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    LeagueName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    QueryConfigJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RemoteId = table.Column<int>(type: "INTEGER", nullable: true),
                    SceneNames = table.Column<string>(type: "TEXT", nullable: false),
                    SessionPatternsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SportType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SceneMappings_IsActive",
                table: "SceneMappings",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SceneMappings_Priority",
                table: "SceneMappings",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_SceneMappings_RemoteId",
                table: "SceneMappings",
                column: "RemoteId");

            migrationBuilder.CreateIndex(
                name: "IX_SceneMappings_Source",
                table: "SceneMappings",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_SceneMappings_SportType_LeagueId",
                table: "SceneMappings",
                columns: new[] { "SportType", "LeagueId" },
                unique: true);
        }
    }
}
