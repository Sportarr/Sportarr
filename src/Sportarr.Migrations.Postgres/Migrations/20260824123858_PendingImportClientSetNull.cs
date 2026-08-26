using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PendingImportClientSetNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PendingImports_DownloadClients_DownloadClientId",
                table: "PendingImports");

            migrationBuilder.AddForeignKey(
                name: "FK_PendingImports_DownloadClients_DownloadClientId",
                table: "PendingImports",
                column: "DownloadClientId",
                principalTable: "DownloadClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PendingImports_DownloadClients_DownloadClientId",
                table: "PendingImports");

            migrationBuilder.AddForeignKey(
                name: "FK_PendingImports_DownloadClients_DownloadClientId",
                table: "PendingImports",
                column: "DownloadClientId",
                principalTable: "DownloadClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
