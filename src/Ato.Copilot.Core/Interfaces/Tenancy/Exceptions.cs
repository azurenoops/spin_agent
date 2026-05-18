namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Thrown when a request reaches a tenant-scoped code path with no resolvable
/// tenant identity. Mapped to HTTP <c>401 MISSING_TENANT_CLAIM</c>.
/// See feature 048 spec FR-011, edge case "Authenticated user with no tenant claim".
/// </summary>
public sealed class MissingTenantClaimException : Exception
{
    public MissingTenantClaimException()
        : base("The current request has no resolvable tenant identity.")
    {
    }

    public MissingTenantClaimException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when an authenticated user's home tenant is not yet provisioned in
/// the <c>Tenants</c> table and the deployment configuration does not allow
/// self-onboarding. Mapped to HTTP <c>404 TENANT_NOT_PROVISIONED</c>.
/// See feature 048 spec FR-040.
/// </summary>
public sealed class TenantNotProvisionedException : Exception
{
    public TenantNotProvisionedException(Guid entraTenantId)
        : base($"Tenant '{entraTenantId}' is not provisioned in this deployment.")
    {
        EntraTenantId = entraTenantId;
    }

    public Guid EntraTenantId { get; }
}

/// <summary>
/// Thrown when a request targets a tenant whose <c>Status</c> is
/// <c>Suspended</c>. Mapped to HTTP <c>423 TENANT_SUSPENDED</c>.
/// Read endpoints succeed; mutating endpoints reject.
/// See feature 048 spec FR-058.
/// </summary>
public sealed class TenantSuspendedException : Exception
{
    public TenantSuspendedException(Guid tenantId)
        : base($"Tenant '{tenantId}' is suspended; mutating operations are not permitted.")
    {
        TenantId = tenantId;
    }

    public Guid TenantId { get; }
}

/// <summary>
/// Thrown when a request targets a tenant whose <c>Status</c> is
/// <c>Disabled</c>. Mapped to HTTP <c>401 TENANT_DISABLED</c>.
/// All endpoints reject.
/// See feature 048 spec FR-059.
/// </summary>
public sealed class TenantDisabledException : Exception
{
    public TenantDisabledException(Guid tenantId)
        : base($"Tenant '{tenantId}' is disabled.")
    {
        TenantId = tenantId;
    }

    public Guid TenantId { get; }
}

/// <summary>
/// Thrown by <c>TenantStampingSaveChangesInterceptor</c> when an entity being
/// saved would create a cross-tenant reference (mismatched <c>TenantId</c>
/// across an FK navigation, or attempted update of <c>TenantId</c>).
/// Mapped to HTTP <c>409 CROSS_TENANT_REFERENCE_REJECTED</c>.
/// See feature 048 spec FR-021..FR-023 and data-model.md §4.
/// </summary>
public sealed class TenantConsistencyException : Exception
{
    public TenantConsistencyException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Thrown when a non-CSP-Admin attempts a CSP-only operation (e.g.,
/// impersonation, cross-tenant dashboard, status changes).
/// Mapped to HTTP <c>403 FORBIDDEN_NOT_CSP_ADMIN</c>.
/// See feature 048 spec FR-050..FR-056.
/// </summary>
public sealed class NotCspAdminException : Exception
{
    public NotCspAdminException()
        : base("This operation requires the CSP.Admin role.")
    {
    }

    public NotCspAdminException(string message)
        : base(message)
    {
    }
}
