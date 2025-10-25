using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReleaseProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Required = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Ignored = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Preferred = table.Column<string>(type: "TEXT", nullable: false),
                    IncludePreferredWhenRenaming = table.Column<bool>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    IndexerId = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseProfiles", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "DelayProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 1, 49, 51, 473, DateTimeKind.Utc).AddTicks(3763));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReleaseProfiles");

            migrationBuilder.UpdateData(
                table: "DelayProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "Created",
                value: new DateTime(2025, 10, 25, 1, 44, 29, 850, DateTimeKind.Utc).AddTicks(7970));
        }
    }
}
