using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Interfaces.Onboarding;

/// <summary>
/// Per-tenant RMF role-assignment service (FR-020..FR-026). Enforces the
/// last-Administrator invariant (FR-002) and the per-role cardinality rules.
/// </summary>
public interface IOrganizationRoleAssignmentService
{
    /// <summary>List all non-removed role assignments for a tenant.</summary>
    Task<IReadOnlyList<OrganizationRoleAssignment>> ListAsync(
        Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Add a role assignment. The <c>Issm</c> and <c>Administrator</c> roles
    /// emit a warning (via the returned <see cref="RoleAssignmentResult.Warnings"/>)
    /// if a non-removed holder already exists; the operation still succeeds.
    /// </summary>
    Task<RoleAssignmentResult> AddAsync(
        Guid tenantId,
        OrganizationRole role,
        Guid personId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Soft-remove a role assignment (sets <c>RemovedAt</c>). Throws
    /// <see cref="InvalidOperationException"/> with errorCode
    /// <c>WIZARD_LAST_ADMIN_PROTECTED</c> when removing the last
    /// <see cref="OrganizationRole.Administrator"/> (FR-002).
    /// </summary>
    Task RemoveAsync(
        Guid tenantId,
        Guid assignmentId,
        Guid actorUserId,
        Guid correlationId,
        CancellationToken ct = default);
}

/// <summary>Result of an add / replace assignment operation.</summary>
public sealed record RoleAssignmentResult(
    OrganizationRoleAssignment Assignment,
    IReadOnlyList<string> Warnings);
