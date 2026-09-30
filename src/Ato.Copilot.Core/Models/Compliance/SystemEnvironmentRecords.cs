using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

[TenantScoped]
public sealed class SystemProviderScopeSelection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    public Guid AssignmentId { get; set; }
    [ConcurrencyCheck] public long Version { get; set; } = 1;
    [MaxLength(16)] public string State { get; set; } = "Active";
    public string HistoryJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string UpdatedBy { get; set; } = "";
}

[TenantScoped]
public sealed class SystemEnvironmentHostingLinkRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    public Guid AttachmentId { get; set; }
    public Guid AssignmentId { get; set; }
    [ConcurrencyCheck] public long Version { get; set; } = 1;
    [MaxLength(16)] public string State { get; set; } = "Linked";
    [MaxLength(24)] public string Source { get; set; } = "Explicit";
    public string HistoryJson { get; set; } = "[]";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string UpdatedBy { get; set; } = "";
}

[TenantScoped]
public sealed class SystemEnvironmentWorkspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
}

[TenantScoped]
public sealed class SystemEnvironmentAttachmentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    public Guid RegistrationId { get; set; }
    public Guid? AllocationId { get; set; }
    public long? AppliedAllocationVersion { get; set; }
    public Guid? HostingAssignmentId { get; set; }
    [MaxLength(32)] public string Source { get; set; } = "";
    [MaxLength(32)] public string State { get; set; } = "Attached";
    [ConcurrencyCheck] public long Version { get; set; } = 1;
    public string ScopeJson { get; set; } = "{}";
    public string RegistrationSnapshotJson { get; set; } = "{}";
    public string ProvenanceJson { get; set; } = "{}";
    public string HistoryJson { get; set; } = "[]";
    public string AssessmentAccessJson { get; set; } = """{"state":"NotChecked","checkedAt":null,"reason":null}""";
    public string MonitoringAccessJson { get; set; } = """{"state":"NotChecked","checkedAt":null,"reason":null}""";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(254)] public string UpdatedBy { get; set; } = "";
}

/// <summary>Expiring actor-bound discovery and impact material; retained decisions are in attachment history.</summary>
[TenantScoped]
public sealed class SystemEnvironmentPendingOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    [MaxLength(32)] public string Kind { get; set; } = "";
    [MaxLength(254)] public string Actor { get; set; } = "";
    public long Version { get; set; }
    public string MaterialJson { get; set; } = "{}";
    public DateTimeOffset ExpiresAt { get; set; }
}

[TenantScoped]
public sealed class SystemEnvironmentReplay
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    [MaxLength(100)] public string Key { get; set; } = "";
    [MaxLength(254)] public string Actor { get; set; } = "";
    [MaxLength(64)] public string IntentHash { get; set; } = "";
    public string ResponseJson { get; set; } = "{}";
}

[ProviderScoped]
public sealed class ProviderEnvironmentAllocationRecord : ProviderOwnedRow
{
    public Guid RegistrationId { get; set; }
    public Guid RegistrationOwnerTenantId { get; set; }
    public Guid ConsumerTenantId { get; set; }
    public Guid HostingScopeRevisionId { get; set; }
    public string PermittedResourceScopesJson { get; set; } = "[]";
    public string RegistrationSnapshotJson { get; set; } = "{}";
    public string ProvenanceJson { get; set; } = "{}";
    [MaxLength(32)] public string State { get; set; } = "Active";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid? ReplacementAllocationId { get; set; }
    public string HistoryJson { get; set; } = "[]";
}

[ProviderScoped]
public sealed class ProviderEnvironmentAllocationPreview : ProviderOwnedRow
{
    public Guid AllocationId { get; set; }
    public long AllocationVersion { get; set; }
    public string MaterialJson { get; set; } = "{}";
    public string ImpactJson { get; set; } = "{}";
    public DateTimeOffset ExpiresAt { get; set; }
}
