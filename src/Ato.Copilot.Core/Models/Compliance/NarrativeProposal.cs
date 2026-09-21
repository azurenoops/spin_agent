using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Compliance;

public sealed record GroundedNarrativeDraft(string Narrative, IReadOnlyList<string> Conflicts, IReadOnlyList<string> MissingEvidence);

[TenantScoped]
[Microsoft.EntityFrameworkCore.Index(nameof(TenantId), nameof(DeduplicationKey), IsUnique = true)]
public class NarrativeProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    [MaxLength(36)] public string RegisteredSystemId { get; set; } = string.Empty;
    [MaxLength(20)] public string ControlId { get; set; } = string.Empty;
    [MaxLength(20)] public string NarrativeType { get; set; } = string.Empty;
    public int BaseVersion { get; set; }
    [MaxLength(8000)] public string BeforeContent { get; set; } = string.Empty;
    [MaxLength(8000)] public string ProposedContent { get; set; } = string.Empty;
    [MaxLength(64)] public string StateHash { get; set; } = string.Empty;
    [MaxLength(64)] public string DeduplicationKey { get; set; } = string.Empty;
    public string ProvenanceJson { get; set; } = "{}";
    public string ConflictsJson { get; set; } = "[]";
    public string MissingEvidenceJson { get; set; } = "[]";
    [MaxLength(20)] public string Status { get; set; } = "Draft";
    [ConcurrencyCheck] public int Revision { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)] public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ReviewedAt { get; set; }
    [MaxLength(200)] public string? ReviewedBy { get; set; }
    [MaxLength(2000)] public string? ReviewNote { get; set; }
    public int? AcceptedVersion { get; set; }
}