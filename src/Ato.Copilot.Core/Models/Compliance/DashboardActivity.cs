using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>
/// Denormalized recent-event record for fast dashboard activity feed rendering.
/// Captures compliance events as lightweight entries.
/// </summary>
[TenantScoped]
public class DashboardActivity
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

    /// <summary>FK to RegisteredSystem.</summary>
    [Required]
    [MaxLength(36)]
    public string RegisteredSystemId { get; set; } = string.Empty;

    /// <summary>Event category (e.g., "AssessmentCompleted", "NarrativeUpdated").</summary>
    [Required]
    [MaxLength(50)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>UTC event timestamp.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>User or service that triggered the event.</summary>
    [Required]
    [MaxLength(200)]
    public string Actor { get; set; } = string.Empty;

    /// <summary>Human-readable event summary.</summary>
    [Required]
    [MaxLength(500)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Type of related entity (e.g., "ComplianceAssessment", "SecurityCapability").</summary>
    [MaxLength(100)]
    public string? RelatedEntityType { get; set; }

    /// <summary>ID of the related entity.</summary>
    [MaxLength(100)]
    public string? RelatedEntityId { get; set; }

    // ─── Navigation ──────────────────────────────────────────────────────────

    /// <summary>Parent registered system.</summary>
    public RegisteredSystem RegisteredSystem { get; set; } = null!;
}
