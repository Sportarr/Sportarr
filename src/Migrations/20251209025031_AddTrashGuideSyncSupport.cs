using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTrashGuideSyncSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSynced",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTrashScoreSync",
                table: "QualityProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrashId",
                table: "QualityProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrashScoreSet",
                table: "QualityProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCustomized",
                table: "CustomFormats",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSynced",
                table: "CustomFormats",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAt",
                table: "CustomFormats",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrashCategory",
                table: "CustomFormats",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrashDefaultScore",
                table: "CustomFormats",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrashDescription",
                table: "CustomFormats",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrashId",
                table: "CustomFormats",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "IsSynced", "LastTrashScoreSync", "TrashId", "TrashScoreSet" },
                values: new object[] { false, null, null, null });

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "IsSynced", "LastTrashScoreSync", "TrashId", "TrashScoreSet" },
                values: new object[] { false, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSynced",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "LastTrashScoreSync",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "TrashId",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "TrashScoreSet",
                table: "QualityProfiles");

            migrationBuilder.DropColumn(
                name: "IsCustomized",
                table: "CustomFormats");

            migrationBuilder.DropColumn(
                name: "IsSynced",
                table: "CustomFormats");

            migrationBuilder.DropColumn(
                name: "LastSyncedAt",
                table: "CustomFormats");

            migrationBuilder.DropColumn(
                name: "TrashCategory",
                table: "CustomFormats");

            migrationBuilder.DropColumn(
                name: "TrashDefaultScore",
                table: "CustomFormats");

            migrationBuilder.DropColumn(
                name: "TrashDescription",
                table: "CustomFormats");

            migrationBuilder.DropColumn(
                name: "TrashId",
                table: "CustomFormats");
        }
    }
}
