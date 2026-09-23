using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Models.Compliance;

public sealed record NarrativeReferenceImpactTarget(string ControlId, string NarrativeType);
public sealed record NarrativeReferencePublicationPayload(
    Guid ReferenceId, Guid? PreviousReferenceId, Guid ReferenceKey, int Version,
    string Scope, string ScopeId, string SourceSha256, IReadOnlyList<NarrativeReferenceImpactTarget> Targets);

/// <summary>Tenant-owned source outbox; publication and this event are persisted in the same save.</summary>
[TenantScoped]
[Table("NarrativeReferencePublications")]
[Index(nameof(ReferenceId), IsUnique = true)]
public sealed class NarrativeReferencePublication
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ReferenceId { get; set; }
    [MaxLength(64)] public string SourceRevision { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    [MaxLength(200)] public string RecordedBy { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    [ConcurrencyCheck] public int Revision { get; set; } = 1;
    public DateTime? DeliveredAt { get; set; }
    [MaxLength(64)] public string? DeliveryErrorCode { get; set; }
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public NarrativeReference Reference { get; set; } = null!;
}

/// <summary>Provider-owned source outbox; contains no customer targets and never bypasses tenant dispatch.</summary>
[GlobalReference]
[Table("ProviderNarrativeReferencePublications")]
[Index(nameof(ReferenceId), IsUnique = true)]
public sealed class ProviderNarrativeReferencePublication
{
    public Guid Id { get; set; }
    public Guid CspProfileId { get; set; }
    public Guid ReferenceId { get; set; }
    [MaxLength(64)] public string SourceRevision { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    [MaxLength(200)] public string RecordedBy { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(20)] public string Status { get; set; } = "Pending";
    [ConcurrencyCheck] public int Revision { get; set; } = 1;
    public DateTime? DeliveredAt { get; set; }
    [MaxLength(64)] public string? DeliveryErrorCode { get; set; }
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public ProviderNarrativeReference Reference { get; set; } = null!;
}
