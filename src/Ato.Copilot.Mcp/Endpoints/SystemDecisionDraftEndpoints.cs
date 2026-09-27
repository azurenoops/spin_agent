using System.Security.Cryptography;
using System.Text;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static class SystemDecisionDraftEndpoints
{
    public static IEndpointRouteBuilder MapSystemDecisionDraftEndpoints(this IEndpointRouteBuilder app)
    {
        var decisions = app.MapGroup("/api/dashboard/systems/{systemId}/authorization").RequireAuthorization();
        decisions.MapGet("/record-context", async (string systemId, AtoCopilotContext db, ITenantContext tenant,
            HttpContext http, [FromQuery] int? page, CancellationToken ct) =>
        {
            var p = page ?? 1;
            if (p < 1 || p > 100000) return Results.BadRequest(new { error = "Invalid context page." });
            if (!await db.RegisteredSystems.AnyAsync(x => x.Id == systemId && x.TenantId == tenant.EffectiveTenantId && x.IsActive, ct))
                return Results.NotFound();
            var sources = db.EvidenceArtifacts.AsNoTracking().Where(x => x.RegisteredSystemId == systemId
                && x.TenantId == tenant.EffectiveTenantId && !x.IsDeleted && x.ContentHash != null && x.ContentHash != "");
            var packages = db.AuthorizationPackages.AsNoTracking().Where(x => x.RegisteredSystemId == systemId
                && x.TenantId == tenant.EffectiveTenantId && x.Status == PackageStatus.Completed && x.ContentHash != null && x.ContentHash != ""
                && x.FilePath != null && x.FilePath != "");
            // SQLite cannot order/compare DateTimeOffset; filter this system's metadata after tenant-scoped projection.
            var packageMetadata = await packages.OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.ContentHash, x.GeneratedAt, x.Purpose, x.ExpiresAt }).ToListAsync(ct);
            var availablePackages = packageMetadata.Where(x => x.ExpiresAt > DateTimeOffset.UtcNow).ToArray();
            var records = db.AuthorizationDecisions.AsNoTracking().Where(x => x.RegisteredSystemId == systemId && x.TenantId == tenant.EffectiveTenantId);
            var canRecord = await CanWriteAsync(http, tenant, systemId, decision: true, ct);
            return Results.Ok(new
            {
                systemId, canRecord, page = p, pageSize = 50,
                sourceTotal = await sources.CountAsync(ct), packageTotal = availablePackages.Length, recordTotal = await records.CountAsync(ct),
                activeDecisionId = await records.Where(x => x.IsActive).Select(x => x.Id).SingleOrDefaultAsync(ct),
                sourceEvidence = await sources.OrderByDescending(x => x.UploadedAt).ThenBy(x => x.Id).Skip((p - 1) * 50).Take(50)
                    .Select(x => new { x.Id, x.FileName, x.ContentHash }).ToListAsync(ct),
                completedPackages = availablePackages.Skip((p - 1) * 50).Take(50)
                    .Select(x => new { x.Id, x.ContentHash, x.GeneratedAt, x.Purpose }).ToArray(),
                records = await records.OrderByDescending(x => x.DecisionDate).ThenBy(x => x.Id).Skip((p - 1) * 50).Take(50)
                    .Select(x => new { x.Id, decisionType = x.DecisionType.ToString(), x.DecisionDate, x.ExpirationDate, x.IsActive,
                        x.ExternalIssuingAuthority, x.SourceEvidenceId, x.SourceEvidenceHash, x.BaselinePackageId, x.BaselinePackageHash,
                        x.RecordedBy, x.RecordedAt, x.TermsAndConditions }).ToListAsync(ct),
            });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        decisions.MapGet("/records/{recordId}", async (string systemId, string recordId, AtoCopilotContext db, ITenantContext tenant, CancellationToken ct) =>
        {
            var record = await db.AuthorizationDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == recordId
                && x.RegisteredSystemId == systemId && x.TenantId == tenant.EffectiveTenantId, ct);
            return record is null ? Results.NotFound() : Results.Ok(record);
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        decisions.MapPost("/records", async (string systemId, ExternalAuthorizationRecordInput input,
            IAuthorizationService service, ICurrentUserService actor, CancellationToken ct) =>
        {
            try
            {
                var result = await service.RecordExternalAuthorizationAsync(systemId, input, actor.CurrentUserId, actor.CurrentUserName, ct);
                return Results.Created($"/api/dashboard/systems/{systemId}/authorization/records/{result.Id}", result);
            }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "The decision context changed. Refresh and review again." }); }
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.DecideAuthorization, Policies.AuthorizationDecisionIssuer);

        var sap = app.MapGroup("/api/v1/systems/{systemId}/sap").RequireAuthorization();
        sap.MapGet("/draft", async (string systemId, AtoCopilotContext db, ITenantContext tenant, HttpContext http, CancellationToken ct) =>
        {
            var plan = await db.SecurityAssessmentPlans.AsNoTracking().Where(x => x.RegisteredSystemId == systemId && x.TenantId == tenant.EffectiveTenantId)
                .OrderBy(x => x.Status).ThenByDescending(x => x.GeneratedAt).FirstOrDefaultAsync(ct);
            if (!await db.RegisteredSystems.AnyAsync(x => x.Id == systemId && x.TenantId == tenant.EffectiveTenantId && x.IsActive, ct))
                return Results.NotFound();
            var allowed = await CanWriteAsync(http, tenant, systemId, decision: false, ct);
            return plan is null ? Results.Ok(new { systemId, sapId = (string?)null, status = "None", canEdit = false, canCreate = allowed })
                : Results.Ok(Project(plan, allowed));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        sap.MapPut("/{sapId}/draft", async (string systemId, string sapId, SapDraftFields input, AtoCopilotContext db,
            ITenantContext tenant, HttpContext http, ISapService service, ICurrentUserService actor, CancellationToken ct) =>
        {
            if (!await CanWriteAsync(http, tenant, systemId, decision: false, ct)) return Results.Forbid();
            var existing = await db.SecurityAssessmentPlans.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == sapId && x.RegisteredSystemId == systemId && x.TenantId == tenant.EffectiveTenantId, ct);
            if (existing is null) return Results.NotFound();
            if (existing.Status != SapStatus.Draft || input.ExpectedContentHash != Hash(existing.Content))
                return Results.Conflict(new { error = "The assessment plan changed or is finalized. Refresh before editing." });
            if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 500 || input.AssessmentLead?.Length > 200
                || input.ScopeNotes?.Length > 4000 || input.AssessmentApproach?.Length > 4000)
                return Results.BadRequest(new { error = "Review the assessment title, lead, scope and approach lengths." });
            try
            {
                await service.UpdateSapAsync(new SapUpdateInput(sapId, ScopeNotes: input.ScopeNotes,
                    Title: input.Title, AssessmentLead: input.AssessmentLead, AssessmentApproach: input.AssessmentApproach,
                    ExpectedContentHash: input.ExpectedContentHash), ct);
                var updated = await db.SecurityAssessmentPlans.AsNoTracking().SingleAsync(x => x.Id == sapId, ct);
                db.DashboardActivities.Add(new DashboardActivity { TenantId = tenant.EffectiveTenantId, RegisteredSystemId = systemId,
                    EventType = "AssessmentPlanDraftUpdated", Actor = actor.CurrentUserId, Summary = "Updated assessment plan title, lead, scope and approach.",
                    RelatedEntityType = nameof(SecurityAssessmentPlan), RelatedEntityId = sapId });
                await db.SaveChangesAsync(ct);
                return Results.Ok(Project(updated, true));
            }
            catch (InvalidOperationException error) { return Results.Conflict(new { error = error.Message }); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "The SAP changed. Refresh and review again." }); }
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.GenerateSap);
        return app;
    }

    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    private static object Project(SecurityAssessmentPlan plan, bool canEdit) => new
    {
        sapId = plan.Id, systemId = plan.RegisteredSystemId, plan.Title, plan.AssessmentLead, plan.ScopeNotes, plan.AssessmentApproach,
        status = plan.Status.ToString(), draftHash = Hash(plan.Content), canEdit = canEdit && plan.Status == SapStatus.Draft, canCreate = canEdit,
    };
    private static async Task<bool> CanWriteAsync(HttpContext http, ITenantContext tenant, string systemId, bool decision, CancellationToken ct)
    {
        if (tenant.IsWorkspaceRequest)
        {
            var access = await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
                .GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct);
            return access.Permissions.CanRead && (decision ? access.Permissions.CanDecideAuthorization : access.Permissions.CanGenerateSap);
        }
        return decision ? http.User.IsInRole(ComplianceRoles.AuthorizingOfficial)
            : http.User.IsInRole(ComplianceRoles.SecurityLead) || http.User.IsInRole(ComplianceRoles.Auditor);
    }
}

public sealed record SapDraftFields(string Title, string? AssessmentLead, string? ScopeNotes, string? AssessmentApproach, string ExpectedContentHash);
