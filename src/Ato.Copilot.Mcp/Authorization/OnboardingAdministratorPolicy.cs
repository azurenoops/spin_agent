using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Mcp.Authorization;

/// <summary>
/// Authorization policy / requirement for wizard endpoints (FR-001 / FR-002 / FR-009).
/// A caller must either:
/// <list type="bullet">
/// <item><description>Be the first authenticated user in an empty tenant (bootstrap path), or</description></item>
/// <item><description>Hold an active in-app <c>Administrator</c> role assignment for the tenant.</description></item>
/// </list>
/// Tier 1 — bootstrap detection — is performed by <see cref="OnboardingAuthorizationFilter"/>;
/// Tier 2 — explicit role enforcement — is performed once onboarding has completed.
/// </summary>
public class OnboardingAdministratorRequirement : IAuthorizationRequirement
{
    /// <summary>Canonical policy name registered with the authorization options.</summary>
    public const string PolicyName = "OnboardingAdministratorPolicy";
}

/// <summary>
/// Resolves <see cref="OnboardingAdministratorRequirement"/> by inspecting the persisted
/// <see cref="OrganizationRoleAssignment"/> table. A non-admin during the bootstrap window
/// is permitted; once any non-removed admin assignment exists, only that user (or other
/// admins) may proceed.
/// </summary>
public class OnboardingAdministratorHandler : AuthorizationHandler<OnboardingAdministratorRequirement>
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILogger<OnboardingAdministratorHandler> _logger;
    private readonly Ato.Copilot.Core.Interfaces.Tenancy.ITenantContextAccessor? _tenants;

    public OnboardingAdministratorHandler(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILogger<OnboardingAdministratorHandler> logger,
        Ato.Copilot.Core.Interfaces.Tenancy.ITenantContextAccessor? tenants = null)
    {
        _contextFactory = contextFactory;
        _logger = logger;
        _tenants = tenants;
    }

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OnboardingAdministratorRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var ct = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (_tenants?.Current is { IsWorkspaceRequest: true } workspace)
        {
            if (workspace.PersonId is not { } personId) return;
            await using var scopedDb = await _contextFactory.CreateDbContextAsync(ct);
            if (await scopedDb.OrganizationRoleAssignments.AnyAsync(a => a.TenantId == workspace.EffectiveTenantId
                && a.PersonId == personId && a.Role == OrganizationRole.Administrator && a.RemovedAt == null, ct))
                context.Succeed(requirement);
            return;
        }

        var tenantClaim = context.User.FindFirstValue("tid")
            ?? context.User.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
        var subjectClaim = context.User.FindFirstValue("oid")
            ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(tenantClaim, out var tenantId) || !Guid.TryParse(subjectClaim, out var subjectId))
        {
            _logger.LogDebug("OnboardingAdministratorPolicy: missing tid/oid claims; denying");
            return;
        }

        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var anyAdmin = await db.OrganizationRoleAssignments
            .AnyAsync(a => a.TenantId == tenantId
                        && a.Role == OrganizationRole.Administrator
                        && a.RemovedAt == null, ct);

        if (!anyAdmin)
        {
            // Bootstrap window — first authenticated user is permitted (FR-001).
            context.Succeed(requirement);
            return;
        }

        // Tier 2 — caller must have an active admin assignment.
        var caller = await db.OrganizationRoleAssignments
            .Include(a => a.Person)
            .Where(a => a.TenantId == tenantId
                     && a.Role == OrganizationRole.Administrator
                     && a.RemovedAt == null)
            .FirstOrDefaultAsync(a => a.Person != null
                && (a.Person.EntraObjectId == subjectId || a.Person.Id == subjectId), ct);

        if (caller != null)
        {
            context.Succeed(requirement);
        }
    }
}
