using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DownloadClientId = table.Column<int>(type: "INTEGER", nullable: false),
                    DownloadId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", nullable: true),
                    QualityScore = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestedEventId = table.Column<int>(type: "INTEGER", nullable: true),
                    SuggestedPart = table.Column<string>(type: "TEXT", nullable: true),
                    SuggestionConfidence = table.Column<int>(type: "INTEGER", nullable: false),
                    Detected = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Protocol = table.Column<string>(type: "TEXT", nullable: true),
                    TorrentInfoHash = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingImports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingImports_DownloadClients_DownloadClientId",
                        column: x => x.DownloadClientId,
                        principalTable: "DownloadClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PendingImports_Events_SuggestedEventId",
                        column: x => x.SuggestedEventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingImports_DownloadClientId",
                table: "PendingImports",
                column: "DownloadClientId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingImports_SuggestedEventId",
                table: "PendingImports",
                column: "SuggestedEventId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingImports_Status",
                table: "PendingImports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PendingImports_DownloadId",
                table: "PendingImports",
                column: "DownloadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingImports");
        }
    }
}
