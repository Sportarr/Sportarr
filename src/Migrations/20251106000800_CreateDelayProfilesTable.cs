using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class CreateDelayProfilesTable : Migration
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
                    Order = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    PreferredProtocol = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Usenet"),
                    UsenetDelay = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    TorrentDelay = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    BypassIfHighestQuality = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    BypassIfAboveCustomFormatScore = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    MinimumCustomFormatScore = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelayProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DelayProfiles_Order",
                table: "DelayProfiles",
                column: "Order");

            // No default delay profile - users should explicitly create if needed
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DelayProfiles");
        }
    }
}
