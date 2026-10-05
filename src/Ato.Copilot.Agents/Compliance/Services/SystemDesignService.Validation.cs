using Ato.Copilot.Core.Dtos.SystemDesign;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class SystemDesignService
{
    private static IEnumerable<DesignGap> ProjectionGaps(SystemDesignGraph graph, SystemDesignGraph canonical)
    {
        var missing = canonical.Nodes.Where(n => n.Kind is "ProfileSection" or "ActorGroup" or "InformationType" or "PpsEntry" or "LeveragedAuthorization"
            && !graph.Nodes.Any(x => x.Id == n.Id))
        .Select(n => new DesignGap($"SourceNotProjected:{n.Id}", "Error", "Canonical System definition information is absent from the design/SSP projection.",
            n.Id, "Context", "SSP source contribution", "System Owner", n.Source!.ResolutionUrl));
        var connections = graph.Edges.Where(e => (e.InterconnectionId is not null || e.Source?.Type == "SystemInterconnection")
            && !MatchesCanonicalInterconnection(e, canonical))
            .Select(e => new DesignGap($"CanonicalAgreementChanged:{e.Id}", "Error",
                "The canonical interconnection or agreement differs from this design. Reconcile its current legal record.",
                e.Id, "Boundary", "SSP interconnection agreements", "System Owner", Link(graph.SystemId, "pps")));
        return missing.Concat(connections);
    }

    private static bool MatchesCanonicalInterconnection(DesignEdge edge, SystemDesignGraph canonical) =>
        canonical.Edges.Any(connection =>
        {
            if (connection.Source?.Type != "SystemInterconnection" || edge.InterconnectionId != connection.InterconnectionId
                || edge.AgreementStatus != connection.AgreementStatus
                || connection.Direction != "Bidirectional" && edge.Direction != connection.Direction)
                return false;
            if (edge.Source?.Type == "SystemInterconnection")
                return edge.Source.Id == connection.Source.Id && edge.SourceNodeId == connection.SourceNodeId
                    && edge.TargetNodeId == connection.TargetNodeId && edge.Direction == connection.Direction;
            var external = canonical.Nodes.SingleOrDefault(node => node.Kind == "ExternalSystem"
                && node.Source?.Type == "SystemInterconnection" && node.Source.Id == edge.InterconnectionId);
            if (external is null) return false;
            var outbound = edge.SourceNodeId == connection.SourceNodeId && edge.TargetNodeId == connection.TargetNodeId;
            var inbound = connection.Direction == "Bidirectional" && edge.SourceNodeId == connection.TargetNodeId && edge.TargetNodeId == connection.SourceNodeId;
            return outbound || inbound;
        });

    private static bool SameProperties(DesignNode left, DesignNode right) =>
        left.Properties.Count == right.Properties.Count && left.Properties.All(p =>
            right.Properties.TryGetValue(p.Key, out var value) && value == p.Value);
    private static bool MatchesPps(DesignNode node, DesignEdge edge)
    {
        var direction = node.Properties.GetValueOrDefault("Direction");
        if (direction == "Both") direction = "Bidirectional";
        return !string.IsNullOrWhiteSpace(direction) && direction == edge.Direction
            && !string.IsNullOrWhiteSpace(node.Properties.GetValueOrDefault("PortOrRange"))
            && string.Equals(node.Properties.GetValueOrDefault("PortOrRange")?.Trim(), edge.Port?.Trim(), StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(node.Properties.GetValueOrDefault("Protocol"))
            && string.Equals(node.Properties.GetValueOrDefault("Protocol")?.Trim(), edge.Protocol?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static void Validate(IReadOnlyList<DesignNode> nodes, IReadOnlyList<DesignEdge> edges,
        IReadOnlyList<DesignGroup> groups, SystemDesignGraph prior, SystemDesignGraph canonical)
    {
        if (nodes is null || edges is null || groups is null || nodes.Count > 1000 || edges.Count > 3000 || groups.Count > 200
            || nodes.Any(x => x is null) || edges.Any(x => x is null) || groups.Any(x => x is null))
            throw new ArgumentException("Design supports at most 1000 nodes, 3000 edges and 200 groups.");
        var ids = nodes.Select(x => x.Id).Concat(edges.Select(x => x.Id)).Concat(groups.Select(x => x.Id)).ToArray();
        if (ids.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Design IDs must be unique, nonempty and at most 200 characters.");
        var knownNodes = prior.Nodes.Concat(canonical.Nodes).GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.ToArray());
        var knownEdges = prior.Edges.Concat(canonical.Edges).GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.ToArray());
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Label) || node.Label.Length > 500 || node.Properties is null
                || node.Properties.Count > 100 || node.Properties.Any(p => p.Key.Length > 200 || p.Value?.Length > 64000)
                || node.BoundaryDisposition is not ("Undetermined" or "InBoundary" or "OutOfBoundary"))
                throw new ArgumentException("Invalid node label, properties or boundary disposition.");
            if (node.DiagramRole is not null && node.DiagramRole != (SourceRecordKind(node.Kind) ? "SourceRecord" : "Architecture"))
                throw new ArgumentException("Diagram role is determined by the canonical source kind.");
            if (node.Source is not null && (!knownNodes.TryGetValue(node.Id, out var matches)
                || !matches.Any(x => x.Source == node.Source && x.Kind == node.Kind && SameProperties(node, x))))
                throw new ArgumentException("Canonical node provenance and source properties cannot be supplied or altered. Edit the canonical source workflow.");
            if (node.Source is null && knownNodes.TryGetValue(node.Id, out var known) && known.Any(x => x.Source is not null))
                throw new ArgumentException("Canonical provenance cannot be removed.");
            if (node.Source is null && node.Kind is not ("ExternalSystem" or "DesignComponent"))
                throw new ArgumentException("New manually authored nodes must be external systems or design components.");
        }
        var nodeIds = nodes.Select(x => x.Id).ToHashSet();
        foreach (var edge in edges)
        {
            if (!nodeIds.Contains(edge.SourceNodeId) || !nodeIds.Contains(edge.TargetNodeId)
                || edge.Direction is not ("Inbound" or "Outbound" or "Bidirectional")
                || edge.BoundaryCrossing is not ("Yes" or "No" or "Unknown")
                || Json(edge).Length > 32000)
                throw new ArgumentException("Relationships require valid endpoints, direction and bounded fields.");
            if (edge.Source is not null && (!knownEdges.TryGetValue(edge.Id, out var matches) || !matches.Any(x => x.Source == edge.Source)))
                throw new ArgumentException("Canonical relationship provenance cannot be supplied or altered by the caller.");
            if (edge.Source is null && knownEdges.TryGetValue(edge.Id, out var known) && known.Any(x => x.Source is not null))
                throw new ArgumentException("Canonical relationship provenance cannot be removed.");
            if (RecordedRelationshipType(edge.RelationshipType) && edge.RelationshipType != "Access" || edge.Source?.Type == "RecordedRelationship")
            {
                if (!knownEdges.TryGetValue(edge.Id, out var structural) || !structural.Any(x =>
                    x.Source?.Type == "RecordedRelationship" && SameRecordedSemantics(x, edge)))
                    throw new ArgumentException("Recorded structural and logical-access relationships require unchanged canonical endpoints, type and provenance.");
            }
            else if (edge.RelationshipType is not ("DataFlow" or "Interconnection" or "Access" or "UserAuthored" or "NetworkConnection" or "Dependency"))
                throw new ArgumentException("Unknown relationship type. Use DataFlow for authored traffic or retain a canonical relationship.");
            if (edge.Source?.Type == "SystemInterconnection" && (!knownEdges.TryGetValue(edge.Id, out var connections)
                || !connections.Any(x => x.Source == edge.Source && x.RelationshipType == edge.RelationshipType
                    && x.SourceNodeId == edge.SourceNodeId && x.TargetNodeId == edge.TargetNodeId
                    && x.InterconnectionId == edge.InterconnectionId && x.Direction == edge.Direction)))
                throw new ArgumentException("Canonical interconnection endpoints and semantic type cannot be changed.");
            if ((edge.InterconnectionId is not null || edge.Source?.Type == "SystemInterconnection")
                && !MatchesCanonicalInterconnection(edge, canonical))
                throw new ArgumentException("Interconnection and agreement status must match the current canonical legal record for this system.");
            if (edge.PpsEntryId is not null && !canonical.Nodes.Any(n => n.Kind == "PpsEntry"
                && n.Source?.Id == edge.PpsEntryId && MatchesPps(n, edge)))
                throw new ArgumentException("PPS assignment requires a current canonical PPS record and matching port, protocol and direction on known endpoints.");
        }
        if (groups.Any(g => string.IsNullOrWhiteSpace(g.Label) || g.Label.Length > 500
            || g.NodeIds is null || g.NodeIds.Count > 1000 || g.NodeIds.Any(x => !nodeIds.Contains(x))))
            throw new ArgumentException("Groups must reference existing nodes.");
        if (!nodes.Any(x => x.Id == $"system:{canonical.SystemId}" && x.Source?.Type == "RegisteredSystem"))
            throw new ArgumentException("The canonical system identity cannot be removed.");
    }

    private static bool SameRecordedSemantics(DesignEdge left, DesignEdge right) =>
        left with { ReviewState = right.ReviewState, ProjectionStatus = right.ProjectionStatus, Origin = right.Origin } == right;

    private static IReadOnlyList<DesignGap> FindGaps(SystemDesignGraph graph, bool stale, SystemDesignGraph canonical)
    {
        var gaps = new List<DesignGap>();
        var designUrl = Link(graph.SystemId, "design");
        void Gap(string code, string record, string explanation, string view = "Context", string? url = null, string severity = "Error") =>
            gaps.Add(new($"{code}:{record}", severity, explanation, record, view, "SSP / OSCAL architecture", "System Owner", url ?? designUrl));
        if (stale) Gap("SourceConflict", graph.SystemId, "Canonical sources changed. Reconcile and review the proposed changes.");
        foreach (var contribution in graph.Contributions.Where(x => x.State is "Missing" or "Unapproved"))
            Gap("SourceContribution", contribution.Section, $"{contribution.Section}: {contribution.State}. Complete and review the canonical source.", url: contribution.ResolutionUrl);
        foreach (var node in graph.Nodes)
        {
            if (node.Properties.GetValueOrDefault("sourceAvailability") is not null)
                Gap("SourceUnavailable", node.Id, "The referenced canonical source is unavailable. Review its recorded assignment.", url: node.Source?.ResolutionUrl);
            if (node.Properties.GetValueOrDefault("unmappedFields") is { } fields)
                Gap("UnmappedSourceFields", node.Id, $"Canonical profile fields are not mapped into the architecture projection: {fields}. Review the source and projection mapping.",
                    url: node.Source?.ResolutionUrl);
            if (node.Properties.GetValueOrDefault("hostingLinkGap") is { } hostingGap)
                Gap("HostingLinkUnresolved", node.Id, hostingGap, url: node.Source?.ResolutionUrl);
            if (node.Properties.GetValueOrDefault("scopeMappingGap") is { } scopeGap)
                Gap("ScopeMappingUnresolved", node.Id, scopeGap, url: node.Source?.ResolutionUrl);
            if (node.Kind == "Environment" && node.Properties.GetValueOrDefault("resourceScopeAuthority") == "Unavailable")
                Gap("EnvironmentScopeUnavailable", node.Id, "The recorded attachment does not grant a current exact resource scope. Reconcile registration or allocation authority.",
                    url: node.Source?.ResolutionUrl);
            if (node.Kind is "Component" or "InventoryItem" or "ExternalSystem" or "DesignComponent" or "AzureResource" && node.BoundaryDisposition == "Undetermined")
                Gap("BoundaryUndetermined", node.Id, "Record boundary disposition and rationale; existence does not establish inclusion.", "Boundary", node.Source?.ResolutionUrl);
            if (node.Source?.ReviewState == "Draft" && node.ProjectionStatus != "Observed")
                Gap("UnapprovedSource", node.Id, "Required canonical information has no retained approval. Review it in its source workflow.", url: node.Source.ResolutionUrl);
            if (node.Kind == "PpsEntry" && !graph.Edges.Any(e => e.PpsEntryId == node.Source?.Id && MatchesPps(node, e)))
                Gap("UnassignedPps", node.Id, "PPS has no matching documented relationship and endpoints.", "Network", node.Source?.ResolutionUrl);
            if (node.Kind == "ExternalSystem" && !graph.Edges.Any(e => e.SourceNodeId == node.Id || e.TargetNodeId == node.Id))
                Gap("ExternalWithoutFlow", node.Id, "External system has no documented connection or flow.", "DataFlows");
            if (node.ProjectionStatus == "Observed")
                Gap("UnreconciledResource", node.Id, "Observed information remains unreviewed; confirm source, boundary and intended use.", "Boundary");
        }
        foreach (var edge in graph.Edges)
        {
            if (CanonicalNonFlow(edge, canonical)) continue;
            if (edge.Source?.Type == "RecordedRelationship" || RecordedRelationshipType(edge.RelationshipType) && edge.RelationshipType != "Access")
            {
                Gap("RecordedRelationshipChanged", edge.Id,
                    "The recorded relationship no longer matches its exact canonical identity, endpoints, type and source version. Reconcile its source before review.");
                if (edge.RelationshipType != "Access") continue;
            }
            if (edge.Source?.Type == "SystemInterconnection" && edge.Source.ReviewState != "Approved")
                Gap("InterconnectionUnapproved", edge.Id, "The active canonical interconnection has no recorded authorization to connect. Design review does not grant it.", "Boundary", edge.Source.ResolutionUrl);
            if (string.IsNullOrWhiteSpace(edge.Purpose)) Gap("FlowPurpose", edge.Id, "Record the business purpose of this relationship.", "DataFlows");
            if (string.IsNullOrWhiteSpace(edge.Classification)) Gap("FlowClassification", edge.Id, "Record the information classification.", "DataFlows");
            if (string.IsNullOrWhiteSpace(edge.Protection)) Gap("FlowProtection", edge.Id, "Record protection mechanisms.", "DataFlows");
            if (string.IsNullOrWhiteSpace(edge.Port) || string.IsNullOrWhiteSpace(edge.Protocol))
                Gap("MissingPps", edge.Id, "Record applicable port and protocol, or an explicit documented non-network applicability decision.", "Network");
            if (edge.BoundaryCrossing == "Unknown") Gap("CrossingUnknown", edge.Id, "Determine whether this relationship crosses the boundary.", "Boundary");
            var endpoints = graph.Nodes.Where(n => n.Id == edge.SourceNodeId || n.Id == edge.TargetNodeId).ToArray();
            var crossing = edge.BoundaryCrossing == "Yes" || endpoints.Any(x => x.Kind == "ExternalSystem")
                || endpoints.Any(x => x.BoundaryDisposition == "OutOfBoundary")
                && endpoints.Any(x => x.BoundaryDisposition == "InBoundary" || x.Kind == "System");
            if (crossing && edge.InterconnectionId is null) Gap("MissingInterconnection", edge.Id, "Boundary crossing requires a canonical interconnection record.", "Boundary");
            if (crossing && edge.BoundaryCrossing != "Yes")
                Gap("CrossingConflict", edge.Id, "An external or out-of-boundary endpoint conflicts with the recorded boundary-crossing state.", "Boundary");
            if (edge.InterconnectionId is not null && edge.AgreementStatus != "Signed")
                Gap("Agreement", edge.Id, $"Agreement is {edge.AgreementStatus ?? "Missing"}. Record an effective, signed, unexpired agreement.", "Boundary", edge.Source?.ResolutionUrl);
        }
        foreach (var proposal in graph.Proposals.Where(x => x.State is "Pending" or "Deferred"))
            Gap("PendingProposal", proposal.Id, proposal.ConflictsWithHigherPrecedence
                ? "Lower-precedence source conflicts with a retained decision. Review without overwriting the approved baseline."
                : "Resolve or explicitly reject the staged source change.", severity: "Error");
        return gaps;
    }

    private static int Completeness(SystemDesignGraph graph, IReadOnlyList<DesignGap> gaps)
    {
        var available = graph.Contributions.Count(x => x.State == "Available");
        var denominator = Math.Max(1, graph.Nodes.Count + graph.Edges.Count + graph.ComponentScopes.Count + 6);
        var blocked = gaps.Where(x => x.Severity == "Error").Select(x => x.RecordId).Distinct().Count();
        return Math.Clamp((denominator - blocked - (6 - available)) * 100 / denominator, 0, 100);
    }
    private static IReadOnlyList<DesignChange> Compare(SystemDesignGraph before, SystemDesignGraph after)
    {
        var old = before.Nodes.Select(x => (x.Id, Value: Json(x))).Concat(before.Edges.Select(x => (x.Id, Value: Json(x))))
            .Concat(before.Groups.Select(x => (x.Id, Value: Json(x))))
            .Concat(before.ComponentScopes.Select(x => (Id: $"component-use:{x.Source}:{x.ComponentId}", Value: Json(x)))).ToDictionary(x => x.Id, x => x.Value);
        var current = after.Nodes.Select(x => (x.Id, Value: Json(x))).Concat(after.Edges.Select(x => (x.Id, Value: Json(x))))
            .Concat(after.Groups.Select(x => (x.Id, Value: Json(x))))
            .Concat(after.ComponentScopes.Select(x => (Id: $"component-use:{x.Source}:{x.ComponentId}", Value: Json(x)))).ToDictionary(x => x.Id, x => x.Value);
        return old.Keys.Union(current.Keys).OrderBy(x => x).Where(id => old.GetValueOrDefault(id) != current.GetValueOrDefault(id))
            .Select(id => new DesignChange(!old.ContainsKey(id) ? "Added" : !current.ContainsKey(id) ? "Removed" : "Modified",
                id, old.GetValueOrDefault(id), current.GetValueOrDefault(id))).ToArray();
    }
}
