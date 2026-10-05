using Ato.Copilot.Core.Dtos.SystemDesign;

namespace Ato.Copilot.Agents.Compliance.Services;

internal static class SystemLogicalProjection
{
    internal static readonly string[] Types = ["Performer", "Activity", "InformationData", "Rule", "Goal", "Capability", "Service", "Project"];
    internal static readonly string[] Predicates = ["Performs", "Provides", "Supports", "Governs", "Enables", "Produces", "Consumes", "Realizes"];

    internal static string? Type(string kind, IReadOnlyDictionary<string, string?>? properties = null, string? diagramRole = null) => kind switch
    {
        "LogicalConstruct" => properties?.GetValueOrDefault("logicalType"),
        "InformationType" => "InformationData",
        "PolicyReference" or "ContextConstraint" => "Rule",
        "Environment" or "BoundaryDefinition" or "AuthorizationScope" => "ScopeReference",
        "ProfileSection" or "PpsEntry" or "LeveragedAuthorization" or "MonitoringObservation" => null,
        "DataFlowElement" => null,
        _ when properties?.GetValueOrDefault("ComponentType") == "Policy" => "Rule",
        _ when diagramRole == "SourceRecord" => null,
        _ when kind == "ProviderReference" || properties?.GetValueOrDefault("ComponentType") == "Service" => "Service",
        _ => "Performer"
    };
    internal static string? Type(DesignNode node) => Type(node.Kind, node.Properties, node.DiagramRole);
    internal static string Group(string kind, IReadOnlyDictionary<string, string?>? properties, string? diagramRole = null) =>
        Type(kind, properties, diagramRole) is "Activity" or "ScopeReference" ? $"Supporting constructs · {Type(kind, properties, diagramRole)}"
            : $"Principal constructs · {Type(kind, properties, diagramRole)}";
    internal static bool ValidPredicate(string predicate, DesignNode from, DesignNode to)
    {
        var source = Type(from);
        var target = Type(to);
        if (from.Id == to.Id || source is null or "ScopeReference" || target is null or "ScopeReference") return false;
        return predicate switch
        {
            "Performs" => source == "Performer" && target == "Activity",
            "Provides" => source is "Performer" or "Service" && target is "Capability" or "Service",
            "Supports" => target is "Goal" or "Capability" or "Project",
            "Governs" => source == "Rule",
            "Enables" => source is "Capability" or "Service" && target is "Activity" or "Capability",
            "Produces" or "Consumes" => source is "Performer" or "Activity" or "Service" && target == "InformationData",
            "Realizes" => source is "Performer" or "Service" or "Project" or "InformationData"
                && target is "Goal" or "Capability" or "Activity" or "Service" or "InformationData",
            _ => false
        };
    }
}
