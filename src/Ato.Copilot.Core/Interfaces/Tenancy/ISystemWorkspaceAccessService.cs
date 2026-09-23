namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>Operation permissions for one system, independent of browser persona and global role claims.</summary>
public sealed record SystemWorkspacePermissions(
    bool CanRead, bool CanEditProfile, bool CanManageSystem, bool CanAuthorNarratives,
    bool CanReviewNarratives, bool CanManageEvidence, bool CanRunAssessments,
    bool CanManageRemediation, bool CanDecideAuthorization,
    bool CanGenerateSap = false, bool CanFinalizeSap = false, bool CanGenerateSar = false,
    bool CanCreateRemediationTasks = false, bool CanMoveRemediationTasks = false,
    bool CanAssignSystemRoles = false, bool CanManageValidationLinks = false,
    bool CanMoveOwnRemediationTasks = false, bool CanMoveAnyRemediationTasks = false);

/// <summary>Complete applicable role set and action permissions for the requested system.</summary>
public sealed record SystemWorkspaceAccessResponse(
    string SystemId, IReadOnlyList<string> Roles, SystemWorkspacePermissions Permissions)
{
    /// <summary>Exact RMF target roles this actor may assign; Administrator is organization-only.</summary>
    public IReadOnlyList<string> AssignableSystemRoles { get; init; } = [];
}

/// <summary>
/// Shared REST/real-time access boundary. The caller must first bind the authenticated
/// directory identity to its current active membership and pass that Person, never client
/// input. Provider/support oversight must come from a separately authorized server context.
/// </summary>
public interface ISystemWorkspaceAccessService
{
    Task<IReadOnlyList<SystemWorkspaceAccessResponse>> GetAccessBatchAsync(
        Guid tenantId, Guid? personId, IReadOnlyCollection<string> systemIds, bool isCspOversight,
        CancellationToken cancellationToken = default);

    Task<SystemWorkspaceAccessResponse> GetAccessAsync(
        Guid tenantId, Guid? personId, string systemId, bool isCspOversight,
        CancellationToken cancellationToken = default);

    Task<bool> CanReadAsync(
        Guid tenantId, Guid? personId, string systemId, bool isCspOversight,
        CancellationToken cancellationToken = default);
}
