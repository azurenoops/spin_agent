using Ato.Copilot.Core.Models.Provenance;

namespace Ato.Copilot.Core.Interfaces.Provenance;

/// <summary>
/// Write-only shadow logger for DeBERTa NLI classifier evaluations (#2497, #2753).
///
/// Each call to <see cref="LogAsync"/> appends one row to <c>classifier_shadow_log</c>.
/// Implementations MUST be append-only — no updates, no deletes.
///
/// This interface is intentionally minimal: the promotion gate analysis (#2753 Stages 1–4)
/// reads directly from the database, not through this interface.
///
/// Production logging is off by default until telemetry prereqs #2748/#2749 are live.
/// The <c>FEATURE_CLASSIFIER_SHADOW</c> env-var guards registration in Program.cs.
/// </summary>
public interface IClassifierShadowLogger
{
    /// <summary>
    /// Appends a single classifier shadow-log entry.
    /// Fire-and-forget callers should log any exception and continue — a logging
    /// failure must never propagate to the user-facing verification path.
    /// </summary>
    Task LogAsync(ClassifierShadowLog entry, CancellationToken cancellationToken = default);
}
