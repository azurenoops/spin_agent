namespace Ato.Copilot.Core.Interfaces.Workspaces;

/// <summary>Hosting provider identity and a bounded page of persisted source artifacts.</summary>
public sealed record ProviderCatalogOverview(
    string? ProviderName, PagedResult<ProviderSourceArtifact> SourceArtifacts,
    ProviderAuthorizationRecord? AuthorizationRecord = null);

/// <summary>Separate authorization metadata; unavailable until an actual provider authorization record exists.</summary>
public sealed record ProviderAuthorizationRecord(string Reference, string? Status, DateTimeOffset? ExpiresAt);

/// <summary>Original source provenance, not an inferred authorization or evidence record.</summary>
public sealed record ProviderSourceArtifact(
    Guid ComponentId, string ComponentName, string SourceFormat,
    string? SourceFileName, string? SourceReference);

/// <summary>Direct provider capability read with resolved and unresolved working contributors.</summary>
public sealed record ProviderCapabilityDetail(
    ProviderCatalogItem Capability,
    IReadOnlyList<SupportingComponentSummary> SupportingComponents,
    IReadOnlyList<string> UnresolvedContributorIds,
    IReadOnlyList<ProviderSourceArtifact> SourceArtifacts,
    IReadOnlyList<string> MappedControlIds,
    IReadOnlyList<string>? SourceEvidenceReferences = null,
    string? ImplementationNarrative = null);
