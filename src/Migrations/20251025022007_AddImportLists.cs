using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportLists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportLists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ListType = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    QualityProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    RootFolderPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    MonitorEvents = table.Column<bool>(type: "INTEGER", nullable: false),
                    SearchOnAdd = table.Column<bool>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    MinimumDaysBeforeEvent = table.Column<int>(type: "INTEGER", nullable: false),
                    OrganizationFilter = table.Column<string>(type: "TEXT", nullable: true),
                    LastSync = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncMessage = table.Column<string>(type: "TEXT", nullable: true),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportLists", x => x.Id);
                });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportLists");

            migrationBuilder.UpdateData(
                table: "DelayProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 420, DateTimeKind.Utc).AddTicks(9891));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(597));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 2,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1379));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 3,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1382));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 4,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1383));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 5,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1385));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 6,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1387));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 7,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1405));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 8,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1407));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 9,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1408));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 10,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1409));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 11,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1411));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 12,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1412));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 13,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1413));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 14,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1415));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 15,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1416));

            migrationBuilder.UpdateData(
                table: "QualityDefinitions",
                keyColumn: "Id",
                keyValue: 16,
                column: "Created",
                value: new DateTime(2025, 10, 25, 2, 11, 58, 422, DateTimeKind.Utc).AddTicks(1425));
        }
    }
}
