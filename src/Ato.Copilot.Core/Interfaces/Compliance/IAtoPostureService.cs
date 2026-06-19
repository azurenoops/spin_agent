// =============================================================================
//  IAtoPostureService.cs
//  Ato.Copilot.Core — Service Interface Contracts
//  Issue #422 — AO Posture API + CI/CD Webhook (W10 cATO Gap Closure)
//
//  C# 12  |  #nullable enable  |  sealed record DTOs
// =============================================================================

#nullable enable

using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

// ─────────────────────────────────────────────────────────────────────────────
//  ENUMS
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Compliance status of a single CSRMC pillar.</summary>
public enum PillarComplianceStatus
{
    /// <summary>Status cannot be determined — insufficient ingestion history.</summary>
    Unknown = 0,
    /// <summary>Pillar requirements have been met within the evaluation window.</summary>
    Compliant = 1,
    /// <summary>Pillar requirements have NOT been met within the evaluation window.</summary>
    NonCompliant = 2,
    /// <summary>Pillar is not applicable to this system's mission profile.</summary>
    NotApplicable = 3
}

/// <summary>High-level outcome of a single webhook ingestion attempt.</summary>
public enum WebhookIngestionStatus
{
    Accepted = 0,
    Rejected = 1,
    /// <summary>Payload is an exact duplicate within the 24-hour idempotency window.</summary>
    Duplicate = 2,
    Processing = 3,
    Failed = 4
}

/// <summary>Semantic type of the JSON payload carried in a pipeline webhook.</summary>
public enum WebhookPayloadType
{
    /// <summary>SARIF 2.1.0 static-analysis results document.</summary>
    Sarif,
    /// <summary>OSCAL component-definition or SSP fragment.</summary>
    Oscal,
    /// <summary>Artifact-evidence linkage record.</summary>
    Evidence
}

// ─────────────────────────────────────────────────────────────────────────────
//  CUSTOM EXCEPTIONS
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Thrown when an inbound webhook's HMAC-SHA256 signature does not match
/// the per-system shared secret.</summary>
public sealed class InvalidSignatureException : Exception
{
    public InvalidSignatureException() : base("Webhook signature validation failed.") { }
    public InvalidSignatureException(string message) : base(message) { }
    public InvalidSignatureException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when a caller exceeds the 100 req/min webhook ingestion rate.</summary>
public sealed class RateLimitExceededException : Exception
{
    public RateLimitExceededException() : base("Webhook ingestion rate limit exceeded.") { }
    public RateLimitExceededException(string message) : base(message) { }
    public RateLimitExceededException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when an inbound webhook payload fails schema or semantic validation
/// (e.g., unsupported SARIF version, missing required fields, malformed JSON).</summary>
public sealed class PayloadValidationException : Exception
{
    public PayloadValidationException() : base("Webhook payload failed validation.") { }
    public PayloadValidationException(string message) : base(message) { }
    public PayloadValidationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Thrown when the caller lacks the required role for the operation
/// (e.g., forceRefresh requires ISSM+).</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException() : base("The caller does not have permission to perform this operation.") { }
    public ForbiddenException(string message) : base(message) { }
    public ForbiddenException(string message, Exception inner) : base(message, inner) { }
}

// ─────────────────────────────────────────────────────────────────────────────
//  DTOs — Authorization
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Current authorization decision status projected from the most recent
/// active AuthorizationDecision row.</summary>
public sealed record AuthorizationStatusDto
{
    public AuthorizationDecisionType? DecisionType { get; init; }
    public DateTimeOffset? DecisionDate { get; init; }
    /// <summary>ATOs expire 3 years from DecisionDate; IATTs are shorter; DATOs have no expiry.</summary>
    public DateTimeOffset? ExpirationDate { get; init; }
    public bool IsActive { get; init; }
    public bool IsExpired { get; init; }
    public int? DaysUntilExpiration { get; init; }
    /// <summary>
    /// Display name of the Authorizing Official.
    /// <para><strong>Role-gated:</strong> populated only for AuthorizingOfficial role; null otherwise.</para>
    /// </summary>
    public string? AuthorizingOfficial { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  DTOs — Compliance / Findings / POA&M / ConMon
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Aggregated ControlEffectiveness summary.
/// Score = (Satisfied / (Total - NotAssessed)) × 100, 2dp.</summary>
public sealed record ComplianceSummaryDto
{
    public int TotalControls { get; init; }
    public int Satisfied { get; init; }
    public int OtherThanSatisfied { get; init; }
    public int NotAssessed { get; init; }
    public decimal ComplianceScore { get; init; }
}

/// <summary>Open ComplianceFinding records by CatSeverity tier.</summary>
public sealed record FindingsSummaryDto
{
    public int CatI { get; init; }
    public int CatII { get; init; }
    public int CatIII { get; init; }
    /// <summary>Computed — do not store separately.</summary>
    public int Total => CatI + CatII + CatIII;
}

/// <summary>PoamItem lifecycle summary across all statuses.</summary>
public sealed record PoamSummaryDto
{
    public int Open { get; init; }
    /// <summary>Open items past ScheduledCompletionDate (status Delayed). Must be 0 for cATO.</summary>
    public int Overdue { get; init; }
    public int Completed { get; init; }
    public int RiskAccepted { get; init; }
}

/// <summary>ConMon programme summary from the system's active ConMonPlan and ConMonReport records.</summary>
public sealed record ConMonSummaryDto
{
    /// <summary>True when an active ConMonPlan row exists.</summary>
    public bool IsEnabled { get; init; }
    public DateTimeOffset? LastReportDate { get; init; }
    public string? AssessmentFrequency { get; init; }
    public decimal? LatestComplianceScore { get; init; }
    /// <summary>ConMonReport.AuthorizedBaselineScore — captured at ATO issuance for drift calculation.</summary>
    public decimal? AuthorizedBaselineScore { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  DTOs — CSRMC Pillars
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// CSRMC pillar compliance status for all three pillars.
/// <para><strong>Role-gated:</strong> populated only for AuthorizingOfficial role on AtoPostureDto.</para>
/// </summary>
public sealed record CsrmcPillarStatusDto
{
    public PillarComplianceStatus Pillar1Status { get; init; }
    public PillarComplianceStatus Pillar2Status { get; init; }
    /// <summary>Pillar 3 — DevSecOps / pipeline integration.
    /// Compliant = successful webhook ingest within the last 90 days.</summary>
    public PillarComplianceStatus Pillar3Status { get; init; }
    public DateTimeOffset EvaluatedAt { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  DTOs — Top-Level ATO Posture
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Aggregated ATO posture snapshot. Primary read-model from IAtoPostureService.GetPostureAsync.
/// Cached 5 minutes in IMemoryCache under key <c>ato-posture:{SystemId}</c>.
/// </summary>
public sealed record AtoPostureDto
{
    public Guid SystemId { get; init; }
    public string SystemName { get; init; } = string.Empty;
    public AuthorizationStatusDto AuthorizationStatus { get; init; } = new();
    public ComplianceSummaryDto ComplianceSummary { get; init; } = new();
    public FindingsSummaryDto FindingsSummary { get; init; } = new();
    public PoamSummaryDto PoamSummary { get; init; } = new();
    public ConMonSummaryDto ConMonSummary { get; init; } = new();
    /// <summary>
    /// <strong>Role-gated:</strong> populated only for AuthorizingOfficial role; null otherwise.
    /// </summary>
    public CsrmcPillarStatusDto? CsrmcPillarStatus { get; init; }
    public DateTimeOffset RetrievedAt { get; init; }
    /// <summary>True = served from IMemoryCache (TTL 5 min); False = recomputed from DB.</summary>
    public bool ServedFromCache { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  DTOs — cATO Eligibility
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// cATO eligibility under DoD CSRMC policy (September 2025).
/// All four criteria must be satisfied for IsEligible = true.
/// </summary>
public sealed record CatoEligibilityDto
{
    public bool IsEligible { get; init; }
    /// <summary>Criterion 1 — zero open CatI findings.</summary>
    public bool HasZeroCatIFindings { get; init; }
    /// <summary>Criterion 2 — zero overdue POA&amp;M items.</summary>
    public bool HasZeroOverduePOAMs { get; init; }
    /// <summary>Criterion 3 — active ConMonPlan exists.</summary>
    public bool IsConMonEnabled { get; init; }
    /// <summary>Criterion 4 — all three CSRMC pillars Compliant.</summary>
    public bool AllCsrmcPillarsCompliant { get; init; }
    /// <summary>Human-readable reasons for ineligibility. Empty when IsEligible = true.</summary>
    public IReadOnlyList<string> IneligibilityReasons { get; init; } = [];
    public DateTimeOffset CheckedAt { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  SERVICE INTERFACE — IAtoPostureService
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Aggregates ATO posture from ConMon, Findings, POA&amp;M, and AuthorizationDecision
/// repositories into a single read-model, and evaluates cATO/CSRMC eligibility.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Caching:</strong> Implementations cache in <see cref="IMemoryCache"/> under key
/// <c>ato-posture:{systemId}</c> with a 5-minute absolute TTL.
/// <see cref="AtoPostureDto.ServedFromCache"/> indicates the source.
/// </para>
/// <para>
/// <strong>forceRefresh authorization:</strong> Requires ISSM or higher role.
/// Callers below this threshold receive <see cref="ForbiddenException"/>.
/// </para>
/// <para>
/// <strong>Role-gated fields:</strong> AuthorizingOfficial role additionally unlocks
/// <see cref="AuthorizationStatusDto.AuthorizingOfficial"/> and
/// <see cref="AtoPostureDto.CsrmcPillarStatus"/>. Both are null for all other callers.
/// </para>
/// </remarks>
public interface IAtoPostureService
{
    /// <summary>
    /// Returns an aggregated ATO posture snapshot, optionally bypassing the 5-minute cache.
    /// </summary>
    /// <param name="systemId">Registered system to evaluate.</param>
    /// <param name="callerRoles">Normalized role names held by the authenticated caller
    /// (e.g., { "ISSM" }, { "AuthorizingOfficial" }).</param>
    /// <param name="forceRefresh">Bypass IMemoryCache and recompute from DB. Requires ISSM+.</param>
    /// <param name="cancellationToken">Propagates operation cancellation.</param>
    /// <returns>The posture snapshot, or null when the system doesn't exist or tenant mismatch.</returns>
    /// <exception cref="ForbiddenException">forceRefresh=true but caller lacks ISSM+ role.</exception>
    Task<AtoPostureDto?> GetPostureAsync(
        Guid systemId,
        IReadOnlySet<string> callerRoles,
        bool forceRefresh,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates cATO eligibility under DoD CSRMC policy (September 2025).
    /// </summary>
    /// <remarks>
    /// All four criteria must be satisfied:
    /// (1) Zero open CatI findings; (2) Zero overdue POA&amp;M items;
    /// (3) Active ConMonPlan exists; (4) All three CSRMC pillars Compliant.
    /// </remarks>
    /// <param name="systemId">Registered system to evaluate.</param>
    /// <exception cref="SystemNotFoundException">System not found.</exception>
    Task<CatoEligibilityDto> EvaluateCatoEligibilityAsync(
        Guid systemId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns CSRMC Pillar 3 compliance status.
    /// </summary>
    /// <remarks>
    /// Compliant = at least one Accepted webhook ingest within the last 90 days.
    /// NonCompliant = webhooks exist but none within 90 days.
    /// Unknown = no webhooks ever received.
    /// </remarks>
    /// <exception cref="SystemNotFoundException">System not found.</exception>
    Task<PillarComplianceStatus> GetPillar3StatusAsync(
        Guid systemId,
        CancellationToken cancellationToken = default);
}
