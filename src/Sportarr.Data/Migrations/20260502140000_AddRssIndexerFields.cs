using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRssIndexerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plain-RSS indexer fields. Only consulted when Type = Rss; for
            // every other indexer type these stay at their defaults and
            // never get read. Cookie is shared with future authenticated-
            // feed support but introduced here because the RSS path needs
            // it first.
            migrationBuilder.AddColumn<string>(
                name: "Cookie",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RssAllowZeroSize",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RssUseEzrssFormat",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RssUseEnclosureUrl",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RssUseEnclosureLength",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RssParseSizeInDescription",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RssParseSeedersInDescription",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RssSizeElementName",
                table: "Indexers",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RssSizeElementName", table: "Indexers");
            migrationBuilder.DropColumn(name: "RssParseSeedersInDescription", table: "Indexers");
            migrationBuilder.DropColumn(name: "RssParseSizeInDescription", table: "Indexers");
            migrationBuilder.DropColumn(name: "RssUseEnclosureLength", table: "Indexers");
            migrationBuilder.DropColumn(name: "RssUseEnclosureUrl", table: "Indexers");
            migrationBuilder.DropColumn(name: "RssUseEzrssFormat", table: "Indexers");
            migrationBuilder.DropColumn(name: "RssAllowZeroSize", table: "Indexers");
            migrationBuilder.DropColumn(name: "Cookie", table: "Indexers");
        }
    }
}
