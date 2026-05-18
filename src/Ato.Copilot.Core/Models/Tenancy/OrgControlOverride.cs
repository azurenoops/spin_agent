using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Per-org override of the CSP-defined defaults for a single NIST 800-53
/// control. Created/edited by org administrators when their system's
/// implementation of a control diverges from the CSP-published baseline.
/// At most one row per (TenantId, ControlId) by uniqueness invariant
/// (enforced by composite unique index on the entity).
/// </summary>
/// <remarks>
/// Per the user's confirmed UX (CSP defines the canonical control set;
/// orgs override only when local reality differs):
///
/// <list type="bullet">
///   <item><see cref="ImplementationStatus"/> records HOW the org implements
///         the control locally (or that it doesn't).</item>
///   <item><see cref="InheritanceApplicability"/> records WHETHER the
///         control inherits from the CSP, applies in part, or is not
///         applicable to this org's system.</item>
/// </list>
///
/// Both fields are nullable because an org may override one dimension
/// without the other (e.g. mark "NotApplicableToThisSystem" without
/// committing to an implementation status). The <see cref="Justification"/>
/// is required by the service-layer validator whenever EITHER override
/// field is set.
///
/// Audit history (who changed what, when, from→to) is captured by the
/// existing <c>AuditLogEntry</c> stream stamped by the
/// <c>AuditLoggingMiddleware</c>; this entity carries only the latest
/// state plus <see cref="UpdatedAt"/>/<see cref="UpdatedBy"/> for
/// dashboard presentation.
/// </remarks>
[TenantScoped(CompositeIndexHint = nameof(ControlId))]
public sealed class OrgControlOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant. Stamped by <c>TenantStampingSaveChangesInterceptor</c>.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// NIST control identifier (e.g. <c>"AC-2"</c>, <c>"SC-7(5)"</c>).
    /// Matches <see cref="NistControl.Id"/> / <see cref="Compliance.FrameworkControl.Id"/>
    /// keys but is not modeled as an FK because the framework catalog can be
    /// re-imported and the override row should survive transient catalog
    /// rebuilds. Validated at the service boundary.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string ControlId { get; set; } = string.Empty;

    /// <summary>
    /// Org-local implementation status, OR <c>null</c> if the org wishes to
    /// inherit the CSP-defined default for this dimension.
    /// </summary>
    public ControlImplementationStatus? ImplementationStatus { get; set; }

    /// <summary>
    /// Org-local inheritance applicability, OR <c>null</c> to inherit the
    /// CSP-defined default for this dimension.
    /// </summary>
    public ControlInheritanceApplicability? InheritanceApplicability { get; set; }

    /// <summary>
    /// Required when either override field is set. Enforced at the service
    /// boundary so the column itself can stay nullable to support legacy
    /// rows (none today, but reserved for future bulk imports).
    /// </summary>
    [MaxLength(2000)]
    public string? Justification { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Required]
    [MaxLength(200)]
    public string CreatedBy { get; set; } = "system";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Required]
    [MaxLength(200)]
    public string UpdatedBy { get; set; } = "system";

    /// <summary>Optimistic-concurrency token (EF Core <c>[Timestamp]</c>).</summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}

/// <summary>
/// Org-side view of a NIST control's implementation lifecycle. The values
/// mirror the <c>implementation-status</c> vocabulary used in NIST
/// 800-53A and OSCAL but are kept as a self-contained enum so the
/// override surface stays decoupled from the broader compliance model.
/// </summary>
public enum ControlImplementationStatus
{
    /// <summary>Implemented as designed.</summary>
    Implemented = 0,

    /// <summary>Implemented in part; remediation in progress.</summary>
    PartiallyImplemented = 1,

    /// <summary>Planned for implementation; design committed.</summary>
    Planned = 2,

    /// <summary>Does not apply to this org's system or is alternate-implemented.</summary>
    NotApplicable = 3,
}

/// <summary>
/// Inheritance posture for the control as it relates to the CSP-published
/// baseline. <see cref="FullyInherited"/> is the assumed default when the
/// org has no override row; <see cref="NotApplicableToThisSystem"/> and
/// <see cref="Hybrid"/> are the explicit deviations.
/// </summary>
public enum ControlInheritanceApplicability
{
    /// <summary>The control fully inherits from the CSP-provided implementation.</summary>
    FullyInherited = 0,

    /// <summary>The org provides part of the implementation; the CSP provides the rest.</summary>
    Hybrid = 1,

    /// <summary>The control does not apply to this org's system at all.</summary>
    NotApplicableToThisSystem = 2,
}
