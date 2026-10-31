using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddFullEventUndoAndParking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PartsBeforeFullEvent",
                table: "Leagues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartsBeforeFullEvent",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsParked",
                table: "EventFiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PartsBeforeFullEvent",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "PartsBeforeFullEvent",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "IsParked",
                table: "EventFiles");
        }
    }
}
