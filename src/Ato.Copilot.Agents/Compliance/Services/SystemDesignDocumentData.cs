using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>One normalized retained design contribution consumed by every SSP renderer.</summary>
internal sealed record SystemDesignDocumentData(ApprovedSystemDesign Approved,
    IReadOnlyDictionary<int, string> Sections, IReadOnlyList<SystemDesignDiagramArtifact> Artifacts)
{
    internal const string Namespace = "https://ato-copilot.io/ns/system-design";
    internal DocumentSourceReference Source => new("ApprovedSystemDesign", Approved.Graph.SystemId,
        Approved.Revision.ToString(CultureInfo.InvariantCulture), Approved.SnapshotHash);
    internal IReadOnlyList<DocumentSourceReference> ArtifactSources => Artifacts.Select(a =>
        new DocumentSourceReference("SystemDesignDiagram", a.Id, $"{Source.VersionId}:{SystemDesignDiagramRenderer.RecipeVersion}", a.ContentHash)).ToArray();

    internal static async Task<SystemDesignDocumentData?> LoadAsync(IServiceProvider services, string systemId, CancellationToken ct)
    {
        var db = services.GetRequiredService<AtoCopilotContext>();
        if (!await db.Set<SystemDesignWorkspace>().AnyAsync(x => x.SystemId == systemId, ct)) return null;
        var service = services.GetRequiredService<ISystemDesignService>();
        return await LoadAsync(service, systemId, ct);
    }

    internal static async Task<SystemDesignDocumentData?> LoadAsync(ISystemDesignService service, string systemId, CancellationToken ct)
    {
        var approved = await service.GetApprovedAsync(systemId, ct);
        if (approved == null)
        {
            var graph = await service.GetAsync(systemId, ct);
            if (graph.GovernanceStatus != "NotStarted")
                throw new InvalidOperationException("DESIGN_APPROVAL_REQUIRED: Review and approve the System design before final SSP generation.");
            return null;
        }
        if (approved.Graph.SystemId != systemId || approved.Graph.GovernanceStatus != "Approved")
            throw new InvalidOperationException("DESIGN_APPROVAL_UNVERIFIED: The retained design identity or approval state is invalid.");
        if (approved.SourcesStale)
            throw new InvalidOperationException("DESIGN_SOURCE_STALE: Canonical design sources changed. Reconcile and review before generating new output.");
        return FromApproved(approved);
    }

    internal static SystemDesignDocumentData FromApproved(ApprovedSystemDesign approved)
    {
        var graph = approved.Graph;
        var source = DiagramSource(graph, $"{graph.SystemId}/design/{approved.Revision}", approved.Revision,
            approved.SnapshotHash, approved.ApprovedAt, approved.ApprovedBy);
        var sections = new Dictionary<int, string>
        {
            [5] = Narrative(approved, "System context", graph.Nodes, graph.Edges),
            [6] = Narrative(approved, "Network architecture", graph.Nodes, graph.Edges),
            [7] = Narrative(approved, "Data-flow description and interconnections", [], graph.Edges, dataFlowsOnly: true),
            [11] = Narrative(approved, "Authorization boundary and system inventory", graph.Nodes, [])
        };
        return new(approved, sections, SystemDesignDiagramRenderer.Views.Select(view => SystemDesignDiagramRenderer.Render(source, view)).ToArray());
    }

    private static SystemDesignDiagramSource DiagramSource(SystemDesignGraph graph, string snapshotId, long revision,
        string hash, DateTimeOffset? approvedAt, string reviewer, string reviewState = "Approved") =>
        new(graph.SystemId, graph.SystemName, snapshotId, revision, approvedAt, reviewer,
            graph.Nodes.Where(n => n.Kind == "System").Select(n => Value(n, "HandlingMarking", "classification")).FirstOrDefault(v => v != null),
            hash,
            graph.Nodes.Select(n => new SystemDesignDiagramNode(n.Id, n.Label, n.BoundaryDisposition, n.Kind, n.Environment, n.NetworkZone, n.DiagramRole)).ToArray(),
            graph.Edges.Select(e => new SystemDesignDiagramEdge(e.Id, e.SourceNodeId, e.TargetNodeId,
                e.Purpose ?? "Purpose not recorded", e.Protocol, e.Port, e.Protection,
                $"{e.Origin}; {e.Source?.Provenance ?? "Explicit design decision"}" +
                    (SystemDesignSemantics.IsFlow(e) ? $"; PPS source: {e.PpsEntryId ?? "Not recorded"}" : ""),
                e.RelationshipType, SystemDesignSemantics.IsFlow(e))).ToArray(),
            reviewState);

    internal static string Narrative(ApprovedSystemDesign approved, string title, IEnumerable<DesignNode> nodes,
        IEnumerable<DesignEdge> edges, bool dataFlowsOnly = false)
    {
        var text = new StringBuilder();
        text.AppendLine($"### {title} — approved System design");
        text.AppendLine($"Design version: {approved.Revision}; reviewer: {approved.ApprovedBy}; approved: {approved.ApprovedAt:O}");
        text.AppendLine($"Retained source SHA-256: {approved.SnapshotHash}");
        AppendGraphText(text, nodes, edges, approved.Revision, dataFlowsOnly);
        if (!dataFlowsOnly) AppendComponentUse(text, approved.Graph);
        return text.ToString();
    }

    private static void AppendComponentUse(StringBuilder text, SystemDesignGraph graph)
    {
        foreach (var scope in graph.ComponentScopes.OrderBy(x => x.Source).ThenBy(x => x.ComponentId))
        {
            text.AppendLine();
            text.AppendLine($"Component service use: {scope.Name}; decision: {scope.Decision}; system area: {scope.BoundaryName ?? "Not recorded"}.");
            text.AppendLine($"Usage: {scope.Usage}");
            text.AppendLine($"Source: {scope.Source}/{scope.ComponentId}; version: {scope.SourceRevision}.");
            if (scope.WordingBasis is { } wording)
                text.AppendLine($"Wording: {wording.Origin}; user corrected: {wording.UserEdited}; responsibility proposal {wording.DraftId}, revision {wording.Revision}, source hash {wording.SourceHash}; sources: {string.Join(", ", wording.SourceIds)}.");
            text.AppendLine("Service use does not establish infrastructure containment, accepted inheritance, verified recovery or authorization.");
        }
    }

    private static void AppendGraphText(StringBuilder text, IEnumerable<DesignNode> nodes, IEnumerable<DesignEdge> edges,
        long revision, bool dataFlowsOnly = false)
    {
        foreach (var node in nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            text.AppendLine();
            text.AppendLine($"{node.Label} ({node.Kind}); boundary: {node.BoundaryDisposition}.");
            if (node.Environment != null || node.NetworkZone != null)
                text.AppendLine($"Environment: {node.Environment ?? "Not recorded"}; network zone: {node.NetworkZone ?? "Not recorded"}.");
            if (node.Provider != null) text.AppendLine($"Provider reference: {node.Provider}; association alone does not establish inherited responsibility.");
            foreach (var field in node.Properties.OrderBy(p => p.Key, StringComparer.Ordinal))
                if (!string.IsNullOrWhiteSpace(field.Value)) text.AppendLine($"{field.Key}: {field.Value}");
            if (node.Source != null)
                text.AppendLine($"Source: {node.Source.Type}/{node.Source.Id}, version {node.Source.Version}; {node.Source.Provenance}; source review: {node.Source.ReviewState}.");
        }
        var relationships = edges.Where(e => !dataFlowsOnly || SystemDesignSemantics.IsFlow(e))
            .OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
        if (dataFlowsOnly && relationships.Length == 0)
            text.AppendLine("Data flows: not recorded. Structural associations do not establish network traffic.");
        foreach (var edge in relationships)
        {
            text.AppendLine();
            text.AppendLine($"{edge.SourceNodeId} → {edge.TargetNodeId}: {edge.Purpose ?? "Purpose not recorded"}.");
            if (SystemDesignSemantics.IsFlow(edge))
            {
                text.AppendLine($"Origin: {edge.Origin}; PPS source: {edge.PpsEntryId ?? "Not recorded"}.");
                text.AppendLine($"Relationship: {edge.RelationshipType}; direction: {edge.Direction}; " +
                    $"information: {edge.InformationType ?? "Not recorded"}; classification: {edge.Classification ?? "Not recorded"}.");
                text.AppendLine($"PPS: {edge.Protocol ?? "Not recorded"}/{edge.Port ?? "Not recorded"}/{edge.Service ?? "Not recorded"}; " +
                    $"protection: {edge.Protection ?? "Not recorded"}; encryption: {edge.EncryptionState ?? "Not recorded"}.");
                text.AppendLine($"Boundary crossing: {edge.BoundaryCrossing}; interconnection: {edge.InterconnectionId ?? "Not recorded"}; agreement: {edge.AgreementStatus ?? "Not recorded"}.");
            }
            else
                text.AppendLine($"Origin: {edge.Origin}; relationship: {edge.RelationshipType}; not a recorded data flow.");
            text.AppendLine($"Provenance: {edge.Source?.Provenance ?? "User-authored design relationship"}; source version: {edge.Source?.Version ?? revision.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    internal string Markdown(int section)
    {
        if (!Sections.TryGetValue(section, out var text)) return "";
        var view = section switch { 5 => "context", 6 => "network", 7 => "data-flow", _ => "boundary" };
        var artifact = Artifacts.Single(a => a.View == view);
        return text + $"\n![{artifact.Title}](data:{artifact.MediaType};base64,{Convert.ToBase64String(artifact.Content)})\n" +
            $"\nDiagram SHA-256: {artifact.ContentHash}\n";
    }

    internal string Text => string.Join("\n", Sections.OrderBy(p => p.Key).Select(p => p.Value));

    internal static string? Value(DesignNode node, params string[] names) =>
        names.Select(name => node.Properties.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    internal void AppendOscal(Dictionary<string, object> characteristics, Dictionary<string, object> implementation,
        Dictionary<string, object> backMatter, bool includeArtifacts)
    {
        characteristics["description"] = Sections[5];
        foreach (var (key, section, view) in new[] { ("authorization-boundary", 11, "boundary"), ("network-architecture", 6, "network"), ("data-flow", 7, "data-flow") })
        {
            var contribution = new Dictionary<string, object> { ["description"] = Sections[section] };
            if (includeArtifacts)
                AppendDiagram(Approved.Graph.SystemId, contribution, Artifacts.Single(a => a.View == view));
            characteristics[key] = contribution;
        }
        AppendGraphStructures(Approved.Graph, characteristics, implementation, approved: true);
        if (includeArtifacts) AppendArtifacts(characteristics, backMatter, Artifacts, approved: true);
    }

    internal static void AppendWorkingOscal(SystemDesignGraph graph, Dictionary<string, object> characteristics,
        Dictionary<string, object> implementation, Dictionary<string, object> backMatter, bool includeArtifacts)
    {
        string Contribution(string title, IEnumerable<DesignNode> nodes, IEnumerable<DesignEdge> edges, bool dataFlowsOnly = false)
        {
            var text = new StringBuilder($"### Working {title}, revision {graph.Revision} ({graph.GovernanceStatus})\n");
            text.AppendLine("DRAFT / UNAPPROVED. Review-only current design; no approval or authorization is implied.");
            AppendGraphText(text, nodes, edges, graph.Revision, dataFlowsOnly);
            if (!dataFlowsOnly) AppendComponentUse(text, graph);
            return text.ToString();
        }
        characteristics["description"] = Contribution("system context", graph.Nodes, graph.Edges);
        characteristics["authorization-boundary"] = new Dictionary<string, object>
            { ["description"] = Contribution("authorization boundary", graph.Nodes, []) };
        characteristics["network-architecture"] = new Dictionary<string, object>
            { ["description"] = Contribution("network architecture", graph.Nodes, graph.Edges) };
        characteristics["data-flow"] = new Dictionary<string, object>
            { ["description"] = Contribution("data flows", [], graph.Edges, dataFlowsOnly: true) };
        AppendGraphStructures(graph, characteristics, implementation, approved: false);
        if (!includeArtifacts) return;
        // Projection timestamps and caller actions are not graph content or evidence of approval.
        var hash = SystemDesignService.Hash(JsonSerializer.Serialize(new
        {
            graph.SystemId, graph.SystemName, graph.Revision, graph.GovernanceStatus, graph.SourceFingerprint, graph.SourcesStale,
            Nodes = graph.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal).Select(n => n with
            {
                Properties = n.Properties.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value)
            }),
            Edges = graph.Edges.OrderBy(e => e.Id, StringComparer.Ordinal),
            Groups = graph.Groups.OrderBy(g => g.Id, StringComparer.Ordinal).Select(g => g with
            {
                NodeIds = g.NodeIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()
            })
        }));
        var source = DiagramSource(graph, $"{graph.SystemId}/design/working/{hash}", graph.Revision,
            hash, null, "Not applicable", $"DRAFT / UNAPPROVED ({graph.GovernanceStatus})");
        var artifacts = SystemDesignDiagramRenderer.Views.Select(view => SystemDesignDiagramRenderer.Render(source, view)).ToArray();
        foreach (var (key, view) in new[] { ("authorization-boundary", "boundary"), ("network-architecture", "network"), ("data-flow", "data-flow") })
            AppendDiagram(graph.SystemId, (Dictionary<string, object>)characteristics[key], artifacts.Single(a => a.View == view));
        AppendArtifacts(characteristics, backMatter, artifacts, approved: false);
    }

    private static void AppendDiagram(string systemId, Dictionary<string, object> contribution, SystemDesignDiagramArtifact image) =>
        contribution["diagrams"] = new[] { new Dictionary<string, object>
        {
            ["uuid"] = new PackageUuidRegistry(systemId).GetOrCreate("design-diagram-reference", image.Id).ToString(),
            ["description"] = WebUtility.HtmlEncode(image.Description), ["caption"] = WebUtility.HtmlEncode(image.Title),
            ["links"] = new[] { new Dictionary<string, string> { ["href"] = $"#{image.Id}", ["rel"] = "diagram" } }
        } };

    private static void AppendGraphStructures(SystemDesignGraph graph, Dictionary<string, object> characteristics,
        Dictionary<string, object> implementation, bool approved)
    {
        var ids = new PackageUuidRegistry(graph.SystemId);
        var components = graph.Nodes.Where(n => n.Kind is "Component" or "SystemComponent" or "InventoryItem"
            or "DesignComponent" or "Application" or "Service" or "AzureResource" or "ProviderReference" or "LeveragedAuthorization").OrderBy(n => n.Id).ToArray();
        var componentOutput = components.Select(n => new Dictionary<string, object>
        {
            ["uuid"] = ids.ComponentUuid(n.Source?.Id ?? n.Id).ToString(), ["type"] = (Value(n, "ComponentType", "Type") ?? n.Kind).ToLowerInvariant(),
            ["title"] = n.Label, ["description"] = NodeDescription(n),
            ["status"] = new Dictionary<string, string> { ["state"] = "other", ["remarks"] = "Operational status is not established by design membership." },
            ["props"] = NodeProperties(n)
        }).ToList();
        for (var index = 0; index < components.Length; index++)
        {
            var protocols = graph.Edges.Where(e => SystemDesignSemantics.IsFlow(e) && e.SourceNodeId == components[index].Id && e.Protocol != null)
                .Select(e => new Dictionary<string, object>
                {
                    ["uuid"] = ids.GetOrCreate("design-protocol", e.Id).ToString(),
                    ["name"] = e.Protocol!,
                    ["title"] = $"{e.Service ?? e.Protocol}: {e.Port ?? "Port not recorded"}"
                }).ToArray();
            if (protocols.Length > 0) componentOutput[index]["protocols"] = protocols;
        }
        implementation["components"] = componentOutput;
        var users = implementation.TryGetValue("users", out var currentUsers) &&
            currentUsers is IEnumerable<Dictionary<string, object>> userRecords ? userRecords.ToList() : [];
        users.AddRange(graph.Nodes.Where(n => n.Kind == "ActorGroup").Select(n => new Dictionary<string, object>
        {
            ["uuid"] = ids.GetOrCreate("design-actor", n.Source?.Id ?? n.Id).ToString(),
            ["title"] = n.Label, ["description"] = NodeDescription(n), ["props"] = NodeProperties(n)
        }));
        implementation["users"] = users;
        var inventory = components.Where(n => n.Kind == "InventoryItem").Select(n => new Dictionary<string, object>
        {
            ["uuid"] = ids.GetOrCreate("design-inventory", n.Source?.Id ?? n.Id).ToString(),
            ["description"] = NodeDescription(n), ["props"] = NodeProperties(n),
            ["implemented-components"] = new[] { new Dictionary<string, string> { ["component-uuid"] = ids.ComponentUuid(n.Source?.Id ?? n.Id).ToString() } }
        }).ToArray();
        if (inventory.Length > 0) implementation["inventory-items"] = inventory;
        else implementation.Remove("inventory-items");
        var information = graph.Nodes.Where(n => n.Kind == "InformationType").ToArray();
        if (information.Length > 0)
        {
            var retainedTypes = characteristics.TryGetValue("system-information", out var priorInformation) &&
                priorInformation is Dictionary<string, object> priorInfo && priorInfo.TryGetValue("information-types", out var priorTypes) &&
                priorTypes is IEnumerable<Dictionary<string, object>> typed ? typed.ToList() : [];
            characteristics["system-information"] = new Dictionary<string, object>
            {
                ["information-types"] = retainedTypes.Concat(information.Select(n => new Dictionary<string, object>
                {
                    ["uuid"] = ids.GetOrCreate("design-information", n.Source?.Id ?? n.Id).ToString(),
                    ["title"] = n.Label, ["description"] = NodeDescription(n), ["props"] = NodeProperties(n)
                })).ToArray()
            };
        }
        var props = implementation.TryGetValue("props", out var existing) && existing is List<Dictionary<string, string>> list ? list : [];
        foreach (var node in graph.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
            props.Add(new() { ["name"] = approved ? "approved-design-node" : "working-design-node",
                ["ns"] = Namespace, ["value"] = JsonSerializer.Serialize(node) });
        foreach (var edge in graph.Edges)
            props.Add(new() { ["name"] = approved ? "approved-design-relationship" : "working-design-relationship",
                ["ns"] = Namespace, ["value"] = JsonSerializer.Serialize(edge) });
        foreach (var scope in graph.ComponentScopes)
            props.Add(new() { ["name"] = approved ? "approved-component-service-use" : "working-component-service-use",
                ["ns"] = Namespace, ["value"] = JsonSerializer.Serialize(scope) });
        if (props.Count > 0) implementation["props"] = props;
    }

    private static void AppendArtifacts(Dictionary<string, object> characteristics, Dictionary<string, object> backMatter,
        IReadOnlyList<SystemDesignDiagramArtifact> artifacts, bool approved)
    {
        var resources = backMatter.TryGetValue("resources", out var value) && value is List<Dictionary<string, object>> prior ? prior : [];
        foreach (var artifact in artifacts)
            resources.Add(new()
            {
                ["uuid"] = artifact.Id, ["title"] = WebUtility.HtmlEncode(artifact.Title), ["description"] = WebUtility.HtmlEncode(artifact.Description),
                ["props"] = new[] { new Dictionary<string, string> { ["name"] = "sha-256", ["ns"] = Namespace, ["value"] = artifact.ContentHash } },
                ["base64"] = new Dictionary<string, string> { ["filename"] = artifact.FileName, ["media-type"] = artifact.MediaType, ["value"] = Convert.ToBase64String(artifact.Content) }
            });
        backMatter["resources"] = resources;
        characteristics["links"] = new[] { new Dictionary<string, string>
        {
            ["href"] = $"#{artifacts.Single(a => a.View == "context").Id}", ["rel"] = "diagram",
            ["text"] = approved ? "Approved system context diagram" : "DRAFT / UNAPPROVED system context diagram"
        } };
    }

    private static string NodeDescription(DesignNode node) => $"{node.Label}; boundary: {node.BoundaryDisposition}. " +
        string.Join("; ", node.Properties.Where(p => p.Value != null).OrderBy(p => p.Key).Select(p => $"{p.Key}: {p.Value}"));
    private static List<Dictionary<string, string>> NodeProperties(DesignNode node) =>
    [
        new() { ["name"] = "design-node-id", ["ns"] = Namespace, ["value"] = node.Id },
        new() { ["name"] = "boundary-disposition", ["ns"] = Namespace, ["value"] = node.BoundaryDisposition },
        new() { ["name"] = "source-reference", ["ns"] = Namespace, ["value"] = JsonSerializer.Serialize(node.Source) }
    ];
}
