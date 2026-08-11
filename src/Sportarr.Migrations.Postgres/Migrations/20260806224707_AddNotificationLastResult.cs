using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationLastResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastNotificationAt",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastNotificationError",
                table: "Notifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LastNotificationSucceeded",
                table: "Notifications",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastNotificationAt",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "LastNotificationError",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "LastNotificationSucceeded",
                table: "Notifications");
        }
    }
}
