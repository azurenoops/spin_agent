using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.ProviderAuthorizations;

/// <summary>An explicitly approved, immutable customer summary of one retained private artifact.</summary>
[ProviderScoped]
public sealed class ProviderEvidenceShare : ProviderOwnedRow
{
    public Guid EvidenceId { get; set; }
    public long EvidenceRevision { get; set; }
    public Guid AssignmentId { get; set; }
    public long AssignmentRevision { get; set; }
    public Guid TargetTenantId { get; set; }
    [MaxLength(36)] public string SystemId { get; set; } = "";
    public long Version { get; set; }
    public Guid? PreviousVersionId { get; set; }
    [MaxLength(8000)] public string Summary { get; set; } = "";
    [MaxLength(64)] public string SourceSha256 { get; set; } = "";
    public string ContentJson { get; set; } = "";
    [MaxLength(64)] public string ContentHash { get; set; } = "";
    [MaxLength(254)] public string ApprovedBy { get; set; } = "";
    public DateTimeOffset ApprovedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    [MaxLength(254)] public string? RevokedBy { get; set; }
    [MaxLength(2000)] public string? RevocationReason { get; set; }
}
