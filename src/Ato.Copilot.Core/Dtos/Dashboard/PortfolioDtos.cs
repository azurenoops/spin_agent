namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>
/// Summary of a registered system for portfolio-level dashboard display.
/// </summary>
public class PortfolioSystemSummaryDto
{
    /// <summary>System identifier.</summary>
    public required string SystemId { get; init; }

    /// <summary>System name.</summary>
    public required string Name { get; init; }

    /// <summary>System acronym.</summary>
    public string? Acronym { get; init; }

    /// <summary>System type.</summary>
    public required string SystemType { get; init; }

    /// <summary>Mission criticality.</summary>
    public required string MissionCriticality { get; init; }

    /// <summary>Hosting environment.</summary>
    public required string HostingEnvironment { get; init; }

    /// <summary>System description.</summary>
    public string? Description { get; init; }

    /// <summary>FIPS 199 impact level (Low, Moderate, High).</summary>
    public required string ImpactLevel { get; init; }

    /// <summary>Current RMF lifecycle phase.</summary>
    public required string CurrentRmfPhase { get; init; }

    /// <summary>Overall compliance score (0-100).</summary>
    public double ComplianceScore { get; init; }

    /// <summary>Change in score since prior assessment (negative = decline).</summary>
    public double ComplianceScoreDelta { get; init; }

    /// <summary>ATO expiration date (null if no ATO).</summary>
    public DateTime? AtoExpirationDate { get; init; }

    /// <summary>ATO status (Active, Expired, None).</summary>
    public required string AtoStatus { get; init; }

    /// <summary>Days remaining until ATO expires (null if no ATO).</summary>
    public int? AtoDaysRemaining { get; init; }

    /// <summary>ATO severity indicator: green (&gt;90d), yellow (30-90d), red (&lt;30d), expired, none.</summary>
    public required string AtoSeverity { get; init; }

    /// <summary>Total open POA&amp;M items.</summary>
    public int OpenPoamCount { get; init; }

    /// <summary>POA&amp;M items past scheduled completion date.</summary>
    public int OverduePoamCount { get; init; }

    /// <summary>Open CAT I findings count.</summary>
    public int CatICounts { get; init; }

    /// <summary>Open CAT II findings count.</summary>
    public int CatIICounts { get; init; }

    /// <summary>Open CAT III findings count.</summary>
    public int CatIIICounts { get; init; }

    /// <summary>Whether the system has at least one authorization boundary definition.</summary>
    public bool HasBoundary { get; init; }

    /// <summary>Whether the system has at least one active RMF role assignment.</summary>
    public bool HasRoles { get; init; }

    /// <summary>Whether the system has a security categorization record.</summary>
    public bool HasCategorization { get; init; }

    /// <summary>Composite: HasBoundary AND HasRoles AND HasCategorization.</summary>
    public bool IsSetupComplete { get; init; }
}

/// <summary>
/// Query parameters for the portfolio endpoint.
/// </summary>
public class PortfolioQuery : PaginationQuery
{
    /// <summary>Sort column: name, impactLevel, rmfPhase, complianceScore, atoExpiration, openPoamCount.</summary>
    public string? SortBy { get; init; }

    /// <summary>Sort direction: asc or desc.</summary>
    public string? SortDir { get; init; }

    /// <summary>Optional filter by impact level (Low, Moderate, High).</summary>
    public string? ImpactLevel { get; init; }

    /// <summary>Optional filter by RMF phase.</summary>
    public string? RmfPhase { get; init; }
}

/// <summary>
/// Request body for registering a new system via the dashboard.
/// </summary>
public class RegisterSystemRequest
{
    public required string Name { get; init; }
    public required string SystemType { get; init; }
    public required string MissionCriticality { get; init; }
    public string? HostingEnvironment { get; init; }
    public string? Acronym { get; init; }
    public string? Description { get; init; }
    public string? CloudEnvironment { get; init; }
    public List<string>? SubscriptionIds { get; init; }
}

/// <summary>
/// Request body for updating a system via the dashboard.
/// </summary>
public class UpdateSystemRequest
{
    public string? Name { get; init; }
    public string? Acronym { get; init; }
    public string? SystemType { get; init; }
    public string? MissionCriticality { get; init; }
    public string? HostingEnvironment { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Request body for assigning an RMF role via the dashboard.
/// </summary>
public class AssignRoleRequest
{
    public required string Role { get; init; }
    public required string UserDisplayName { get; init; }
    public string? UserId { get; init; }
}

public class GenerateComponentDescriptionRequest
{
    public required string Name { get; init; }
    public required string ComponentType { get; init; }
    public string? SubType { get; init; }
}

public class GenerateCapabilityDescriptionRequest
{
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public string? Category { get; init; }
}

public class GenerateSystemDescriptionRequest
{
    public required string Name { get; init; }
    public required string SystemType { get; init; }
    public required string MissionCriticality { get; init; }
    public required string HostingEnvironment { get; init; }
}

/// <summary>
/// Request body for creating a Privacy Threshold Analysis via the dashboard.
/// </summary>
public class CreatePtaRequest
{
    public bool CollectsPii { get; init; }
    public bool MaintainsPii { get; init; }
    public bool DisseminatesPii { get; init; }
    public List<string>? PiiCategories { get; init; }
    public int? EstimatedRecordCount { get; init; }
    public string? Purpose { get; init; }
}

/// <summary>
/// Request body for adding an interconnection via the dashboard.
/// </summary>
public class AddInterconnectionRequest
{
    public required string RemoteSystem { get; init; }
    public string? Hostname { get; init; }
    public required string Direction { get; init; }
    public string? Type { get; init; }
    public string? Protocol { get; init; }
    public string? Port { get; init; }
    public string? DataClassification { get; init; }
}

/// <summary>
/// Request body for advancing a system's RMF step via the dashboard.
/// </summary>
public class AdvanceRmfStepRequest
{
    public required string TargetStep { get; init; }
    public bool? Force { get; init; }
}

/// <summary>
/// Request body for setting FIPS 199 security categorization via the dashboard.
/// </summary>
public class SetCategorizationRequest
{
    public bool IsNationalSecuritySystem { get; init; }
    public string? Justification { get; init; }
    public required List<SetCategorizationInfoTypeInput> InformationTypes { get; init; }
}

public class SetCategorizationInfoTypeInput
{
    public string Sp80060Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string ConfidentialityImpact { get; init; } = "Low";
    public string IntegrityImpact { get; init; } = "Low";
    public string AvailabilityImpact { get; init; } = "Low";
    public bool UsesProvisional { get; init; } = true;
    public string? AdjustmentJustification { get; init; }
}

/// <summary>
/// Request body for selecting a NIST 800-53 control baseline via the dashboard.
/// </summary>
public class SelectBaselineRequest
{
    public bool ApplyOverlay { get; init; } = true;
    public string? OverlayName { get; init; }
}

// ─── Document Catalog DTOs ─────────────────────────────────────────────────

/// <summary>Aggregated document catalog for a system.</summary>
public class SystemDocumentsResponse
{
    public required string SystemId { get; init; }
    public required string SystemName { get; init; }
    public required string CurrentPhase { get; init; }

    // Authorization Package
    public required SspDocumentInfo Ssp { get; init; }
    public SapDocumentInfo? Sap { get; init; }
    public SarDocumentInfo? Sar { get; init; }
    public AuthDecisionInfo? Authorization { get; init; }
    public int PoamCount { get; init; }
    public int PoamOverdueCount { get; init; }
    public bool HasBaseline { get; init; }
    public int BaselineControlCount { get; init; }

    // Privacy
    public PtaDocumentInfo? Pta { get; init; }
    public PiaDocumentInfo? Pia { get; init; }

    // Interconnections
    public required List<InterconnectionDocInfo> Interconnections { get; init; }

    // ConMon
    public ConMonInfo? ConMon { get; init; }

    // SSP Sections
    public required List<SspSectionInfo> SspSections { get; init; }

    // Active waivers (Feature 035 — Deviation Management)
    public int ActiveWaiverCount { get; init; }

    // Narrative Governance
    public NarrativeGovernanceInfo? NarrativeGovernance { get; init; }

    // Scan & Import History
    public required List<ScanImportInfo> ImportHistory { get; init; }

    // Inventory
    public int InventoryItemCount { get; init; }
}

public class SspDocumentInfo
{
    public double NarrativeCompletionPct { get; init; }
    public int TotalNarratives { get; init; }
    public int CompletedNarratives { get; init; }
}

public class SapDocumentInfo
{
    public required string SapId { get; init; }
    public required string Status { get; init; }
    public required string Title { get; init; }
    public string? ContentHash { get; init; }
    public int TotalControls { get; init; }
    public DateTime? FinalizedAt { get; init; }
    public DateTime? ScheduleStart { get; init; }
    public DateTime? ScheduleEnd { get; init; }
}

public class SarDocumentInfo
{
    public required string SarId { get; init; }
    public required string Status { get; init; }
    public required string Title { get; init; }
    public int TotalControlsAssessed { get; init; }
    public int SatisfiedCount { get; init; }
    public int NotSatisfiedCount { get; init; }
    public required string CreatedBy { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
}

public class AuthDecisionInfo
{
    public required string DecisionId { get; init; }
    public required string DecisionType { get; init; }
    public DateTime DecisionDate { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public required string ResidualRisk { get; init; }
    public required string IssuedBy { get; init; }
    public int? DaysUntilExpiration { get; init; }
}

public class PtaDocumentInfo
{
    public required string PtaId { get; init; }
    public required string Determination { get; init; }
    public bool CollectsPii { get; init; }
    public required List<string> PiiCategories { get; init; }
    public DateTime AnalyzedAt { get; init; }
    public required string AnalyzedBy { get; init; }
}

public class PiaDocumentInfo
{
    public required string PiaId { get; init; }
    public required string Status { get; init; }
    public int Version { get; init; }
    public string? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public int? DaysUntilExpiration { get; init; }
}

public class InterconnectionDocInfo
{
    public required string InterconnectionId { get; init; }
    public required string TargetSystem { get; init; }
    public required string Direction { get; init; }
    public required string Status { get; init; }
    public bool HasAgreement { get; init; }
    public string? AgreementType { get; init; }
    public string? AgreementStatus { get; init; }
}

public class ConMonInfo
{
    public required string PlanId { get; init; }
    public required string Frequency { get; init; }
    public int ReportCount { get; init; }
    public DateTime? LastReportDate { get; init; }
}

public class ConMonOverviewResponse
{
    public required string SystemId { get; init; }
    public required string SystemName { get; init; }
    public required string CurrentPhase { get; init; }
    public ConMonPlanDetailInfo? Plan { get; init; }
    public required ConMonStatusInfo Status { get; init; }
    public required ConMonExpirationInfo Expiration { get; init; }
    public required ConMonReauthorizationInfo Reauthorization { get; init; }
    public required List<AgreementExpirationInfo> AgreementAlerts { get; init; }
    public required List<SignificantChangeItemInfo> SignificantChanges { get; init; }
    public required List<ConMonReportSummaryInfo> Reports { get; init; }
}

public class ConMonPlanDetailInfo
{
    public required string PlanId { get; init; }
    public required string AssessmentFrequency { get; init; }
    public DateTime AnnualReviewDate { get; init; }
    public required List<string> ReportDistribution { get; init; }
    public required List<string> SignificantChangeTriggers { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ModifiedAt { get; init; }
}

public class ConMonStatusInfo
{
    public double CurrentComplianceScore { get; init; }
    public double? AuthorizedBaselineScore { get; init; }
    public double? ScoreDelta { get; init; }
    public int OpenFindings { get; init; }
    public int ResolvedFindings { get; init; }
    public int OpenPoamItems { get; init; }
    public int OverduePoamItems { get; init; }
    public bool MonitoringEnabled { get; init; }
    public int DriftAlertCount { get; init; }
    public int AutoRemediationRuleCount { get; init; }
    public DateTime? LastMonitoringCheck { get; init; }
}

public class ConMonExpirationInfo
{
    public bool HasActiveAuthorization { get; init; }
    public string? DecisionType { get; init; }
    public DateTime? DecisionDate { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public int? DaysUntilExpiration { get; init; }
    public required string AlertLevel { get; init; }
    public required string AlertMessage { get; init; }
    public bool IsExpired { get; init; }
}

public class ConMonReauthorizationInfo
{
    public bool IsTriggered { get; init; }
    public required List<string> Triggers { get; init; }
    public int UnreviewedChangeCount { get; init; }
}

public class AgreementExpirationInfo
{
    public required string ItemType { get; init; }
    public required string AgreementTitle { get; init; }
    public string? TargetSystemName { get; init; }
    public DateTime? ExpirationDate { get; init; }
    public int DaysUntilExpiration { get; init; }
    public required string AlertLevel { get; init; }
    public required string Message { get; init; }
}

public class SignificantChangeItemInfo
{
    public required string Id { get; init; }
    public required string ChangeType { get; init; }
    public required string Description { get; init; }
    public DateTime DetectedAt { get; init; }
    public required string DetectedBy { get; init; }
    public bool RequiresReauthorization { get; init; }
    public bool ReauthorizationTriggered { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? Disposition { get; init; }
}

public class ConMonReportSummaryInfo
{
    public required string ReportId { get; init; }
    public required string ReportType { get; init; }
    public required string Period { get; init; }
    public double ComplianceScore { get; init; }
    public double? AuthorizedBaselineScore { get; init; }
    public double? ScoreDelta { get; init; }
    public int NewFindings { get; init; }
    public int ResolvedFindings { get; init; }
    public int OpenPoamItems { get; init; }
    public int OverduePoamItems { get; init; }
    public DateTime GeneratedAt { get; init; }
    public required string GeneratedBy { get; init; }
}

public class SspSectionInfo
{
    public int SectionNumber { get; init; }
    public required string Title { get; init; }
    public required string Status { get; init; }
    public string? AuthoredBy { get; init; }
    public DateTime? AuthoredAt { get; init; }
    public string? ReviewedBy { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public int Version { get; init; }
}

public class NarrativeGovernanceInfo
{
    public int TotalNarratives { get; init; }
    public int Draft { get; init; }
    public int InReview { get; init; }
    public int Approved { get; init; }
    public int NeedsRevision { get; init; }
    public double ApprovalPct { get; init; }
}

public class ScanImportInfo
{
    public required string ImportId { get; init; }
    public required string ImportType { get; init; }
    public required string FileName { get; init; }
    public DateTime ImportedAt { get; init; }
    public int TotalEntries { get; init; }
    public int OpenCount { get; init; }
    public int PassCount { get; init; }
    public string? BenchmarkTitle { get; init; }
}

// ───────────── Assessments Page DTOs ──────────────────────────────────────────

public class AssessmentListItemDto
{
    public required string AssessmentId { get; init; }
    public string? SystemId { get; init; }
    public string? SystemName { get; init; }
    public required string Framework { get; init; }
    public required string Status { get; init; }
    public required string ScanType { get; init; }
    public double ComplianceScore { get; init; }
    public int TotalControls { get; init; }
    public int PassedControls { get; init; }
    public int FailedControls { get; init; }
    public int TotalFindings { get; init; }
    public DateTime AssessedAt { get; init; }
    public string? InitiatedBy { get; init; }
    public bool HasCategorization { get; init; }
}

public class AssessmentDetailDto
{
    public required string AssessmentId { get; init; }
    public string? SystemId { get; init; }
    public string? SystemName { get; init; }
    public required string Framework { get; init; }
    public required string ScanType { get; init; }
    public required string Status { get; init; }
    public double ComplianceScore { get; init; }
    public int TotalControls { get; init; }
    public int PassedControls { get; init; }
    public int FailedControls { get; init; }
    public int NotAssessedControls { get; init; }
    public DateTime AssessedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? InitiatedBy { get; init; }
    public string? ExecutiveSummary { get; init; }
    public int CriticalCount { get; init; }
    public int HighCount { get; init; }
    public int MediumCount { get; init; }
    public int LowCount { get; init; }
    public List<AssessmentFamilyDto> FamilyResults { get; init; } = [];
    public List<AssessmentFindingDto> Findings { get; init; } = [];
}

public class AssessmentFamilyDto
{
    public required string FamilyCode { get; init; }
    public required string FamilyName { get; init; }
    public int TotalControls { get; init; }
    public int PassedControls { get; init; }
    public int FailedControls { get; init; }
    public double ComplianceScore { get; init; }
}

public class AssessmentFindingDto
{
    public required string FindingId { get; init; }
    public string? ControlId { get; init; }
    public string ControlFamily { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public required string Severity { get; init; }
    public required string Status { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? RemediationGuidance { get; init; }
    public DateTime DiscoveredAt { get; init; }
    public string? DeviationId { get; init; }
    public string? DeviationType { get; init; }
}

// ───────────── Narrative Viewer DTOs ──────────────────────────────────────────

public class NarrativeListItemDto
{
    public required string Id { get; init; }
    public required string ControlId { get; init; }
    public required string Family { get; init; }
    public string? Narrative { get; init; }
    public required string ImplementationStatus { get; init; }
    public required string ApprovalStatus { get; init; }
    public required string AuthoredBy { get; init; }
    public DateTime AuthoredAt { get; init; }
    public int Version { get; init; }
    public bool IsAutoPopulated { get; init; }
    public bool AiSuggested { get; init; }
}

public class BulkNarrativeUpdateRequest
{
    public required List<string> ControlIds { get; init; }
    public string? ImplementationStatus { get; init; }
    public string? ApprovalStatus { get; init; }
    public string? UpdatedBy { get; init; }
}

public class SaveNarrativeRequest
{
    public required string Narrative { get; init; }
}

public class CreateNarrativeRequest
{
    public required string ControlId { get; init; }
    public string? Narrative { get; init; }
    public string? ImplementationStatus { get; init; }
}

// ─── System Profile (Feature 046) ───────────────────────────────────────────

public class SaveProfileSectionBody
{
    public required string Content { get; init; }
    public object[]? ChildItems { get; init; }
}

public class SubmitSectionsBody
{
    public string? Action { get; init; }
    public string[]? SectionTypes { get; init; }
}

public class ReviewSectionBody
{
    public required string Decision { get; init; }
    public string? Comments { get; init; }
}
