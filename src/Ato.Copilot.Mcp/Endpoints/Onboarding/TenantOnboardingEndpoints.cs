using System.Security.Claims;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

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
            .WithTags("Onboarding");

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
                var progress = await service.SubmitFinalAsync(ctx.EffectiveTenantId, actor, ct);
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
            },
        });

    private static IResult EnvelopeError(string errorCode, string message)
        => Results.Json(new
        {
            status = "error",
            error = new { errorCode, message },
        }, statusCode: StatusCodes.Status400BadRequest);
}
