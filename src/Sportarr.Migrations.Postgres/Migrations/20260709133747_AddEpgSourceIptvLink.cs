using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddEpgSourceIptvLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IptvSourceId",
                table: "EpgSources",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EpgSources_IptvSourceId",
                table: "EpgSources",
                column: "IptvSourceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EpgSources_IptvSourceId",
                table: "EpgSources");

            migrationBuilder.DropColumn(
                name: "IptvSourceId",
                table: "EpgSources");
        }
    }
}
