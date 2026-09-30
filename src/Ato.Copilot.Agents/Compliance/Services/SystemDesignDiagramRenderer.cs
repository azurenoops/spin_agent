using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Ato.Copilot.Agents.Compliance.Services;

internal sealed record SystemDesignDiagramNode(string Id, string Label, string BoundaryDisposition, string Kind,
    string? Environment = null, string? NetworkZone = null, string? DiagramRole = null);
internal sealed record SystemDesignDiagramEdge(string Id, string SourceId, string TargetId, string Purpose,
    string? Protocol, string? Port, string? Protection, string Provenance = "User-authored design relationship",
    string RelationshipType = "DataFlow", bool IsFlow = true);
internal sealed record SystemDesignDiagramSource(string SystemId, string SystemName, string BaselineId, long Revision,
    DateTimeOffset? ApprovedAt, string ApprovedBy, string? HandlingMarking, string SourceHash,
    IReadOnlyList<SystemDesignDiagramNode> Nodes, IReadOnlyList<SystemDesignDiagramEdge> Edges,
    string ReviewState = "Approved");
internal sealed record SystemDesignDiagramArtifact(string Id, string View, string Title, string Description,
    string FileName, string MediaType, byte[] Content, string ContentHash);

/// <summary>Self-contained deterministic SVG recipes; no browser state or external resources are accepted.</summary>
internal static class SystemDesignDiagramRenderer
{
    internal const string RecipeVersion = "2";
    internal static readonly string[] Views = ["context", "boundary", "network", "data-flow"];
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    internal static SystemDesignDiagramArtifact Render(SystemDesignDiagramSource source, string view)
    {
        if (!Views.Contains(view)) throw new ArgumentException("Unsupported design diagram view.", nameof(view));
        var sourceIds = source.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        if (sourceIds.Count != source.Nodes.Count || source.Edges.Any(e => !sourceIds.Contains(e.SourceId) || !sourceIds.Contains(e.TargetId)))
            throw new InvalidOperationException("Diagram node identity or relationship endpoint is invalid.");
        var nodes = source.Nodes.Where(n => SystemDesignSemantics.IsArchitectureNode(n.Kind, n.DiagramRole))
            .OrderBy(n => Group(n, view), StringComparer.Ordinal).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray();
        var ids = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var edges = source.Edges.Where(e => ids.Contains(e.SourceId) && ids.Contains(e.TargetId)
            && (view != "data-flow" || e.IsFlow)).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        var positions = Layout(nodes, edges, view);
        var graphBottom = positions.Count == 0 ? 230 : positions.Values.Max(p => p.Y) + 110;
        var approved = source.ReviewState == "Approved";
        var description = approved
            ? $"{source.ReviewState} design {source.BaselineId}, revision {source.Revision}; " +
                $"baseline timestamp {source.ApprovedAt:O}; reviewer {source.ApprovedBy}; source SHA-256 {source.SourceHash}; SVG recipe {RecipeVersion}."
            : $"{source.ReviewState} working design {source.BaselineId}, revision {source.Revision}; " +
                $"source mode CurrentWorkingData; source SHA-256 {source.SourceHash}; SVG recipe {RecipeVersion}. No approval or authorization is implied.";
        var title = (approved ? "" : $"{source.ReviewState} — ") + $"{source.SystemName} — {view} diagram";
        var svg = new XElement(Svg + "svg", new XAttribute("width", 1140),
            new XAttribute("height", graphBottom + edges.Length * 52 + 110),
            new XAttribute("viewBox", $"0 0 1140 {graphBottom + edges.Length * 52 + 110}"),
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
        if (view == "data-flow" && edges.Length == 0)
            Text(svg, 24, 190, "Data flows: not recorded between architecture elements. Structural associations do not establish traffic.");
        else if (nodes.Length == 0)
            Text(svg, 24, 190, "Architecture elements: not recorded.");

        foreach (var group in nodes.GroupBy(n => Group(n, view)))
        {
            var points = group.Select(n => positions[n.Id]).ToArray();
            var x = points.Min(p => p.X) - 10;
            var y = points.Min(p => p.Y) - 23;
            var frame = new XElement(Svg + "rect", new XAttribute("x", x), new XAttribute("y", y),
                new XAttribute("width", points.Max(p => p.X) - x + 330),
                new XAttribute("height", points.Max(p => p.Y) - y + 88),
                new XAttribute("fill", "none"), new XAttribute("stroke", "#cbd5e1"));
            svg.Add(frame);
            Text(svg, x + 5, y + 16, group.Key, 12);
        }
        foreach (var edge in edges)
        {
            var start = positions[edge.SourceId];
            var end = positions[edge.TargetId];
            var x1 = start.X + 160; var y1 = start.Y + 36;
            var x2 = end.X + 160; var y2 = end.Y + 36;
            svg.Add(new XElement(Svg + "line", new XAttribute("data-edge-id", edge.Id),
                new XAttribute("data-relationship-type", edge.RelationshipType), new XAttribute("x1", x1), new XAttribute("y1", y1),
                new XAttribute("x2", x2), new XAttribute("y2", y2), new XAttribute("stroke", "#475569"),
                new XAttribute("stroke-dasharray", edge.Provenance.Contains("Canonical", StringComparison.OrdinalIgnoreCase) ? "none" : "5 3")));
            if (edge.SourceId != edge.TargetId)
            {
                var angle = Math.Atan2(y2 - y1, x2 - x1);
                var arrow = $"{F(x2)},{F(y2)} {F(x2 - 10 * Math.Cos(angle - .4))},{F(y2 - 10 * Math.Sin(angle - .4))} " +
                    $"{F(x2 - 10 * Math.Cos(angle + .4))},{F(y2 - 10 * Math.Sin(angle + .4))}";
                svg.Add(new XElement(Svg + "polygon", new XAttribute("points", arrow), new XAttribute("fill", "#475569")));
            }
        }
        foreach (var node in nodes)
        {
            var position = positions[node.Id];
            var group = new XElement(Svg + "g", new XAttribute("data-node-id", node.Id));
            group.Add(new XElement(Svg + "rect", new XAttribute("x", position.X), new XAttribute("y", position.Y),
                new XAttribute("width", 320), new XAttribute("height", 72), new XAttribute("rx", 8),
                new XAttribute("fill", "#f8fafc"), new XAttribute("stroke", "#334155"),
                new XAttribute("stroke-dasharray", node.BoundaryDisposition == "InBoundary" ? "none" :
                    node.BoundaryDisposition == "OutOfBoundary" ? "7 4" : "2 3")));
            Text(group, position.X + 8, position.Y + 23, node.Label, 13);
            Text(group, position.X + 8, position.Y + 45, $"{node.Kind} · {node.BoundaryDisposition}", 11);
            if (view == "network") Text(group, position.X + 8, position.Y + 63,
                $"{node.Environment ?? "Environment unknown"} / {node.NetworkZone ?? "Zone unknown"}", 10);
            svg.Add(group);
        }
        var rowY = graphBottom + 20;
        foreach (var edge in edges)
        {
            Text(svg, 24, rowY, $"{edge.SourceId} → {edge.TargetId}: {edge.RelationshipType}; {edge.Purpose}", 12);
            Text(svg, 24, rowY + 20, edge.IsFlow
                ? $"{edge.Protocol ?? "Protocol not recorded"} / {edge.Port ?? "Port not recorded"}; " +
                    $"{edge.Protection ?? "Protection not recorded"}; {edge.Provenance}"
                : $"Not a recorded data flow; {edge.Provenance}", 11);
            rowY += 52;
        }
        var bytes = Encoding.UTF8.GetBytes(svg.ToString(SaveOptions.DisableFormatting));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var id = new PackageUuidRegistry(source.SystemId).GetOrCreate("design-diagram",
            $"{source.BaselineId}:{source.Revision}:{view}:{RecipeVersion}").ToString();
        return new(id, view, title, description, $"system-design-{view}.svg", "image/svg+xml", bytes, hash);
    }

    private static string Group(SystemDesignDiagramNode node, string view) => view switch
    {
        "boundary" => node.BoundaryDisposition,
        "network" => $"{node.Environment ?? "Environment unknown"} / {node.NetworkZone ?? "Zone unknown"}",
        "context" => node.Kind == "System" ? "System" : node.Kind == "ActorGroup" ? "Actors" : "Dependencies",
        _ => "Directed data flows"
    };

    private static Dictionary<string, (int X, int Y)> Layout(SystemDesignDiagramNode[] nodes,
        IReadOnlyList<SystemDesignDiagramEdge> edges, string view)
    {
        var result = new Dictionary<string, (int X, int Y)>();
        if (view == "context")
        {
            foreach (var group in nodes.GroupBy(n => n.Kind == "ActorGroup" ? 0 : n.Kind == "System" ? 1 : 2))
                foreach (var (node, index) in group.Select((node, index) => (node, index)))
                    result.Add(node.Id, (30 + group.Key * 370, 220 + index * 110));
            return result;
        }
        if (view == "data-flow")
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
                result.Add(node.Id, (30 + column * 370, 220 + rowNumber * 110));
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
                result.Add(ordered[i].Id, (30 + i % 3 * 370, 220 + (row + i / 3) * 110));
            row += (ordered.Length + 2) / 3 + 1;
        }
        return result;
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static void Text(XElement parent, int x, int y, string value, int size = 14) =>
        parent.Add(new XElement(Svg + "text", new XAttribute("x", x), new XAttribute("y", y),
            new XAttribute("font-family", "sans-serif"), new XAttribute("font-size", size),
            new XAttribute("fill", "#0f172a"), value));
}
