using Ato.Copilot.Core.Models.Provenance;

namespace Ato.Copilot.Core.Interfaces.Provenance;

/// <summary>
/// Routes a claim–evidence pair through either the DeBERTa fast-path or the LLM fallback,
/// depending on the <c>DEBERTA_NLI_MODE</c> environment flag and the confidence-margin
/// of the DeBERTa verdict (#2753).
///
/// Routing rules:
/// - When <c>DEBERTA_NLI_MODE</c> is <c>false</c> (default): always route to LLM.
/// - When <c>DEBERTA_NLI_MODE</c> is <c>true</c>:
///   - If DeBERTa top_margin ≥ τ → DeBERTa fast-path.
///   - Otherwise → LLM fallback.
///   - If the auto-rollback guard has tripped (precision or agreement below floor) → LLM fallback.
///   - If the LLM comparator is unavailable (#2780) → DeBERTa only for clear-cut; others are
///     held/skipped until LLM is available.
/// </summary>
public interface IClaimVerifierRouter
{
    /// <summary>
    /// Verify a single claim–evidence pair and return the routing result.
    /// </summary>
    Task<ClaimVerificationResult> VerifyAsync(
        string claim,
        string evidence,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the LLM comparator is currently available.
    /// When false, agreement metrics are not collected (#2780 protocol).
    /// </summary>
    bool IsLlmComparatorAvailable { get; }

    /// <summary>
    /// Signals that the LLM comparator is unavailable (e.g. #2780 Anthropic credits exhausted).
    /// While this flag is set, agreement metrics are skipped and the router will not attempt
    /// LLM calls for the purpose of shadow comparison.
    /// </summary>
    void MarkLlmComparatorUnavailable();

    /// <summary>Clears the LLM unavailability flag once credits/service is restored.</summary>
    void MarkLlmComparatorAvailable();

    /// <summary>
    /// Signals that the auto-rollback guard has fired.  Once tripped, all traffic is routed
    /// to LLM fallback regardless of DEBERTA_NLI_MODE until the guard is manually cleared.
    /// </summary>
    void TripAutoRollback(string reason);

    /// <summary>Clears the auto-rollback guard (requires engineering-lead sign-off).</summary>
    void ClearAutoRollback();

    /// <summary>True when the auto-rollback guard is currently tripped.</summary>
    bool IsAutoRollbackActive { get; }
}
