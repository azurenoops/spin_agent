namespace Ato.Copilot.Core.Models.Provenance;

/// <summary>
/// Result of routing a single claim–evidence pair through the
/// <see cref="Ato.Copilot.Mcp.Services.ClaimVerifierRouter"/> (#2753).
/// </summary>
public sealed class ClaimVerificationResult
{
    /// <summary>Final verdict: "supported" | "refuted" | "tangential" | "insufficient".</summary>
    public string Verdict { get; init; } = string.Empty;

    /// <summary>Confidence score for the verdict (0–1).</summary>
    public double Confidence { get; init; }

    /// <summary>Which path produced this verdict.</summary>
    public VerificationPath Path { get; init; }

    /// <summary>Wall-clock latency in milliseconds.</summary>
    public long LatencyMs { get; init; }
}

/// <summary>Which code path produced a claim verification result.</summary>
public enum VerificationPath
{
    /// <summary>DeBERTa fast-path (clear-cut: top_margin ≥ τ and DEBERTA_NLI_MODE is ON).</summary>
    DebertaFastPath,

    /// <summary>LLM verifier fallback (below τ, or DEBERTA_NLI_MODE is OFF).</summary>
    LlmFallback,

    /// <summary>
    /// DeBERTa fast-path was attempted but degraded to LLM because the auto-rollback
    /// guard fired (contradicted-class precision or agreement dropped below gate floors).
    /// </summary>
    AutoRollbackToLlm,
}
