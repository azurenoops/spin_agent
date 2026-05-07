namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Per-<see cref="Compliance.RegisteredSystem"/> snapshot of an
/// <see cref="OrganizationRoleAssignment"/>. Created when a system is registered
/// to satisfy FR-024 ("organization-level defaults inherited by every system created
/// afterward") and editable independently to satisfy FR-025 ("per-system overrides
/// do not affect the organization-level default").
/// </summary>
public class SystemRoleAssignment
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant (denormalized for fast tenant-scoped queries).</summary>
    public Guid TenantId { get; set; }

    /// <summary>FK → <see cref="Compliance.RegisteredSystem.Id"/> (string GUID).</summary>
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Role being filled.</summary>
    public OrganizationRole Role { get; set; }

    /// <summary>FK → <see cref="Person.Id"/>.</summary>
    public Guid PersonId { get; set; }

    /// <summary>True when this row was copied from a tenant-level
    /// <see cref="OrganizationRoleAssignment"/>; false when the row is an
    /// explicit per-system override (FR-025).</summary>
    public bool IsInherited { get; set; }

    /// <summary>FK → original org-level row when <see cref="IsInherited"/> is true.</summary>
    public Guid? SourceOrganizationRoleAssignmentId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UpdatedBy { get; set; }

    /// <summary>UTC timestamp when soft-removed (audit-trail preserved).</summary>
    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>Navigation: assignee.</summary>
    public Person? Person { get; set; }
}
