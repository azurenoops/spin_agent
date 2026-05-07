using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Service that snapshots <see cref="OrganizationRoleAssignment"/> rows into
/// per-system <see cref="SystemRoleAssignment"/> rows when a system is registered
/// (FR-024). Per-system overrides written later (FR-025) coexist with the inherited
/// rows so the original org-level rows remain untouched.
/// </summary>
public interface IRegisteredSystemRoleSnapshotter
{
    /// <summary>
    /// Copy every active organization-level assignment for <paramref name="tenantId"/>
    /// into per-system rows for <paramref name="registeredSystemId"/>.
    /// Idempotent: re-running for the same system is a no-op once snapshots exist.
    /// </summary>
    Task<int> SnapshotAsync(
        Guid tenantId,
        string registeredSystemId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);

    /// <summary>List all active <see cref="SystemRoleAssignment"/> rows for a system
    /// (inherited + override).</summary>
    Task<IReadOnlyList<SystemRoleAssignment>> ListEffectiveAsync(
        string registeredSystemId, CancellationToken ct = default);
}
