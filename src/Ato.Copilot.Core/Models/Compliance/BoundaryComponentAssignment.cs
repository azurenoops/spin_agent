using System.ComponentModel.DataAnnotations;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Links a <see cref="SystemComponent"/> to an <see cref="AuthorizationBoundaryDefinition"/>
/// with per-boundary scope status (In Scope or Excluded).
/// Replaces the scope-tracking role of <see cref="AuthorizationBoundary"/>.
/// </summary>
public class BoundaryComponentAssignment
{
    /// <summary>Unique identifier (GUID).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the component.</summary>
    [Required]
    [MaxLength(36)]
    public string SystemComponentId { get; set; } = string.Empty;

    /// <summary>FK to the boundary definition.</summary>
    [Required]
    [MaxLength(36)]
    public string AuthorizationBoundaryDefinitionId { get; set; } = string.Empty;

    /// <summary>true = in scope, false = excluded from boundary.</summary>
    [Required]
    public bool IsInScope { get; set; } = true;

    /// <summary>Required when IsInScope is false. Explains why the component is excluded.</summary>
    [MaxLength(1000)]
    public string? ExclusionRationale { get; set; }

    /// <summary>CSP or common control provider if scope is inherited.</summary>
    [MaxLength(200)]
    public string? InheritanceProvider { get; set; }

    /// <summary>UTC timestamp when the assignment was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who created the assignment.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    /// <summary>Last modification timestamp (UTC).</summary>
    public DateTime? ModifiedAt { get; set; }

    /// <summary>User who last modified the assignment.</summary>
    [MaxLength(200)]
    public string? ModifiedBy { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Linked component.</summary>
    public SystemComponent SystemComponent { get; set; } = null!;

    /// <summary>Linked boundary definition.</summary>
    public AuthorizationBoundaryDefinition AuthorizationBoundaryDefinition { get; set; } = null!;
}
