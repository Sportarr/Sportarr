using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFightCardAndOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DownloadQueue_FightCards_FightCardId",
                table: "DownloadQueue");

            migrationBuilder.DropTable(
                name: "FightCards");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Events_Organization",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_DownloadQueue_FightCardId",
                table: "DownloadQueue");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "Fights");

            migrationBuilder.DropColumn(
                name: "Round",
                table: "Fights");

            migrationBuilder.DropColumn(
                name: "Organization",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "FightCardId",
                table: "DownloadQueue");

            migrationBuilder.RenameColumn(
                name: "Time",
                table: "Fights",
                newName: "Winner");

            migrationBuilder.AddColumn<int>(
                name: "FightOrder",
                table: "Fights",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsTitleFight",
                table: "Fights",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FightOrder",
                table: "Fights");

            migrationBuilder.DropColumn(
                name: "IsTitleFight",
                table: "Fights");

            migrationBuilder.RenameColumn(
                name: "Winner",
                table: "Fights",
                newName: "Time");

            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "Fights",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "Fights",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Organization",
                table: "Events",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FightCardId",
                table: "DownloadQueue",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FightCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    AirDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CardNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CardType = table.Column<int>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: true),
                    HasFile = table.Column<bool>(type: "INTEGER", nullable: false),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    Quality = table.Column<string>(type: "TEXT", nullable: true),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FightCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FightCards_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Added = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Monitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PosterUrl = table.Column<string>(type: "TEXT", nullable: true),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_Organization",
                table: "Events",
                column: "Organization");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadQueue_FightCardId",
                table: "DownloadQueue",
                column: "FightCardId");

            migrationBuilder.CreateIndex(
                name: "IX_FightCards_EventId",
                table: "FightCards",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Name",
                table: "Organizations",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DownloadQueue_FightCards_FightCardId",
                table: "DownloadQueue",
                column: "FightCardId",
                principalTable: "FightCards",
                principalColumn: "Id");
        }
    }
}
