using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <summary>
    /// Removes the Fights table as part of the TheSportsDB alignment
    /// Fighting sports are now handled the same as all other sports (no special Fight subdivision)
    /// </summary>
    public partial class RemoveFightsTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the Fights table
            migrationBuilder.DropTable(
                name: "Fights");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate Fights table for rollback (if needed)
            migrationBuilder.CreateTable(
                name: "Fights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    Fighter1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Fighter2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    WeightClass = table.Column<string>(type: "TEXT", nullable: true),
                    IsMainEvent = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsTitleFight = table.Column<bool>(type: "INTEGER", nullable: false),
                    FightOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: true),
                    Winner = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fights", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fights_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Fights_EventId",
                table: "Fights",
                column: "EventId");
        }
    }
}
