// ─────────────────────────────────────────────────────────────────────────────
// Feature 015 · Phase 12 — eMASS & OSCAL Interoperability (US10)
// T143: EmassControlExportRow and EmassPoamExportRow records
// ─────────────────────────────────────────────────────────────────────────────

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Represents a single row in an eMASS control-level export.
/// Column names match the eMASS Excel export format exactly (25 fields).
/// This is a DTO record — not an EF Core entity.
/// </summary>
public record EmassControlExportRow(
    // ─── System Identification ───
    string SystemName,
    string SystemAcronym,
    string DitprId,
    string EmassId,

    // ─── Control Identification ───
    string ControlIdentifier,
    string ControlName,
    string ControlFamily,

    // ─── Implementation ───
    string ImplementationStatus,
    string? ImplementationNarrative,
    string? CommonControlProvider,
    string ResponsibilityType,

    // ─── Assessment ───
    string? ComplianceStatus,
    string? AssessmentProcedure,
    string? AssessorName,
    DateTime? AssessmentDate,
    string? TestResult,

    // ─── Applicable Baseline ───
    string SecurityControlBaseline,
    bool IsOverlayControl,
    string? OverlayName,

    // ─── AP / SSP Fields ───
    string? ApNumber,
    string? SecurityPlanTitle,

    // ─── Metadata ───
    DateTime? LastModified,
    string? ModifiedBy
);

/// <summary>
/// Represents a single row in an eMASS POA&amp;M export (24 fields).
/// Column names match the eMASS POA&amp;M worksheet export format.
/// This is a DTO record — not an EF Core entity.
/// </summary>
public record EmassPoamExportRow(
    // ─── System ───
    string SystemName,
    string EmassId,

    // ─── POA&M Item ───
    string PoamId,
    string Weakness,
    string WeaknessSource,
    string PointOfContact,
    string? PocEmail,
    string SecurityControlNumber,

    // ─── Severity ───
    string RawSeverity,
    string? RelevanceOfThreat,
    string? LikelihoodOfExploitation,
    string? ImpactDescription,
    string? ResidualRiskLevel,

    // ─── Remediation ───
    string ScheduledCompletionDate,
    string? PlannedMilestones,
    string? MilestoneChanges,
    string? ResourcesRequired,
    string? CostEstimate,

    // ─── Status ───
    string Status,
    DateTime? CompletionDate,
    string? Comments,
    bool IsActive,

    // ─── Metadata ───
    DateTime? CreatedDate,
    DateTime? LastUpdatedDate,
    string? LastUpdatedBy,

    // ─── Deviation (Feature 035) ───
    string? DeviationJustification = null,
    string? DeviationTypeName = null,
    string? DeviationExpiration = null
);

/// <summary>
/// Options for eMASS import — conflict resolution strategy, dry-run mode, and field selectors.
/// </summary>
public record EmassImportOptions(
    ConflictResolution OnConflict = ConflictResolution.Skip,
    bool DryRun = true,
    bool ImportNarratives = true,
    bool ImportAssessmentResults = true
);

/// <summary>
/// Conflict resolution strategy for eMASS imports.
/// </summary>
public enum ConflictResolution
{
    /// <summary>Keep ATO Copilot data, skip eMASS conflicts.</summary>
    Skip,

    /// <summary>Overwrite ATO Copilot data with eMASS data.</summary>
    Overwrite,

    /// <summary>Per-field merge — text fields appended, enum/status prefer imported, dates prefer more recent.</summary>
    Merge
}

/// <summary>
/// Result of an eMASS import operation.
/// </summary>
public record EmassImportResult(
    int TotalRows,
    int Imported,
    int Skipped,
    int Conflicts,
    List<EmassImportConflict> ConflictDetails
);

/// <summary>
/// Detail about a single conflict encountered during eMASS import.
/// </summary>
public record EmassImportConflict(
    string ControlId,
    string Field,
    string ExistingValue,
    string ImportedValue,
    string Resolution
);

/// <summary>
/// Supported OSCAL model types for export.
/// </summary>
public enum OscalModelType
{
    /// <summary>OSCAL System Security Plan.</summary>
    Ssp,

    /// <summary>OSCAL Assessment Results.</summary>
    AssessmentResults,

    /// <summary>OSCAL Plan of Action and Milestones.</summary>
    Poam,

    /// <summary>OSCAL Assessment Plan (Security Assessment Plan).</summary>
    AssessmentPlan
}
