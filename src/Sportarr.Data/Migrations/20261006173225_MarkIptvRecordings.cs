using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class MarkIptvRecordings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsIptvRecording",
                table: "EventFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Recordings imported straight from the DVR folder carry Source
            // "IPTV". Recordings imported through the library parse as
            // "HDTV", but their name carries the DVR quality token
            // ("HDTV-1080p.DVR") under the default naming format.
            migrationBuilder.Sql(@"
                UPDATE ""EventFiles"" SET ""IsIptvRecording"" = 1
                WHERE ""Source"" = 'IPTV'
                   OR ""FilePath"" LIKE '%p.DVR.%'
                   OR ""FilePath"" LIKE '%p.DVR %'
                   OR ""FilePath"" LIKE '%SDTV.DVR.%'
                   OR ""FilePath"" LIKE '%SDTV.DVR %'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsIptvRecording",
                table: "EventFiles");
        }
    }
}
