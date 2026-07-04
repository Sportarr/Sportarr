using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonPosters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SeasonPosters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LeagueId = table.Column<int>(type: "integer", nullable: false),
                    Season = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PosterUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeasonPosters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeasonPosters_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SeasonPosters_LeagueId_Season",
                table: "SeasonPosters",
                columns: new[] { "LeagueId", "Season" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SeasonPosters");
        }
    }
}
