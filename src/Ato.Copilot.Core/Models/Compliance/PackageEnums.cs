namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Lifecycle status of an authorization package generation job.
/// Follows strict state machine: Pending → Generating → Validating → Completed|Failed.
/// </summary>
public enum PackageStatus
{
    Pending = 0,
    Generating = 1,
    Validating = 2,
    Completed = 3,
    Failed = 4
}

/// <summary>
/// Controls whether evidence files are embedded in the package archive
/// or referenced via a manifest with download links.
/// </summary>
public enum EvidenceMode
{
    Embedded = 0,
    ManifestOnly = 1
}

/// <summary>
/// Type of artifact included in an authorization package.
/// Each type appears at most once per package.
/// </summary>
public enum PackageArtifactType
{
    OscalSsp = 0,
    OscalPoam = 1,
    OscalAssessmentResults = 2,
    OscalAssessmentPlan = 3,
    Sar = 4,
    EvidenceManifest = 5
}

/// <summary>
/// Lifecycle status of a Security Assessment Report.
/// Sequential progression: NotStarted → Draft → UnderReview → Approved.
/// UnderReview → Draft is allowed for revision requests.
/// </summary>
public enum SarStatus
{
    NotStarted = 0,
    Draft = 1,
    UnderReview = 2,
    Approved = 3
}

/// <summary>
/// Type of narrative section within a SAR document.
/// </summary>
public enum SarSectionType
{
    ExecutiveSummary = 0,
    AssessmentScope = 1,
    FindingsSummary = 2,
    FindingDetails = 3,
    Recommendations = 4
}

/// <summary>
/// Severity of a package validation finding.
/// Errors block package generation; warnings allow generation with acknowledgment.
/// </summary>
public enum ValidationSeverity
{
    Error = 0,
    Warning = 1
}
