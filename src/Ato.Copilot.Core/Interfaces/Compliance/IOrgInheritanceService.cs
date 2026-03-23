using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Interfaces.Compliance;

/// <summary>
/// Service contract for org-level inheritance default derivation, propagation,
/// and revert operations (Feature 044).
/// </summary>
public interface IOrgInheritanceService
{
    /// <summary>
    /// Re-derive all org-level defaults from implemented org-wide capabilities.
    /// Called after capability mutations and available as admin action.
    /// </summary>
    Task<OrgDerivationResult> DeriveOrgDefaultsAsync(
        string derivedBy = "system",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Propagate org-level defaults to a system's baseline.
    /// Sets designations only for controls without existing overrides.
    /// </summary>
    Task<OrgPropagationResult> PropagateToSystemAsync(
        string systemId,
        string baselineId,
        IReadOnlySet<string> baselineControlIds,
        string propagatedBy = "system",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revert specific controls in a system back to current org defaults.
    /// </summary>
    Task<RevertResult> RevertToOrgDefaultsAsync(
        string systemId,
        IReadOnlyList<string> controlIds,
        string revertedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all current org-level defaults with optional filtering.
    /// </summary>
    Task<OrgDefaultsListResult> GetOrgDefaultsAsync(
        string? familyFilter = null,
        string? typeFilter = null,
        string? search = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
}
