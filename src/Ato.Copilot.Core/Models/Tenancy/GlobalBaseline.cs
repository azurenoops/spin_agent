using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Feature 048 (T134, FR-081/FR-082): represents a tenant-local row that has
/// been published as a baseline visible to every tenant. The published copy
/// lives in the system tenant; this entity stores the publication metadata
/// and a back-reference to the source row for unpublish + audit.
/// </summary>
/// <remarks>
/// Marked with <see cref="GlobalReferenceAttribute"/> so it is excluded from
/// per-tenant query filters and is readable by every authenticated session.
/// Mutations are gated to CSP-Admin via the endpoint surface
/// (<c>GlobalBaselineEndpoints</c>).
/// </remarks>
[GlobalReference]
public class GlobalBaseline
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>One of <c>ControlNarrative</c> / <c>EvidenceArtifact</c> /
    /// <c>OrgInheritanceDefault</c> per the OpenAPI contract.</summary>
    [Required, MaxLength(64)]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Tenant-local row id this baseline was published from.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Tenant the source row belongs to.</summary>
    public Guid SourceTenantId { get; set; }

    /// <summary>Optional human-readable title.</summary>
    [MaxLength(300)]
    public string? Title { get; set; }

    /// <summary>Optional CSP-Admin notes.</summary>
    [MaxLength(4000)]
    public string? Notes { get; set; }

    /// <summary>UTC publication timestamp.</summary>
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Actor who published the baseline (oid or email).</summary>
    [MaxLength(200)]
    public string PublishedBy { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the baseline was unpublished. NULL until then.</summary>
    public DateTimeOffset? UnpublishedAt { get; set; }

    /// <summary>Actor who unpublished the baseline.</summary>
    [MaxLength(200)]
    public string? UnpublishedBy { get; set; }
}
