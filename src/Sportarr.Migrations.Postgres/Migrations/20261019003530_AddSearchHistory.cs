using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SearchHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    Part = table.Column<string>(type: "text", nullable: true),
                    SearchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    ReleasesFound = table.Column<int>(type: "integer", nullable: false),
                    ReleasesPassed = table.Column<int>(type: "integer", nullable: false),
                    SelectedRelease = table.Column<string>(type: "text", nullable: true),
                    QualityProfile = table.Column<string>(type: "text", nullable: true),
                    ReasonsJson = table.Column<string>(type: "text", nullable: false),
                    ReleasesJson = table.Column<string>(type: "text", nullable: true),
                    IndexersJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchHistory_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistory_EventId_SearchedAt",
                table: "SearchHistory",
                columns: new[] { "EventId", "SearchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchHistory_SearchedAt",
                table: "SearchHistory",
                column: "SearchedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchHistory");
        }
    }
}
