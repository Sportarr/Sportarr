using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataProvidersAndRenameToUniversalTerminology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "OrganizationLogos",
                table: "MetadataProviders",
                newName: "PlayerImages");

            migrationBuilder.RenameColumn(
                name: "FighterImages",
                table: "MetadataProviders",
                newName: "LeagueLogos");

            migrationBuilder.RenameColumn(
                name: "FightCardNfo",
                table: "MetadataProviders",
                newName: "EventCardNfo");

            migrationBuilder.RenameColumn(
                name: "OrganizationFilter",
                table: "ImportLists",
                newName: "LeagueFilter");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PlayerImages",
                table: "MetadataProviders",
                newName: "OrganizationLogos");

            migrationBuilder.RenameColumn(
                name: "LeagueLogos",
                table: "MetadataProviders",
                newName: "FighterImages");

            migrationBuilder.RenameColumn(
                name: "EventCardNfo",
                table: "MetadataProviders",
                newName: "FightCardNfo");

            migrationBuilder.RenameColumn(
                name: "LeagueFilter",
                table: "ImportLists",
                newName: "OrganizationFilter");
        }
    }
}
