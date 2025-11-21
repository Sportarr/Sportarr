using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEventFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PartName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PartNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastVerified = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Exists = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventFiles_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventFiles_EventId",
                table: "EventFiles",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventFiles_PartNumber",
                table: "EventFiles",
                column: "PartNumber");

            migrationBuilder.CreateIndex(
                name: "IX_EventFiles_Exists",
                table: "EventFiles",
                column: "Exists");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventFiles");
        }
    }
}
