using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropPersistedRootFolderState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop the live-disk fields that were persisted alongside Path.
            // GET /api/rootfolder always recomputes them, and every consumer
            // re-checks Directory.Exists / DiskSpaceService before using the
            // result, so the persisted columns just produced drift between
            // "what the row says" and "what the disk actually has." Match
            // the upstream model: store only Path + Created and compute the
            // rest at request time.
            migrationBuilder.DropColumn(name: "Accessible", table: "RootFolders");
            migrationBuilder.DropColumn(name: "FreeSpace", table: "RootFolders");
            migrationBuilder.DropColumn(name: "TotalSpace", table: "RootFolders");
            migrationBuilder.DropColumn(name: "LastChecked", table: "RootFolders");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Accessible",
                table: "RootFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
            migrationBuilder.AddColumn<long>(
                name: "FreeSpace",
                table: "RootFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
            migrationBuilder.AddColumn<long>(
                name: "TotalSpace",
                table: "RootFolders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
            migrationBuilder.AddColumn<DateTime>(
                name: "LastChecked",
                table: "RootFolders",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));
        }
    }
}
