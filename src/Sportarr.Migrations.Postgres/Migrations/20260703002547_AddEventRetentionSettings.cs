using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddEventRetentionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableEventRetention",
                table: "MediaManagementSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EventRetentionDays",
                table: "MediaManagementSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableEventRetention",
                table: "MediaManagementSettings");

            migrationBuilder.DropColumn(
                name: "EventRetentionDays",
                table: "MediaManagementSettings");
        }
    }
}
