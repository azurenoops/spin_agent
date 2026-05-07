namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Assignment of a <see cref="Person"/> to one of the four organization-level RMF roles
/// (Step 2 of the onboarding wizard). Multiple assignments per role are supported with
/// per-role cardinality semantics enforced at service boundaries (FR-022 / FR-023).
/// </summary>
public class OrganizationRoleAssignment
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Role being filled.</summary>
    public OrganizationRole Role { get; set; }

    /// <summary>FK → <see cref="Person.Id"/>.</summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Convenience flag for the first-assigned holder per role; powers default-of-defaults
    /// when downstream services need a single "primary" assignee.
    /// </summary>
    public bool IsPrimary { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }

    /// <summary>UTC timestamp when soft-removed (audit-trail preserved).</summary>
    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>Navigation: assignee.</summary>
    public Person? Person { get; set; }
}

/// <summary>Top-level RMF roles assigned at the organization (tenant) scope.</summary>
public enum OrganizationRole
{
    /// <summary>Information System Security Manager (typically singleton).</summary>
    Issm,
    /// <summary>Information System Security Officer (one or many).</summary>
    Isso,
    /// <summary>Tenant administrator (typically singleton; never zero per FR-002).</summary>
    Administrator,
    /// <summary>Security control assessor (one or many).</summary>
    Assessor,
}
