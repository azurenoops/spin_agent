using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Authorization;
using Ato.Copilot.Core.Configuration;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Core.Services.Authorization;

public sealed class ProviderAccessService(
    IDbContextFactory<AtoCopilotContext> factory,
    IOptions<PlatformOperationsOptions> platformOptions) : IProviderAccessService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProviderAccessResolution> ResolveAsync(
        ProviderAccessSubject subject, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var matches = await db.ProviderDirectoryMatches.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.DirectoryTenantId == subject.DirectoryTenantId
                && x.ObjectId == subject.ObjectId && x.State == "Verified")
            .ToListAsync(cancellationToken);
        if (matches.Count > 1)
            return Blocked("PROVIDER_IDENTITY_CONFLICT");

        if (matches.Count == 1)
        {
            var match = matches[0];
            var memberships = await db.ProviderMemberships.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.ProviderId == match.ProviderId
                    && x.PrincipalId == match.PrincipalId
                    && x.DirectoryMatchId == match.Id)
                .OrderByDescending(x => x.Id).ToListAsync(cancellationToken);
            var active = memberships.Where(x => x.State == "Active"
                && x.RevokedAt == null && (!x.ExpiresAt.HasValue || x.ExpiresAt > now)).ToList();
            if (active.Count > 1)
                return Blocked("PROVIDER_MEMBERSHIP_CONFLICT", match.ProviderId, match.PrincipalId);
            if (active.Count == 1)
            {
                var membership = active[0];
                var roleRows = await db.ProviderRoleAssignments.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.ProviderId == match.ProviderId
                        && x.MembershipId == membership.Id && x.RemovedAt == null)
                    .ToListAsync(cancellationToken);
                var roles = roleRows.Select(x => Enum.TryParse<ProviderRole>(x.Role, out var role)
                        ? role : (ProviderRole?)null)
                    .Where(x => x.HasValue).Select(x => x!.Value).Distinct().Order().ToArray();
                if (roles.Length == 0)
                    return Blocked("PROVIDER_ROLE_MISSING", match.ProviderId, match.PrincipalId, membership.Id);
                return new(ProviderAccessState.Active, match.ProviderId, match.PrincipalId,
                    membership.Id, roles, Actions(roles), "PROVIDER_MEMBERSHIP_ACTIVE",
                    "ProviderMembership");
            }
            if (memberships.Any(x => x.RevokedAt.HasValue || x.State == "Revoked"))
                return Blocked("PROVIDER_MEMBERSHIP_REVOKED", match.ProviderId, match.PrincipalId);
            if (memberships.Any(x => x.ExpiresAt <= now || x.State == "Expired"))
                return Blocked("PROVIDER_MEMBERSHIP_EXPIRED", match.ProviderId, match.PrincipalId);
        }

        var invitations = await db.ProviderInvitations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (!x.TargetDirectoryTenantId.HasValue || x.TargetDirectoryTenantId == subject.DirectoryTenantId)
                && (!x.TargetObjectId.HasValue || x.TargetObjectId == subject.ObjectId))
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        invitations = invitations.Where(x => string.IsNullOrWhiteSpace(x.TargetEmail)
            || string.Equals(x.TargetEmail, subject.Email, StringComparison.OrdinalIgnoreCase)).ToList();
        var pendingInvitations = invitations.Where(x => x.Status == "Pending").ToList();
        if (pendingInvitations.Count > 1)
            return Blocked("PROVIDER_INVITATION_CONFLICT");
        if (pendingInvitations.Count == 1)
        {
            var invitation = pendingInvitations[0];
            if (invitation.ExpiresAt <= now)
                return Blocked("PROVIDER_INVITATION_EXPIRED", invitation.ProviderId,
                    invitationId: invitation.Id);
            return new(ProviderAccessState.PendingInvitation, invitation.ProviderId, null, null,
                [], [], "PROVIDER_INVITATION_PENDING", "ProviderInvitation", invitation.Id);
        }
        var revokedInvitation = invitations.LastOrDefault(x => x.Status == "Revoked");
        if (revokedInvitation is not null)
            return Blocked("PROVIDER_INVITATION_REVOKED", revokedInvitation.ProviderId,
                invitationId: revokedInvitation.Id);

        var request = await db.ProviderAccessRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.DirectoryTenantId == subject.DirectoryTenantId
                && x.ObjectId == subject.ObjectId && x.Status == "Pending")
            .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (request is not null)
            return new(ProviderAccessState.PendingRequest, request.ProviderId, null, null,
                [], [], "PROVIDER_ACCESS_REQUEST_PENDING", "ProviderAccessRequest",
                AccessRequestId: request.Id);

        if (subject.IsCspAdmin)
        {
            var provider = await db.CspProfiles.AsNoTracking().OrderBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            return new(ProviderAccessState.Compatibility, provider?.Id, null, null,
                [ProviderRole.PortalAdministrator], Actions([ProviderRole.PortalAdministrator]),
                "CSP_ADMIN_COMPATIBILITY", "CSP.Admin");
        }

        return new(ProviderAccessState.None, null, null, null, [], [],
            "NO_PROVIDER_ACCESS", "None");
    }

    public async Task<ProviderAccessResolution> RequireActionAsync(
        ProviderAccessSubject subject, Guid providerId, string action,
        CancellationToken cancellationToken = default)
    {
        var access = await ResolveAsync(subject, cancellationToken);
        if (access.ProviderId != providerId || !access.Actions.Contains(action, StringComparer.Ordinal))
            throw new UnauthorizedAccessException($"Provider action '{action}' is not authorized.");
        return access;
    }

    public async Task<ProviderInvitationCreated> CreateInvitationAsync(
        ProviderAccessSubject actor, CreateProviderInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeManagementAsync(actor, request.ProviderId, cancellationToken);
        ValidateRoles(request.RequestedRoles);
        if (string.IsNullOrWhiteSpace(request.TargetEmail)
            && !request.TargetDirectoryTenantId.HasValue && !request.TargetObjectId.HasValue)
            throw new ArgumentException("Invitation requires an email or explicit directory identity.");
        if (request.ExpiresAt <= DateTimeOffset.UtcNow || request.ExpiresAt > DateTimeOffset.UtcNow.AddDays(30))
            throw new ArgumentException("Invitation expiry must be within 30 days.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await ValidateScopeAsync(db, request.ProviderId, request.PortfolioId, request.OfferingId, cancellationToken);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var invitation = new ProviderInvitation
        {
            ProviderId = request.ProviderId,
            TokenHash = HashToken(token),
            TargetEmail = NormalizeEmail(request.TargetEmail),
            TargetDirectoryTenantId = request.TargetDirectoryTenantId,
            TargetObjectId = request.TargetObjectId,
            RequestedRolesJson = JsonSerializer.Serialize(request.RequestedRoles.Distinct().Order().ToArray(), JsonOptions),
            PortfolioId = request.PortfolioId,
            OfferingId = request.OfferingId,
            ExpiresAt = request.ExpiresAt,
            CreatedBy = actor.ObjectId.ToString("D")
        };
        db.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);
        return new(invitation, token);
    }

    public async Task<ProviderInvitation> GetInvitationAsync(
        ProviderAccessSubject actor, Guid invitationId, string? token,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var invitation = await db.ProviderInvitations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == invitationId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider invitation was not found.");
        var target = Matches(invitation, actor);
        var manager = IsPlatformOperator(actor)
            || (await ResolveAsync(actor, cancellationToken)) is { State: ProviderAccessState.Active } access
                && access.ProviderId == invitation.ProviderId
                && access.Actions.Contains(ProviderActions.MembershipsManage);
        var tokenMatches = !string.IsNullOrWhiteSpace(token)
            && CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(invitation.TokenHash),
                Convert.FromHexString(HashToken(token)));
        if (!target && !manager && !tokenMatches)
            throw new UnauthorizedAccessException("Invitation identity does not match the authenticated user.");
        return invitation;
    }

    public async Task<IReadOnlyList<ProviderInvitation>> ListInvitationsAsync(
        ProviderAccessSubject actor, Guid providerId,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeManagementAsync(actor, providerId, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ProviderInvitations.AsNoTracking()
            .Where(x => x.ProviderId == providerId)
            .OrderByDescending(x => x.Id)
            .Take(100).ToListAsync(cancellationToken);
    }

    public async Task RevokeInvitationAsync(
        ProviderAccessSubject actor, Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var invitation = await db.ProviderInvitations.IgnoreQueryFilters().SingleOrDefaultAsync(
            x => x.Id == invitationId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider invitation was not found.");
        await AuthorizeManagementAsync(actor, invitation.ProviderId, cancellationToken);
        if (invitation.Status != "Pending")
            throw new InvalidOperationException("Only a pending invitation can be revoked.");
        invitation.Status = "Revoked";
        invitation.RevokedAt = DateTimeOffset.UtcNow;
        invitation.RevokedBy = actor.ObjectId.ToString("D");
        invitation.Revision++;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProviderInvitationAcceptance> AcceptInvitationAsync(
        Guid invitationId, string token, ProviderAccessSubject subject,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var invitation = await db.ProviderInvitations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == invitationId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider invitation was not found.");
        if (invitation.Status != "Pending")
            throw new InvalidOperationException("Invitation was already accepted or revoked.");
        if (invitation.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("Invitation is expired.");
        if (!Matches(invitation, subject))
            throw new UnauthorizedAccessException("Invitation identity does not match the authenticated user.");
        if (string.IsNullOrWhiteSpace(token)
            || !CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(invitation.TokenHash),
                Convert.FromHexString(HashToken(token))))
            throw new UnauthorizedAccessException("Invitation token is invalid.");

        var roles = JsonSerializer.Deserialize<ProviderRole[]>(invitation.RequestedRolesJson, JsonOptions) ?? [];
        var grant = await GrantInternalAsync(db, new(
            invitation.ProviderId, subject.DirectoryTenantId, subject.ObjectId,
            subject.DisplayName, subject.Email ?? invitation.TargetEmail ?? "",
            roles, invitation.PortfolioId, invitation.OfferingId), invitation.CreatedBy,
            "ProviderInvitation", cancellationToken);
        invitation.Status = "Accepted";
        invitation.AcceptedAt = DateTimeOffset.UtcNow;
        invitation.AcceptedPrincipalId = grant.Membership.PrincipalId;
        invitation.Revision++;
        await db.SaveChangesAsync(cancellationToken);
        return new(grant.Membership.Id, "/workspaces/csp/authorizations");
    }

    public async Task<ProviderMembershipGrant> GrantMembershipAsync(
        ProviderAccessSubject actor, GrantProviderMembershipRequest request,
        CancellationToken cancellationToken = default)
    {
        await AuthorizeManagementAsync(actor, request.ProviderId, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var result = await GrantInternalAsync(db, request, actor.ObjectId.ToString("D"),
            "ExplicitGrant", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<ProviderMembership>> ListMembershipsAsync(
        ProviderAccessSubject actor, Guid providerId, CancellationToken cancellationToken = default)
    {
        await AuthorizeManagementAsync(actor, providerId, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ProviderMemberships.AsNoTracking().Where(x => x.ProviderId == providerId)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
    }

    public async Task RevokeMembershipAsync(
        ProviderAccessSubject actor, Guid membershipId, string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var membership = await db.ProviderMemberships.IgnoreQueryFilters().SingleOrDefaultAsync(
            x => x.Id == membershipId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider membership was not found.");
        await AuthorizeManagementAsync(actor, membership.ProviderId, cancellationToken);
        if (membership.RevokedAt.HasValue) return;
        membership.State = "Revoked";
        membership.RevokedAt = DateTimeOffset.UtcNow;
        membership.RevokedBy = actor.ObjectId.ToString("D");
        membership.RevocationReason = Bound(reason, 2000);
        membership.Revision++;
        var roles = await db.ProviderRoleAssignments.IgnoreQueryFilters().Where(x =>
            x.ProviderId == membership.ProviderId && x.MembershipId == membership.Id
            && x.RemovedAt == null).ToListAsync(cancellationToken);
        foreach (var role in roles)
        {
            role.RemovedAt = membership.RevokedAt;
            role.RemovedBy = membership.RevokedBy;
            role.RemovalReason = membership.RevocationReason;
            role.Revision++;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProviderAccessRequest> CreateAccessRequestAsync(
        ProviderAccessSubject subject, CreateProviderAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        var justification = Bound(request.Justification, 2000);
        if (string.IsNullOrWhiteSpace(justification))
            throw new ArgumentException("Access request justification is required.");
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var providerId = request.RequestedProviderId
            ?? await db.CspProfiles.Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("A provider must be selected before requesting access.");
        if (!await db.CspProfiles.AnyAsync(x => x.Id == providerId, cancellationToken))
            throw new KeyNotFoundException("Requested provider was not found.");
        var existing = await db.ProviderAccessRequests.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.DirectoryTenantId == subject.DirectoryTenantId && x.ObjectId == subject.ObjectId
            && x.Status == "Pending", cancellationToken);
        if (existing is not null) return existing;
        var row = new ProviderAccessRequest
        {
            ProviderId = providerId,
            DirectoryTenantId = subject.DirectoryTenantId,
            ObjectId = subject.ObjectId,
            Email = NormalizeEmail(subject.Email),
            Justification = justification!
        };
        db.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task<ProviderAccessRequest?> GetCurrentAccessRequestAsync(
        ProviderAccessSubject subject, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.ProviderAccessRequests.IgnoreQueryFilters().AsNoTracking().Where(x =>
                x.DirectoryTenantId == subject.DirectoryTenantId && x.ObjectId == subject.ObjectId)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProviderAccessRequest> DecideAccessRequestAsync(
        ProviderAccessSubject actor, Guid requestId, DecideProviderAccessRequest decision,
        CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var request = await db.ProviderAccessRequests.IgnoreQueryFilters().SingleOrDefaultAsync(
            x => x.Id == requestId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider access request was not found.");
        var providerId = request.ProviderId;
        await AuthorizeManagementAsync(actor, providerId, cancellationToken);
        if (request.Status != "Pending")
            throw new InvalidOperationException("Access request was already decided.");
        if (decision.Approved)
        {
            ValidateRoles(decision.Roles);
            await GrantInternalAsync(db, new(providerId, request.DirectoryTenantId, request.ObjectId,
                request.Email ?? "Provider member", request.Email ?? "", decision.Roles, null, null),
                actor.ObjectId.ToString("D"), "ApprovedAccessRequest", cancellationToken);
            request.Status = "Approved";
        }
        else
        {
            request.Status = "Denied";
        }
        request.DecidedAt = DateTimeOffset.UtcNow;
        request.DecidedBy = actor.ObjectId.ToString("D");
        request.DecisionReason = Bound(decision.Reason, 2000);
        request.Revision++;
        await db.SaveChangesAsync(cancellationToken);
        return request;
    }

    private async Task<ProviderMembershipGrant> GrantInternalAsync(
        AtoCopilotContext db, GrantProviderMembershipRequest request, string actor,
        string provenance, CancellationToken cancellationToken)
    {
        ValidateRoles(request.Roles);
        await ValidateScopeAsync(db, request.ProviderId, request.PortfolioId,
            request.OfferingId, cancellationToken);
        var matches = await db.ProviderDirectoryMatches.Where(x =>
            x.DirectoryTenantId == request.DirectoryTenantId && x.ObjectId == request.ObjectId
            && x.State == "Verified").ToListAsync(cancellationToken);
        if (matches.Count > 1 || matches.Count == 1 && matches[0].ProviderId != request.ProviderId)
            throw new InvalidOperationException("Provider directory identity conflicts with an existing principal.");

        ProviderPrincipal principal;
        ProviderDirectoryMatch match;
        if (matches.Count == 1)
        {
            match = matches[0];
            principal = await db.ProviderPrincipals.SingleAsync(x =>
                x.ProviderId == request.ProviderId && x.Id == match.PrincipalId, cancellationToken);
        }
        else
        {
            principal = new ProviderPrincipal
            {
                ProviderId = request.ProviderId,
                DisplayName = Bound(request.DisplayName, 256) ?? throw new ArgumentException("Display name is required."),
                Email = NormalizeEmail(request.Email),
                CreatedBy = actor
            };
            match = new ProviderDirectoryMatch
            {
                ProviderId = request.ProviderId,
                PrincipalId = principal.Id,
                DirectoryTenantId = request.DirectoryTenantId,
                ObjectId = request.ObjectId,
                Provenance = provenance,
                VerifiedBy = actor
            };
            db.AddRange(principal, match);
        }

        var membership = await db.ProviderMemberships.SingleOrDefaultAsync(x =>
            x.ProviderId == request.ProviderId && x.PrincipalId == principal.Id
            && x.DirectoryMatchId == match.Id && x.State == "Active"
            && x.RevokedAt == null, cancellationToken);
        var created = membership is null;
        membership ??= new ProviderMembership
        {
            ProviderId = request.ProviderId,
            PrincipalId = principal.Id,
            DirectoryMatchId = match.Id,
            GrantedBy = actor,
            ExpiresAt = request.ExpiresAt
        };
        if (created) db.Add(membership);
        foreach (var role in request.Roles.Distinct())
        {
            var roleName = role.ToString();
            if (await db.ProviderRoleAssignments.AnyAsync(x =>
                    x.ProviderId == request.ProviderId && x.MembershipId == membership.Id
                    && x.Role == roleName && x.PortfolioId == request.PortfolioId
                    && x.OfferingId == request.OfferingId && x.RemovedAt == null,
                    cancellationToken))
                continue;
            db.Add(new ProviderRoleAssignment
            {
                ProviderId = request.ProviderId,
                MembershipId = membership.Id,
                Role = roleName,
                PortfolioId = request.PortfolioId,
                OfferingId = request.OfferingId,
                GrantedBy = actor
            });
        }
        return new(membership, created);
    }

    private async Task AuthorizeManagementAsync(
        ProviderAccessSubject actor, Guid providerId, CancellationToken cancellationToken)
    {
        if (IsPlatformOperator(actor)) return;
        var access = await ResolveAsync(actor, cancellationToken);
        if (access.State != ProviderAccessState.Active || access.ProviderId != providerId
            || !access.Actions.Contains(ProviderActions.MembershipsManage))
            throw new UnauthorizedAccessException("Provider membership management is not authorized.");
    }

    private bool IsPlatformOperator(ProviderAccessSubject subject) =>
        platformOptions.Value.AuthorizedObjectIds.Contains(subject.ObjectId);

    private static async Task ValidateScopeAsync(
        AtoCopilotContext db, Guid providerId, Guid? portfolioId, Guid? offeringId,
        CancellationToken cancellationToken)
    {
        if (!await db.CspProfiles.AnyAsync(x => x.Id == providerId, cancellationToken))
            throw new KeyNotFoundException("Provider was not found.");
        if (portfolioId.HasValue && !await db.ServicePortfolios.AnyAsync(x =>
                x.ProviderId == providerId && x.Id == portfolioId, cancellationToken))
            throw new KeyNotFoundException("Provider portfolio was not found.");
        if (offeringId.HasValue && !await db.Set<ProviderOffering>().AnyAsync(x =>
                x.ProviderId == providerId && x.Id == offeringId, cancellationToken))
            throw new KeyNotFoundException("Provider offering was not found.");
    }

    private static bool Matches(ProviderInvitation invitation, ProviderAccessSubject subject) =>
        (!invitation.TargetDirectoryTenantId.HasValue
            || invitation.TargetDirectoryTenantId == subject.DirectoryTenantId)
        && (!invitation.TargetObjectId.HasValue || invitation.TargetObjectId == subject.ObjectId)
        && (string.IsNullOrWhiteSpace(invitation.TargetEmail)
            || string.Equals(invitation.TargetEmail, subject.Email, StringComparison.OrdinalIgnoreCase));

    private static void ValidateRoles(IReadOnlyList<ProviderRole> roles)
    {
        if (roles is null || roles.Count is < 1 or > 8 || roles.Distinct().Count() != roles.Count)
            throw new ArgumentException("One or more distinct provider roles are required.");
    }

    private static IReadOnlyList<string> Actions(IEnumerable<ProviderRole> roles) =>
        roles.SelectMany(ActionsForRole)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> ActionsForRole(ProviderRole role) =>
        role switch
        {
            ProviderRole.PortalAdministrator =>
            [
                ProviderActions.ProfileView, ProviderActions.ProfileEdit,
                ProviderActions.SetupManage, ProviderActions.MembershipsManage,
                ProviderActions.OfferingsManage
            ],
            ProviderRole.Issm =>
            [
                ProviderActions.ProfileView, ProviderActions.OfferingsManage,
                ProviderActions.SecurityReview
            ],
            ProviderRole.Isso or ProviderRole.Engineer or ProviderRole.ServiceOwner =>
            [
                ProviderActions.ProfileView, ProviderActions.OfferingsManage
            ],
            ProviderRole.Assessor =>
            [
                ProviderActions.ProfileView, ProviderActions.Assess
            ],
            ProviderRole.AuthorizingOfficial =>
            [
                ProviderActions.ProfileView, ProviderActions.AuthorizationDecide
            ],
            _ => []
        };

    private static ProviderAccessResolution Blocked(
        string reason, Guid? providerId = null, Guid? principalId = null,
        Guid? membershipId = null, Guid? invitationId = null) =>
        new(ProviderAccessState.Blocked, providerId, principalId, membershipId,
            [], [], reason, "ProviderAccess");

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string? NormalizeEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Bound(value.Trim().ToLowerInvariant(), 320);

    private static string? Bound(string? value, int max) =>
        value?.Length > max ? throw new ArgumentException($"Value exceeds {max} characters.") : value;
}
