using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Interfaces.Workspaces;

public partial interface IWorkspaceOperationsService
{
    Task<OrganizationOnboardingDraftResult> SaveOrganizationDraftAsync(Guid id, SaveOrganizationDraftRequest request, string actor, CancellationToken ct);
    Task<OrganizationOnboardingDraftResult?> GetOrganizationDraftAsync(Guid id, CancellationToken ct);
    Task<PagedResult<OrganizationOnboardingDraftResult>> ListOrganizationDraftsAsync(int page, int pageSize, CancellationToken ct);
    Task<OrganizationOnboardingDraftResult> ConfirmOrganizationDraftAsync(Guid id, ConfirmOrganizationDraftRequest request, string actor, CancellationToken ct);
    Task<OrganizationOnboardingDraftResult> DiscardOrganizationDraftAsync(Guid id, long expectedRevision, string actor, CancellationToken ct);
    Task<OrganizationSetupSummary> GetOrganizationSetupSummaryAsync(Guid tenantId, Guid? operationId, int page, int pageSize, Guid directoryId, Guid objectId, CancellationToken ct);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrganizationDraftValues(
    string OrganizationChoice = "create", Guid? ExistingTenantId = null,
    string? DisplayName = null, string? LegalEntityName = null, string? PrimaryPocName = null,
    string? PrimaryPocEmail = null, string? AdministratorChoice = null,
    OrganizationDraftAdministrator? Administrator = null, OrganizationDraftDiscovery? Discovery = null,
    string? DeferralReason = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrganizationDraftAdministrator(
    Guid? DirectoryTenantId = null, Guid? ObjectId = null, Guid? PersonId = null,
    NewAdministratorPersonRequest? NewPerson = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OrganizationDraftDiscovery(string Source, string? ConnectionId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveOrganizationDraftRequest(int SchemaVersion, long ExpectedRevision, string CurrentStep, OrganizationDraftValues Values);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmOrganizationDraftRequest(long ExpectedRevision, bool Confirmed);
public sealed record OrganizationOnboardingDraftResult(
    Guid DraftId, long Revision, string State, DateTimeOffset SavedAt, string DisplayName,
    string CurrentStep, OrganizationDraftValues Values, string CreationKey, Guid? TenantId, Guid? OperationId,
    string ResumeUrl, int SchemaVersion = 1);
public sealed record OrganizationLiveAdministrator(Guid PersonId, string DisplayName, Guid MembershipId,
    Guid DirectoryTenantId, Guid ObjectId, Guid AssignmentId);
public sealed record OrganizationLiveAccess(string State, int ActiveMemberCount,
    PagedResult<OrganizationLiveAdministrator> Administrators);
public sealed record OrganizationSetupTenant(Guid Id, string DisplayName, string Lifecycle, string OnboardingState);
public sealed record OrganizationSetupActions(bool CanManageMemberships, bool CanResumeEnrollment, bool CanEnterOrganization);
public sealed record OrganizationSetupSummary(OrganizationSetupTenant Tenant, DateTimeOffset ObservedAt,
    OrganizationLiveAccess LiveAccess, OrganizationProvisioningResult? RequestedOperation,
    string Reconciliation, OrganizationSetupActions ActorActions);
