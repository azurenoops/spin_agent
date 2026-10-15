using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.ProviderAuthorizations;

[ProviderScoped]
public sealed class ProviderOfferingAssociation : ProviderOwnedRow
{
    public Guid ComponentId { get; set; }
    public Guid CapabilityId { get; set; }
    [MaxLength(24)] public string Lifecycle { get; set; } = "Draft";
    public Guid? CurrentRevisionId { get; set; }
}

[ProviderScoped]
public sealed class ProviderOfferingAssociationRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssociationId { get; set; }
    public Guid ProviderId { get; set; }
    public Guid OfferingId { get; set; }
    public Guid ComponentId { get; set; }
    public Guid CapabilityId { get; set; }
    public Guid CapabilityReleaseId { get; set; }
    public long CapabilityReleaseRevision { get; set; }
    public long OfferingRevision { get; set; }
    [ConcurrencyCheck] public long Revision { get; set; } = 1;
    [MaxLength(24)] public string Status { get; set; } = "Draft";
    public string SourceProvenanceJson { get; set; } = "[]";
    public string ProviderCoverageJson { get; set; } = "[]";
    public string CustomerObligationsJson { get; set; } = "[]";
    [MaxLength(24)] public string ContributionRole { get; set; } = "Primary";
    [MaxLength(64)] public string ContentHash { get; set; } = string.Empty;
    [MaxLength(254)] public string Author { get; set; } = string.Empty;
    [MaxLength(254)] public string? Reviewer { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewRationale { get; set; }
    public Guid? SupersedesRevisionId { get; set; }
}
