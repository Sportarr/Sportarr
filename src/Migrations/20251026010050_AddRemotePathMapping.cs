using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fightarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRemotePathMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RemotePathMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    RemotePath = table.Column<string>(type: "TEXT", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemotePathMappings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RemotePathMappings");
        }
    }
}
