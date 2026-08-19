namespace Ato.Copilot.Core.Models.Provenance;

/// <summary>
/// Write-only shadow log entry for each DeBERTa NLI + LLM verifier pair evaluation (#2497, #2753).
///
/// One row per claim–evidence pair.  Never updated or deleted — append-only by design.
/// Accumulates until N ≥ 20k rows at which point Stage 1–4 analysis gates (#2753) can be run.
///
/// The production flip (DEBERTA_NLI_MODE) is blocked on #2748/#2749 and remains OFF until
/// all eight promotion gates pass.  This table is instrumentation only.
/// </summary>
public class ClassifierShadowLog
{
    /// <summary>Primary key — auto-generated GUID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Stable identifier for this claim–evidence pair.
    /// Callers should use a deterministic hash of (claim_hash + evidence_hash) so the same
    /// pair is correlated across multiple traffic slices.
    /// </summary>
    public string PairId { get; set; } = string.Empty;

    /// <summary>SHA-256 (lowercase hex) of the normalized claim text.</summary>
    public string ClaimHash { get; set; } = string.Empty;

    /// <summary>SHA-256 (lowercase hex) of the normalized evidence text.</summary>
    public string EvidenceHash { get; set; } = string.Empty;

    /// <summary>
    /// DeBERTa mapped verdict: "supported" | "refuted" | "tangential" | "insufficient".
    /// Label mapping per #2497: entailment→supported, contradiction→refuted,
    /// neutral→tangential (conf > 0.6) | insufficient (conf ≤ 0.6).
    /// </summary>
    public string DebertaVerdict { get; set; } = string.Empty;

    /// <summary>DeBERTa softmax confidence for the winning class (0–1).</summary>
    public double DebertaConfidence { get; set; }

    /// <summary>
    /// Margin between the top-1 and top-2 DeBERTa softmax probabilities.
    /// Promotion gate requires this ≥ 0.5 at the chosen τ threshold.
    /// </summary>
    public double DebertaTopMargin { get; set; }

    /// <summary>LLM verifier verdict for the same pair (ground-truth proxy).</summary>
    public string LlmVerdict { get; set; } = string.Empty;

    /// <summary>LLM verifier confidence score (0–1), when available; null otherwise.</summary>
    public double? LlmConfidence { get; set; }

    /// <summary>
    /// Total wall-clock latency in milliseconds for this pair evaluation
    /// (DeBERTa inference + optional LLM call combined).
    /// Used for p95 latency gate (#2753 gate 6: p95 &lt; 50ms).
    /// </summary>
    public long LatencyMs { get; set; }

    /// <summary>
    /// Traffic slice tag for segmentation, e.g. "live", "replay", "adversarial-eval-b".
    /// Allows splitting the adversarial set B from live-traffic snapshot A in Stage 1.
    /// </summary>
    public string TrafficSlice { get; set; } = "live";

    /// <summary>UTC timestamp when this row was written.</summary>
    public DateTime Ts { get; set; } = DateTime.UtcNow;
}
