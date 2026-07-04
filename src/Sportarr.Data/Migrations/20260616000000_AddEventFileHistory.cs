using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <summary>
    /// Adds the EventFileHistory table, which records file removals (manual
    /// deletions and upgrade replacements) so an event's history timeline shows
    /// the full chain: grabbed -> imported -> deleted -> re-grabbed, the way the
    /// other *arr apps do. Grabs and imports are already tracked in GrabHistory
    /// and ImportHistory; this fills in the removals those don't capture.
    ///
    /// The table is created at runtime by the raw-SQL safety net in
    /// DatabaseInitializer (matching how recent schema changes are applied in
    /// this project); this migration documents the change.
    /// </summary>
    public partial class AddEventFileHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventFileHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceTitle = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Part = table.Column<string>(type: "TEXT", nullable: true),
                    Date = table.Column<System.DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventFileHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventFileHistory_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventFileHistory_EventId",
                table: "EventFileHistory",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventFileHistory_Date",
                table: "EventFileHistory",
                column: "Date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EventFileHistory");
        }
    }
}
