using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowedTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FollowedTeams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Sport = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    BadgeUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLeagueDiscovery = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowedTeams", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowedTeams_ExternalId",
                table: "FollowedTeams",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowedTeams_Sport",
                table: "FollowedTeams",
                column: "Sport");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FollowedTeams");
        }
    }
}
