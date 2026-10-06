using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>Tenant-authorized governed architecture; presentation and retained approvals are separate stores.</summary>
public sealed partial class SystemDesignService(IDbContextFactory<AtoCopilotContext> factory,
    ITenantContext tenant, ISystemWorkspaceAccessService access, IWorkspaceOperationsService? workspace = null) : ISystemDesignService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private Guid TenantId => tenant.EffectiveTenantId;
    private string Actor => tenant.PersonId!.Value.ToString();
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    internal static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions)
        ?? throw new InvalidOperationException("Invalid retained design snapshot.");
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Expected(long actual, long expected)
    {
        if (expected < 0 || actual != expected) throw new DbUpdateConcurrencyException("Design revision changed. Reload before retrying.");
    }
    private static void Reason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
            throw new ArgumentException("A rationale of 1–2000 characters is required.");
    }
    private async Task<(RegisteredSystem System, DesignActions Actions)> AuthorizeAsync(AtoCopilotContext db, string id, CancellationToken ct)
    {
        if (TenantId == Guid.Empty || tenant.IsCspAdmin || tenant.ImpersonatedTenantId.HasValue || tenant.PersonId is null)
            throw new UnauthorizedAccessException("Use an authenticated ordinary organization workspace.");
        var system = await db.RegisteredSystems.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == id && x.TenantId == TenantId && x.IsActive, ct) ?? throw new KeyNotFoundException("System not found.");
        var allowed = await access.GetAccessAsync(TenantId, tenant.PersonId, id, false, ct);
        if (!allowed.Permissions.CanRead) throw new KeyNotFoundException("System not found.");
        // Roles here are resolved by the persisted server policy, never supplied by a caller.
        var edit = allowed.Permissions.CanEditProfile || allowed.Permissions.CanAuthorNarratives;
        var review = allowed.Roles.Contains("Issm") && allowed.Permissions.CanReviewNarratives;
        return (system, new(edit, edit, edit, review, edit));
    }
    private IQueryable<SystemDesignWorkspace> Workspaces(AtoCopilotContext db, string id) =>
        db.Set<SystemDesignWorkspace>().Where(x => x.TenantId == TenantId && x.SystemId == id);
    private IQueryable<SystemDesignRevision> Revisions(AtoCopilotContext db, string id) =>
        db.Set<SystemDesignRevision>().Where(x => x.TenantId == TenantId && x.SystemId == id);
    private static void RequireEdit(DesignActions actions)
    {
        if (!actions.CanEdit) throw new UnauthorizedAccessException("An authorized design editor is required.");
    }

    public async Task<SystemDesignGraph> GetAsync(string systemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (system, actions) = await AuthorizeAsync(db, systemId, ct);
        var canonical = await ProjectAsync(db, system, ct);
        var row = await Workspaces(db, systemId).AsNoTracking().SingleOrDefaultAsync(ct);
        var graph = row is null ? canonical : Read<SystemDesignGraph>(row.GraphJson);
        return await DecorateAsync(db, graph, canonical, actions, row?.ApprovedRevision, ct);
    }

    private async Task<SystemDesignGraph> DecorateAsync(AtoCopilotContext db, SystemDesignGraph graph,
        SystemDesignGraph canonical, DesignActions actions, long? approvedRevision, CancellationToken ct)
    {
        var scopeGaps = await ComponentScopeGapsAsync(graph, ct);
        var stale = graph.SourceFingerprint != canonical.SourceFingerprint || scopeGaps.Any(x => x.Id.StartsWith("ComponentScopeSource:"));
        var gaps = FindGaps(graph, stale, canonical).Concat(ProjectionGaps(graph, canonical)).Concat(scopeGaps).ToArray();
        var baseline = approvedRevision is null ? null : await Revisions(db, graph.SystemId).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Revision == approvedRevision, ct);
        var contentEditable = graph.GovernanceStatus is "NotStarted" or "Draft" or "NeedsRevision";
        return graph with
        {
            Nodes = graph.Nodes.Select(ClassifyNode).ToArray(),
            ApprovedRevision = approvedRevision, SourcesStale = stale, Gaps = gaps,
            AvailableNodes = canonical.Nodes.Where(x => !graph.Nodes.Any(n => n.Id == x.Id)).ToArray(),
            Actions = actions with { CanEdit = actions.CanEdit && contentEditable,
                CanSubmit = actions.CanSubmit && graph.GovernanceStatus is "Draft" or "NeedsRevision",
                CanWithdraw = actions.CanWithdraw && graph.GovernanceStatus == "UnderReview",
                CanReview = actions.CanReview && graph.GovernanceStatus == "UnderReview" && graph.LastEditor != Actor,
                CanReconcile = actions.CanReconcile && contentEditable,
                CanDeriveDraft = actions.CanEdit && graph.GovernanceStatus == "Approved" },
            CompletenessPercentage = Completeness(graph, gaps),
            SspReadiness = stale ? "NeedsRevision" : graph.GovernanceStatus == "NotStarted" ? "Missing"
                : gaps.Any(x => x.Severity == "Error") ? "Blocked"
                : graph.GovernanceStatus == "Approved" ? "Ready" : "Unapproved",
            BaselineChanges = baseline is null ? [] : Compare(Read<SystemDesignGraph>(baseline.GraphJson), graph)
        };
    }

    public Task<SystemDesignGraph> SaveAsync(string systemId, SaveSystemDesignRequest request, CancellationToken ct = default) =>
        SaveDesignAsync(systemId, request, null, ct);

    private async Task<SystemDesignGraph> SaveDesignAsync(string systemId, SaveSystemDesignRequest request,
        SaveComponentScopeRequest? scopeRequest, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (system, actions) = await AuthorizeAsync(db, systemId, ct);
        RequireEdit(actions); Reason(request.Reason);
        var row = await Workspaces(db, systemId).SingleOrDefaultAsync(ct);
        Expected(row?.Revision ?? 0, request.ExpectedRevision);
        var canonical = await ProjectAsync(db, system, ct);
        var prior = row is null ? canonical : Read<SystemDesignGraph>(row.GraphJson);
        Editable(prior);
        var scopes = prior.ComponentScopes;
        if (scopeRequest is not null)
        {
            var scope = await ValidateComponentScopeAsync(systemId, scopeRequest, ct);
            scopes = scopes.Where(x => x.Source != scope.Source || x.ComponentId != scope.ComponentId).Append(scope).ToArray();
            if (scopes.Count > 1000) throw new ArgumentException("Component scope exceeds the 1000 record budget.");
        }
        var groups = MergeProjectedGroups(request.Nodes, request.Groups);
        Validate(request.Nodes, request.Edges, groups, prior, canonical);
        var priorNodes = prior.Nodes.ToDictionary(x => x.Id, Json);
        var priorEdges = prior.Edges.ToDictionary(x => x.Id, Json);
        var nodes = request.Nodes.Select(node => priorNodes.GetValueOrDefault(node.Id) == Json(node)
            ? node : node with { ReviewState = "Draft", ProjectionStatus = node.Source is null ? "Working" : "WorkingOverride" }).ToArray();
        var edges = request.Edges.Select(edge => priorEdges.GetValueOrDefault(edge.Id) == Json(edge)
            ? edge : edge with { Origin = "UserAuthored", ReviewState = "Draft", ProjectionStatus = edge.Source is null ? "Working" : "WorkingOverride" }).ToArray();
        var retainedIds = nodes.Select(x => x.Id).Concat(edges.Select(x => x.Id)).ToHashSet();
        var removedSourceIds = prior.Nodes.Where(x => x.Source is not null).Select(x => x.Id)
            .Concat(prior.Edges.Where(x => x.Source is not null).Select(x => x.Id)).Where(x => !retainedIds.Contains(x));
        var graph = prior with { Nodes = nodes, Edges = edges, Groups = groups, ComponentScopes = scopes,
            SuppressedSourceIds = prior.SuppressedSourceIds.Concat(removedSourceIds).Distinct()
                .Where(x => !retainedIds.Contains(x)).OrderBy(x => x).ToArray(), HasAssemblyBaseline = true,
            GovernanceStatus = "Draft", LastEditor = Actor };
        await PersistAsync(db, row, graph, "Save", request.Reason, ct);
        return await GetAsync(systemId, ct);
    }
    private static void Editable(SystemDesignGraph graph)
    {
        if (graph.GovernanceStatus is "Approved" or "UnderReview")
            throw new ArgumentException("Derive a draft or withdraw the review before editing.");
    }
    private async Task PersistAsync(AtoCopilotContext db, SystemDesignWorkspace? row, SystemDesignGraph graph,
        string action, string reason, CancellationToken ct)
    {
        if (row is null)
        {
            row = new() { TenantId = TenantId, SystemId = graph.SystemId };
            db.Add(row);
        }
        row.Revision++;
        if (action == "approve") row.ApprovedRevision = row.Revision;
        if (action == "submit") row.SubmittedBy = Actor;
        graph = graph with { Revision = row.Revision, ApprovedRevision = row.ApprovedRevision,
            Actions = new(false, false, false, false, false), Gaps = [], BaselineChanges = [] };
        row.GraphJson = Json(graph);
        if (row.GraphJson.Length > 4_000_000) throw new ArgumentException("Design exceeds the 4 MB storage budget.");
        db.Add(new SystemDesignRevision { TenantId = TenantId, SystemId = graph.SystemId, Revision = row.Revision,
            Action = action, Actor = Actor, At = DateTimeOffset.UtcNow, Reason = reason,
            GovernanceStatus = graph.GovernanceStatus, SourceFingerprint = graph.SourceFingerprint,
            SnapshotHash = Hash(row.GraphJson), GraphJson = row.GraphJson });
        await db.SaveChangesAsync(ct);
    }

    public async Task<SystemDesignGraph> ReviewAsync(string systemId, DesignReviewRequest request, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (system, actions) = await AuthorizeAsync(db, systemId, ct);
        Reason(request.Reason);
        var row = await Workspaces(db, systemId).SingleOrDefaultAsync(ct) ?? throw new ArgumentException("Save a draft first.");
        Expected(row.Revision, request.ExpectedRevision);
        var graph = Read<SystemDesignGraph>(row.GraphJson);
        var canonical = await ProjectAsync(db, system, ct);
        var action = request.Action;
        if (action is "approve" or "request_revision")
        {
            var contributed = await Revisions(db, systemId).AnyAsync(x => x.Actor == Actor
                && x.Revision > (row.ApprovedRevision ?? 0) && (x.Action == "Save" || x.Action == "BuildFromRecorded"
                    || x.Action == "Proposal:accept" || x.Action == "Proposal:edit_accept"), ct);
            if (!actions.CanReview || row.SubmittedBy == Actor || graph.LastEditor == Actor || contributed)
                throw new UnauthorizedAccessException("An independent assigned ISSM reviewer is required.");
            if (graph.GovernanceStatus != "UnderReview") throw new ArgumentException("The design is not under review.");
            if (action == "approve" && FindGaps(graph, graph.SourceFingerprint != canonical.SourceFingerprint, canonical)
                .Concat(ProjectionGaps(graph, canonical)).Concat(await ComponentScopeGapsAsync(graph, ct)).Any(x => x.Severity == "Error"))
                throw new ArgumentException("Resolve blocking design and source gaps before approval.");
            graph = graph with { GovernanceStatus = action == "approve" ? "Approved" : "NeedsRevision",
                Reviewer = Actor, ReviewerComments = request.Reason };
            if (action == "approve")
                graph = graph with { Nodes = graph.Nodes.Select(x => x with { ReviewState = "Approved" }).ToArray(),
                    Edges = graph.Edges.Select(x => x with { ReviewState = "Approved" }).ToArray() };
        }
        else
        {
            RequireEdit(actions);
            var status = (action, graph.GovernanceStatus) switch
            {
                ("submit", "Draft" or "NeedsRevision") => "UnderReview",
                ("withdraw", "UnderReview") => "Draft",
                ("derive_draft", "Approved") => "Draft",
                _ => throw new ArgumentException("Invalid design governance transition.")
            };
            if (action == "submit" && (graph.SourceFingerprint != canonical.SourceFingerprint
                || (await ComponentScopeGapsAsync(graph, ct)).Any(x => x.Id.StartsWith("ComponentScopeSource:"))))
                throw new DbUpdateConcurrencyException("Canonical sources changed. Reconcile before submitting.");
            graph = graph with { GovernanceStatus = status };
        }
        await PersistAsync(db, row, graph, action, request.Reason, ct);
        return await GetAsync(systemId, ct);
    }

    public async Task<IReadOnlyList<DesignHistoryEntry>> GetHistoryAsync(string systemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AuthorizeAsync(db, systemId, ct);
        return await Revisions(db, systemId).AsNoTracking().OrderByDescending(x => x.Revision).Take(200)
            .Select(x => new DesignHistoryEntry(x.Revision, x.Action, x.Actor, x.At, x.Reason, x.GovernanceStatus, x.SourceFingerprint)).ToListAsync(ct);
    }

    public async Task<ApprovedSystemDesign?> GetApprovedAsync(string systemId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (system, _) = await AuthorizeAsync(db, systemId, ct);
        var row = await Workspaces(db, systemId).AsNoTracking().SingleOrDefaultAsync(ct);
        if (row?.ApprovedRevision is null) return null;
        var snapshot = await Revisions(db, systemId).AsNoTracking().SingleAsync(x => x.Revision == row.ApprovedRevision, ct);
        if (snapshot.Action != "approve" || Hash(snapshot.GraphJson) != snapshot.SnapshotHash)
            throw new InvalidOperationException("Approved design integrity verification failed.");
        var graph = Read<SystemDesignGraph>(snapshot.GraphJson);
        var canonical = await ProjectAsync(db, system, ct);
        return new(graph, snapshot.Revision, snapshot.Actor, snapshot.At, snapshot.SnapshotHash,
            snapshot.SourceFingerprint, snapshot.SourceFingerprint != canonical.SourceFingerprint
                || (await ComponentScopeGapsAsync(graph, ct)).Any(x => x.Id.StartsWith("ComponentScopeSource:")));
    }

    public async Task<SystemDesignGraph> GetRevisionAsync(string systemId, long revision, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await AuthorizeAsync(db, systemId, ct);
        var snapshot = await Revisions(db, systemId).AsNoTracking().SingleOrDefaultAsync(x => x.Revision == revision, ct)
            ?? throw new KeyNotFoundException("Design revision not found.");
        if (Hash(snapshot.GraphJson) != snapshot.SnapshotHash)
            throw new InvalidOperationException("Design revision integrity verification failed.");
        return Read<SystemDesignGraph>(snapshot.GraphJson) with { Actions = new(false, false, false, false, false) };
    }
}
