using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGrabHistoryReleaseChoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CrowdHintShown",
                table: "GrabHistory",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "GrabSource",
                table: "GrabHistory",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IdentityRejectedAtGrab",
                table: "GrabHistory",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MatchScoreAtGrab",
                table: "GrabHistory",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CrowdHintShown",
                table: "GrabHistory");

            migrationBuilder.DropColumn(
                name: "GrabSource",
                table: "GrabHistory");

            migrationBuilder.DropColumn(
                name: "IdentityRejectedAtGrab",
                table: "GrabHistory");

            migrationBuilder.DropColumn(
                name: "MatchScoreAtGrab",
                table: "GrabHistory");
        }
    }
}
