using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Migrations
{
    /// <inheritdoc />
    public partial class Feature044_OrgLevelInheritance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DesignationSource",
                table: "ControlInheritances",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrgInheritanceDefaultId",
                table: "ControlInheritances",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            // Backfill: Set DesignationSource = "Manual" for all existing ControlInheritance rows
            migrationBuilder.Sql(
                "UPDATE ControlInheritances SET DesignationSource = 'Manual' WHERE DesignationSource IS NULL");

            migrationBuilder.CreateTable(
                name: "ComplianceFrameworks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Identifier = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Publisher = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CatalogUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OscalModelType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ControlCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceFrameworks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InheritanceAuditEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ControlInheritanceId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ControlBaselineId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PreviousInheritanceType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    NewInheritanceType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    PreviousProvider = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    NewProvider = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PreviousCustomerResponsibility = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    NewCustomerResponsibility = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ChangeSource = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InheritanceAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgInheritanceDefaults",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    InheritanceType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    SourceCapabilityIds = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SourceCapabilityNames = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    MappingRole = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DerivedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgInheritanceDefaults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemCapabilityLinks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SecurityCapabilityId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LinkedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemCapabilityLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemCapabilityLinks_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SystemCapabilityLinks_SecurityCapabilities_SecurityCapabilityId",
                        column: x => x.SecurityCapabilityId,
                        principalTable: "SecurityCapabilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FrameworkBaselines",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    FrameworkId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ControlCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "FrameworkControls",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    FrameworkId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Family = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    ParentControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    IsEnhancement = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Withdrawn = table.Column<bool>(type: "INTEGER", nullable: false),
                    WithdrawnTo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "BaselineControlEntries",
                columns: table => new
                {
                    BaselineId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Parameters = table.Column<string>(type: "TEXT", nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "IX_ControlInheritance_DesignationSource",
                table: "ControlInheritances",
                column: "DesignationSource");

            migrationBuilder.CreateIndex(
                name: "IX_ControlInheritances_OrgInheritanceDefaultId",
                table: "ControlInheritances",
                column: "OrgInheritanceDefaultId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceFramework_Identifier",
                table: "ComplianceFrameworks",
                column: "Identifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FrameworkBaseline_FwkLevel",
                table: "FrameworkBaselines",
                columns: new[] { "FrameworkId", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FrameworkControl_Family",
                table: "FrameworkControls",
                column: "Family");

            migrationBuilder.CreateIndex(
                name: "IX_FrameworkControl_FwkCtl",
                table: "FrameworkControls",
                columns: new[] { "FrameworkId", "ControlId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InheritanceAuditEntries_Baseline_Timestamp",
                table: "InheritanceAuditEntries",
                columns: new[] { "ControlBaselineId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_InheritanceAuditEntries_ControlInheritanceId",
                table: "InheritanceAuditEntries",
                column: "ControlInheritanceId");

            migrationBuilder.CreateIndex(
                name: "IX_InheritanceAuditEntries_Timestamp",
                table: "InheritanceAuditEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_OrgInheritanceDefault_ControlId",
                table: "OrgInheritanceDefaults",
                column: "ControlId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgInheritanceDefault_InheritanceType",
                table: "OrgInheritanceDefaults",
                column: "InheritanceType");

            migrationBuilder.CreateIndex(
                name: "IX_SCL_SystemCapability",
                table: "SystemCapabilityLinks",
                columns: new[] { "RegisteredSystemId", "SecurityCapabilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SystemCapabilityLinks_SecurityCapabilityId",
                table: "SystemCapabilityLinks",
                column: "SecurityCapabilityId");

            migrationBuilder.AddForeignKey(
                name: "FK_ControlInheritances_OrgInheritanceDefaults_OrgInheritanceDefaultId",
                table: "ControlInheritances",
                column: "OrgInheritanceDefaultId",
                principalTable: "OrgInheritanceDefaults",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ControlInheritances_OrgInheritanceDefaults_OrgInheritanceDefaultId",
                table: "ControlInheritances");

            migrationBuilder.DropTable(
                name: "BaselineControlEntries");

            migrationBuilder.DropTable(
                name: "FrameworkControls");

            migrationBuilder.DropTable(
                name: "InheritanceAuditEntries");

            migrationBuilder.DropTable(
                name: "OrgInheritanceDefaults");

            migrationBuilder.DropTable(
                name: "SystemCapabilityLinks");

            migrationBuilder.DropTable(
                name: "FrameworkBaselines");

            migrationBuilder.DropTable(
                name: "ComplianceFrameworks");

            migrationBuilder.DropIndex(
                name: "IX_ControlInheritance_DesignationSource",
                table: "ControlInheritances");

            migrationBuilder.DropIndex(
                name: "IX_ControlInheritances_OrgInheritanceDefaultId",
                table: "ControlInheritances");

            migrationBuilder.DropColumn(
                name: "DesignationSource",
                table: "ControlInheritances");

            migrationBuilder.DropColumn(
                name: "OrgInheritanceDefaultId",
                table: "ControlInheritances");
        }
    }
}
