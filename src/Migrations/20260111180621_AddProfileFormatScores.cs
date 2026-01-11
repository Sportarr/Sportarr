using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileFormatScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only apply FormatItems if the TRaSH custom formats were actually inserted
            // Check if format ID 1 has our specific TrashId (meaning it's the TRaSH format, not user's existing format)
            // This prevents applying incorrect scores to existing user formats that happen to have IDs 1-7
            migrationBuilder.Sql(@"
                UPDATE ""QualityProfiles""
                SET ""FormatItems"" = '[{""Id"":0,""FormatId"":1,""Format"":null,""Score"":-10000},{""Id"":0,""FormatId"":2,""Format"":null,""Score"":-10000},{""Id"":0,""FormatId"":3,""Format"":null,""Score"":5},{""Id"":0,""FormatId"":4,""Format"":null,""Score"":-10000},{""Id"":0,""FormatId"":5,""Format"":null,""Score"":-10000},{""Id"":0,""FormatId"":6,""Format"":null,""Score"":0},{""Id"":0,""FormatId"":7,""Format"":null,""Score"":10}]'
                WHERE ""Id"" = 1
                AND EXISTS (SELECT 1 FROM ""CustomFormats"" WHERE ""Id"" = 1 AND ""TrashId"" = '85c61753-c413-4d8b-9e0d-f7f6f61e8c42');
            ");

            migrationBuilder.Sql(@"
                UPDATE ""QualityProfiles""
                SET ""FormatItems"" = '[{""Id"":0,""FormatId"":1,""Format"":null,""Score"":-10000},{""Id"":0,""FormatId"":2,""Format"":null,""Score"":-10000},{""Id"":0,""FormatId"":3,""Format"":null,""Score"":5},{""Id"":0,""FormatId"":4,""Format"":null,""Score"":-10000},{""Id"":0,""FormatId"":5,""Format"":null,""Score"":-10000},{""Id"":0,""FormatId"":6,""Format"":null,""Score"":0},{""Id"":0,""FormatId"":7,""Format"":null,""Score"":10}]'
                WHERE ""Id"" = 2
                AND EXISTS (SELECT 1 FROM ""CustomFormats"" WHERE ""Id"" = 1 AND ""TrashId"" = '85c61753-c413-4d8b-9e0d-f7f6f61e8c42');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 1,
                column: "FormatItems",
                value: "[]");

            migrationBuilder.UpdateData(
                table: "QualityProfiles",
                keyColumn: "Id",
                keyValue: 2,
                column: "FormatItems",
                value: "[]");
        }
    }
}
