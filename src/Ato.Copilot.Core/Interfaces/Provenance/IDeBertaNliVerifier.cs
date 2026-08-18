using Ato.Copilot.Core.Models.Provenance;

namespace Ato.Copilot.Core.Interfaces.Provenance;

/// <summary>
/// DeBERTa NLI inference contract for claim–evidence pair classification (#2497, #2753).
///
/// Implementations run the ECE-calibrated DeBERTa NLI model and return the four-class
/// verdict (supported / refuted / tangential / insufficient) with confidence and margin.
///
/// Label mapping (fixed, per #2497):
///   entailment   → "supported"
///   contradiction → "refuted"
///   neutral (conf > 0.6) → "tangential"
///   neutral (conf ≤ 0.6) → "insufficient"
/// </summary>
public interface IDeBertaNliVerifier
{
    /// <summary>
    /// Run DeBERTa NLI inference on a single claim–evidence pair.
    /// </summary>
    /// <param name="claim">Normalized claim text.</param>
    /// <param name="evidence">Normalized evidence text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DebertaNliResult"/> with the four-class verdict, confidence,
    /// and top-vs-second class margin.
    /// </returns>
    Task<DebertaNliResult> InferAsync(
        string claim,
        string evidence,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a single DeBERTa NLI inference call.
/// </summary>
public sealed class DebertaNliResult
{
    /// <summary>Verdict: "supported" | "refuted" | "tangential" | "insufficient".</summary>
    public string Verdict { get; init; } = string.Empty;

    /// <summary>Softmax confidence for the top class (0–1).</summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Margin = top-class probability − second-class probability.
    /// Gate 2 requires this ≥ 0.50 in the clear-cut region.
    /// </summary>
    public double TopMargin { get; init; }

    /// <summary>Inference latency in milliseconds.</summary>
    public long LatencyMs { get; init; }
}
