using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sportarr.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDelayProfileSeedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No operation needed - this migration just removes the HasData seed from the DbContext
            // to prevent conflicts with the existing migration-based insert.
            // Existing delay profile row (Id=1) will remain untouched for existing users.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No operation needed on rollback - delay profile will remain from original migration
        }
    }
}
