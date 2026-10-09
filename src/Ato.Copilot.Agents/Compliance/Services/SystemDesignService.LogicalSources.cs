using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class SystemDesignService
{
    private async Task ProjectLogicalSourcesAsync(AtoCopilotContext db, string systemId, List<DesignNode> nodes,
        List<DesignEdge> edges, Action<string, DesignNode> add, List<string> fingerprints, CancellationToken ct)
    {
        var system = nodes.Single(n => n.Id == $"system:{systemId}");
        void AddLogical(string section, string id, string label, string type, DesignSource source,
            Dictionary<string, string?> properties, string relationship)
        {
            properties["logicalType"] = type;
            properties["logicalLayer"] = type is "Goal" or "Capability" ? "Capability" : type == "Activity" ? "Operational" : "System";
            var node = new DesignNode { Id = id, Kind = "LogicalConstruct", Label = label, Source = source,
                Properties = properties, ReviewState = source.ReviewState, ProjectionStatus = "Recorded" };
            add(section, node);
            edges.Add(RecordedRelationship("LogicalAssociation", system, node, relationship));
        }
        foreach (var profile in nodes.Where(n => n.Kind == "ProfileSection"
            && n.Properties.GetValueOrDefault("sectionType") == "MissionAndPurpose").ToArray())
        {
            foreach (var (field, type, label) in new[] { ("missionStatement", "Goal", "Recorded mission statement"),
                ("businessFunctions", "Activity", "Recorded business functions") })
            {
                if (profile.Properties.GetValueOrDefault(field) is not { } text || string.IsNullOrWhiteSpace(text)) continue;
                AddLogical("Mission", $"{profile.Id}:logical:{field}", label, type,
                    profile.Source! with { Version = Hash(Json(new { profile.Source, field, text })) },
                    new() { ["description"] = text, ["profileField"] = field },
                    $"Recorded {field} of system; prose is not decomposed or linked by inference");
            }
        }
        var links = await db.SystemCapabilityLinks.AsNoTracking().Where(l =>
            l.TenantId == TenantId && l.RegisteredSystemId == systemId).OrderBy(l => l.Id).ToListAsync(ct);
        var capabilityIds = links.Select(l => l.SecurityCapabilityId).ToArray();
        var capabilities = await db.SecurityCapabilities.AsNoTracking().Where(c =>
            c.TenantId == TenantId && capabilityIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        foreach (var link in links)
        {
            capabilities.TryGetValue(link.SecurityCapabilityId, out var capability);
            var properties = capability is null ? new Dictionary<string, string?> { ["sourceAvailability"] = "Unavailable" }
                : Fields(capability, "Description", "Provider", "Category", "Owner", "ImplementationStatus");
            properties["capabilityCategory"] = "Organization security measure; not inferred mission capability";
            properties["supportingProviderCapabilityIds"] = link.SupportingProviderCapabilityIdsJson;
            AddLogical("InventoryBoundary", $"logical-capability:{link.Id}", capability?.Name ?? "Linked security capability unavailable", "Capability",
                Source("SystemCapabilityLink", link.Id, new { link = Scalars(link), capability = capability is null ? null : Scalars(capability) },
                    $"/systems/{Uri.EscapeDataString(systemId)}/capabilities", "Recorded"), properties, "System-linked security measure; implementation not inferred");
        }
        var subscriptions = await db.CapabilitySubscriptions.AsNoTracking().Where(s =>
            s.RegisteredSystemId == systemId && s.IsActive).OrderBy(s => s.Id).ToListAsync(ct);
        foreach (var subscription in subscriptions)
        {
            var adoption = subscription.CurrentAdoptionSnapshotId is { } selected
                ? await db.Set<CapabilityAdoptionSnapshot>().AsNoTracking().SingleOrDefaultAsync(a =>
                    a.Id == selected && a.TenantId == TenantId && a.SystemId == systemId
                    && a.SubscriptionId == subscription.Id, ct) : null;
            if (adoption is not null && (!Guid.TryParse(subscription.CspInheritedCapabilityId, out var capabilityId) || adoption.CapabilityId != capabilityId))
                throw new InvalidOperationException("Selected CSP adoption capability does not match its system subscription.");
            var release = adoption is null ? null : await db.ProviderCapabilityReleases.AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == adoption.ReleaseId && r.CapabilityId == adoption.CapabilityId, ct);
            string? name = null;
            string? description = null;
            if (release is not null)
            {
                using var retained = JsonDocument.Parse(release.SnapshotJson);
                if (retained.RootElement.ValueKind == JsonValueKind.Object
                    && retained.RootElement.TryGetProperty("Capability", out var capability) && capability.ValueKind == JsonValueKind.Object)
                {
                    if (capability.TryGetProperty("Name", out var title) && title.ValueKind == JsonValueKind.String) name = title.GetString();
                    if (capability.TryGetProperty("Description", out var detail) && detail.ValueKind == JsonValueKind.String) description = detail.GetString();
                }
            }
            AddLogical("Environment", $"logical-subscription:{subscription.Id}",
                string.IsNullOrWhiteSpace(name) ? "Subscribed CSP capability · source details incomplete" : name, "Capability",
                Source("CapabilitySubscription", subscription.Id, new { subscription = Scalars(subscription),
                    adoption = adoption is null ? null : Scalars(adoption), release = release is null ? null : Scalars(release) },
                    $"/systems/{Uri.EscapeDataString(systemId)}/capabilities", "Recorded", 7, "Active system subscription; not accepted inheritance or implemented capability"),
                new() { ["capabilityCategory"] = "CSP capability reference; inheritance not established",
                    ["capabilityId"] = subscription.CspInheritedCapabilityId, ["selectedAdoptionId"] = adoption?.Id.ToString(),
                    ["selectedReleaseId"] = adoption?.ReleaseId.ToString(), ["retainedAdoptionHash"] = adoption?.SnapshotHash,
                    ["retainedReleaseHash"] = release?.SnapshotHash, ["description"] = description,
                    ["logicalSourceGap"] = adoption is null ? "No valid explicit adoption selected. Review the capability in its source workflow."
                        : string.IsNullOrWhiteSpace(name) ? "Selected release details are unavailable or incomplete. Review the exact retained source; no latest catalog release is substituted." : null },
                "Active selected-system CSP subscription; not inheritance acceptance");
        }
        var roadmaps = await db.ImplementationRoadmaps.AsNoTracking().Where(r =>
            r.TenantId == TenantId && r.SystemId == systemId).OrderBy(r => r.Id).ToListAsync(ct);
        foreach (var roadmap in roadmaps)
            AddLogical("Mission", $"logical-project:{roadmap.Id}", roadmap.Name, "Project",
                Source("ImplementationRoadmap", roadmap.Id, Scalars(roadmap), $"/systems/{Uri.EscapeDataString(systemId)}/roadmap", "Recorded"),
                new() { ["description"] = "Recorded compliance improvement roadmap; not an inferred system upgrade",
                    ["status"] = roadmap.Status.ToString(), ["version"] = roadmap.Version.ToString(), ["baselineLevel"] = roadmap.BaselineLevel },
                "System-specific compliance improvement project");
        fingerprints.Add(Json(nodes.Where(n => n.Kind == "LogicalConstruct").Select(n => n.Source)));
    }
}
