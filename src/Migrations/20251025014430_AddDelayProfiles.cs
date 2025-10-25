using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDelayProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DelayProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    PreferredProtocol = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UsenetDelay = table.Column<int>(type: "INTEGER", nullable: false),
                    TorrentDelay = table.Column<int>(type: "INTEGER", nullable: false),
                    BypassIfHighestQuality = table.Column<bool>(type: "INTEGER", nullable: false),
                    BypassIfAboveCustomFormatScore = table.Column<bool>(type: "INTEGER", nullable: false),
                    MinimumCustomFormatScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelayProfiles", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "DelayProfiles",
                columns: new[] { "Id", "BypassIfAboveCustomFormatScore", "BypassIfHighestQuality", "Created", "LastModified", "MinimumCustomFormatScore", "Order", "PreferredProtocol", "Tags", "TorrentDelay", "UsenetDelay" },
                values: new object[] { 1, false, false, new DateTime(2025, 10, 25, 1, 44, 29, 850, DateTimeKind.Utc).AddTicks(7970), null, 0, 1, "Usenet", "[]", 0, 0 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelayProfiles");
        }
    }
}
