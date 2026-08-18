using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Provenance;
using Ato.Copilot.Core.Models.Provenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// Stratified query/aggregation implementation of <see cref="IClassifierPromotionGateService"/> (#2753).
///
/// Reads <c>classifier_shadow_log</c> rows and computes all eight promotion-gate metrics:
/// per-class precision/recall, DeBERTa–LLM agreement stratified by confidence and margin,
/// ECE/reliability curve, routable fraction at candidate (τ, margin) thresholds, and p95 latency.
///
/// This service is strictly read-only — it never writes to or modifies shadow log rows.
/// Uses <see cref="IDbContextFactory{T}"/> to stay singleton-safe (mirrors <see cref="ClassifierShadowLogger"/>).
///
/// Important: Gate 3 (agreement) is skipped with a null result when the LLM comparator is
/// unavailable (#2780 Anthropic credits exhausted) — the caller must signal this by providing
/// only rows whose <c>LlmVerdict</c> is non-empty.  Rows with empty <c>LlmVerdict</c> are
/// excluded from agreement calculations automatically.
/// </summary>
public sealed class ClassifierPromotionGateService : IClassifierPromotionGateService
{
    // Minimum pairs required before gate analysis is valid (#2753 Phase 1).
    private const int MinPairsRequired = 10_000;

    // Confidence buckets for stratified agreement breakdown (Gate 3 / Stage 2).
    private static readonly (double Lo, double Hi, string Label)[] ConfidenceBuckets =
    [
        (0.00, 0.60, "[0.00,0.60)"),
        (0.60, 0.75, "[0.60,0.75)"),
        (0.75, 0.90, "[0.75,0.90)"),
        (0.90, 1.01, "[0.90,1.01)"),
    ];

    // Margin buckets for stratified agreement breakdown.
    private static readonly (double Lo, double Hi, string Label)[] MarginBuckets =
    [
        (0.00, 0.30, "[0.00,0.30)"),
        (0.30, 0.50, "[0.30,0.50)"),
        (0.50, 0.70, "[0.50,0.70)"),
        (0.70, 1.01, "[0.70,1.01)"),
    ];

    // ECE bin count for reliability diagram (10 equal-width bins 0–1).
    private const int EceBins = 10;

    private static readonly string[] AllVerdicts = ["supported", "refuted", "tangential", "insufficient"];

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILogger<ClassifierPromotionGateService> _logger;

    public ClassifierPromotionGateService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILogger<ClassifierPromotionGateService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ClassifierPromotionGateResult> EvaluateAsync(
        double tau = 0.50,
        bool humanAdjudicationAccepted = false,
        double? badgeAccuracyDelta = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Pull all shadow rows into memory. The shadow log is expected to be in the tens of
        // thousands — not large enough to require streaming, but large enough to benefit from
        // AsNoTracking for read-only scenarios.
        var rows = await db.ClassifierShadowLogs
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "[PromotionGate] Loaded {Count} shadow log rows for gate evaluation (τ={Tau}).",
            rows.Count, tau);

        if (rows.Count < MinPairsRequired)
        {
            _logger.LogWarning(
                "[PromotionGate] Insufficient data: {Count} rows (min {Min}). " +
                "Gates cannot be reliably evaluated — telemetry must accumulate before promotion.",
                rows.Count, MinPairsRequired);

            return new ClassifierPromotionGateResult
            {
                TotalPairs = rows.Count,
                SufficientData = false,
                SelectedTau = tau,
                Gate6_HumanAdjudicationAccepted = humanAdjudicationAccepted,
                BadgeAccuracyDelta = badgeAccuracyDelta,
            };
        }

        // ─── Partition into clear-cut and fallback regions ────────────────────
        var clearCut = rows.Where(r => r.DebertaTopMargin >= tau).ToList();
        var fallback = rows.Where(r => r.DebertaTopMargin < tau).ToList();

        // ─── Gate 1: Contradicted-class precision ─────────────────────────────
        // Uses DebertaVerdict vs LlmVerdict as the ground-truth proxy (live traffic).
        // On the gold set this is replaced by human labels — callers can pass in a
        // pre-filtered row set to run against gold data.
        double contradictedPrecision = ComputeContradictedPrecision(clearCut);

        // ─── Gate 2: Mean top-margin in clear-cut region ──────────────────────
        double meanMargin = clearCut.Count > 0
            ? clearCut.Average(r => r.DebertaTopMargin)
            : 0.0;

        // ─── Gate 3: DeBERTa–LLM agreement (skip rows with empty LLM verdict) ─
        // Rows with empty LlmVerdict are excluded — this handles the #2780 outage
        // scenario: if no rows have LLM verdicts, AgreementRate becomes null (deferred).
        var comparableRows = clearCut.Where(r => !string.IsNullOrWhiteSpace(r.LlmVerdict)).ToList();
        double? agreementRate = comparableRows.Count > 0
            ? comparableRows.Count(r => r.DebertaVerdict == r.LlmVerdict) / (double)comparableRows.Count
            : null;

        if (agreementRate is null)
        {
            _logger.LogWarning(
                "[PromotionGate] Gate 3 (agreement) deferred: zero clear-cut rows have an LLM verdict. " +
                "This is expected during an LLM outage (#2780). Agreement metrics are invalid " +
                "and should not be used to make a promotion decision.");
        }

        // ─── Gate 4: Expected Calibration Error ───────────────────────────────
        double ece = ComputeEce(rows);

        // ─── Gate 5: p95 latency ──────────────────────────────────────────────
        double p95 = ComputeP95(rows.Select(r => r.LatencyMs));

        // ─── Gate 8: Routable fraction ────────────────────────────────────────
        double routableFraction = rows.Count > 0
            ? (double)clearCut.Count / rows.Count
            : 0.0;

        // ─── Per-class precision/recall ───────────────────────────────────────
        var (precisionByClass, recallByClass) = ComputePerClassMetrics(rows);

        // ─── Stratified agreement breakdown ──────────────────────────────────
        var agreementByConf = ComputeStratifiedAgreement(
            comparableRows, r => r.DebertaConfidence, ConfidenceBuckets);
        var agreementByMargin = ComputeStratifiedAgreement(
            comparableRows, r => r.DebertaTopMargin, MarginBuckets);

        var result = new ClassifierPromotionGateResult
        {
            TotalPairs = rows.Count,
            SufficientData = true,
            SelectedTau = tau,
            ClearCutPairCount = clearCut.Count,
            FallbackPairCount = fallback.Count,
            ContradictedClassPrecision = contradictedPrecision,
            MeanTopMarginInClearCutRegion = meanMargin,
            AgreementRateInClearCutRegion = agreementRate,
            ExpectedCalibrationError = ece,
            P95LatencyMs = p95,
            Gate6_HumanAdjudicationAccepted = humanAdjudicationAccepted,
            BadgeAccuracyDelta = badgeAccuracyDelta,
            RoutableFraction = routableFraction,
            PrecisionByClass = precisionByClass,
            RecallByClass = recallByClass,
            AgreementByConfidenceBucket = agreementByConf,
            AgreementByMarginBucket = agreementByMargin,
        };

        _logger.LogInformation(
            "[PromotionGate] Evaluation complete.\n{Summary}", result.FormatGateSummary());

        return result;
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Contradicted-class precision: fraction of DeBERTa "refuted"/"insufficient" decisions
    /// in the clear-cut region that match the LLM ground-truth verdict.
    /// </summary>
    private static double ComputeContradictedPrecision(
        IReadOnlyList<ClassifierShadowLog> clearCut)
    {
        var debertaRefuted = clearCut
            .Where(r => r.DebertaVerdict is "refuted" or "insufficient"
                        && !string.IsNullOrWhiteSpace(r.LlmVerdict))
            .ToList();

        if (debertaRefuted.Count == 0) return 0.0;

        var trueRefuted = debertaRefuted.Count(r => r.LlmVerdict is "refuted" or "insufficient");
        return (double)trueRefuted / debertaRefuted.Count;
    }

    /// <summary>
    /// Per-class precision and recall using LlmVerdict as the ground-truth label.
    /// Only rows that have a non-empty LlmVerdict are included.
    /// </summary>
    private static (Dictionary<string, double> Precision, Dictionary<string, double> Recall)
        ComputePerClassMetrics(IReadOnlyList<ClassifierShadowLog> rows)
    {
        var comparable = rows.Where(r => !string.IsNullOrWhiteSpace(r.LlmVerdict)).ToList();

        var precision = new Dictionary<string, double>();
        var recall = new Dictionary<string, double>();

        foreach (var v in AllVerdicts)
        {
            int tp = comparable.Count(r => r.DebertaVerdict == v && r.LlmVerdict == v);
            int fp = comparable.Count(r => r.DebertaVerdict == v && r.LlmVerdict != v);
            int fn = comparable.Count(r => r.DebertaVerdict != v && r.LlmVerdict == v);

            precision[v] = (tp + fp) > 0 ? (double)tp / (tp + fp) : 0.0;
            recall[v]    = (tp + fn) > 0 ? (double)tp / (tp + fn) : 0.0;
        }

        return (precision, recall);
    }

    /// <summary>
    /// Expected Calibration Error (ECE) using <c>DebertaConfidence</c> as the predicted
    /// probability and agreement with <c>LlmVerdict</c> as the empirical accuracy proxy.
    ///
    /// ECE = Σ_b (|B_b| / N) × |acc(B_b) − conf(B_b)|
    /// where bins partition [0,1] by confidence.
    /// </summary>
    private static double ComputeEce(IReadOnlyList<ClassifierShadowLog> rows)
    {
        var comparable = rows.Where(r => !string.IsNullOrWhiteSpace(r.LlmVerdict)).ToList();
        if (comparable.Count == 0) return 0.0;

        double ece = 0.0;
        double binWidth = 1.0 / EceBins;

        for (int i = 0; i < EceBins; i++)
        {
            double lo = i * binWidth;
            double hi = lo + binWidth;
            var bin = comparable
                .Where(r => r.DebertaConfidence >= lo && (i == EceBins - 1 || r.DebertaConfidence < hi))
                .ToList();

            if (bin.Count == 0) continue;

            double avgConf = bin.Average(r => r.DebertaConfidence);
            double avgAcc  = bin.Count(r => r.DebertaVerdict == r.LlmVerdict) / (double)bin.Count;
            ece += ((double)bin.Count / comparable.Count) * Math.Abs(avgAcc - avgConf);
        }

        return ece;
    }

    /// <summary>p95 latency from LatencyMs values.</summary>
    private static double ComputeP95(IEnumerable<long> latencies)
    {
        var sorted = latencies.OrderBy(x => x).ToList();
        if (sorted.Count == 0) return 0.0;
        var idx = (int)Math.Ceiling(0.95 * sorted.Count) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }

    /// <summary>
    /// Agreement rate stratified by a numeric field, using the provided bucket definitions.
    /// </summary>
    private static Dictionary<string, double> ComputeStratifiedAgreement(
        IReadOnlyList<ClassifierShadowLog> comparableRows,
        Func<ClassifierShadowLog, double> selector,
        (double Lo, double Hi, string Label)[] buckets)
    {
        var result = new Dictionary<string, double>();
        foreach (var (lo, hi, label) in buckets)
        {
            var bin = comparableRows
                .Where(r => { var v = selector(r); return v >= lo && v < hi; })
                .ToList();
            if (bin.Count == 0) continue;
            result[label] = bin.Count(r => r.DebertaVerdict == r.LlmVerdict) / (double)bin.Count;
        }
        return result;
    }
}
