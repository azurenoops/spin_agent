using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ato.Copilot.Core.Migrations
{
    /// <inheritdoc />
    public partial class Feature076_MakeConfidenceScoreNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SspSections_TenantId",
                table: "SspSections");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ValidationFindings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "UserCategories",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TicketingIntegrations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TaskHistoryEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TaskComments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SystemProfileSections",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SystemInterconnections",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SystemComponents",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SystemCapabilityLinks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SuppressionRules",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SignificantChanges",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SecurityCategorizations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SecurityCapabilities",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SecurityAssessmentReports",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SecurityAssessmentPlans",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ScanImportRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ScanImportFindings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SarSections",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SapTeamMembers",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "SapControlEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RoadmapPhases",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RoadmapItems",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RmfRoleAssignments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RiskAcceptances",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RemediationTasks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RemediationPlans",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RemediationBoards",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "RegisteredSystems",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ProfileAuditEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PrivacyThresholdAnalyses",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PrivacyImpactAssessments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PpsEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PoamTicketSyncs",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PoamMilestones",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PoamItems",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PoamHistoryEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PoamComponentLinks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PackageValidationResults",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "PackageArtifacts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "OrgInheritanceDefaults",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "OnboardingStepCompletions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "NotificationPreferences",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "NarrativeVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "IndexedAt",
                table: "NarrativeSeedDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IndexedChunkCount",
                table: "NarrativeSeedDocuments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexingError",
                table: "NarrativeSeedDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "NarrativeReviews",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "MonitoringConfigurations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "LeveragedAuthorizations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "JitRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "InventoryItems",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "InterconnectionAgreements",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "InheritanceAuditEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ImplementationRoadmaps",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Findings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "EvidenceVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "EvidenceArtifacts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Evidence",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "EscalationPaths",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Documents",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Deviations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "DeferredPrerequisites",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "DataTypeEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "DashboardActivities",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ControlTailorings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ControlInheritances",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ControlImplementations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ControlEffectivenessRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ControlBaselines",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ContingencyPlanReferences",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ConMonReports",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ConMonPlans",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ComponentSystemAssignments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ComponentCapabilityLinks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ComplianceTrendSnapshots",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ComplianceSnapshots",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ComplianceBaselines",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "ComplianceAlerts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CertificateRoleMappings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CapabilityControlMappings",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CacSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "CachedResponses",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "BusinessContextDrafts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "BusinessContextControlFlags",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "BoundaryComponentAssignments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AutoRemediationRules",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AuthorizationPackages",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AuthorizationDecisions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AuthorizationBoundaryDefinitions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AuthorizationBoundaries",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ActorTenantId",
                table: "AuditLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "AuditLogs",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImpersonatedTenantId",
                table: "AuditLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AuditLogs",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "Assessments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AssessmentRecords",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AlertRules",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AlertNotifications",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "AlertIdCounters",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "CapabilitySubscriptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    CspInheritedCapabilityId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    SubscribedBy = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    SubscribedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilitySubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ControlEvidenceMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SubscriptionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    EvidenceSourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    EvidenceReferenceId = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MappingNote = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CorrelationScore = table.Column<double>(type: "REAL", nullable: false),
                    MappedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MappedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlEvidenceMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CspInheritedComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CspProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ComponentType = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceFileName = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SourceFormat = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceArtifactReference = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ImportedBy = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CspInheritedComponents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CspProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LegalEntityName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LogoUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    PrimarySupportEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    SupportPhone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    DefaultClassificationFloor = table.Column<int>(type: "INTEGER", nullable: false),
                    OnboardingState = table.Column<int>(type: "INTEGER", nullable: false),
                    OnboardingCompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    IdentityCompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SupportCompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ClassificationCompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CspProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SubscriptionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvidenceFreshnessRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SubscriptionId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    LastCollectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FreshnessWindowHours = table.Column<int>(type: "INTEGER", nullable: false),
                    EvidenceSourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvidenceFreshnessRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalBaselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PublishedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UnpublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UnpublishedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalBaselines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ParentOrganizationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Organizations_Organizations_ParentOrganizationId",
                        column: x => x.ParentOrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrgControlOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ImplementationStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    InheritanceApplicability = table.Column<int>(type: "INTEGER", nullable: true),
                    Justification = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "BLOB", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgControlOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OscalDecompositionDrafts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GeneratedBy = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OscalDecompositionDrafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OscalImportRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    ImportedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SchemaValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    OscalVersion = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SourceDocumentUuid = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    Mode = table.Column<int>(type: "INTEGER", nullable: false),
                    ControlsCreated = table.Column<int>(type: "INTEGER", nullable: false),
                    ControlsUpdated = table.Column<int>(type: "INTEGER", nullable: false),
                    ControlsSkipped = table.Column<int>(type: "INTEGER", nullable: false),
                    ControlsFailed = table.Column<int>(type: "INTEGER", nullable: false),
                    WarningsJson = table.Column<string>(type: "TEXT", maxLength: 65535, nullable: true),
                    ErrorsJson = table.Column<string>(type: "TEXT", maxLength: 65535, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OscalImportRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OscalImportRuns_RegisteredSystems_RegisteredSystemId",
                        column: x => x.RegisteredSystemId,
                        principalTable: "RegisteredSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OverlayDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ControlId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    SourceReference = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverlayDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemRoleAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RegisteredSystemId = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsInherited = table.Column<bool>(type: "INTEGER", nullable: false),
                    SourceOrganizationRoleAssignmentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemRoleAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemRoleAssignments_Persons_PersonId",
                        column: x => x.PersonId,
                        principalTable: "Persons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntraTenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    LegalEntityName = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    DoDComponent = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    PrimaryPocName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PrimaryPocEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    PrimaryPocPhone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    HqAddressLine1 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    HqAddressLine2 = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    HqCity = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    HqStateOrProvince = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    HqPostalCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    HqCountry = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    DefaultClassificationLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorizingOfficialName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AuthorizingOfficialEmail = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    TimeZone = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    OnboardingState = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CspInheritedCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CspInheritedComponentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    MappedNistControlIds = table.Column<string>(type: "TEXT", nullable: false),
                    MappingConfidence = table.Column<double>(type: "REAL", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    MappingFailureReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MappedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ReviewedBy = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    ReviewerNote = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "BLOB", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CspInheritedCapabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CspInheritedCapabilities_CspInheritedComponents_CspInheritedComponentId",
                        column: x => x.CspInheritedComponentId,
                        principalTable: "CspInheritedComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OscalDecompositionFragments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DraftId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    StatementId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ComponentUuid = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    SuggestedParamsJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ConfidenceScore = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OscalDecompositionFragments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OscalDecompositionFragments_OscalDecompositionDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "OscalDecompositionDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoginAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Oid = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    Tid = table.Column<string>(type: "TEXT", maxLength: 254, nullable: true),
                    EffectiveTenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceIp = table.Column<string>(type: "TEXT", maxLength: 45, nullable: false),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Surface = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ErrorClass = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoginAuditEvents_Tenants_EffectiveTenantId",
                        column: x => x.EffectiveTenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CapabilityHistoryEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CapabilityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActorOid = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    MetadataJson = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapabilityHistoryEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapabilityHistoryEvents_CspInheritedCapabilities_CapabilityId",
                        column: x => x.CapabilityId,
                        principalTable: "CspInheritedCapabilities",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CapabilityHistoryEvents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "OverlayDocuments",
                columns: new[] { "Id", "Content", "ControlId", "CreatedAt", "CreatedBy", "IsActive", "ModifiedAt", "ModifiedBy", "SourceReference", "TenantId", "Title", "Type" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "CNSSI-1253 NSS Overlay for SC-1: Organizations operating National Security Systems must implement SC controls at the HIGH baseline. CNSS-approved cryptographic modules (FIPS 140-3 validated) are mandatory for all data in transit and at rest. Refer to CNSSI No. 1253 Annex D for NSS-specific parameter assignments.", "SC-1", new DateTime(2026, 8, 13, 21, 49, 7, 678, DateTimeKind.Utc).AddTicks(5690), "seed", true, null, null, "CNSSI No. 1253 Annex D, SC Family", null, "CNSSI-1253 SC Family Overlay — System and Communications Protection", "CNSSI-1253" },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "CNSSI-1253 NSS Overlay for SI-1: National Security Systems require continuous integrity monitoring at the HIGH baseline. Anti-malware tools must be CNSS-approved. Integrity verification of software and firmware is mandatory before deployment. Refer to CNSSI No. 1253 Annex D for SI parameter assignments.", "SI-1", new DateTime(2026, 8, 13, 21, 49, 7, 678, DateTimeKind.Utc).AddTicks(6940), "seed", true, null, null, "CNSSI No. 1253 Annex D, SI Family", null, "CNSSI-1253 SI Family Overlay — System and Information Integrity", "CNSSI-1253" },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "SECNAVINST 5239.3C requires all DON information systems to implement access control policies consistent with the DON RMF Process Guide. AC-1 must reference SECNAVINST 5239.3C as the governing authority. CIO N2/N6 is the DAA for Navy IT systems. Access control policies must be reviewed annually.", "AC-1", new DateTime(2026, 8, 13, 21, 49, 7, 678, DateTimeKind.Utc).AddTicks(6950), "seed", true, null, null, "SECNAVINST 5239.3C, Para 5.a", null, "SECNAVINST 5239.3C — Navy RMF Policy Overlay for AC Controls", "SECNAVINST" },
                    { new Guid("10000000-0000-0000-0000-000000000004"), "DoD Instruction 8140.01 requires all DoD personnel with privileged access to information systems to hold DCWF role-qualified certifications. AT-1 policies must reference DoDI 8140.01 and specify the applicable DCWF work roles. IAT Level II or III certification required for system administrators.", "AT-1", new DateTime(2026, 8, 13, 21, 49, 7, 678, DateTimeKind.Utc).AddTicks(6960), "seed", true, null, null, "DoDI 8140.01, Enclosure 3", null, "DoD 8140 Cyberspace Workforce Overlay for IA Controls", "DoD-8140" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorTenantId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "ActorTenantId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_Timestamp",
                table: "AuditLogs",
                columns: new[] { "TenantId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityHistoryEvents_CapabilityId",
                table: "CapabilityHistoryEvents",
                column: "CapabilityId");

            migrationBuilder.CreateIndex(
                name: "IX_CapabilityHistoryEvents_Tenant_Capability_Occurred",
                table: "CapabilityHistoryEvents",
                columns: new[] { "TenantId", "CapabilityId", "OccurredAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_CapabilitySubscription_System_Capability",
                table: "CapabilitySubscriptions",
                columns: new[] { "RegisteredSystemId", "CspInheritedCapabilityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ControlEvidenceMapping_ControlId_Sub",
                table: "ControlEvidenceMappings",
                columns: new[] { "ControlId", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ControlEvidenceMapping_MappedAt",
                table: "ControlEvidenceMappings",
                column: "MappedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CspInheritedCapabilities_ComponentId_Status",
                table: "CspInheritedCapabilities",
                columns: new[] { "CspInheritedComponentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CspInheritedComponents_CspProfileId_Status",
                table: "CspInheritedComponents",
                columns: new[] { "CspProfileId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceAuditEvent_ControlId",
                table: "EvidenceAuditEvents",
                column: "ControlId");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceAuditEvent_OccurredAt",
                table: "EvidenceAuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceAuditEvent_Sub_OccurredAt",
                table: "EvidenceAuditEvents",
                columns: new[] { "SubscriptionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EvidenceFreshness_ControlId_Sub_Unique",
                table: "EvidenceFreshnessRecords",
                columns: new[] { "ControlId", "SubscriptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoginAuditEvents_Occurred",
                table: "LoginAuditEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAuditEvents_Oid",
                table: "LoginAuditEvents",
                columns: new[] { "Oid", "OccurredAt" },
                descending: new[] { false, true },
                filter: "[Oid] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LoginAuditEvents_Tenant_Occurred",
                table: "LoginAuditEvents",
                columns: new[] { "EffectiveTenantId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_ParentOrganizationId",
                table: "Organizations",
                column: "ParentOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_TenantId_Name",
                table: "Organizations",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_TenantId_ParentOrganizationId",
                table: "Organizations",
                columns: new[] { "TenantId", "ParentOrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgControlOverride_TenantId_ControlId",
                table: "OrgControlOverrides",
                columns: new[] { "TenantId", "ControlId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OscalDecompositionFragments_DraftId",
                table: "OscalDecompositionFragments",
                column: "DraftId");

            migrationBuilder.CreateIndex(
                name: "IX_OscalImportRuns_RegisteredSystemId",
                table: "OscalImportRuns",
                column: "RegisteredSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_OverlayDocument_ControlId_Type",
                table: "OverlayDocuments",
                columns: new[] { "ControlId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_OverlayDocument_IsActive",
                table: "OverlayDocuments",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_SystemRoleAssignments_PersonId",
                table: "SystemRoleAssignments",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_EntraTenantId",
                table: "Tenants",
                column: "EntraTenantId",
                unique: true,
                filter: "[EntraTenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Status",
                table: "Tenants",
                column: "Status");

            // Data-fix: nullify historical fallback confidence scores (Feature 076)
            migrationBuilder.Sql(
                "UPDATE OscalDecompositionFragments SET ConfidenceScore = NULL WHERE ConfidenceScore = 0.5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CapabilityHistoryEvents");

            migrationBuilder.DropTable(
                name: "CapabilitySubscriptions");

            migrationBuilder.DropTable(
                name: "ControlEvidenceMappings");

            migrationBuilder.DropTable(
                name: "CspProfiles");

            migrationBuilder.DropTable(
                name: "EvidenceAuditEvents");

            migrationBuilder.DropTable(
                name: "EvidenceFreshnessRecords");

            migrationBuilder.DropTable(
                name: "GlobalBaselines");

            migrationBuilder.DropTable(
                name: "LoginAuditEvents");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "OrgControlOverrides");

            migrationBuilder.DropTable(
                name: "OscalDecompositionFragments");

            migrationBuilder.DropTable(
                name: "OscalImportRuns");

            migrationBuilder.DropTable(
                name: "OverlayDocuments");

            migrationBuilder.DropTable(
                name: "SystemRoleAssignments");

            migrationBuilder.DropTable(
                name: "CspInheritedCapabilities");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "OscalDecompositionDrafts");

            migrationBuilder.DropTable(
                name: "CspInheritedComponents");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_ActorTenantId_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId_Timestamp",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ValidationFindings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserCategories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TicketingIntegrations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TaskHistoryEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TaskComments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SystemProfileSections");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SystemInterconnections");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SystemComponents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SystemCapabilityLinks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SuppressionRules");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SignificantChanges");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SecurityCategorizations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SecurityCapabilities");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SecurityAssessmentReports");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SecurityAssessmentPlans");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ScanImportRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ScanImportFindings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SarSections");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SapTeamMembers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SapControlEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RoadmapPhases");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RoadmapItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RmfRoleAssignments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RiskAcceptances");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RemediationTasks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RemediationPlans");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RemediationBoards");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RegisteredSystems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProfileAuditEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PrivacyThresholdAnalyses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PrivacyImpactAssessments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PpsEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PoamTicketSyncs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PoamMilestones");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PoamItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PoamHistoryEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PoamComponentLinks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PackageValidationResults");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PackageArtifacts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "OrgInheritanceDefaults");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "OnboardingStepCompletions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "NarrativeVersions");

            migrationBuilder.DropColumn(
                name: "IndexedAt",
                table: "NarrativeSeedDocuments");

            migrationBuilder.DropColumn(
                name: "IndexedChunkCount",
                table: "NarrativeSeedDocuments");

            migrationBuilder.DropColumn(
                name: "IndexingError",
                table: "NarrativeSeedDocuments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "NarrativeReviews");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MonitoringConfigurations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "LeveragedAuthorizations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "JitRequests");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "InterconnectionAgreements");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "InheritanceAuditEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ImplementationRoadmaps");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Findings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EvidenceVersions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EvidenceArtifacts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Evidence");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "EscalationPaths");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Deviations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DeferredPrerequisites");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DataTypeEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DashboardActivities");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ControlTailorings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ControlInheritances");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ControlImplementations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ControlEffectivenessRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ControlBaselines");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ContingencyPlanReferences");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ConMonReports");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ConMonPlans");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ComponentSystemAssignments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ComponentCapabilityLinks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ComplianceTrendSnapshots");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ComplianceSnapshots");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ComplianceBaselines");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ComplianceAlerts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CertificateRoleMappings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CapabilityControlMappings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CacSessions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CachedResponses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BusinessContextDrafts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BusinessContextControlFlags");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "BoundaryComponentAssignments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AutoRemediationRules");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuthorizationPackages");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuthorizationDecisions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuthorizationBoundaryDefinitions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuthorizationBoundaries");

            migrationBuilder.DropColumn(
                name: "ActorTenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ImpersonatedTenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Assessments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AssessmentRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AlertRules");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AlertNotifications");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AlertIdCounters");

            migrationBuilder.CreateIndex(
                name: "IX_SspSections_TenantId",
                table: "SspSections",
                column: "TenantId");
        }
    }
}
