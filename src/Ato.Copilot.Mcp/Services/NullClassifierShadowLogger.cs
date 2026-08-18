using Ato.Copilot.Core.Interfaces.Provenance;
using Ato.Copilot.Core.Models.Provenance;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// No-op implementation of <see cref="IClassifierShadowLogger"/> used when
/// <c>FEATURE_CLASSIFIER_SHADOW</c> is not set to <c>true</c>.
///
/// Zero overhead — logging calls return immediately without touching the database.
/// Swap for <see cref="ClassifierShadowLogger"/> once #2748/#2749 unblock telemetry.
/// </summary>
public sealed class NullClassifierShadowLogger : IClassifierShadowLogger
{
    /// <inheritdoc />
    public Task LogAsync(ClassifierShadowLog entry, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
