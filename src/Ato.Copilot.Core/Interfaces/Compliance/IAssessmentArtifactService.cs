using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for assessment artifact operations: per-control effectiveness recording,
/// immutable snapshot creation, snapshot comparison, evidence integrity verification,
/// and evidence completeness checking.
/// </summary>
public interface IAssessmentArtifactService
{
    /// <summary>
    /// Record an SCA's effectiveness determination for a specific control.
    /// Creates a <see cref="ControlEffectiveness"/> record and, if the determination
    /// is OtherThanSatisfied, requires a CAT severity.
    /// </summary>
    /// <param name="assessmentId">ComplianceAssessment ID.</param>
    /// <param name="controlId">NIST 800-53 control ID (e.g., "AC-2").</param>
    /// <param name="determination">Satisfied or OtherThanSatisfied.</param>
    /// <param name="method">Assessment method: "Test", "Interview", "Examine".</param>
    /// <param name="evidenceIds">Optional linked evidence record IDs.</param>
    /// <param name="notes">Assessor notes.</param>
    /// <param name="catSeverity">CAT severity level (required if OtherThanSatisfied).</param>
    /// <param name="assessorId">SCA user ID making the determination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created ControlEffectiveness record.</returns>
    /// <exception cref="InvalidOperationException">Assessment not found, invalid determination, or missing CAT severity.</exception>
    Task<ControlEffectiveness> AssessControlAsync(
        string assessmentId,
        string controlId,
        string determination,
        string? method = null,
        List<string>? evidenceIds = null,
        string? notes = null,
        string? catSeverity = null,
        string assessorId = "mcp-user",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create an immutable, SHA-256-hashed snapshot of the current assessment state.
    /// The hash covers all ControlEffectiveness determinations, ComplianceFinding summaries,
    /// ComplianceEvidence hashes, and the computed compliance score in canonical JSON form.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="assessmentId">ComplianceAssessment ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created immutable ComplianceSnapshot with integrity hash.</returns>
    /// <exception cref="InvalidOperationException">System or assessment not found.</exception>
    Task<ComplianceSnapshot> TakeSnapshotAsync(
        string systemId,
        string assessmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two assessment snapshots side-by-side.
    /// Returns controls changed (newly Satisfied, newly OtherThanSatisfied, unchanged),
    /// score delta, new findings, resolved findings, and evidence changes.
    /// </summary>
    /// <param name="snapshotIdA">First snapshot ID.</param>
    /// <param name="snapshotIdB">Second snapshot ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Comparison result with deltas and breakdown.</returns>
    /// <exception cref="InvalidOperationException">Snapshot not found.</exception>
    Task<SnapshotComparison> CompareSnapshotsAsync(
        string snapshotIdA,
        string snapshotIdB,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recompute the SHA-256 hash of evidence content and verify it matches the stored hash.
    /// Requires an active directory-qualified organization member whose effective system access
    /// permits evidence management or assigns SCA. This is integrity verification, not approval
    /// or evidence authorship. Atomically audits the trusted verifier and updates
    /// IntegrityVerifiedAt only on a hash match; original collector attribution is preserved.
    /// </summary>
    /// <param name="evidenceId">ComplianceEvidence ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Verification result with original hash, recomputed hash, and match status.</returns>
    /// <exception cref="UnauthorizedAccessException">Identity, ownership, or effective system access is not authorized.</exception>
    Task<EvidenceVerificationResult> VerifyEvidenceAsync(
        string evidenceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check which controls have verified evidence vs. missing evidence.
    /// Returns per-control evidence status and overall completeness percentage.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="assessmentId">Optional filter to a specific assessment.</param>
    /// <param name="familyFilter">Optional filter by control family prefix (e.g., "AC").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evidence completeness report.</returns>
    Task<EvidenceCompletenessReport> CheckEvidenceCompletenessAsync(
        string systemId,
        string? assessmentId = null,
        string? familyFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a Security Assessment Report (SAR) for a system and assessment.
    /// Includes executive summary, control-by-control results, risk summary, and CAT breakdown.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="assessmentId">ComplianceAssessment ID.</param>
    /// <param name="format">Output format: "markdown" (default) or "docx".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated SAR document content.</returns>
    Task<SarDocument> GenerateSarAsync(
        string systemId,
        string assessmentId,
        string format = "markdown",
        CancellationToken cancellationToken = default);
}

// ═══════════════════════════════════════════════════════════════════════════════
// Result DTOs for IAssessmentArtifactService
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Result of comparing two assessment snapshots.
/// </summary>
public class SnapshotComparison
{
    /// <summary>First snapshot info.</summary>
    public SnapshotSummary SnapshotA { get; set; } = new();

    /// <summary>Second snapshot info.</summary>
    public SnapshotSummary SnapshotB { get; set; } = new();

    /// <summary>Compliance score change (B - A).</summary>
    public double ScoreDelta { get; set; }

    /// <summary>Controls that changed from OtherThanSatisfied to Satisfied.</summary>
    public List<string> NewlySatisfied { get; set; } = new();

    /// <summary>Controls that changed from Satisfied to OtherThanSatisfied.</summary>
    public List<string> NewlyOtherThanSatisfied { get; set; } = new();

    /// <summary>Controls that remained in the same determination.</summary>
    public int UnchangedCount { get; set; }

    /// <summary>New findings in snapshot B not present in A.</summary>
    public int NewFindings { get; set; }

    /// <summary>Findings in A that are resolved in B.</summary>
    public int ResolvedFindings { get; set; }

    /// <summary>Evidence records added between snapshots.</summary>
    public int EvidenceAdded { get; set; }

    /// <summary>Evidence records removed between snapshots.</summary>
    public int EvidenceRemoved { get; set; }
}

/// <summary>
/// Summary info for a single snapshot used in comparisons.
/// </summary>
public class SnapshotSummary
{
    /// <summary>Snapshot ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>When snapshot was captured.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>Compliance score at capture time.</summary>
    public double ComplianceScore { get; set; }

    /// <summary>Total controls in snapshot.</summary>
    public int TotalControls { get; set; }

    /// <summary>Passed controls in snapshot.</summary>
    public int PassedControls { get; set; }

    /// <summary>Failed controls in snapshot.</summary>
    public int FailedControls { get; set; }

    /// <summary>SHA-256 integrity hash.</summary>
    public string IntegrityHash { get; set; } = string.Empty;
}

/// <summary>
/// Result of verifying evidence integrity.
/// </summary>
public class EvidenceVerificationResult
{
    /// <summary>Evidence ID verified.</summary>
    public string EvidenceId { get; set; } = string.Empty;

    /// <summary>Control ID the evidence supports.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Originally stored SHA-256 hash.</summary>
    public string OriginalHash { get; set; } = string.Empty;

    /// <summary>Recomputed SHA-256 hash.</summary>
    public string RecomputedHash { get; set; } = string.Empty;

    /// <summary>Whether hashes match: "verified" or "tampered".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Identity of original collector.</summary>
    public string? CollectorIdentity { get; set; }

    /// <summary>Trusted directory/object-qualified identity that performed this integrity check.</summary>
    public string VerifierIdentity { get; set; } = string.Empty;

    /// <summary>Collection method used.</summary>
    public string? CollectionMethod { get; set; }

    /// <summary>When integrity was last verified.</summary>
    public DateTime? IntegrityVerifiedAt { get; set; }
}

/// <summary>
/// Evidence completeness report for a system's controls.
/// </summary>
public class EvidenceCompletenessReport
{
    /// <summary>System ID.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Assessment ID filter applied (if any).</summary>
    public string? AssessmentId { get; set; }

    /// <summary>Overall evidence completeness percentage.</summary>
    public double CompletenessPercentage { get; set; }

    /// <summary>Total controls checked.</summary>
    public int TotalControls { get; set; }

    /// <summary>Controls with verified evidence.</summary>
    public int ControlsWithEvidence { get; set; }

    /// <summary>Controls without any evidence.</summary>
    public int ControlsWithoutEvidence { get; set; }

    /// <summary>Controls with unverified evidence (hash not checked).</summary>
    public int ControlsWithUnverifiedEvidence { get; set; }

    /// <summary>Per-control evidence status details.</summary>
    public List<ControlEvidenceStatus> ControlStatuses { get; set; } = new();
}

/// <summary>
/// Evidence status for a single control.
/// </summary>
public class ControlEvidenceStatus
{
    /// <summary>Control ID.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>Evidence status: "verified", "unverified", "missing".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Number of evidence records for this control.</summary>
    public int EvidenceCount { get; set; }

    /// <summary>Number of verified (hash-checked) evidence records.</summary>
    public int VerifiedCount { get; set; }
}

/// <summary>
/// Generated Security Assessment Report (SAR) document.
/// </summary>
public class SarDocument
{
    /// <summary>System ID.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Assessment ID.</summary>
    public string AssessmentId { get; set; } = string.Empty;

    /// <summary>Output format (markdown, docx).</summary>
    public string Format { get; set; } = "markdown";

    /// <summary>Generated SAR content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Overall compliance score.</summary>
    public double ComplianceScore { get; set; }

    /// <summary>Total controls assessed.</summary>
    public int ControlsAssessed { get; set; }

    /// <summary>Controls satisfied.</summary>
    public int ControlsSatisfied { get; set; }

    /// <summary>Controls other than satisfied.</summary>
    public int ControlsOtherThanSatisfied { get; set; }

    /// <summary>CAT I finding count.</summary>
    public int CatIFindings { get; set; }

    /// <summary>CAT II finding count.</summary>
    public int CatIIFindings { get; set; }

    /// <summary>CAT III finding count.</summary>
    public int CatIIIFindings { get; set; }

    /// <summary>Per-family assessment results.</summary>
    public List<FamilyAssessmentResult> FamilyResults { get; set; } = new();

    /// <summary>UTC timestamp of SAR generation.</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-family assessment result for SAR.
/// </summary>
public class FamilyAssessmentResult
{
    /// <summary>Control family prefix (e.g., "AC").</summary>
    public string Family { get; set; } = string.Empty;

    /// <summary>Controls assessed in this family.</summary>
    public int ControlsAssessed { get; set; }

    /// <summary>Controls satisfied in this family.</summary>
    public int ControlsSatisfied { get; set; }

    /// <summary>Controls other than satisfied.</summary>
    public int ControlsOtherThanSatisfied { get; set; }

    /// <summary>CAT severity breakdown for this family.</summary>
    public Dictionary<string, int> CatBreakdown { get; set; } = new();
}
