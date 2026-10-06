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
                   OR ""FilePath"" LIKE '%p.DVR.%'
                   OR ""FilePath"" LIKE '%p.DVR %'
                   OR ""FilePath"" LIKE '%SDTV.DVR.%'
                   OR ""FilePath"" LIKE '%SDTV.DVR %'");

            // A recording that reached the library through a rescan or a
            // manual import parses as "HDTV" and, under a naming format
            // without the DVR quality token, carries nothing above. The DVR
            // records to MPEG-TS and indexer releases carry a release title,
            // so an HDTV .ts file without one is a recording
            // (SourcePrecedence.LooksLikeIptvRecording).
            migrationBuilder.Sql(@"
                UPDATE ""EventFiles"" SET ""IsIptvRecording"" = TRUE
                WHERE ""IsIptvRecording"" = FALSE
                  AND ""ReleaseTitle"" IS NULL
                  AND LOWER(""FilePath"") LIKE '%.ts'
                  AND UPPER(""Quality"") LIKE '%HDTV%'");
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
