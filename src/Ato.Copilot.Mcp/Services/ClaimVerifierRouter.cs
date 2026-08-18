using System.Diagnostics;
using Ato.Copilot.Core.Interfaces.Provenance;
using Ato.Copilot.Core.Models.Provenance;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// Routes claim–evidence pairs through either the DeBERTa fast-path or the LLM fallback,
/// controlled by the <c>DEBERTA_NLI_MODE</c> environment flag (#2753).
///
/// Routing rules (evaluated in priority order):
/// 1. If auto-rollback guard is active → always LLM fallback (logs reason).
/// 2. If <c>DEBERTA_NLI_MODE</c> is <c>false</c> → always LLM fallback (default, flag OFF).
/// 3. If DeBERTa top_margin ≥ τ → DeBERTa fast-path (clear-cut region).
/// 4. Otherwise → LLM fallback (ambiguous region).
///
/// LLM outage (#2780) handling:
/// - When the LLM comparator is marked unavailable, LLM calls are still attempted for
///   user-facing verification (they must not silently fail).
/// - Agreement/shadow-log LLM comparisons are SKIPPED (recorded as empty LlmVerdict).
/// - If the LLM call fails with <see cref="LlmVerifierUnavailableException"/>, the router
///   marks the comparator unavailable and surfaces the exception to the caller.
///
/// Auto-rollback:
/// - When <see cref="TripAutoRollback"/> is called (by the monitoring job or the
///   <see cref="ClassifierPromotionGateService"/> detecting a gate regression), all traffic
///   immediately reverts to LLM fallback until <see cref="ClearAutoRollback"/> is called
///   by an engineering lead.
/// </summary>
public sealed class ClaimVerifierRouter : IClaimVerifierRouter
{
    /// <summary>
    /// Default τ threshold: DeBERTa decides when top_margin ≥ this value.
    /// Override by setting <c>DEBERTA_NLI_TAU</c> environment variable.
    /// </summary>
    public const double DefaultTau = 0.50;

    private readonly IDeBertaNliVerifier _deberta;
    private readonly ILlmClaimVerifier _llm;
    private readonly IClassifierShadowLogger _shadowLogger;
    private readonly ILogger<ClaimVerifierRouter> _logger;
    private readonly bool _debertaNliModeEnabled;
    private readonly double _tau;

    // Thread-safe flags — volatile is sufficient (single-writer semantics from monitoring).
    private volatile bool _llmComparatorUnavailable;
    private volatile bool _autoRollbackActive;
    private volatile string _autoRollbackReason = string.Empty;

    public ClaimVerifierRouter(
        IDeBertaNliVerifier deberta,
        ILlmClaimVerifier llm,
        IClassifierShadowLogger shadowLogger,
        ILogger<ClaimVerifierRouter> logger,
        bool debertaNliModeEnabled,
        double tau = DefaultTau)
    {
        _deberta = deberta;
        _llm = llm;
        _shadowLogger = shadowLogger;
        _logger = logger;
        _debertaNliModeEnabled = debertaNliModeEnabled;
        _tau = tau;

        if (_debertaNliModeEnabled)
        {
            _logger.LogInformation(
                "[ClaimVerifierRouter] DEBERTA_NLI_MODE is ON (τ={Tau:F2}). " +
                "Clear-cut pairs will route to DeBERTa fast-path; others fall back to LLM.",
                _tau);
        }
        else
        {
            _logger.LogInformation(
                "[ClaimVerifierRouter] DEBERTA_NLI_MODE is OFF. All pairs route to LLM (shadow logging only).");
        }
    }

    /// <inheritdoc />
    public bool IsLlmComparatorAvailable => !_llmComparatorUnavailable;

    /// <inheritdoc />
    public bool IsAutoRollbackActive => _autoRollbackActive;

    /// <inheritdoc />
    public void MarkLlmComparatorUnavailable()
    {
        _llmComparatorUnavailable = true;
        _logger.LogWarning(
            "[ClaimVerifierRouter] LLM comparator marked unavailable (#2780). " +
            "Agreement metrics will not be collected until availability is restored.");
    }

    /// <inheritdoc />
    public void MarkLlmComparatorAvailable()
    {
        _llmComparatorUnavailable = false;
        _logger.LogInformation("[ClaimVerifierRouter] LLM comparator marked available.");
    }

    /// <inheritdoc />
    public void TripAutoRollback(string reason)
    {
        _autoRollbackActive = true;
        _autoRollbackReason = reason;
        _logger.LogCritical(
            "[ClaimVerifierRouter] AUTO-ROLLBACK TRIPPED. All traffic reverted to LLM fallback. " +
            "Reason: {Reason}. DEBERTA_NLI_MODE effectively disabled until rollback is cleared " +
            "by engineering lead.", reason);
    }

    /// <inheritdoc />
    public void ClearAutoRollback()
    {
        _autoRollbackActive = false;
        _autoRollbackReason = string.Empty;
        _logger.LogInformation("[ClaimVerifierRouter] Auto-rollback cleared by engineering lead.");
    }

    /// <inheritdoc />
    public async Task<ClaimVerificationResult> VerifyAsync(
        string claim,
        string evidence,
        CancellationToken cancellationToken = default)
    {
        // ── Step 1: run DeBERTa unconditionally (shadow logging requires the verdict). ──
        // Even when DEBERTA_NLI_MODE is OFF, we run DeBERTa in shadow to accumulate data.
        var sw = Stopwatch.StartNew();
        DebertaNliResult debertaResult = await RunDebertaSafeAsync(claim, evidence, cancellationToken);
        sw.Stop();

        bool isClearCut = debertaResult.TopMargin >= _tau;

        // ── Step 2: determine routing path. ──────────────────────────────────
        VerificationPath path;
        ClaimVerificationResult finalResult;

        if (_autoRollbackActive)
        {
            // Rollback guard tripped — all traffic to LLM.
            path = VerificationPath.AutoRollbackToLlm;
            finalResult = await RunLlmPathAsync(claim, evidence, path, cancellationToken);
        }
        else if (!_debertaNliModeEnabled || !isClearCut)
        {
            // Flag is OFF, or pair is ambiguous — LLM fallback.
            path = VerificationPath.LlmFallback;
            finalResult = await RunLlmPathAsync(claim, evidence, path, cancellationToken);
        }
        else
        {
            // Clear-cut: DeBERTa fast-path.
            path = VerificationPath.DebertaFastPath;
            finalResult = new ClaimVerificationResult
            {
                Verdict    = debertaResult.Verdict,
                Confidence = debertaResult.Confidence,
                Path       = path,
                LatencyMs  = debertaResult.LatencyMs,
            };
        }

        // ── Step 3: shadow log (fire-and-forget — must never throw to caller). ─
        // Skip LLM verdict collection when comparator is unavailable (#2780).
        string llmVerdictForLog = path switch
        {
            VerificationPath.DebertaFastPath when _llmComparatorUnavailable => string.Empty,
            VerificationPath.DebertaFastPath => string.Empty, // LLM not called on fast-path
            _ => finalResult.Verdict,                          // LLM fallback: verdict IS the LLM verdict
        };

        _ = WriteShadowLogFireAndForgetAsync(
            claim, evidence, debertaResult, llmVerdictForLog, finalResult.LatencyMs, cancellationToken);

        _logger.LogDebug(
            "[ClaimVerifierRouter] claim_hash={ClaimLen} evidence_hash={EvidLen} " +
            "deberta={DebertaVerdict}(conf={Conf:F3},margin={Margin:F3}) path={Path} final={FinalVerdict}",
            claim.Length, evidence.Length,
            debertaResult.Verdict, debertaResult.Confidence, debertaResult.TopMargin,
            path, finalResult.Verdict);

        return finalResult;
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task<DebertaNliResult> RunDebertaSafeAsync(
        string claim, string evidence, CancellationToken ct)
    {
        try
        {
            return await _deberta.InferAsync(claim, evidence, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // DeBERTa failure must not surface to the user — degrade gracefully with a
            // low-confidence "insufficient" so the pair is always routed to LLM fallback.
            _logger.LogWarning(ex,
                "[ClaimVerifierRouter] DeBERTa inference failed — degrading to insufficient/0. " +
                "Pair will be routed to LLM fallback.");
            return new DebertaNliResult
            {
                Verdict    = "insufficient",
                Confidence = 0.0,
                TopMargin  = 0.0,
                LatencyMs  = 0,
            };
        }
    }

    private async Task<ClaimVerificationResult> RunLlmPathAsync(
        string claim, string evidence, VerificationPath path, CancellationToken ct)
    {
        try
        {
            var llmSw = Stopwatch.StartNew();
            var llm = await _llm.VerifyAsync(claim, evidence, ct);
            llmSw.Stop();

            return new ClaimVerificationResult
            {
                Verdict    = llm.Verdict,
                Confidence = llm.Confidence ?? 0.0,
                Path       = path,
                LatencyMs  = llmSw.ElapsedMilliseconds,
            };
        }
        catch (LlmVerifierUnavailableException ex)
        {
            // Propagate but also flip the unavailability flag so agreement metrics stop.
            MarkLlmComparatorUnavailable();
            _logger.LogError(ex,
                "[ClaimVerifierRouter] LLM verifier unavailable (#2780). " +
                "Caller will receive exception; agreement metrics suspended.");
            throw;
        }
    }

    private async Task WriteShadowLogFireAndForgetAsync(
        string claim,
        string evidence,
        DebertaNliResult deberta,
        string llmVerdict,
        long totalLatencyMs,
        CancellationToken ct)
    {
        try
        {
            var entry = new ClassifierShadowLog
            {
                PairId          = ComputePairId(claim, evidence),
                ClaimHash       = ComputeHash(claim),
                EvidenceHash    = ComputeHash(evidence),
                DebertaVerdict  = deberta.Verdict,
                DebertaConfidence = deberta.Confidence,
                DebertaTopMargin  = deberta.TopMargin,
                LlmVerdict      = llmVerdict,
                LlmConfidence   = null, // not captured on this path
                LatencyMs       = totalLatencyMs,
                TrafficSlice    = "live",
                Ts              = DateTime.UtcNow,
            };
            await _shadowLogger.LogAsync(entry, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Constitution §VI: no silent swallowing — log visibly.
            _logger.LogWarning(ex,
                "[ClaimVerifierRouter] Shadow log write failed — telemetry gap for this pair. " +
                "Verify #2748/#2749 are live.");
        }
    }

    private static string ComputePairId(string claim, string evidence)
    {
        var combined = $"{ComputeHash(claim)}:{ComputeHash(evidence)}";
        return ComputeHash(combined);
    }

    private static string ComputeHash(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
