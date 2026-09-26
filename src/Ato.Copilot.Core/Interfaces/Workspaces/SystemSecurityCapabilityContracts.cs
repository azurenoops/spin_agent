namespace Ato.Copilot.Core.Interfaces.Workspaces;

/// <summary>Independently authorized actions in the selected system.</summary>
public sealed record SystemSecurityCapabilityAccess(bool CanRead, bool CanManage,
    bool CanReviewResponsibilities, bool CanManageEvidence, bool CanAuthorNarratives, bool CanReviewNarratives);
/// <summary>Applied system inventory or eligible library query.</summary>
public sealed record SystemSecurityCapabilityQuery(string Scope = "applied", string Grouping = "capability",
    string? Source = null, string? Search = null, string? ComponentType = null, string? BoundaryId = null,
    string Sort = "name", string Direction = "asc", int Page = 1, int PageSize = 25);
/// <summary>Source-qualified navigable record identity.</summary>
public sealed record SystemCapabilityRecordReference(string Source, string RecordType, string RecordId, string Name);
/// <summary>An actual component assignment, or an explicit absence of assignment.</summary>
public sealed record SystemCapabilityPlacement(string Id, string? BoundaryId, string? BoundaryName, string State, string Revision);
/// <summary>Existing authorized boundary selection.</summary>
public sealed record SystemCapabilityBoundary(string Id, string Name);
/// <summary>Source ownership does not prevent authorized system boundary placement.</summary>
public sealed record SystemComponentPlacementOption(string Id, string? BoundaryId, string? BoundaryName,
    string State, string Revision, bool CanUnassign, string? UnassignBlockedReason);
/// <summary>Current source, relationship tokens and independently authorized placement actions.</summary>
public sealed record SystemComponentPlacementOptions(string Source, string RecordId, string SourceRevision,
    string RelationshipRevision, bool CanAssignBoundary, string? AssignBlockedReason,
    IReadOnlyList<SystemCapabilityBoundary> Boundaries, IReadOnlyList<SystemComponentPlacementOption> Placements);
/// <summary>Assign one actual boundary using the reviewed source and system relationship tokens.</summary>
public sealed record AssignSystemComponentPlacementRequest(string BoundaryId, string SourceRevision, string RelationshipRevision);
/// <summary>Remove only the exact reviewed boundary placement.</summary>
public sealed record UnassignSystemComponentPlacementRequest(string SourceRevision, string RelationshipRevision, string PlacementRevision);
/// <summary>Confirmed placement mutation and refreshed system relationship token.</summary>
public sealed record SystemComponentPlacementResult(string Source, string RecordId, string PlacementId,
    string BoundaryId, string Action, string RelationshipRevision);
/// <summary>A source-owned component with selected-system relationships.</summary>
public sealed record SystemCapabilityComponent(string Source, string RecordType, string RecordId,
    string Name, string Description, string ComponentType, string? SubType, string SourceName,
    string MutationAuthority, string SourceRevision, IReadOnlyList<SystemCapabilityPlacement> Placements,
    IReadOnlyList<SystemCapabilityRecordReference> Capabilities);
/// <summary>A capability or component projected exclusively into one system.</summary>
public sealed record SystemSecurityCapabilityItem(string Source, string RecordType, string RecordId,
    string Name, string Description, string SourceName, string MutationAuthority, string SourceRevision,
    bool IsApplied, bool IsAvailable, string Status, string? ComponentType, string? SubType,
    IReadOnlyList<SystemCapabilityComponent> Components, IReadOnlyList<SystemCapabilityRecordReference> Capabilities,
    IReadOnlyList<SystemCapabilityPlacement> Placements, IReadOnlyList<string> ControlIds, int ReviewRequiredCount);
/// <summary>Filtered and paginated system security capabilities with authorized actions.</summary>
public sealed record SystemSecurityCapabilityPage(IReadOnlyList<SystemSecurityCapabilityItem> Items,
    int Page, int PageSize, int Total, string Scope, string Grouping, SystemSecurityCapabilityAccess Permissions,
    IReadOnlyList<SystemCapabilityBoundary> Boundaries);
/// <summary>Persisted per-control allocation and revision-bound responsibility comparison.</summary>
public sealed record SystemCapabilityControl(string ControlId, string? ProviderCoverage, string? OrganizationDuty,
    string? Allocation, string ReviewState, string? ConfirmedSourceRevision, string AvailableSourceRevision,
    string? ReviewRevision, string? SourceSnapshot, string? ConfirmedSourceSnapshot,
    bool? ProviderCoverageVerified = null, bool? CustomerDutiesReviewed = null, string? ReviewNotes = null);
/// <summary>Safe evidence metadata with an authenticated relative download endpoint.</summary>
public sealed record SystemCapabilityEvidence(string Id, string FileName, string Owner, string Source, string State,
    string? ControlId, string NarrativeType, string OpenUrl);
/// <summary>Source-bound proposal reference with independently evaluated review eligibility.</summary>
public sealed record SystemCapabilityProposal(Guid Id, int Revision, string Status, bool IsStale, bool CanReview,
    string Source, string RecordId);
/// <summary>Independent policy or technical content and freshness.</summary>
public sealed record SystemCapabilityNarrative(string ControlId, string NarrativeType, string? ApprovedContent,
    string? CurrentContent, string ApprovalStatus, string Freshness, int CurrentVersion,
    IReadOnlyList<SystemCapabilityProposal> Proposals, bool CanGenerate, string? BlockedReason);
/// <summary>System capability implementation, responsibility, evidence and narrative projection.</summary>
public sealed record SystemSecurityCapabilityDetail(SystemSecurityCapabilityItem Item,
    SystemSecurityCapabilityAccess Permissions, string? BaselineId, IReadOnlyList<SystemCapabilityControl> Controls,
    IReadOnlyList<SystemCapabilityEvidence> Evidence, IReadOnlyList<SystemCapabilityNarrative> Narratives,
    string RelationshipRevision, string ResponsibilityReviewUrl);
/// <summary>An actual component placement to add, not a capability-level boundary label.</summary>
public sealed record SystemCapabilityPlacementRequest(string Source, string ComponentId, string? BoundaryId);
/// <summary>An existing local capability supporting a provider capability in this system.</summary>
public sealed record SystemSupportingCapabilityRequest(string RecordId, string SourceRevision);
/// <summary>Exact source revision and component placements reviewed by the caller.</summary>
public sealed record SystemCapabilitySelection(string Source, string RecordId, string SourceRevision,
    IReadOnlyList<SystemCapabilityPlacementRequest> Placements,
    IReadOnlyList<SystemSupportingCapabilityRequest> SupportingCapabilities);
/// <summary>Immutable multi-selection setup intent; the route owns the target system.</summary>
public sealed record PrepareSystemCapabilitySetupRequest(string IdempotencyKey, IReadOnlyList<SystemCapabilitySelection> Selections);
/// <summary>Optimistic execution claim over an already prepared immutable operation.</summary>
public sealed record CompleteSystemCapabilitySetupRequest(long ExpectedRevision);
/// <summary>Revision-bound removal preview request.</summary>
public sealed record PrepareSystemCapabilityRemovalRequest(string IdempotencyKey, string SourceRevision, string RelationshipRevision);
/// <summary>One exact relationship or downstream notification in a persisted preview.</summary>
public sealed record SystemCapabilityPlannedWrite(string WriteKind, string WriteId, string Source, string RecordId,
    string? ComponentId, string? BoundaryId, bool AlreadyExists,
    IReadOnlyList<string>? ControlIds = null, IReadOnlyList<string>? NarrativeTypes = null, string? DisplayLabel = null);
/// <summary>Recoverable selected-system execution using the existing capability setup operation store.</summary>
public sealed record SystemCapabilityOperation(Guid OperationId, string IdempotencyKey, Guid TenantId,
    string SystemId, string Kind, string State, long Revision, IReadOnlyList<SystemCapabilitySelection> Selections,
    IReadOnlyList<SystemCapabilityPlannedWrite> PlannedWrites, IReadOnlyList<SetupWriteOutcome> Outcomes,
    string? LastError, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
/// <summary>Prepared operation or stable idempotent replay.</summary>
public sealed record PreparedSystemCapabilityOperation(SystemCapabilityOperation Operation, bool Existing);
/// <summary>A stale or competing selected-system operation requiring an explicit client decision.</summary>
public sealed class SystemCapabilityConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
/// <summary>A durable operation stopped at a recoverable write failure.</summary>
public sealed class SystemCapabilityWriteException(Guid operationId, Exception inner)
    : Exception("A setup write could not be saved. Reload the operation and retry incomplete writes.", inner)
{
    public Guid OperationId { get; } = operationId;
}
