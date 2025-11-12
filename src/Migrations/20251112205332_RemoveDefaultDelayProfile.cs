using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDefaultDelayProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove the default delay profile that was automatically created
            // Users should explicitly create delay profiles if they want to use them
            migrationBuilder.Sql(@"
                DELETE FROM DelayProfiles WHERE Id = 1
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No operation on rollback - users can manually create delay profiles as needed
        }
    }
}
