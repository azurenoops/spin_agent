using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service for SSP authoring and narrative management.
/// Provides CRUD for per-control implementation narratives, AI-assisted suggestions,
/// batch population for inherited controls, progress tracking, and SSP generation.
/// </summary>
/// <remarks>Feature 015 Phase 7 (US5).</remarks>
public interface ISspService
{
    Task<ControlImplementation> WriteGeneratedNarrativeAsync(
        string systemId,
        string controlId,
        string narrative,
        bool generatedByModel,
        string authoredBy = "mcp-user",
        string? changeReason = null,
        CancellationToken cancellationToken = default,
        int? expectedVersion = null);

    /// <summary>
    /// Write or update the implementation narrative for a control in a system's SSP.
    /// If a narrative already exists for this (systemId, controlId), it is updated.
    /// Creates an immutable NarrativeVersion record on every write.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="controlId">NIST 800-53 control ID (e.g., "AC-1").</param>
    /// <param name="narrative">Implementation narrative text.</param>
    /// <param name="status">Implementation status (Implemented, PartiallyImplemented, Planned, NotApplicable).</param>
    /// <param name="authoredBy">Identity of the user.</param>
    /// <param name="expectedVersion">Optimistic concurrency check (null skips check).</param>
    /// <param name="changeReason">Reason for the edit (stored on NarrativeVersion).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created or updated ControlImplementation.</returns>
    /// <exception cref="InvalidOperationException">System not found or control not in baseline.</exception>
    Task<ControlImplementation> WriteNarrativeAsync(
        string systemId,
        string controlId,
        string narrative,
        string? status = null,
        string authoredBy = "mcp-user",
        int? expectedVersion = null,
        string? changeReason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate an AI-suggested narrative for a control based on system context,
    /// control requirements, inheritance data, and Azure configuration.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="controlId">NIST 800-53 control ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Suggested narrative with confidence score and references.</returns>
    Task<NarrativeSuggestion> SuggestNarrativeAsync(
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Auto-populate narratives for inherited controls using provider templates.
    /// Skips controls that already have narratives (idempotent).
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="inheritanceType">Filter by "Inherited", "Shared", or both (null).</param>
    /// <param name="authoredBy">Identity of the user.</param>
    /// <param name="progress">Optional progress reporter for streaming updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result with counts of populated and skipped controls.</returns>
    Task<BatchPopulateResult> BatchPopulateNarrativesAsync(
        string systemId,
        string? inheritanceType = null,
        string authoredBy = "mcp-user",
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get narrative completion progress for a system's SSP.
    /// Returns per-family and overall completion statistics.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="familyFilter">Optional family prefix filter (e.g., "AC").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Narrative progress with family breakdowns and overall percentage.</returns>
    Task<NarrativeProgress> GetNarrativeProgressAsync(
        string systemId,
        string? familyFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate the System Security Plan (SSP) document for a system.
    /// Produces a Markdown document with system information, categorization,
    /// control baseline, and per-control implementation narratives.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="format">Output format: "markdown" (default) or "docx".</param>
    /// <param name="sections">Specific sections to include (null for all).</param>
    /// <param name="progress">Optional progress reporter for streaming updates.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Generated SSP document with content, counts, and warnings.</returns>
    Task<SspDocument> GenerateSspAsync(
        string systemId,
        string format = "markdown",
        IEnumerable<string>? sections = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams SSP document sections incrementally, yielding each section's content
    /// independently without buffering the entire document. Useful for large SSP generation
    /// where clients can render sections as they arrive via SSE.
    /// </summary>
    /// <param name="systemId">RegisteredSystem ID.</param>
    /// <param name="sections">Specific sections to include (null for all).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of (sectionNumber, content) tuples.</returns>
    IAsyncEnumerable<(int SectionNumber, string Content)> StreamSspSectionsAsync(
        string systemId,
        IEnumerable<string>? sections = null,
        CancellationToken cancellationToken = default);

    // ─── SSP Section Management (Feature 022) ───────────────────────────────

    /// <summary>
    /// Write or update an individual SSP section (NIST 800-18 §1–§13).
    /// Creates a new section if none exists; updates and resets status on subsequent writes.
    /// </summary>
    /// <param name="registeredSystemId">RegisteredSystem ID.</param>
    /// <param name="sectionNumber">Section number (1–13).</param>
    /// <param name="content">Markdown content (required for authored sections).</param>
    /// <param name="authoredBy">Identity of the user.</param>
    /// <param name="expectedVersion">Optimistic concurrency check (null skips check).</param>
    /// <param name="submitForReview">If true, transitions Draft→UnderReview after writing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created or updated SspSection.</returns>
    Task<SspSection> WriteSspSectionAsync(
        string registeredSystemId,
        int sectionNumber,
        string? content,
        string authoredBy,
        int? expectedVersion = null,
        bool submitForReview = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Review (approve or request revision) an SSP section currently in UnderReview status.
    /// </summary>
    /// <param name="registeredSystemId">RegisteredSystem ID.</param>
    /// <param name="sectionNumber">Section number (1–13).</param>
    /// <param name="decision">"approve" or "request_revision".</param>
    /// <param name="reviewer">Identity of the reviewer.</param>
    /// <param name="comments">Reviewer comments (required for request_revision).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated SspSection.</returns>
    Task<SspSection> ReviewSspSectionAsync(
        string registeredSystemId,
        int sectionNumber,
        string decision,
        string reviewer,
        string? comments = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get SSP section completeness status for a system across all 13 NIST 800-18 sections.
    /// </summary>
    /// <param name="registeredSystemId">RegisteredSystem ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completeness report with per-section summaries and blocking issues.</returns>
    Task<SspCompletenessReport> GetSspCompletenessAsync(
        string registeredSystemId,
        CancellationToken cancellationToken = default);
}
