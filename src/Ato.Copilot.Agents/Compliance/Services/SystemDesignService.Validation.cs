using Ato.Copilot.Core.Dtos.SystemDesign;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class SystemDesignService
{
    private static bool SafeReference(string value) => !value.Any(char.IsControl) && !value.Contains('\\')
        && (value.StartsWith('/') && !value.StartsWith("//")
            || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);

    private static IEnumerable<DesignGap> ProjectionGaps(SystemDesignGraph graph, SystemDesignGraph canonical)
    {
        var missing = canonical.Nodes.Where(n => n.Kind is "ProfileSection" or "ActorGroup" or "InformationType" or "PpsEntry" or "LeveragedAuthorization" or "PolicyReference"
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
            if (node.DiagramRole is not null && node.DiagramRole != (SystemDesignSemantics.IsSourceRecordNode(node) ? "SourceRecord" : "Architecture"))
                throw new ArgumentException("Diagram role is determined by the canonical source kind.");
            if (node.Source is not null && (!knownNodes.TryGetValue(node.Id, out var matches)
                || !matches.Any(x => x.Source == node.Source && x.Kind == node.Kind && SameProperties(node, x))))
                throw new ArgumentException("Canonical node provenance and source properties cannot be supplied or altered. Edit the canonical source workflow.");
            if (node.Source is null && knownNodes.TryGetValue(node.Id, out var known) && known.Any(x => x.Source is not null))
                throw new ArgumentException("Canonical provenance cannot be removed.");
            if (node.Kind == "LogicalConstruct" && (!SystemLogicalProjection.Types.Contains(node.Properties.GetValueOrDefault("logicalType"))
                || node.Properties.GetValueOrDefault("logicalLayer") is not (null or "Capability" or "Operational" or "System" or "Implementation")))
                throw new ArgumentException("Logical constructs require a supported DM2-aligned type and abstraction layer.");
            if (node.DataFlowRole is not null && (!SystemDataFlowProjection.Roles.Contains(node.DataFlowRole) || !SystemDataFlowProjection.CanAnnotate(node))
                || node.Kind == "LogicalConstruct" && node.DataFlowRole is not (null or "Function" or "Undetermined")
                || node.DataFlowRole == "ExternalEntity" && node.BoundaryDisposition == "InBoundary"
                || node.FunctionDescription?.Length > 4000 || node.DataRetention?.Length > 4000 || node.DisposalMethod?.Length > 4000)
                throw new ArgumentException("DFD roles and bounded handling annotations must describe eligible records; external entities cannot be inside the system boundary.");
            if (node.Kind == "DataFlowElement" && node.DataFlowRole is null)
                throw new ArgumentException("A proposed DFD element requires an explicit role or Undetermined.");
            if (node.NetworkRole is not null && (!SystemNetworkProjection.Roles.Contains(node.NetworkRole) || !SystemNetworkProjection.IsComponent(node))
                || (node.NetworkSegment is not null || node.NetworkAddress is not null || node.HostingImpactLevel is not null) && !SystemNetworkProjection.IsComponent(node)
                || node.NetworkRole == "ExternalSystem" && node.BoundaryDisposition == "InBoundary"
                || node.NetworkSegment?.Length > 500 || node.NetworkAddress?.Length > 100
                || node.HostingImpactLevel is not null && !SystemNetworkProjection.ImpactLevels.Contains(node.HostingImpactLevel))
                throw new ArgumentException("Network role, segment, address and claimed hosting impact level must be supported and bounded.");
            if (!string.IsNullOrWhiteSpace(node.NetworkAddress)
                && !System.Net.IPAddress.TryParse(node.NetworkAddress, out _) && !System.Net.IPNetwork.TryParse(node.NetworkAddress, out _))
                throw new ArgumentException("Network address must be a single recorded IPv4/IPv6 address or CIDR, not an inferred route.");
            var hasDeployment = node.SacaZone is not null || node.SacaRole is not null || node.DeploymentScopeNodeId is not null
                || node.DeploymentOwner is not null || node.DeploymentEvidenceReference is not null || node.DeploymentSecurityFunctions is not null;
            if (hasDeployment && !SystemDesignSemantics.IsArchitectureNode(node)
                || node.SacaZone is not null && !SystemAzureDeploymentProjection.Zones.Contains(node.SacaZone)
                || node.SacaRole is not null && !SystemAzureDeploymentProjection.Roles.Contains(node.SacaRole)
                || node.DeploymentOwner?.Length > 500 || node.DeploymentEvidenceReference?.Length > 2000 || node.DeploymentSecurityFunctions?.Length > 4000)
                throw new ArgumentException("SACA deployment annotations require an architecture record, supported zone/role and bounded fields.");
            if (node.SacaRole == "TCCM" && !SystemContextProjection.IsPerformer(node)
                || node.SacaRole is not (null or "TCCM" or "Undetermined") && SystemContextProjection.IsPerformer(node))
                throw new ArgumentException("TCCM is a recorded business performer, not a vault/appliance; technical SACA roles cannot be assigned to people.");
            if (node.DeploymentEvidenceReference is { } deploymentReference && !string.IsNullOrWhiteSpace(deploymentReference) && !SafeReference(deploymentReference))
                throw new ArgumentException("Deployment evidence requires an HTTPS or application-relative source reference.");
            if (node.DeploymentScopeNodeId is { } scopeId)
            {
                var scope = canonical.Nodes.SingleOrDefault(n => n.Id == scopeId && n.Kind == "Environment")
                    ?? throw new ArgumentException("Deployment scope must be an environment recorded in the current tenant/system.");
                var subscription = SystemAzureDeploymentProjection.ArmScope(node).Subscription;
                if (subscription is not null && !string.Equals(subscription, scope.Properties.GetValueOrDefault("subscriptionId"), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Selected deployment scope must match the exact ARM subscription.");
            }
            if (node.Properties.GetValueOrDefault("contextEntityClass") is { } entityClass && entityClass is not ("Performer" or "System")
                || node.Properties.GetValueOrDefault("contextEntityCategory") is { } category
                    && category is not ("Operational" or "SecurityCompliance" or "DataSource" or "SupportService"))
                throw new ArgumentException("Context entity class/category must use a supported recorded classification.");
            if (node.Properties.GetValueOrDefault("referenceUrl") is { } referenceUrl && !string.IsNullOrWhiteSpace(referenceUrl)
                && !SafeReference(referenceUrl))
                throw new ArgumentException("Constraint references require an HTTPS or application-relative source URL.");
            if (node.BoundaryDefinitionId is not null && !canonical.Nodes.Any(n => n.Kind == "BoundaryDefinition"
                && n.Source?.Id == node.BoundaryDefinitionId))
                throw new ArgumentException("Selected boundary must belong to the current tenant/system.");
            if (node.BoundaryRationale?.Length > 4000 || node.SecurityResponsibility?.Length > 4000
                || node.ExternalAuthorizationReference?.Length > 2000
                || node.BoundaryRelationship is not (null or "SystemManaged" or "SharedService" or "SeparatelyAuthorized" or "Undetermined"))
                throw new ArgumentException("Boundary responsibility, rationale and ownership relationship must be valid and bounded.");
            if (node.ExternalAuthorizationReference is { } externalReference && !string.IsNullOrWhiteSpace(externalReference)
                && !SafeReference(externalReference))
                throw new ArgumentException("External authorization references require an HTTPS or application-relative source URL.");
            if (node.BoundaryDisposition == "InBoundary" && (node.BoundaryRelationship is "SharedService" or "SeparatelyAuthorized"
                || node.Kind == "ExternalSystem" && node.Source?.Type == "SystemInterconnection"))
                throw new ArgumentException("Shared/separately authorized or canonical external systems must remain outside the workload boundary.");
            if (node.Source is null && node.Kind is not ("ExternalSystem" or "DesignComponent" or "ContextConstraint" or "LogicalConstruct" or "DataFlowElement"))
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
            if (edge.LifecycleStage is not null && !SystemDataFlowProjection.LifecycleStages.Contains(edge.LifecycleStage)
                || (edge.LifecycleStage is not null || edge.InformationTypeId is not null) && !SystemDesignSemantics.IsFlow(edge))
                throw new ArgumentException("DFD information/lifecycle annotations require a technical flow and a supported lifecycle stage.");
            if (edge.InformationTypeId is not null && !canonical.Nodes.Any(n => n.Kind == "InformationType" && n.Source?.Id == edge.InformationTypeId))
                throw new ArgumentException("Select an information type recorded in the current tenant/system.");
            if (edge.ProtocolStack?.Length > 2000 || edge.StandardsReference?.Length > 2000 || edge.SecurityControlReferences?.Length > 4000
                || edge.ConnectionMedium is not null && !SystemNetworkProjection.Media.Contains(edge.ConnectionMedium)
                || edge.StandardsReference is { } standards && !string.IsNullOrWhiteSpace(standards) && !SafeReference(standards))
                throw new ArgumentException("Network interface annotations must be bounded, use supported media and safe standards URLs.");
            if ((edge.ProtocolStack is not null || edge.StandardsReference is not null || edge.ConnectionMedium is not null || edge.SecurityControlReferences is not null)
                && !SystemDesignSemantics.IsFlow(edge))
                throw new ArgumentException("Protocol stacks, standards and connection media belong to actual technical interfaces, not source associations.");
            if (RecordedRelationshipType(edge.RelationshipType) && edge.RelationshipType != "Access" || edge.Source?.Type == "RecordedRelationship")
            {
                if (!knownEdges.TryGetValue(edge.Id, out var structural) || !structural.Any(x =>
                    x.Source?.Type == "RecordedRelationship" && SameRecordedSemantics(x, edge)))
                    throw new ArgumentException("Recorded structural and logical-access relationships require unchanged canonical endpoints, type and provenance.");
            }
            else if (!SystemLogicalProjection.Predicates.Contains(edge.RelationshipType) && edge.RelationshipType is not ("DataFlow" or "ServiceFlow" or "ResourceFlow" or "GovernanceInteraction" or "ConstraintReference"
                or "Interconnection" or "Access" or "UserAuthored" or "NetworkConnection" or "Dependency"))
                throw new ArgumentException("Unknown relationship type. Use DataFlow for authored traffic or retain a canonical relationship.");
            if (SystemLogicalProjection.Predicates.Contains(edge.RelationshipType))
            {
                if (prior.Edges.Any(old => old.Id == edge.Id && (SystemDesignSemantics.IsFlow(old) || old.Source is not null))
                    || edge.Source is not null || edge.PpsEntryId is not null || edge.InterconnectionId is not null || edge.AgreementStatus is not null
                    || !string.IsNullOrWhiteSpace(edge.Port) || !string.IsNullOrWhiteSpace(edge.Protocol)
                    || !string.IsNullOrWhiteSpace(edge.EncryptionState) || !string.IsNullOrWhiteSpace(edge.Service)
                    || !string.IsNullOrWhiteSpace(edge.Protection) || edge.Direction != "Outbound"
                    || !SystemLogicalProjection.ValidPredicate(edge.RelationshipType,
                        nodes.Single(n => n.Id == edge.SourceNodeId), nodes.Single(n => n.Id == edge.TargetNodeId)))
                    throw new ArgumentException("Logical predicates require valid directed constructs and no technical/canonical flow relabeling or transport fields.");
            }
            else if (edge.RelationshipType is "GovernanceInteraction" or "ConstraintReference")
            {
                if (prior.Edges.Any(old => old.Id == edge.Id && SystemDesignSemantics.IsFlow(old))
                    || edge.Source is not null || edge.PpsEntryId is not null || edge.InterconnectionId is not null
                    || !string.IsNullOrWhiteSpace(edge.Port) || !string.IsNullOrWhiteSpace(edge.Protocol)
                    || !string.IsNullOrWhiteSpace(edge.EncryptionState) || !string.IsNullOrWhiteSpace(edge.Service)
                    || !string.IsNullOrWhiteSpace(edge.Protection))
                    throw new ArgumentException("Do not relabel a technical or canonical flow as non-technical context. Record a separate attributable interaction.");
                var endpoints = nodes.Where(n => n.Id == edge.SourceNodeId || n.Id == edge.TargetNodeId).ToArray();
                if (edge.RelationshipType == "GovernanceInteraction" && !endpoints.Any(SystemContextProjection.IsPerformer)
                    || edge.RelationshipType == "ConstraintReference" && !endpoints.Any(SystemContextProjection.IsConstraint))
                    throw new ArgumentException("Non-technical context requires a recorded performer or constraint endpoint.");
            }
            else if (SystemDesignSemantics.IsFlow(edge) && nodes.Where(n => n.Id == edge.SourceNodeId || n.Id == edge.TargetNodeId)
                .Any(n => !SystemDataFlowProjection.IsEndpoint(n)))
                throw new ArgumentException("Technical flows must connect architecture entities, not policy/PPS/source documents.");
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
        if (!graph.Nodes.Any(n => n.Source?.Type == "ResolvedRmfRole"))
            Gap("ContextGovernanceContacts", graph.SystemId, "No assigned RMF governance contacts are present in this design. Review System team sources.",
                url: $"/systems/{Uri.EscapeDataString(graph.SystemId)}/roles", severity: "Warning");
        foreach (var contribution in graph.Contributions.Where(x => x.State is "Missing" or "Unapproved"))
            Gap("SourceContribution", contribution.Section, $"{contribution.Section}: {contribution.State}. Complete and review the canonical source.", url: contribution.ResolutionUrl);
        foreach (var node in graph.Nodes)
        {
            if (node.Kind == "AuthorizationScope")
                Gap("AuthorizationComponentCoverage", node.Id,
                    $"Recorded authorization: {node.Properties.GetValueOrDefault("currency") ?? "Unknown"}. Individual component/named-boundary coverage is not pinned; design inclusion and cATO are not established.",
                    "Boundary", node.Source?.ResolutionUrl, "Warning");
            if (SystemDesignSemantics.IsArchitectureNode(node) && !SystemContextProjection.IsPerformer(node) && node.Kind != "Environment")
            {
                if (node.BoundaryDisposition == "InBoundary" && string.IsNullOrWhiteSpace(node.SecurityResponsibility))
                    Gap("BoundaryResponsibility", node.Id, "Record the accepted security responsibility for this included component; design inclusion is not an ATO.", "Boundary", severity: "Warning");
                if (node.BoundaryDisposition != "Undetermined" && string.IsNullOrWhiteSpace(node.BoundaryRationale))
                    Gap("BoundaryScopeRationale", node.Id, "Record why this named resource is included/excluded.", "Boundary", severity: "Warning");
                if (node.BoundaryDisposition == "InBoundary" && node.BoundaryDefinitionId is null
                    && node.Properties.GetValueOrDefault("boundaryId") is null)
                    Gap("NamedBoundarySelection", node.Id, "Named boundary selection is not recorded for this included component.", "Boundary", severity: "Warning");
                if (node.BoundaryDisposition == "InBoundary" && node.BoundaryRelationship is "SharedService" or "SeparatelyAuthorized")
                    Gap("ExternalBoundaryConflict", node.Id, "A shared/separately authorized resource conflicts with inclusion. Stage correction without implying authorization.", "Boundary");
            }
            if (node.Kind == "InformationType" && (!string.IsNullOrWhiteSpace(node.Properties.GetValueOrDefault("Source"))
                || !string.IsNullOrWhiteSpace(node.Properties.GetValueOrDefault("Destination"))))
                Gap("ContextInformationEndpoints", node.Id,
                    $"Information type {node.Label} declares source '{node.Properties.GetValueOrDefault("Source") ?? "Not recorded"}' and destination '{node.Properties.GetValueOrDefault("Destination") ?? "Not recorded"}'. Verify exact context entity identities and attributable exchanges; names alone do not establish a flow.",
                    url: node.Source?.ResolutionUrl, severity: "Warning");
            if (SystemContextProjection.IsConstraint(node) && string.IsNullOrWhiteSpace(node.Properties.GetValueOrDefault("rationale")))
                Gap("ContextConstraintRationale", node.Id, "Record or review why this named policy/constraint applies; its presence is not applicability approval.",
                    url: node.Source?.ResolutionUrl, severity: "Warning");
            if (node.Kind == "PolicyReference" && node.Properties.GetValueOrDefault("retention") != "Retained")
                Gap("ContextPolicyRetention", node.Id, "This policy reference has no retained source snapshot. Review the named policy in its source workflow.",
                    url: node.Source?.ResolutionUrl, severity: "Warning");
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
            if (SystemLogicalProjection.Predicates.Contains(edge.RelationshipType))
            {
                if (string.IsNullOrWhiteSpace(edge.Purpose))
                    Gap("LogicalPurpose", edge.Id, "Record the reason for this logical relationship; it is not network traffic.", "Logical");
                continue;
            }
            if (edge.RelationshipType is "GovernanceInteraction" or "ConstraintReference")
            {
                if (string.IsNullOrWhiteSpace(edge.Purpose))
                    Gap("ContextInteractionPurpose", edge.Id, "Record the governance interaction or constraint applicability purpose.");
                continue;
            }
            if (edge.Source?.Type == "RecordedRelationship" || RecordedRelationshipType(edge.RelationshipType) && edge.RelationshipType != "Access")
            {
                Gap("RecordedRelationshipChanged", edge.Id,
                    "The recorded relationship no longer matches its exact canonical identity, endpoints, type and source version. Reconcile its source before review.");
                if (edge.RelationshipType != "Access") continue;
            }
            if (edge.Source?.Type == "SystemInterconnection" && edge.Source.ReviewState != "Approved")
                Gap("InterconnectionUnapproved", edge.Id, "The active canonical interconnection has no recorded authorization to connect. Design review does not grant it.", "Boundary", edge.Source.ResolutionUrl);
            if (string.IsNullOrWhiteSpace(edge.Purpose)) Gap("FlowPurpose", edge.Id, "Record the business purpose of this relationship.", "DataFlows");
            if (string.IsNullOrWhiteSpace(edge.InformationType)) Gap("FlowData", edge.Id, "Name the exchanged data; a hosting/resource association is not data.", "DataFlows", severity: "Warning");
            if (edge.LifecycleStage is null) Gap("FlowLifecycle", edge.Id, "Record whether this exchange receives, processes, stores, distributes or destroys data.", "DataFlows", severity: "Warning");
            if (edge.InformationTypeId is { } informationId)
            {
                var information = canonical.Nodes.SingleOrDefault(n => n.Kind == "InformationType" && n.Source?.Id == informationId);
                if (information is null || edge.InformationType != information.Properties.GetValueOrDefault("DataTypeName")
                    || edge.Classification != information.Properties.GetValueOrDefault("SensitivityClassification"))
                    Gap("FlowInformationChanged", edge.Id, "The selected information record/name/classification differs. Review its exact current source.", "DataFlows", severity: "Warning");
            }
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
        foreach (var type in SystemLogicalProjection.Types.Where(type => !graph.Nodes.Any(n => SystemLogicalProjection.Type(n) == type)))
            Gap("LogicalConstructMissing", $"logical:{type}", $"No {type} construct is recorded. Capture or review applicability; do not infer one.", "Logical", severity: "Warning");
        foreach (var node in graph.Nodes.Where(n => n.Kind == "LogicalConstruct"))
        {
            if (string.IsNullOrWhiteSpace(node.Properties.GetValueOrDefault("logicalLayer")))
                Gap("LogicalLayerMissing", node.Id, "Record this construct's abstraction layer.", "Logical", severity: "Warning");
            if (!graph.Edges.Any(e => e.SourceNodeId == node.Id || e.TargetNodeId == node.Id))
                Gap("LogicalUnrelated", node.Id, "No recorded relationship explains this logical construct.", "Logical", severity: "Warning");
            if (node.Properties.GetValueOrDefault("logicalSourceGap") is { } sourceGap)
                Gap("LogicalSourceSelection", node.Id, sourceGap, "Logical", node.Source?.ResolutionUrl, severity: "Warning");
        }
        var dfd = SystemDataFlowProjection.Project(graph.Nodes, graph.Edges);
        if (!dfd.Nodes.Any(n => SystemDataFlowProjection.Role(n) == "Function"))
            Gap("DfdFunctionsMissing", graph.SystemId, "No explicit system function is recorded. Annotate a source-backed function or add a governed function draft.", "DataFlows", severity: "Warning");
        foreach (var node in dfd.Nodes)
        {
            var role = SystemDataFlowProjection.Role(node);
            if (role == "Undetermined") Gap("DfdRoleMissing", node.Id, "Map this recorded endpoint to a function, store or external producer/consumer; its name alone is insufficient.", "DataFlows", severity: "Warning");
            if (role == "Function" && string.IsNullOrWhiteSpace(node.FunctionDescription))
                Gap("DfdFunctionDescription", node.Id, "Describe the actual transformation/system function.", "DataFlows", severity: "Warning");
            if (role == "DataStore" && (string.IsNullOrWhiteSpace(node.DataRetention) || string.IsNullOrWhiteSpace(node.DisposalMethod)))
                Gap("DfdStoreHandling", node.Id, "Record retention and disposal of data in this store.", "DataFlows", severity: "Warning");
            if (!dfd.Edges.Any(e => e.SourceNodeId == node.Id || e.TargetNodeId == node.Id) && role != "Undetermined")
                Gap("DfdNoFlow", node.Id, "This DFD record has no documented information exchange. Do not infer one from hosting or membership.", "DataFlows", severity: "Warning");
        }
        var network = SystemNetworkProjection.Project(graph.Nodes, graph.Edges);
        foreach (var node in network.Nodes.Where(n => n.Kind != "System" && !SystemContextProjection.IsPerformer(n)))
        {
            if (node.NetworkRole is null or "Undetermined")
                Gap("NetworkRoleMissing", node.Id, "Record the actual network/device/application role; names and CSP references do not establish it.", "Network", severity: "Warning");
            if (string.IsNullOrWhiteSpace(node.NetworkSegment))
                Gap("NetworkSegmentMissing", node.Id, "Record the internal/external network segment or document applicability.", "Network", severity: "Warning");
            if (node.NetworkRole is "Server" or "VirtualMachine" or "NetworkSegment" or "Router" or "Switch" or "Firewall" or "LoadBalancer" or "VpnGateway"
                && string.IsNullOrWhiteSpace(node.NetworkAddress) && string.IsNullOrWhiteSpace(node.Properties.GetValueOrDefault("IpAddress")))
                Gap("NetworkAddressMissing", node.Id, "Record the source-backed IP/CIDR for this addressable device/segment; do not infer it.", "Network", severity: "Warning");
            if (node.Source is null)
                Gap("NetworkInventoryMapping", node.Id, "Proposed network element has no canonical inventory source. Reconcile its owning inventory before package acceptance.", "Network", severity: "Warning");
        }
        foreach (var edge in network.Edges.Where(SystemDesignSemantics.IsFlow))
        {
            if (string.IsNullOrWhiteSpace(edge.ProtocolStack))
                Gap("NetworkStackMissing", edge.Id, "Record the actual protocol stack, separate from the single PPS protocol.", "Network", severity: "Warning");
            if (string.IsNullOrWhiteSpace(edge.StandardsReference))
                Gap("NetworkStandardsMissing", edge.Id, "Record the applicable standards-profile reference; a URL alone does not verify conformance.", "Network", severity: "Warning");
            if (edge.ConnectionMedium == "DISN")
                Gap("DisnApprovalEvidence", edge.Id, "Recorded DISN connection requires source approval/evidence review; the medium label is not an authorization to connect.", "Network", severity: "Warning");
        }
        var networkIds = network.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var edge in graph.Edges.Where(e => SystemDesignSemantics.IsFlow(e)
            && (!networkIds.Contains(e.SourceNodeId) || !networkIds.Contains(e.TargetNodeId))))
            Gap("NetworkEndpointMapping", edge.Id, "Technical exchange includes functional/hosting records not mapped to computing/user endpoints. Retained in DFD/full design; document its actual network mapping.", "Network", severity: "Warning");
        var deployment = SystemAzureDeploymentProjection.Project(graph.Nodes, graph.Edges);
        foreach (var role in SystemAzureDeploymentProjection.ReferenceRoles.Where(r => !deployment.Nodes.Any(n => n.SacaRole == r)))
            Gap("SacaRoleMissing", $"saca:{role}", $"{role} is not recorded. Review applicability, responsibility and evidence; a reference diagram does not implement it.", "AzureDeployment", severity: "Warning");
        foreach (var node in deployment.Nodes)
        {
            if (node.SacaRole == "TCCM")
                Gap("TccmAppointment", node.Id, "TCCM is a business role. AO appointment and credential-management plan are not verified by this annotation.", "AzureDeployment", severity: "Warning");
            if (SystemAzureDeploymentProjection.Zone(node) == "Undetermined" && node.SacaRole != "TCCM")
                Gap("DeploymentZoneMissing", node.Id, "Record actual SACA placement; CSP linkage/name does not establish an Azure Government deployment.", "AzureDeployment", severity: "Warning");
            if (node.SacaRole is not (null or "Undetermined"))
            {
                if (string.IsNullOrWhiteSpace(node.DeploymentOwner) || string.IsNullOrWhiteSpace(node.DeploymentEvidenceReference))
                    Gap("DeploymentResponsibilityEvidence", node.Id, "Record responsibility and source evidence for this SACA role; inheritance and implementation are not established.", "AzureDeployment", severity: "Warning");
                if (node.SacaRole != "TCCM" && string.IsNullOrWhiteSpace(node.DeploymentSecurityFunctions))
                    Gap("DeploymentSecurityFunctions", node.Id, "Record actual transit/at-rest protection, isolation, access and monitoring functions as applicable, with evidence.", "AzureDeployment", severity: "Warning");
            }
            if (SystemAzureDeploymentProjection.Zone(node) == "AzureCloud"
                && SystemAzureDeploymentProjection.Scope(node, graph.Nodes, graph.Edges)?.Properties.GetValueOrDefault("cloud") is not ("AzureCloud" or "AzureUSGovernment"))
                Gap("DeploymentCloudIdentity", node.Id, "Exact recorded Azure cloud scope is unavailable/ambiguous. ARM identity or role labels do not prove Azure Government/IL accreditation.", "AzureDeployment", severity: "Warning");
        }
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
