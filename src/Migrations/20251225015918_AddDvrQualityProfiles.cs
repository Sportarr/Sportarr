using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDvrQualityProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DvrQualityProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Preset = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoCodec = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AudioCodec = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    VideoBitrate = table.Column<int>(type: "INTEGER", nullable: false),
                    AudioBitrate = table.Column<int>(type: "INTEGER", nullable: false),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FrameRate = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    HardwareAcceleration = table.Column<int>(type: "INTEGER", nullable: false),
                    EncodingPreset = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ConstantRateFactor = table.Column<int>(type: "INTEGER", nullable: false),
                    Container = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CustomArguments = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    AudioChannels = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AudioSampleRate = table.Column<int>(type: "INTEGER", nullable: false),
                    Deinterlace = table.Column<bool>(type: "INTEGER", nullable: false),
                    EstimatedSizePerHourMb = table.Column<int>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Modified = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DvrQualityProfiles", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DvrQualityProfiles");
        }
    }
}
