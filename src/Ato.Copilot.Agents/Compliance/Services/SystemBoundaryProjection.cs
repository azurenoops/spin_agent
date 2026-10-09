using Ato.Copilot.Core.Dtos.SystemDesign;

namespace Ato.Copilot.Agents.Compliance.Services;

internal static class SystemBoundaryProjection
{
    internal static string Disposition(DesignNode node) => node.BoundaryRelationship is "SharedService" or "SeparatelyAuthorized"
        ? "OutOfBoundary" : node.BoundaryDisposition;

    internal static string GroupName(DesignNode node, IReadOnlyList<DesignNode> nodes)
    {
        if (SystemContextProjection.IsPerformer(node)) return "External actors / governance (not components)";
        if (node.Kind == "Environment") return "Hosting scope references (not authorized components)";
        var disposition = Disposition(node);
        if (disposition == "OutOfBoundary") return "Outside authorization boundary";
        if (disposition != "InBoundary") return "Boundary undetermined";
        var id = node.BoundaryDefinitionId ?? node.Properties.GetValueOrDefault("boundaryId");
        var definition = nodes.SingleOrDefault(n => n.Kind == "BoundaryDefinition" && n.Source?.Id == id);
        return definition is null ? "Authorization boundary · In boundary" : $"Authorization boundary · {definition.Label}";
    }
}
