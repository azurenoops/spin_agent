using System.Text.Json;
using Ato.Copilot.Agents.Compliance.Services;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Mcp.Authorization;
using Ato.Copilot.Mcp.Services;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Endpoints;

public static partial class DashboardEndpoints
{
    private static void MapPackageReadinessRoutes(IEndpointRouteBuilder group)
    {
        var routes = group.MapGroup("/systems/{systemId}/package-readiness");
        routes.AddEndpointFilter(async (invocation, next) =>
        {
            invocation.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(invocation); }
            catch (ArgumentException ex) { return Results.BadRequest(new ErrorResponse { Error = ex.Message, ErrorCode = "INVALID_READINESS_REQUEST", Suggestion = "Review purpose, selection and pagination." }); }
            catch (KeyNotFoundException) { return Results.NotFound(); }
        });
        routes.MapGet("", ReadReadinessWorkspace)
            .RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        routes.MapGet("/runs/latest", async (string systemId, HttpRequest request, AtoCopilotContext db,
            PackageReadinessService service, ICurrentUserService actor, CancellationToken ct) =>
        {
            var selection = ReadinessSelection(request);
            var runs = ReadinessRuns(db, systemId, selection);
            var latest = await runs.OrderByDescending(x => x.EvaluatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(ct);
            var source = await service.ReadSourceAsync(systemId, selection, actor.CurrentUserId, ct);
            var permissions = await ReadinessPermissions(request.HttpContext, systemId, ct);
            return Results.Ok(new { systemId, selection.Purpose, selection.RetainedContext,
                selectionHash = PackageReadinessService.SelectionHash(selection),
                latestRun = latest == null ? null : ReadinessRunDto(latest, source, permissions) });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        routes.MapGet("/runs", async (string systemId, HttpRequest request, AtoCopilotContext db,
            PackageReadinessService service, ICurrentUserService actor, CancellationToken ct) =>
        {
            var selection = ReadinessSelection(request);
            var (limit, offset) = ReadinessPaging(request, 20, 100);
            var query = ReadinessRuns(db, systemId, selection);
            var totalCount = await query.CountAsync(ct);
            var runs = await query.OrderByDescending(x => x.EvaluatedAt).ThenBy(x => x.Id).Skip(offset).Take(limit).ToListAsync(ct);
            var source = await service.ReadSourceAsync(systemId, selection, actor.CurrentUserId, ct);
            var permissions = await ReadinessPermissions(request.HttpContext, systemId, ct);
            return Results.Ok(new { systemId, selection.Purpose, selection.RetainedContext,
                selectionHash = PackageReadinessService.SelectionHash(selection),
                items = runs.Select(x => ReadinessRunDto(x, source, permissions)), totalCount, limit, offset });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        routes.MapGet("/runs/{runId}", async (string systemId, string runId, HttpRequest request, AtoCopilotContext db,
            PackageReadinessService service, ICurrentUserService actor, HttpContext http, CancellationToken ct) =>
        {
            var selection = ReadinessSelection(request);
            var run = await ReadinessRuns(db, systemId, selection).SingleOrDefaultAsync(x => x.Id == runId, ct)
                ?? throw new KeyNotFoundException();
            var (limit, offset) = ReadinessPaging(request, 50, 200);
            return Results.Ok(await ReadinessRunResponse(systemId, selection, run, db, service, actor, http, limit, offset,
                request.Query["outcome"].FirstOrDefault(), ct));
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        routes.MapGet("/runs/{runId}/checks/{checkId}", async (string systemId, string runId, string checkId,
            HttpRequest request, HttpContext http, AtoCopilotContext db, CancellationToken ct) =>
        {
            var selection = ReadinessSelection(request);
            var run = await ReadinessRuns(db, systemId, selection).SingleOrDefaultAsync(x => x.Id == runId, ct)
                ?? throw new KeyNotFoundException();
            var check = PackageReadinessService.Checks(run).SingleOrDefault(x => x.Id == checkId) ?? throw new KeyNotFoundException();
            var permissions = await ReadinessPermissions(http, systemId, ct);
            return Results.Ok(new { systemId, selection.Purpose, selection.RetainedContext,
                selectionHash = run.SelectionHash, runId,
                check = check with { Action = ReadinessAction(check, permissions) } });
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
        routes.MapPost("/runs", async (string systemId, PackageReadinessSelection selection,
            AtoCopilotContext db, PackageReadinessService service, ICurrentUserService actor, HttpContext http, CancellationToken ct) =>
        {
            var run = await service.ValidateAsync(systemId, selection, actor.CurrentUserId, ct);
            return Results.Json(await ReadinessRunResponse(systemId, selection, run, db, service, actor, http, 50, 0, null, ct),
                statusCode: StatusCodes.Status201Created);
        }).RequireWorkspaceOperation(SystemWorkspaceOperation.ReadSystem, Policies.ComplianceReader);
    }

    private static PackageReadinessSelection ReadinessSelection(HttpRequest request)
    {
        var raw = request.Query["purpose"].FirstOrDefault();
        if (raw != null && (!Enum.TryParse<PackagePurpose>(raw, false, out _) || !Enum.GetNames<PackagePurpose>().Contains(raw)))
            throw new ArgumentException("Invalid package purpose.");
        var purpose = raw == null ? PackagePurpose.Legacy : Enum.Parse<PackagePurpose>(raw);
        string? Get(string name) => request.Query[name].FirstOrDefault();
        RetainedPackageSelection? retained = null;
        if (new[] { "baselinePackageId", "baselineContentHash", "authorizationDecisionId", "changePreviewId",
            "changeContentHash", "expectedDecisionSnapshotHash", "expectedSourceContextHash" }.Any(x => request.Query.ContainsKey(x)))
        {
            var preview = Get("changePreviewId");
            if (preview != null && !Guid.TryParse(preview, out _)) throw new ArgumentException("Invalid retained preview ID.");
            retained = new(Get("baselinePackageId") ?? "", Get("baselineContentHash") ?? "", Get("authorizationDecisionId") ?? "",
                preview == null ? null : Guid.Parse(preview), Get("changeContentHash"),
                Get("expectedDecisionSnapshotHash"), Get("expectedSourceContextHash"));
        }
        var selection = new PackageReadinessSelection(purpose, retained);
        PackageReadinessService.ValidateSelection(selection);
        return selection;
    }

    private static IQueryable<PackageReadinessRun> ReadinessRuns(AtoCopilotContext db, string systemId, PackageReadinessSelection selection)
    {
        var hash = PackageReadinessService.SelectionHash(selection);
        return db.PackageReadinessRuns.AsNoTracking().Where(x => x.RegisteredSystemId == systemId
            && x.Purpose == selection.Purpose && x.SelectionHash == hash);
    }

    private static (int Limit, int Offset) ReadinessPaging(HttpRequest request, int defaultLimit, int max)
    {
        var limit = defaultLimit; var offset = 0;
        if (request.Query.ContainsKey("limit") && !int.TryParse(request.Query["limit"], out limit)
            || request.Query.ContainsKey("offset") && !int.TryParse(request.Query["offset"], out offset)
            || limit < 1 || limit > max || offset < 0) throw new ArgumentException("Invalid readiness page.");
        return (limit, offset);
    }

    private static PackageReadinessRunDto ReadinessRunDto(PackageReadinessRun run, PackageReadinessService.SourceState source,
        SystemWorkspacePermissions permissions)
    {
        var checks = PackageReadinessService.Checks(run);
        var recommended = OrderedReadinessChecks(checks.Select(x => x with { Action = ReadinessAction(x, permissions) }))
            .FirstOrDefault(x => x.Outcome is "Blocking" or "FollowUp" || x.Required && x.Outcome == "Unavailable");
        return new(run.Id, run.Outcome, Utc(run.StartedAt), Utc(run.EvaluatedAt), run.EvaluatedBy, run.SourceHash, run.SourceHashAfter,
            run.RuleVersion, PackageReadinessCounts.From(checks), recommended?.Id,
            run.FailureJson == null ? null : JsonSerializer.Deserialize<PackageReadinessFailure>(run.FailureJson, PackageReadinessService.Json),
            PackageReadinessService.Freshness(run, source));
    }

    private static IEnumerable<PackageReadinessCheck> OrderedReadinessChecks(IEnumerable<PackageReadinessCheck> checks) =>
        checks.OrderBy(x => x.Outcome == "Blocking" || x.Required && x.Outcome == "Unavailable" ? 0 : x.Outcome == "FollowUp" ? 1 : 2)
            .ThenByDescending(x => x.Action.CanEdit).ThenBy(x => x.Id, StringComparer.Ordinal);

    private static async Task<object> ReadinessRunResponse(string systemId, PackageReadinessSelection selection, PackageReadinessRun run,
        AtoCopilotContext db, PackageReadinessService service, ICurrentUserService actor, HttpContext http,
        int limit, int offset, string? outcome, CancellationToken ct)
    {
        if (outcome != null && !new[] { "Passed", "Blocking", "FollowUp", "NotApplicable", "Unavailable" }.Contains(outcome))
            throw new ArgumentException("Invalid check outcome.");
        var permissions = await ReadinessPermissions(http, systemId, ct);
        var all = PackageReadinessService.Checks(run).Select(x => x with { Action = ReadinessAction(x, permissions) });
        var selected = OrderedReadinessChecks(all).Where(x => outcome == null || x.Outcome == outcome).ToArray();
        var source = await service.ReadSourceAsync(systemId, selection, actor.CurrentUserId, ct);
        return new { systemId, selection.Purpose, selection.RetainedContext, selectionHash = run.SelectionHash,
            run = ReadinessRunDto(run, source, permissions), checks = new PackageReadinessPage<PackageReadinessCheck>(selected.Skip(offset).Take(limit).ToArray(), selected.Length, limit, offset) };
    }

    private static async Task<SystemWorkspacePermissions> ReadinessPermissions(HttpContext http, string systemId, CancellationToken ct)
    {
        var tenant = http.RequestServices.GetRequiredService<ITenantContext>();
        return (await http.RequestServices.GetRequiredService<ISystemWorkspaceAccessService>()
            .GetAccessAsync(tenant.EffectiveTenantId, tenant.PersonId, systemId, tenant.IsCspAdmin, ct)).Permissions;
    }

    private static PackageReadinessAction ReadinessAction(PackageReadinessCheck check, SystemWorkspacePermissions permissions)
    {
        var (path, allowed) = check.Category switch
        {
            "boundary" => ("boundaries", permissions.CanManageSystem),
            "sap" => ("assessments?tab=plan", permissions.CanGenerateSap),
            "sar" => ("assessments", permissions.CanGenerateSar),
            "poam" or "cross-reference" => ("poam", permissions.CanManageRemediation),
            "evidence" => ("evidence", permissions.CanManageEvidence),
            "authorization-decision" => ("authorize", permissions.CanDecideAuthorization),
            "provider-authorization" => ("inheritance/subscriptions", false),
            "privacy" => ("legal", false),
            _ => ("documents", false)
        };
        // Aggregate profile/SSP findings do not identify a verified single editor; never invent an edit action.
        var editable = allowed && check.Outcome is not ("Passed" or "NotApplicable");
        return new(true, editable, path, "Open", editable ? null : "View the source workflow. This check does not grant edit or approval authority.");
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
