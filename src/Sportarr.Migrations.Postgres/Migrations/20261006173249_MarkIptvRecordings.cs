using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
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
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Recordings imported straight from the DVR folder carry Source
            // "IPTV". Recordings imported through the library parse as
            // "HDTV", but their name carries the DVR quality token
            // ("HDTV-1080p.DVR") under the default naming format.
            migrationBuilder.Sql(@"
                UPDATE ""EventFiles"" SET ""IsIptvRecording"" = TRUE
                WHERE ""Source"" = 'IPTV'
                   OR ((LOWER(""FilePath"") LIKE '%p.dvr.%'
                     OR LOWER(""FilePath"") LIKE '%p.dvr %'
                     OR LOWER(""FilePath"") LIKE '%sdtv.dvr.%'
                     OR LOWER(""FilePath"") LIKE '%sdtv.dvr %')
                    AND LOWER(REPLACE(""FilePath"", CHR(92), '/')) NOT LIKE '%p.dvr.%/%'
                    AND LOWER(REPLACE(""FilePath"", CHR(92), '/')) NOT LIKE '%p.dvr %/%'
                    AND LOWER(REPLACE(""FilePath"", CHR(92), '/')) NOT LIKE '%sdtv.dvr.%/%'
                    AND LOWER(REPLACE(""FilePath"", CHR(92), '/')) NOT LIKE '%sdtv.dvr %/%');");

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
