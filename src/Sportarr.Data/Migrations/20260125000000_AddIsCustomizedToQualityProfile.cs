using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIsCustomizedToQualityProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCustomized",
                table: "QualityProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCustomized",
                table: "QualityProfiles");
        }
    }
}
