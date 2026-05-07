using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>One PDF in a batch upload result.</summary>
public record SspPdfBatchEntry(Guid SessionId, Guid ExtractJobId, string OriginalFileName);

/// <summary>Aggregate result of a batch upload — one job per PDF (FR-040).</summary>
public record SspPdfBatchUploadResult(Guid BatchId, IReadOnlyList<SspPdfBatchEntry> Sessions);

/// <summary>
/// Per-session view used by the dashboard batch summary (FR-041).
/// </summary>
public record SspPdfSessionSummary(
    Guid SessionId,
    string OriginalFileName,
    SspPdfStatus Status,
    SspPdfRejectReason? RejectReason,
    Guid? ExtractJobId,
    Guid? CreatedSystemId);

/// <summary>
/// User correction to apply on top of the extraction result before committing
/// the system (FR-042). Unset fields fall through to the extraction result.
/// </summary>
public record SspPdfFieldCorrection(string FieldName, string? Value);

/// <summary>
/// Orchestrates SSP-PDF batch upload, per-PDF extraction (background job),
/// admin field correction, and final commit-to-system with PDF-source
/// provenance metadata (FR-040..FR-046).
/// </summary>
public interface ISspPdfImportService
{
    /// <summary>Persist + enqueue extraction jobs for each PDF in the batch.</summary>
    Task<SspPdfBatchUploadResult> StartBatchAsync(
        Guid tenantId,
        IReadOnlyList<(string fileName, string contentType, Stream content)> files,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);

    /// <summary>Returns one row per session in the batch.</summary>
    Task<IReadOnlyList<SspPdfSessionSummary>> GetBatchSummaryAsync(
        Guid tenantId, Guid batchId, CancellationToken ct = default);

    /// <summary>Returns the extraction result (deserialised JSON) for a session.</summary>
    Task<SspPdfExtractionResult?> GetExtractionAsync(
        Guid tenantId, Guid sessionId, CancellationToken ct = default);

    /// <summary>Persists user-supplied field corrections (FR-042).</summary>
    Task UpdateCorrectionsAsync(
        Guid tenantId,
        Guid sessionId,
        IReadOnlyList<SspPdfFieldCorrection> corrections,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Commits the corrected extraction to a new <see cref="Ato.Copilot.Core.Models.Compliance.RegisteredSystem"/>
    /// with PDF-source audit metadata (FR-043).
    /// </summary>
    Task<Guid> CommitToSystemAsync(
        Guid tenantId,
        Guid sessionId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);
}
