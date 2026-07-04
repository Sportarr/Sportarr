using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddReorganizeFoldersColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReorganizeFolders",
                table: "MediaManagementSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReorganizeFolders",
                table: "MediaManagementSettings");
        }
    }
}
