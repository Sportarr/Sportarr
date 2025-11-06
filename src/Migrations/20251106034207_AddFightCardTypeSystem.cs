using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFightCardTypeSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "CardType",
                table: "FightCards",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "FightCardId",
                table: "DownloadQueue",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_FightCardId",
                table: "DownloadQueue",
                column: "FightCardId");

            migrationBuilder.AddForeignKey(
                name: "FK_DownloadQueue_FightCards_FightCardId",
                table: "DownloadQueue",
                column: "FightCardId",
                principalTable: "FightCards",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DownloadQueue_FightCards_FightCardId",
                table: "DownloadQueue");

            migrationBuilder.DropIndex(
                name: "IX_DownloadQueue_FightCardId",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "FightCardId",
                table: "DownloadQueue");

            migrationBuilder.AlterColumn<string>(
                name: "CardType",
                table: "FightCards",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");
        }
    }
}
