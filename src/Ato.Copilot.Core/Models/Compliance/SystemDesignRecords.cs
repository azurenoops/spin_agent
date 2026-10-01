using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

[TenantScoped]
public sealed class SystemDesignWorkspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    [ConcurrencyCheck] public long Revision { get; set; }
    public long? ApprovedRevision { get; set; }
    public string GraphJson { get; set; } = "{}";
    [MaxLength(36)] public string? SubmittedBy { get; set; }
}

[TenantScoped]
public sealed class SystemDesignRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    public long Revision { get; set; }
    [MaxLength(32)] public string Action { get; set; } = "";
    [MaxLength(36)] public string Actor { get; set; } = "";
    public DateTimeOffset At { get; set; }
    [MaxLength(2000)] public string Reason { get; set; } = "";
    [MaxLength(32)] public string GovernanceStatus { get; set; } = "";
    [MaxLength(64)] public string SourceFingerprint { get; set; } = "";
    [MaxLength(64)] public string SnapshotHash { get; set; } = "";
    public string GraphJson { get; set; } = "{}";
}

[TenantScoped]
public sealed class SystemDesignLayoutRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    [MaxLength(16)] public string View { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string LayoutJson { get; set; } = "{}";
}
