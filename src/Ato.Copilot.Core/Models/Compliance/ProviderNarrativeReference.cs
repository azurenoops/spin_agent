using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Models.Compliance;

/// <summary>Provider-owned reference claims; only published, applicable revisions may cross into customer grounding.</summary>
[GlobalReference]
[Table("ProviderNarrativeReferences")]
[Index(nameof(CspProfileId), nameof(ReferenceKey), nameof(Version), IsUnique = true)]
[Index(nameof(CspProfileId), nameof(Scope), nameof(ScopeId), nameof(Title), nameof(Version), IsUnique = true)]
public sealed class ProviderNarrativeReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CspProfileId { get; set; }
    public Guid ReferenceKey { get; set; } = Guid.NewGuid();
    [MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(20)] public string Scope { get; set; } = "Provider";
    public Guid ScopeId { get; set; }
    [MaxLength(200)] public string SourceName { get; set; } = string.Empty;
    [MaxLength(64)] public string SourceSha256 { get; set; } = string.Empty;
    public string OriginalPassagesJson { get; set; } = "[]";
    public string PassagesJson { get; set; } = "[]";
    public int Version { get; set; } = 1;
    [ConcurrencyCheck] public int Revision { get; set; } = 1;
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(200)] public string CreatedBy { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
    [MaxLength(200)] public string? PublishedBy { get; set; }
    public ICollection<ProviderNarrativeReferencePublication> Publications { get; set; } = new List<ProviderNarrativeReferencePublication>();
}
