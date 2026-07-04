using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddDevelopmentSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DevelopmentSettings",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DevelopmentSettings",
                table: "AppSettings");
        }
    }
}
