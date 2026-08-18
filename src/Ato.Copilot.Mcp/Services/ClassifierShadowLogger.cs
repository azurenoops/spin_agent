using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Provenance;
using Ato.Copilot.Core.Models.Provenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// EF Core-backed, append-only implementation of <see cref="IClassifierShadowLogger"/> (#2497, #2753).
///
/// Mirrors the house pattern of <see cref="ModelCallLedger"/>:
/// - Uses <see cref="IDbContextFactory{T}"/> so this singleton-safe service creates a fresh
///   <see cref="AtoCopilotContext"/> per write, matching Program.cs DI conventions.
/// - Never reads, updates, or deletes rows — insert only.
/// - Exceptions are caught, logged, and swallowed so a logging failure never surfaces
///   to the user-facing verification path (R7 from the experiment design).
///
/// Registration is conditional on the <c>FEATURE_CLASSIFIER_SHADOW=true</c> env-var
/// (see Program.cs) so this is a true no-op until #2748/#2749 unblock telemetry.
/// </summary>
public sealed class ClassifierShadowLogger : IClassifierShadowLogger
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILogger<ClassifierShadowLogger> _logger;

    public ClassifierShadowLogger(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILogger<ClassifierShadowLogger> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task LogAsync(ClassifierShadowLog entry, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            db.ClassifierShadowLogs.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogDebug(
                "[ClassifierShadowLogger] Logged pair={PairId} deberta={DebertaVerdict} " +
                "conf={DebertaConfidence:F3} margin={DebertaTopMargin:F3} llm={LlmVerdict} " +
                "latency={LatencyMs}ms slice={TrafficSlice}",
                entry.PairId, entry.DebertaVerdict, entry.DebertaConfidence,
                entry.DebertaTopMargin, entry.LlmVerdict, entry.LatencyMs, entry.TrafficSlice);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logging must never throw — swallow and warn so the verification path is unaffected.
            // Constitution §VI: no silent error swallowing → we log the failure visibly.
            _logger.LogWarning(ex,
                "[ClassifierShadowLogger] Failed to persist shadow log entry pair={PairId}; " +
                "telemetry gap will occur. Verify #2748/#2749 are live.",
                entry.PairId);
        }
    }
}
