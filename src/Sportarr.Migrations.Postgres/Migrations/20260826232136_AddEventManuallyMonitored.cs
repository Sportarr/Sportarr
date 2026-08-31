using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddEventManuallyMonitored : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ManuallyMonitored",
                table: "Events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Nothing records who monitored an event before this column
            // existed, and the out-of-filter cleanup reads it to decide what
            // to keep. Treat every monitored event as a person's choice, so
            // an upgrade cannot take away a game someone picked by hand.
            migrationBuilder.Sql(@"UPDATE ""Events"" SET ""ManuallyMonitored"" = ""Monitored""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManuallyMonitored",
                table: "Events");
        }
    }
}
