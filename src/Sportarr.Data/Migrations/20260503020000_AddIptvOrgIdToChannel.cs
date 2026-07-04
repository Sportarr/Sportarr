using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIptvOrgIdToChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Canonical iptv-org channel id (e.g. "ESPN.us") and the
            // matcher's confidence in that assignment. Both nullable
            // because not every user channel will resolve to a known
            // public channel.
            migrationBuilder.AddColumn<string>(
                name: "IptvOrgId",
                table: "IptvChannels",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IptvOrgConfidence",
                table: "IptvChannels",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IptvChannels_IptvOrgId",
                table: "IptvChannels",
                column: "IptvOrgId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_IptvChannels_IptvOrgId", table: "IptvChannels");
            migrationBuilder.DropColumn(name: "IptvOrgConfidence", table: "IptvChannels");
            migrationBuilder.DropColumn(name: "IptvOrgId", table: "IptvChannels");
        }
    }
}
