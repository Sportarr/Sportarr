using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.src.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFightsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Fights");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Fights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    FightOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Fighter1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Fighter2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsMainEvent = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsTitleFight = table.Column<bool>(type: "INTEGER", nullable: false),
                    Result = table.Column<string>(type: "TEXT", nullable: true),
                    WeightClass = table.Column<string>(type: "TEXT", nullable: true),
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
