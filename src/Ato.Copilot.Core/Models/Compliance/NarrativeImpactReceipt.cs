using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>Immutable per-target delivery receipt, including events that create no new proposal.</summary>
[TenantScoped]
[Table("NarrativeImpactReceipts")]
public sealed class NarrativeImpactReceipt
{
    [Key, MaxLength(64)] public string Id { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    [MaxLength(128)] public string ImpactId { get; set; } = string.Empty;
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = string.Empty;
    [MaxLength(20)] public string ControlId { get; set; } = string.Empty;
    [MaxLength(20)] public string NarrativeType { get; set; } = string.Empty;
    public Guid NarrativeProposalId { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(32)] public string? SourceKind { get; set; }
    [MaxLength(200)] public string? SourceId { get; set; }
    [MaxLength(200)] public string? SourceActor { get; set; }
    public string? SourceContextJson { get; set; }
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public NarrativeProposal NarrativeProposal { get; set; } = null!;
}
