using Ato.Copilot.Core.Models.Provenance;

namespace Ato.Copilot.Core.Interfaces.Provenance;

/// <summary>
/// LLM-based claim verifier contract — the existing production path (#2753 fallback).
///
/// This is the current primary verifier.  The <see cref="IClaimVerifierRouter"/> uses it
/// as the fallback for pairs that DeBERTa cannot handle with high confidence (top_margin &lt; τ),
/// and as the sole path when <c>DEBERTA_NLI_MODE</c> is disabled.
/// </summary>
public interface ILlmClaimVerifier
{
    /// <summary>
    /// Run LLM-based verification on a single claim–evidence pair.
    /// </summary>
    /// <param name="claim">Claim text.</param>
    /// <param name="evidence">Evidence text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="LlmVerifierResult"/> with the verdict and confidence.
    /// Implementations should throw <see cref="LlmVerifierUnavailableException"/> when
    /// the LLM service is unreachable (e.g. #2780 credits exhausted).
    /// </returns>
    Task<LlmVerifierResult> VerifyAsync(
        string claim,
        string evidence,
        CancellationToken cancellationToken = default);
}

/// <summary>Result of a single LLM claim verification call.</summary>
public sealed class LlmVerifierResult
{
    /// <summary>Verdict: "supported" | "refuted" | "tangential" | "insufficient".</summary>
    public string Verdict { get; init; } = string.Empty;

    /// <summary>LLM-reported confidence score (0–1), when available.</summary>
    public double? Confidence { get; init; }

    /// <summary>Wall-clock latency in milliseconds.</summary>
    public long LatencyMs { get; init; }
}

/// <summary>
/// Thrown when the LLM verifier service is unavailable (e.g. #2780 Anthropic credits exhausted).
/// The <see cref="IClaimVerifierRouter"/> catches this and marks the comparator as unavailable.
/// </summary>
public sealed class LlmVerifierUnavailableException : Exception
{
    public LlmVerifierUnavailableException(string message, Exception? inner = null)
        : base(message, inner) { }
}
