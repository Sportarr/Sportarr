using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRootFolderIdToLeague : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable so legacy leagues stay valid until the user picks a
            // root for them. New leagues added through the modal get this
            // populated at insert time. The importer falls back to the
            // free-space heuristic when the column is null.
            migrationBuilder.AddColumn<int>(
                name: "RootFolderId",
                table: "Leagues",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_RootFolderId",
                table: "Leagues",
                column: "RootFolderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leagues_RootFolderId",
                table: "Leagues");

            migrationBuilder.DropColumn(
                name: "RootFolderId",
                table: "Leagues");
        }
    }
}
