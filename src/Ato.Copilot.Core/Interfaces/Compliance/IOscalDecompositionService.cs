namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// AI-assisted OSCAL control statement decomposition (Feature 076 — T012).
/// Segments a free-text narrative into OSCAL statement-level fragments.
/// </summary>
public interface IOscalDecompositionService
{
    /// <summary>
    /// Decompose a control narrative into OSCAL by-component statement fragments.
    /// Stores a pending OscalDecompositionDraft — requires human approval before export.
    /// </summary>
    Task<DecompositionDraftDto> DecomposeAsync(
        string tenantId,
        string systemId,
        string controlId,
        string narrative,
        string requestedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieve the current pending decomposition draft for a control.</summary>
    Task<DecompositionDraftDto?> GetDraftAsync(
        string tenantId,
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve a decomposition draft — writes fragments to ControlImplementation and
    /// marks the draft as Approved.
    /// </summary>
    Task<DecompositionApprovalResult> ApproveAsync(
        string tenantId,
        string systemId,
        string controlId,
        string approvedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Discard the current pending draft without applying it.</summary>
    Task DiscardAsync(
        string tenantId,
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default);
}

// ── DTOs ────────────────────────────────────────────────────────────────────

public record DecompositionDraftDto(
    string DraftId,
    string ControlId,
    string Status,         // "Pending" | "Approved" | "Discarded"
    DateTimeOffset GeneratedAt,
    string GeneratedBy,
    List<DecompositionFragmentDto> Fragments);

public record DecompositionFragmentDto(
    string FragmentId,
    string StatementId,             // e.g. "ac-1_smt.a"
    string? ComponentUuid,
    string Description,
    List<SuggestedParamDto> SuggestedParams,
    double? ConfidenceScore,
    string DerivationBasis,
    bool RequiresHumanValidation);

public record SuggestedParamDto(string ParamId, string Value);

public record DecompositionApprovalResult(
    string DraftId,
    string ControlId,
    int FragmentsApplied,
    DateTimeOffset ApprovedAt);
