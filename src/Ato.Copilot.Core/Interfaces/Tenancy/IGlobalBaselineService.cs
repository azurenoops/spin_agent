using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Feature 048 (T134, FR-081/FR-082): publishes a tenant-local row as a
/// global baseline (visible to every tenant via <c>[GlobalReference]</c>) and
/// supports unpublishing it.
/// </summary>
public interface IGlobalBaselineService
{
    /// <summary>
    /// Lists currently-published baselines, optionally filtered by kind.
    /// </summary>
    Task<IReadOnlyList<GlobalBaseline>> ListAsync(
        string? kind,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a single published baseline by id.
    /// </summary>
    Task<GlobalBaseline?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Publishes <paramref name="sourceId"/> from the active tenant as a
    /// baseline of the given <paramref name="kind"/>. Emits an audit row.
    /// </summary>
    /// <returns>The persisted <see cref="GlobalBaseline"/>.</returns>
    /// <exception cref="ArgumentException">When <paramref name="kind"/> is unsupported or <paramref name="sourceId"/> is empty.</exception>
    Task<GlobalBaseline> PublishAsync(
        string kind,
        Guid sourceId,
        string? title,
        string? notes,
        string actor,
        CancellationToken cancellationToken);

    /// <summary>
    /// Unpublishes a baseline. Sets <c>UnpublishedAt</c> + <c>UnpublishedBy</c>
    /// (logical delete) and emits an audit row. Idempotent — returns true on
    /// first call, false on subsequent calls or unknown id.
    /// </summary>
    Task<bool> UnpublishAsync(
        Guid id,
        string actor,
        CancellationToken cancellationToken);
}
