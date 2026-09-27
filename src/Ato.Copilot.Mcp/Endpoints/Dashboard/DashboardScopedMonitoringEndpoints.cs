using System.Security.Claims;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Mcp.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public sealed record MonitoringRuleResponse(Guid Id, string Name, string? RegisteredSystemId,
    string? BoundaryDefinitionId, string? ReviewedScopeJson, string? BaselineReference, string? OwnerId,
    string Signal, string? TriggerCondition, int CadenceMinutes, string? SeverityOverride, string Response,
    bool IsEnabled, long Version, DateTimeOffset? LastEvaluatedAt, string CreatedBy, DateTimeOffset UpdatedAt)
{
    public static MonitoringRuleResponse From(AlertRule rule) => new(rule.Id, rule.Name, rule.RegisteredSystemId,
        rule.BoundaryDefinitionId, rule.ReviewedScopeJson, rule.BaselineReference, rule.OwnerId, rule.Signal,
        rule.TriggerCondition, rule.CadenceMinutes, rule.SeverityOverride?.ToString(), rule.Response,
        rule.IsEnabled, rule.Version, rule.LastEvaluatedAt, rule.CreatedBy, rule.UpdatedAt);
}

public static partial class DashboardEndpoints
{
    private static void MapScopedMonitoringRoutes(IEndpointRouteBuilder group)
    {
        const string root = "/systems/{systemId}/conmon";
        group.MapGet(root + "/workspace", async (string systemId, ScopedMonitoringService service,
            AtoCopilotContext db, HttpContext http, CancellationToken ct) =>
        {
            if (!await db.RegisteredSystems.AnyAsync(x => x.Id == systemId, ct)) return Results.NotFound();
            var boundaries = await db.AuthorizationBoundaryDefinitions.AsNoTracking()
                .Where(x => x.RegisteredSystemId == systemId).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
            var rules = await db.AlertRules.AsNoTracking().Where(x => x.RegisteredSystemId == systemId).ToListAsync(ct);
            var history = await db.Set<MonitoringRuleEvaluation>().AsNoTracking()
                .Where(x => x.RegisteredSystemId == systemId).ToListAsync(ct);
            var impacts = await db.Set<MonitoringImpactReview>().AsNoTracking()
                .Where(x => x.RegisteredSystemId == systemId).ToListAsync(ct);
            var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
            var access = tenant.IsWorkspaceRequest ? await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
                .GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct) : null;
            var legacyWrite = !tenant.IsWorkspaceRequest && (await http.RequestServices.GetRequiredService<IAuthorizationService>()
                .AuthorizeAsync(http.User, null, Policies.ComplianceWriter)).Succeeded;
            return Results.Ok(new { boundaries, rules = rules.Select(MonitoringRuleResponse.From), coverage = await service.CoverageAsync(systemId, ct),
                changes = await service.ChangesAsync(systemId, ct), evaluations = history.OrderByDescending(x => x.EvaluatedAt), impacts,
                canManageRules = access?.Permissions.CanManageSystem ?? legacyWrite,
                canReviewImpacts = access?.Permissions.CanReviewNarratives ?? legacyWrite });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);

        group.MapPost(root + "/rules", (string systemId, SaveMonitoringRuleRequest body, ScopedMonitoringService service,
            HttpContext http, CancellationToken ct) =>
            MonitoringResult(async () => MonitoringRuleResponse.From(await service.SaveRuleAsync(systemId, null, body, MonitoringActor(http), ct))))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem, Policies.ComplianceWriter);

        group.MapPut(root + "/rules/{ruleId:guid}", (string systemId, Guid ruleId, SaveMonitoringRuleRequest body,
            ScopedMonitoringService service, HttpContext http, CancellationToken ct) =>
            MonitoringResult(async () => MonitoringRuleResponse.From(await service.SaveRuleAsync(systemId, ruleId, body, MonitoringActor(http), ct))))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem, Policies.ComplianceWriter);

        group.MapPost(root + "/rules/{ruleId:guid}/test", (string systemId, Guid ruleId,
            ScopedMonitoringService service, CancellationToken ct) =>
            MonitoringResult(() => service.TestAsync(systemId, ruleId, ct)))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ManageSystem, Policies.ComplianceWriter);

        group.MapPost(root + "/impacts/{impactId:guid}/disposition", (string systemId, Guid impactId,
            DispositionMonitoringImpactRequest body, ScopedMonitoringService service, HttpContext http, CancellationToken ct) =>
            MonitoringResult(() => service.DispositionAsync(systemId, impactId, body, MonitoringActor(http), ct)))
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReviewNarratives, Policies.ComplianceWriter);
    }

    private static string MonitoringActor(HttpContext http) =>
        http.User.FindFirstValue("oid") ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("An authenticated actor identity is required.");

    private static async Task<IResult> MonitoringResult<T>(Func<Task<T>> operation)
    {
        try { return Results.Ok(await operation()); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message, errorCode = "INVALID_MONITORING_INPUT" }); }
        catch (System.Text.Json.JsonException) { return Results.BadRequest(new { error = "A typed monitoring condition is required.", errorCode = "INVALID_MONITORING_INPUT" }); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Record changed; reload and retry.", errorCode = "VERSION_CONFLICT" }); }
    }
}
