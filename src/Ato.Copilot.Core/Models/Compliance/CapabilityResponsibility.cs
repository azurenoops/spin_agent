using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>An explicit system review of one subscribed provider/control contribution.</summary>
[TenantScoped]
public sealed class CapabilityResponsibilityConfirmation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = string.Empty;
    [MaxLength(36)] public string SubscriptionId { get; set; } = string.Empty;
    [MaxLength(36)] public string ReviewedBaselineId { get; set; } = string.Empty;
    [MaxLength(20)] public string ControlId { get; set; } = string.Empty;
    [MaxLength(64)] public string SourceRevision { get; set; } = string.Empty;
    public string SourceSnapshotJson { get; set; } = string.Empty;
    public InheritanceType InheritanceType { get; set; }
    [MaxLength(200)] public string? Provider { get; set; }
    [MaxLength(2000)] public string? CustomerResponsibility { get; set; }
    [MaxLength(200)] public string ConfirmedBy { get; set; } = string.Empty;
    public DateTimeOffset ConfirmedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsCurrent { get; set; } = true;
    public bool? ProviderCoverageVerified { get; set; }
    public bool? CustomerDutiesReviewed { get; set; }
    [MaxLength(2000)] public string? ReviewNotes { get; set; }
}

/// <summary>Ownership and compare-before-write token for a subscription-derived designation.</summary>
[TenantScoped]
public sealed class CapabilityResponsibilityProjection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = string.Empty;
    [MaxLength(36)] public string ControlBaselineId { get; set; } = string.Empty;
    [MaxLength(20)] public string ControlId { get; set; } = string.Empty;
    [MaxLength(36)] public string? ControlInheritanceId { get; set; }
    [MaxLength(64)] public string? AppliedHash { get; set; }
    [MaxLength(64)] public string StateHash { get; set; } = string.Empty;
    public long Revision { get; set; }
}

/// <summary>Durable mark-only review work; retained until the narrative consumer acknowledges it.</summary>
[TenantScoped]
public sealed class CapabilityResponsibilityImpact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = string.Empty;
    [MaxLength(36)] public string ControlBaselineId { get; set; } = string.Empty;
    [MaxLength(20)] public string ControlId { get; set; } = string.Empty;
    public Guid ProjectionId { get; set; }
    public long Revision { get; set; }
    [MaxLength(64)] public string StateHash { get; set; } = string.Empty;
    [MaxLength(40)] public string Reason { get; set; } = string.Empty;
    public string SourcesJson { get; set; } = string.Empty;
    [MaxLength(200)] public string Actor { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? AcknowledgedAt { get; set; }
}

/// <summary>Provider-only durable source revision; contains no customer data or customer authorization.</summary>
[GlobalReference]
public sealed class CspResponsibilitySourceEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CapabilityId { get; set; }
    public Guid ComponentId { get; set; }
    public Guid CspProfileId { get; set; }
    [MaxLength(64)] public string SourceRevision { get; set; } = string.Empty;
    public long Sequence { get; set; }
    [MaxLength(200)] public string? Actor { get; set; }
    public bool IsAvailable { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(36)] public string? LastSubscriptionId { get; set; }
    public bool FanoutCompleted { get; set; }
    public long NextExpansionUtcTicks { get; set; } = DateTime.UtcNow.Ticks;
    public long ExpansionRevision { get; set; }
}

/// <summary>Internal routing-only outbox. Customer state is read only after entering this target tenant.</summary>
[GlobalReference]
public sealed class CapabilityResponsibilityDelivery
{
    [MaxLength(64)] public string Id { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = string.Empty;
    public Guid? SourceEventId { get; set; }
    public Guid? ImpactId { get; set; }
    [MaxLength(36)] public string? SubscriptionId { get; set; }
    [MaxLength(36)] public string? BaselineId { get; set; }
    [MaxLength(20)] public string? ControlCursor { get; set; }
    [MaxLength(40)] public string Outcome { get; set; } = "Pending";
    public long NextAttemptUtcTicks { get; set; } = DateTime.UtcNow.Ticks;
    public long LeaseUntilUtcTicks { get; set; }
    public Guid? LeaseToken { get; set; }
    public int Attempts { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
