using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSonarrIndexerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdditionalParameters",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApiPath",
                table: "Indexers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DownloadClientId",
                table: "Indexers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableAutomaticSearch",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableInteractiveSearch",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "EnableRss",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MultiLanguages",
                table: "Indexers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RejectBlocklistedTorrentHashes",
                table: "Indexers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SeasonPackSeedTime",
                table: "Indexers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tags",
                table: "Indexers",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdditionalParameters",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "ApiPath",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "DownloadClientId",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "EnableAutomaticSearch",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "EnableInteractiveSearch",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "EnableRss",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "MultiLanguages",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "RejectBlocklistedTorrentHashes",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "SeasonPackSeedTime",
                table: "Indexers");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Indexers");
        }
    }
}
