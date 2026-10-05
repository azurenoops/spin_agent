using System.Text.RegularExpressions;
using Ato.Copilot.Core.Dtos.SystemDesign;

namespace Ato.Copilot.Agents.Compliance.Services;

internal sealed record SystemAzureDeploymentView(IReadOnlyList<DesignNode> Nodes, IReadOnlyList<DesignEdge> Edges);

internal static class SystemAzureDeploymentProjection
{
    internal static readonly string[] Zones = ["OnPremisesDisn", "SecureCloudAccessBoundary", "AzureCloud", "Undetermined"];
    internal static readonly string[] Roles = ["BCAP", "VDSS", "VDMS", "TCCM", "CNAP", "Workload", "SharedService", "Undetermined"];
    internal static readonly string[] ReferenceRoles = ["BCAP", "VDSS", "VDMS", "TCCM", "CNAP"];

    internal static (string? Subscription, string? ResourceGroup) ArmScope(DesignNode node)
    {
        var match = Regex.Match(node.Properties.GetValueOrDefault("AzureResourceId") ?? node.Properties.GetValueOrDefault("resourceId") ?? "",
            @"^/subscriptions/([^/]+)/resourceGroups/([^/]+)/providers/[^/]+/", RegexOptions.IgnoreCase);
        return match.Success ? (match.Groups[1].Value, match.Groups[2].Value) : (null, null);
    }
    internal static bool IsSeed(DesignNode node) => node.Kind is "Environment" or "ProviderReference"
        || ArmScope(node).Subscription is not null || node.SacaZone is not null || node.SacaRole is not null || node.DeploymentScopeNodeId is not null
        || node.DeploymentOwner is not null || node.DeploymentEvidenceReference is not null || node.DeploymentSecurityFunctions is not null;
    internal static bool IsRelationship(DesignEdge edge, IReadOnlyList<DesignNode> nodes) =>
        SystemDesignSemantics.IsFlow(edge) || SystemNetworkProjection.IsAssociation(edge)
        || (edge.RelationshipType == "GovernanceInteraction" || edge.RelationshipType == "GovernanceAssignment" && edge.Source?.Type == "RecordedRelationship")
            && nodes.Any(n => n.SacaRole == "TCCM" && (n.Id == edge.SourceNodeId || n.Id == edge.TargetNodeId));

    internal static DesignNode? Scope(DesignNode node, IReadOnlyList<DesignNode> nodes, IReadOnlyList<DesignEdge> edges)
    {
        if (node.Kind == "Environment") return node;
        if (node.DeploymentScopeNodeId is not null) return nodes.SingleOrDefault(n => n.Id == node.DeploymentScopeNodeId && n.Kind == "Environment");
        var scopes = edges.Where(e => e.RelationshipType == "Containment" && e.Source?.Type == "RecordedRelationship"
            && e.SourceNodeId == node.Id).Select(e => nodes.SingleOrDefault(n => n.Id == e.TargetNodeId && n.Kind == "Environment"))
            .Where(n => n is not null).DistinctBy(n => n!.Id).ToArray();
        return scopes.Length == 1 ? scopes[0] : null;
    }
    internal static string Zone(DesignNode node) => node.SacaZone ?? (node.Kind == "Environment" || ArmScope(node).Subscription is not null ? "AzureCloud" : "Undetermined");
    internal static string ZoneLabel(string zone) => zone switch
    {
        "OnPremisesDisn" => "01 On-premises / DISN",
        "SecureCloudAccessBoundary" => "02 Secure cloud access boundary",
        "AzureCloud" => "03 Azure cloud (identity requires recorded scope)",
        _ => "04 Undetermined deployment context"
    };
    internal static string Group(DesignNode node, IReadOnlyList<DesignNode> nodes, IReadOnlyList<DesignEdge> edges)
    {
        if (node.SacaRole == "TCCM") return "05 TCCM business role (not an appliance)";
        var scope = Scope(node, nodes, edges);
        var arm = ArmScope(node);
        var cloud = scope?.Properties.GetValueOrDefault("cloud") ?? "Cloud not recorded";
        var subscription = scope?.Properties.GetValueOrDefault("subscriptionId") ?? arm.Subscription ?? "Subscription not recorded";
        return $"{ZoneLabel(Zone(node))} / {cloud} / {subscription} / {arm.ResourceGroup ?? "Resource group not recorded"} / " +
            $"{SystemBoundaryProjection.GroupName(node, nodes)}";
    }
    internal static SystemAzureDeploymentView Project(IReadOnlyList<DesignNode> nodes, IReadOnlyList<DesignEdge> edges)
    {
        var architecture = nodes.Where(SystemDesignSemantics.IsArchitectureNode).ToArray();
        var seeds = architecture.Where(IsSeed).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var relationships = edges.Where(e => IsRelationship(e, nodes)).ToArray();
        var connected = relationships.Where(e => seeds.Contains(e.SourceNodeId) || seeds.Contains(e.TargetNodeId))
            .SelectMany(e => new[] { e.SourceNodeId, e.TargetNodeId });
        var ids = seeds.Concat(connected).ToHashSet(StringComparer.Ordinal);
        var members = architecture.Where(n => ids.Contains(n.Id)).ToArray();
        var endpoints = members.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        return new(members, relationships.Where(e => endpoints.Contains(e.SourceNodeId) && endpoints.Contains(e.TargetNodeId)).ToArray());
    }
}
