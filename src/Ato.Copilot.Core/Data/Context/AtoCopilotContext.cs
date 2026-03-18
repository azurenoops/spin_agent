using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ato.Copilot.Core.Models;
using Ato.Copilot.Core.Models.Auth;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Kanban;
using Ato.Copilot.Core.Models.Roadmap;

namespace Ato.Copilot.Core.Data.Context;

/// <summary>
/// Database context for ATO Copilot compliance data.
/// Supports SQLite (development) and SQL Server (production) providers.
/// </summary>
public class AtoCopilotContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of <see cref="AtoCopilotContext"/>.
    /// </summary>
    /// <param name="options">Database context options with provider configuration.</param>
    public AtoCopilotContext(DbContextOptions<AtoCopilotContext> options) : base(options) { }

    /// <summary>Compliance assessments with scan results and statistics.</summary>
    public DbSet<ComplianceAssessment> Assessments => Set<ComplianceAssessment>();

    /// <summary>Individual compliance findings linked to assessments.</summary>
    public DbSet<ComplianceFinding> Findings => Set<ComplianceFinding>();

    /// <summary>Evidence artifacts collected for compliance controls.</summary>
    public DbSet<ComplianceEvidence> Evidence => Set<ComplianceEvidence>();

    /// <summary>Generated compliance documents (SSP, SAR, POA&M).</summary>
    public DbSet<ComplianceDocument> Documents => Set<ComplianceDocument>();

    /// <summary>NIST 800-53 Rev 5 controls loaded from OSCAL catalog.</summary>
    public DbSet<NistControl> NistControls => Set<NistControl>();

    /// <summary>Audit log entries for all compliance actions (730-day retention).</summary>
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    /// <summary>Remediation plans with ordered steps.</summary>
    public DbSet<RemediationPlan> RemediationPlans => Set<RemediationPlan>();

    /// <summary>Kanban remediation boards grouping tasks by subscription.</summary>
    public DbSet<RemediationBoard> RemediationBoards => Set<RemediationBoard>();

    /// <summary>Individual remediation tasks (Kanban cards).</summary>
    public DbSet<RemediationTask> RemediationTasks => Set<RemediationTask>();

    /// <summary>Threaded comments on remediation tasks.</summary>
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    /// <summary>Immutable history entries for remediation task changes.</summary>
    public DbSet<TaskHistoryEntry> TaskHistoryEntries => Set<TaskHistoryEntry>();

    /// <summary>CAC/PIV authentication sessions with configurable timeout.</summary>
    public DbSet<CacSession> CacSessions => Set<CacSession>();

    /// <summary>JIT access requests for PIM role activations and VM network access.</summary>
    public DbSet<JitRequestEntity> JitRequests => Set<JitRequestEntity>();

    /// <summary>Certificate-to-role mappings for automatic role resolution.</summary>
    public DbSet<CertificateRoleMapping> CertificateRoleMappings => Set<CertificateRoleMapping>();

    // ─── Compliance Watch DbSets ─────────────────────────────────────────────
    /// <summary>Compliance alerts with full lifecycle tracking.</summary>
    public DbSet<ComplianceAlert> ComplianceAlerts => Set<ComplianceAlert>();

    /// <summary>Date-partitioned counters for alert ID generation.</summary>
    public DbSet<AlertIdCounter> AlertIdCounters => Set<AlertIdCounter>();

    /// <summary>Notification records for alert delivery audit.</summary>
    public DbSet<AlertNotification> AlertNotifications => Set<AlertNotification>();

    /// <summary>Per-user notification preferences.</summary>
    public DbSet<NotificationPreferences> NotificationPreferences => Set<NotificationPreferences>();

    /// <summary>Per-scope monitoring configurations.</summary>
    public DbSet<MonitoringConfiguration> MonitoringConfigurations => Set<MonitoringConfiguration>();

    /// <summary>Point-in-time resource compliance baselines for drift detection.</summary>
    public DbSet<ComplianceBaseline> ComplianceBaselines => Set<ComplianceBaseline>();

    /// <summary>User-defined or default alert rules with severity overrides.</summary>
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();

    /// <summary>Temporary or permanent alert suppression rules.</summary>
    public DbSet<SuppressionRule> SuppressionRules => Set<SuppressionRule>();

    /// <summary>Escalation path configurations for SLA violations.</summary>
    public DbSet<EscalationPath> EscalationPaths => Set<EscalationPath>();

    /// <summary>Point-in-time compliance posture snapshots for trend analysis.</summary>
    public DbSet<ComplianceSnapshot> ComplianceSnapshots => Set<ComplianceSnapshot>();

    /// <summary>Opt-in auto-remediation rules for trusted, low-risk violations.</summary>
    public DbSet<AutoRemediationRule> AutoRemediationRules => Set<AutoRemediationRule>();

    // ─── RMF Persona-Driven Workflow DbSets (Feature 015) ────────────────────
    /// <summary>Registered systems for RMF lifecycle tracking.</summary>
    public DbSet<RegisteredSystem> RegisteredSystems => Set<RegisteredSystem>();

    /// <summary>FIPS 199 security categorizations for registered systems.</summary>
    public DbSet<SecurityCategorization> SecurityCategorizations => Set<SecurityCategorization>();

    /// <summary>SP 800-60 information types linked to security categorizations.</summary>
    public DbSet<InformationType> InformationTypes => Set<InformationType>();

    /// <summary>Authorization boundary resources for registered systems.</summary>
    public DbSet<AuthorizationBoundary> AuthorizationBoundaries => Set<AuthorizationBoundary>();

    /// <summary>RMF role assignments for registered systems.</summary>
    public DbSet<RmfRoleAssignment> RmfRoleAssignments => Set<RmfRoleAssignment>();

    /// <summary>Control baselines for registered systems (post-selection, post-tailoring).</summary>
    public DbSet<ControlBaseline> ControlBaselines => Set<ControlBaseline>();

    /// <summary>Control tailoring actions applied to baselines.</summary>
    public DbSet<ControlTailoring> ControlTailorings => Set<ControlTailoring>();

    /// <summary>Control inheritance designations for FedRAMP/DoD shared responsibility.</summary>
    public DbSet<ControlInheritance> ControlInheritances => Set<ControlInheritance>();

    /// <summary>Per-control implementation narratives for SSP authoring (Feature 015 US5).</summary>
    public DbSet<ControlImplementation> ControlImplementations => Set<ControlImplementation>();

    // ─── Assessment Artifact DbSets (Feature 015 — US7) ─────────────────────
    /// <summary>Per-control effectiveness determinations made by SCAs.</summary>
    public DbSet<ControlEffectiveness> ControlEffectivenessRecords => Set<ControlEffectiveness>();

    /// <summary>Aggregate per-system assessment summaries.</summary>
    public DbSet<AssessmentRecord> AssessmentRecords => Set<AssessmentRecord>();

    // ─── Authorization & POA&M DbSets (Feature 015 — US8) ───────────────────
    /// <summary>AO authorization decisions (ATO/ATOwC/IATT/DATO).</summary>
    public DbSet<AuthorizationDecision> AuthorizationDecisions => Set<AuthorizationDecision>();

    /// <summary>Risk acceptances issued by AOs for specific findings.</summary>
    public DbSet<RiskAcceptance> RiskAcceptances => Set<RiskAcceptance>();

    /// <summary>Plan of Action and Milestones items tracking weaknesses.</summary>
    public DbSet<PoamItem> PoamItems => Set<PoamItem>();

    /// <summary>Milestones within POA&M items.</summary>
    public DbSet<PoamMilestone> PoamMilestones => Set<PoamMilestone>();

    // ─── Continuous Monitoring DbSets (Feature 015 — US9) ────────────────────
    /// <summary>Formal ConMon plans (one per system).</summary>
    public DbSet<ConMonPlan> ConMonPlans => Set<ConMonPlan>();

    /// <summary>Periodic ConMon reports (monthly/quarterly/annual).</summary>
    public DbSet<ConMonReport> ConMonReports => Set<ConMonReport>();

    /// <summary>Significant system changes that may trigger reauthorization.</summary>
    public DbSet<SignificantChange> SignificantChanges => Set<SignificantChange>();

    // ─── SCAP/STIG Import DbSets (Feature 017) ──────────────────────────────
    /// <summary>Tracks each file import operation (one per CKL/XCCDF file).</summary>
    public DbSet<ScanImportRecord> ScanImportRecords => Set<ScanImportRecord>();

    /// <summary>Per-finding audit trail linking raw parsed data to ComplianceFindings.</summary>
    public DbSet<ScanImportFinding> ScanImportFindings => Set<ScanImportFinding>();

    // ─── SAP Generation DbSets (Feature 018) ────────────────────────────────
    /// <summary>Security Assessment Plans (RMF Step 4 deliverables).</summary>
    public DbSet<SecurityAssessmentPlan> SecurityAssessmentPlans => Set<SecurityAssessmentPlan>();

    /// <summary>Per-control assessment plan entries within SAPs.</summary>
    public DbSet<SapControlEntry> SapControlEntries => Set<SapControlEntry>();

    /// <summary>Assessment team members assigned to SAPs.</summary>
    public DbSet<SapTeamMember> SapTeamMembers => Set<SapTeamMember>();

    // ─── Privacy & Interconnections (Feature 021) ────────────────────────────

    /// <summary>Privacy Threshold Analyses — one per registered system.</summary>
    public DbSet<PrivacyThresholdAnalysis> PrivacyThresholdAnalyses => Set<PrivacyThresholdAnalysis>();

    /// <summary>Privacy Impact Assessments — one per registered system (when PTA determination = PiaRequired).</summary>
    public DbSet<PrivacyImpactAssessment> PrivacyImpactAssessments => Set<PrivacyImpactAssessment>();

    /// <summary>System interconnections crossing the authorization boundary.</summary>
    public DbSet<SystemInterconnection> SystemInterconnections => Set<SystemInterconnection>();

    /// <summary>ISA/MOU/SLA agreements governing system interconnections.</summary>
    public DbSet<InterconnectionAgreement> InterconnectionAgreements => Set<InterconnectionAgreement>();

    // ─── SSP Sections (Feature 022) ──────────────────────────────────────────

    /// <summary>Individual NIST SP 800-18 SSP sections with lifecycle tracking.</summary>
    public DbSet<SspSection> SspSections => Set<SspSection>();

    /// <summary>External contingency plan document references for SSP §13.</summary>
    public DbSet<ContingencyPlanReference> ContingencyPlanReferences => Set<ContingencyPlanReference>();

    // ─── Narrative Governance (Feature 024) ───────────────────────────────────

    /// <summary>Immutable version snapshots of control-implementation narratives.</summary>
    public DbSet<NarrativeVersion> NarrativeVersions => Set<NarrativeVersion>();

    /// <summary>Review decisions recorded against narrative versions.</summary>
    public DbSet<NarrativeReview> NarrativeReviews => Set<NarrativeReview>();

    // ─── HW/SW Inventory (Feature 025) ──────────────────────────────────────

    /// <summary>Hardware and software inventory items for registered systems.</summary>
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    // ─── Cached Responses (Feature 029) ──────────────────────────────────────

    /// <summary>Persistent cache entries for offline mode (FR-036).</summary>
    public DbSet<CachedResponse> CachedResponses => Set<CachedResponse>();

    // ─── Compliance Dashboard (Feature 030) ──────────────────────────────────

    /// <summary>Organization-wide security capabilities (write once, apply everywhere).</summary>
    public DbSet<SecurityCapability> SecurityCapabilities => Set<SecurityCapability>();

    /// <summary>Capability-to-control mappings with role and optional system scope.</summary>
    public DbSet<CapabilityControlMapping> CapabilityControlMappings => Set<CapabilityControlMapping>();

    /// <summary>System component inventory items (Person/Place/Thing).</summary>
    public DbSet<SystemComponent> SystemComponents => Set<SystemComponent>();

    /// <summary>Join table linking components to capabilities.</summary>
    public DbSet<ComponentCapabilityLink> ComponentCapabilityLinks => Set<ComponentCapabilityLink>();

    /// <summary>Join table linking org-wide components to systems with boundary scope (Feature 036).</summary>
    public DbSet<ComponentSystemAssignment> ComponentSystemAssignments => Set<ComponentSystemAssignment>();

    /// <summary>Point-in-time compliance metric snapshots for trend visualization.</summary>
    public DbSet<ComplianceTrendSnapshot> ComplianceTrendSnapshots => Set<ComplianceTrendSnapshot>();

    /// <summary>Denormalized activity feed entries for dashboard rendering.</summary>
    public DbSet<DashboardActivity> DashboardActivities => Set<DashboardActivity>();

    // ─── Boundary-Scoped Model (Feature 033) ───────────────────────────────

    /// <summary>Named authorization boundary definitions within registered systems.</summary>
    public DbSet<AuthorizationBoundaryDefinition> AuthorizationBoundaryDefinitions => Set<AuthorizationBoundaryDefinition>();

    // ─── Deferred Prerequisites (Force-Advanced Gate Tracking) ───────────

    /// <summary>Prerequisites skipped during forced RMF phase advances.</summary>
    public DbSet<DeferredPrerequisite> DeferredPrerequisites => Set<DeferredPrerequisite>();

    // ─── Deviation Management (Feature 035) ──────────────────────────────────

    /// <summary>Compliance deviations (false positives, risk acceptances, waivers) with approval workflow.</summary>
    public DbSet<Deviation> Deviations => Set<Deviation>();

    // ─── Implementation Roadmap (Feature 031) ────────────────────────────────

    /// <summary>Phased implementation roadmaps for closing compliance gaps.</summary>
    public DbSet<ImplementationRoadmap> ImplementationRoadmaps => Set<ImplementationRoadmap>();

    /// <summary>Logical phase groupings within implementation roadmaps.</summary>
    public DbSet<RoadmapPhase> RoadmapPhases => Set<RoadmapPhase>();

    /// <summary>Individual control-gap items assigned to roadmap phases.</summary>
    public DbSet<RoadmapItem> RoadmapItems => Set<RoadmapItem>();

    // ─── SSP Document Export (Feature 037) ──────────────────────────────────

    /// <summary>SSP export job metadata and audit records.</summary>
    public DbSet<SspExport> SspExports => Set<SspExport>();

    /// <summary>Custom DOCX templates for SSP document generation.</summary>
    public DbSet<SspTemplate> SspTemplates => Set<SspTemplate>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ─── Value Converters ────────────────────────────────────────────────────
        var stringListConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
        );

        var controlFamilyResultsConverter = new ValueConverter<List<ControlFamilyAssessment>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<ControlFamilyAssessment>>(v, (JsonSerializerOptions?)null) ?? new List<ControlFamilyAssessment>()
        );

        var riskProfileConverter = new ValueConverter<RiskProfile?, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => v == null ? null : JsonSerializer.Deserialize<RiskProfile>(v, (JsonSerializerOptions?)null)
        );

        var scanPillarResultsConverter = new ValueConverter<Dictionary<string, bool>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<Dictionary<string, bool>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, bool>()
        );

        // ─── ComplianceAssessment ────────────────────────────────────────────────
        modelBuilder.Entity<ComplianceAssessment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(100);
            entity.Property(e => e.SubscriptionId).HasMaxLength(100);
            entity.Property(e => e.Framework).HasMaxLength(50);
            entity.Property(e => e.Baseline).HasMaxLength(20);
            entity.Property(e => e.ScanType).HasMaxLength(20);
            entity.Property(e => e.InitiatedBy).HasMaxLength(200);
            entity.Property(e => e.ProgressMessage).HasMaxLength(500);

            // Owned types: ScanSummary stored as columns in Assessments table
            entity.OwnsOne(e => e.ResourceScanSummary, summary =>
            {
                summary.Property(s => s.ResourcesScanned).HasColumnName("ResourceScan_ResourcesScanned");
                summary.Property(s => s.PoliciesEvaluated).HasColumnName("ResourceScan_PoliciesEvaluated");
                summary.Property(s => s.Compliant).HasColumnName("ResourceScan_Compliant");
                summary.Property(s => s.NonCompliant).HasColumnName("ResourceScan_NonCompliant");
                summary.Property(s => s.CompliancePercentage).HasColumnName("ResourceScan_CompliancePercentage");
            });
            entity.OwnsOne(e => e.PolicyScanSummary, summary =>
            {
                summary.Property(s => s.ResourcesScanned).HasColumnName("PolicyScan_ResourcesScanned");
                summary.Property(s => s.PoliciesEvaluated).HasColumnName("PolicyScan_PoliciesEvaluated");
                summary.Property(s => s.Compliant).HasColumnName("PolicyScan_Compliant");
                summary.Property(s => s.NonCompliant).HasColumnName("PolicyScan_NonCompliant");
                summary.Property(s => s.CompliancePercentage).HasColumnName("PolicyScan_CompliancePercentage");
            });

            // Relationship: Assessment 1:N Findings (cascade delete)
            entity.HasMany(e => e.Findings)
                .WithOne()
                .HasForeignKey(f => f.AssessmentId)
                .OnDelete(DeleteBehavior.Cascade);

            // JSON column conversions for new properties (Feature 008)
            entity.Property(e => e.ControlFamilyResults).HasConversion(controlFamilyResultsConverter);
            entity.Property(e => e.RiskProfile).HasConversion(riskProfileConverter);
            entity.Property(e => e.ScanPillarResults).HasConversion(scanPillarResultsConverter);
            entity.Property(e => e.SubscriptionIds).HasConversion(stringListConverter);
            entity.Property(e => e.ResourceGroupFilter).HasMaxLength(200);
            entity.Property(e => e.EnvironmentName).HasMaxLength(200);

            // Optional FK to RegisteredSystem (Feature 015 — US7, nullable for backward compat)
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36);
            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            // Indexes
            entity.HasIndex(e => e.SubscriptionId);
            entity.HasIndex(e => e.AssessedAt);
            entity.HasIndex(e => new { e.SubscriptionId, e.Framework });
        });

        // ─── ComplianceFinding ───────────────────────────────────────────────────
        modelBuilder.Entity<ComplianceFinding>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(100);
            entity.Property(e => e.AssessmentId).HasMaxLength(100);
            entity.Property(e => e.ControlId).HasMaxLength(20);
            entity.Property(e => e.ControlFamily).HasMaxLength(5);
            entity.Property(e => e.ResourceType).HasMaxLength(200);
            entity.Property(e => e.Source).HasMaxLength(50);
            entity.Property(e => e.PolicyDefinitionId).HasMaxLength(500);
            entity.Property(e => e.PolicyAssignmentId).HasMaxLength(500);
            entity.Property(e => e.DefenderRecommendationId).HasMaxLength(200);

            // New properties (Feature 008)
            entity.Property(e => e.StigId).HasMaxLength(50);
            entity.Property(e => e.RemediatedBy).HasMaxLength(200);

            // New properties (Feature 015 — US7)
            entity.Property(e => e.CatSeverity).HasConversion<string>().HasMaxLength(10);

            // New properties (Feature 017 — SCAP/STIG Import)
            entity.Property(e => e.ImportRecordId).HasMaxLength(100);

            // Relationship: ComplianceFinding → ScanImportRecord (optional)
            entity.HasOne<ScanImportRecord>()
                .WithMany()
                .HasForeignKey(e => e.ImportRecordId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            entity.HasIndex(e => e.ControlId);
            entity.HasIndex(e => e.AssessmentId);
            entity.HasIndex(e => e.ControlFamily);
            entity.HasIndex(e => new { e.AssessmentId, e.Severity });
            entity.HasIndex(e => e.ImportRecordId).HasDatabaseName("IX_ComplianceFinding_ImportRecordId");
            entity.HasIndex(e => e.DeviationId).HasDatabaseName("IX_ComplianceFinding_DeviationId");
            entity.Property(e => e.DeviationId).HasMaxLength(36);
        });

        // ─── NistControl ────────────────────────────────────────────────────────
        modelBuilder.Entity<NistControl>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(20);
            entity.Property(e => e.Family).HasMaxLength(5);
            entity.Property(e => e.ImpactLevel).HasMaxLength(20);
            entity.Property(e => e.ParentControlId).HasMaxLength(20);

            // Value conversions for List<string> properties
            entity.Property(e => e.Enhancements).HasConversion(stringListConverter);
            entity.Property(e => e.Baselines).HasConversion(stringListConverter);
            entity.Property(e => e.AzurePolicyDefinitionIds).HasConversion(stringListConverter);

            // Self-referential: NistControl 1:N ControlEnhancements
            // SQL Server does not allow CASCADE on self-referencing FKs.
            entity.HasMany(e => e.ControlEnhancements)
                .WithOne()
                .HasForeignKey(e => e.ParentControlId)
                .OnDelete(DeleteBehavior.Restrict);

            // NistControl 1:N ComplianceFinding (restrict — findings must not be orphaned)
            entity.HasMany<ComplianceFinding>()
                .WithOne()
                .HasForeignKey(f => f.ControlId)
                .HasPrincipalKey(c => c.Id)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            // Indexes
            entity.HasIndex(e => e.Family);
            entity.HasIndex(e => e.ParentControlId);
        });

        // ─── ComplianceEvidence ──────────────────────────────────────────────────
        modelBuilder.Entity<ComplianceEvidence>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.ControlId).HasMaxLength(20);
            entity.Property(e => e.SubscriptionId).HasMaxLength(100);
            entity.Property(e => e.EvidenceType).HasMaxLength(50);
            entity.Property(e => e.CollectedBy).HasMaxLength(200);
            entity.Property(e => e.ContentHash).HasMaxLength(64);
            entity.Property(e => e.ResourceId).HasMaxLength(500);

            // New properties (Feature 015 — US7)
            entity.Property(e => e.CollectorIdentity).HasMaxLength(200);
            entity.Property(e => e.CollectionMethod).HasMaxLength(50);

            // Indexes
            entity.HasIndex(e => e.ControlId);
            entity.HasIndex(e => e.AssessmentId);
        });

        // ─── ComplianceDocument ──────────────────────────────────────────────────
        modelBuilder.Entity<ComplianceDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.DocumentType).HasMaxLength(10);
            entity.Property(e => e.Framework).HasMaxLength(50);
            entity.Property(e => e.SystemName).HasMaxLength(200);
            entity.Property(e => e.Owner).HasMaxLength(200);
            entity.Property(e => e.GeneratedBy).HasMaxLength(200);

            // Owned type: DocumentMetadata stored as columns in Documents table
            entity.OwnsOne(e => e.Metadata, meta =>
            {
                meta.Property(m => m.PreparedBy).HasMaxLength(200);
                meta.Property(m => m.ApprovedBy).HasMaxLength(200);
            });
        });

        // ─── RemediationPlan ─────────────────────────────────────────────────────
        modelBuilder.Entity<RemediationPlan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.SubscriptionId).HasMaxLength(100);
            entity.Property(e => e.ApprovedBy).HasMaxLength(200);
            entity.Property(e => e.FailedStepId).HasMaxLength(50);

            // Owned collection: RemediationSteps in separate table with implicit FK
            entity.OwnsMany(e => e.Steps, step =>
            {
                step.WithOwner().HasForeignKey("RemediationPlanId");
                step.HasKey(s => s.Id);
                step.Property(s => s.ControlId).HasMaxLength(20);
                step.Property(s => s.FindingId).HasMaxLength(100);
                step.Property(s => s.Effort).HasMaxLength(20);
                step.Property(s => s.ResourceId).HasMaxLength(500);
            });
        });

        // ─── AuditLogEntry ──────────────────────────────────────────────────────
        modelBuilder.Entity<AuditLogEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.UserId).HasMaxLength(200);
            entity.Property(e => e.UserRole).HasMaxLength(50);
            entity.Property(e => e.Action).HasMaxLength(50);
            entity.Property(e => e.ScanType).HasMaxLength(20);
            entity.Property(e => e.SubscriptionId).HasMaxLength(100);

            // Value conversions for List<string> properties
            entity.Property(e => e.AffectedResources).HasConversion(stringListConverter);
            entity.Property(e => e.AffectedControls).HasConversion(stringListConverter);

            // Indexes
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.SubscriptionId);
            entity.HasIndex(e => new { e.UserId, e.Timestamp });
        });

        // ─── RemediationBoard ────────────────────────────────────────────────────
        modelBuilder.Entity<RemediationBoard>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.SubscriptionId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.AssessmentId).HasMaxLength(100);
            entity.Property(e => e.Owner).HasMaxLength(200).IsRequired();
            entity.Property(e => e.RowVersion).IsConcurrencyToken();

            // Relationship: Board → Assessment (optional, restrict delete)
            entity.HasOne<ComplianceAssessment>()
                .WithMany()
                .HasForeignKey(e => e.AssessmentId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            // Relationship: Board 1:N Tasks (cascade delete)
            entity.HasMany(e => e.Tasks)
                .WithOne(t => t.Board)
                .HasForeignKey(t => t.BoardId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.SubscriptionId);
            entity.HasIndex(e => new { e.SubscriptionId, e.IsArchived });
        });

        // ─── RemediationTask ─────────────────────────────────────────────────────
        modelBuilder.Entity<RemediationTask>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.TaskNumber).HasMaxLength(10).IsRequired();
            entity.Property(e => e.BoardId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(4000);
            entity.Property(e => e.ControlId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ControlFamily).HasMaxLength(5);
            entity.Property(e => e.AssigneeId).HasMaxLength(200);
            entity.Property(e => e.AssigneeName).HasMaxLength(200);
            entity.Property(e => e.RemediationScript).HasMaxLength(8000);
            entity.Property(e => e.RemediationScriptType).HasMaxLength(20);
            entity.Property(e => e.ValidationCriteria).HasMaxLength(2000);
            entity.Property(e => e.FindingId).HasMaxLength(100);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.RoadmapItemId).HasMaxLength(36);
            entity.Property(e => e.RowVersion).IsConcurrencyToken();

            // Value conversion for List<string> AffectedResources
            entity.Property(e => e.AffectedResources).HasConversion(stringListConverter);

            // Relationship: Task 1:N Comments (cascade delete)
            entity.HasMany(e => e.Comments)
                .WithOne(c => c.Task)
                .HasForeignKey(c => c.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relationship: Task 1:N History (cascade delete)
            entity.HasMany(e => e.History)
                .WithOne(h => h.Task)
                .HasForeignKey(h => h.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.BoardId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.AssigneeId);
            entity.HasIndex(e => e.ControlId);
            entity.HasIndex(e => e.DueDate);
            entity.HasIndex(e => new { e.BoardId, e.Status });
            entity.HasIndex(e => new { e.BoardId, e.ControlFamily });
            entity.HasIndex(e => e.RoadmapItemId);
        });

        // ─── TaskComment ─────────────────────────────────────────────────────────
        modelBuilder.Entity<TaskComment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.TaskId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.AuthorId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.AuthorName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Content).HasMaxLength(4000).IsRequired();
            entity.Property(e => e.ParentCommentId).HasMaxLength(100);

            // Value conversion for List<string> Mentions
            entity.Property(e => e.Mentions).HasConversion(stringListConverter);

            // Indexes
            entity.HasIndex(e => e.TaskId);
            entity.HasIndex(e => new { e.TaskId, e.CreatedAt });
        });

        // ─── TaskHistoryEntry ────────────────────────────────────────────────────
        modelBuilder.Entity<TaskHistoryEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.TaskId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.OldValue).HasMaxLength(500);
            entity.Property(e => e.NewValue).HasMaxLength(500);
            entity.Property(e => e.ActingUserId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ActingUserName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Details).HasMaxLength(4000);

            // Indexes
            entity.HasIndex(e => e.TaskId);
            entity.HasIndex(e => new { e.TaskId, e.Timestamp });
        });

        // ─── CacSession ─────────────────────────────────────────────────────────
        modelBuilder.Entity<CacSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(200);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ClientType).HasConversion<string>().HasMaxLength(20);

            entity.HasIndex(e => new { e.UserId, e.Status }).HasDatabaseName("IX_CacSession_UserId_Status");
            entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("IX_CacSession_ExpiresAt");
        });

        // ─── JitRequestEntity ────────────────────────────────────────────────────
        modelBuilder.Entity<JitRequestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.UserDisplayName).HasMaxLength(200);
            entity.Property(e => e.ConversationId).HasMaxLength(100);
            entity.Property(e => e.RoleName).HasMaxLength(200);
            entity.Property(e => e.Scope).HasMaxLength(500);
            entity.Property(e => e.ScopeDisplayName).HasMaxLength(500);
            entity.Property(e => e.Justification).HasMaxLength(500).IsRequired();
            entity.Property(e => e.TicketNumber).HasMaxLength(50);
            entity.Property(e => e.TicketSystem).HasMaxLength(50);
            entity.Property(e => e.PimRequestId).HasMaxLength(100);
            entity.Property(e => e.ApproverId).HasMaxLength(200);
            entity.Property(e => e.ApproverDisplayName).HasMaxLength(200);
            entity.Property(e => e.ApproverComments).HasMaxLength(500);
            entity.Property(e => e.VmName).HasMaxLength(200);
            entity.Property(e => e.ResourceGroup).HasMaxLength(200);
            entity.Property(e => e.SubscriptionId).HasMaxLength(100);
            entity.Property(e => e.Protocol).HasMaxLength(10);
            entity.Property(e => e.SourceIp).HasMaxLength(45);
            entity.Property(e => e.RequestType).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.RowVersion).IsConcurrencyToken();

            // Relationship: JitRequest → CacSession (optional, restrict delete)
            entity.HasOne(e => e.Session)
                .WithMany()
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            entity.HasIndex(e => new { e.UserId, e.Status }).HasDatabaseName("IX_JitRequest_UserId_Status");
            entity.HasIndex(e => e.SessionId).HasDatabaseName("IX_JitRequest_SessionId");
            entity.HasIndex(e => e.RequestedAt).HasDatabaseName("IX_JitRequest_RequestedAt");
            entity.HasIndex(e => new { e.RoleName, e.Scope }).HasDatabaseName("IX_JitRequest_RoleName_Scope");
            entity.HasIndex(e => new { e.Status, e.ExpiresAt }).HasDatabaseName("IX_JitRequest_Status_ExpiresAt");
        });

        // ─── CertificateRoleMapping ──────────────────────────────────────────────
        modelBuilder.Entity<CertificateRoleMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CertificateThumbprint).HasMaxLength(64).IsRequired();
            entity.Property(e => e.CertificateSubject).HasMaxLength(500).IsRequired();
            entity.Property(e => e.MappedRole).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(200);

            entity.HasIndex(e => e.CertificateThumbprint).IsUnique().HasDatabaseName("IX_CertMapping_Thumbprint");
            entity.HasIndex(e => e.CertificateSubject).IsUnique().HasDatabaseName("IX_CertMapping_Subject");
        });

        // ─── ComplianceAlert ─────────────────────────────────────────────────
        modelBuilder.Entity<ComplianceAlert>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AlertId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.SubscriptionId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ControlId).HasMaxLength(20);
            entity.Property(e => e.ControlFamily).HasMaxLength(5);
            entity.Property(e => e.ActorId).HasMaxLength(200);
            entity.Property(e => e.AssignedTo).HasMaxLength(200);
            entity.Property(e => e.DismissedBy).HasMaxLength(200);
            entity.Property(e => e.AcknowledgedBy).HasMaxLength(200);

            // Value conversion for List<string> AffectedResources
            entity.Property(e => e.AffectedResources).HasConversion(stringListConverter);

            // Self-referential FK: GroupedAlertId → ComplianceAlert.Id (restricted cascade)
            entity.HasOne(e => e.GroupedAlert)
                .WithMany(e => e.ChildAlerts)
                .HasForeignKey(e => e.GroupedAlertId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            // Relationship: Alert 1:N AlertNotifications (cascade delete)
            entity.HasMany(e => e.Notifications)
                .WithOne(n => n.Alert)
                .HasForeignKey(n => n.AlertId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.AlertId).IsUnique().HasDatabaseName("IX_ComplianceAlert_AlertId");
            entity.HasIndex(e => new { e.Status, e.Severity }).HasDatabaseName("IX_ComplianceAlert_Status_Severity");
            entity.HasIndex(e => new { e.SubscriptionId, e.CreatedAt }).HasDatabaseName("IX_ComplianceAlert_Sub_Created");
            entity.HasIndex(e => e.ControlFamily).HasDatabaseName("IX_ComplianceAlert_ControlFamily");
            entity.HasIndex(e => new { e.AssignedTo, e.Status }).HasDatabaseName("IX_ComplianceAlert_Assignee_Status");
            entity.HasIndex(e => e.GroupedAlertId).HasDatabaseName("IX_ComplianceAlert_GroupedAlertId");
            entity.HasIndex(e => new { e.SlaDeadline, e.Status }).HasDatabaseName("IX_ComplianceAlert_Sla_Status");

            // Optional FK: RegisteredSystemId → RegisteredSystem.Id (Phase 17 §9a.1)
            // Set null on delete so alerts survive system de-registration.
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36);
            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_ComplianceAlert_RegisteredSystemId");
        });

        // ─── AlertIdCounter ─────────────────────────────────────────────────
        modelBuilder.Entity<AlertIdCounter>(entity =>
        {
            entity.HasKey(e => e.Date);
        });

        // ─── AlertNotification ──────────────────────────────────────────────
        modelBuilder.Entity<AlertNotification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Recipient).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(500);
            entity.Property(e => e.UserId).HasMaxLength(200);

            entity.HasIndex(e => new { e.AlertId, e.Channel }).HasDatabaseName("IX_AlertNotification_Alert_Channel");
            entity.HasIndex(e => e.SentAt).HasDatabaseName("IX_AlertNotification_SentAt");
            entity.HasIndex(e => new { e.UserId, e.IsRead }).HasDatabaseName("IX_AlertNotification_User_Read");
        });

        // ─── NotificationPreferences ─────────────────────────────────────────
        modelBuilder.Entity<NotificationPreferences>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("IX_NotificationPreferences_UserId");
        });

        // ─── MonitoringConfiguration ─────────────────────────────────────────
        modelBuilder.Entity<MonitoringConfiguration>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SubscriptionId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ResourceGroupName).HasMaxLength(200);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => new { e.SubscriptionId, e.ResourceGroupName })
                .IsUnique()
                .HasDatabaseName("IX_MonitoringConfig_Sub_RG");
            entity.HasIndex(e => new { e.NextRunAt, e.IsEnabled })
                .HasDatabaseName("IX_MonitoringConfig_NextRun_Enabled");
        });

        // ─── ComplianceBaseline ──────────────────────────────────────────────
        modelBuilder.Entity<ComplianceBaseline>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SubscriptionId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ResourceId).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.ResourceType).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ConfigurationHash).HasMaxLength(64).IsRequired();

            entity.HasIndex(e => new { e.ResourceId, e.IsActive })
                .HasDatabaseName("IX_ComplianceBaseline_Resource_Active");
            entity.HasIndex(e => new { e.SubscriptionId, e.CapturedAt })
                .HasDatabaseName("IX_ComplianceBaseline_Sub_Captured");
        });

        // ─── AlertRule ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<AlertRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.SubscriptionId).HasMaxLength(100);
            entity.Property(e => e.ResourceGroupName).HasMaxLength(200);
            entity.Property(e => e.ResourceType).HasMaxLength(200);
            entity.Property(e => e.ResourceId).HasMaxLength(1000);
            entity.Property(e => e.ControlFamily).HasMaxLength(10);
            entity.Property(e => e.ControlId).HasMaxLength(20);
            entity.Property(e => e.TriggerCondition).HasMaxLength(4000);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.RecipientOverrides)
                .HasConversion(stringListConverter)
                .HasMaxLength(4000);

            entity.HasIndex(e => new { e.SubscriptionId, e.ControlFamily, e.IsEnabled })
                .HasDatabaseName("IX_AlertRule_Sub_Family_Enabled");
            entity.HasIndex(e => e.IsDefault)
                .HasDatabaseName("IX_AlertRule_IsDefault");
        });

        // ─── SuppressionRule ─────────────────────────────────────────────────────────
        modelBuilder.Entity<SuppressionRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SubscriptionId).HasMaxLength(100);
            entity.Property(e => e.ResourceGroupName).HasMaxLength(200);
            entity.Property(e => e.ResourceId).HasMaxLength(1000);
            entity.Property(e => e.ControlFamily).HasMaxLength(10);
            entity.Property(e => e.ControlId).HasMaxLength(20);
            entity.Property(e => e.Justification).HasMaxLength(2000);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => new { e.IsActive, e.ExpiresAt })
                .HasDatabaseName("IX_SuppressionRule_Active_Expires");
            entity.HasIndex(e => new { e.SubscriptionId, e.ResourceId })
                .HasDatabaseName("IX_SuppressionRule_Sub_Resource");
        });

        // ─── EscalationPath ─────────────────────────────────────────────────────────
        modelBuilder.Entity<EscalationPath>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.WebhookUrl).HasMaxLength(2000);
            entity.Property(e => e.Recipients).HasConversion(stringListConverter);

            entity.HasIndex(e => new { e.TriggerSeverity, e.IsEnabled })
                .HasDatabaseName("IX_EscalationPath_Severity_Enabled");
        });

        // ─── ComplianceSnapshot ─────────────────────────────────────────────────────
        modelBuilder.Entity<ComplianceSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SubscriptionId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ControlFamilyBreakdown).HasMaxLength(4000);

            entity.HasIndex(e => new { e.SubscriptionId, e.CapturedAt })
                .HasDatabaseName("IX_ComplianceSnapshot_Sub_CapturedAt");

            entity.HasIndex(e => new { e.IsWeeklySnapshot, e.CapturedAt })
                .HasDatabaseName("IX_ComplianceSnapshot_Weekly_CapturedAt");

            // New properties (Feature 015 — US7)
            entity.Property(e => e.IntegrityHash).HasMaxLength(64);
        });

        // ─── AutoRemediationRule ─────────────────────────────────────────────────────
        modelBuilder.Entity<AutoRemediationRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.SubscriptionId).HasMaxLength(200);
            entity.Property(e => e.ResourceGroupName).HasMaxLength(200);
            entity.Property(e => e.ControlFamily).HasMaxLength(10);
            entity.Property(e => e.ControlId).HasMaxLength(20);
            entity.Property(e => e.Action).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ApprovalMode).HasMaxLength(20).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => new { e.SubscriptionId, e.ControlFamily, e.IsEnabled })
                .HasDatabaseName("IX_AutoRemediationRule_Sub_Family_Enabled");

            entity.HasIndex(e => e.IsEnabled)
                .HasDatabaseName("IX_AutoRemediationRule_Enabled");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // RMF Persona-Driven Workflow Entities (Feature 015)
        // ═══════════════════════════════════════════════════════════════════════════

        // ─── RegisteredSystem ────────────────────────────────────────────────────
        modelBuilder.Entity<RegisteredSystem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Acronym).HasMaxLength(20);
            entity.Property(e => e.SystemType).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.MissionCriticality).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.ClassifiedDesignation).HasMaxLength(20);
            entity.Property(e => e.HostingEnvironment).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CurrentRmfStep).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            // Owned entity: AzureEnvironmentProfile stored as columns in RegisteredSystems table
            entity.OwnsOne(e => e.AzureProfile, profile =>
            {
                profile.Property(p => p.CloudEnvironment).HasConversion<string>().HasMaxLength(40).HasColumnName("Azure_CloudEnvironment");
                profile.Property(p => p.ArmEndpoint).HasMaxLength(500).HasColumnName("Azure_ArmEndpoint");
                profile.Property(p => p.AuthenticationEndpoint).HasMaxLength(500).HasColumnName("Azure_AuthenticationEndpoint");
                profile.Property(p => p.DefenderEndpoint).HasMaxLength(500).HasColumnName("Azure_DefenderEndpoint");
                profile.Property(p => p.PolicyEndpoint).HasMaxLength(500).HasColumnName("Azure_PolicyEndpoint");
                profile.Property(p => p.ProxyUrl).HasMaxLength(500).HasColumnName("Azure_ProxyUrl");
                profile.Property(p => p.SubscriptionIds).HasConversion(stringListConverter).HasColumnName("Azure_SubscriptionIds");
            });

            // Relationships
            entity.HasOne(e => e.SecurityCategorization)
                .WithOne(sc => sc.RegisteredSystem)
                .HasForeignKey<SecurityCategorization>(sc => sc.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ControlBaseline)
                .WithOne(cb => cb.RegisteredSystem)
                .HasForeignKey<ControlBaseline>(cb => cb.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.AuthorizationBoundaries)
                .WithOne(ab => ab.RegisteredSystem)
                .HasForeignKey(ab => ab.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.RmfRoleAssignments)
                .WithOne(ra => ra.RegisteredSystem)
                .HasForeignKey(ra => ra.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Privacy & Interconnection relationships (Feature 021)
            entity.HasOne(e => e.PrivacyThresholdAnalysis)
                .WithOne()
                .HasForeignKey<PrivacyThresholdAnalysis>(p => p.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PrivacyImpactAssessment)
                .WithOne()
                .HasForeignKey<PrivacyImpactAssessment>(p => p.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.SystemInterconnections)
                .WithOne()
                .HasForeignKey(i => i.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            // SSP section relationships (Feature 022)
            entity.HasMany(e => e.SspSections)
                .WithOne(s => s.RegisteredSystem)
                .HasForeignKey(s => s.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ContingencyPlanReference)
                .WithOne(c => c.RegisteredSystem)
                .HasForeignKey<ContingencyPlanReference>(c => c.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.DitprId).HasMaxLength(50);
            entity.Property(e => e.EmassId).HasMaxLength(50);
            entity.Property(e => e.OperationalStatus)
                .HasConversion<string>()
                .HasMaxLength(20);

            // Indexes
            entity.HasIndex(e => e.Name).HasDatabaseName("IX_RegisteredSystem_Name");
            entity.HasIndex(e => e.Acronym).HasDatabaseName("IX_RegisteredSystem_Acronym");
            entity.HasIndex(e => new { e.IsActive, e.CurrentRmfStep }).HasDatabaseName("IX_RegisteredSystem_Active_Step");
            entity.HasIndex(e => e.CreatedBy).HasDatabaseName("IX_RegisteredSystem_CreatedBy");
        });

        // ─── SspSection (Feature 022) ────────────────────────────────────────────
        modelBuilder.Entity<SspSection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.SectionTitle).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Content).HasMaxLength(32000);
            entity.Property(e => e.AuthoredBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ReviewedBy).HasMaxLength(200);
            entity.Property(e => e.ReviewerComments).HasMaxLength(4000);

            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Version).IsConcurrencyToken();

            entity.HasIndex(e => new { e.RegisteredSystemId, e.SectionNumber })
                .IsUnique()
                .HasDatabaseName("IX_SspSection_System_Number");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_SspSection_Status");
        });

        // ─── ContingencyPlanReference (Feature 022) ──────────────────────────────
        modelBuilder.Entity<ContingencyPlanReference>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.DocumentTitle).HasMaxLength(500).IsRequired();
            entity.Property(e => e.DocumentLocation).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.DocumentVersion).HasMaxLength(50);
            entity.Property(e => e.TestType).HasMaxLength(50);
            entity.Property(e => e.RecoveryTimeObjective).HasMaxLength(100);
            entity.Property(e => e.RecoveryPointObjective).HasMaxLength(100);
            entity.Property(e => e.AlternateProcessingSite).HasMaxLength(500);
            entity.Property(e => e.BackupProceduresSummary).HasMaxLength(4000);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => e.RegisteredSystemId)
                .IsUnique()
                .HasDatabaseName("IX_ContingencyPlan_SystemId");
        });

        // ─── SecurityCategorization ──────────────────────────────────────────────
        modelBuilder.Entity<SecurityCategorization>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Justification).HasMaxLength(4000);
            entity.Property(e => e.CategorizedBy).HasMaxLength(200).IsRequired();

            // One categorization per system
            entity.HasIndex(e => e.RegisteredSystemId).IsUnique().HasDatabaseName("IX_SecurityCategorization_SystemId");

            entity.HasMany(e => e.InformationTypes)
                .WithOne(it => it.SecurityCategorization)
                .HasForeignKey(it => it.SecurityCategorizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── InformationType ─────────────────────────────────────────────────────
        modelBuilder.Entity<InformationType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.SecurityCategorizationId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Sp80060Id).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(200);
            entity.Property(e => e.ConfidentialityImpact).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.IntegrityImpact).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.AvailabilityImpact).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.AdjustmentJustification).HasMaxLength(2000);

            // Indexes
            entity.HasIndex(e => e.SecurityCategorizationId).HasDatabaseName("IX_InformationType_CategorizationId");
            entity.HasIndex(e => e.Sp80060Id).HasDatabaseName("IX_InformationType_Sp80060Id");
        });

        // ─── AuthorizationBoundary ───────────────────────────────────────────────
        modelBuilder.Entity<AuthorizationBoundary>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.ResourceId).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ResourceType).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ResourceName).HasMaxLength(200);
            entity.Property(e => e.ExclusionRationale).HasMaxLength(1000);
            entity.Property(e => e.InheritanceProvider).HasMaxLength(200);
            entity.Property(e => e.AddedBy).HasMaxLength(200).IsRequired();

            // Indexes
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_AuthorizationBoundary_SystemId");
            entity.HasIndex(e => new { e.RegisteredSystemId, e.ResourceId }).HasDatabaseName("IX_AuthorizationBoundary_System_Resource");
        });

        // ─── RmfRoleAssignment ───────────────────────────────────────────────────
        modelBuilder.Entity<RmfRoleAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.RmfRole).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.UserId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.UserDisplayName).HasMaxLength(200);
            entity.Property(e => e.AssignedBy).HasMaxLength(200).IsRequired();

            // Indexes
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_RmfRoleAssignment_SystemId");
            entity.HasIndex(e => new { e.RegisteredSystemId, e.RmfRole }).HasDatabaseName("IX_RmfRoleAssignment_System_Role");
            entity.HasIndex(e => e.UserId).HasDatabaseName("IX_RmfRoleAssignment_UserId");
        });

        // ─── ControlBaseline ─────────────────────────────────────────────────────
        modelBuilder.Entity<ControlBaseline>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.BaselineLevel).HasMaxLength(20).IsRequired();
            entity.Property(e => e.OverlayApplied).HasMaxLength(100);
            entity.Property(e => e.ControlIds).HasConversion(stringListConverter);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            // One baseline per system
            entity.HasIndex(e => e.RegisteredSystemId).IsUnique().HasDatabaseName("IX_ControlBaseline_SystemId");

            entity.HasMany(e => e.Tailorings)
                .WithOne(t => t.ControlBaseline)
                .HasForeignKey(t => t.ControlBaselineId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Inheritances)
                .WithOne(i => i.ControlBaseline)
                .HasForeignKey(i => i.ControlBaselineId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── ControlTailoring ────────────────────────────────────────────────────
        modelBuilder.Entity<ControlTailoring>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.ControlBaselineId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.ControlId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.Rationale).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.TailoredBy).HasMaxLength(200).IsRequired();

            // Indexes
            entity.HasIndex(e => e.ControlBaselineId).HasDatabaseName("IX_ControlTailoring_BaselineId");
            entity.HasIndex(e => new { e.ControlBaselineId, e.ControlId }).HasDatabaseName("IX_ControlTailoring_Baseline_Control");
        });

        // ─── ControlInheritance ──────────────────────────────────────────────────
        modelBuilder.Entity<ControlInheritance>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.ControlBaselineId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.ControlId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.InheritanceType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Provider).HasMaxLength(200);
            entity.Property(e => e.CustomerResponsibility).HasMaxLength(2000);
            entity.Property(e => e.SetBy).HasMaxLength(200).IsRequired();

            // Indexes
            entity.HasIndex(e => e.ControlBaselineId).HasDatabaseName("IX_ControlInheritance_BaselineId");
            entity.HasIndex(e => new { e.ControlBaselineId, e.ControlId }).HasDatabaseName("IX_ControlInheritance_Baseline_Control");
            entity.HasIndex(e => e.InheritanceType).HasDatabaseName("IX_ControlInheritance_Type");
        });

        // ─── ControlImplementation (Feature 015 US5 — SSP Authoring) ─────────────
        modelBuilder.Entity<ControlImplementation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.ControlId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ImplementationStatus).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Narrative).HasMaxLength(8000);
            entity.Property(e => e.AuthoredBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ReviewedBy).HasMaxLength(200);

            // Governance fields (Feature 024)
            entity.Property(e => e.ApprovalStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ApprovedVersionId).HasMaxLength(36);

            // Dashboard capability link (Feature 030)
            entity.Property(e => e.SecurityCapabilityId).HasMaxLength(36);
            entity.HasOne(e => e.SecurityCapability)
                .WithMany()
                .HasForeignKey(e => e.SecurityCapabilityId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.SecurityCapabilityId)
                .HasDatabaseName("IX_ControlImplementation_SecurityCapabilityId");

            // Unique constraint: one implementation per control per system
            entity.HasIndex(e => new { e.RegisteredSystemId, e.ControlId })
                .IsUnique()
                .HasDatabaseName("IX_ControlImplementation_System_Control");

            // Indexes
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_ControlImplementation_SystemId");
            entity.HasIndex(e => e.ImplementationStatus).HasDatabaseName("IX_ControlImplementation_Status");
            entity.HasIndex(e => e.ApprovalStatus).HasDatabaseName("IX_ControlImplementation_ApprovalStatus");

            // FK to RegisteredSystem
            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            // FK to approved NarrativeVersion (optional)
            // Use NoAction to avoid SQL Server error 1785 (cycle:
            // ControlImplementation ↔ NarrativeVersion via Versions CASCADE).
            entity.HasOne(e => e.ApprovedVersion)
                .WithMany()
                .HasForeignKey(e => e.ApprovedVersionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // Assessment Artifact Entities (Feature 015 — US7)
        // ═══════════════════════════════════════════════════════════════════════════

        // ─── ControlEffectiveness ────────────────────────────────────────────────
        modelBuilder.Entity<ControlEffectiveness>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.AssessmentId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.ControlId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Determination).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.AssessmentMethod).HasMaxLength(50);
            entity.Property(e => e.Notes).HasMaxLength(4000);
            entity.Property(e => e.CatSeverity).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.AssessorId).HasMaxLength(200).IsRequired();

            // JSON column: EvidenceIds
            entity.Property(e => e.EvidenceIds).HasConversion(stringListConverter);

            // FK to ComplianceAssessment
            entity.HasOne(e => e.Assessment)
                .WithMany()
                .HasForeignKey(e => e.AssessmentId)
                .OnDelete(DeleteBehavior.Restrict);

            // FK to RegisteredSystem
            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.AssessmentId).HasDatabaseName("IX_ControlEffectiveness_AssessmentId");
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_ControlEffectiveness_SystemId");
            entity.HasIndex(e => new { e.AssessmentId, e.ControlId }).HasDatabaseName("IX_ControlEffectiveness_Assessment_Control");
            entity.HasIndex(e => e.Determination).HasDatabaseName("IX_ControlEffectiveness_Determination");
        });

        // ─── AssessmentRecord ────────────────────────────────────────────────────
        modelBuilder.Entity<AssessmentRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.ComplianceAssessmentId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.OverallDetermination).HasMaxLength(50);
            entity.Property(e => e.AssessorId).HasMaxLength(200).IsRequired();
            entity.Property(e => e.AssessorName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(4000);

            // Unique constraint: one record per system per assessment
            entity.HasIndex(e => new { e.RegisteredSystemId, e.ComplianceAssessmentId })
                .IsUnique()
                .HasDatabaseName("IX_AssessmentRecord_System_Assessment");

            // FK to RegisteredSystem
            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            // FK to ComplianceAssessment
            entity.HasOne(e => e.ComplianceAssessment)
                .WithMany()
                .HasForeignKey(e => e.ComplianceAssessmentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_AssessmentRecord_SystemId");
            entity.HasIndex(e => e.AssessedAt).HasDatabaseName("IX_AssessmentRecord_AssessedAt");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // Authorization & POA&M Entities (Feature 015 — US8)
        // ═══════════════════════════════════════════════════════════════════════════

        // ─── AuthorizationDecision ───────────────────────────────────────────────
        modelBuilder.Entity<AuthorizationDecision>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.DecisionType).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.TermsAndConditions).HasMaxLength(8000);
            entity.Property(e => e.ResidualRiskLevel).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ResidualRiskJustification).HasMaxLength(4000);
            entity.Property(e => e.FindingsAtDecision).HasMaxLength(1000);
            entity.Property(e => e.IssuedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.IssuedByName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.SupersededById).HasMaxLength(36);

            // FK to RegisteredSystem
            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Self-referencing FK for supersession
            entity.HasOne(e => e.SupersededBy)
                .WithMany()
                .HasForeignKey(e => e.SupersededById)
                .OnDelete(DeleteBehavior.Restrict);

            // Risk acceptances
            entity.HasMany(e => e.RiskAcceptances)
                .WithOne(e => e.AuthorizationDecision)
                .HasForeignKey(e => e.AuthorizationDecisionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_AuthorizationDecision_SystemId");
            entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_AuthorizationDecision_IsActive");
            entity.HasIndex(e => e.DecisionDate).HasDatabaseName("IX_AuthorizationDecision_DecisionDate");
        });

        // ─── RiskAcceptance ──────────────────────────────────────────────────────
        modelBuilder.Entity<RiskAcceptance>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.AuthorizationDecisionId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.FindingId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ControlId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.CatSeverity).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.Justification).HasMaxLength(4000).IsRequired();
            entity.Property(e => e.CompensatingControl).HasMaxLength(2000);
            entity.Property(e => e.AcceptedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.RevokedBy).HasMaxLength(200);
            entity.Property(e => e.RevocationReason).HasMaxLength(1000);

            // FK to ComplianceFinding
            entity.HasOne(e => e.Finding)
                .WithMany()
                .HasForeignKey(e => e.FindingId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            entity.HasIndex(e => e.AuthorizationDecisionId).HasDatabaseName("IX_RiskAcceptance_DecisionId");
            entity.HasIndex(e => e.FindingId).HasDatabaseName("IX_RiskAcceptance_FindingId");
            entity.HasIndex(e => e.IsActive).HasDatabaseName("IX_RiskAcceptance_IsActive");
            entity.HasIndex(e => e.ExpirationDate).HasDatabaseName("IX_RiskAcceptance_ExpirationDate");
        });

        // ─── PoamItem ────────────────────────────────────────────────────────────
        modelBuilder.Entity<PoamItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.FindingId).HasMaxLength(100);
            entity.Property(e => e.RemediationTaskId).HasMaxLength(36);
            entity.Property(e => e.Weakness).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.WeaknessSource).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SecurityControlNumber).HasMaxLength(20).IsRequired();
            entity.Property(e => e.CatSeverity).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.PointOfContact).HasMaxLength(200).IsRequired();
            entity.Property(e => e.PocEmail).HasMaxLength(200);
            entity.Property(e => e.ResourcesRequired).HasMaxLength(1000);
            entity.Property(e => e.CostEstimate).HasColumnType("decimal(18,2)");
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Comments).HasMaxLength(4000);

            // DeviationId FK (Feature 035)
            entity.Property(e => e.DeviationId).HasMaxLength(36);

            // FK to RegisteredSystem
            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            // FK to ComplianceFinding (optional)
            entity.HasOne(e => e.Finding)
                .WithMany()
                .HasForeignKey(e => e.FindingId)
                .OnDelete(DeleteBehavior.Restrict);

            // Milestones
            entity.HasMany(e => e.Milestones)
                .WithOne(e => e.PoamItem)
                .HasForeignKey(e => e.PoamItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_PoamItem_SystemId");
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_PoamItem_Status");
            entity.HasIndex(e => e.CatSeverity).HasDatabaseName("IX_PoamItem_CatSeverity");
            entity.HasIndex(e => e.ScheduledCompletionDate).HasDatabaseName("IX_PoamItem_ScheduledDate");
            entity.HasIndex(e => e.DeviationId).HasDatabaseName("IX_PoamItem_DeviationId");
        });

        // ─── Deviation (Feature 035) ────────────────────────────────────────────
        modelBuilder.Entity<Deviation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.DeviationType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.ControlId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.CatSeverity).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.Justification).HasMaxLength(4000).IsRequired();
            entity.Property(e => e.CompensatingControls).HasMaxLength(2000);
            entity.Property(e => e.ReviewCycle).HasMaxLength(20).IsRequired();
            entity.Property(e => e.FindingId).HasMaxLength(100);
            entity.Property(e => e.PoamEntryId).HasMaxLength(36);
            entity.Property(e => e.AuthorizationDecisionId).HasMaxLength(36);
            entity.Property(e => e.BoundaryDefinitionId).HasMaxLength(36);
            entity.Property(e => e.RequestedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ReviewedBy).HasMaxLength(200);
            entity.Property(e => e.ReviewerRole).HasMaxLength(50);
            entity.Property(e => e.ReviewerComments).HasMaxLength(2000);
            entity.Property(e => e.ISSMRecommendation).HasMaxLength(20);
            entity.Property(e => e.ISSMRecommendedBy).HasMaxLength(200);
            entity.Property(e => e.RevokedBy).HasMaxLength(200);
            entity.Property(e => e.RevocationReason).HasMaxLength(1000);

            // FK to RegisteredSystem
            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            // FK to ComplianceFinding (optional)
            entity.HasOne(e => e.Finding)
                .WithMany()
                .HasForeignKey(e => e.FindingId)
                .OnDelete(DeleteBehavior.Restrict);

            // FK to PoamItem (optional)
            entity.HasOne(e => e.PoamEntry)
                .WithMany()
                .HasForeignKey(e => e.PoamEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            // FK to AuthorizationDecision (optional)
            entity.HasOne(e => e.AuthorizationDecision)
                .WithMany()
                .HasForeignKey(e => e.AuthorizationDecisionId)
                .OnDelete(DeleteBehavior.Restrict);

            // FK to AuthorizationBoundaryDefinition (optional, waivers only)
            entity.HasOne(e => e.BoundaryDefinition)
                .WithMany()
                .HasForeignKey(e => e.BoundaryDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_Deviations_RegisteredSystemId");
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_Deviations_Status");
            entity.HasIndex(e => e.FindingId).HasDatabaseName("IX_Deviations_FindingId");
            entity.HasIndex(e => e.ExpirationDate).HasDatabaseName("IX_Deviations_ExpirationDate");
        });

        // ─── PoamMilestone ───────────────────────────────────────────────────────
        modelBuilder.Entity<PoamMilestone>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.PoamItemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000).IsRequired();

            // Indexes
            entity.HasIndex(e => e.PoamItemId).HasDatabaseName("IX_PoamMilestone_PoamItemId");
        });

        // ─── ConMonPlan (Feature 015 — US9) ──────────────────────────────────
        modelBuilder.Entity<ConMonPlan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RegisteredSystemId).IsUnique().HasDatabaseName("IX_ConMonPlan_RegisteredSystemId");
            entity.HasOne(e => e.RegisteredSystem).WithMany().HasForeignKey(e => e.RegisteredSystemId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.ReportDistribution).HasConversion(stringListConverter);
            entity.Property(e => e.SignificantChangeTriggers).HasConversion(stringListConverter);
        });

        // ─── ConMonReport (Feature 015 — US9) ───────────────────────────────
        modelBuilder.Entity<ConMonReport>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ConMonPlanId).HasDatabaseName("IX_ConMonReport_ConMonPlanId");
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_ConMonReport_RegisteredSystemId");
            entity.HasOne(e => e.ConMonPlan).WithMany(p => p.Reports).HasForeignKey(e => e.ConMonPlanId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.RegisteredSystem).WithMany().HasForeignKey(e => e.RegisteredSystemId).OnDelete(DeleteBehavior.NoAction);
        });

        // ─── SignificantChange (Feature 015 — US9) ──────────────────────────
        modelBuilder.Entity<SignificantChange>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_SignificantChange_RegisteredSystemId");
            entity.HasOne(e => e.RegisteredSystem).WithMany().HasForeignKey(e => e.RegisteredSystemId).OnDelete(DeleteBehavior.Cascade);
        });

        // ─── RemediationTask: Add optional PoamItemId FK (US8 — T111) ─────────
        modelBuilder.Entity<RemediationTask>(entity =>
        {
            entity.HasIndex(e => e.PoamItemId).HasDatabaseName("IX_RemediationTask_PoamItemId");
        });

        // ─── ScanImportRecord (Feature 017 — SCAP/STIG Import) ──────────────
        modelBuilder.Entity<ScanImportRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(100);

            // Required strings
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36);
            entity.Property(e => e.AssessmentId).HasMaxLength(100);
            entity.Property(e => e.FileName).HasMaxLength(500);
            entity.Property(e => e.FileHash).HasMaxLength(128);
            entity.Property(e => e.ImportedBy).HasMaxLength(200);

            // Optional strings
            entity.Property(e => e.BenchmarkId).HasMaxLength(200);
            entity.Property(e => e.BenchmarkVersion).HasMaxLength(50);
            entity.Property(e => e.BenchmarkTitle).HasMaxLength(500);
            entity.Property(e => e.TargetHostName).HasMaxLength(200);
            entity.Property(e => e.TargetIpAddress).HasMaxLength(50);
            entity.Property(e => e.ErrorMessage).HasMaxLength(4000);

            // Enum → string conversion
            entity.Property(e => e.ImportType).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.ImportStatus).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.ConflictResolution).HasConversion<string>().HasMaxLength(20);

            // JSON column — Warnings
            entity.Property(e => e.Warnings).HasConversion(stringListConverter);

            // Relationships
            entity.HasOne<RegisteredSystem>()
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ComplianceAssessment>()
                .WithMany()
                .HasForeignKey(e => e.AssessmentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_ScanImportRecord_RegisteredSystemId");
            entity.HasIndex(e => new { e.RegisteredSystemId, e.BenchmarkId }).HasDatabaseName("IX_ScanImportRecord_System_Benchmark");
            entity.HasIndex(e => new { e.RegisteredSystemId, e.FileHash }).HasDatabaseName("IX_ScanImportRecord_System_FileHash");
        });

        // ─── ScanImportFinding (Feature 017 — SCAP/STIG Import) ─────────────
        modelBuilder.Entity<ScanImportFinding>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(100);

            // Required strings
            entity.Property(e => e.ScanImportRecordId).HasMaxLength(100);
            entity.Property(e => e.VulnId).HasMaxLength(20);
            entity.Property(e => e.RawStatus).HasMaxLength(50);
            entity.Property(e => e.RawSeverity).HasMaxLength(20);

            // Optional strings
            entity.Property(e => e.RuleId).HasMaxLength(100);
            entity.Property(e => e.StigVersion).HasMaxLength(50);
            entity.Property(e => e.FindingDetails).HasMaxLength(8000);
            entity.Property(e => e.Comments).HasMaxLength(8000);
            entity.Property(e => e.SeverityOverride).HasMaxLength(20);
            entity.Property(e => e.SeverityJustification).HasMaxLength(4000);
            entity.Property(e => e.ResolvedStigControlId).HasMaxLength(20);
            entity.Property(e => e.ComplianceFindingId).HasMaxLength(100);

            // Enum → string conversion
            entity.Property(e => e.MappedSeverity).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.ImportAction).HasConversion<string>().HasMaxLength(20);

            // JSON columns
            entity.Property(e => e.ResolvedNistControlIds).HasConversion(stringListConverter);
            entity.Property(e => e.ResolvedCciRefs).HasConversion(stringListConverter);

            // Relationships
            entity.HasOne<ScanImportRecord>()
                .WithMany()
                .HasForeignKey(e => e.ScanImportRecordId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ComplianceFinding>()
                .WithMany()
                .HasForeignKey(e => e.ComplianceFindingId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            entity.HasIndex(e => e.ScanImportRecordId).HasDatabaseName("IX_ScanImportFinding_ImportRecordId");
            entity.HasIndex(e => new { e.ScanImportRecordId, e.VulnId }).HasDatabaseName("IX_ScanImportFinding_Import_VulnId");
        });

        // ─── SecurityAssessmentPlan (Feature 018) ────────────────────────────────
        modelBuilder.Entity<SecurityAssessmentPlan>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.AssessmentId).HasMaxLength(100);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.BaselineLevel).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ScopeNotes).HasMaxLength(4000);
            entity.Property(e => e.RulesOfEngagement).HasMaxLength(4000);
            entity.Property(e => e.ContentHash).HasMaxLength(64);
            entity.Property(e => e.GeneratedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.FinalizedBy).HasMaxLength(200);
            entity.Property(e => e.Format).HasMaxLength(20).HasDefaultValue("markdown");

            // Relationships
            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ComplianceAssessment)
                .WithMany()
                .HasForeignKey(e => e.AssessmentId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            entity.HasIndex(e => new { e.RegisteredSystemId, e.Status })
                .HasDatabaseName("IX_SecurityAssessmentPlan_System_Status");
        });

        // ─── SapControlEntry (Feature 018) ───────────────────────────────────────
        modelBuilder.Entity<SapControlEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.SecurityAssessmentPlanId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.ControlId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ControlTitle).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ControlFamily).HasMaxLength(100).IsRequired();
            entity.Property(e => e.InheritanceType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Provider).HasMaxLength(200);
            entity.Property(e => e.OverrideRationale).HasMaxLength(2000);

            // JSON column conversions for list properties
            entity.Property(e => e.AssessmentMethods).HasConversion(stringListConverter);
            entity.Property(e => e.AssessmentObjectives).HasConversion(stringListConverter);
            entity.Property(e => e.EvidenceRequirements).HasConversion(stringListConverter);
            entity.Property(e => e.StigBenchmarks).HasConversion(stringListConverter);

            // Relationships
            entity.HasOne(e => e.SecurityAssessmentPlan)
                .WithMany(s => s.ControlEntries)
                .HasForeignKey(e => e.SecurityAssessmentPlanId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            entity.HasIndex(e => new { e.SecurityAssessmentPlanId, e.ControlId })
                .IsUnique()
                .HasDatabaseName("IX_SapControlEntry_Plan_Control");
        });

        // ─── SapTeamMember (Feature 018) ─────────────────────────────────────────
        modelBuilder.Entity<SapTeamMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.SecurityAssessmentPlanId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Organization).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ContactInfo).HasMaxLength(500);

            // Relationships
            entity.HasOne(e => e.SecurityAssessmentPlan)
                .WithMany(s => s.TeamMembers)
                .HasForeignKey(e => e.SecurityAssessmentPlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── PrivacyThresholdAnalysis (Feature 021) ──────────────────────────────
        modelBuilder.Entity<PrivacyThresholdAnalysis>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Determination).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.AffectedIndividuals).HasMaxLength(200);
            entity.Property(e => e.ExemptionRationale).HasMaxLength(2000);
            entity.Property(e => e.Rationale).HasMaxLength(4000);
            entity.Property(e => e.AnalyzedBy).HasMaxLength(200).IsRequired();

            entity.Property(e => e.PiiCategories).HasConversion(stringListConverter);
            entity.Property(e => e.PiiSourceInfoTypes).HasConversion(stringListConverter);

            entity.HasIndex(e => e.RegisteredSystemId)
                .IsUnique()
                .HasDatabaseName("IX_PTA_SystemId");
        });

        // ─── PrivacyImpactAssessment (Feature 021) ──────────────────────────────
        modelBuilder.Entity<PrivacyImpactAssessment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.PtaId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.SystemDescription).HasMaxLength(4000);
            entity.Property(e => e.PurposeOfCollection).HasMaxLength(4000);
            entity.Property(e => e.IntendedUse).HasMaxLength(4000);
            entity.Property(e => e.NoticeAndConsent).HasMaxLength(4000);
            entity.Property(e => e.IndividualAccess).HasMaxLength(4000);
            entity.Property(e => e.Safeguards).HasMaxLength(4000);
            entity.Property(e => e.RetentionPeriod).HasMaxLength(500);
            entity.Property(e => e.DisposalMethod).HasMaxLength(500);
            entity.Property(e => e.SornReference).HasMaxLength(200);
            entity.Property(e => e.NarrativeDocument).HasMaxLength(16000);
            entity.Property(e => e.ReviewerComments).HasMaxLength(4000);
            entity.Property(e => e.ApprovedBy).HasMaxLength(200);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            entity.Property(e => e.SharingPartners).HasConversion(stringListConverter);
            entity.Property(e => e.ReviewDeficiencies).HasConversion(stringListConverter);

            var piaSectionsConverter = new ValueConverter<List<PiaSection>, string>(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<PiaSection>>(v, (JsonSerializerOptions?)null) ?? new List<PiaSection>()
            );
            entity.Property(e => e.Sections).HasConversion(piaSectionsConverter);

            // PTA → PIA relationship (NoAction to avoid cascade cycles)
            entity.HasOne<PrivacyThresholdAnalysis>()
                .WithOne()
                .HasForeignKey<PrivacyImpactAssessment>(e => e.PtaId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(e => e.RegisteredSystemId)
                .IsUnique()
                .HasDatabaseName("IX_PIA_SystemId");
            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_PIA_Status");
        });

        // ─── SystemInterconnection (Feature 021) ─────────────────────────────────
        modelBuilder.Entity<SystemInterconnection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.TargetSystemName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.TargetSystemOwner).HasMaxLength(200);
            entity.Property(e => e.TargetSystemAcronym).HasMaxLength(20);
            entity.Property(e => e.InterconnectionType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.DataFlowDirection).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.DataClassification).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DataDescription).HasMaxLength(2000);
            entity.Property(e => e.AuthenticationMethod).HasMaxLength(200);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.StatusReason).HasMaxLength(1000);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            entity.Property(e => e.ProtocolsUsed).HasConversion(stringListConverter);
            entity.Property(e => e.PortsUsed).HasConversion(stringListConverter);
            entity.Property(e => e.SecurityMeasures).HasConversion(stringListConverter);

            entity.HasIndex(e => new { e.RegisteredSystemId, e.Status })
                .HasDatabaseName("IX_Interconnection_System_Status");
            entity.HasIndex(e => e.TargetSystemName)
                .HasDatabaseName("IX_Interconnection_TargetSystem");
        });

        // ─── InterconnectionAgreement (Feature 021) ──────────────────────────────
        modelBuilder.Entity<InterconnectionAgreement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.SystemInterconnectionId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.AgreementType).HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.DocumentReference).HasMaxLength(1000);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.SignedByLocal).HasMaxLength(200);
            entity.Property(e => e.SignedByRemote).HasMaxLength(200);
            entity.Property(e => e.ReviewNotes).HasMaxLength(4000);
            entity.Property(e => e.NarrativeDocument).HasMaxLength(16000);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            // Relationship to parent interconnection
            entity.HasOne<SystemInterconnection>()
                .WithMany(i => i.Agreements)
                .HasForeignKey(e => e.SystemInterconnectionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_Agreement_Status");
            entity.HasIndex(e => e.ExpirationDate)
                .HasDatabaseName("IX_Agreement_Expiration");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // Narrative Governance Entities (Feature 024)
        // ═══════════════════════════════════════════════════════════════════════════

        // ─── NarrativeVersion ────────────────────────────────────────────────────
        modelBuilder.Entity<NarrativeVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.ControlImplementationId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Content).HasMaxLength(8000).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.AuthoredBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ChangeReason).HasMaxLength(1000);
            entity.Property(e => e.SubmittedBy).HasMaxLength(200);

            // Composite index for history queries (newest-first)
            entity.HasIndex(e => new { e.ControlImplementationId, e.VersionNumber })
                .IsUnique()
                .HasDatabaseName("IX_NarrativeVersion_Impl_Version");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_NarrativeVersion_Status");

            // FK to ControlImplementation
            entity.HasOne(e => e.ControlImplementation)
                .WithMany(e => e.Versions)
                .HasForeignKey(e => e.ControlImplementationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── NarrativeReview ─────────────────────────────────────────────────────
        modelBuilder.Entity<NarrativeReview>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.NarrativeVersionId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.ReviewedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Decision).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ReviewerComments).HasMaxLength(2000);

            entity.HasIndex(e => e.NarrativeVersionId)
                .HasDatabaseName("IX_NarrativeReview_VersionId");

            // FK to NarrativeVersion
            entity.HasOne(e => e.NarrativeVersion)
                .WithMany(e => e.Reviews)
                .HasForeignKey(e => e.NarrativeVersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── Inventory Item (Feature 025) ────────────────────────────────────────
        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.ItemName).HasMaxLength(300).IsRequired();
            entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.HardwareFunction).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.SoftwareFunction).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Manufacturer).HasMaxLength(300);
            entity.Property(e => e.Model).HasMaxLength(300);
            entity.Property(e => e.SerialNumber).HasMaxLength(200);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.MacAddress).HasMaxLength(17);
            entity.Property(e => e.Location).HasMaxLength(500);
            entity.Property(e => e.Vendor).HasMaxLength(300);
            entity.Property(e => e.Version).HasMaxLength(100);
            entity.Property(e => e.PatchLevel).HasMaxLength(200);
            entity.Property(e => e.LicenseType).HasMaxLength(200);
            entity.Property(e => e.ParentHardwareId).HasMaxLength(36);
            entity.Property(e => e.BoundaryResourceId).HasMaxLength(36);
            entity.Property(e => e.DecommissionRationale).HasMaxLength(2000);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ModifiedBy).HasMaxLength(200);

            // FK → RegisteredSystem (required)
            // Use Restrict to avoid SQL Server error 1785 (multiple cascade paths
            // through RegisteredSystem → AuthorizationBoundaries → InventoryItems).
            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Restrict);

            // Self-referencing FK: SW → parent HW (optional)
            entity.HasOne(e => e.ParentHardware)
                .WithMany(e => e.InstalledSoftware)
                .HasForeignKey(e => e.ParentHardwareId)
                .OnDelete(DeleteBehavior.Restrict);

            // FK → AuthorizationBoundary (optional, for auto-seed idempotency)
            entity.HasOne(e => e.BoundaryResource)
                .WithMany()
                .HasForeignKey(e => e.BoundaryResourceId)
                .OnDelete(DeleteBehavior.SetNull);

            // Indexes
            entity.HasIndex(e => e.RegisteredSystemId)
                .HasDatabaseName("IX_InventoryItem_SystemId");
            entity.HasIndex(e => new { e.RegisteredSystemId, e.Type })
                .HasDatabaseName("IX_InventoryItem_System_Type");
            entity.HasIndex(e => new { e.RegisteredSystemId, e.IpAddress })
                .IsUnique()
                .HasFilter("[IpAddress] IS NOT NULL")
                .HasDatabaseName("IX_InventoryItem_System_Ip");
            entity.HasIndex(e => e.BoundaryResourceId)
                .HasDatabaseName("IX_InventoryItem_BoundaryResourceId");
        });

        // ─── CachedResponse (Feature 029 — FR-036) ──────────────────────────
        modelBuilder.Entity<CachedResponse>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CacheKey).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ToolName).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Response).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(20);
            entity.Property(e => e.SubscriptionId).HasMaxLength(100).IsRequired();

            entity.HasIndex(e => e.CacheKey)
                .IsUnique()
                .HasDatabaseName("IX_CachedResponse_CacheKey");
            entity.HasIndex(e => new { e.ToolName, e.SubscriptionId })
                .HasDatabaseName("IX_CachedResponse_Tool_Sub");
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // Dashboard Entities (Feature 030)
        // ═══════════════════════════════════════════════════════════════════════════

        // ─── SecurityCapability ──────────────────────────────────────────────────
        modelBuilder.Entity<SecurityCapability>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(5).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(8000).IsRequired();
            entity.Property(e => e.ImplementationStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Owner).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ModifiedBy).HasMaxLength(200);

            entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("IX_SecurityCapability_Name");
            entity.HasIndex(e => e.Category).HasDatabaseName("IX_SecurityCapability_Category");
            entity.HasIndex(e => e.ImplementationStatus).HasDatabaseName("IX_SecurityCapability_Status");
        });

        // ─── CapabilityControlMapping ────────────────────────────────────────────
        modelBuilder.Entity<CapabilityControlMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.SecurityCapabilityId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.ControlId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36);
            entity.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => new { e.SecurityCapabilityId, e.ControlId, e.RegisteredSystemId })
                .IsUnique()
                .HasDatabaseName("IX_CapabilityControlMapping_Unique");
            entity.HasIndex(e => e.ControlId).HasDatabaseName("IX_CapabilityControlMapping_ControlId");
            entity.HasIndex(e => e.RegisteredSystemId).HasDatabaseName("IX_CapabilityControlMapping_SystemId");

            entity.HasOne(e => e.SecurityCapability)
                .WithMany(c => c.ControlMappings)
                .HasForeignKey(e => e.SecurityCapabilityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── SystemComponent ─────────────────────────────────────────────────────
        modelBuilder.Entity<SystemComponent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            // RegisteredSystemId is now nullable for org-wide components
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ComponentType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.SubType).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.Owner).HasMaxLength(200);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => new { e.RegisteredSystemId, e.ComponentType })
                .HasDatabaseName("IX_SystemComponent_System_Type");
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_SystemComponent_Status");

            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ─── ComponentSystemAssignment (Feature 036) ─────────────────────────────
        modelBuilder.Entity<ComponentSystemAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.SystemComponentId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.AuthorizationBoundaryDefinitionId).HasMaxLength(36);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => new { e.SystemComponentId, e.RegisteredSystemId, e.AuthorizationBoundaryDefinitionId })
                .IsUnique()
                .HasDatabaseName("IX_ComponentSystemAssignment_Unique");

            entity.HasIndex(e => e.RegisteredSystemId)
                .HasDatabaseName("IX_ComponentSystemAssignment_System");

            entity.HasOne(e => e.SystemComponent)
                .WithMany(c => c.SystemAssignments)
                .HasForeignKey(e => e.SystemComponentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.AuthorizationBoundaryDefinition)
                .WithMany()
                .HasForeignKey(e => e.AuthorizationBoundaryDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ─── ComponentCapabilityLink ─────────────────────────────────────────────
        modelBuilder.Entity<ComponentCapabilityLink>(entity =>
        {
            entity.HasKey(e => new { e.SystemComponentId, e.SecurityCapabilityId });
            entity.Property(e => e.SystemComponentId).HasMaxLength(36);
            entity.Property(e => e.SecurityCapabilityId).HasMaxLength(36);

            entity.HasOne(e => e.SystemComponent)
                .WithMany(c => c.CapabilityLinks)
                .HasForeignKey(e => e.SystemComponentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SecurityCapability)
                .WithMany(c => c.ComponentLinks)
                .HasForeignKey(e => e.SecurityCapabilityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── ComplianceTrendSnapshot ─────────────────────────────────────────────
        modelBuilder.Entity<ComplianceTrendSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Source).HasMaxLength(50).IsRequired();

            entity.HasIndex(e => new { e.RegisteredSystemId, e.CapturedAt })
                .IsDescending(false, true)
                .HasDatabaseName("IX_ComplianceTrendSnapshot_System_CapturedAt");

            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── DashboardActivity ───────────────────────────────────────────────────
        modelBuilder.Entity<DashboardActivity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Actor).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Summary).HasMaxLength(500).IsRequired();
            entity.Property(e => e.RelatedEntityType).HasMaxLength(100);
            entity.Property(e => e.RelatedEntityId).HasMaxLength(100);

            entity.HasIndex(e => new { e.RegisteredSystemId, e.Timestamp })
                .IsDescending(false, true)
                .HasDatabaseName("IX_DashboardActivity_System_Timestamp");

            entity.HasOne(e => e.RegisteredSystem)
                .WithMany()
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── ImplementationRoadmap (Feature 031) ────────────────────────────────
        modelBuilder.Entity<ImplementationRoadmap>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.SystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(500).IsRequired();
            entity.Property(e => e.BaselineLevel).HasMaxLength(20).IsRequired();
            entity.Property(e => e.LinkedBoardId).HasMaxLength(36);
            entity.Property(e => e.GenerationMethod).HasMaxLength(20).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();
            entity.Property(e => e.RowVersion).IsConcurrencyToken();

            entity.HasMany(e => e.Phases)
                .WithOne(p => p.Roadmap)
                .HasForeignKey(p => p.RoadmapId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.SystemId);
            entity.HasIndex(e => new { e.SystemId, e.Status })
                .HasDatabaseName("IX_ImplementationRoadmaps_SystemId_Status");
        });

        // ─── RoadmapPhase ────────────────────────────────────────────────────────
        modelBuilder.Entity<RoadmapPhase>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RoadmapId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(500).IsRequired();
            entity.Property(e => e.RowVersion).IsConcurrencyToken();

            entity.HasMany(e => e.Items)
                .WithOne(i => i.Phase)
                .HasForeignKey(i => i.PhaseId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.RoadmapId, e.DisplayOrder })
                .HasDatabaseName("IX_RoadmapPhases_RoadmapId_DisplayOrder");
        });

        // ─── RoadmapItem ─────────────────────────────────────────────────────────
        modelBuilder.Entity<RoadmapItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.PhaseId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.RoadmapId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.ControlId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ControlTitle).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ControlFamily).HasMaxLength(5).IsRequired();
            entity.Property(e => e.EstimationSource).HasMaxLength(20).IsRequired();
            entity.Property(e => e.AssignedRole).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DependsOn).HasMaxLength(500);
            entity.Property(e => e.LinkedTaskId).HasMaxLength(36);
            entity.Property(e => e.RowVersion).IsConcurrencyToken();

            entity.HasOne(e => e.Roadmap)
                .WithMany()
                .HasForeignKey(e => e.RoadmapId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(e => e.PhaseId);
            entity.HasIndex(e => e.RoadmapId);
            entity.HasIndex(e => e.ControlId);
        });

        // ═══════════════════════════════════════════════════════════════════════════
        // Boundary-Scoped Model (Feature 033)
        // ═══════════════════════════════════════════════════════════════════════════

        // ─── AuthorizationBoundaryDefinition ───────────────────────────────────
        modelBuilder.Entity<AuthorizationBoundaryDefinition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36);
            entity.Property(e => e.RegisteredSystemId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.BoundaryType).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.CreatedBy).HasMaxLength(200).IsRequired();

            entity.HasOne(e => e.RegisteredSystem)
                .WithMany(s => s.AuthorizationBoundaryDefinitions)
                .HasForeignKey(e => e.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.RegisteredSystemId, e.Name })
                .IsUnique()
                .HasDatabaseName("IX_BoundaryDefinition_System_Name");

            entity.HasIndex(e => new { e.RegisteredSystemId, e.IsPrimary })
                .HasDatabaseName("IX_BoundaryDefinition_System_Primary");
        });

        // ─── AuthorizationBoundary → BoundaryDefinition (nullable FK) ────────
        modelBuilder.Entity<AuthorizationBoundary>(entity =>
        {
            entity.HasOne(e => e.AuthorizationBoundaryDefinition)
                .WithMany(d => d.AuthorizationBoundaries)
                .HasForeignKey(e => e.AuthorizationBoundaryDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.AuthorizationBoundaryDefinitionId).HasMaxLength(36);

            entity.HasIndex(e => e.AuthorizationBoundaryDefinitionId)
                .HasDatabaseName("IX_AuthorizationBoundary_BoundaryDefinitionId");
        });

        // ─── SystemComponent → BoundaryDefinition (nullable FK) ─────────────
        modelBuilder.Entity<SystemComponent>(entity =>
        {
            entity.HasOne(e => e.AuthorizationBoundaryDefinition)
                .WithMany(d => d.SystemComponents)
                .HasForeignKey(e => e.AuthorizationBoundaryDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.AuthorizationBoundaryDefinitionId).HasMaxLength(36);

            entity.HasIndex(e => new { e.RegisteredSystemId, e.AuthorizationBoundaryDefinitionId, e.ComponentType })
                .HasDatabaseName("IX_SystemComponent_System_Boundary_Type");
        });

        // ─── CapabilityControlMapping → BoundaryDefinition (nullable FK) ────
        modelBuilder.Entity<CapabilityControlMapping>(entity =>
        {
            entity.HasOne(e => e.AuthorizationBoundaryDefinition)
                .WithMany(d => d.CapabilityControlMappings)
                .HasForeignKey(e => e.AuthorizationBoundaryDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.AuthorizationBoundaryDefinitionId).HasMaxLength(36);

            entity.HasIndex(e => new { e.RegisteredSystemId, e.AuthorizationBoundaryDefinitionId, e.ControlId })
                .HasDatabaseName("IX_CapabilityMapping_System_Boundary_Control");
        });

        // ─── DeferredPrerequisite ─────────────────────────────────────────────
        modelBuilder.Entity<DeferredPrerequisite>(entity =>
        {
            entity.HasOne(d => d.RegisteredSystem)
                .WithMany()
                .HasForeignKey(d => d.RegisteredSystemId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(d => new { d.RegisteredSystemId, d.IsResolved })
                .HasDatabaseName("IX_DeferredPrerequisite_System_Resolved");
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// Auto-regenerates RowVersion for all modified ConcurrentEntity entries
    /// to support optimistic concurrency with Guid-based tokens (per research R-001).
    /// </remarks>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ConcurrentEntity>())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Added)
            {
                entry.Entity.RowVersion = Guid.NewGuid();
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
