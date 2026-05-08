using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Sub-grouping inside a tenant (e.g., division, mission program, command).
/// Allows hierarchical scoping below the tenant boundary while still being
/// fully tenant-isolated.
/// See feature 048 spec FR-002 and data-model.md §1.2.
/// </summary>
[TenantScoped(CompositeIndexHint = nameof(Name))]
public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant. Stamped by <c>TenantStampingSaveChangesInterceptor</c>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Human-readable name; unique within tenant.</summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Parent organization (self-reference). Allows shallow hierarchy
    /// (e.g., service → command). Future-proofing; may be null.
    /// </summary>
    public Guid? ParentOrganizationId { get; set; }

    /// <summary>Navigation to the parent organization, if any.</summary>
    public Organization? ParentOrganization { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = "system";

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
