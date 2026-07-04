using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEventFileLanguagesAndIndexerFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Audio/subtitle languages, stored as a JSON-serialized list of strings.
            // Defaults to "[]" so existing rows stay non-null after the migration.
            migrationBuilder.AddColumn<string>(
                name: "Languages",
                table: "EventFiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            // Indexer-side flags from the original release ("Freeleech", "Internal",
            // "Scene", "Nuked", etc.). Stored as a comma-separated token list.
            migrationBuilder.AddColumn<string>(
                name: "IndexerFlags",
                table: "EventFiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IndexerFlags", table: "EventFiles");
            migrationBuilder.DropColumn(name: "Languages", table: "EventFiles");
        }
    }
}
