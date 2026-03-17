using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Feature033_BoundaryScopedModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorizationBoundaryDefinitionId",
                table: "SystemComponents",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoadmapItemId",
                table: "RemediationTasks",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationBoundaryDefinitionId",
                table: "CapabilityControlMappings",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationBoundaryDefinitionId",
                table: "AuthorizationBoundaries",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuthorizationBoundaryDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BoundaryType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationBoundaryDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthorizationBoundaryDefinitions_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImplementationRoadmaps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalEstimatedEffort = table.Column<double>(type: "REAL", nullable: false),
                    TotalRiskPoints = table.Column<double>(type: "REAL", nullable: false),
                    ProjectedRiskReduction = table.Column<double>(type: "REAL", nullable: false),
                    BaselineLevel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TotalGaps = table.Column<int>(type: "INTEGER", nullable: false),
                    LinkedBoardId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    GenerationMethod = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    RowVersion = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImplementationRoadmaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RoadmapPhases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RoadmapId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedEffort = table.Column<double>(type: "REAL", nullable: false),
                    RiskPoints = table.Column<double>(type: "REAL", nullable: false),
                    RiskReductionPercent = table.Column<double>(type: "REAL", nullable: false),
                    TargetStartWeek = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetEndWeek = table.Column<int>(type: "INTEGER", nullable: true),
                    TargetCompletionDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoadmapPhases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoadmapPhases_ImplementationRoadmaps_RoadmapId",
                        column: x => x.RoadmapId,
                        principalTable: "ImplementationRoadmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoadmapItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    PhaseId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RoadmapId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ControlTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ControlFamily = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    GapType = table.Column<int>(type: "INTEGER", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    RiskPoints = table.Column<double>(type: "REAL", nullable: false),
                    EstimatedEffortDays = table.Column<double>(type: "REAL", nullable: false),
                    EstimationSource = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AssignedRole = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DependsOn = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LinkedTaskId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RowVersion = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoadmapItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoadmapItems_ImplementationRoadmaps_RoadmapId",
                        column: x => x.RoadmapId,
                        principalTable: "ImplementationRoadmaps",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RoadmapItems_RoadmapPhases_PhaseId",
                        column: x => x.PhaseId,
                        principalTable: "RoadmapPhases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemComponent_System_Boundary_Type",
                table: "SystemComponents",
                columns: new[] { "RegisteredSystemId", "AuthorizationBoundaryDefinitionId", "ComponentType" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemComponents_AuthorizationBoundaryDefinitionId",
                table: "SystemComponents",
                column: "AuthorizationBoundaryDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RemediationTasks_RoadmapItemId",
                table: "RemediationTasks",
                column: "RoadmapItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityControlMappings_AuthorizationBoundaryDefinitionId",
                table: "CapabilityControlMappings",
                column: "AuthorizationBoundaryDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityMapping_System_Boundary_Control",
                table: "CapabilityControlMappings",
                columns: new[] { "RegisteredSystemId", "AuthorizationBoundaryDefinitionId", "ControlId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationBoundary_BoundaryDefinitionId",
                table: "AuthorizationBoundaries",
                column: "AuthorizationBoundaryDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_BoundaryDefinition_System_Name",
                table: "AuthorizationBoundaryDefinitions",
                columns: new[] { "RegisteredSystemId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BoundaryDefinition_System_Primary",
                table: "AuthorizationBoundaryDefinitions",
                columns: new[] { "RegisteredSystemId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_ImplementationRoadmaps_SystemId",
                table: "ImplementationRoadmaps",
                column: "SystemId");

            migrationBuilder.CreateIndex(
                name: "IX_ImplementationRoadmaps_SystemId_Status",
                table: "ImplementationRoadmaps",
                columns: new[] { "SystemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapItems_ControlId",
                table: "RoadmapItems",
                column: "ControlId");

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapItems_PhaseId",
                table: "RoadmapItems",
                column: "PhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapItems_RoadmapId",
                table: "RoadmapItems",
                column: "RoadmapId");

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapPhases_RoadmapId_DisplayOrder",
                table: "RoadmapPhases",
                columns: new[] { "RoadmapId", "DisplayOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_AuthorizationBoundaries_AuthorizationBoundaryDefinitions_AuthorizationBoundaryDefinitionId",
                table: "AuthorizationBoundaries",
                column: "AuthorizationBoundaryDefinitionId",
                principalTable: "AuthorizationBoundaryDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CapabilityControlMappings_AuthorizationBoundaryDefinitions_AuthorizationBoundaryDefinitionId",
                table: "CapabilityControlMappings",
                column: "AuthorizationBoundaryDefinitionId",
                principalTable: "AuthorizationBoundaryDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemComponents_AuthorizationBoundaryDefinitions_AuthorizationBoundaryDefinitionId",
                table: "SystemComponents",
                column: "AuthorizationBoundaryDefinitionId",
                principalTable: "AuthorizationBoundaryDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ─── Data Seed: Create Primary boundary definitions for existing systems ───
            migrationBuilder.Sql(@"
                INSERT INTO AuthorizationBoundaryDefinitions (Id, RegisteredSystemId, Name, BoundaryType, Description, IsPrimary, CreatedAt, CreatedBy)
                SELECT
                    lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))),
                    Id,
                    Name || ' — Primary',
                    'Logical',
                    'Default authorization boundary created during Feature 033 migration.',
                    1,
                    datetime('now'),
                    'system-migration'
                FROM RegisteredSystems
                WHERE IsActive = 1;
            ");

            // Assign existing boundary resource records to Primary
            migrationBuilder.Sql(@"
                UPDATE AuthorizationBoundaries
                SET AuthorizationBoundaryDefinitionId = (
                    SELECT abd.Id FROM AuthorizationBoundaryDefinitions abd
                    WHERE abd.RegisteredSystemId = AuthorizationBoundaries.RegisteredSystemId
                    AND abd.IsPrimary = 1
                );
            ");

            // Assign existing components to Primary
            migrationBuilder.Sql(@"
                UPDATE SystemComponents
                SET AuthorizationBoundaryDefinitionId = (
                    SELECT abd.Id FROM AuthorizationBoundaryDefinitions abd
                    WHERE abd.RegisteredSystemId = SystemComponents.RegisteredSystemId
                    AND abd.IsPrimary = 1
                );
            ");
            // Note: CapabilityControlMappings are NOT updated — null means org-wide (all boundaries)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuthorizationBoundaries_AuthorizationBoundaryDefinitions_AuthorizationBoundaryDefinitionId",
                table: "AuthorizationBoundaries");

            migrationBuilder.DropForeignKey(
                name: "FK_CapabilityControlMappings_AuthorizationBoundaryDefinitions_AuthorizationBoundaryDefinitionId",
                table: "CapabilityControlMappings");

            migrationBuilder.DropForeignKey(
                name: "FK_SystemComponents_AuthorizationBoundaryDefinitions_AuthorizationBoundaryDefinitionId",
                table: "SystemComponents");

            migrationBuilder.DropTable(
                name: "AuthorizationBoundaryDefinitions");

            migrationBuilder.DropTable(
                name: "RoadmapItems");

            migrationBuilder.DropTable(
                name: "RoadmapPhases");

            migrationBuilder.DropTable(
                name: "ImplementationRoadmaps");

            migrationBuilder.DropIndex(
                name: "IX_SystemComponent_System_Boundary_Type",
                table: "SystemComponents");

            migrationBuilder.DropIndex(
                name: "IX_SystemComponents_AuthorizationBoundaryDefinitionId",
                table: "SystemComponents");

            migrationBuilder.DropIndex(
                name: "IX_RemediationTasks_RoadmapItemId",
                table: "RemediationTasks");

            migrationBuilder.DropIndex(
                name: "IX_CapabilityControlMappings_AuthorizationBoundaryDefinitionId",
                table: "CapabilityControlMappings");

            migrationBuilder.DropIndex(
                name: "IX_CapabilityMapping_System_Boundary_Control",
                table: "CapabilityControlMappings");

            migrationBuilder.DropIndex(
                name: "IX_AuthorizationBoundary_BoundaryDefinitionId",
                table: "AuthorizationBoundaries");

            migrationBuilder.DropColumn(
                name: "AuthorizationBoundaryDefinitionId",
                table: "SystemComponents");

            migrationBuilder.DropColumn(
                name: "RoadmapItemId",
                table: "RemediationTasks");

            migrationBuilder.DropColumn(
                name: "AuthorizationBoundaryDefinitionId",
                table: "CapabilityControlMappings");

            migrationBuilder.DropColumn(
                name: "AuthorizationBoundaryDefinitionId",
                table: "AuthorizationBoundaries");
        }
    }
}
