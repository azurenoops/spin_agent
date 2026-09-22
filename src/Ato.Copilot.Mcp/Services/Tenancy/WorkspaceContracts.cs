namespace Ato.Copilot.Mcp.Services.Tenancy;

/// <summary>An ordinary authorized landing option; never a support impersonation grant.</summary>
public sealed record WorkspaceOptionResponse(
    string Kind, Guid? TenantId, string DisplayName, string Status, string OnboardingState);

/// <summary>Operations authorized in the selected workspace, independently of RMF approval roles.</summary>
public sealed record WorkspacePermissionsResponse(bool CanManageMemberships, bool CanManageOrganization, bool CanAccessCsp,
    bool CanCreateSystem = false);

/// <summary>Resolved request scope. PersonId is present only for ordinary organization membership.</summary>
public sealed record WorkspaceResponse(
    string Kind, Guid? TenantId, string DisplayName, string Mode, Guid? PersonId,
    string[] Roles, WorkspacePermissionsResponse Permissions);

/// <summary>Explicit directory identity association; assigning membership does not assign an RMF role.</summary>
public sealed record GrantOrganizationMembershipRequest(Guid DirectoryTenantId, Guid ObjectId, Guid PersonId);

/// <summary>Separate CSP-authorized enrollment of the initial organization Administrator.</summary>
public sealed record EnrollOrganizationAdministratorRequest(Guid PersonId);

/// <summary>Persisted organization Administrator assignment, not a membership grant.</summary>
public sealed record OrganizationAdministratorResponse(Guid Id, Guid TenantId, Guid PersonId, string Role);

/// <summary>Persisted access grant including revocation and actor attribution.</summary>
public sealed record OrganizationMembershipResponse(
    Guid Id, Guid TenantId, Guid DirectoryTenantId, Guid ObjectId, Guid PersonId,
    DateTimeOffset GrantedAt, string GrantedBy, DateTimeOffset? RevokedAt, string? RevokedBy);

/// <summary>Paginated membership administration result.</summary>
public sealed record OrganizationMembershipListResponse(IReadOnlyList<OrganizationMembershipResponse> Items, int Total);

/// <summary>Paginated authenticated workspace discovery result.</summary>
public sealed record WorkspaceListResponse(IReadOnlyList<WorkspaceOptionResponse> Items, int Total);

/// <summary>Isolation tenant identity; never an Entra directory or organizational subgroup.</summary>
public sealed record WorkspaceTenantResponse(Guid Id, string DisplayName, string Status);

/// <summary>Validated, time-limited support session for the selected request.</summary>
public sealed record WorkspaceImpersonationResponse(
    WorkspaceTenantResponse ImpersonatedTenant, DateTimeOffset StartedAt, DateTimeOffset ExpiresAt);

/// <summary>An active, time-limited PIM role in the selected tenant.</summary>
public sealed record WorkspacePimRoleResponse(string Name, DateTimeOffset ExpiresAt);

/// <summary>
/// Authenticated workspace descriptor. HomeTenant is informational and nullable. Workspace is
/// null while choosing a context; EffectiveTenant is also null in the provider workspace.
/// DirectoryTenantId identifies the authenticated subject's directory, not an isolation tenant.
/// </summary>
public sealed record WorkspaceMeResponse(
    Guid Oid, Guid DirectoryTenantId, string DisplayName, string Persona,
    WorkspaceTenantResponse? HomeTenant, WorkspaceTenantResponse? EffectiveTenant,
    bool IsImpersonating, WorkspaceImpersonationResponse? Impersonation,
    IReadOnlyList<WorkspacePimRoleResponse> PimRoles, bool IsCspAdmin, bool IsSocAnalyst,
    IReadOnlyList<WorkspaceTenantResponse> TenantMemberships,
    WorkspaceResponse? Workspace, IReadOnlyList<WorkspaceOptionResponse> AvailableWorkspaces,
    int AvailableWorkspacesTotal, WorkspacePermissionsResponse Permissions);

/// <summary>Structured authorization/validation failure safe for an API response.</summary>
public sealed class WorkspaceException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
