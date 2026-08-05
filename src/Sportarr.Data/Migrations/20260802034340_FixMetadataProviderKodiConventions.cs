using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixMetadataProviderKodiConventions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventNfoFilename",
                table: "MetadataProviders");

            migrationBuilder.AddColumn<bool>(
                name: "ShowNfo",
                table: "MetadataProviders",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.UpdateData(
                table: "MetadataProviders",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "ShowNfo", "UseEventFolder" },
                values: new object[] { true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowNfo",
                table: "MetadataProviders");

            migrationBuilder.AddColumn<string>(
                name: "EventNfoFilename",
                table: "MetadataProviders",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "MetadataProviders",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "EventNfoFilename", "UseEventFolder" },
                values: new object[] { "{Event Title}.nfo", true });
        }
    }
}
