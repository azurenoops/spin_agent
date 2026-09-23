using Ato.Copilot.Core.Services.Workspaces;

namespace Ato.Copilot.Core.Interfaces.Workspaces;

public interface IWorkspaceOperationsService
{
    Task<PagedResult<ProviderCatalogItem>> ListProviderCatalogAsync(WorkspaceCatalogQuery query, CancellationToken ct);
    Task<ProviderCatalogOverview> GetProviderCatalogOverviewAsync(int page, int pageSize, CancellationToken ct);
    Task<ProviderCapabilityDetail?> GetProviderCapabilityAsync(Guid capabilityId, CancellationToken ct);
    Task<PagedResult<ProviderSubscriberSummary>> ListProviderSubscribersAsync(
        Guid capabilityId, int page, int pageSize, CancellationToken ct);
    Task<WorkingRevisionResult?> GetWorkingRevisionAsync(Guid capabilityId, CancellationToken ct);
    Task<WorkingRevisionResult> SaveWorkingRevisionAsync(Guid capabilityId, SaveWorkingRevisionRequest request, string actor, CancellationToken ct);
    Task<PublicationPreviewResult> GeneratePublicationPreviewAsync(Guid capabilityId, long revision, CancellationToken ct);
    Task<WorkingRevisionResult> ApproveWorkingRevisionAsync(
        Guid capabilityId, ApproveWorkingRevisionRequest request, string actor, CancellationToken ct);
    Task<PublishResult> PublishAsync(Guid capabilityId, PublishWorkingRevisionRequest request, string actor, CancellationToken ct);
    Task<PagedResult<OrganizationCatalogItem>> ListOrganizationsAsync(OrganizationCatalogQuery query, CancellationToken ct);
    Task<OrganizationDetail?> GetOrganizationAsync(Guid tenantId, CancellationToken ct);
    Task<CreateWorkspaceOrganizationResult> CreateOrganizationAsync(
        CreateWorkspaceOrganizationRequest request, string idempotencyKey, string actor, CancellationToken ct);
    Task<OrganizationProvisioningResult> GetOrCreateProvisioningAsync(Guid tenantId, string idempotencyKey, CancellationToken ct);
    Task<OrganizationProvisioningResult?> GetProvisioningAsync(
        Guid tenantId, string idempotencyKey, CancellationToken ct);
    Task<OrganizationProvisioningResult?> GetCurrentProvisioningAsync(
        Guid tenantId, CancellationToken ct);
    Task<OrganizationProvisioningResult> UpdateProvisioningAsync(Guid tenantId, Guid operationId, UpdateProvisioningRequest request, CancellationToken ct);
    Task<PagedResult<OrganizationCapabilityItem>> ListOrganizationCapabilitiesAsync(Guid tenantId, WorkspaceCatalogQuery query,
        string? source, string? systemId, IReadOnlyCollection<string> authorizedSystemIds, CancellationToken ct);
    Task<OrganizationCapabilityDetail?> GetOrganizationCapabilityAsync(Guid tenantId, string source, string recordId,
        string? systemId, IReadOnlyCollection<string> authorizedSystemIds, CancellationToken ct,
        string? recordType = null);
    Task<OrganizationCatalogAdditionResult> AddOrganizationCatalogAsync(
        Guid tenantId, OrganizationCatalogAdditionRequest request, string actor, CancellationToken ct);
    Task<PreparedCapabilitySetupResult> PrepareSetupAsync(
        Guid tenantId, PrepareCapabilitySetupRequest request,
        IReadOnlyCollection<string> manageableSystemIds, CancellationToken ct);
    Task<CapabilitySetupResult> CompleteSetupAsync(Guid tenantId, CompleteCapabilitySetupRequest request, string actor,
        IReadOnlyCollection<string> manageableSystemIds, CancellationToken ct);
    Task<CapabilitySetupOperationResult?> GetCapabilitySetupAsync(
        Guid tenantId, Guid operationId, CancellationToken ct);
    Task<OrganizationProvisioningResult> RecordProvisioningFailureAsync(
        Guid tenantId, Guid operationId, string error, CancellationToken ct);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total, string? AggregateState = null);
/// <summary>Provider-owned catalog row with tenant-qualified adoption aggregates.</summary>
public sealed record ProviderCatalogItem(
    string Source, Guid ComponentId, Guid? CapabilityId, string Name, string Description,
    string ComponentName, string ComponentType, string Lifecycle, string ReviewState,
    string SourceFormat, string? SourceReference, int? DistinctAdoptionCount,
    long? WorkingRevision, long? ReleasedRevision,
    IReadOnlyList<SupportingComponentSummary>? SupportingComponents = null,
    int? DistinctOrganizationCount = null, string? WorkingApprovalState = null);
public sealed record WorkingRevisionResult(
    Guid CapabilityId, long Revision, string SnapshotHash, long? ApprovedRevision,
    DateTimeOffset UpdatedAt, IReadOnlyList<string> Contributors, IReadOnlyDictionary<string, string> ControlDuties,
    string Classification = "", string ServiceCategory = "", string ApprovalState = "NotApproved",
    Guid? ApprovedPreviewId = null, string? ApprovedPreviewHash = null,
    DateTimeOffset? ApprovedAt = null, string? ApprovedBy = null);
public sealed record PublicationValueChange(string Value, string ChangeKind);
public sealed record PublicationDutyChange(string Key, string? Before, string? After, string ChangeKind);
public sealed record PublicationDeliveryProjection(
    int ImpactWrites, int DistinctOrganizations, int DistinctSystems);
public sealed record PublicationNotificationProjection(int RecipientCount, int DistinctOrganizations);
public sealed record PublicationAffectedSystem(Guid OrganizationId, string SystemId);
public sealed record PublicationPreviewResult(
    Guid PreviewId, Guid CapabilityId, long Revision, string WorkingSnapshotHash,
    string PreviewHash, DateTimeOffset GeneratedAt, DateTimeOffset ExpiresAt, bool IsStale,
    IReadOnlyList<PublicationValueChange> ContributorChanges,
    IReadOnlyList<PublicationDutyChange> DutyChanges,
    IReadOnlyList<PublicationValueChange> ReferenceChanges,
    IReadOnlyList<Guid> AffectedOrganizations,
    IReadOnlyList<PublicationAffectedSystem> AffectedSystems,
    PublicationDeliveryProjection Delivery,
    PublicationNotificationProjection Notifications);
public sealed record ProviderSubscriberSummary(
    Guid OrganizationId, string OrganizationName, string SystemId, string SystemName,
    string SubscriptionId, string? SourceRevision, string ReviewState);
public sealed record PublishResult(
    Guid ReleaseId, Guid CapabilityId, long Revision, string SnapshotHash,
    DateTimeOffset PublishedAt, int ImpactCount, bool Existing);
public sealed record CreateWorkspaceOrganizationRequest(
    string DisplayName, string? LegalEntityName, string? PrimaryPocName, string? PrimaryPocEmail);
public sealed record CreateWorkspaceOrganizationResult(
    Guid TenantId, Guid OperationId, string DisplayName, string Lifecycle,
    string Onboarding, bool Existing);
public sealed record OrganizationCatalogItem(
    Guid Id, string DisplayName, string Lifecycle, string Onboarding, string ReviewState,
    int SystemCount, int? DistinctAdoptionCount);
public sealed record OrganizationSystemItem(string Id, string Name, string RmfPhase, bool IsActive);
public sealed record OrganizationSubscriptionItem(string Id, string SystemId, string CapabilityId, string? SourceRevision, bool IsActive);
public sealed record OrganizationActivityItem(string Action, DateTimeOffset OccurredAt, string Outcome);
public sealed record OrganizationDetail(
    Guid Id, string DisplayName, string Lifecycle, string Onboarding,
    IReadOnlyList<OrganizationSystemItem> Systems,
    IReadOnlyList<OrganizationSubscriptionItem> Subscriptions,
    IReadOnlyList<OrganizationActivityItem> Activity);
public sealed record OrganizationProvisioningResult(
    Guid OperationId, Guid TenantId, string TenantState, string AdministratorState,
    string MembershipState, string? LastError, string IdempotencyKey = "")
{
    public string OverallState => TenantState == "Completed" && AdministratorState == "Completed"
        && MembershipState == "Completed" ? "Completed" : LastError is null ? "InProgress" : "Failed";
}
public sealed record UpdateProvisioningRequest(
    Guid DirectoryTenantId, Guid ObjectId, Guid PersonId);
public sealed record OrganizationCapabilityItem(
    string Source, string RecordId, string Name, string Description, string Category,
    string Availability, bool IsSubscribed, int SystemCount, string MutationAuthority,
    string RecordType = "capability",
    IReadOnlyList<SupportingComponentSummary>? SupportingComponents = null,
    int? ControlCount = null, string? ReviewState = null, string? Responsibility = null,
    string? SourceName = null, string? OrganizationContribution = null,
    string? OrganizationOwner = null, bool IsOrganizationAdopted = false);
public sealed record SupportingComponentSummary(
    string Id, string Name, string ComponentType, string Source, string? Description = null);
public sealed record CapabilityControlCoverage(
    string ControlId, string Designation, string? RemainingDuty = null, string? SystemId = null);
public sealed record ResponsibilityItem(
    string SystemId, string ControlId, string Designation, string? ConfirmedBy,
    DateTimeOffset? ConfirmedAt, string? SourceRevision);
public sealed record OrganizationCapabilityDetail(
    OrganizationCapabilityItem Capability, IReadOnlyList<ResponsibilityItem> Responsibilities,
    IReadOnlyList<NarrativeReviewItem> NarrativeReviews,
    IReadOnlyList<SupportingComponentSummary>? SupportingComponents = null,
    IReadOnlyList<CapabilityControlCoverage>? ControlCoverage = null,
    string? SourceReference = null, string? ProviderName = null);
public sealed record NarrativeReviewItem(
    Guid Id, string SystemId, string ControlId, string NarrativeType, string Status, int Revision,
    System.Text.Json.JsonElement Provenance, DateTimeOffset CreatedAt, string CreatedBy,
    DateTimeOffset? ReviewedAt, string? ReviewedBy, string? ReviewNote);
public sealed record CompleteCapabilitySetupRequest(
    string IdempotencyKey, string Source, string RecordId, string? SystemId,
    IReadOnlyList<string> ComponentIds, bool Subscribe,
    InlineLocalCapabilityRequest? InlineLocalCapability = null,
    Guid? PreparedOperationId = null);
/// <summary>Immutable intent submitted before capability setup execution.</summary>
public sealed record PrepareCapabilitySetupRequest(
    string IdempotencyKey, string Source, string RecordId, string? SystemId,
    IReadOnlyList<string> ComponentIds, bool Subscribe,
    InlineLocalCapabilityRequest? InlineLocalCapability = null);
public sealed record InlineLocalCapabilityRequest(
    string Name, string Provider, string Category, string Description,
    string ImplementationStatus, string Owner);
public sealed record CapabilitySetupResult(
    Guid OperationId, string RecordState, string ComponentLinksState,
    string SubscriptionState, IReadOnlyList<SetupWriteOutcome> Outcomes, string? LastError);
/// <summary>Immutable persisted setup intent and its current execution state.</summary>
public sealed record CapabilitySetupOperationResult(
    Guid OperationId,
    string IdempotencyKey,
    Guid TenantId,
    string? SystemId,
    string Source,
    string RecordId,
    IReadOnlyList<string> ComponentIds,
    InlineLocalCapabilityRequest? InlineLocalCapability,
    bool SubscribeRequested,
    string RecordState,
    string ComponentLinksState,
    string SubscriptionState,
    IReadOnlyList<SetupWriteOutcome> Outcomes,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
/// <summary>Result of preparing or replaying a capability setup operation.</summary>
public sealed record PreparedCapabilitySetupResult(
    CapabilitySetupOperationResult Operation,
    bool Existing);
public sealed record SetupWriteOutcome(
    string WriteKind, string WriteId, string State, string? Error, DateTimeOffset UpdatedAt);
