using System.Diagnostics;
using System.Security.Claims;
using Ato.Copilot.Core.Configuration.Tenancy;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

/// <summary>
/// CSP-Inherited Components management endpoints (Feature 048 US9 / T208).
/// Mirrors
/// <c>specs/048-tenant-isolation/contracts/csp-inherited-components.openapi.yaml</c>.
/// </summary>
/// <remarks>
/// <para>
/// Auth model:
/// <list type="bullet">
///   <item><b>Read</b> endpoints (GET list / GET detail / GET capabilities) —
///         every authenticated tenant user. Non-CSP-Admins see only
///         <see cref="CspInheritedComponentStatus.Published"/> rows
///         (FR-104). CSP-Admins additionally see Draft + Archived.</item>
///   <item><b>Write</b> endpoints (PATCH / DELETE / POST publish / POST
///         remap / PATCH review / POST import) — <c>CSP.Admin</c> only;
///         everyone else gets <c>403 FORBIDDEN_NOT_CSP_ADMIN</c>
///         (FR-106).</item>
/// </list>
/// </para>
/// <para>
/// SingleTenant short-circuit — when <c>DeploymentOptions.Mode</c> is
/// <see cref="DeploymentMode.SingleTenant"/>, every route returns
/// <c>404 SINGLE_TENANT_MODE</c> (FR-093).
/// </para>
/// <para>
/// CSP-onboarding gate — the post-onboarding <c>POST /import</c> endpoint
/// returns <c>503 CSP_ONBOARDING_INCOMPLETE</c> when
/// <see cref="CspProfile.OnboardingState"/> is not
/// <see cref="OnboardingState.Active"/> (FR-104). The list/detail/read
/// endpoints stay available so dashboards and scripts can keep polling.
/// </para>
/// </remarks>
public static class CspInheritedComponentEndpoints
{
    public static IEndpointRouteBuilder MapCspInheritedComponentEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/inherited-components")
            .WithTags("CSP Inherited Components");

        group.MapGet("", ListAsync).WithName("ListCspInheritedComponents");
        group.MapGet("/{componentId:guid}", GetAsync).WithName("GetCspInheritedComponent");
        group.MapPost("", CreateAsync).WithName("CreateCspInheritedComponent");
        group.MapPatch("/{componentId:guid}", PatchAsync).WithName("PatchCspInheritedComponent");
        group.MapDelete("/{componentId:guid}", DeleteAsync).WithName("DeleteCspInheritedComponent");
        group.MapPost("/{componentId:guid}/publish", PublishAsync).WithName("PublishCspInheritedComponent");
        group.MapPost("/{componentId:guid}/remap", RemapAsync).WithName("RemapCspInheritedComponent");
        group.MapGet("/{componentId:guid}/capabilities", GetCapabilitiesAsync)
            .WithName("GetCspInheritedComponentCapabilities");
        group.MapPost("/{componentId:guid}/capabilities", AddCapabilityAsync)
            .WithName("AddCspInheritedCapability");
        group.MapPatch("/{componentId:guid}/capabilities/{capabilityId:guid}/review", ReviewAsync)
            .WithName("ReviewCspInheritedCapability");

        // Post-onboarding import endpoint — same pipeline as the wizard
        // upload but gated on CspProfile.OnboardingState = Active.
        group.MapPost("/import", ImportAsync)
            .WithName("ImportCspInheritedComponents")
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(50L * 1024L * 1024L));

        return app;
    }

    // ─── GET / list ────────────────────────────────────────────────────

    private static async Task<IResult> ListAsync(
        HttpContext http,
        ITenantContext tenantCtx,
        ICspProfileService profileService,
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;

        var profile = await profileService.GetAsync(ct).ConfigureAwait(false);
        if (profile is null)
        {
            // No profile → no components yet. Return an empty page instead
            // of 503 because read endpoints are deliberately permissive
            // (FR-104) so dashboards can poll during the wizard.
            return Success(sw, new
            {
                items = Array.Empty<object>(),
                page = page ?? 1,
                pageSize = pageSize ?? 50,
                total = 0,
            });
        }

        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 50, 1, 200);

        await using var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var query = db.CspInheritedComponents
            .AsNoTracking()
            .Where(c => c.CspProfileId == profile.Id);

        // Role-based status filter (FR-104). Mission Owners may pass status
        // in the query string, but CSP-Admin restrictions apply.
        if (Enum.TryParse<CspInheritedComponentStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            if (!tenantCtx.IsCspAdmin && parsedStatus != CspInheritedComponentStatus.Published)
            {
                // Non-CSP-Admin attempting to view non-Published — silently
                // narrow to Published (their effective view).
                query = query.Where(c => c.Status == CspInheritedComponentStatus.Published);
            }
            else
            {
                query = query.Where(c => c.Status == parsedStatus);
            }
        }
        else if (!tenantCtx.IsCspAdmin)
        {
            query = query.Where(c => c.Status == CspInheritedComponentStatus.Published);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(c => EF.Functions.Like(c.Name, $"%{s}%")
                || EF.Functions.Like(c.Description, $"%{s}%"));
        }

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderBy(c => c.Name)
            .Skip((p - 1) * ps)
            .Take(ps)
            .Select(c => new
            {
                id = c.Id,
                name = c.Name,
                description = c.Description,
                componentType = c.ComponentType.ToString(),
                status = c.Status.ToString(),
                sourceFormat = c.SourceFormat.ToString(),
                sourceFileName = c.SourceFileName,
                sourceArtifactReference = c.SourceArtifactReference,
                importedAt = c.ImportedAt,
                updatedAt = c.UpdatedAt,
            })
            .ToListAsync(ct).ConfigureAwait(false);

        return Success(sw, new { items, page = p, pageSize = ps, total });
    }

    // ─── GET / detail ──────────────────────────────────────────────────

    private static async Task<IResult> GetAsync(
        Guid componentId,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspInheritedComponentService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;

        var component = await service.GetAsync(componentId, ct).ConfigureAwait(false);
        if (component is null) return NotFound(sw);

        // Mission Owners must not see Draft or Archived rows even if they
        // know the id — surface a 404 to avoid leaking existence.
        if (!tenantCtx.IsCspAdmin && component.Status != CspInheritedComponentStatus.Published)
        {
            return NotFound(sw);
        }

        return Success(sw, BuildComponentDto(component));
    }

    // ─── POST / (CSP-Admin) — manual create ─────────────────────────────

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateComponentRequest? body,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspProfileService profileService,
        ICspInheritedComponentService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        if (body is null
            || string.IsNullOrWhiteSpace(body.Name)
            || string.IsNullOrWhiteSpace(body.Description))
        {
            return ValidationError(sw, "name and description are required.");
        }
        if (!Enum.TryParse<CspComponentType>(body.ComponentType, ignoreCase: true, out var componentType))
        {
            return ValidationError(sw,
                $"componentType '{body.ComponentType}' is not a valid CspComponentType.");
        }

        // Onboarding gate (FR-104) mirrors the /import endpoint — manual
        // create is conceptually the same write as import, just without a
        // file payload, so the same gate applies.
        var profile = await profileService.GetAsync(ct).ConfigureAwait(false);
        if (profile is null || profile.OnboardingState != OnboardingState.Active)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "CSP_ONBOARDING_INCOMPLETE",
                "CSP onboarding must be completed before creating CSP-inherited components.");
        }

        try
        {
            var actor = ResolveActor(http);
            var created = await service.CreateAsync(
                profile.Id,
                body.Name,
                body.Description,
                componentType,
                actor,
                ct).ConfigureAwait(false);
            return Success(sw, BuildComponentDto(created));
        }
        catch (ArgumentException ex)
        {
            return ValidationError(sw, ex.Message);
        }
    }

    // ─── PATCH /{id} (CSP-Admin) ───────────────────────────────────────

    private static async Task<IResult> PatchAsync(
        Guid componentId,
        [FromBody] PatchComponentRequest body,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspInheritedComponentService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        var existing = await service.GetAsync(componentId, ct).ConfigureAwait(false);
        if (existing is null) return NotFound(sw);

        var componentType = body.ComponentType is { Length: > 0 }
            && Enum.TryParse<CspComponentType>(body.ComponentType, ignoreCase: true, out var parsed)
                ? parsed
                : existing.ComponentType;
        var newName = body.Name ?? existing.Name;
        var newDescription = body.Description ?? existing.Description;

        try
        {
            var actor = ResolveActor(http);
            var updated = await service.UpdateAsync(
                componentId,
                newName,
                newDescription,
                componentType,
                rowVersion: existing.RowVersion,
                actor,
                ct).ConfigureAwait(false);
            return Success(sw, BuildComponentDto(updated));
        }
        catch (ArgumentException ex)
        {
            return ValidationError(sw, ex.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error(StatusCodes.Status412PreconditionFailed,
                "ROW_VERSION_MISMATCH",
                "Component was modified by another user; reload and retry.");
        }
    }

    // ─── DELETE /{id} (CSP-Admin) — archive ─────────────────────────────

    private static async Task<IResult> DeleteAsync(
        Guid componentId,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspInheritedComponentService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        var existing = await service.GetAsync(componentId, ct).ConfigureAwait(false);
        if (existing is null) return NotFound(sw);

        try
        {
            var actor = ResolveActor(http);
            await service.ArchiveAsync(componentId, actor, ct).ConfigureAwait(false);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(sw, "INVALID_TRANSITION", ex.Message);
        }
    }

    // ─── POST /{id}/publish (CSP-Admin) ─────────────────────────────────

    private static async Task<IResult> PublishAsync(
        Guid componentId,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspInheritedComponentService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        var existing = await service.GetAsync(componentId, ct).ConfigureAwait(false);
        if (existing is null) return NotFound(sw);

        try
        {
            var actor = ResolveActor(http);
            var published = await service.PublishAsync(componentId, actor, ct).ConfigureAwait(false);
            return Success(sw, BuildComponentDto(published));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(sw, "INVALID_TRANSITION", ex.Message);
        }
    }

    // ─── POST /{id}/remap (CSP-Admin) ───────────────────────────────────

    private static async Task<IResult> RemapAsync(
        Guid componentId,
        [FromBody] RemapRequest? body,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspInheritedComponentService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        var existing = await service.GetAsync(componentId, ct).ConfigureAwait(false);
        if (existing is null) return NotFound(sw);

        var preserveHumanMappings = !(body?.ReplaceMapped ?? false);
        var actor = ResolveActor(http);
        var result = await service.RemapAsync(
            componentId,
            preserveHumanMappings,
            actor,
            ct).ConfigureAwait(false);
        return Success(sw, new
        {
            mapped = result.Mapped.Count,
            needsReview = result.NeedsReview.Count,
            aiMappingAvailable = result.AiMappingAvailable,
            aiMappingFailureReason = result.AiMappingFailureReason,
        });
    }

    // ─── GET /{id}/capabilities ────────────────────────────────────────

    private static async Task<IResult> GetCapabilitiesAsync(
        Guid componentId,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspInheritedComponentService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;

        var component = await service.GetAsync(componentId, ct).ConfigureAwait(false);
        if (component is null) return NotFound(sw);
        if (!tenantCtx.IsCspAdmin && component.Status != CspInheritedComponentStatus.Published)
        {
            return NotFound(sw);
        }

        var capabilities = component.Capabilities
            .Select(BuildCapabilityDto)
            .ToArray();
        return Success(sw, capabilities);
    }

    // ─── POST /{id}/capabilities (CSP-Admin) — manual create ─────────────

    private static async Task<IResult> AddCapabilityAsync(
        Guid componentId,
        [FromBody] AddCapabilityRequest? body,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspInheritedComponentService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        if (body is null
            || string.IsNullOrWhiteSpace(body.Name)
            || string.IsNullOrWhiteSpace(body.Description))
        {
            return ValidationError(sw, "name and description are required.");
        }
        if (body.MappedNistControlIds is null || body.MappedNistControlIds.Length == 0)
        {
            return ValidationError(sw, "mappedNistControlIds must be a non-empty array.");
        }

        try
        {
            var actor = ResolveActor(http);
            var capability = await service.AddCapabilityAsync(
                componentId,
                body.Name,
                body.Description,
                body.MappedNistControlIds,
                actor,
                ct).ConfigureAwait(false);
            return Success(sw, BuildCapabilityDto(capability));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(sw);
        }
        catch (ArgumentException ex)
        {
            return ValidationError(sw, ex.Message);
        }
    }

    // ─── PATCH /{id}/capabilities/{capId}/review (CSP-Admin) ────────────

    private static async Task<IResult> ReviewAsync(
        Guid componentId,
        Guid capabilityId,
        [FromBody] ReviewCapabilityRequest body,
        HttpContext http,
        ITenantContext tenantCtx,
        ICspInheritedComponentService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        if (body is null || body.MappedNistControlIds is null || body.MappedNistControlIds.Length == 0)
        {
            return ValidationError(sw, "mappedNistControlIds must be a non-empty array.");
        }

        try
        {
            var actor = ResolveActor(http);
            var capability = await service.ReviewCapabilityAsync(
                componentId,
                capabilityId,
                body.MappedNistControlIds,
                body.ReviewerNote,
                actor,
                ct).ConfigureAwait(false);
            return Success(sw, BuildCapabilityDto(capability));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(sw);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(sw, "INVALID_TRANSITION", ex.Message);
        }
    }

    // ─── POST /import (CSP-Admin, Active only) ──────────────────────────

    private static async Task<IResult> ImportAsync(
        HttpContext http,
        ITenantContext tenantCtx,
        ICspProfileService profileService,
        ICspAtoDocumentParser parser,
        ICspComponentExtractionService extractionService,
        ICspCapabilityMappingService mappingService,
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IOptions<CspInheritedOptions> options,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var singleTenantResult))
            return singleTenantResult;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        // Onboarding gate (FR-104) — only Active profiles may import.
        var profile = await profileService.GetAsync(ct).ConfigureAwait(false);
        if (profile is null || profile.OnboardingState != OnboardingState.Active)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "CSP_ONBOARDING_INCOMPLETE",
                "CSP onboarding must be completed before importing additional ATO documents.");
        }

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

        var actor = ResolveActor(http);
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

            // Post-onboarding imports auto-publish (FR-104) — Draft is only
            // a wizard-time staging state; outside the wizard new components
            // are immediately visible across tenants. Load + save (vs.
            // ExecuteUpdateAsync) keeps the audit interceptors and value
            // converters in play.
            await using (var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false))
            {
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
            }

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

    // ─── helpers ────────────────────────────────────────────────────────

    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/json",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/zip",
    };

    private static string NormalizeContentType(string contentType)
    {
        var idx = contentType.IndexOf(';');
        var head = idx >= 0 ? contentType[..idx] : contentType;
        return head.Trim().ToLowerInvariant();
    }

    private static bool ShouldShortCircuitSingleTenant(
        IOptions<DeploymentOptions> deployment, out IResult result)
    {
        if (deployment.Value.Mode == DeploymentMode.SingleTenant)
        {
            result = Error(StatusCodes.Status404NotFound, "SINGLE_TENANT_MODE",
                "CSP inherited components are unavailable in SingleTenant deployments.");
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

    private static object BuildComponentDto(CspInheritedComponent c) => new
    {
        id = c.Id,
        cspProfileId = c.CspProfileId,
        name = c.Name,
        description = c.Description,
        componentType = c.ComponentType.ToString(),
        status = c.Status.ToString(),
        sourceFormat = c.SourceFormat.ToString(),
        sourceFileName = c.SourceFileName,
        sourceArtifactReference = c.SourceArtifactReference,
        importedAt = c.ImportedAt,
        importedBy = c.ImportedBy,
        updatedAt = c.UpdatedAt,
        updatedBy = c.UpdatedBy,
        capabilityMappedCount = c.CapabilityMappedCount,
        capabilityNeedsReviewCount = c.CapabilityNeedsReviewCount,
        capabilities = c.Capabilities.Select(BuildCapabilityDto).ToArray(),
    };

    private static object BuildCapabilityDto(CspInheritedCapability cap) => new
    {
        id = cap.Id,
        cspInheritedComponentId = cap.CspInheritedComponentId,
        name = cap.Name,
        description = cap.Description,
        mappedNistControlIds = cap.MappedNistControlIds,
        mappingConfidence = cap.MappingConfidence,
        status = cap.Status.ToString(),
        mappingFailureReason = cap.MappingFailureReason,
        mappedBy = cap.MappedBy.ToString(),
        createdAt = cap.CreatedAt,
        createdBy = cap.CreatedBy,
        reviewedAt = cap.ReviewedAt,
        reviewedBy = cap.ReviewedBy,
        reviewerNote = cap.ReviewerNote,
    };

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

    private static IResult NotFound(Stopwatch sw) =>
        Results.Json(new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error = new { errorCode = "NOT_FOUND", message = "CSP-inherited component not found." },
        }, statusCode: StatusCodes.Status404NotFound);

    private static IResult Conflict(Stopwatch sw, string code, string message) =>
        Results.Json(new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error = new { errorCode = code, message },
        }, statusCode: StatusCodes.Status409Conflict);

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new
        {
            status = "error",
            error = new { errorCode = code, message },
        }, statusCode: statusCode);

    // ─── request DTOs ──────────────────────────────────────────────────

    public sealed record CreateComponentRequest(string? Name, string? Description, string? ComponentType);
    public sealed record AddCapabilityRequest(string? Name, string? Description, string[]? MappedNistControlIds);
    public sealed record PatchComponentRequest(string? Name, string? Description, string? ComponentType);
    public sealed record RemapRequest(bool ReplaceMapped);
    public sealed record ReviewCapabilityRequest(string[] MappedNistControlIds, string? ReviewerNote);
}
