using System.Security.Claims;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints.Onboarding;

/// <summary>
/// Tenant-and-Organization onboarding wizard endpoints (Feature 048 US4 /
/// FR-054 / FR-056). Mirrors
/// <c>specs/048-tenant-isolation/contracts/tenant-onboarding.openapi.yaml</c>.
/// </summary>
/// <remarks>
/// Auth: every endpoint requires an authenticated principal. The acting
/// tenant id is taken from the resolved <see cref="ITenantContext"/> (set by
/// <c>TenantResolutionMiddleware</c>) so this surface is identical for both
/// pre-provisioned and self-onboarded tenants.
/// </remarks>
public static class TenantOnboardingEndpoints
{
    public static IEndpointRouteBuilder MapTenantOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding/tenant")
            .WithTags("Onboarding")
            .WithMetadata(new WorkspaceAuthorizedEndpoint())
            .AddEndpointFilter(async (invocation, next) =>
            {
                var http = invocation.HttpContext;
                http.Response.Headers.CacheControl = "no-store";
                var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
                if (http.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
                if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
                {
                    if (!tenant.IsWorkspaceRequest && !tenant.IsCspAdmin
                        && !http.User.IsInRole(Ato.Copilot.Core.Constants.ComplianceRoles.Administrator))
                        return Results.Json(new { error = new { errorCode = "TENANT_ADMIN_REQUIRED", message = "Tenant activation requires existing administrator authority." } }, statusCode: 403);
                    if (tenant.ImpersonatedTenantId.HasValue || tenant.IsWorkspaceRequest && (tenant.IsCspAdmin || !tenant.PersonId.HasValue))
                        return Results.Json(new { error = new { errorCode = "TENANT_ADMIN_REQUIRED", message = "Use an ordinary organization administrator workspace." } }, statusCode: 403);
                    if (tenant.IsWorkspaceRequest)
                    {
                        await using var db = await http.RequestServices.GetRequiredService<IDbContextFactory<AtoCopilotContext>>().CreateDbContextAsync(http.RequestAborted);
                        var person = tenant.PersonId!.Value;
                        if (!await db.OrganizationRoleAssignments.AnyAsync(x => x.TenantId == tenant.EffectiveTenantId
                                && x.PersonId == person && x.Role == OrganizationRole.Administrator && x.RemovedAt == null, http.RequestAborted)
                            || !await db.OrganizationMemberships.AnyAsync(x => x.TenantId == tenant.EffectiveTenantId
                                && x.PersonId == person && x.RevokedAt == null, http.RequestAborted))
                            return Results.Json(new { error = new { errorCode = "TENANT_ADMIN_REQUIRED", message = "An active organization administrator is required." } }, statusCode: 403);
                    }
                }
                try { return await next(invocation); }
                catch (DbUpdateConcurrencyException)
                {
                    return Results.Json(new { status = "error", error = new { errorCode = "STALE_REVISION", message = "Tenant setup changed. Reload before applying your edits." } }, statusCode: 409);
                }
            });

        group.MapPut("/draft", async (SaveTenantDraftRequest body, HttpContext http, ITenantContext ctx,
            ITenantOnboardingService service, CancellationToken ct) =>
        {
            try { return EnvelopeOk(await service.SaveDraftAsync(ctx.EffectiveTenantId, ResolveActor(http), body, ct)); }
            catch (ArgumentException ex) { return EnvelopeError("INVALID_REQUEST", ex.Message); }
        });
        group.MapPost("/draft/discard", async (DiscardTenantDraftRequest body, HttpContext http, ITenantContext ctx,
            ITenantOnboardingService service, CancellationToken ct) =>
            EnvelopeOk(await service.DiscardDraftAsync(ctx.EffectiveTenantId, ResolveActor(http), body.ExpectedRevision, ct)));

        group.MapGet("/state", async (
            HttpContext http,
            ITenantContext ctx,
            ITenantOnboardingService service,
            CancellationToken ct) =>
        {
            var progress = await service.GetStateAsync(ctx.EffectiveTenantId, ct);
            return EnvelopeOk(progress);
        }).WithName("GetTenantOnboardingState");

        group.MapPost("/legal-entity", async (
            LegalEntityStepRequest body,
            HttpContext http,
            ITenantContext ctx,
            ITenantOnboardingService service,
            CancellationToken ct) =>
        {
            var actor = ResolveActor(http);
            try
            {
                var progress = await service.SubmitLegalEntityAsync(ctx.EffectiveTenantId, actor, body, ct);
                return EnvelopeOk(progress);
            }
            catch (ArgumentException ex)
            {
                return EnvelopeError("INVALID_REQUEST", ex.Message);
            }
        }).WithName("SubmitTenantLegalEntity");

        group.MapPost("/hq-address", async (
            HqAddressStepRequest body,
            HttpContext http,
            ITenantContext ctx,
            ITenantOnboardingService service,
            CancellationToken ct) =>
        {
            var actor = ResolveActor(http);
            try
            {
                var progress = await service.SubmitHqAddressAsync(ctx.EffectiveTenantId, actor, body, ct);
                return EnvelopeOk(progress);
            }
            catch (ArgumentException ex)
            {
                return EnvelopeError("INVALID_REQUEST", ex.Message);
            }
        }).WithName("SubmitTenantHqAddress");

        group.MapPost("/classification", async (
            ClassificationStepRequest body,
            HttpContext http,
            ITenantContext ctx,
            ITenantOnboardingService service,
            CancellationToken ct) =>
        {
            var actor = ResolveActor(http);
            try
            {
                var progress = await service.SubmitClassificationAsync(ctx.EffectiveTenantId, actor, body, ct);
                return EnvelopeOk(progress);
            }
            catch (ArgumentException ex)
            {
                return EnvelopeError("INVALID_REQUEST", ex.Message);
            }
        }).WithName("SubmitTenantClassification");

        group.MapPost("/ao", async (
            AoStepRequest body,
            HttpContext http,
            ITenantContext ctx,
            ITenantOnboardingService service,
            CancellationToken ct) =>
        {
            var actor = ResolveActor(http);
            try
            {
                var progress = await service.SubmitAoAsync(ctx.EffectiveTenantId, actor, body, ct);
                return EnvelopeOk(progress);
            }
            catch (ArgumentException ex)
            {
                return EnvelopeError("INVALID_REQUEST", ex.Message);
            }
        }).WithName("SubmitTenantAo");

        group.MapPost("/primary-poc", async (
            PrimaryPocStepRequest body,
            HttpContext http,
            ITenantContext ctx,
            ITenantOnboardingService service,
            CancellationToken ct) =>
        {
            var actor = ResolveActor(http);
            try
            {
                var progress = await service.SubmitPrimaryPocAsync(ctx.EffectiveTenantId, actor, body, ct);
                return EnvelopeOk(progress);
            }
            catch (ArgumentException ex)
            {
                return EnvelopeError("INVALID_REQUEST", ex.Message);
            }
        }).WithName("SubmitTenantPrimaryPoc");

        group.MapPost("/org-profile", async (
            OrgProfileStepRequest body,
            HttpContext http,
            ITenantContext ctx,
            ITenantOnboardingService service,
            CancellationToken ct) =>
        {
            var actor = ResolveActor(http);
            try
            {
                var progress = await service.SubmitOrgProfileAsync(ctx.EffectiveTenantId, actor, body, ct);
                return EnvelopeOk(progress);
            }
            catch (ArgumentException ex)
            {
                return EnvelopeError("INVALID_REQUEST", ex.Message);
            }
        }).WithName("SubmitTenantOrgProfile");

        group.MapPost("/submit", async (
            HttpContext http,
            ITenantContext ctx,
            ITenantOnboardingService service,
            CancellationToken ct) =>
        {
            var actor = ResolveActor(http);
            try
            {
                var body = http.Request.HasJsonContentType() && http.Request.ContentLength != 0
                    ? await http.Request.ReadFromJsonAsync<SubmitTenantRequest>(ct) : null;
                if (body?.ExpectedRevision.HasValue == true && !body.Confirmed)
                    return EnvelopeError("CONFIRMATION_REQUIRED", "Confirm the saved tenant facts before activation.");
                var progress = await service.SubmitFinalAsync(ctx.EffectiveTenantId, actor, ct, body?.ExpectedRevision);
                return EnvelopeOk(progress);
            }
            catch (InvalidOperationException ex)
            {
                return EnvelopeError("ONBOARDING_INCOMPLETE", ex.Message);
            }
        }).WithName("SubmitTenantOnboardingFinal");

        return app;
    }

    private static Guid ResolveActor(HttpContext http)
    {
        // Prefer the Entra `oid` claim, then the standard NameIdentifier;
        // finally fall back to a deterministic sentinel so the audit row
        // always has a non-empty UserId. The sentinel is explicitly the
        // SystemTenantId GUID so it is easily identifiable in audit
        // queries (FR-052: actor identity is non-null on every row).
        var raw = http.User.FindFirstValue("oid")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var oid) ? oid : Guid.Empty;
    }

    private static IResult EnvelopeOk(TenantOnboardingProgress progress)
        => Results.Ok(new
        {
            status = "success",
            data = new
            {
                tenantId = progress.TenantId,
                currentStep = progress.CurrentStep,
                completedSteps = progress.CompletedSteps,
                onboardingState = progress.OnboardingState.ToString(),
                firstOrganizationId = progress.FirstOrganizationId,
                submittedValues = progress.SubmittedValues,
                draft = progress.Draft,
                draftRevision = progress.DraftRevision,
                missingRequiredFields = progress.MissingRequiredFields,
            },
        });

    private static IResult EnvelopeError(string errorCode, string message)
        => Results.Json(new
        {
            status = "error",
            error = new { errorCode, message },
        }, statusCode: StatusCodes.Status400BadRequest);

    public sealed record DiscardTenantDraftRequest(long ExpectedRevision);
    public sealed record SubmitTenantRequest(long? ExpectedRevision = null, bool Confirmed = false);
}
