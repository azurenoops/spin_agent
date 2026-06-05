using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Migrations
{
    /// <inheritdoc />
    public partial class Feature122_SspSectionTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add TenantId column to SspSections — the [TenantScoped] attribute was applied
            // to SspSection in Feature 022 but the corresponding column was omitted from that
            // migration. Without this column the row-level security HasQueryFilter
            // (ApplyTenantQueryFilters) will not function for SspSection rows.
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SspSections",
                type: "TEXT",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.CreateIndex(
                name: "IX_SspSections_TenantId",
                table: "SspSections",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SspSections_TenantId",
                table: "SspSections");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SspSections");
        }
    }
}
