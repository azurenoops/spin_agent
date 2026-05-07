using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Per-system commit decision (FR-032..FR-034). The operator chooses for each parsed
/// system whether to merge with an existing system, skip it entirely, or overwrite
/// the existing system's metadata.
/// </summary>
public enum EmassCommitDecision
{
    /// <summary>Skip this system (do not import).</summary>
    Skip = 0,

    /// <summary>Create new or merge into existing system if name+identifier matches.</summary>
    Merge = 1,

    /// <summary>Overwrite existing system metadata; create if not present.</summary>
    Overwrite = 2,
}

/// <summary>
/// Per-system commit instruction. <see cref="SystemIdentifier"/> matches
/// <see cref="EmassParsedSystem.SystemIdentifier"/>.
/// </summary>
public sealed record EmassCommitInstruction(string SystemIdentifier, EmassCommitDecision Decision);

/// <summary>
/// Orchestrates eMASS bulk imports for Step 3 of the onboarding wizard. Manages the
/// upload → async parse → preview → async commit lifecycle, persisting state in
/// <see cref="EmassImportSession"/> rows and dispatching work onto the wizard job
/// queue (research §R7).
/// </summary>
public interface IEmassImportService
{
    /// <summary>
    /// Upload + persist a session and enqueue an <c>EmassParse</c> job.
    /// </summary>
    /// <param name="tenantId">Owning tenant.</param>
    /// <param name="originalFileName">Original client filename (preserved for replay).</param>
    /// <param name="contentType">MIME content type from the upload.</param>
    /// <param name="content">File content stream — service consumes / hashes / persists.</param>
    /// <param name="actorUserId">Acting user.</param>
    /// <param name="correlationId">Correlation id for audit.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The persisted session and the parse job id.</returns>
    Task<(EmassImportSession Session, Guid ParseJobId)> StartParseAsync(
        Guid tenantId,
        string originalFileName,
        string contentType,
        Stream content,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve the parsed preview for a session. Returns <c>null</c> if the session
    /// is still parsing or has failed.
    /// </summary>
    Task<EmassParseResult?> GetPreviewAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Enqueue an <c>EmassCommit</c> job using the operator's per-system instructions
    /// (FR-032..FR-034). Returns the commit job id.
    /// </summary>
    Task<Guid> CommitAsync(
        Guid tenantId,
        Guid sessionId,
        IReadOnlyList<EmassCommitInstruction> instructions,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve the post-commit log for a session — list of created / merged / skipped /
    /// failed systems with the resolved <see cref="Ato.Copilot.Core.Models.Compliance.RegisteredSystem.Id"/>.
    /// </summary>
    Task<EmassImportLog?> GetLogAsync(Guid tenantId, Guid sessionId, CancellationToken ct = default);
}

/// <summary>Per-system commit outcome captured in the post-commit log.</summary>
public sealed record EmassImportLogEntry(
    string SystemIdentifier,
    string SystemName,
    string Outcome,
    string? RegisteredSystemId,
    string? Reason);

/// <summary>Top-level post-commit log returned by <see cref="IEmassImportService.GetLogAsync"/>.</summary>
public sealed record EmassImportLog(
    Guid SessionId,
    EmassImportStatus Status,
    IReadOnlyList<EmassImportLogEntry> Entries);
