using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class SystemDesignService
{
    public Task<SystemDesignGraph> BuildFromRecordedAsync(string systemId, DesignRevisionRequest request, CancellationToken ct = default) =>
        ReconcileCoreAsync(systemId, request, true, ct);

    public Task<SystemDesignGraph> ReconcileAsync(string systemId, DesignRevisionRequest request, CancellationToken ct = default) =>
        ReconcileCoreAsync(systemId, request, false, ct);

    private async Task<SystemDesignGraph> ReconcileCoreAsync(string systemId, DesignRevisionRequest request, bool build, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (system, actions) = await AuthorizeAsync(db, systemId, ct);
        RequireEdit(actions); Reason(request.Reason);
        var row = await Workspaces(db, systemId).SingleOrDefaultAsync(ct);
        Expected(row?.Revision ?? 0, request.ExpectedRevision);
        var canonical = await ProjectAsync(db, system, ct);
        var graph = row is null ? canonical : Read<SystemDesignGraph>(row.GraphJson);
        Editable(graph);
        var proposals = graph.Proposals.ToList();
        var observed = await ObservationsAsync(db, systemId, ct);
        var candidate = canonical with { Nodes = canonical.Nodes.Concat(observed).GroupBy(x => x.Id).Select(x => x.First()).ToArray() };
        if (build)
            graph = AssembleAdditions(graph, canonical, row is null);
        foreach (var change in Compare(graph with { Groups = [] }, candidate with { Groups = [] }))
        {
            var node = candidate.Nodes.SingleOrDefault(x => x.Id == change.RecordId);
            var edge = candidate.Edges.SingleOrDefault(x => x.Id == change.RecordId);
            var previousNode = graph.Nodes.SingleOrDefault(x => x.Id == change.RecordId);
            var previousEdge = graph.Edges.SingleOrDefault(x => x.Id == change.RecordId);
            if (node?.Source is not null && previousNode?.Source is not null
                && node.Source.Type == previousNode.Source.Type && node.Source.Id == previousNode.Source.Id
                && node.Source.Version == previousNode.Source.Version) continue;
            if (edge?.Source is not null && previousEdge?.Source is not null
                && edge.Source.Type == previousEdge.Source.Type && edge.Source.Id == previousEdge.Source.Id
                && edge.Source.Version == previousEdge.Source.Version) continue;
            // A manual design decision is not deleted simply because no canonical authoring record exists.
            if (change.Kind == "Removed" && previousNode?.Source is null && previousEdge?.Source is null) continue;
            var key = Hash($"{change.RecordId}:{change.Kind}:{canonical.SourceFingerprint}:{change.After}");
            if (proposals.Any(x => x.Id == key)) continue;
            var oldRank = graph.ApprovedRevision.HasValue ? 1 : previousNode?.Source?.Precedence ?? previousEdge?.Source?.Precedence ?? 7;
            var rank = node?.Source?.Precedence ?? edge?.Source?.Precedence ?? 7;
            proposals.Add(new() { Id = key, Kind = change.Kind, RecordId = change.RecordId,
                OriginalNode = node ?? previousNode, OriginalEdge = edge ?? previousEdge,
                SourceFingerprint = canonical.SourceFingerprint,
                ConflictsWithHigherPrecedence = change.Kind != "Added" && rank >= oldRank });
        }
        if (proposals.Count > 2000) throw new ArgumentException("Proposal history exceeds 2000 records; split the design scope.");
        graph = graph with { GovernanceStatus = "Draft", Proposals = proposals, SourceFingerprint = canonical.SourceFingerprint,
            HasAssemblyBaseline = true, LastEditor = build ? Actor : graph.LastEditor,
            Contributions = canonical.Contributions, SynchronizedAt = DateTimeOffset.UtcNow,
            DiscoveryState = observed.Any(x => x.Kind == "AzureResource") ? "RetainedObservations" : "Unavailable",
            MonitoringState = observed.Any(x => x.Kind == "MonitoringObservation") ? "RetainedObservationsNotHealth" : "Unavailable" };
        await PersistAsync(db, row, graph, build ? "BuildFromRecorded" : "Reconcile", request.Reason, ct);
        return await GetAsync(systemId, ct);
    }

    private static SystemDesignGraph AssembleAdditions(SystemDesignGraph graph, SystemDesignGraph canonical, bool newWorkspace)
    {
        var nodes = graph.Nodes.ToList();
        var edges = graph.Edges.ToList();
        var blocked = graph.SuppressedSourceIds.Concat(graph.Proposals.Select(x => x.RecordId)).ToHashSet();
        // Legacy saves lack omission evidence. Preserve missing nodes as proposals, not guessed restorations.
        if (graph.HasAssemblyBaseline || newWorkspace)
            nodes.AddRange(canonical.Nodes.Where(x => x.Source is not null && !blocked.Contains(x.Id)
                && !nodes.Any(n => n.Id == x.Id)));
        var ids = nodes.Select(x => x.Id).ToHashSet();
        var sources = nodes.ToDictionary(x => x.Id, x => x.Source);
        var canonicalSources = canonical.Nodes.ToDictionary(x => x.Id, x => x.Source);
        edges.AddRange(canonical.Edges.Where(x => x.Source is not null && !blocked.Contains(x.Id)
            && !edges.Any(e => e.Id == x.Id) && ids.Contains(x.SourceNodeId) && ids.Contains(x.TargetNodeId)
            && sources[x.SourceNodeId] == canonicalSources.GetValueOrDefault(x.SourceNodeId)
            && sources[x.TargetNodeId] == canonicalSources.GetValueOrDefault(x.TargetNodeId)));
        var groups = MergeProjectedGroups(nodes, graph.Groups);
        Validate(nodes, edges, groups, graph, canonical with { Edges = canonical.Edges.Concat(graph.Edges).ToArray() });
        return graph with { Nodes = nodes.OrderBy(x => x.Id).ToArray(), Edges = edges.OrderBy(x => x.Id).ToArray(), Groups = groups };
    }

    public async Task<SystemDesignGraph> DecideProposalAsync(string systemId, string proposalId,
        DesignProposalDecisionRequest request, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (system, actions) = await AuthorizeAsync(db, systemId, ct);
        RequireEdit(actions); Reason(request.Reason);
        var row = await Workspaces(db, systemId).SingleOrDefaultAsync(ct) ?? throw new ArgumentException("Reconcile the design first.");
        Expected(row.Revision, request.ExpectedRevision);
        var graph = Read<SystemDesignGraph>(row.GraphJson);
        Editable(graph);
        var proposal = graph.Proposals.SingleOrDefault(x => x.Id == proposalId) ?? throw new KeyNotFoundException("Proposal not found.");
        var canonical = await ProjectAsync(db, system, ct);
        var action = request.Action;
        if (action == "recover")
        {
            if (proposal.State is not ("Rejected" or "Deferred")) throw new ArgumentException("Only rejected or deferred proposals can be recovered.");
        }
        else if (proposal.State != "Pending") throw new ArgumentException("Recover this proposal before deciding it again.");
        var state = action switch { "accept" or "edit_accept" => "Accepted", "reject" => "Rejected", "defer" => "Deferred", "recover" => "Pending",
            _ => throw new ArgumentException("Unknown proposal decision.") };
        DesignNode? resultNode = null; DesignEdge? resultEdge = null;
        if (state == "Accepted")
        {
            if (canonical.SourceFingerprint != proposal.SourceFingerprint)
                throw new DbUpdateConcurrencyException("Proposal source changed. Reconcile the latest source rather than accepting a stale proposal.");
            if (proposal.ConflictsWithHigherPrecedence && action == "accept")
                throw new ArgumentException("Conflicting lower-precedence information requires an explicit edit-and-accept decision and rationale.");
            resultNode = action == "edit_accept" ? request.Node : proposal.OriginalNode;
            resultEdge = action == "edit_accept" ? request.Edge : proposal.OriginalEdge;
            if (proposal.Kind != "Removed" && (resultNode is null) == (resultEdge is null))
                throw new ArgumentException("Choose exactly one result of the same type as the proposed record.");
            if (resultNode is not null && (proposal.OriginalNode is null || resultNode.Id != proposal.RecordId || resultNode.Source != proposal.OriginalNode.Source)
                || resultEdge is not null && (proposal.OriginalEdge is null || resultEdge.Id != proposal.RecordId || resultEdge.Source != proposal.OriginalEdge.Source))
                throw new ArgumentException("A proposal decision cannot change source identity or provenance.");
            var nodes = graph.Nodes.Where(x => x.Id != proposal.RecordId).ToList();
            var edges = graph.Edges.Where(x => x.Id != proposal.RecordId).ToList();
            if (proposal.Kind != "Removed")
            {
                if (resultNode is not null) { resultNode = resultNode with { ReviewState = "Reviewed", ProjectionStatus = "ReviewedProposal" }; nodes.Add(resultNode); }
                if (resultEdge is not null) { resultEdge = resultEdge with { Origin = action == "edit_accept" ? "UserAuthored" : resultEdge.Origin,
                    ReviewState = "Reviewed", ProjectionStatus = "ReviewedProposal" }; edges.Add(resultEdge); }
            }
            else { resultNode = null; resultEdge = null; }
            var allowed = canonical with { Nodes = canonical.Nodes.Concat(proposal.OriginalNode is null ? [] : new[] { proposal.OriginalNode }).ToArray(),
                Edges = canonical.Edges.Concat(proposal.OriginalEdge is null ? [] : new[] { proposal.OriginalEdge }).ToArray() };
            var groups = MergeProjectedGroups(nodes, graph.Groups);
            Validate(nodes, edges, groups, graph, allowed);
            graph = graph with { Nodes = nodes, Edges = edges, Groups = groups, LastEditor = Actor,
                SuppressedSourceIds = proposal.Kind == "Removed"
                    ? graph.SuppressedSourceIds.Append(proposal.RecordId).Distinct().ToArray()
                    : graph.SuppressedSourceIds.Where(x => x != proposal.RecordId).ToArray() };
        }
        var decided = proposal with { State = state, Actor = Actor, DecidedAt = DateTimeOffset.UtcNow,
            Reason = request.Reason, ResultNode = resultNode, ResultEdge = resultEdge };
        graph = graph with { Proposals = graph.Proposals.Select(x => x.Id == proposalId ? decided : x).ToArray() };
        await PersistAsync(db, row, graph, $"Proposal:{action}", request.Reason, ct);
        return await GetAsync(systemId, ct);
    }

    private sealed record RetainedDiscovery(EnvironmentDiscoveryResponse Response, EnvironmentRegistration Registration);
    private async Task<IReadOnlyList<DesignNode>> ObservationsAsync(AtoCopilotContext db, string systemId, CancellationToken ct)
    {
        var result = new List<DesignNode>();
        var attachments = await db.Set<SystemEnvironmentAttachmentRecord>().AsNoTracking().Where(x =>
            x.TenantId == TenantId && x.SystemId == systemId && x.State == "Attached").ToListAsync(ct);
        var discoveries = await db.Set<SystemEnvironmentPendingOperation>().AsNoTracking().Where(x =>
            x.TenantId == TenantId && x.SystemId == systemId && x.Kind == "Discovery").OrderByDescending(x => x.Id).Take(101).ToListAsync(ct);
        if (discoveries.Count > 100) throw new ArgumentException("Too many retained discovery records to reconcile safely.");
        foreach (var item in discoveries.Select(record => (Record: record, Material: Read<RetainedDiscovery>(record.MaterialJson)))
            .OrderByDescending(x => x.Material.Response.DiscoveredAt).ThenBy(x => x.Record.Id))
        {
            var record = item.Record;
            var discovery = item.Material;
            var attachment = attachments.SingleOrDefault(x => x.RegistrationId == discovery.Registration.RegistrationId);
            if (attachment is null) continue;
            var scope = Read<EnvironmentScope>(attachment.ScopeJson);
            foreach (var resource in discovery.Response.Resources.Where(x => scope.ResourceIds.Contains(x.ResourceId, StringComparer.OrdinalIgnoreCase)))
            {
                result.Add(new() { Id = $"azure:{Hash(resource.ResourceId.ToLowerInvariant())}", Label = resource.Name, Kind = "AzureResource",
                    Source = new("AzureObservation", record.Id.ToString(), Hash(Json(resource)), "Retained discovery; no live collection",
                        "Observed", 4, Link(systemId, "environment")) { SourceTenantId = TenantId },
                    ProjectionStatus = "Observed", ReviewState = "Unreviewed", Properties = new()
                    { ["resourceId"] = resource.ResourceId, ["resourceType"] = resource.ResourceType, ["location"] = resource.Location,
                        ["resourceGroup"] = resource.ResourceGroup, ["observedAt"] = discovery.Response.DiscoveredAt.ToString("O") } });
            }
        }
        var monitoring = await db.Set<ScopedMonitoringObservation>().AsNoTracking().Where(x =>
            x.TenantId == TenantId && x.RegisteredSystemId == systemId).OrderByDescending(x => x.Id).Take(501).ToListAsync(ct);
        if (monitoring.Count > 500) throw new ArgumentException("Monitoring observation budget exceeded. Review a narrower source scope.");
        foreach (var row in monitoring)
        {
            if (Hash(row.SnapshotJson) != row.Fingerprint) throw new InvalidOperationException("Retained monitoring observation integrity failed.");
            var observation = Read<MonitoringObservedChange>(row.SnapshotJson);
            result.Add(new() { Id = $"monitoring:{row.Id}", Label = observation.Title, Kind = "MonitoringObservation",
                DiagramRole = "SourceRecord",
                Source = new("ScopedMonitoringObservation", row.Id.ToString(), row.Fingerprint, "Attributed retained monitoring change; not monitoring health",
                    "Observed", 4, $"/systems/{Uri.EscapeDataString(systemId)}/conmon/changes") { SourceTenantId = TenantId },
                ProjectionStatus = "Observed", Properties = new() { ["kind"] = observation.Kind, ["controlId"] = observation.ControlId,
                    ["changeDetails"] = observation.ChangeDetails, ["sourceId"] = observation.SourceId, ["observedAt"] = observation.ObservedAt.ToString("O") } });
        }
        return result.GroupBy(x => x.Id).Select(x => x.First()).ToArray();
    }
}
