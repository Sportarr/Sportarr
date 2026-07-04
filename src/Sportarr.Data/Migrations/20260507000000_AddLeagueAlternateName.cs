using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueAlternateName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Comma-separated league alternate names sourced from
            // TheSportsDB strLeagueAlternate (e.g. "English Prem Rugby"
            // has alternate "Gallagher Premiership Rugby" — the sponsor
            // name scene release groups actually use). The release
            // matcher splits on commas and accepts any alias as a valid
            // league reference. Mirrors the existing AlternateName
            // column on Teams.
            migrationBuilder.AddColumn<string>(
                name: "AlternateName",
                table: "Leagues",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AlternateName", table: "Leagues");
        }
    }
}
