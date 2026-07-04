using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFailDownloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Per-indexer FailDownloads policy. Stored as a JSON list of
            // FailDownloads enum values (0=Executables, 1=Potentially-
            // Dangerous, 2=UserDefinedExtensions). Default "[]" means the
            // import path keeps its existing warn-and-continue semantics
            // for indexers added before this column existed.
            migrationBuilder.AddColumn<string>(
                name: "FailDownloads",
                table: "Indexers",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            // Comma-separated user list paired with the
            // FailDownloads.UserDefinedExtensions enum value. Lives next
            // to ExtraFileExtensions on MediaManagementSettings to mirror
            // the upstream Settings > Media Management UX.
            migrationBuilder.AddColumn<string>(
                name: "UserRejectedExtensions",
                table: "MediaManagementSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "UserRejectedExtensions", table: "MediaManagementSettings");
            migrationBuilder.DropColumn(name: "FailDownloads", table: "Indexers");
        }
    }
}
