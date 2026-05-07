using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Wizard artifact-dependency service (FR-094 — cascade replace, research §R6). Lets
/// callers declare derivation links, flag dependents stale when a source is replaced,
/// and trigger re-runs for stale dependents.
/// </summary>
public interface IWizardArtifactDependencyService
{
    /// <summary>Declare a derivation: <c>dependent</c> was derived from <c>source</c>.</summary>
    Task<WizardArtifactDependency> LinkAsync(
        Guid tenantId,
        ArtifactSourceKind sourceKind,
        Guid sourceArtifactId,
        string sourceVersionTag,
        ArtifactDependentKind dependentKind,
        Guid dependentId,
        CancellationToken ct = default);

    /// <summary>Flag every dependent of a source as stale (FR-094 cascade).</summary>
    Task<int> FlagDependentsStaleAsync(
        Guid tenantId,
        ArtifactSourceKind sourceKind,
        Guid sourceArtifactId,
        string staleReason,
        CancellationToken ct = default);

    /// <summary>List dependents derived from a single source artifact.</summary>
    Task<IReadOnlyList<WizardArtifactDependency>> ListBySourceAsync(
        Guid tenantId,
        ArtifactSourceKind sourceKind,
        Guid sourceArtifactId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>Trigger a re-run job for a stale dependency. Returns the enqueued job id.</summary>
    Task<Guid?> RerunAsync(
        Guid tenantId,
        Guid dependencyId,
        Guid actorUserId,
        CancellationToken ct = default);
}
