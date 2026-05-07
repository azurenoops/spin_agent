using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ato.Copilot.Agents.Compliance.Services.Onboarding.Cascade;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Onboarding;
using Ato.Copilot.Core.Models.Onboarding;
using Ato.Copilot.Core.Onboarding;
using Ato.Copilot.Mcp.Authorization;

namespace Ato.Copilot.Mcp.Endpoints.Onboarding;

/// <summary>
/// Cross-kind imports management view (T130 / FR-092..FR-095). Implements
/// <c>contracts/onboarding-api.yaml</c> for `/api/onboarding/imports*` and
/// `/api/onboarding/dependencies/{id}/rerun`.
/// </summary>
public static class ImportedDocumentsEndpoints
{
    public static IEndpointRouteBuilder MapImportedDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/onboarding")
            .WithTags("Onboarding")
            .RequireAuthorization(OnboardingAdministratorRequirement.PolicyName)
            .DisableAntiforgery();

        group.MapGet("/imports", async (
                HttpContext http,
                IWizardArtifactInventoryService inventory,
                string? kind, int? page, int? pageSize,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                ArtifactSourceKind? filter = null;
                if (!string.IsNullOrWhiteSpace(kind))
                {
                    if (!Enum.TryParse<ArtifactSourceKind>(kind, ignoreCase: true, out var parsed))
                        return Envelope.Failure("WIZARD_INVALID_FILTER", $"Unknown kind '{kind}'.");
                    filter = parsed;
                }
                var result = await inventory.ListAsync(tenantId, filter, page ?? 1, pageSize ?? 50, ct);
                return Results.Ok(new
                {
                    ok = true,
                    data = new
                    {
                        items = result.Items,
                        page = result.Page,
                        pageSize = result.PageSize,
                        total = result.TotalCount,
                    },
                });
            });

        group.MapGet("/imports/{id:guid}/dependencies", async (
                HttpContext http,
                Guid id,
                IDbContextFactory<AtoCopilotContext> factory,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                await using var db = await factory.CreateDbContextAsync(ct);
                var rows = await db.WizardArtifactDependencies
                    .Where(d => d.TenantId == tenantId && d.SourceArtifactId == id)
                    .OrderByDescending(d => d.IsStale)
                    .ThenByDescending(d => d.DerivedAt)
                    .Select(d => new
                    {
                        id = d.Id,
                        sourceArtifactType = d.SourceArtifactType.ToString(),
                        sourceArtifactId = d.SourceArtifactId,
                        sourceVersionTag = d.SourceVersionTag,
                        dependentType = d.DependentType.ToString(),
                        dependentId = d.DependentId,
                        derivedAt = d.DerivedAt,
                        isStale = d.IsStale,
                        staleSince = d.StaleSince,
                        staleReason = d.StaleReason,
                        lastReRunJobId = d.LastReRunJobId,
                    })
                    .ToListAsync(ct);
                return Results.Ok(new { ok = true, data = rows });
            });

        group.MapPost("/dependencies/{id:guid}/rerun", async (
                HttpContext http,
                Guid id,
                IDbContextFactory<AtoCopilotContext> factory,
                IWizardJobRunner jobs,
                CancellationToken ct) =>
            {
                if (!TryGetTenantId(http.User, out var tenantId)) return Forbidden();
                if (!TryGetSubject(http.User, out var actorId)) return Forbidden();

                await using var db = await factory.CreateDbContextAsync(ct);
                var dep = await db.WizardArtifactDependencies
                    .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId, ct);
                if (dep is null) return Results.NotFound(new { ok = false, errorCode = "NotFound" });

                var jobType = dep.DependentType switch
                {
                    ArtifactDependentKind.SspExport => WizardJobType.ExportRerender,
                    ArtifactDependentKind.SarExport => WizardJobType.ExportRerender,
                    ArtifactDependentKind.SapExport => WizardJobType.ExportRerender,
                    ArtifactDependentKind.CrmExport => WizardJobType.ExportRerender,
                    ArtifactDependentKind.HwSwExport => WizardJobType.ExportRerender,
                    _ => WizardJobType.ImportRerender,
                };
                var job = await jobs.EnqueueAsync(jobType, tenantId, actorId,
                    new ExportRerenderJobHandler.RerenderPayload(dep.Id), ct);

                return Results.Accepted(
                    $"/api/onboarding/jobs/{job.Id:D}",
                    new { ok = true, data = new { jobId = job.Id } });
            });

        return app;
    }

    private static bool TryGetTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var raw = user.FindFirstValue("tid")
            ?? user.FindFirstValue("http://schemas.microsoft.com/identity/claims/tenantid");
        return Guid.TryParse(raw, out tenantId);
    }

    private static bool TryGetSubject(ClaimsPrincipal user, out Guid subjectId)
    {
        var raw = user.FindFirstValue("oid") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out subjectId);
    }

    private static IResult Forbidden() => Envelope.Failure(
        WizardErrorCodes.AuthForbidden,
        "You do not have permission to use the onboarding wizard.",
        suggestion: "Sign in with an account that holds the Administrator role for your tenant.");
}
