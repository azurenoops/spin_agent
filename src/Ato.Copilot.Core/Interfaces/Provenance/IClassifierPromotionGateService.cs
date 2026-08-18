using Ato.Copilot.Core.Models.Provenance;

namespace Ato.Copilot.Core.Interfaces.Provenance;

/// <summary>
/// Runs the eight-gate DeBERTa NLI promotion analysis over <c>classifier_shadow_log</c> data (#2753).
///
/// Callers should await <see cref="EvaluateAsync"/> to obtain a <see cref="ClassifierPromotionGateResult"/>
/// describing which of the eight gates pass.  All eight must pass before <c>DEBERTA_NLI_MODE</c>
/// may be enabled in production.
///
/// This service is read-only with respect to the database; it never modifies shadow log rows.
/// </summary>
public interface IClassifierPromotionGateService
{
    /// <summary>
    /// Runs the full eight-gate evaluation over <c>classifier_shadow_log</c>.
    /// </summary>
    /// <param name="tau">
    /// Confidence-margin threshold that defines the "clear-cut" routing region.
    /// DeBERTa decides when <c>top_margin ≥ tau</c>; else LLM fallback is used.
    /// Defaults to 0.50 (the #2753 reference operating point).
    /// </param>
    /// <param name="humanAdjudicationAccepted">
    /// Whether the 200-sample human-adjudicated gold set has been completed and
    /// accepted by the engineering lead (Gate 6).  This cannot be derived from the
    /// database and must be supplied by the caller.
    /// </param>
    /// <param name="badgeAccuracyDelta">
    /// Badge-accuracy delta from the staging A/B run (Gate 7).  Pass null if the
    /// A/B has not been run yet — the gate will be deferred (not failed).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ClassifierPromotionGateResult"/> with all eight gate outcomes.</returns>
    Task<ClassifierPromotionGateResult> EvaluateAsync(
        double tau = 0.50,
        bool humanAdjudicationAccepted = false,
        double? badgeAccuracyDelta = null,
        CancellationToken cancellationToken = default);
}
