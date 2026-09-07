using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Migrations;

/// <summary>
/// Fix: <c>IX_OrgInheritanceDefault_ControlId</c> was a global unique index on ControlId alone,
/// which prevents multiple tenants from sharing the same ControlId value.
/// Replaced with a composite unique index on (TenantId, ControlId) for correct multi-tenant isolation.
/// </summary>
public partial class Fix044_OrgInheritanceDefault_UniqueIndex_TenantScoped : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OrgInheritanceDefault_ControlId",
            table: "OrgInheritanceDefaults");

        migrationBuilder.CreateIndex(
            name: "IX_OrgInheritanceDefault_TenantId_ControlId",
            table: "OrgInheritanceDefaults",
            columns: new[] { "TenantId", "ControlId" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_OrgInheritanceDefault_TenantId_ControlId",
            table: "OrgInheritanceDefaults");

        migrationBuilder.CreateIndex(
            name: "IX_OrgInheritanceDefault_ControlId",
            table: "OrgInheritanceDefaults",
            column: "ControlId",
            unique: true);
    }
}
