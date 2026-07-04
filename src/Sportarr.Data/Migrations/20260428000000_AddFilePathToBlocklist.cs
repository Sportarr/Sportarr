using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFilePathToBlocklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FilePath is the dedup key for disk-discovered rejections. Nullable
            // because download-client-originated entries match by TorrentInfoHash
            // or Title and don't need a file path.
            migrationBuilder.AddColumn<string>(
                name: "FilePath",
                table: "Blocklist",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FilePath",
                table: "Blocklist");
        }
    }
}
