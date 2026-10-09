using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class SystemDesignService
{
    private static bool SourceRecordKind(string kind) =>
        SystemDesignSemantics.IsSourceRecordKind(kind);

    private static DesignNode ClassifyNode(DesignNode node) =>
        node with { DiagramRole = SystemDesignSemantics.IsSourceRecordNode(node) ? "SourceRecord" : node.DiagramRole ?? "Architecture" };

    private static bool RecordedRelationshipType(string type) =>
        SystemDesignSemantics.IsRecordedRelationshipType(type);

    private static bool CanonicalNonFlow(DesignEdge edge, SystemDesignGraph canonical) =>
        RecordedRelationshipType(edge.RelationshipType) && canonical.Edges.Any(source =>
            source.Id == edge.Id && source.Source?.Type == "RecordedRelationship" && SameRecordedSemantics(source, edge));

    private DesignEdge RecordedRelationship(string type, DesignNode from, DesignNode to, string purpose, object? pins = null)
    {
        var id = Hash($"{type}:{from.Id}:{to.Id}");
        var source = from.Kind == "System" ? to.Source! : from.Source!;
        return new() { Id = $"recorded:{type}:{id}", RelationshipType = type,
            SourceNodeId = from.Id, TargetNodeId = to.Id, Purpose = purpose, Direction = "Outbound",
            Source = Source("RecordedRelationship", id,
                new { type, from = from.Source, to = to.Source, purpose, pins },
                source.ResolutionUrl, source.ReviewState, source.Precedence,
                $"Explicit recorded {type}; source {from.Id}; target {to.Id}; not network traffic or design approval"),
            Origin = "VerifiedCanonical", ReviewState = source.ReviewState, ProjectionStatus = "Recorded",
            BoundaryCrossing = "Unknown" };
    }

    private async Task ProjectRelationshipsAsync(AtoCopilotContext db, string systemId, List<DesignNode> nodes,
        List<DesignEdge> edges, Action<string, DesignNode> add, List<string> fingerprints, CancellationToken ct)
    {
        var system = nodes.Single(x => x.Id == $"system:{systemId}");
        foreach (var node in nodes.ToArray())
        {
            if (node.Kind == "ActorGroup" && node.Properties.GetValueOrDefault("AccessMethod") is { } method
                && !string.IsNullOrWhiteSpace(method))
                edges.Add(RecordedRelationship("Access", node, system, method));
            if (node.Kind is "Component" or "InventoryItem" || node.Source?.Type == "BoundaryComponentAssignment")
                edges.Add(RecordedRelationship("Membership", node, system, "Recorded system membership; boundary inclusion requires review"));
            if (node.Source?.Type is "SystemProviderScopeSelection" or "ProviderHostingAssignment"
                && node.Properties.GetValueOrDefault("assignmentSourceVersion") is not null)
                edges.Add(RecordedRelationship("UsesService", system, node, "Selected provider service; not accepted control inheritance"));
            if (node.Kind == "Environment")
            {
                edges.Add(RecordedRelationship("Attachment", system, node, "Recorded environment attachment; not collection access or coverage"));
                ProjectContainment(systemId, node, nodes, edges, add);
            }
        }
        var links = await db.Set<SystemEnvironmentHostingLinkRecord>().AsNoTracking()
            .Where(x => x.TenantId == TenantId && x.SystemId == systemId).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        if (links.Count > 1000) throw new ArgumentException("Hosting link source exceeds design budget.");
        fingerprints.Add(Json(links.Select(Scalars)));
        foreach (var environment in nodes.Where(x => x.Kind == "Environment").ToArray())
        {
            var attachmentId = Guid.Parse(environment.Source!.Id);
            var explicitLinks = links.Where(x => x.AttachmentId == attachmentId).ToArray();
            foreach (var link in explicitLinks.Where(x => x.State == "Linked"))
                AddHosting(link.AssignmentId.ToString(), Scalars(link));
            var legacyId = environment.Properties.GetValueOrDefault("hostingAssignmentId");
            if (legacyId is not null && !explicitLinks.Any(x => x.AssignmentId.ToString() == legacyId))
                AddHosting(legacyId, new { source = "RetainedLegacy", endpointSource = environment.Source });

            void AddHosting(string assignmentId, object pins)
            {
                var provider = nodes.SingleOrDefault(x => x.Kind == "ProviderReference"
                    && x.Properties.GetValueOrDefault("AssignmentId") == assignmentId);
                if (provider is null || provider.Properties.GetValueOrDefault("scopeSourceVersion") is null)
                {
                    var index = nodes.FindIndex(x => x.Id == environment.Id);
                    nodes[index] = nodes[index] with { Properties = new(nodes[index].Properties)
                        { ["hostingLinkGap"] = "Recorded hosting link has no authorized selected provider scope; reconcile its exact reference." } };
                    return;
                }
                edges.Add(RecordedRelationship("HostingAssociation", environment, provider,
                    "Explicit environment/provider scope association; not authorization or inheritance", pins));
            }
        }
    }

    private void ProjectContainment(string systemId, DesignNode environment, List<DesignNode> nodes,
        List<DesignEdge> edges, Action<string, DesignNode> add)
    {
        var json = environment.Properties.GetValueOrDefault("resourceIds");
        if (string.IsNullOrWhiteSpace(json)) return;
        var resourceIds = Read<string[]>(json);
        if (resourceIds.Length > 1000) throw new ArgumentException("Environment resource selection exceeds design budget.");
        if (environment.Properties.GetValueOrDefault("resourceScopeAuthority") != "Recorded") return;
        var subscription = environment.Properties.GetValueOrDefault("subscriptionId");
        var exclusionsJson = environment.Properties.GetValueOrDefault("exclusions");
        var exclusions = string.IsNullOrWhiteSpace(exclusionsJson) ? [] : Read<EnvironmentExcludedResource[]>(exclusionsJson);
        var permittedJson = environment.Properties.GetValueOrDefault("permittedResourceScopes");
        var permitted = permittedJson is null ? null : Read<string[]>(permittedJson);
        foreach (var resourceId in resourceIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Exact retained selections authorize documentation identity, never a subscription-wide lookup.
            if (string.IsNullOrWhiteSpace(resourceId) || !resourceId.StartsWith($"/subscriptions/{subscription}/", StringComparison.OrdinalIgnoreCase)
                || resourceId.Split('/').Length < 9 || permitted is not null && !permitted.Any(scope =>
                    resourceId.Equals(scope.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
                    || resourceId.StartsWith(scope.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)))
            {
                var index = nodes.FindIndex(x => x.Id == environment.Id);
                nodes[index] = nodes[index] with { Properties = new(nodes[index].Properties)
                    { ["scopeMappingGap"] = "A retained resource selection is outside the exact authorized environment scope or has no valid ARM identity." } };
                continue;
            }
            if (exclusions.Any(x => x.ResourceId.Equals(resourceId, StringComparison.OrdinalIgnoreCase))) continue;
            var components = nodes.Where(x => x.Kind == "Component" &&
                string.Equals(x.Properties.GetValueOrDefault("AzureResourceId"), resourceId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (components.Length > 0)
            {
                foreach (var component in components)
                    edges.Add(RecordedRelationship("Containment", component, environment, "Exact recorded resource selection", resourceId));
                continue;
            }
            var id = $"selected-resource:{Hash(resourceId.ToLowerInvariant())}";
            var resource = nodes.SingleOrDefault(x => x.Id == id);
            if (resource is null)
            {
                var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
                resource = new() { Id = id, Label = parts[^1], Kind = "AzureResource", ProjectionStatus = "RecordedScope",
                    Source = Source("EnvironmentResourceSelection", resourceId,
                        new { resourceId }, Link(systemId, "environment"), "Unreviewed", 7,
                        "Exact selected resource identity; no inferred discovery, connectivity or boundary approval"),
                    Environment = environment.Environment,
                    Properties = new() { ["resourceId"] = resourceId, ["subscriptionId"] = parts[1],
                        ["resourceGroup"] = parts[3], ["resourceType"] = $"{parts[5]}/{string.Join("/", parts.Where((_, index) => index >= 6 && index % 2 == 0))}" },
                    BoundaryDisposition = "Undetermined" };
                add("Environment", resource);
            }
            edges.Add(RecordedRelationship("Containment", resource, environment, "Exact recorded resource selection", resourceId));
        }
    }
}

internal static class SystemDesignSemantics
{
    internal static bool IsSourceRecordKind(string kind) =>
        kind is "ProfileSection" or "PpsEntry" or "InformationType" or "LeveragedAuthorization" or "MonitoringObservation"
            or "PolicyReference" or "ContextConstraint" or "BoundaryDefinition" or "AuthorizationScope" or "LogicalConstruct" or "DataFlowElement";

    internal static bool IsSourceRecordNode(DesignNode node) => IsSourceRecordKind(node.Kind)
        || node.Properties.GetValueOrDefault("ComponentType") == "Policy";

    internal static bool IsArchitectureNode(string kind, string? diagramRole, IReadOnlyDictionary<string, string?>? properties = null) =>
        !IsSourceRecordKind(kind) && diagramRole != "SourceRecord" && properties?.GetValueOrDefault("ComponentType") != "Policy";

    internal static bool IsArchitectureNode(DesignNode node) =>
        IsArchitectureNode(node.Kind, node.DiagramRole, node.Properties);

    internal static bool IsRecordedRelationshipType(string type) =>
        type is "Membership" or "UsesService" or "Attachment" or "Containment" or "HostingAssociation" or "Access" or "GovernanceAssignment" or "LogicalAssociation";

    // Presentation of retained graphs only. Mutation and approval additionally verify exact canonical semantics.
    internal static bool IsFlow(DesignEdge edge) =>
        edge.RelationshipType is "DataFlow" or "ServiceFlow" or "ResourceFlow" or "Interconnection" or "UserAuthored" or "NetworkConnection" or "Dependency"
        || edge.RelationshipType == "Access" && edge.Source?.Type != "RecordedRelationship";
}
