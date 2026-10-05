using Ato.Copilot.Core.Dtos.SystemDesign;

namespace Ato.Copilot.Agents.Compliance.Services;

internal sealed record SystemContextView(IReadOnlyList<DesignNode> Nodes, IReadOnlyList<DesignEdge> Edges,
    IReadOnlyList<DesignNode> Constraints, int CollapsedCount, bool HasCenter);

internal static class SystemContextProjection
{
    internal static bool IsPerformer(DesignNode node) => node.Kind == "ActorGroup"
        || node.Properties.GetValueOrDefault("contextEntityClass") == "Performer"
        || node.Properties.GetValueOrDefault("ComponentType") == "Person"
        || node.Properties.GetValueOrDefault("componentType") == "Actor group";

    internal static bool IsConstraint(DesignNode node) => node.Kind is "PolicyReference" or "ContextConstraint"
        || node.Properties.GetValueOrDefault("ComponentType") == "Policy";

    internal static SystemContextView Project(IReadOnlyList<DesignNode> nodes, IReadOnlyList<DesignEdge> edges)
    {
        var architecture = nodes.Where(SystemDesignSemantics.IsArchitectureNode).ToArray();
        var centers = architecture.Where(n => n.Kind == "System").ToArray();
        if (centers.Length > 1) throw new ArgumentException("System context requires one recorded central system.");
        var constraints = nodes.Where(IsConstraint).ToArray();
        var ids = architecture.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var connections = edges.Where(e => ids.Contains(e.SourceNodeId) && ids.Contains(e.TargetNodeId)).ToArray();
        if (centers.Length == 0) return new(architecture, connections, constraints, 0, false);
        var center = centers[0];
        var members = connections.Where(e => e.RelationshipType == "Membership" && e.Source?.Type == "RecordedRelationship"
            && e.TargetNodeId == center.Id).Select(e => e.SourceNodeId).ToHashSet(StringComparer.Ordinal);
        var internalIds = architecture.Where(n => n.Id != center.Id && !IsPerformer(n)
            && n.Kind is not ("ProviderReference" or "Environment") && SystemBoundaryProjection.Disposition(n) != "OutOfBoundary"
            && (SystemBoundaryProjection.Disposition(n) == "InBoundary" || members.Contains(n.Id))).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var projected = connections.Select(e => e with
        {
            SourceNodeId = internalIds.Contains(e.SourceNodeId) ? center.Id : e.SourceNodeId,
            TargetNodeId = internalIds.Contains(e.TargetNodeId) ? center.Id : e.TargetNodeId
        }).Where(e => e.SourceNodeId != e.TargetNodeId).ToArray();
        return new(architecture.Where(n => !internalIds.Contains(n.Id)).ToArray(), projected, constraints, internalIds.Count, true);
    }
}
