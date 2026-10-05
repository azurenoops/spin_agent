using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.SystemDesign;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.ProviderAuthorizations;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Agents.Compliance.Services;

public sealed partial class SystemDesignService
{
    private static string Link(string id, string tab) => $"/systems/{Uri.EscapeDataString(id)}/" + (tab switch
    {
        "mission" => "profile/MissionAndPurpose", "users" => "profile/UsersAndAccess",
        "environment" => "profile/EnvironmentAndDeployment", "data" => "profile/DataTypes",
        "inventory" => "boundaries", "pps" => "profile/PortsProtocolsAndServices",
        _ => "profile/SystemDesign"
    });
    private static Dictionary<string, string?> Scalars(object value) => value.GetType().GetProperties()
        .Where(p => p.CanRead && (p.PropertyType.IsValueType || p.PropertyType == typeof(string)))
        .OrderBy(p => p.Name).ToDictionary(p => p.Name, p => p.GetValue(value)?.ToString());
    private static Dictionary<string, string?> Fields(object value, params string[] names) => value.GetType().GetProperties()
        .Where(p => names.Contains(p.Name)).OrderBy(p => p.Name).ToDictionary(p => p.Name, p => p.GetValue(value)?.ToString());
    private DesignSource Source(string type, string id, object value, string url,
        string review = "Unreviewed", int precedence = 7, string provenance = "Canonical record") =>
        new(type, id, Hash(Json(value)), provenance, review, precedence, url) { SourceTenantId = TenantId };
    private static readonly string[] Sections = ["Mission", "Users", "Environment", "Data", "InventoryBoundary", "PPSinterconnections"];
    private static Dictionary<string, string?> ProfileFields(ProfileSectionType type, string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        JsonElement value;
        try { value = Read<JsonElement>(json); }
        catch (JsonException)
        {
            return new() { ["legacySourceText"] = json, ["unmappedFields"] = "Legacy unstructured profile text requires explicit source mapping." };
        }
        if (value.ValueKind != JsonValueKind.Object)
            return new() { ["legacySourceText"] = json, ["unmappedFields"] = "Legacy non-object profile content requires explicit source mapping." };
        string[] fields = type switch
        {
            ProfileSectionType.MissionAndPurpose => ["systemVersion", "responsibleOrganization", "programOffice",
                "missionStatement", "businessPurpose", "operationalJustification", "businessFunctions"],
            ProfileSectionType.UsersAndAccess => ["accessOverview", "authenticationMethod"],
            ProfileSectionType.EnvironmentAndDeployment => ["hostingModel", "cloudProvider", "networkZones", "geographicLocations",
                "availabilityTier", "disasterRecoveryPosture", "rtoRpo", "maintenanceWindows", "operatingSystem", "additionalDetails"],
            ProfileSectionType.DataTypes => ["dataOverview", "highestSensitivityLevel"],
            ProfileSectionType.PortsProtocolsAndServices => ["ppsOverview"],
            _ => ["leveragedAuthOverview"]
        };
        var result = value.EnumerateObject().Where(x => fields.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).ToDictionary(x => x.Name,
            x => x.Value.ValueKind == JsonValueKind.Null ? null : x.Value.ToString());
        var unmapped = value.EnumerateObject().Where(x => !fields.Contains(x.Name, StringComparer.OrdinalIgnoreCase)).Select(x => x.Name).ToArray();
        if (unmapped.Length > 0) result["unmappedFields"] = Json(unmapped);
        return result;
    }

    private async Task<SystemDesignGraph> ProjectAsync(AtoCopilotContext db, RegisteredSystem system, CancellationToken ct)
    {
        var nodes = new List<DesignNode>();
        var edges = new List<DesignEdge>();
        var contributions = new Dictionary<string, List<DesignNode>>(Sections.ToDictionary(x => x, _ => new List<DesignNode>()));
        var fingerprints = new List<string>();
        void Add(string section, DesignNode node)
        {
            node = ClassifyNode(node);
            nodes.Add(node);
            contributions[section].Add(node);
        }
        var systemProperties = Fields(system, "Name", "Acronym", "Description", "SystemType", "OperationalStatus", "MissionCriticality",
            "ImpactLevel", "Categorization", "ResponsibleOrganization", "SystemOwner", "EmassId", "DitprId");
        Add("Mission", new() { Id = $"system:{system.Id}", Label = system.Name, Kind = "System",
            Source = Source("RegisteredSystem", system.Id, systemProperties, Link(system.Id, "mission")),
            Properties = systemProperties, ProjectionStatus = "Canonical" });
        var profiles = await db.SystemProfileSections.AsNoTracking()
            .Where(x => x.TenantId == TenantId && x.RegisteredSystemId == system.Id).OrderBy(x => x.SectionType).ToListAsync(ct);
        var profileGaps = new List<string>();
        var approved = await ApprovedProfileDocumentData.LoadAsync(db, system.Id, profileGaps, ct);
        foreach (var profile in profiles)
        {
            fingerprints.Add(Json(new { profile.Id, profile.DraftContent, profile.ApprovedSnapshotId, profile.GovernanceStatus }));
            var retained = approved.SingleOrDefault(x => x.SectionId == profile.Id);
            var section = profile.SectionType switch
            {
                ProfileSectionType.MissionAndPurpose => "Mission", ProfileSectionType.UsersAndAccess => "Users",
                ProfileSectionType.EnvironmentAndDeployment or ProfileSectionType.LeveragedAuthorizations => "Environment",
                ProfileSectionType.DataTypes => "Data", _ => "PPSinterconnections"
            };
            var tab = section switch { "Mission" => "mission", "Users" => "users", "Environment" => "environment", "Data" => "data", _ => "pps" };
            var scalar = retained?.Content.GetProperty("scalarContent").GetString() ?? (retained is null ? profile.DraftContent : null);
            var source = new DesignSource("SystemProfileSection", profile.Id,
                retained?.Hash ?? Hash(profile.DraftContent ?? ""), retained is null ? "Working profile; not approved" : $"Retained profile approval {retained.ApprovalId}",
                retained is null ? "Draft" : "Approved", retained is null ? 7 : 3, Link(system.Id, tab)) { SourceTenantId = TenantId };
            var profileProperties = ProfileFields(profile.SectionType, scalar);
            profileProperties["sectionType"] = profile.SectionType.ToString();
            profileProperties["approvalId"] = retained?.ApprovalId;
            profileProperties["reviewedBy"] = retained?.ReviewedBy;
            profileProperties["reviewedAt"] = retained?.ReviewedAt.ToString("O");
            Add(section, new() { Id = $"profile:{profile.Id}", Label = profile.SectionType.ToString(), Kind = "ProfileSection",
                Source = source, Properties = profileProperties,
                ReviewState = source.ReviewState, ProjectionStatus = retained is null ? "Working" : "ApprovedSnapshot" });
            await ProjectChildrenAsync(db, system.Id, profile, retained?.Content, source, section, Add, fingerprints, ct);
        }
        await ProjectInventoryAsync(db, system.Id, Add, fingerprints, ct);
        await ProjectEnvironmentAsync(db, system.Id, Add, fingerprints, ct);
        await ProjectConnectionsAsync(db, system.Id, Add, edges, ct);
        await ProjectRelationshipsAsync(db, system.Id, nodes, edges, Add, fingerprints, ct);
        await ProjectGovernanceAsync(db, system.Id, nodes, edges, Add, fingerprints, ct);
        await ProjectBoundarySourcesAsync(db, system.Id, Add, fingerprints, ct);
        await ProjectLogicalSourcesAsync(db, system.Id, nodes, edges, Add, fingerprints, ct);
        fingerprints.Add(Json(await ObservationsAsync(db, system.Id, ct)));
        var groups = ProjectedGroups(nodes);
        if (nodes.Count > 1000 || edges.Count > 3000 || groups.Count > 200)
            throw new ArgumentException("Canonical graph exceeds 1000 nodes / 3000 edges / 200 groups. Narrow the recorded design scope.");
        var summaries = Sections.Select(section => new DesignContribution(section, contributions[section].Count,
            contributions[section].Count == 0 ? "Missing" :
                contributions[section].Any(x => x.Source?.ReviewState == "Draft") ? "Unapproved" : "Available",
            contributions[section].Count == 0 ? "No canonical contribution recorded." :
                "Canonical references only; associations do not establish approved design, boundary inclusion, inheritance or monitoring health.",
            Link(system.Id, section switch { "Mission" => "mission", "Users" => "users", "Environment" => "environment", "Data" => "data", "InventoryBoundary" => "inventory", _ => "pps" }))).ToArray();
        if (profileGaps.Count > 0)
            summaries = summaries.Select(summary =>
            {
                var matching = profiles.Where(p => contributions[summary.Section].Any(n => n.Source?.Id == p.Id))
                    .SelectMany(p => profileGaps.Where(g => g.StartsWith($"Profile {p.SectionType}:"))).ToArray();
                return matching.Length == 0 ? summary : summary with { State = "Unapproved", Explanation = string.Join(" ", matching) };
            }).ToArray();
        return new() { TenantId = TenantId, SystemId = system.Id, SystemName = system.Name,
            Nodes = nodes.OrderBy(x => x.Id).ToArray(), Edges = edges.OrderBy(x => x.Id).ToArray(), Groups = groups,
            SourceFingerprint = Hash(Json(new { nodes = nodes.OrderBy(x => x.Id), edges = edges.OrderBy(x => x.Id),
                fingerprints = fingerprints.OrderBy(x => x) })), Contributions = summaries, SynchronizedAt = DateTimeOffset.UtcNow,
            DiscoveryState = "RetainedOnly", MonitoringState = "NotEvaluated" };
    }

    private static IReadOnlyList<DesignGroup> ProjectedGroups(IReadOnlyList<DesignNode> nodes)
    {
        var memberships = new List<(string Kind, string Label, string NodeId)>();
        foreach (var node in nodes)
        {
            if (node.Kind is "Component" or "InventoryItem" or "ExternalSystem" or "DesignComponent" or "AzureResource")
                memberships.Add(("BoundaryDisposition", node.BoundaryDisposition, node.Id));
            if (!string.IsNullOrWhiteSpace(node.Environment)) memberships.Add(("Environment", node.Environment, node.Id));
            if (!string.IsNullOrWhiteSpace(node.NetworkZone)) memberships.Add(("NetworkZone", node.NetworkZone, node.Id));
            var resource = node.Properties.GetValueOrDefault("AzureResourceId") ?? node.Properties.GetValueOrDefault("resourceId");
            if (!string.IsNullOrWhiteSpace(resource))
            {
                var parts = resource.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0].Equals("subscriptions", StringComparison.OrdinalIgnoreCase))
                {
                    memberships.Add(("Subscription", parts[1], node.Id));
                    if (parts.Length >= 4 && parts[2].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
                        memberships.Add(("ResourceGroup", $"{parts[1]}/{parts[3]}", node.Id));
                }
            }
        }
        return memberships.GroupBy(x => (x.Kind, x.Label)).OrderBy(x => x.Key.Kind).ThenBy(x => x.Key.Label)
            .Select(x => new DesignGroup($"projected:{Hash($"{x.Key.Kind}:{x.Key.Label}")}", x.Key.Label, x.Key.Kind,
                x.Select(n => n.NodeId).Distinct().OrderBy(id => id).ToArray())).ToArray();
    }

    private static IReadOnlyList<DesignGroup> MergeProjectedGroups(IReadOnlyList<DesignNode> nodes, IReadOnlyList<DesignGroup> groups)
    {
        if (nodes is null || groups is null || nodes.Any(x => x is null || x.Properties is null)
            || groups.Any(x => x is null || x.Id is null))
            throw new ArgumentException("Design nodes and groups must contain valid records.");
        return groups.Where(x => !x.Id.StartsWith("projected:", StringComparison.Ordinal)).Concat(ProjectedGroups(nodes)).ToArray();
    }

    private async Task ProjectChildrenAsync(AtoCopilotContext db, string systemId, SystemProfileSection profile,
        JsonElement? retained, DesignSource parent, string section, Action<string, DesignNode> add, List<string> fingerprints, CancellationToken ct)
    {
        var users = await db.UserCategories.AsNoTracking().Where(x => x.TenantId == TenantId && x.SystemProfileSectionId == profile.Id).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        var data = await db.DataTypeEntries.AsNoTracking().Where(x => x.TenantId == TenantId && x.SystemProfileSectionId == profile.Id).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        var ports = await db.PpsEntries.AsNoTracking().Where(x => x.TenantId == TenantId && x.SystemProfileSectionId == profile.Id).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        var authorizations = await db.LeveragedAuthorizations.AsNoTracking().Where(x => x.TenantId == TenantId && x.SystemProfileSectionId == profile.Id).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        if (users.Count + data.Count + ports.Count + authorizations.Count > 1000)
            throw new ArgumentException("Profile source exceeds the design record budget.");
        fingerprints.Add(Json(new { users = users.Select(Scalars), data = data.Select(Scalars), ports = ports.Select(Scalars), authorizations = authorizations.Select(Scalars) }));
        void Rows(string collection, string kind, string label, IEnumerable<object> working)
        {
            var rows = retained.HasValue && retained.Value.TryGetProperty(collection, out var values)
                ? values.EnumerateArray().ToArray()
                : retained.HasValue ? [] : working.Select(x => JsonSerializer.SerializeToElement(Scalars(x), JsonOptions)).ToArray();
            foreach (var value in rows)
            {
                var properties = value.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.ValueKind == JsonValueKind.Null ? null : x.Value.ToString());
                var id = properties.GetValueOrDefault("Id") ?? throw new InvalidOperationException("Canonical profile row lacks identity.");
                if (properties.GetValueOrDefault("PendingDeletion") is "True" or "true") continue;
                string[] allowed = kind switch
                {
                    "ActorGroup" => ["CategoryName", "Description", "ApproximateCount", "AccessMethod", "DataSensitivityLevel"],
                    "InformationType" => ["DataTypeName", "Description", "SensitivityClassification", "Source", "Destination", "ApplicableRegulations"],
                    "PpsEntry" => ["PortOrRange", "Protocol", "ServiceName", "Direction", "Justification"],
                    _ => ["ProviderName", "AuthorizationType", "AuthorizationDate", "CoveredControlFamilies"]
                };
                properties = properties.Where(x => allowed.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value);
                add(section, new() { Id = $"{kind}:{id}", Label = properties.GetValueOrDefault(label) ?? kind, Kind = kind,
                    Source = parent with { Type = kind, Id = id, Version = Hash(Json(new { parent.Version, row = value })) },
                    Properties = properties, ReviewState = parent.ReviewState,
                    ProjectionStatus = retained.HasValue ? "ApprovedSnapshot" : "Working" });
            }
        }
        Rows("userCategories", "ActorGroup", "CategoryName", users);
        Rows("dataTypes", "InformationType", "DataTypeName", data);
        Rows("portsProtocolsServices", "PpsEntry", "ServiceName", ports);
        Rows("leveragedAuthorizationReferences", "LeveragedAuthorization", "ProviderName", authorizations);
    }

    private async Task ProjectInventoryAsync(AtoCopilotContext db, string id, Action<string, DesignNode> add,
        List<string> fingerprints, CancellationToken ct)
    {
        var assignments = await db.BoundaryComponentAssignments.AsNoTracking().Where(x => x.TenantId == TenantId &&
            db.AuthorizationBoundaryDefinitions.Any(b => b.TenantId == TenantId && b.Id == x.AuthorizationBoundaryDefinitionId && b.RegisteredSystemId == id))
            .OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        var definitions = await db.AuthorizationBoundaryDefinitions.AsNoTracking()
            .Where(b => b.TenantId == TenantId && b.RegisteredSystemId == id).ToDictionaryAsync(b => b.Id, ct);
        var components = await db.SystemComponents.AsNoTracking().Where(x => x.TenantId == TenantId &&
            (x.RegisteredSystemId == id || db.ComponentSystemAssignments.Any(a => a.TenantId == TenantId &&
                a.SystemComponentId == x.Id && a.RegisteredSystemId == id) || assignments.Select(a => a.SystemComponentId).Contains(x.Id)
                || x.ComponentType == ComponentType.Policy && db.ComponentCapabilityLinks.Any(link => link.TenantId == TenantId
                    && link.SystemComponentId == x.Id && db.SystemCapabilityLinks.Any(scope => scope.TenantId == TenantId
                        && scope.RegisteredSystemId == id && scope.SecurityCapabilityId == link.SecurityCapabilityId))))
            .OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        var inventory = await db.InventoryItems.AsNoTracking().Where(x => x.TenantId == TenantId && x.RegisteredSystemId == id)
            .OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        if (assignments.Count > 1000 || components.Count + inventory.Count > 1000) throw new ArgumentException("Inventory source exceeds design budget.");
        fingerprints.Add(Json(assignments.Select(Scalars)));
        var systemAssignments = await db.ComponentSystemAssignments.AsNoTracking()
            .Where(x => x.TenantId == TenantId && x.RegisteredSystemId == id).OrderBy(x => x.Id).ToListAsync(ct);
        fingerprints.Add(Json(systemAssignments.Select(Scalars)));
        foreach (var component in components)
        {
            if (component.ComponentType == ComponentType.Policy)
            {
                ProjectPolicyReference(id, component, systemAssignments, add);
                continue;
            }
            var scope = assignments.Where(x => x.SystemComponentId == component.Id).ToArray();
            var properties = Fields(component, "Name", "ComponentType", "SubType", "Description", "Owner", "Status",
                "AzureResourceId", "AzureResourceType", "AzureResourceGroup", "AzureLocation", "PersonName", "RmfRoleName");
            properties["boundaryAssignments"] = Json(scope.Select(x => new { x.Id, x.AuthorizationBoundaryDefinitionId,
                boundaryName = definitions[x.AuthorizationBoundaryDefinitionId].Name,
                boundaryType = definitions[x.AuthorizationBoundaryDefinitionId].BoundaryType.ToString(),
                sourceVersion = Hash(Json(new { assignment = Scalars(x), boundary = Scalars(definitions[x.AuthorizationBoundaryDefinitionId]) })),
                x.IsInScope, x.ExclusionRationale }));
            properties["boundaryId"] = scope.Length == 1 ? scope[0].AuthorizationBoundaryDefinitionId : null;
            properties["recordedBoundaryName"] = scope.Length == 1 ? definitions[scope[0].AuthorizationBoundaryDefinitionId].Name : null;
            properties["recordedBoundaryDisposition"] = scope.Length == 0 || scope.Select(x => x.IsInScope).Distinct().Count() != 1
                ? "Undetermined" : scope[0].IsInScope ? "InBoundary" : "OutOfBoundary";
            properties["boundarySourceReview"] = "Unreviewed: the canonical boundary record has no retained approval state";
            properties["membershipSourceVersion"] = Hash(Json(new { component = Scalars(component),
                assignments = systemAssignments.Where(x => x.SystemComponentId == component.Id).Select(Scalars) }));
            add("InventoryBoundary", new() { Id = $"component:{component.Id}", Label = component.Name, Kind = "Component",
                Source = Source("SystemComponent", component.Id, properties, Link(id, "inventory")), Properties = properties,
                BoundaryDisposition = "Undetermined", ProjectionStatus = "Canonical" });
        }
        foreach (var item in inventory)
            add("InventoryBoundary", new() { Id = $"inventory:{item.Id}", Label = item.ItemName, Kind = "InventoryItem",
                Source = Source("InventoryItem", item.Id, Scalars(item), Link(id, "inventory")),
                Properties = Fields(item, "ItemName", "Type", "HardwareFunction", "SoftwareFunction", "Manufacturer", "Model",
                    "Version", "OperatingSystem", "IpAddress", "Location", "Environment", "Owner", "Status"), ProjectionStatus = "Canonical" });
        foreach (var assignment in assignments.Where(x => x.CspInheritedComponentId.HasValue))
        {
            var component = await db.CspInheritedComponents.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == assignment.CspInheritedComponentId, ct);
            add("InventoryBoundary", new() { Id = $"boundary:{assignment.Id}", Label = component?.Name ?? assignment.InheritanceProvider ?? "Provider component reference",
                Kind = "ProviderReference", Source = Source("BoundaryComponentAssignment", assignment.Id,
                    new { assignment = Scalars(assignment), boundary = Scalars(definitions[assignment.AuthorizationBoundaryDefinitionId]),
                        component = component is null ? null : Scalars(component) }, Link(id, "inventory")),
                BoundaryDisposition = "Undetermined",
                Provider = assignment.InheritanceProvider,
                Properties = new() { ["boundaryId"] = assignment.AuthorizationBoundaryDefinitionId,
                    ["recordedBoundaryName"] = definitions[assignment.AuthorizationBoundaryDefinitionId].Name,
                    ["ComponentType"] = component?.ComponentType.ToString(),
                    ["providerComponentId"] = assignment.CspInheritedComponentId?.ToString(),
                    ["recordedBoundaryDisposition"] = assignment.IsInScope ? "InBoundary" : "OutOfBoundary",
                    ["boundarySourceReview"] = "Unreviewed: the canonical boundary record has no retained approval state",
                    ["exclusionRationale"] = assignment.ExclusionRationale,
                    ["componentSourceVersion"] = component is null ? null : Hash(Json(Scalars(component))),
                    ["sourceAvailability"] = component is null ? "Provider component unavailable" : null }, ProjectionStatus = "Canonical" });
        }
    }

    private async Task ProjectEnvironmentAsync(AtoCopilotContext db, string id, Action<string, DesignNode> add,
        List<string> fingerprints, CancellationToken ct)
    {
        var attachments = await db.Set<SystemEnvironmentAttachmentRecord>().AsNoTracking()
            .Where(x => x.TenantId == TenantId && x.SystemId == id).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        if (attachments.Count > 1000) throw new ArgumentException("Environment source exceeds design budget.");
        fingerprints.Add(Json(attachments.Select(Scalars)));
        foreach (var row in attachments.Where(x => x.State == "Attached"))
        {
            var allocation = row.Source == "ProviderAllocation"
                ? await db.Set<ProviderEnvironmentAllocationRecord>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.Id == row.AllocationId && x.ConsumerTenantId == TenantId && x.RegistrationId == row.RegistrationId, ct)
                : null;
            var ownerTenant = row.Source == "OrganizationOwned" ? TenantId : allocation?.RegistrationOwnerTenantId;
            var currentRegistration = ownerTenant.HasValue
                ? await db.AzureSubscriptionRegistrations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
                    x.Id == row.RegistrationId && x.TenantId == ownerTenant.Value, ct)
                : null;
            var allocationOffering = allocation is null ? null : await db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == allocation.OfferingId && x.ProviderId == allocation.ProviderId, ct);
            var registration = Read<System.Text.Json.JsonElement>(row.RegistrationSnapshotJson);
            var name = registration.TryGetProperty("displayName", out var displayName) ? displayName.GetString() : null;
            var scope = Read<JsonElement>(row.ScopeJson);
            static string? Value(JsonElement json, string key) => json.TryGetProperty(key, out var value) ? value.ToString() : null;
            var properties = new Dictionary<string, string?>
            {
                ["registrationId"] = row.RegistrationId.ToString(), ["subscriptionId"] = Value(registration, "subscriptionId"),
                ["directoryTenantId"] = Value(registration, "directoryTenantId"), ["cloud"] = Value(registration, "cloud"),
                ["attachmentState"] = row.State, ["allocationId"] = row.AllocationId?.ToString(),
                ["hostingAssignmentId"] = row.HostingAssignmentId?.ToString(),
                ["appliedAllocationVersion"] = row.AppliedAllocationVersion?.ToString(), ["source"] = row.Source,
                ["scopeRevisionId"] = Value(scope, "revisionId"), ["scopeReviewState"] = Value(scope, "reviewState"),
                ["resourceIds"] = Value(scope, "resourceIds"), ["exclusions"] = Value(scope, "exclusions"),
                ["scopeVersion"] = Value(scope, "version"), ["discoveredAt"] = Value(scope, "discoveredAt")
            };
            properties["currentAllocationVersion"] = allocation?.Revision.ToString();
            properties["allocationState"] = allocation?.State;
            properties["permittedResourceScopes"] = allocation?.PermittedResourceScopesJson;
            properties["resourceScopeAuthority"] = currentRegistration is not null
                && currentRegistration.Status == Ato.Copilot.Core.Models.Onboarding.SubscriptionStatus.Selected
                && string.Equals(currentRegistration.SubscriptionId.ToString(), Value(registration, "subscriptionId"), StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentRegistration.ParentTenantId.ToString(), Value(registration, "directoryTenantId"), StringComparison.OrdinalIgnoreCase)
                && currentRegistration.Environment.ToString() == Value(registration, "cloud")
                && (row.Source == "OrganizationOwned" || allocation is not null && allocation.State == "Active"
                    && allocationOffering?.Lifecycle == "Active" && allocationOffering.CurrentHostingScopeRevisionId == allocation.HostingScopeRevisionId
                    && allocation.StartsAt <= DateTimeOffset.UtcNow && (allocation.ExpiresAt is null || allocation.ExpiresAt > DateTimeOffset.UtcNow))
                ? "Recorded" : "Unavailable";
            add("Environment", new() { Id = $"environment:{row.Id}", Label = name ?? "Attached Azure environment", Kind = "Environment",
                Source = Source("SystemEnvironmentAttachment", row.Id.ToString(),
                    new { attachment = Scalars(row), properties, allocation = allocation is null ? null : Scalars(allocation),
                        offering = allocationOffering is null ? null : Scalars(allocationOffering),
                        registration = currentRegistration is null ? null : Fields(currentRegistration, "Id", "TenantId", "SubscriptionId", "ParentTenantId", "Environment", "Status") },
                    Link(id, "environment")),
                Environment = Value(registration, "cloud"), Properties = properties, ProjectionStatus = "AttachmentOnly" });
        }
        var selections = await db.Set<SystemProviderScopeSelection>().AsNoTracking().Where(x =>
            x.TenantId == TenantId && x.SystemId == id).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        var selectionIds = selections.Where(x => x.State == "Active").Select(x => x.AssignmentId).ToArray();
        var removedIds = selections.Where(x => x.State == "Removed").Select(x => x.AssignmentId).ToArray();
        fingerprints.Add(Json(selections.Select(Scalars)));
        var linkedIds = await db.Set<SystemEnvironmentHostingLinkRecord>().AsNoTracking().Where(x =>
            x.TenantId == TenantId && x.SystemId == id && x.State == "Linked").Select(x => x.AssignmentId).ToListAsync(ct);
        var legacyIds = attachments.Where(x => x.State == "Attached" && x.HostingAssignmentId.HasValue)
            .Select(x => x.HostingAssignmentId!.Value).ToArray();
        // The explicit target-tenant/system grant limits this provider-owned read; provider-wide records are never projected.
        var assignments = await db.Set<ProviderHostingAssignment>().IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TargetTenantId == TenantId && x.SystemId == id && !removedIds.Contains(x.Id)
            && (selectionIds.Contains(x.Id) || linkedIds.Contains(x.Id) || legacyIds.Contains(x.Id) || db.Set<MissionProviderRelationshipReview>().Any(review =>
                review.TenantId == TenantId && review.SystemId == id && review.AssignmentId == x.Id)))
            .Take(1001).ToDictionaryAsync(x => x.Id, ct);
        if (selections.Count > 1000 || assignments.Count > 1000) throw new ArgumentException("Provider source exceeds design budget.");
        var retainedSelections = assignments.Values.Where(assignment => !selectionIds.Contains(assignment.Id))
            .Select(assignment => new SystemProviderScopeSelection { Id = assignment.Id, TenantId = TenantId,
                SystemId = id, AssignmentId = assignment.Id, State = "Active", Version = assignment.Revision });
        foreach (var row in selections.Where(x => x.State == "Active").Concat(retainedSelections))
        {
            var retainedAssignment = !selectionIds.Contains(row.AssignmentId);
            var properties = Fields(row, "AssignmentId", "State", "Version");
            string? providerName = null;
            if (assignments.TryGetValue(row.AssignmentId, out var assignment))
            {
                properties["providerId"] = assignment.ProviderId.ToString();
                properties["offeringId"] = assignment.OfferingId.ToString();
                properties["hostingScopeRevisionId"] = assignment.HostingScopeRevisionId.ToString();
                properties["assignmentRevision"] = assignment.Revision.ToString();
                properties["assignmentSourceVersion"] = Hash(Json(Scalars(assignment)));
                var offering = await db.Set<ProviderOffering>().IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == assignment.OfferingId && x.ProviderId == assignment.ProviderId, ct);
                var scope = await db.Set<ProviderHostingScopeRevision>().IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == assignment.HostingScopeRevisionId &&
                        x.OfferingId == assignment.OfferingId && x.ProviderId == assignment.ProviderId, ct);
                var reviews = await db.Set<MissionProviderRelationshipReview>().AsNoTracking().Where(x =>
                    x.TenantId == TenantId && x.SystemId == id && x.AssignmentId == assignment.Id).OrderBy(x => x.Id).ToListAsync(ct);
                properties["offeringName"] = offering?.Name;
                properties["offeringLifecycle"] = offering?.Lifecycle;
                properties["offeringSourceVersion"] = offering is null ? null : Hash(Json(Scalars(offering)));
                properties["scopeSourceVersion"] = scope is null ? null : Hash(Json(Scalars(scope)));
                properties["relationshipReviewVersion"] = Hash(Json(reviews.Select(Scalars)));
                properties["relationshipReviewState"] = string.Join(", ", reviews.Select(x => x.State));
                if (scope is null || offering is null) properties["sourceAvailability"] = "Provider scope unavailable";
                else if (offering.Lifecycle is "Withdrawn" or "Retired" or "Archived")
                    properties["sourceAvailability"] = $"Recorded provider offering is {offering.Lifecycle}";
                providerName = await db.CspProfiles.Where(x => x.Id == assignment.ProviderId).Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
                properties["providerName"] = providerName;
            }
            else properties["sourceAvailability"] = "Assignment unavailable";
            var label = properties.GetValueOrDefault("offeringName");
            add("Environment", new() { Id = $"provider:{row.Id}", Label = string.IsNullOrWhiteSpace(label)
                    ? providerName is null ? "Selected provider scope" : $"{providerName} — selected scope"
                    : providerName is null ? label : $"{providerName} — {label}",
                Source = Source(retainedAssignment ? "ProviderHostingAssignment" : "SystemProviderScopeSelection",
                    row.Id.ToString(), properties, Link(id, "environment")),
                Kind = "ProviderReference", Provider = providerName, Properties = properties, ProjectionStatus = "AssociationOnly" });
        }
    }

    private async Task ProjectConnectionsAsync(AtoCopilotContext db, string id, Action<string, DesignNode> add,
        List<DesignEdge> edges, CancellationToken ct)
    {
        var connections = await db.SystemInterconnections.AsNoTracking().Where(x => x.TenantId == TenantId && x.RegisteredSystemId == id)
            .Include(x => x.Agreements).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        foreach (var row in connections)
        {
            var properties = Fields(row, "TargetSystemName", "TargetSystemOwner", "TargetSystemAcronym", "InterconnectionType",
                "DataFlowDirection", "DataClassification", "DataDescription", "AuthenticationMethod", "Status", "AuthorizationToConnect");
            properties["agreements"] = Json(row.Agreements.Where(x => x.TenantId == TenantId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.AgreementType, x.Status, x.EffectiveDate, x.ExpirationDate, sourceVersion = Hash(Json(Scalars(x))) }));
            properties["protocols"] = Json(row.ProtocolsUsed); properties["ports"] = Json(row.PortsUsed); properties["protection"] = Json(row.SecurityMeasures);
            var approved = row.AuthorizationToConnect && row.Status == InterconnectionStatus.Active;
            var source = Source("SystemInterconnection", row.Id, new { record = Scalars(row), properties },
                Link(id, "pps"), approved ? "Approved" : "Unreviewed", approved ? 2 : 7);
            add("PPSinterconnections", new() { Id = $"external:{row.Id}", Label = row.TargetSystemName, Kind = "ExternalSystem",
                Source = source, Properties = properties, BoundaryDisposition = "OutOfBoundary",
                ProjectionStatus = row.Status == InterconnectionStatus.Active ? "Canonical" : "Proposed" });
            var agreements = row.Agreements.Where(x => x.TenantId == TenantId).ToArray();
            var agreement = agreements.Any(x => x.Status == AgreementStatus.Signed && x.ExpirationDate > DateTime.UtcNow
                && (x.EffectiveDate is null || x.EffectiveDate <= DateTime.UtcNow)) ? "Signed"
                : agreements.Any(x => x.ExpirationDate <= DateTime.UtcNow) ? "Expired" : agreements.Length == 0 ? "Missing" : "Unapproved";
            if (row.Status != InterconnectionStatus.Active) continue;
            edges.Add(new() { Id = $"interconnection:{row.Id}", Origin = approved ? "VerifiedCanonical" : "Undetermined",
                SourceNodeId = row.DataFlowDirection == DataFlowDirection.Inbound ? $"external:{row.Id}" : $"system:{id}",
                TargetNodeId = row.DataFlowDirection == DataFlowDirection.Inbound ? $"system:{id}" : $"external:{row.Id}",
                Direction = row.DataFlowDirection.ToString(), RelationshipType = "Interconnection", Purpose = row.DataDescription,
                Classification = row.DataClassification, InformationType = row.DataDescription,
                Port = string.Join(", ", row.PortsUsed), Protocol = string.Join(", ", row.ProtocolsUsed),
                Protection = string.Join(", ", row.SecurityMeasures), InterconnectionId = row.Id,
                AgreementStatus = agreement, BoundaryCrossing = "Yes", Source = source, ReviewState = source.ReviewState, ProjectionStatus = "Canonical" });
        }
    }
}
