using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Data.Migrations;

/// <summary>
/// Adds the CapabilitySubscriptions table for UF-CSP-01/02/03 (spec-070).
/// Org-users subscribe their registered systems to Published CSP capabilities
/// so inherited control coverage is tracked automatically.
/// </summary>
public partial class AddCapabilitySubscription : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CapabilitySubscriptions",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                RegisteredSystemId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                CspInheritedCapabilityId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                SubscribedBy = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false, defaultValue: "dashboard-user"),
                SubscribedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CapabilitySubscriptions", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CapabilitySubscription_System_Capability",
            table: "CapabilitySubscriptions",
            columns: new[] { "RegisteredSystemId", "CspInheritedCapabilityId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CapabilitySubscriptions");
    }
}
