using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Join entity linking a <see cref="SecurityCapability"/> to a NIST control
/// with a role and optional system scope.
/// </summary>
[TenantScoped]
public class CapabilityControlMapping
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Unique identifier (GUID).</summary>
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to <see cref="SecurityCapability"/>.</summary>
    [Required]
    [MaxLength(36)]
    public string SecurityCapabilityId { get; set; } = string.Empty;

    /// <summary>NIST control ID (e.g., "AC-2").</summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>FK to RegisteredSystem (null = org-wide mapping).</summary>
    [MaxLength(36)]
    public string? RegisteredSystemId { get; set; }

    /// <summary>Role of this mapping (Primary, Supporting, Shared).</summary>
    [Required]
    public CapabilityMappingRole Role { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who created the mapping.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent capability.</summary>
    public SecurityCapability SecurityCapability { get; set; } = null!;

    /// <summary>Optional parent system scope.</summary>
    public RegisteredSystem? RegisteredSystem { get; set; }

    // ─── Feature 033: Boundary-Scoped Model ──────────────────────────────────

    /// <summary>FK to AuthorizationBoundaryDefinition (nullable — null means org-wide / all boundaries).</summary>
    [MaxLength(36)]
    public string? AuthorizationBoundaryDefinitionId { get; set; }

    /// <summary>Parent boundary definition scope.</summary>
    public AuthorizationBoundaryDefinition? AuthorizationBoundaryDefinition { get; set; }
}
