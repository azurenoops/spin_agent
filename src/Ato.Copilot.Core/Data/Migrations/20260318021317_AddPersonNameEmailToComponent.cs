using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ato.Copilot.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonNameEmailToComponent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.DropForeignKey(
                name: "FK_SystemComponents_RegisteredSystems_RegisteredSystemId",
                table: "SystemComponents");

            migrationBuilder.AlterColumn<string>(
                name: "RegisteredSystemId",
                table: "SystemComponents",
                type: "TEXT",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 36);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "SystemComponents",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonName",
                table: "SystemComponents",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviationId",
                table: "PoamItems",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviationId",
                table: "Findings",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRead",
                table: "AlertNotifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadAt",
                table: "AlertNotifications",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "AlertNotifications",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComponentSystemAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SystemComponentId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    AuthorizationBoundaryDefinitionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComponentSystemAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComponentSystemAssignments_AuthorizationBoundaryDefinitions_AuthorizationBoundaryDefinitionId",
                        column: x => x.AuthorizationBoundaryDefinitionId,
                        principalTable: "AuthorizationBoundaryDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComponentSystemAssignments_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComponentSystemAssignments_SystemComponents_SystemComponentId",
                        column: x => x.SystemComponentId,
                        principalTable: "SystemComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeferredPrerequisites",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    GateName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SkippedFromPhase = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    AdvancedToPhase = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeferredPrerequisites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeferredPrerequisites_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Deviations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    DeviationType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CatSeverity = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Justification = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CompensatingControls = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    EvidenceReferences = table.Column<string>(type: "TEXT", nullable: false),
                    ExpirationDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewCycle = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FindingId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PoamEntryId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    AuthorizationDecisionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    BoundaryDefinitionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewerRole = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ReviewerComments = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ISSMRecommendation = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ISSMRecommendedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ISSMRecommendedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevocationReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deviations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deviations_AuthorizationBoundaryDefinitions_BoundaryDefinitionId",
                        column: x => x.BoundaryDefinitionId,
                        principalTable: "AuthorizationBoundaryDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deviations_AuthorizationDecisions_AuthorizationDecisionId",
                        column: x => x.AuthorizationDecisionId,
                        principalTable: "AuthorizationDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deviations_Findings_FindingId",
                        column: x => x.FindingId,
                        principalTable: "Findings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deviations_PoamItems_PoamEntryId",
                        column: x => x.PoamEntryId,
                        principalTable: "PoamItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deviations_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PoamOverdueAlerts = table.Column<bool>(type: "INTEGER", nullable: false),
                    AtoExpirationAlerts = table.Column<bool>(type: "INTEGER", nullable: false),
                    ComplianceDriftAlerts = table.Column<bool>(type: "INTEGER", nullable: false),
                    AlertDaysBefore = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PoamItem_DeviationId",
                table: "PoamItems",
                column: "DeviationId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceFinding_DeviationId",
                table: "Findings",
                column: "DeviationId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertNotification_User_Read",
                table: "AlertNotifications",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_ComponentSystemAssignment_System",
                table: "ComponentSystemAssignments",
                column: "RegisteredSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_ComponentSystemAssignment_Unique",
                table: "ComponentSystemAssignments",
                columns: new[] { "SystemComponentId", "RegisteredSystemId", "AuthorizationBoundaryDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComponentSystemAssignments_AuthorizationBoundaryDefinitionId",
                table: "ComponentSystemAssignments",
                column: "AuthorizationBoundaryDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_DeferredPrerequisite_System_Resolved",
                table: "DeferredPrerequisites",
                columns: new[] { "RegisteredSystemId", "IsResolved" });

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_AuthorizationDecisionId",
                table: "Deviations",
                column: "AuthorizationDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_BoundaryDefinitionId",
                table: "Deviations",
                column: "BoundaryDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_ExpirationDate",
                table: "Deviations",
                column: "ExpirationDate");

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_FindingId",
                table: "Deviations",
                column: "FindingId");

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_PoamEntryId",
                table: "Deviations",
                column: "PoamEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_RegisteredSystemId",
                table: "Deviations",
                column: "RegisteredSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_Status",
                table: "Deviations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationPreferences_UserId",
                table: "NotificationPreferences",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AuthorizationBoundaries_AuthorizationBoundaryDefinitions_AuthorizationBoundaryDefinitionId",
                table: "AuthorizationBoundaries",
                column: "AuthorizationBoundaryDefinitionId",
                principalTable: "AuthorizationBoundaryDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CapabilityControlMappings_AuthorizationBoundaryDefinitions_AuthorizationBoundaryDefinitionId",
                table: "CapabilityControlMappings",
                column: "AuthorizationBoundaryDefinitionId",
                principalTable: "AuthorizationBoundaryDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemComponents_AuthorizationBoundaryDefinitions_AuthorizationBoundaryDefinitionId",
                table: "SystemComponents",
                column: "AuthorizationBoundaryDefinitionId",
                principalTable: "AuthorizationBoundaryDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SystemComponents_RegisteredSystems_RegisteredSystemId",
                table: "SystemComponents",
                column: "RegisteredSystemId",
                principalTable: "RegisteredSystems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
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

            migrationBuilder.DropForeignKey(
                name: "FK_SystemComponents_RegisteredSystems_RegisteredSystemId",
                table: "SystemComponents");

            migrationBuilder.DropTable(
                name: "ComponentSystemAssignments");

            migrationBuilder.DropTable(
                name: "DeferredPrerequisites");

            migrationBuilder.DropTable(
                name: "Deviations");

            migrationBuilder.DropTable(
                name: "NotificationPreferences");

            migrationBuilder.DropIndex(
                name: "IX_PoamItem_DeviationId",
                table: "PoamItems");

            migrationBuilder.DropIndex(
                name: "IX_ComplianceFinding_DeviationId",
                table: "Findings");

            migrationBuilder.DropIndex(
                name: "IX_AlertNotification_User_Read",
                table: "AlertNotifications");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "SystemComponents");

            migrationBuilder.DropColumn(
                name: "PersonName",
                table: "SystemComponents");

            migrationBuilder.DropColumn(
                name: "DeviationId",
                table: "PoamItems");

            migrationBuilder.DropColumn(
                name: "DeviationId",
                table: "Findings");

            migrationBuilder.DropColumn(
                name: "IsRead",
                table: "AlertNotifications");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "AlertNotifications");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AlertNotifications");

            migrationBuilder.AlterColumn<string>(
                name: "RegisteredSystemId",
                table: "SystemComponents",
                type: "TEXT",
                maxLength: 36,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 36,
                oldNullable: true);

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

            migrationBuilder.AddForeignKey(
                name: "FK_SystemComponents_RegisteredSystems_RegisteredSystemId",
                table: "SystemComponents",
                column: "RegisteredSystemId",
                principalTable: "RegisteredSystems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
