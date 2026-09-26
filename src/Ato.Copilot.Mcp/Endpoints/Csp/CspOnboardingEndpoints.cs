using System.Diagnostics;
using System.Security.Claims;
using Ato.Copilot.Core.Configuration.Tenancy;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

/// <summary>
/// CSP-Admin onboarding wizard endpoints (Feature 048 US7 / FR-006 / FR-090 /
/// FR-092 / FR-093). Mirrors
/// <c>specs/048-tenant-isolation/contracts/csp-onboarding.openapi.yaml</c>.
/// </summary>
/// <remarks>
/// Auth — every endpoint requires <c>ITenantContext.IsCspAdmin = true</c>;
/// otherwise the response is <c>403 FORBIDDEN_NOT_CSP_ADMIN</c>.
/// SingleTenant short-circuit — when <c>DeploymentOptions.Mode</c> is
/// <see cref="DeploymentMode.SingleTenant"/>, every route returns
/// <c>404 SINGLE_TENANT_MODE</c> per FR-093 BEFORE touching the DB so no
/// <see cref="CspProfile"/> row is ever persisted.
/// </remarks>
public static class CspOnboardingEndpoints
{
    public static IEndpointRouteBuilder MapCspOnboardingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/onboarding")
            .WithTags("CSP Onboarding");

        group.MapGet("/state", GetStateAsync).WithName("GetCspOnboardingState");
        group.MapPost("/identity", PostIdentityAsync).WithName("PostCspOnboardingIdentity");
        group.MapPost("/support", PostSupportAsync).WithName("PostCspOnboardingSupport");
        group.MapPost("/classification", PostClassificationAsync).WithName("PostCspOnboardingClassification");
        group.MapPost("/submit", PostSubmitAsync).WithName("PostCspOnboardingSubmit");

        // Feature 048 T207 [US9]: wizard-time ATO document upload.
        // 50 MB per-file cap (FR-099 / FR-103); CSP-Admin only;
        // SingleTenant-mode 404 short-circuit; profile is auto-created on
        // first upload (mirrors the identity step's EnsureCreatedAsync).
        group.MapPost("/atos/upload", CspPackageImportEndpoints.UploadOnboardingAsync)
            .WithName("PostCspOnboardingAtosUpload")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(50L * 1024L * 1024L));
        group.MapGet("/atos/state", GetAtosStateAsync).WithName("GetCspOnboardingAtosState");

        return app;
    }

    // ─── handlers ───────────────────────────────────────────────────────────

    private static async Task<IResult> GetStateAsync(
        HttpContext http,
        ITenantContext tenantCtx,
        ICspProfileService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin || tenantCtx.ImpersonatedTenantId is not null) return ForbiddenNotCspAdmin(sw);

        var profile = await service.GetAsync(ct);
        var step = service.ComputeCurrentStep(profile);
        return Success(sw, BuildStateDto(profile, step));
    }

    private static async Task<IResult> PostIdentityAsync(
        [FromBody] IdentityRequest body,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspProfileService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin || tenantCtx.ImpersonatedTenantId is not null) return ForbiddenNotCspAdmin(sw);

        try
        {
            var actor = ResolveActor(http);
            var profile = await service.UpdateIdentityAsync(
                body.LegalEntityName ?? string.Empty,
                body.DisplayName ?? string.Empty,
                body.LogoUrl,
                actor,
                ct);
            var step = service.ComputeCurrentStep(profile);
            return Success(sw, BuildStateDto(profile, step));
        }
        catch (ArgumentException ex)
        {
            return ValidationError(sw, ex.Message);
        }
        catch (CspAlreadyOnboardedException)
        {
            return AlreadyOnboarded(sw);
        }
    }

    private static async Task<IResult> PostSupportAsync(
        [FromBody] SupportRequest body,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspProfileService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin || tenantCtx.ImpersonatedTenantId is not null) return ForbiddenNotCspAdmin(sw);

        try
        {
            var actor = ResolveActor(http);
            var profile = await service.UpdateSupportAsync(
                body.PrimarySupportEmail ?? string.Empty,
                body.SupportPhone,
                actor,
                ct);
            var step = service.ComputeCurrentStep(profile);
            return Success(sw, BuildStateDto(profile, step));
        }
        catch (ArgumentException ex)
        {
            return ValidationError(sw, ex.Message);
        }
        catch (CspAlreadyOnboardedException)
        {
            return AlreadyOnboarded(sw);
        }
    }

    private static async Task<IResult> PostClassificationAsync(
        [FromBody] ClassificationRequest body,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspProfileService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin || tenantCtx.ImpersonatedTenantId is not null) return ForbiddenNotCspAdmin(sw);

        try
        {
            if (!Enum.TryParse<ClassificationLevel>(
                    body.DefaultClassificationFloor, ignoreCase: true, out var floor))
            {
                return ValidationError(sw,
                    $"defaultClassificationFloor '{body.DefaultClassificationFloor}' is not a valid ClassificationLevel.");
            }
            var actor = ResolveActor(http);
            var profile = await service.UpdateClassificationAsync(floor, actor, ct);
            var step = service.ComputeCurrentStep(profile);
            return Success(sw, BuildStateDto(profile, step));
        }
        catch (ArgumentException ex)
        {
            return ValidationError(sw, ex.Message);
        }
        catch (CspAlreadyOnboardedException)
        {
            return AlreadyOnboarded(sw);
        }
    }

    private static async Task<IResult> PostSubmitAsync(
        HttpContext http,
        ITenantContext tenantCtx,
        ICspProfileService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin || tenantCtx.ImpersonatedTenantId is not null) return ForbiddenNotCspAdmin(sw);

        try
        {
            var actor = ResolveActor(http);
            var profile = await service.SubmitAsync(actor, ct);

            var step = service.ComputeCurrentStep(profile);
            return Success(sw, BuildStateDto(profile, step));
        }
        catch (CspOnboardingIncompleteException ex)
        {
            return ValidationError(sw, ex.Message);
        }
        catch (CspAlreadyOnboardedException)
        {
            return AlreadyOnboarded(sw);
        }
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private static bool ShouldShortCircuitSingleTenant(
        IOptions<DeploymentOptions> deployment, out IResult result)
    {
        if (deployment.Value.Mode == DeploymentMode.SingleTenant)
        {
            result = Error(StatusCodes.Status404NotFound, "SINGLE_TENANT_MODE",
                "CSP onboarding is unavailable in SingleTenant deployments.");
            return true;
        }
        result = null!;
        return false;
    }

    private static string ResolveActor(HttpContext http)
    {
        var raw = http.User.FindFirstValue("oid")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(raw) ? "system" : raw;
    }

    private static object BuildStateDto(CspProfile? profile, CspOnboardingStep currentStep)
    {
        if (profile is null)
        {
            return new
            {
                cspProfileId = (Guid?)null,
                onboardingState = OnboardingState.Pending.ToString(),
                currentStep = currentStep.ToString(),
                identity = (object?)null,
                supportContact = (object?)null,
                classification = (object?)null,
                onboardingCompletedAt = (DateTimeOffset?)null,
            };
        }

        return new
        {
            cspProfileId = (Guid?)profile.Id,
            onboardingState = profile.OnboardingState.ToString(),
            currentStep = currentStep.ToString(),
            identity = profile.IdentityCompletedAt is null ? null : new
            {
                legalEntityName = profile.LegalEntityName,
                displayName = profile.DisplayName,
                logoUrl = profile.LogoUrl,
            },
            supportContact = profile.SupportCompletedAt is null ? null : new
            {
                primarySupportEmail = profile.PrimarySupportEmail,
                supportPhone = profile.SupportPhone,
            },
            classification = profile.ClassificationCompletedAt is null ? null : new
            {
                defaultClassificationFloor = profile.DefaultClassificationFloor.ToString(),
            },
            onboardingCompletedAt = profile.OnboardingCompletedAt,
        };
    }

    private static IResult Success(Stopwatch sw, object data) =>
        Results.Json(new
        {
            status = "success",
            data,
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
        }, statusCode: StatusCodes.Status200OK);

    private static IResult ForbiddenNotCspAdmin(Stopwatch sw) =>
        Results.Json(new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error = new
            {
                errorCode = "FORBIDDEN_NOT_CSP_ADMIN",
                message = "Operation requires CSP.Admin role.",
            },
        }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ValidationError(Stopwatch sw, string message) =>
        Results.Json(new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error = new { errorCode = "VALIDATION_FAILED", message },
        }, statusCode: StatusCodes.Status422UnprocessableEntity);

    private static IResult AlreadyOnboarded(Stopwatch sw) =>
        Results.Json(new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error = new
            {
                errorCode = "CSP_ALREADY_ONBOARDED",
                message = "CSP onboarding is already complete; further submissions are rejected.",
            },
        }, statusCode: StatusCodes.Status409Conflict);

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new
        {
            status = "error",
            error = new { errorCode = code, message },
        }, statusCode: statusCode);

    // ─── T207 [US9]: ATO Documents step (FR-099, FR-100, FR-101, FR-103) ─

    private static async Task<IResult> GetAtosStateAsync(
        HttpContext http,
        ITenantContext tenantCtx,
        ICspProfileService profileService,
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin || tenantCtx.ImpersonatedTenantId is not null) return ForbiddenNotCspAdmin(sw);

        var profile = await profileService.GetAsync(ct).ConfigureAwait(false);
        if (profile is null)
        {
            // No uploads yet — return zeroed tally.
            return Success(sw, new
            {
                cspProfileId = (Guid?)null,
                documentsUploaded = 0,
                componentsExtracted = 0,
                capabilitiesMapped = 0,
                capabilitiesNeedsReview = 0,
                aiMappingAvailable = true,
                files = Array.Empty<object>(),
            });
        }

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var components = await db.CspInheritedComponents
            .Where(c => c.CspProfileId == profile.Id && (c.SourceArtifactReference == null
                || !c.SourceArtifactReference.StartsWith("package:")))
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.SourceFileName,
                c.SourceFormat,
                Mapped = c.Capabilities
                    .Count(x => x.Status == CspInheritedCapabilityStatus.Mapped),
                NeedsReview = c.Capabilities
                    .Count(x => x.Status == CspInheritedCapabilityStatus.NeedsReview),
            })
            .ToListAsync(ct).ConfigureAwait(false);

        // Group by source file so the UI can show one row per uploaded document.
        var fileGroups = components
            .GroupBy(c => new { c.SourceFileName, c.SourceFormat })
            .Select(g => new
            {
                fileName = g.Key.SourceFileName ?? "(unknown)",
                sourceFormat = g.Key.SourceFormat.ToString(),
                componentsExtracted = g.Count(),
                capabilitiesMapped = g.Sum(x => x.Mapped),
                capabilitiesNeedsReview = g.Sum(x => x.NeedsReview),
            })
            .ToList();

        var packages = db.CspPackages.Where(x => x.ProviderId == profile.Id).Select(x => x.Id);
        var uploaded = await db.CspPackageEntries.CountAsync(x => packages.Contains(x.PackageId) && x.IsOriginal, ct);
        var stagedComponents = await db.CspPackageCandidates.CountAsync(x => packages.Contains(x.PackageId) && x.Type == "Component", ct);
        var stagedCapabilities = await db.CspPackageCandidates.CountAsync(x => packages.Contains(x.PackageId) && x.Type == "Capability", ct);

        return Success(sw, new
        {
            cspProfileId = (Guid?)profile.Id,
            documentsUploaded = fileGroups.Count + uploaded,
            componentsExtracted = components.Count + stagedComponents,
            capabilitiesMapped = components.Sum(c => c.Mapped),
            capabilitiesNeedsReview = components.Sum(c => c.NeedsReview) + stagedCapabilities,
            aiMappingAvailable = false,
            files = fileGroups,
        });
    }

    // ─── request DTOs ──────────────────────────────────────────────────────

    public sealed record IdentityRequest(string? LegalEntityName, string? DisplayName, string? LogoUrl);

    public sealed record SupportRequest(string? PrimarySupportEmail, string? SupportPhone);

    public sealed record ClassificationRequest(string? DefaultClassificationFloor);
}
