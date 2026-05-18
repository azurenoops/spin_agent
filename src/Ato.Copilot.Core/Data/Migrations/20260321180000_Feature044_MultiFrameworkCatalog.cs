using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Data.Migrations;

/// <summary>
/// Adds the multi-framework catalog tables: ComplianceFrameworks, FrameworkControls,
/// FrameworkBaselines, and BaselineControlEntries.
/// </summary>
public partial class Feature044_MultiFrameworkCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── ComplianceFrameworks ────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "ComplianceFrameworks",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                Identifier = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: ""),
                Publisher = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: ""),
                CatalogUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                OscalModelType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "catalog"),
                ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ControlCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ComplianceFrameworks", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ComplianceFramework_Identifier",
            table: "ComplianceFrameworks",
            column: "Identifier",
            unique: true);

        // ─── FrameworkControls ───────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "FrameworkControls",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                FrameworkId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                ControlId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Family = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                ParentControlId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                IsEnhancement = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                Withdrawn = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                WithdrawnTo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FrameworkControls", x => x.Id);
                table.ForeignKey(
                    name: "FK_FrameworkControls_ComplianceFrameworks_FrameworkId",
                    column: x => x.FrameworkId,
                    principalTable: "ComplianceFrameworks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FrameworkControl_FwkCtl",
            table: "FrameworkControls",
            columns: new[] { "FrameworkId", "ControlId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_FrameworkControl_Family",
            table: "FrameworkControls",
            column: "Family");

        // ─── FrameworkBaselines ──────────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "FrameworkBaselines",
            columns: table => new
            {
                Id = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                FrameworkId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                Level = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                SourceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                ControlCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FrameworkBaselines", x => x.Id);
                table.ForeignKey(
                    name: "FK_FrameworkBaselines_ComplianceFrameworks_FrameworkId",
                    column: x => x.FrameworkId,
                    principalTable: "ComplianceFrameworks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FrameworkBaseline_FwkLevel",
            table: "FrameworkBaselines",
            columns: new[] { "FrameworkId", "Level" },
            unique: true);

        // ─── BaselineControlEntries ──────────────────────────────────────────
        migrationBuilder.CreateTable(
            name: "BaselineControlEntries",
            columns: table => new
            {
                BaselineId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                ControlId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Parameters = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BaselineControlEntries", x => new { x.BaselineId, x.ControlId });
                table.ForeignKey(
                    name: "FK_BaselineControlEntries_FrameworkBaselines_BaselineId",
                    column: x => x.BaselineId,
                    principalTable: "FrameworkBaselines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "BaselineControlEntries");
        migrationBuilder.DropTable(name: "FrameworkBaselines");
        migrationBuilder.DropTable(name: "FrameworkControls");
        migrationBuilder.DropTable(name: "ComplianceFrameworks");
    }
}
