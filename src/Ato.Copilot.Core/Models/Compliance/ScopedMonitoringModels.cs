using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

public sealed record MonitoringScopeResource(string AssignmentId, string BoundaryId, string? ComponentId,
    string? ResourceId, Guid? ProviderComponentId);

[TenantScoped]
public sealed class ScopedMonitoringObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = "";
    [MaxLength(64)] public string SourceId { get; set; } = "";
    [MaxLength(64)] public string Fingerprint { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
    public DateTimeOffset AttributedAt { get; set; } = DateTimeOffset.UtcNow;
}

[TenantScoped]
public sealed class MonitoringRuleEvaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid RuleId { get; set; }
    public long RuleVersion { get; set; }
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = "";
    [MaxLength(64)] public string InputFingerprint { get; set; } = "";
    [MaxLength(32)] public string Outcome { get; set; } = "";
    public string RuleSnapshotJson { get; set; } = "";
    public string InputSnapshotJson { get; set; } = "";
    public DateTimeOffset EvaluatedAt { get; set; } = DateTimeOffset.UtcNow;
}

[TenantScoped]
public sealed class MonitoringImpactReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid EvaluationId { get; set; }
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = "";
    [MaxLength(20)] public string? ControlId { get; set; }
    [MaxLength(200)] public string OwnerId { get; set; } = "";
    [MaxLength(32)] public string Disposition { get; set; } = "Pending";
    public string? Rationale { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public long Version { get; set; } = 1;
    public string AffectedRecordsJson { get; set; } = "[]";
    public string NarrativeProposalIdsJson { get; set; } = "[]";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
