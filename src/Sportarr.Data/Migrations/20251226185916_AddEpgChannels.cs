using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddEpgChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EpgChannels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EpgSourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IconUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpgChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpgChannels_EpgSources_EpgSourceId",
                        column: x => x.EpgSourceId,
                        principalTable: "EpgSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EpgChannels_ChannelId",
                table: "EpgChannels",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgChannels_EpgSourceId",
                table: "EpgChannels",
                column: "EpgSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_EpgChannels_NormalizedName",
                table: "EpgChannels",
                column: "NormalizedName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EpgChannels");
        }
    }
}
