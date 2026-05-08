using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Links an org-wide <see cref="SystemComponent"/> to a <see cref="RegisteredSystem"/>
/// with an explicit boundary scope.
/// </summary>
[TenantScoped]
public class ComponentSystemAssignment
{
    /// <summary>
    /// FK to <see cref="Ato.Copilot.Core.Models.Tenancy.Tenant"/> — populated by
    /// <c>TenantStampingSaveChangesInterceptor</c> (Feature 048 FR-021).
    /// </summary>
    public Guid TenantId { get; set; }

    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>FK to the org-wide component.</summary>
    [Required]
    [MaxLength(36)]
    public string SystemComponentId { get; set; } = string.Empty;

    /// <summary>FK to the assigned system.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>FK to the boundary within the system (nullable — null means system-wide / Primary).</summary>
    [MaxLength(36)]
    public string? AuthorizationBoundaryDefinitionId { get; set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User who created the assignment.</summary>
    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = string.Empty;

    // ─── Navigation ──────────────────────────────────────────────────────────

    public SystemComponent SystemComponent { get; set; } = null!;

    public RegisteredSystem RegisteredSystem { get; set; } = null!;

    public AuthorizationBoundaryDefinition? AuthorizationBoundaryDefinition { get; set; }
}
