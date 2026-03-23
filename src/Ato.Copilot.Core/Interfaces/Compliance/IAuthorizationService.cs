using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for authorization decision workflows: issuing ATO/ATOwC/IATT/DATO,
/// risk acceptance with auto-expire, POA&amp;M management, RAR generation,
/// and authorization package bundling.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Issue an authorization decision for a registered system.
    /// Advances the system to the Monitor RMF step on success.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="decisionType">ATO, ATOwC, IATT, or DATO.</param>
    /// <param name="expirationDate">Required for ATO/ATOwC/IATT; null for DATO.</param>
    /// <param name="residualRiskLevel">Residual risk level: Low, Medium, High, Critical.</param>
    /// <param name="termsAndConditions">Optional conditions text.</param>
    /// <param name="residualRiskJustification">Optional risk justification.</param>
    /// <param name="riskAcceptances">Optional list of findings to accept risk on.</param>
    /// <param name="issuedBy">AO user ID.</param>
    /// <param name="issuedByName">AO display name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created AuthorizationDecision with risk acceptances.</returns>
    /// <exception cref="InvalidOperationException">System not found, invalid decision type, or missing expiration.</exception>
    Task<AuthorizationDecision> IssueAuthorizationAsync(
        string systemId,
        string decisionType,
        DateTime? expirationDate,
        string residualRiskLevel,
        string? termsAndConditions = null,
        string? residualRiskJustification = null,
        List<RiskAcceptanceInput>? riskAcceptances = null,
        string issuedBy = "mcp-user",
        string issuedByName = "MCP User",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Accept risk for a specific finding with justification and expiration.
    /// AO-only operation.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="findingId">ComplianceFinding ID.</param>
    /// <param name="controlId">NIST control ID.</param>
    /// <param name="catSeverity">CAT severity: CatI, CatII, CatIII.</param>
    /// <param name="justification">Risk acceptance rationale.</param>
    /// <param name="expirationDate">Auto-expire date.</param>
    /// <param name="compensatingControl">Optional compensating measure.</param>
    /// <param name="acceptedBy">AO user ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created RiskAcceptance record.</returns>
    /// <exception cref="InvalidOperationException">No active authorization or finding not found.</exception>
    Task<RiskAcceptance> AcceptRiskAsync(
        string systemId,
        string findingId,
        string controlId,
        string catSeverity,
        string justification,
        DateTime expirationDate,
        string? compensatingControl = null,
        string acceptedBy = "mcp-user",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the risk register for a system showing active, expired, and/or revoked risk acceptances.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="statusFilter">Filter: "active", "expired", "revoked", "all" (default: "active").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of risk acceptance records with finding details.</returns>
    Task<RiskRegister> GetRiskRegisterAsync(
        string systemId,
        string statusFilter = "active",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a POA&amp;M item linked to a finding and optionally to a Kanban task.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="weakness">Weakness description.</param>
    /// <param name="controlId">NIST control ID.</param>
    /// <param name="catSeverity">CAT severity.</param>
    /// <param name="poc">Point of contact.</param>
    /// <param name="scheduledCompletion">Target fix date.</param>
    /// <param name="findingId">Optional ComplianceFinding ID.</param>
    /// <param name="resourcesRequired">Optional resources needed.</param>
    /// <param name="milestones">Optional milestones.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created PoamItem with milestones.</returns>
    Task<PoamItem> CreatePoamAsync(
        string systemId,
        string weakness,
        string controlId,
        string catSeverity,
        string poc,
        DateTime scheduledCompletion,
        string? findingId = null,
        string? resourcesRequired = null,
        List<MilestoneInput>? milestones = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List POA&amp;M items for a system with optional filters.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="statusFilter">Optional: "Ongoing", "Completed", "Delayed", "RiskAccepted".</param>
    /// <param name="severityFilter">Optional: "CatI", "CatII", "CatIII".</param>
    /// <param name="overdueOnly">Only show overdue items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of PoamItem records with milestones.</returns>
    Task<List<PoamItem>> ListPoamAsync(
        string systemId,
        string? statusFilter = null,
        string? severityFilter = null,
        bool overdueOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a Risk Assessment Report for a system.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="assessmentId">ComplianceAssessment ID.</param>
    /// <param name="format">Output format: "markdown" (default) or "docx".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>RAR document content.</returns>
    Task<RarDocument> GenerateRarAsync(
        string systemId,
        string assessmentId,
        string? format = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bundle a complete authorization package as a collection of documents.
    /// Includes SSP + SAR + RAR + POA&amp;M + CRM + ATO Letter.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="format">Document format: "markdown" (default), "docx", "pdf".</param>
    /// <param name="includeEvidence">Whether to include evidence attachments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Package result with document list and metadata.</returns>
    Task<AuthorizationPackageBundle> BundlePackageAsync(
        string systemId,
        string? format = null,
        bool includeEvidence = false,
        CancellationToken cancellationToken = default);
}

// ═══════════════════════════════════════════════════════════════════════════════
// DTOs for Authorization Service
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Input DTO for risk acceptance during authorization issuance.
/// </summary>
public class RiskAcceptanceInput
{
    /// <summary>ComplianceFinding ID to accept risk for.</summary>
    public string FindingId { get; set; } = string.Empty;

    /// <summary>NIST control ID.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>CAT severity: CatI, CatII, CatIII.</summary>
    public string CatSeverity { get; set; } = string.Empty;

    /// <summary>Risk acceptance justification.</summary>
    public string Justification { get; set; } = string.Empty;

    /// <summary>Compensating control description.</summary>
    public string? CompensatingControl { get; set; }

    /// <summary>Auto-expire date for this risk acceptance.</summary>
    public DateTime ExpirationDate { get; set; }
}

/// <summary>
/// Input DTO for POA&amp;M milestone creation.
/// </summary>
public class MilestoneInput
{
    /// <summary>Milestone description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Target completion date.</summary>
    public DateTime TargetDate { get; set; }
}

/// <summary>
/// Risk register view model containing risk acceptances with metadata.
/// </summary>
public class RiskRegister
{
    /// <summary>RegisteredSystem ID.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Total risk acceptances matching the filter.</summary>
    public int TotalAcceptances { get; set; }

    /// <summary>Count of active acceptances.</summary>
    public int ActiveCount { get; set; }

    /// <summary>Count of expired acceptances.</summary>
    public int ExpiredCount { get; set; }

    /// <summary>Count of revoked acceptances.</summary>
    public int RevokedCount { get; set; }

    /// <summary>Risk acceptance records.</summary>
    public List<RiskAcceptanceDetail> Acceptances { get; set; } = new();
}

/// <summary>
/// Detailed risk acceptance for risk register display.
/// </summary>
public class RiskAcceptanceDetail
{
    /// <summary>Risk acceptance ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Control ID associated with the accepted finding.</summary>
    public string ControlId { get; set; } = string.Empty;

    /// <summary>CAT severity.</summary>
    public string CatSeverity { get; set; } = string.Empty;

    /// <summary>Justification text.</summary>
    public string Justification { get; set; } = string.Empty;

    /// <summary>Compensating control, if any.</summary>
    public string? CompensatingControl { get; set; }

    /// <summary>Expiration date.</summary>
    public DateTime ExpirationDate { get; set; }

    /// <summary>When the risk was accepted.</summary>
    public DateTime AcceptedAt { get; set; }

    /// <summary>Who accepted the risk.</summary>
    public string AcceptedBy { get; set; } = string.Empty;

    /// <summary>Current status: "active", "expired", "revoked".</summary>
    public string Status { get; set; } = "active";

    /// <summary>Finding title, if available.</summary>
    public string? FindingTitle { get; set; }
}

/// <summary>
/// Risk Assessment Report document.
/// </summary>
public class RarDocument
{
    /// <summary>RegisteredSystem ID.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>ComplianceAssessment ID.</summary>
    public string AssessmentId { get; set; } = string.Empty;

    /// <summary>Generated at timestamp.</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Document format.</summary>
    public string Format { get; set; } = "markdown";

    /// <summary>Executive summary of aggregate risk.</summary>
    public string ExecutiveSummary { get; set; } = string.Empty;

    /// <summary>Aggregate risk level.</summary>
    public string AggregateRiskLevel { get; set; } = string.Empty;

    /// <summary>Per-family risk breakdown.</summary>
    public List<FamilyRiskResult> FamilyRisks { get; set; } = new();

    /// <summary>CAT severity breakdown.</summary>
    public CatBreakdown CatBreakdown { get; set; } = new();

    /// <summary>Full document content (markdown).</summary>
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Risk assessment by control family.
/// </summary>
public class FamilyRiskResult
{
    /// <summary>Control family prefix (e.g., "AC").</summary>
    public string Family { get; set; } = string.Empty;

    /// <summary>Family display name.</summary>
    public string FamilyName { get; set; } = string.Empty;

    /// <summary>Total findings in this family.</summary>
    public int TotalFindings { get; set; }

    /// <summary>Open findings count.</summary>
    public int OpenFindings { get; set; }

    /// <summary>Risk-accepted findings count.</summary>
    public int AcceptedFindings { get; set; }

    /// <summary>Risk level for this family.</summary>
    public string RiskLevel { get; set; } = "Low";
}

/// <summary>
/// CAT severity breakdown for reports.
/// </summary>
public class CatBreakdown
{
    /// <summary>CAT I (critical) finding count.</summary>
    public int CatI { get; set; }

    /// <summary>CAT II (medium) finding count.</summary>
    public int CatII { get; set; }

    /// <summary>CAT III (low) finding count.</summary>
    public int CatIII { get; set; }

    /// <summary>Total findings with CAT severity.</summary>
    public int Total => CatI + CatII + CatIII;
}

/// <summary>
/// Authorization package containing bundled compliance documents.
/// </summary>
public class AuthorizationPackageBundle
{
    /// <summary>RegisteredSystem ID.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Generated at timestamp.</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Document format used.</summary>
    public string Format { get; set; } = "markdown";

    /// <summary>List of documents included in the package.</summary>
    public List<PackageDocument> Documents { get; set; } = new();

    /// <summary>Whether evidence attachments are included.</summary>
    public bool IncludesEvidence { get; set; }

    /// <summary>Total count of documents in the package.</summary>
    public int DocumentCount => Documents.Count;
}

/// <summary>
/// A single document in the authorization package.
/// </summary>
public class PackageDocument
{
    /// <summary>Document name (e.g., "System Security Plan").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>File name (e.g., "ssp.md").</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Document type: SSP, SAR, RAR, POAM, CRM, ATO_LETTER.</summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>Document content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Generation status: "generated", "not_available".</summary>
    public string Status { get; set; } = "generated";
}
