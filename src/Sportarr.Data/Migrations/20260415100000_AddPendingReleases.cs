using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingReleases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Guid = table.Column<string>(type: "TEXT", nullable: false),
                    DownloadUrl = table.Column<string>(type: "TEXT", nullable: false),
                    InfoUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Indexer = table.Column<string>(type: "TEXT", nullable: false),
                    IndexerId = table.Column<int>(type: "INTEGER", nullable: true),
                    TorrentInfoHash = table.Column<string>(type: "TEXT", nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: true),
                    Codec = table.Column<string>(type: "TEXT", nullable: true),
                    Language = table.Column<string>(type: "TEXT", nullable: true),
                    ReleaseGroup = table.Column<string>(type: "TEXT", nullable: true),
                    QualityScore = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomFormatScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Part = table.Column<string>(type: "TEXT", nullable: true),
                    Seeders = table.Column<int>(type: "INTEGER", nullable: true),
                    Leechers = table.Column<int>(type: "INTEGER", nullable: true),
                    PublishDate = table.Column<System.DateTime>(type: "TEXT", nullable: false),
                    AddedToPendingAt = table.Column<System.DateTime>(type: "TEXT", nullable: false),
                    ReleasableAt = table.Column<System.DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingReleases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingReleases_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingReleases_EventId",
                table: "PendingReleases",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingReleases_Status_ReleasableAt",
                table: "PendingReleases",
                columns: new[] { "Status", "ReleasableAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PendingReleases");
        }
    }
}
