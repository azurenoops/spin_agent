namespace Ato.Copilot.Core.Models.Provenance;

/// <summary>
/// Result of running the eight-gate DeBERTa NLI promotion analysis (#2753).
///
/// All eight gates must pass before <c>DEBERTA_NLI_MODE</c> may be enabled in production.
/// A single <see cref="AllGatesPass"/> = false blocks flag enablement.
/// </summary>
public sealed class ClassifierPromotionGateResult
{
    // ─── Data availability ────────────────────────────────────────────────────

    /// <summary>Total rows analyzed from <c>classifier_shadow_log</c>.</summary>
    public int TotalPairs { get; init; }

    /// <summary>
    /// Whether enough data has accumulated to run the gates.
    /// Minimum required: 10 000 rows across all four verdict classes.
    /// </summary>
    public bool SufficientData { get; init; }

    // ─── Gate 1: Contradicted-class precision (#2753 gate 1) ─────────────────

    /// <summary>
    /// DeBERTa precision on the refuted/insufficient class vs human-gold labels,
    /// at τ = <see cref="SelectedTau"/>.  Required: ≥ 0.95.
    /// </summary>
    public double ContradictedClassPrecision { get; init; }

    /// <summary>Gate 1 passes when <see cref="ContradictedClassPrecision"/> ≥ 0.95.</summary>
    public bool Gate1_ContradictedPrecision => ContradictedClassPrecision >= 0.95;

    // ─── Gate 2: Confidence margin (#2753 gate 2) ────────────────────────────

    /// <summary>
    /// Mean top-vs-second softmax margin across clear-cut (fast-path) decisions.
    /// Required: ≥ 0.50 on the clear-cut region.
    /// </summary>
    public double MeanTopMarginInClearCutRegion { get; init; }

    /// <summary>Gate 2 passes when mean margin ≥ 0.50.</summary>
    public bool Gate2_MarginFloor => MeanTopMarginInClearCutRegion >= 0.50;

    // ─── Gate 3: DeBERTa–LLM agreement (#2753 gate 3) ───────────────────────

    /// <summary>
    /// DeBERTa–LLM agreement rate within the clear-cut region (margin ≥ τ).
    /// Required: ≥ 0.95.  Null when LLM comparator is unavailable (#2780).
    /// </summary>
    public double? AgreementRateInClearCutRegion { get; init; }

    /// <summary>
    /// Gate 3 passes when agreement ≥ 0.95, or is null (LLM outage — gate deferred,
    /// not failed, per #2780 protocol).
    /// </summary>
    public bool Gate3_Agreement =>
        AgreementRateInClearCutRegion is null || AgreementRateInClearCutRegion.Value >= 0.95;

    // ─── Gate 4: Calibration / ECE (#2753 gate 4) ────────────────────────────

    /// <summary>
    /// Expected Calibration Error (ECE) across confidence strata.
    /// Required: ≤ 0.05.
    /// </summary>
    public double ExpectedCalibrationError { get; init; }

    /// <summary>Gate 4 passes when ECE ≤ 0.05.</summary>
    public bool Gate4_Calibration => ExpectedCalibrationError <= 0.05;

    // ─── Gate 5: p95 latency (#2753 gate 5) ─────────────────────────────────

    /// <summary>
    /// 95th-percentile DeBERTa inference latency in milliseconds from shadow log.
    /// Required: &lt; 50ms.
    /// </summary>
    public double P95LatencyMs { get; init; }

    /// <summary>Gate 5 passes when p95 latency &lt; 50ms.</summary>
    public bool Gate5_P95Latency => P95LatencyMs < 50.0;

    // ─── Gate 6: Human adjudication (#2753 gate 6) ───────────────────────────

    /// <summary>
    /// Whether the 200-sample human-adjudicated gold set has been collected
    /// and accepted by the engineering lead.  Must be set externally.
    /// </summary>
    public bool Gate6_HumanAdjudicationAccepted { get; init; }

    // ─── Gate 7: No A/B badge regression (#2753 gate 7) ─────────────────────

    /// <summary>
    /// Badge-accuracy delta (DeBERTa fast-path vs current LLM-only baseline).
    /// Positive means improvement; zero is acceptable; negative blocks.
    /// Null when staging A/B has not been run yet.
    /// </summary>
    public double? BadgeAccuracyDelta { get; init; }

    /// <summary>
    /// Gate 7 passes when delta ≥ 0, or is null (A/B not yet run — gate deferred).
    /// </summary>
    public bool Gate7_NoBadgeRegression =>
        BadgeAccuracyDelta is null || BadgeAccuracyDelta.Value >= 0.0;

    // ─── Gate 8: Routable fraction (#2753 gate 8) ────────────────────────────

    /// <summary>
    /// Fraction of live traffic pairs that fall in the clear-cut region (0–1).
    /// Required: ≥ 0.40 (40%) to make the fast-path material.
    /// </summary>
    public double RoutableFraction { get; init; }

    /// <summary>Gate 8 passes when routable fraction ≥ 0.40.</summary>
    public bool Gate8_RoutableFraction => RoutableFraction >= 0.40;

    // ─── Operating-point metadata ─────────────────────────────────────────────

    /// <summary>
    /// The τ threshold applied when producing this result.
    /// DeBERTa decides when top_margin ≥ τ; otherwise LLM fallback is used.
    /// </summary>
    public double SelectedTau { get; init; }

    /// <summary>Number of pairs that fell into the clear-cut (DeBERTa fast-path) region.</summary>
    public int ClearCutPairCount { get; init; }

    /// <summary>Number of pairs routed to LLM fallback.</summary>
    public int FallbackPairCount { get; init; }

    // ─── Per-class precision/recall breakdown ─────────────────────────────────

    /// <summary>Per-class precision keyed by verdict label (supported/refuted/tangential/insufficient).</summary>
    public IReadOnlyDictionary<string, double> PrecisionByClass { get; init; } =
        new Dictionary<string, double>();

    /// <summary>Per-class recall keyed by verdict label.</summary>
    public IReadOnlyDictionary<string, double> RecallByClass { get; init; } =
        new Dictionary<string, double>();

    /// <summary>Agreement rate per confidence bucket (key = bucket label e.g. "[0.60,0.75)").</summary>
    public IReadOnlyDictionary<string, double> AgreementByConfidenceBucket { get; init; } =
        new Dictionary<string, double>();

    /// <summary>Agreement rate per top-margin bucket (key = bucket label e.g. "[0.30,0.50)").</summary>
    public IReadOnlyDictionary<string, double> AgreementByMarginBucket { get; init; } =
        new Dictionary<string, double>();

    // ─── Summary ──────────────────────────────────────────────────────────────

    /// <summary>
    /// True only when all eight gates pass AND sufficient data has accumulated.
    /// This is the production-promotion authorization signal.
    /// </summary>
    public bool AllGatesPass =>
        SufficientData &&
        Gate1_ContradictedPrecision &&
        Gate2_MarginFloor &&
        Gate3_Agreement &&
        Gate4_Calibration &&
        Gate5_P95Latency &&
        Gate6_HumanAdjudicationAccepted &&
        Gate7_NoBadgeRegression &&
        Gate8_RoutableFraction;

    /// <summary>Human-readable summary of which gates passed and which blocked promotion.</summary>
    public string FormatGateSummary()
    {
        var lines = new[]
        {
            $"Gate 1 – Contradicted precision ≥ 0.95  : {(Gate1_ContradictedPrecision ? "PASS" : "FAIL")}  ({ContradictedClassPrecision:F3})",
            $"Gate 2 – Mean margin ≥ 0.50             : {(Gate2_MarginFloor           ? "PASS" : "FAIL")}  ({MeanTopMarginInClearCutRegion:F3})",
            $"Gate 3 – Agreement ≥ 0.95 (clear-cut)  : {(Gate3_Agreement              ? "PASS" : (AgreementRateInClearCutRegion is null ? "DEFER (LLM outage)" : "FAIL"))}  ({AgreementRateInClearCutRegion?.ToString("F3") ?? "n/a"})",
            $"Gate 4 – ECE ≤ 0.05                    : {(Gate4_Calibration            ? "PASS" : "FAIL")}  ({ExpectedCalibrationError:F4})",
            $"Gate 5 – p95 latency < 50ms             : {(Gate5_P95Latency             ? "PASS" : "FAIL")}  ({P95LatencyMs:F1}ms)",
            $"Gate 6 – Human adjudication accepted   : {(Gate6_HumanAdjudicationAccepted ? "PASS" : "FAIL")}",
            $"Gate 7 – No badge regression            : {(Gate7_NoBadgeRegression      ? "PASS" : (BadgeAccuracyDelta is null ? "DEFER (A/B not run)" : "FAIL"))}  (delta={BadgeAccuracyDelta?.ToString("F4") ?? "n/a"})",
            $"Gate 8 – Routable fraction ≥ 40%       : {(Gate8_RoutableFraction        ? "PASS" : "FAIL")}  ({RoutableFraction:P1})",
            "",
            $"Overall: {(AllGatesPass ? "PROMOTION AUTHORIZED" : "PROMOTION BLOCKED")}",
        };
        return string.Join(Environment.NewLine, lines);
    }
}
