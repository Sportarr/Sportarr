using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetadataProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventNfo = table.Column<bool>(type: "INTEGER", nullable: false),
                    FightCardNfo = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventImages = table.Column<bool>(type: "INTEGER", nullable: false),
                    FighterImages = table.Column<bool>(type: "INTEGER", nullable: false),
                    OrganizationLogos = table.Column<bool>(type: "INTEGER", nullable: false),
                    EventNfoFilename = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventPosterFilename = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EventFanartFilename = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UseEventFolder = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImageQuality = table.Column<int>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataProviders", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "DelayProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 633, DateTimeKind.Utc).AddTicks(5593));

            migrationBuilder.InsertData(
                table: "MetadataProviders",
                columns: new[] { "Id", "Created", "Enabled", "EventFanartFilename", "EventImages", "EventNfo", "EventNfoFilename", "EventPosterFilename", "FightCardNfo", "FighterImages", "ImageQuality", "LastModified", "Name", "OrganizationLogos", "Tags", "Type", "UseEventFolder" },
                values: new object[] { 1, new DateTime(2025, 10, 25, 2, 29, 7, 636, DateTimeKind.Utc).AddTicks(1100), false, "fanart.jpg", true, true, "{Event Title}.nfo", "poster.jpg", false, false, 95, null, "Kodi/XBMC", false, "[]", 0, true });

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 634, DateTimeKind.Utc).AddTicks(9746));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 2,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(597));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 3,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(600));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 4,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(602));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 5,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(604));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 6,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(606));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 7,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(608));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 8,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(622));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 9,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(624));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 10,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(627));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 11,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(629));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 12,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(632));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 13,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(635));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 14,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(637));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 15,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(640));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 16,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 29, 7, 635, DateTimeKind.Utc).AddTicks(642));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetadataProviders");

            migrationBuilder.UpdateData(
                table: "DelayProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 594, DateTimeKind.Utc).AddTicks(5702));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(2683));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 2,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4046));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 3,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4052));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 4,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4054));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 5,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4056));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 6,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4102));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 7,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4124));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 8,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4126));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 9,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4128));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 10,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4129));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 11,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4130));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 12,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4132));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 13,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4133));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 14,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4135));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 15,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4137));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 16,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 20, 6, 596, DateTimeKind.Utc).AddTicks(4146));
        }
    }
}
