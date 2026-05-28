using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Append-only audit-trail row recording a single state-changing operation
/// on a <see cref="CspInheritedCapability"/>. Feature 050 FR-004 (audit
/// trail entity), FR-005 (drawer History section), FR-014 (pagination
/// contract), FR-015 (retention), FR-016 (Remap audit semantics).
/// </summary>
/// <remarks>
/// <para>
/// History rows are scoped to the CSP tenant performing the operation
/// (FR-013 / R6). They survive capability hard-delete (logical FK with
/// <c>DeleteBehavior.NoAction</c>) and are removed only by tenant
/// offboarding (FK with <c>DeleteBehavior.Cascade</c>) — see
/// <c>AtoCopilotContext.OnModelCreating</c>.
/// </para>
/// <para>
/// The service surface (<c>ICapabilityHistoryService</c>) exposes only
/// <c>AppendAsync</c> and <c>ListAsync</c>; there is intentionally no
/// update or delete operation. Direct-SQL mutation is an out-of-band
/// admin action and is never emitted by the application layer.
/// </para>
/// </remarks>
[TenantScoped]
public class CapabilityHistoryEvent
{
    /// <summary>Surrogate key.</summary>
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Logical FK to <see cref="CspInheritedCapability.Id"/>.
    /// </summary>
    /// <remarks>
    /// Declared as <c>DeleteBehavior.NoAction</c> in <c>OnModelCreating</c>
    /// so a hard-delete of the parent capability does not cascade — the
    /// history row outlives the capability per FR-015 / R9.
    /// </remarks>
    public Guid CapabilityId { get; set; }

    /// <summary>
    /// FK to <c>Tenant.Id</c> (Feature 048). Cascade-deletes on tenant
    /// offboarding per FR-015 / R9.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Which of the six lifecycle events this row records.</summary>
    [Required]
    public CapabilityHistoryEventType EventType { get; set; }

    /// <summary>
    /// Caller's Entra <c>oid</c> claim. Same shape as
    /// <see cref="CspInheritedCapability.ReviewedBy"/>.
    /// </summary>
    [Required, MaxLength(254)]
    public string ActorOid { get; set; } = string.Empty;

    /// <summary>
    /// Server-side UTC timestamp at the moment the row was written.
    /// Defaults to <see cref="DateTimeOffset.UtcNow"/>; the service
    /// layer never accepts a caller-supplied timestamp.
    /// </summary>
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Human-readable description of the event.</summary>
    [Required, MaxLength(500)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Optional structured payload, serialized as JSON. Shape varies by
    /// <see cref="EventType"/> per <c>data-model.md § 1.4</c>. <c>null</c>
    /// when no structured metadata applies; never the string <c>"null"</c>.
    /// </summary>
    [MaxLength(2000)]
    public string? MetadataJson { get; set; }
}
