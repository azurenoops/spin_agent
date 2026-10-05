using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Ato.Copilot.Core.Dtos.SystemDesign;

namespace Ato.Copilot.Agents.Compliance.Services;

internal sealed record SystemDesignDiagramNode(string Id, string Label, string BoundaryDisposition, string Kind,
    string? Environment = null, string? NetworkZone = null, string? DiagramRole = null,
    Dictionary<string, string?>? Properties = null, string ReviewState = "Not recorded", string? Provider = null,
    DesignSource? Source = null);
internal sealed record SystemDesignDiagramEdge(string Id, string SourceId, string TargetId, string Purpose,
    string? Protocol, string? Port, string? Protection, string Provenance = "User-authored design relationship",
    string RelationshipType = "DataFlow", bool IsFlow = true, string? Service = null,
    string? InformationType = null, string? Classification = null, string Direction = "Outbound", DesignSource? Source = null,
    string? InterconnectionId = null, string? AgreementStatus = null,
    string? LifecycleStage = null, string? InformationTypeId = null, string BoundaryCrossing = "Unknown",
    string? ProtocolStack = null, string? StandardsReference = null, string? ConnectionMedium = null, string? SecurityControlReferences = null);
internal sealed record SystemDesignDiagramSource(string SystemId, string SystemName, string BaselineId, long Revision,
    DateTimeOffset? ApprovedAt, string ApprovedBy, string? HandlingMarking, string SourceHash,
    IReadOnlyList<SystemDesignDiagramNode> Nodes, IReadOnlyList<SystemDesignDiagramEdge> Edges,
    string ReviewState = "Approved");
internal sealed record SystemDesignDiagramArtifact(string Id, string View, string Title, string Description,
    string FileName, string MediaType, byte[] Content, string ContentHash);

/// <summary>Self-contained deterministic SVG recipes; no browser state or external resources are accepted.</summary>
internal static class SystemDesignDiagramRenderer
{
    internal const string RecipeVersion = "11";
    internal static readonly string[] Views = ["context", "boundary", "logical", "data-flow", "network", "azure-deployment"];
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    internal static SystemDesignDiagramArtifact Render(SystemDesignDiagramSource source, string view)
    {
        if (!Views.Contains(view)) throw new ArgumentException("Unsupported design diagram view.", nameof(view));
        var sourceIds = source.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        if (sourceIds.Count != source.Nodes.Count || source.Edges.Any(e => !sourceIds.Contains(e.SourceId) || !sourceIds.Contains(e.TargetId)))
            throw new InvalidOperationException("Diagram node identity or relationship endpoint is invalid.");
        DesignNode[] deploymentNodes = view == "azure-deployment" ? source.Nodes.Select(DeploymentNode).ToArray() : [];
        var nodes = source.Nodes.Where(n => SystemDesignSemantics.IsArchitectureNode(n.Kind, n.DiagramRole, n.Properties))
            .OrderBy(n => Group(n, view), StringComparer.Ordinal).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray();
        if (view == "logical") nodes = source.Nodes.Where(n => SystemLogicalProjection.Type(n.Kind, n.Properties, n.DiagramRole) is not null)
            .OrderBy(n => Group(n, view), StringComparer.Ordinal).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray();
        if (view == "azure-deployment")
        {
            var deployment = SystemAzureDeploymentProjection.Project(deploymentNodes,
                source.Edges.Select(e => new DesignEdge { Id = e.Id, SourceNodeId = e.SourceId, TargetNodeId = e.TargetId,
                    RelationshipType = e.RelationshipType, Source = e.Source }).ToArray());
            var selected = deployment.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            nodes = source.Nodes.Where(n => selected.Contains(n.Id)).OrderBy(n => Group(n, view), StringComparer.Ordinal).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray();
        }
        var ids = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var edges = source.Edges.Where(e => ids.Contains(e.SourceId) && ids.Contains(e.TargetId)
            && (view != "data-flow" || e.IsFlow)).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        var originalEdges = source.Edges.ToDictionary(e => e.Id, StringComparer.Ordinal);
        if (view == "network")
        {
            var network = SystemNetworkProjection.Project(source.Nodes.Select(n => new DesignNode
            {
                Id = n.Id, Kind = n.Kind, DiagramRole = n.DiagramRole, Properties = n.Properties ?? [],
                BoundaryDisposition = n.BoundaryDisposition
            }).ToArray(), source.Edges.Select(e => new DesignEdge
            {
                Id = e.Id, SourceNodeId = e.SourceId, TargetNodeId = e.TargetId, RelationshipType = e.RelationshipType, Source = e.Source
            }).ToArray());
            var selected = network.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            nodes = source.Nodes.Where(n => selected.Contains(n.Id)).OrderBy(n => Group(n, view), StringComparer.Ordinal).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray();
            edges = network.Edges.Select(e => originalEdges[e.Id]).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        }
        if (view == "azure-deployment") edges = edges.Where(e => SystemAzureDeploymentProjection.IsRelationship(
            new DesignEdge { RelationshipType = e.RelationshipType, SourceNodeId = e.SourceId, TargetNodeId = e.TargetId, Source = e.Source },
            deploymentNodes)).ToArray();
        SystemContextView? context = null;
        if (view == "context")
        {
            context = SystemContextProjection.Project(source.Nodes.Select(n => new DesignNode
            {
                Id = n.Id, Label = n.Label, Kind = n.Kind, DiagramRole = n.DiagramRole, Source = n.Source,
                BoundaryDisposition = n.BoundaryDisposition, Properties = n.Properties ?? []
            }).ToArray(), source.Edges.Select(e => new DesignEdge
            {
                Id = e.Id, SourceNodeId = e.SourceId, TargetNodeId = e.TargetId, RelationshipType = e.RelationshipType, Source = e.Source
            }).ToArray());
            var selected = context.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            nodes = nodes.Where(n => selected.Contains(n.Id)).ToArray();
            edges = context.Edges.Select(e => originalEdges[e.Id] with { SourceId = e.SourceNodeId, TargetId = e.TargetNodeId }).ToArray();
        }
        if (view == "data-flow")
        {
            var dfd = SystemDataFlowProjection.Project(source.Nodes.Select(n => new DesignNode
            {
                Id = n.Id, Label = n.Label, Kind = n.Kind, DiagramRole = n.DiagramRole, Source = n.Source,
                Properties = n.Properties ?? [], BoundaryDisposition = n.BoundaryDisposition,
                DataFlowRole = Value(n, "dataFlowRole")
            }).ToArray(), source.Edges.Select(e => new DesignEdge
            {
                Id = e.Id, SourceNodeId = e.SourceId, TargetNodeId = e.TargetId, RelationshipType = e.RelationshipType, Source = e.Source
            }).ToArray());
            var selected = dfd.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            nodes = source.Nodes.Where(n => selected.Contains(n.Id)).OrderBy(n => Group(n, view), StringComparer.Ordinal).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray();
            edges = dfd.Edges.Select(e => originalEdges[e.Id]).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        }
        var positions = Layout(nodes, edges, view);
        var graphBottom = positions.Count == 0 ? 230 : positions.Values.Max(p => p.Y) + 170;
        var approved = source.ReviewState == "Approved";
        var description = approved
            ? $"{source.ReviewState} design {source.BaselineId}, revision {source.Revision}; " +
                $"baseline timestamp {source.ApprovedAt:O}; reviewer {source.ApprovedBy}; source SHA-256 {source.SourceHash}; SVG recipe {RecipeVersion}."
            : $"{source.ReviewState} working design {source.BaselineId}, revision {source.Revision}; " +
                $"source mode CurrentWorkingData; source SHA-256 {source.SourceHash}; SVG recipe {RecipeVersion}. No approval or authorization is implied.";
        var title = (approved ? "" : $"{source.ReviewState} — ") + $"{source.SystemName} — {view} diagram";
        var svg = new XElement(Svg + "svg", new XAttribute("width", 1140),
            new XAttribute("height", graphBottom + edges.Length * 72 + (context?.Constraints.Count ?? 0) * 52
                + (view == "boundary" ? source.Nodes.Count(n => n.Kind == "AuthorizationScope") * 65 : 0) + 170),
            new XAttribute("viewBox", $"0 0 1140 {graphBottom + edges.Length * 72 + (context?.Constraints.Count ?? 0) * 52
                + (view == "boundary" ? source.Nodes.Count(n => n.Kind == "AuthorizationScope") * 65 : 0) + 170}"),
            new XAttribute("role", "img"), new XAttribute("aria-labelledby", "title description"),
            new XElement(Svg + "title", new XAttribute("id", "title"), title),
            new XElement(Svg + "desc", new XAttribute("id", "description"), description),
            new XElement(Svg + "rect", new XAttribute("width", "100%"), new XAttribute("height", "100%"), new XAttribute("fill", "#ffffff")));
        Text(svg, 24, 30, title, 22);
        Text(svg, 24, 56, approved
            ? $"{source.ReviewState} baseline: {source.BaselineId}; version {source.Revision}; reviewer: {source.ApprovedBy}"
            : $"Working snapshot: {source.BaselineId}; version {source.Revision}");
        Text(svg, 24, 78, approved
            ? $"Baseline timestamp: {source.ApprovedAt?.ToString("O", CultureInfo.InvariantCulture)}; artifact recipe: {RecipeVersion}"
            : $"Approval timestamp/reviewer: not applicable; source-record timestamp: not recorded; artifact recipe: {RecipeVersion}");
        Text(svg, 24, 100, $"Source provenance SHA-256: {source.SourceHash}");
        Text(svg, 24, 122, $"Handling marking: {source.HandlingMarking ?? "Not recorded"}");
        Text(svg, 24, 147, "Legend: solid boundary = in boundary; dashed = outside; dotted = undetermined. Arrow = directed relationship.");
        Text(svg, 24, 168, "Relationship provenance is printed below; observed/generated links do not imply accepted inheritance or authorization.");
        if (view == "boundary") Text(svg, 24, 190,
            "Included design scope is not verified component authorization coverage. System decisions/CSP links do not establish ATO or cATO.", 11);
        if (view == "logical") Text(svg, 24, 190,
            "DM2-aligned records; predicates are not traffic; Realizes = explicit refinement. Not formal DoDAF/PES conformance.", 11);
        if (view == "data-flow") Text(svg, 24, 190,
            "DFD legend: Function box / Open data store / External producer-consumer / Unmapped endpoint. CSP links are not flows.", 11);
        if (view == "network") Text(svg, 24, 190,
            $"Network: {edges.Count(e => e.IsFlow)} technical interfaces; {edges.Count(e => !e.IsFlow)} dashed source associations (not traffic). ATO/IL/DISN unverified.", 11);
        if (view == "azure-deployment") Text(svg, 24, 190,
            "SACA zones: on-premises/DISN / secure cloud access / recorded Azure scope. TCCM = business role. Compliance/appointment unverified.", 11);
        if (view == "context") Text(svg, 24, 190,
            "SV-1-aligned context overview; internal detail is summarized. Governance/constraints are not traffic or DoDAF certification.", 11);
        if (view == "data-flow" && edges.Length == 0)
            Text(svg, 24, 190, "Data flows: not recorded between architecture elements. Structural associations do not establish traffic.");
        else if (nodes.Length == 0)
            Text(svg, 24, 190, view == "azure-deployment"
                ? "Azure deployment: no attached environment or exact ARM resource identity recorded."
                : "Architecture elements: not recorded.");

        foreach (var group in nodes.GroupBy(n => Group(n, view)))
        {
            if (view == "context" && group.Key != "System") continue;
            var points = group.Select(n => positions[n.Id]).ToArray();
            var x = points.Min(p => p.X) - 10;
            var y = points.Min(p => p.Y) - 23;
            var frame = new XElement(Svg + "rect", new XAttribute("x", x), new XAttribute("y", y),
                new XAttribute("width", points.Max(p => p.X) - x + 330),
                new XAttribute("height", points.Max(p => p.Y) - y + 160),
                new XAttribute("rx", 12), new XAttribute("fill", "none"),
                new XAttribute("stroke", view == "boundary" ? "#6046ff" : "#cbd5e1"),
                new XAttribute("stroke-dasharray", view == "boundary" ? "6 4" : "none"));
            svg.Add(frame);
            Text(svg, x + 5, y + 16, view == "context" ? "Recorded system context scope · not an authorization decision" : group.Key, 12);
        }
        foreach (var edge in edges)
        {
            var start = positions[edge.SourceId];
            var end = positions[edge.TargetId];
            var horizontal = start.X != end.X;
            var x1 = horizontal ? start.X + (end.X > start.X ? 320 : 0) : start.X + 160;
            var y1 = horizontal ? start.Y + 65 : start.Y + (end.Y > start.Y ? 130 : 0);
            var x2 = horizontal ? end.X + (end.X > start.X ? 0 : 320) : end.X + 160;
            var y2 = horizontal ? end.Y + 65 : end.Y + (end.Y > start.Y ? 0 : 130);
            svg.Add(new XElement(Svg + "line", new XAttribute("data-edge-id", edge.Id),
                new XAttribute("data-relationship-type", edge.RelationshipType), new XAttribute("x1", x1), new XAttribute("y1", y1),
                new XAttribute("x2", x2), new XAttribute("y2", y2), new XAttribute("stroke", "#475569"),
                new XAttribute("stroke-dasharray", view is "network" or "azure-deployment" && !edge.IsFlow ? "10 2"
                    : edge.Provenance.Contains("Canonical", StringComparison.OrdinalIgnoreCase) ? "none" : "5 3")));
            if (edge.SourceId != edge.TargetId && (edge.IsFlow || view == "logical"))
            {
                var angle = Math.Atan2(y2 - y1, x2 - x1);
                var arrow = $"{F(x2)},{F(y2)} {F(x2 - 10 * Math.Cos(angle - .4))},{F(y2 - 10 * Math.Sin(angle - .4))} " +
                    $"{F(x2 - 10 * Math.Cos(angle + .4))},{F(y2 - 10 * Math.Sin(angle + .4))}";
                svg.Add(new XElement(Svg + "polygon", new XAttribute("points", arrow), new XAttribute("fill", "#475569")));
                if (edge.Direction == "Bidirectional")
                {
                    var reverse = $"{F(x1)},{F(y1)} {F(x1 + 10 * Math.Cos(angle - .4))},{F(y1 + 10 * Math.Sin(angle - .4))} " +
                        $"{F(x1 + 10 * Math.Cos(angle + .4))},{F(y1 + 10 * Math.Sin(angle + .4))}";
                    svg.Add(new XElement(Svg + "polygon", new XAttribute("points", reverse), new XAttribute("fill", "#475569")));
                }
            }
            var annotation = new XElement(Svg + "g", new XAttribute("data-edge-label", edge.Id));
            Text(annotation, (x1 + x2) / 2, (y1 + y2) / 2 - 8, EdgeLabel(edge, view), 10);
            svg.Add(annotation);
        }
        foreach (var node in nodes)
        {
            var position = positions[node.Id];
            var group = new XElement(Svg + "g", new XAttribute("data-node-id", node.Id));
            if (view == "network") group.Add(new XElement(Svg + "title",
                $"{node.Label}; role {Value(node, "networkRole") ?? "Not recorded"}; segment {Value(node, "networkSegment") ?? "Not recorded"}; " +
                $"IP/CIDR {Value(node, "networkAddress", "IpAddress") ?? "Not recorded"}; claimed hosting IL {Value(node, "hostingImpactLevel") ?? "Not recorded"}; " +
                $"authorization/IL coverage not verified; source {node.Source?.Type ?? "Governed draft"}/{node.Source?.Id ?? node.Id}; version {node.Source?.Version ?? "Working"}"));
            if (view == "azure-deployment") group.Add(new XElement(Svg + "title",
                $"{node.Label}; SACA role {Value(node, "sacaRole") ?? "Not recorded"}; zone {SystemAzureDeploymentProjection.Zone(DeploymentNode(node))}; " +
                $"owner {Value(node, "deploymentOwner") ?? "Not recorded"}; evidence {Value(node, "deploymentEvidenceReference") ?? "Not recorded"}; " +
                $"security functions {Value(node, "deploymentSecurityFunctions") ?? "Not recorded"}; cloud {Value(node, "recordedDeploymentCloud", "cloud") ?? "Not recorded"}; " +
                $"network {Value(node, "networkAddress", "IpAddress") ?? "Not recorded"}; segment {Value(node, "networkSegment") ?? "Not recorded"}; claimed IL {Value(node, "hostingImpactLevel") ?? "Not recorded"}; " +
                $"directory {Value(node, "recordedDeploymentDirectoryId", "directoryTenantId") ?? "Not recorded"}; source {node.Source?.Type ?? "Governed draft"}/{node.Source?.Id ?? node.Id}; version {node.Source?.Version ?? "Working"}; implementation/appointment/authorization unverified"));
            if (view == "logical") group.Add(new XElement(Svg + "title",
                $"{node.Label}; DM2 type {SystemLogicalProjection.Type(node.Kind, node.Properties, node.DiagramRole)}; " +
                $"layer {Value(node, "logicalLayer") ?? "Not recorded"}; description {Value(node, "description", "Description") ?? "Not recorded"}; " +
                $"conditions {Value(node, "conditions") ?? "Not recorded"}; desired effects {Value(node, "desiredEffect") ?? "Not recorded"}; " +
                $"source {node.Source?.Type ?? "Governed draft"}/{node.Source?.Id ?? node.Id}; version {node.Source?.Version ?? "Working"}"));
            var dfdRole = Value(node, "dataFlowRole") ?? (node.Kind == "ExternalSystem" ? "ExternalEntity" : "Undetermined");
            var box = new XElement(Svg + "rect", new XAttribute("x", position.X), new XAttribute("y", position.Y),
                new XAttribute("width", 320), new XAttribute("height", 130), new XAttribute("rx", 8),
                new XAttribute("fill", node.BoundaryDisposition == "InBoundary" ? "#f1effc" : "#f8fafc"),
                new XAttribute("stroke", node.BoundaryDisposition == "Undetermined" ? "#dc2626" : "#93a4bb"),
                new XAttribute("stroke-dasharray", node.BoundaryDisposition == "InBoundary" ? "none" :
                    node.BoundaryDisposition == "OutOfBoundary" ? "7 4" : "2 3"));
            if (view == "data-flow")
            {
                box.SetAttributeValue("data-dfd-role", dfdRole);
                box.SetAttributeValue("rx", 0);
                if (dfdRole == "DataStore")
                {
                    box.SetAttributeValue("stroke", "none");
                    group.Add(new XElement(Svg + "path", new XAttribute("data-dfd-store", node.Id),
                        new XAttribute("d", $"M {position.X + 320} {position.Y} H {position.X} V {position.Y + 130} H {position.X + 320}"),
                        new XAttribute("fill", "none"), new XAttribute("stroke", "#475569"), new XAttribute("stroke-width", 2)));
                }
                group.Add(new XElement(Svg + "title",
                    $"{node.Label}; DFD role {dfdRole}; function {Value(node, "functionDescription") ?? "Not recorded"}; " +
                    $"retention {Value(node, "dataRetention") ?? "Not recorded"}; disposal {Value(node, "disposalMethod") ?? "Not recorded"}; " +
                    $"source {node.Source?.Type ?? "Governed draft"}/{node.Source?.Id ?? node.Id}; version {node.Source?.Version ?? "Working"}"));
            }
            group.AddFirst(box);
            Text(group, position.X + 8, position.Y + 23, node.Label, 13);
            Text(group, position.X + 8, position.Y + 45, view == "context"
                ? node.Kind == "System" ? "Information system under ATO review" :
                    node.Kind == "ActorGroup" || Value(node, "contextEntityClass", "ComponentType") is "Performer" or "Person"
                        ? $"Performer · {Value(node, "contextEntityCategory") ?? "Operational"}"
                        : $"System · {Value(node, "contextEntityCategory") ?? (node.Kind is "Environment" or "ProviderReference" ? "SupportService" : "Operational")}"
                : view == "logical" ? $"DM2 type: {SystemLogicalProjection.Type(node.Kind, node.Properties, node.DiagramRole)}"
                : view == "data-flow" ? $"DFD role: {dfdRole}"
                : view == "network" ? $"Network role: {Value(node, "networkRole") ?? "Not recorded"}"
                : view == "azure-deployment" ? $"SACA role: {Value(node, "sacaRole") ?? "Not recorded"}; {(Value(node, "sacaRole") == "TCCM" ? "Business performer" : "Recorded context")}"
                : Value(node, "SubType", "AzureResourceType", "resourceType", "ComponentType", "Type") ?? node.Kind, 11);
            Text(group, position.X + 8, position.Y + 64, view == "logical"
                ? Compact(Value(node, "description", "Description", "desiredEffect") ?? "Description / effect not recorded")
                : view == "azure-deployment" ? Compact(Value(node, "SubType", "AzureResourceType", "resourceType", "ComponentType", "Type") ?? node.Kind)
                : string.Join(" · ", new[]
            {
                Value(node, "AccessMethod"), Value(node, "OperatingSystem", "Version"),
                Value(node, "DataSensitivityLevel", "SensitivityClassification"), node.Provider
            }.Where(v => !string.IsNullOrWhiteSpace(v))), 10);
            Text(group, position.X + 8, position.Y + 84, node.BoundaryDisposition == "InBoundary" ? view is "boundary" or "network" ? "Included design scope · authorization unverified" : "In boundary" :
                node.BoundaryDisposition == "OutOfBoundary" ? "External" : "Boundary undetermined", 10);
            Text(group, position.X + 8, position.Y + 103, $"Source review: {node.ReviewState}", 10);
            if (view == "azure-deployment") Text(group, position.X + 8, position.Y + 122,
                Compact($"{Value(node, "recordedDeploymentCloud", "cloud") ?? "Cloud identity not verified"} / {Value(node, "deploymentOwner") ?? "Owner not recorded"}"), 10);
            if (view == "network") Text(group, position.X + 8, position.Y + 122,
                Compact($"{Value(node, "networkAddress", "IpAddress") ?? "IP/CIDR not recorded"} / {Value(node, "networkSegment") ?? "Segment not recorded"}"), 10);
            if (view == "boundary") Text(group, position.X + 8, position.Y + 122,
                $"Responsibility: {Value(node, "securityResponsibility", "Owner") ?? "Not recorded"}", 10);
            if (view == "data-flow") Text(group, position.X + 8, position.Y + 122,
                Compact(Value(node, "functionDescription", "dataRetention") ?? "Function / handling details not recorded"), 10);
            if (view == "logical") Text(group, position.X + 8, position.Y + 122,
                $"Layer: {Value(node, "logicalLayer") ?? "Not recorded"}; source: {node.Source?.Type ?? "Governed draft"}", 10);
            svg.Add(group);
        }
        var rowY = graphBottom + 20;
        foreach (var edge in edges)
        {
            Text(svg, 24, rowY, $"{edge.SourceId} → {edge.TargetId}: {edge.RelationshipType}; {edge.Purpose}", 12);
            Text(svg, 24, rowY + 20, edge.IsFlow
                ? $"{edge.Protocol ?? "Protocol not recorded"} / {edge.Port ?? "Port not recorded"}; " +
                    $"{edge.Service ?? "Service not recorded"}; {edge.Protection ?? "Protection not recorded"}; " +
                    $"{edge.InformationType ?? "Data not recorded"}; {edge.Classification ?? "Classification not recorded"}; {edge.Provenance}"
                : $"Not a recorded data flow; {edge.Provenance}", 11);
            if (view == "context")
            {
                var original = originalEdges[edge.Id];
                Text(svg, 24, rowY + 40, $"Original interface {original.Id}: {original.SourceId} → {original.TargetId}; context: {edge.SourceId} → {edge.TargetId}", 10);
            }
            if (view == "boundary") Text(svg, 24, rowY + 40,
                $"Interconnection: {edge.InterconnectionId ?? "Not recorded"}; agreement: {edge.AgreementStatus ?? "Not recorded"}; classification: {edge.Classification ?? "Not recorded"}", 10);
            if (view == "data-flow") Text(svg, 24, rowY + 40,
                $"Lifecycle: {edge.LifecycleStage ?? "Not recorded"}; information reference: {edge.InformationTypeId ?? "Not selected"}; crossing: {edge.BoundaryCrossing}; agreement: {edge.AgreementStatus ?? "Not recorded"}", 10);
            if (view == "network" && edge.IsFlow) Text(svg, 24, rowY + 40,
                $"Stack: {edge.ProtocolStack ?? "Not recorded"}; medium: {edge.ConnectionMedium ?? "Not recorded"}; crossing: {edge.BoundaryCrossing}; agreement: {edge.AgreementStatus ?? "Not recorded"}", 10);
            if (view == "network" && edge.IsFlow) svg.Add(new XElement(Svg + "desc",
                $"Interface {edge.Id}; standards profile {edge.StandardsReference ?? "Not recorded"}; controls {edge.SecurityControlReferences ?? "Not recorded"}; compliance/implementation not verified."));
            rowY += 72;
        }
        if (view == "azure-deployment")
        {
            Text(svg, 24, rowY, "SACA roles not recorded / applicability review: " + string.Join(", ",
                SystemAzureDeploymentProjection.ReferenceRoles.Where(role => !nodes.Any(n => Value(n, "sacaRole") == role))), 11);
            Text(svg, 24, rowY + 20, "TCCM appointment, SCCA compliance, control implementation, inherited responsibility and authorization are not verified.", 11);
        }
        if (context is not null)
        {
            Text(svg, 24, rowY, $"Internal elements summarized: {context.CollapsedCount}. Boundary decisions remain in the detailed boundary records.", 11);
            rowY += 24;
            foreach (var constraint in context.Constraints)
            {
                Text(svg, 24, rowY, $"Constraint reference: {constraint.Label}; applicability approval not established.", 11);
                Text(svg, 24, rowY + 20, $"Source: {constraint.Source?.Type ?? "Design draft"}/{constraint.Source?.Id ?? constraint.Id}; {constraint.Properties.GetValueOrDefault("rationale") ?? "Rationale not recorded"}", 10);
                rowY += 52;
            }
        }
        if (view == "boundary")
        {
            Text(svg, 24, rowY, "ABD legend: included design components / external systems / undetermined scope / technical arrows / network segments.", 11);
            Text(svg, 24, rowY + 20, "People and hosting references are not computing components. Shared/separately authorized systems remain outside.", 11);
            Text(svg, 24, rowY + 40, "Reviewed design is not an AO decision. Component-to-decision coverage and cATO scope remain unverified.", 11);
            foreach (var decision in source.Nodes.Where(n => n.Kind == "AuthorizationScope"))
            {
                rowY += 65;
                var reference = new XElement(Svg + "g", new XAttribute("data-authorization-reference", decision.Id),
                    new XElement(Svg + "title", $"Source {decision.Source?.Type ?? "Not recorded"}/{decision.Source?.Id ?? decision.Id}; " +
                        $"version {decision.Source?.Version ?? "Not recorded"}; currency {Value(decision, "currency") ?? "Not recorded"}; component coverage not verified"));
                Text(reference, 24, rowY, $"Authorization reference: {decision.Label}; component coverage not verified.", 11);
                Text(reference, 24, rowY + 20, $"Decision date: {Value(decision, "DecisionDate") ?? "Not recorded"}; " +
                    $"expiration: {Value(decision, "ExpirationDate") ?? "Not recorded"}; issuer: {Value(decision, "IssuedBy") ?? "Not recorded"}", 10);
                svg.Add(reference);
            }
        }
        var bytes = Encoding.UTF8.GetBytes(svg.ToString(SaveOptions.DisableFormatting));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var id = new PackageUuidRegistry(source.SystemId).GetOrCreate("design-diagram",
            $"{source.BaselineId}:{source.Revision}:{view}:{RecipeVersion}").ToString();
        return new(id, view, title, description, $"system-design-{view}.svg", "image/svg+xml", bytes, hash);
    }

    private static string Group(SystemDesignDiagramNode node, string view) => view switch
    {
        "boundary" => Value(node, "diagramBoundaryGroup") ?? (node.Kind == "ActorGroup" || Value(node, "contextEntityClass", "ComponentType") is "Performer" or "Person"
            ? "External actors / governance (not components)" : node.Kind == "Environment" ? "Hosting scope references (not authorized components)"
            : Value(node, "boundaryRelationship") is "SharedService" or "SeparatelyAuthorized" ? "Outside authorization boundary"
            : node.BoundaryDisposition == "InBoundary" ? "Authorization boundary · In boundary" :
            node.BoundaryDisposition == "OutOfBoundary" ? "Outside authorization boundary" : "Boundary undetermined"),
        "network" => Value(node, "diagramNetworkGroup") ?? $"{Value(node, "diagramBoundaryGroup") ?? node.BoundaryDisposition} / {node.Environment ?? "Environment not recorded"} / {node.NetworkZone ?? "Zone not recorded"} / {Value(node, "networkSegment") ?? "Segment not recorded"}",
        "azure-deployment" => Value(node, "diagramDeploymentGroup") ?? SystemAzureDeploymentProjection.Group(DeploymentNode(node), [], []),
        "logical" => SystemLogicalProjection.Group(node.Kind, node.Properties, node.DiagramRole),
        "data-flow" => Value(node, "diagramDataFlowGroup") ?? ((Value(node, "dataFlowRole") ?? (node.Kind == "ExternalSystem" ? "ExternalEntity" : "Undetermined")) == "ExternalEntity"
            ? "External producers / consumers" : $"{node.BoundaryDisposition} · functions / stores"),
        "context" => node.Kind == "System" ? "System" : node.Kind == "ActorGroup" ? "Actors" : "Dependencies",
        _ => "Directed data flows"
    };

    private static Dictionary<string, (int X, int Y)> Layout(SystemDesignDiagramNode[] nodes,
        IReadOnlyList<SystemDesignDiagramEdge> edges, string view)
    {
        var result = new Dictionary<string, (int X, int Y)>();
        if (view == "context")
        {
            var center = nodes.SingleOrDefault(n => n.Kind == "System");
            var left = nodes.Where(n => n.Id != center?.Id && (n.Kind == "ActorGroup" || Value(n, "contextEntityClass", "ComponentType") is "Performer" or "Person"
                || edges.Any(e => e.SourceId == n.Id && e.TargetId == center?.Id))).ToArray();
            var leftIds = left.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            var right = nodes.Where(n => n.Id != center?.Id && !leftIds.Contains(n.Id)).ToArray();
            if (center is not null) result.Add(center.Id, (410, 220 + Math.Max(0, Math.Max(left.Length, right.Length) - 1) * 90));
            foreach (var (node, index) in left.Select((node, index) => (node, index))) result.Add(node.Id, (30, 220 + index * 180));
            foreach (var (node, index) in right.Select((node, index) => (node, index))) result.Add(node.Id, (790, 220 + index * 180));
            return result;
        }
        if (view == "data-flow" && nodes.Select(n => Group(n, view)).Distinct().Count() == 1)
        {
            var depth = nodes.ToDictionary(n => n.Id, _ => 0);
            var incoming = nodes.ToDictionary(n => n.Id, n => edges.Count(e => e.TargetId == n.Id && e.SourceId != n.Id));
            var queue = new Queue<string>(incoming.Where(p => p.Value == 0).Select(p => p.Key).Order(StringComparer.Ordinal));
            while (queue.TryDequeue(out var id))
                foreach (var edge in edges.Where(e => e.SourceId == id && e.TargetId != id).OrderBy(e => e.Id, StringComparer.Ordinal))
                {
                    depth[edge.TargetId] = Math.Max(depth[edge.TargetId], depth[id] + 1);
                    if (--incoming[edge.TargetId] == 0) queue.Enqueue(edge.TargetId);
                }
            var rowIndex = new Dictionary<int, int>();
            foreach (var node in nodes.OrderBy(n => depth[n.Id]).ThenBy(n => n.Id, StringComparer.Ordinal))
            {
                var column = depth[node.Id] % 3;
                var rowNumber = rowIndex.GetValueOrDefault(column);
                result.Add(node.Id, (30 + column * 370, 220 + rowNumber * 180));
                rowIndex[column] = rowNumber + 1;
            }
            return result;
        }
        var row = 0;
        foreach (var group in nodes.GroupBy(n => Group(n, view)))
        {
            var ordered = view == "data-flow"
                ? group.OrderBy(n => edges.Count(e => e.TargetId == n.Id)).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray()
                : group.ToArray();
            for (var i = 0; i < ordered.Length; i++)
                result.Add(ordered[i].Id, (30 + i % 3 * 370, 220 + (row + i / 3) * 180));
            row += (ordered.Length + 2) / 3 + 1;
        }
        return result;
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Compact(string value) => value.Length > 48 ? value[..45] + "..." : value;
    private static DesignNode DeploymentNode(SystemDesignDiagramNode node) => new()
    {
        Id = node.Id, Kind = node.Kind, Label = node.Label, DiagramRole = node.DiagramRole, Properties = node.Properties ?? [],
        Source = node.Source, BoundaryDisposition = node.BoundaryDisposition, SacaZone = Value(node, "sacaZone"), SacaRole = Value(node, "sacaRole"),
        DeploymentScopeNodeId = Value(node, "deploymentScopeNodeId"), DeploymentOwner = Value(node, "deploymentOwner"),
        DeploymentEvidenceReference = Value(node, "deploymentEvidenceReference"), DeploymentSecurityFunctions = Value(node, "deploymentSecurityFunctions")
    };
    private static string? Value(SystemDesignDiagramNode node, params string[] keys) =>
        keys.Select(key => node.Properties?.GetValueOrDefault(key)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    private static string EdgeLabel(SystemDesignDiagramEdge edge, string view) => view == "logical"
        ? $"{edge.RelationshipType} · {edge.Purpose}"
        : view is "network" or "azure-deployment" && !edge.IsFlow ? $"{edge.RelationshipType} (not network traffic)"
        : !edge.IsFlow
        ? edge.RelationshipType == "Access" ? $"Recorded access · {edge.Purpose}" : $"{edge.RelationshipType} (association)"
        : view == "data-flow"
            ? $"{edge.InformationType ?? "Data not recorded"} · {edge.Classification ?? "Classification not recorded"} · {edge.Protection ?? "Protection not recorded"}"
                + (edge.LifecycleStage is not null ? $" · Lifecycle: {edge.LifecycleStage}" : "")
            : $"{edge.Protocol ?? "Protocol not recorded"} / {edge.Port ?? "Port not recorded"} · {edge.Service ?? "Service not recorded"} · {edge.Protection ?? "Protection not recorded"}"
                + (view == "network" ? $" · {edge.Classification ?? edge.InformationType ?? "Data not recorded"} · Stack: {edge.ProtocolStack ?? "Not recorded"} · Crossing: {edge.BoundaryCrossing}" : "");
    private static void Text(XElement parent, int x, int y, string value, int size = 14) =>
        parent.Add(new XElement(Svg + "text", new XAttribute("x", x), new XAttribute("y", y),
            new XAttribute("font-family", "sans-serif"), new XAttribute("font-size", size),
            new XAttribute("fill", "#0f172a"), value));
}
