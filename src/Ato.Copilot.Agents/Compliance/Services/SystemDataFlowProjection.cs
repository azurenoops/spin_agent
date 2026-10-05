using Ato.Copilot.Core.Dtos.SystemDesign;

namespace Ato.Copilot.Agents.Compliance.Services;

internal sealed record SystemDataFlowView(IReadOnlyList<DesignNode> Nodes, IReadOnlyList<DesignEdge> Edges);

internal static class SystemDataFlowProjection
{
    internal static readonly string[] Roles = ["Function", "DataStore", "ExternalEntity", "Undetermined"];
    internal static readonly string[] LifecycleStages = ["Receive", "Process", "Store", "Distribute", "Destroy"];
    internal static bool CanAnnotate(DesignNode node) => SystemDesignSemantics.IsArchitectureNode(node)
        || node.Kind == "DataFlowElement" || node.Kind == "LogicalConstruct" && node.Properties.GetValueOrDefault("logicalType") == "Activity";
    internal static bool IsEndpoint(DesignNode node) => SystemDesignSemantics.IsArchitectureNode(node)
        || node.Kind == "DataFlowElement" || node.Kind == "LogicalConstruct" && node.Properties.GetValueOrDefault("logicalType") == "Activity"
            && node.DataFlowRole == "Function";
    internal static string Role(DesignNode node) => node.DataFlowRole
        ?? (node.Kind == "ExternalSystem" ? "ExternalEntity" : "Undetermined");

    internal static string Group(DesignNode node, IReadOnlyList<DesignNode> nodes) =>
        Role(node) == "ExternalEntity" ? "External producers / consumers"
            : $"{SystemBoundaryProjection.GroupName(node, nodes)} · functions / stores";

    internal static SystemDataFlowView Project(IReadOnlyList<DesignNode> nodes, IReadOnlyList<DesignEdge> edges)
    {
        var eligible = nodes.Where(IsEndpoint).ToArray();
        var ids = eligible.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var flows = edges.Where(e => SystemDesignSemantics.IsFlow(e) && ids.Contains(e.SourceNodeId) && ids.Contains(e.TargetNodeId)).ToArray();
        var connected = flows.SelectMany(e => new[] { e.SourceNodeId, e.TargetNodeId }).ToHashSet(StringComparer.Ordinal);
        return new(eligible.Where(n => connected.Contains(n.Id) || n.Kind == "System"
            || n.DataFlowRole is "Function" or "DataStore" or "ExternalEntity").ToArray(), flows);
    }
}
