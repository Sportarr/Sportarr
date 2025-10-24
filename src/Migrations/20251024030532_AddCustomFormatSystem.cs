using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFormatSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CutoffFormatScore",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CutoffQuality",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FormatItems",
                table: "QualityProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "MaxSize",
                table: "QualityProfiles",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinFormatScore",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MinSize",
                table: "QualityProfiles",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomFormats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IncludeCustomFormatWhenRenaming = table.Column<bool>(type: "INTEGER", nullable: false),
                    Specifications = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFormats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormatSpecifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Negate = table.Column<bool>(type: "INTEGER", nullable: false),
                    Required = table.Column<bool>(type: "INTEGER", nullable: false),
                    Pattern = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormatSpecifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualityDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Weight = table.Column<int>(type: "INTEGER", nullable: false),
                    MinSize = table.Column<double>(type: "REAL", nullable: true),
                    MaxSize = table.Column<double>(type: "REAL", nullable: true),
                    PreferredSize = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProfileFormatItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FormatId = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileFormatItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileFormatItems_CustomFormats_FormatId",
                        column: x => x.FormatId,
                        principalTable: "CustomFormats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CutoffFormatScore", "CutoffQuality", "FormatItems", "MaxSize", "MinFormatScore", "MinSize" },
                values: new object[] { null, null, "[]", null, null, null });

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CutoffFormatScore", "CutoffQuality", "FormatItems", "MaxSize", "MinFormatScore", "MinSize" },
                values: new object[] { null, null, "[]", null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFormats_Name",
                table: "CustomFormats",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileFormatItems_FormatId",
                table: "ProfileFormatItems",
                column: "FormatId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityDefinitions_Name",
                table: "QualityDefinitions",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormatSpecifications");

            migrationBuilder.DropTable(
                name: "ProfileFormatItems");

            migrationBuilder.DropTable(
                name: "QualityDefinitions");

            migrationBuilder.DropTable(
                name: "CustomFormats");

            migrationBuilder.DropColumn(
                name: "CutoffFormatScore",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "CutoffQuality",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "FormatItems",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "MaxSize",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "MinFormatScore",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "MinSize",
                table: "QualityProfiles");
        }
    }
}
