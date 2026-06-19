// =============================================================================
//  IPipelineWebhookService.cs + ISarifParserService.cs
//  Ato.Copilot.Core — Service Interface Contracts
//  Issue #422 — AO Posture API + CI/CD Webhook (W10 cATO Gap Closure)
// =============================================================================

#nullable enable

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

// ─────────────────────────────────────────────────────────────────────────────
//  ADDITIONAL EXCEPTION
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Thrown when a referenced RegisteredSystem cannot be located in the platform.</summary>
public sealed class SystemNotFoundException : Exception
{
    public SystemNotFoundException() : base("The specified registered system was not found.") { }
    public SystemNotFoundException(string message) : base(message) { }
    public SystemNotFoundException(Guid systemId)
        : base($"Registered system '{systemId}' was not found.") { }
    public SystemNotFoundException(string message, Exception inner) : base(message, inner) { }
}

// ─────────────────────────────────────────────────────────────────────────────
//  DTOs — Pipeline Webhook
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Inbound pipeline webhook request. Raw body bytes are passed separately to
/// IPipelineWebhookService.ProcessAsync to preserve byte-for-byte fidelity for
/// HMAC-SHA256 signature verification.
/// </summary>
public sealed record PipelineWebhookRequest
{
    public Guid SystemId { get; init; }
    public WebhookPayloadType PayloadType { get; init; }
    /// <summary>Deserialized JSON payload. Caller retains ownership and must dispose.</summary>
    public JsonDocument Payload { get; init; } = null!;
    public string PipelineId { get; init; } = string.Empty;
}

/// <summary>Acknowledgement returned immediately after webhook receipt and validation.</summary>
public sealed record WebhookAckDto
{
    public Guid IngestionId { get; init; }
    public WebhookIngestionStatus Status { get; init; }
    /// <summary>Null unless Status = Accepted.</summary>
    public WebhookProcessingSummaryDto? ProcessingSummary { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
}

/// <summary>Compliance objects created/updated from a successful webhook ingestion.</summary>
public sealed record WebhookProcessingSummaryDto
{
    public int FindingsImported { get; init; }
    public int ControlsUpdated { get; init; }
    /// <summary>POA&amp;M items auto-created for CAT I findings lacking an existing open item.</summary>
    public int PoamItemsCreated { get; init; }
    public int OscalObjectsImported { get; init; }
    public int EvidenceLinked { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>A single entry in the webhook ingestion audit log.</summary>
public sealed record WebhookIngestionLogDto
{
    public Guid Id { get; init; }
    public Guid SystemId { get; init; }
    public string PipelineId { get; init; } = string.Empty;
    public string PipelineRun { get; init; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; init; }
    public string CallerIp { get; init; } = string.Empty;
    public WebhookIngestionStatus Status { get; init; }
    public WebhookPayloadType PayloadType { get; init; }
    public int FindingsCount { get; init; }
    public long ProcessingDurationMs { get; init; }
    public string? ErrorMessage { get; init; }
}

// PagedResult<T> is defined in Ato.Copilot.Core.Interfaces.Kanban.
// Use Ato.Copilot.Core.Interfaces.Kanban.PagedResult<T> to avoid namespace ambiguity.
// (Issue #422: removed duplicate declaration that caused CS0104 in KanbanService)

// ─────────────────────────────────────────────────────────────────────────────
//  DTOs — SARIF
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A single SARIF result after normalization, CWE→NIST mapping, and CAT-tier derivation.</summary>
public sealed record SarifFindingDto
{
    public string RuleId { get; init; } = string.Empty;
    public string RuleDescription { get; init; } = string.Empty;
    public string Level { get; init; } = "none";
    public decimal? CvssScore { get; init; }
    /// <summary>
    /// CAT tier derived from Level and CvssScore (first-match-wins):
    /// 1. CVSS >= 9.0 + level=error → CatI
    /// 2. level=error OR CVSS >= 7.0 → CatII
    /// 3. CVSS >= 4.0 OR level=warning → CatIII
    /// 4. note/none → discard (not stored)
    /// </summary>
    public CatSeverity CatTier { get; init; }
    /// <summary>NIST 800-53 Rev.5 control IDs resolved via the 3-tier mapping cascade.</summary>
    public IReadOnlyList<string> NistControlIds { get; init; } = [];
    public IReadOnlyList<string> CweIds { get; init; } = [];
    public string? LocationUri { get; init; }
    public string? LocationRegion { get; init; }
    /// <summary>Stable fingerprint for dedup. Sourced from result.fingerprints or SHA-256 fallback.</summary>
    public string? FingerprintHash { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>True = new ComplianceFinding INSERT; false = merged via dedup UPDATE.</summary>
    public bool IsNew { get; init; }
}

/// <summary>Info about a SARIF rule that could not be mapped to any NIST 800-53 control.</summary>
public sealed record UnmappedRuleInfo(
    string RuleId,
    string? ToolName,
    int OccurrenceCount,
    IReadOnlyList<string> Tags,
    double? SecuritySeverity);

/// <summary>Aggregated result of a complete SARIF 2.1.0 ingestion operation.</summary>
public sealed record SarifImportResult
{
    public Guid ImportId { get; init; }
    public Guid SystemId { get; init; }
    public string PipelineId { get; init; } = string.Empty;
    public string PipelineRun { get; init; } = string.Empty;
    public string SarifVersion { get; init; } = string.Empty;
    /// <summary>New ComplianceFinding INSERT count (excludes deduplicated merges).</summary>
    public int FindingsImported { get; init; }
    /// <summary>Findings merged into existing records via fingerprint/hash dedup.</summary>
    public int FindingsDeduplicated { get; init; }
    /// <summary>Rule IDs that could not be mapped to any NIST control (unique count).</summary>
    public int UnmappedRuleCount { get; init; }
    /// <summary>Individual result occurrences that were unmapped.</summary>
    public int UnmappedFindingCount { get; init; }
    public IReadOnlyList<SarifFindingDto> Findings { get; init; } = [];
    public IReadOnlyList<UnmappedRuleInfo> UnmappedRules { get; init; } = [];
    /// <summary>
    /// CWE→NIST mapping table built during this import.
    /// Key: CWE ID (e.g. "CWE-89"); Value: resolved NIST control IDs.
    /// CWEs with no mapping → empty list, surfaced as warnings.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ControlMappings { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();
    public IReadOnlyList<string> ParseErrors { get; init; } = [];
    public DateTimeOffset ProcessedAt { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  SERVICE INTERFACE — IPipelineWebhookService
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Receives, validates, deduplicates, and dispatches inbound CI/CD pipeline webhook payloads
/// (SARIF, OSCAL, evidence) into the SPIN Agent compliance data model.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Signature validation:</strong> Every payload must carry an HMAC-SHA256 signature
/// over the raw request body using the per-system shared secret.
/// Format: <c>sha256=&lt;lowercase-hex-digest&gt;</c>.
/// Invalid/missing signature → <see cref="InvalidSignatureException"/> before any DB I/O.
/// </para>
/// <para>
/// <strong>Idempotency:</strong> Deduplicated by pipelineRun ID within a 24-hour sliding
/// window per system. Duplicates return <see cref="WebhookIngestionStatus.Duplicate"/>.
/// </para>
/// <para>
/// <strong>Dispatch routing:</strong> sarif → ISarifParserService | oscal → OSCAL importer |
/// evidence → evidence linker.
/// </para>
/// <para>
/// <strong>Auto-remediation:</strong> New CatI findings without an existing open PoamItem
/// automatically receive one. Count in WebhookProcessingSummaryDto.PoamItemsCreated.
/// </para>
/// <para>
/// <strong>Audit logging:</strong> Every invocation — accepted, rejected, or duplicate —
/// is written to the platform audit log unconditionally.
/// </para>
/// </remarks>
public interface IPipelineWebhookService
{
    /// <summary>
    /// Validates, deduplicates, dispatches, and acknowledges a single inbound pipeline
    /// webhook payload.
    /// </summary>
    /// <param name="request">Deserialized webhook request. Caller retains JsonDocument ownership.</param>
    /// <param name="rawBody">Raw HTTP request body bytes for HMAC-SHA256 verification.
    /// Must not be re-serialized from request — byte-for-byte transport fidelity required.</param>
    /// <param name="signatureHeader">X-Hub-Signature-256 value: <c>sha256=&lt;hex&gt;</c>.</param>
    /// <param name="pipelineRunHeader">X-Pipeline-Run header value (idempotency key). Null = no dedup guarantee.</param>
    /// <param name="callerIp">Source IP for audit logging and rate-limit enforcement.</param>
    /// <exception cref="InvalidSignatureException">HMAC does not match per-system secret.</exception>
    /// <exception cref="RateLimitExceededException">100 req/min per system exceeded.</exception>
    /// <exception cref="PayloadValidationException">Payload fails schema/semantic validation.</exception>
    /// <exception cref="SystemNotFoundException">System not found.</exception>
    Task<WebhookAckDto> ProcessAsync(
        PipelineWebhookRequest request,
        byte[] rawBody,
        string signatureHeader,
        string? pipelineRunHeader,
        string callerIp,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates HMAC-SHA256 signature without triggering processing, DB writes, or audit logs.
    /// Safe to call in middleware / pre-authorization filters.
    /// Does not consume idempotency budget.
    /// </summary>
    /// <returns>True when computed HMAC matches via constant-time comparison.</returns>
    /// <exception cref="SystemNotFoundException">System not found (no shared secret to retrieve).</exception>
    Task<bool> ValidateSignatureAsync(
        Guid systemId,
        byte[] rawBody,
        string signatureHeader,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a time-descending, paginated log of webhook ingestion events for the system.
    /// </summary>
    /// <param name="take">Max records per page. Range: [1, 100]. Default: 25.</param>
    /// <param name="skip">Records to skip for offset pagination. Must be non-negative.</param>
    /// <exception cref="SystemNotFoundException">System not found.</exception>
    /// <exception cref="ArgumentOutOfRangeException">take outside [1,100] or skip negative.</exception>
    Task<Ato.Copilot.Core.Interfaces.Kanban.PagedResult<WebhookIngestionLogDto>> GetIngestionHistoryAsync(
        Guid systemId,
        int take = 25,
        int skip = 0,
        CancellationToken cancellationToken = default);
}

// ─────────────────────────────────────────────────────────────────────────────
//  SERVICE INTERFACE — ISarifParserService
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Parses, normalizes, and persists SARIF 2.1.0 static-analysis documents into the SPIN Agent
/// compliance data model, mapping findings to NIST 800-53 Rev.5 controls and deriving DoD CAT tiers.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Version enforcement:</strong> document.version must equal "2.1.0" exactly.
/// Any other value → <see cref="PayloadValidationException"/> before further parsing.
/// </para>
/// <para>
/// <strong>CWE → NIST mapping (3-tier cascade):</strong>
/// Tier 1: rule.properties.nist (explicit array) → highest confidence.
/// Tier 2: rule.properties.tags regex patterns (NIST.SP.800-53.*, nist:*, control:*, direct ID).
/// Tier 3: CWE IDs in tags/relationships/rule.id → static CweToNistMap dictionary.
/// Fallback: ControlId = null, WeaknessSource = "SARIF-Unmapped" — stored, not thrown.
/// </para>
/// <para>
/// <strong>CAT-tier derivation (first-match-wins):</strong>
/// CVSS >= 9.0 AND level=error → CatI; level=error OR CVSS >= 7.0 → CatII;
/// CVSS >= 4.0 OR level=warning → CatIII; note/none → discard.
/// </para>
/// <para>
/// <strong>Deduplication:</strong> result.fingerprints["primaryLocationLineHash/v1"] (primary)
/// or SHA-256(systemId+ruleId+locationUri+startLine) (fallback).
/// Matched records → UPDATE LastSeenAt; new records → INSERT with IsNew=true.
/// </para>
/// <para>
/// <strong>The parser itself never writes to the database.</strong>
/// The controller/service layer owns all persistence after ParseAsync returns.
/// </para>
/// <para>
/// <strong>Multi-control expansion:</strong> One SARIF result mapping to N controls produces
/// N ComplianceFinding rows. All siblings share the same fingerprint, ruleId, location, and CatTier.
/// </para>
/// <para>
/// Do NOT throw on unmapped rules — accumulate in SarifImportResult (ControlMappings empty list).
/// </para>
/// </remarks>
public interface ISarifParserService
{
    /// <summary>
    /// Parses a SARIF 2.1.0 document, resolves rule descriptors, maps findings to NIST 800-53
    /// controls, derives CAT tiers, and deduplicates against existing records.
    /// </summary>
    /// <param name="sarifPayload">Parsed JsonDocument. Caller retains ownership; do not cache beyond this call.</param>
    /// <param name="systemId">Registered system findings are attributed to.</param>
    /// <param name="pipelineId">CI/CD pipeline identifier — stored on ScanImportRecord.</param>
    /// <param name="pipelineRun">Pipeline run number — used upstream for idempotency dedup.</param>
    /// <param name="existingFingerprints">Pre-fetched set of open fingerprints for this system (bulk dedup).
    /// Pass an empty set when pre-fetch is not possible — parser will fall back to per-result queries.</param>
    /// <returns>
    /// SarifImportResult with all normalized SarifFindingDto records, the CWE→NIST
    /// ControlMappings table, and FindingsImported / FindingsDeduplicated counts.
    /// </returns>
    /// <exception cref="PayloadValidationException">SARIF version ≠ "2.1.0", required fields absent, or invalid JSON structure.</exception>
    /// <exception cref="SystemNotFoundException">System not found.</exception>
    Task<SarifImportResult> ParseAsync(
        JsonDocument sarifPayload,
        Guid systemId,
        string pipelineId,
        string pipelineRun,
        IReadOnlySet<string> existingFingerprints,
        CancellationToken cancellationToken = default);
}
