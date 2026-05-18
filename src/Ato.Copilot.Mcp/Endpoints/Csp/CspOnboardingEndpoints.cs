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
        group.MapPost("/atos/upload", PostAtosUploadAsync)
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
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

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
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

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
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

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
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

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
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        try
        {
            var actor = ResolveActor(http);
            var profile = await service.SubmitAsync(actor, ct);

            // T209 [US9 / FR-103 / FR-104]: any CspInheritedComponent rows
            // still in Status = Draft for THIS profile transition to
            // Published in the same logical transaction as the profile
            // flipping to OnboardingState = Active. We use a load + save
            // pattern (matching CspInheritedComponentService.PublishAsync)
            // rather than ExecuteUpdateAsync because (a) the scoped
            // SaveChangesInterceptor still needs to attach RowVersion /
            // UpdatedBy / UpdatedAt audit fields, and (b) the existing
            // value-converter on CspInheritedCapability.MappedNistControlIds
            // makes ExecuteUpdate translation brittle on SQLite.
            await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var draftComponents = await db.CspInheritedComponents
                .Where(c => c.CspProfileId == profile.Id
                    && c.Status == CspInheritedComponentStatus.Draft)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (draftComponents.Count > 0)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var component in draftComponents)
                {
                    component.Status = CspInheritedComponentStatus.Published;
                    component.UpdatedAt = now;
                    component.UpdatedBy = actor;
                }
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

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

    /// <summary>Allow-list per FR-100. Anything else returns 400 UNSUPPORTED_ATO_DOCUMENT.</summary>
    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/json",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/zip",
    };

    private static async Task<IResult> PostAtosUploadAsync(
        HttpContext http,
        ITenantContext tenantCtx,
        ICspProfileService profileService,
        ICspAtoDocumentParser parser,
        ICspComponentExtractionService extractionService,
        ICspCapabilityMappingService mappingService,
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IOptions<CspInheritedOptions> options,
        IOptions<DeploymentOptions> deployment,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        // ── Multipart pre-flight ────────────────────────────────────────
        if (!http.Request.HasFormContentType)
        {
            return ValidationError(sw, "Request must be multipart/form-data with a 'files' part.");
        }

        IFormFileCollection formFiles;
        try
        {
            var form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
            formFiles = form.Files;
        }
        catch (Microsoft.AspNetCore.Http.BadHttpRequestException badReq)
            when (badReq.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Error(StatusCodes.Status413PayloadTooLarge, "ATO_DOCUMENT_TOO_LARGE",
                "File exceeds the 50 MB per-file upload limit.");
        }

        if (formFiles.Count == 0)
        {
            return ValidationError(sw, "At least one file must be supplied in the 'files' part.");
        }

        // ── Per-file validation ─────────────────────────────────────────
        foreach (var f in formFiles)
        {
            if (f.Length > options.Value.MaxFileSizeBytes)
            {
                return Error(StatusCodes.Status413PayloadTooLarge, "ATO_DOCUMENT_TOO_LARGE",
                    $"File '{f.FileName}' is {f.Length} bytes; max is {options.Value.MaxFileSizeBytes}.");
            }
            if (string.IsNullOrWhiteSpace(f.ContentType)
                || !SupportedContentTypes.Contains(NormalizeContentType(f.ContentType)))
            {
                return Error(StatusCodes.Status400BadRequest, "UNSUPPORTED_ATO_DOCUMENT",
                    $"Content type '{f.ContentType}' is not in the ATO-document allow-list.");
            }
        }

        // ── EnsureCreatedAsync to obtain a CspProfileId ─────────────────
        var actor = ResolveActor(http);
        CspProfile profile;
        try
        {
            profile = await profileService.EnsureCreatedAsync(actor, ct).ConfigureAwait(false);
        }
        catch (CspAlreadyOnboardedException)
        {
            // Already-onboarded profiles upload via /api/csp/inherited-components/import,
            // not the wizard endpoint. Surface the same 409 the other wizard
            // mutations use so the client can route the user.
            return AlreadyOnboarded(sw);
        }

        // ── Orchestrate parse → extract → map → persist ─────────────────
        var uploadFiles = new List<CspAtoUploadHelpers.UploadFile>(formFiles.Count);
        foreach (var f in formFiles)
        {
            uploadFiles.Add(new CspAtoUploadHelpers.UploadFile(
                Content: f.OpenReadStream(),
                ContentType: NormalizeContentType(f.ContentType ?? string.Empty),
                FileName: f.FileName));
        }

        try
        {
            var result = await CspAtoUploadHelpers.OrchestrateAsync(
                files: uploadFiles,
                cspProfileId: profile.Id,
                actor: actor,
                parser: parser,
                extractionService: extractionService,
                mappingService: mappingService,
                options: options,
                persistCapabilities: async (caps, token) =>
                {
                    await using var db = await contextFactory.CreateDbContextAsync(token).ConfigureAwait(false);
                    db.CspInheritedCapabilities.AddRange(caps);
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                },
                ct: ct).ConfigureAwait(false);

            return Success(sw, BuildUploadDto(result));
        }
        catch (CspAtoUploadHelpers.UploadException ex)
        {
            return Error(ex.StatusCode, ex.ErrorCode, ex.Message);
        }
        finally
        {
            foreach (var f in uploadFiles)
            {
                try { f.Content.Dispose(); } catch { /* best-effort */ }
            }
        }
    }

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
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

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
            .Where(c => c.CspProfileId == profile.Id)
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

        return Success(sw, new
        {
            cspProfileId = (Guid?)profile.Id,
            documentsUploaded = fileGroups.Count,
            componentsExtracted = components.Count,
            capabilitiesMapped = components.Sum(c => c.Mapped),
            capabilitiesNeedsReview = components.Sum(c => c.NeedsReview),
            aiMappingAvailable = true,
            files = fileGroups,
        });
    }

    /// <summary>Strip parameters (e.g. <c>; charset=utf-8</c>) and lowercase.</summary>
    private static string NormalizeContentType(string contentType)
    {
        var idx = contentType.IndexOf(';');
        var head = idx >= 0 ? contentType[..idx] : contentType;
        return head.Trim().ToLowerInvariant();
    }

    private static object BuildUploadDto(CspAtoUploadHelpers.UploadResult result) => new
    {
        documentsAccepted = result.DocumentsAccepted,
        componentsExtracted = result.ComponentsExtracted,
        capabilitiesMapped = result.CapabilitiesMapped,
        capabilitiesNeedsReview = result.CapabilitiesNeedsReview,
        aiMappingAvailable = result.AiMappingAvailable,
        files = result.Files.Select(f => new
        {
            fileName = f.FileName,
            sourceFormat = f.SourceFormat,
            componentsExtracted = f.ComponentsExtracted,
            capabilitiesMapped = f.CapabilitiesMapped,
            capabilitiesNeedsReview = f.CapabilitiesNeedsReview,
        }).ToArray(),
    };

    // ─── request DTOs ──────────────────────────────────────────────────────

    public sealed record IdentityRequest(string? LegalEntityName, string? DisplayName, string? LogoUrl);

    public sealed record SupportRequest(string? PrimarySupportEmail, string? SupportPhone);

    public sealed record ClassificationRequest(string? DefaultClassificationFloor);
}
