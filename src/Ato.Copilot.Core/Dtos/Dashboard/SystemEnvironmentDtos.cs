namespace Ato.Copilot.Core.Dtos.Dashboard;

/// <summary>Read-only projection of the canonical registration; never a new subscription registry.</summary>
public sealed record EnvironmentRegistration(Guid RegistrationId, Guid OwnerTenantId, Guid SubscriptionId,
    Guid DirectoryTenantId, string Cloud, string DisplayName, string Status, DateTimeOffset LastVerifiedAt);
public sealed record EnvironmentProvenance(string Source, string? ExternalId, string? SourceRevision,
    string ReconciliationState, string? EvidenceReference, DateTimeOffset RecordedAt);
public sealed record EnvironmentPermissionFlags(bool CanManageEnvironments, bool CanCheckAccess,
    bool CanRunAssessments, bool CanManageMonitoring, bool CanRegisterSubscriptions);
/// <summary>Access and actual collection are different kinds; absent success/evidence is never inferred.</summary>
public sealed record EnvironmentSourceCheck(string SourceId, string Kind, string State, bool Required,
    DateTimeOffset? AttemptedAt, DateTimeOffset? LastSucceededAt, string? Reason, string? ErrorCode,
    string? SourceRevision, string? EvidenceReference);
public sealed record EnvironmentCheckState(string State, DateTimeOffset? CheckedAt, string? Reason)
{
    public IReadOnlyList<EnvironmentSourceCheck> Sources { get; init; } = [];
}
public sealed record EnvironmentMonitoringState(bool Configured, bool Enabled, string Health,
    DateTimeOffset? EvaluatedAt, string? Reason)
{
    public IReadOnlyList<EnvironmentSourceCheck> Sources { get; init; } = [];
}
public sealed record EnvironmentResource(string ResourceId, string Name, string ResourceType,
    string ResourceGroup, string? Location);
public sealed record EnvironmentExcludedResource(string ResourceId, string Rationale);
public sealed record EnvironmentScope(Guid RevisionId, long Version, string ReviewState,
    IReadOnlyList<string> ResourceIds, IReadOnlyList<EnvironmentExcludedResource> Exclusions,
    IReadOnlyList<string> SharedDependencyResourceIds, DateTimeOffset DiscoveredAt)
{
    public string? ReviewedBy { get; init; }
    public DateTimeOffset? ReviewedAt { get; init; }
}
public sealed record EnvironmentSourceSelection(string Source, Guid RegistrationId,
    Guid? AllocationId, long? ExpectedAllocationVersion);
public sealed record EnvironmentChoice(string ChoiceId, string Source, EnvironmentRegistration Registration,
    Guid? AllocationId, long? AllocationVersion, Guid? OfferingId, string? OfferingName,
    Guid? HostingScopeRevisionId, string? AllocationState, DateTimeOffset? StartsAt,
    DateTimeOffset? ExpiresAt, EnvironmentProvenance Provenance, bool Eligible, string? IneligibleReason)
{
    public string? ProviderName { get; init; }
    public string? ConsumerName { get; init; }
    public string? HostingScopeName { get; init; }
}
public sealed record SystemEnvironmentAttachment(Guid AttachmentId, string SystemId, long Version,
    string Source, EnvironmentRegistration Registration, Guid? AllocationId, long? AllocationVersion,
    Guid? OfferingId, string? OfferingName, Guid? HostingAssignmentId, string HostingReviewState,
    string AttachmentState, EnvironmentScope Scope, EnvironmentCheckState AssessmentAccess,
    EnvironmentCheckState MonitoringAccess, EnvironmentMonitoringState Monitoring,
    EnvironmentCheckState Readiness, EnvironmentProvenance Provenance, DateTimeOffset UpdatedAt)
{
    public string? ProviderName { get; init; }
    public string? ConsumerName { get; init; }
    public string? HostingScopeName { get; init; }
    public Guid? HostingScopeRevisionId { get; init; }
    public string? AllocationState { get; init; }
    public DateTimeOffset? AllocationStartsAt { get; init; }
    public DateTimeOffset? AllocationExpiresAt { get; init; }
}
public sealed record LegacyEnvironmentReference(string ReferenceId, string Kind, string DisplayName,
    string ReconciliationState, string Reason);
public sealed record SystemEnvironmentsResponse(string SystemId, long Version,
    EnvironmentPermissionFlags Permissions, IReadOnlyList<SystemEnvironmentAttachment> Attachments,
    IReadOnlyList<LegacyEnvironmentReference> LegacyReferences)
{
    public IReadOnlyList<SystemProviderScope> ProviderScopes { get; init; } = [];
    public IReadOnlyList<EnvironmentHostingLink> HostingLinks { get; init; } = [];
}
public sealed record SystemProviderScope(Guid AssignmentId, long AssignmentVersion, Guid? RelationshipId,
    Guid OfferingId, string OfferingName, string? ProviderName, Guid HostingScopeRevisionId, string HostingScopeName,
    string State, string RelationshipState, bool ReviewRequired,
    IReadOnlyList<Ato.Copilot.Core.Interfaces.ProviderAuthorizations.ProviderScope> AssignedScopes, long SelectionVersion)
{
    public Guid ProviderId { get; init; }
    public long HostingScopeRevision { get; init; }
    public ProviderScopePublishedDuties PublishedDuties { get; init; } = new("Unavailable", [], "Published duties have not been loaded.");
    public ProviderScopeResponsibilityReview ResponsibilityReview { get; init; } =
        new("Unavailable", false, false, "Responsibility review has not been loaded.");
}
public sealed record SystemProviderScopeChoice(Guid OfferingId, long OfferingVersion, string OfferingName,
    string? ProviderName, Guid HostingScopeRevisionId, string HostingScopeName,
    IReadOnlyList<Ato.Copilot.Core.Interfaces.ProviderAuthorizations.ProviderScope> PermittedScopes,
    IReadOnlyList<Ato.Copilot.Core.Interfaces.ProviderAuthorizations.ProviderHostingExclusion> Exclusions, string EligibilitySource)
{
    public Guid ProviderId { get; init; }
    public long HostingScopeRevision { get; init; }
    public ProviderScopePublishedDuties PublishedDuties { get; init; } = new("Unavailable", [], "Published duties have not been loaded.");
}
public sealed record ProviderScopePublishedDuties(string State, IReadOnlyList<ProviderScopeCapabilityDuties> Capabilities, string? Reason);
public sealed record ProviderScopeCapabilityDuties(Guid CapabilityId, string CapabilityName, string? Description,
    Guid ReleaseId, long ReleaseRevision, string ReleaseSnapshotHash, string ContentHash, Guid ApplicabilityContextId,
    IReadOnlyList<string> ProviderControlIds, IReadOnlyList<string> SharedControlIds, IReadOnlyList<string> CustomerControlIds);
public sealed record ProviderScopeResponsibilityReview(string State, bool CanReview, bool CanConfirm, string? Reason);
public sealed record SystemProviderScopeChoicesResponse(string SystemId, long Version, bool CanManage,
    IReadOnlyList<SystemProviderScopeChoice> Choices);
public sealed record AddSystemProviderScopeRequest(long ExpectedVersion, Guid OfferingId,
    long ExpectedOfferingVersion, Guid HostingScopeRevisionId);
public sealed record PreviewProviderScopeRemovalRequest(long ExpectedVersion, long ExpectedAssignmentVersion,
    long ExpectedSelectionVersion, string Rationale);
public sealed record EnvironmentHostingLink(Guid LinkId, Guid AttachmentId, Guid AssignmentId,
    long Version, string State, string Source, DateTimeOffset UpdatedAt);
public sealed record PreviewEnvironmentHostingLinkRequest(long ExpectedVersion, Guid AttachmentId,
    long ExpectedAttachmentVersion, Guid AssignmentId, long ExpectedAssignmentVersion, string Action, string Rationale);
public sealed record ApplySystemEnvironmentsRequest(long ExpectedVersion, IReadOnlyList<ApplySystemEnvironmentRequest> Items);
public sealed record EnvironmentChoicesResponse(string SystemId, long Version,
    EnvironmentPermissionFlags Permissions, IReadOnlyList<EnvironmentChoice> Choices, string RegistrationHref);
public sealed record DiscoverEnvironmentResourcesRequest(long ExpectedVersion, EnvironmentSourceSelection Selection);
/// <summary>The server retains this expiring discovery snapshot, bound to actor/system/source and authority versions.</summary>
public sealed record EnvironmentDiscoveryResponse(string SystemId, string DiscoveryToken, DateTimeOffset ExpiresAt,
    EnvironmentSourceSelection Selection, IReadOnlyList<EnvironmentResource> Resources, DateTimeOffset DiscoveredAt);
/// <summary>Initial reviewed apply may explicitly link an existing system hosting assignment; null never creates a provider relationship.</summary>
public sealed record ApplySystemEnvironmentRequest(long ExpectedVersion, EnvironmentSourceSelection Selection,
    string DiscoveryToken, IReadOnlyList<string> ResourceIds, IReadOnlyList<EnvironmentExcludedResource> Exclusions,
    IReadOnlyList<string> SharedDependencyResourceIds, Guid? ReuseHostingAssignmentId);
public sealed record EnvironmentScopeChangeRequest(long ExpectedVersion, long ExpectedAttachmentVersion,
    string DiscoveryToken, IReadOnlyList<string> ResourceIds, IReadOnlyList<EnvironmentExcludedResource> Exclusions,
    IReadOnlyList<string> SharedDependencyResourceIds, string Rationale)
{
    /// <summary>Explicitly review the unchanged pending selection; never changes recorded boundaries or approved baselines.</summary>
    public bool ReviewPendingScope { get; init; }
}
public sealed record EnvironmentImpactSystem(string SystemId, string SystemName, Guid AttachmentId,
    long AttachmentVersion, int SelectedResourceCount, bool AssessmentAffected, bool MonitoringAffected);
public sealed record EnvironmentImpactPreview(string PreviewId, string? SystemId, Guid? AllocationId,
    long ExpectedVersion, DateTimeOffset ExpiresAt, IReadOnlyList<EnvironmentImpactSystem> Systems,
    bool RequiresScopeReview, IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<string> Blockers { get; init; } = [];
    public bool CanCommit { get; init; } = true;
}
public sealed record CommitEnvironmentChangeRequest(long ExpectedVersion, string PreviewId,
    string Rationale, bool AcknowledgeImpact);
public sealed record PreviewEnvironmentDetachRequest(long ExpectedVersion, long ExpectedAttachmentVersion,
    string Rationale);
public sealed record CheckEnvironmentAccessRequest(long ExpectedVersion, string Purpose);
public sealed record EnvironmentAccessResponse(string SystemId, long Version,
    IReadOnlyList<SystemEnvironmentAttachment> Attachments);
public sealed record ProviderEnvironmentAllocation(Guid AllocationId, long Version, Guid OfferingId,
    string OfferingName, Guid ConsumerTenantId, string ConsumerName, EnvironmentRegistration Registration,
    Guid HostingScopeRevisionId, IReadOnlyList<string> PermittedResourceScopes, string State,
    DateTimeOffset StartsAt, DateTimeOffset? ExpiresAt, EnvironmentProvenance Provenance, int SystemCount)
{
    public string? ProviderName { get; init; }
    public string? HostingScopeName { get; init; }
}
public sealed record ProviderAllocationConsumer(Guid TenantId, string Name);
public sealed record ProviderAllocationHostingScope(Guid RevisionId, string Name,
    IReadOnlyList<string> PermittedResourceScopes, IReadOnlyList<string> ExcludedResourceScopes);
public sealed record ProviderAllocationChoicesResponse(Guid OfferingId, long OfferingVersion, bool CanManage,
    IReadOnlyList<EnvironmentRegistration> Registrations, IReadOnlyList<ProviderAllocationConsumer> Consumers,
    IReadOnlyList<ProviderAllocationHostingScope> ReleasedScopes, string RegistrationHref);
public sealed record ProviderAllocationsResponse(Guid OfferingId, bool CanManage,
    IReadOnlyList<ProviderEnvironmentAllocation> Allocations);
public sealed record RecordProviderAllocationRequest(long ExpectedOfferingVersion, Guid RegistrationId,
    Guid ConsumerTenantId, Guid HostingScopeRevisionId, IReadOnlyList<string> PermittedResourceScopes,
    DateTimeOffset StartsAt, DateTimeOffset? ExpiresAt, EnvironmentProvenance Provenance);
public sealed record ProviderAllocationUsageResponse(Guid AllocationId, long Version,
    IReadOnlyList<EnvironmentImpactSystem> Systems);
public sealed record PreviewAllocationChangeRequest(long ExpectedVersion, string Action,
    Guid? ReplacementAllocationId, string Rationale);
