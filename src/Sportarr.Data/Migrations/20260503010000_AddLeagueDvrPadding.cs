using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLeagueDvrPadding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Per-league pre/post DVR padding overrides. Null falls
            // back to sport-aware defaults at scheduling time.
            migrationBuilder.AddColumn<int>(
                name: "DvrPrePadMinutes",
                table: "Leagues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DvrPostRollMinutes",
                table: "Leagues",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DvrPrePadMinutes", table: "Leagues");
            migrationBuilder.DropColumn(name: "DvrPostRollMinutes", table: "Leagues");
        }
    }
}
