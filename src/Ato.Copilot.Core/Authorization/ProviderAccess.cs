using System.Text.Json.Serialization;

namespace Ato.Copilot.Core.Authorization;

public static class ProviderActions
{
    public const string ProfileView = "provider.profile.view";
    public const string ProfileEdit = "provider.profile.edit";
    public const string SetupManage = "provider.setup.manage";
    public const string MembershipsManage = "provider.memberships.manage";
    public const string OfferingsManage = "provider.offerings.manage";
    public const string SecurityReview = "provider.security.review";
    public const string Assess = "provider.assess";
    public const string AuthorizationDecide = "provider.authorization.decide";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProviderRole
{
    PortalAdministrator,
    Issm,
    Isso,
    Engineer,
    ServiceOwner,
    Assessor,
    AuthorizingOfficial
}

public enum ProviderAccessState
{
    None,
    Compatibility,
    PendingInvitation,
    PendingRequest,
    Active,
    Blocked
}

public sealed record ProviderAccessSubject(
    Guid DirectoryTenantId,
    Guid ObjectId,
    string DisplayName,
    string? Email,
    bool IsCspAdmin = false);

public sealed record ProviderAccessResolution(
    ProviderAccessState State,
    Guid? ProviderId,
    Guid? PrincipalId,
    Guid? MembershipId,
    IReadOnlyList<ProviderRole> Roles,
    IReadOnlyList<string> Actions,
    string ReasonCode,
    string Source,
    Guid? InvitationId = null,
    Guid? AccessRequestId = null);

public sealed record CreateProviderInvitationRequest(
    Guid ProviderId,
    string? TargetEmail,
    Guid? TargetDirectoryTenantId,
    Guid? TargetObjectId,
    IReadOnlyList<ProviderRole> RequestedRoles,
    Guid? PortfolioId,
    Guid? OfferingId,
    DateTimeOffset ExpiresAt);

public sealed record ProviderInvitationCreated(Models.Tenancy.ProviderInvitation Invitation, string Token);
public sealed record ProviderInvitationAcceptance(Guid MembershipId, string Destination);

public sealed record GrantProviderMembershipRequest(
    Guid ProviderId,
    Guid DirectoryTenantId,
    Guid ObjectId,
    string DisplayName,
    string Email,
    IReadOnlyList<ProviderRole> Roles,
    Guid? PortfolioId,
    Guid? OfferingId,
    DateTimeOffset? ExpiresAt = null);

public sealed record ProviderMembershipGrant(Models.Tenancy.ProviderMembership Membership, bool Created);
public sealed record CreateProviderAccessRequest(string Justification, Guid? RequestedProviderId);
public sealed record DecideProviderAccessRequest(bool Approved, string Reason, IReadOnlyList<ProviderRole> Roles);

public interface IProviderAccessService
{
    Task<ProviderAccessResolution> ResolveAsync(
        ProviderAccessSubject subject, CancellationToken cancellationToken = default);

    Task<ProviderAccessResolution> RequireActionAsync(
        ProviderAccessSubject subject, Guid providerId, string action,
        CancellationToken cancellationToken = default);
}
