using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReleaseProfilesTable : Migration
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
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    Required = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Ignored = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    Preferred = table.Column<string>(type: "TEXT", nullable: false),
                    IncludePreferredWhenRenaming = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    IndexerId = table.Column<string>(type: "TEXT", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastModified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseProfiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseProfiles_Name",
                table: "ReleaseProfiles",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReleaseProfiles");
        }
    }
}
