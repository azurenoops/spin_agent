using Ato.Copilot.Core.Dtos.SystemDesign;

namespace Ato.Copilot.Agents.Compliance.Services;

internal sealed record SystemNetworkView(IReadOnlyList<DesignNode> Nodes, IReadOnlyList<DesignEdge> Edges);

internal static class SystemNetworkProjection
{
    internal static readonly string[] Roles = ["Server", "VirtualMachine", "Application", "Database", "NetworkSegment",
        "Router", "Switch", "Firewall", "LoadBalancer", "VpnGateway", "SecurityAppliance", "MonitoringTool", "ExternalSystem", "Undetermined"];
    internal static readonly string[] ImpactLevels = ["IL2", "IL3", "IL4", "IL5", "IL6"];
    internal static readonly string[] Media = ["Local", "Private", "Internet", "DISN", "Other"];
    internal static bool IsAssociation(DesignEdge edge) => edge.Source?.Type == "RecordedRelationship"
        && edge.RelationshipType is "Membership" or "UsesService" or "Attachment" or "Containment" or "HostingAssociation" or "Access";

    internal static bool IsComponent(DesignNode node) => SystemDesignSemantics.IsArchitectureNode(node)
        && node.Kind != "Environment" && !SystemContextProjection.IsPerformer(node);

    internal static string Group(DesignNode node, IReadOnlyList<DesignNode> nodes) =>
        SystemContextProjection.IsPerformer(node) ? "User endpoints · not computing assets" :
        $"{SystemBoundaryProjection.GroupName(node, nodes)} / {node.Environment ?? node.Properties.GetValueOrDefault("Environment") ?? "Environment not recorded"} / " +
        $"{node.NetworkZone ?? "Zone not recorded"} / {node.NetworkSegment ?? "Segment not recorded"}";

    internal static SystemNetworkView Project(IReadOnlyList<DesignNode> nodes, IReadOnlyList<DesignEdge> edges)
    {
        var relationships = edges.Where(e => SystemDesignSemantics.IsFlow(e) || IsAssociation(e)).ToArray();
        var participants = relationships.SelectMany(e => new[] { e.SourceNodeId, e.TargetNodeId }).ToHashSet(StringComparer.Ordinal);
        var members = nodes.Where(n => IsComponent(n) || SystemDesignSemantics.IsArchitectureNode(n)
            && SystemContextProjection.IsPerformer(n) && participants.Contains(n.Id)).ToArray();
        var ids = members.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        return new(members, relationships.Where(e => ids.Contains(e.SourceNodeId) && ids.Contains(e.TargetNodeId)).ToArray());
    }
}
