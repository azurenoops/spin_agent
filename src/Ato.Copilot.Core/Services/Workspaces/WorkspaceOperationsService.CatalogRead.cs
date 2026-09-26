using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Workspaces;

public sealed partial class WorkspaceOperationsService
{
    private sealed class CatalogRow
    {
        public string Source { get; init; } = "";
        public string RecordId { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
        public string Availability { get; init; } = "";
    }

    private static async Task<PagedResult<OrganizationCapabilityItem>> ListOrganizationCatalogAsync(
        AtoCopilotContext db, Guid tenantId, WorkspaceCatalogQuery query, string? source, CancellationToken ct)
    {
        var componentGrouping = query.Grouping == "component";
        var local = componentGrouping
            ? db.SystemComponents.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == null && x.AuthorizationBoundaryDefinitionId == null)
                .Select(x => new CatalogRow
                {
                    Source = "local", RecordId = x.Id, Name = x.Name, Description = x.Description ?? "",
                    Availability = x.Status == ComponentStatus.Active ? "Active" :
                        x.Status == ComponentStatus.Planned ? "Planned" : "Decommissioned"
                })
            : db.SecurityCapabilities.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId)
                .Select(x => new CatalogRow
                {
                    Source = "local", RecordId = x.Id, Name = x.Name, Description = x.Description,
                    Availability = x.ImplementationStatus == CapabilityStatus.Planned ? "Planned"
                        : x.ImplementationStatus == CapabilityStatus.InProgress ? "InProgress"
                        : x.ImplementationStatus == CapabilityStatus.Implemented ? "Implemented" : "Deprecated"
                });
        var provider = componentGrouping
            ? db.CspInheritedComponents.AsNoTracking().Where(x => x.Status == CspInheritedComponentStatus.Published)
                .Select(x => new CatalogRow
                {
                    Source = "provider", RecordId = x.Id.ToString(), Name = x.Name, Description = x.Description,
                    Availability = "Published"
                })
            : db.CspInheritedCapabilities.AsNoTracking().Where(x => x.Status == CspInheritedCapabilityStatus.Mapped
                && x.CspInheritedComponent.Status == CspInheritedComponentStatus.Published)
                .Select(x => new CatalogRow
                {
                    Source = "provider", RecordId = x.Id.ToString(), Name = x.Name, Description = x.Description,
                    Availability = "Available"
                });
        if (query.ComponentId is { } componentId)
        {
            var localIds = db.ComponentCapabilityLinks.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.SystemComponentId == componentId
                    && db.SystemComponents.IgnoreQueryFilters().Any(c => c.TenantId == tenantId
                        && c.Id == componentId && c.RegisteredSystemId == null && c.AuthorizationBoundaryDefinitionId == null))
                .Select(x => x.SecurityCapabilityId);
            local = local.Where(x => localIds.Contains(x.RecordId));
            if (source == "provider")
            {
                var id = CatalogProviderId(componentId);
                var capabilityIds = db.CspInheritedCapabilities.Where(x => x.CspInheritedComponentId == id)
                    .Select(x => x.Id.ToString());
                provider = provider.Where(x => capabilityIds.Contains(x.RecordId));
            }
        }
        var rows = source switch { "local" => local, "provider" => provider, _ => local.Concat(provider) };
        if (query.Search is { } search)
            rows = rows.Where(x => EF.Functions.Like(x.Name, $"%{search}%") || EF.Functions.Like(x.Description, $"%{search}%"));
        var total = await rows.CountAsync(ct);
        var ordered = query.Sort == "status"
            ? query.Direction == "desc"
                ? rows.OrderByDescending(x => x.Availability).ThenBy(x => x.Name).ThenBy(x => x.Source).ThenBy(x => x.RecordId)
                : rows.OrderBy(x => x.Availability).ThenBy(x => x.Name).ThenBy(x => x.Source).ThenBy(x => x.RecordId)
            : query.Direction == "desc"
                ? rows.OrderByDescending(x => x.Name).ThenBy(x => x.Source).ThenBy(x => x.RecordId)
                : rows.OrderBy(x => x.Name).ThenBy(x => x.Source).ThenBy(x => x.RecordId);
        var page = await ordered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var keys = page.Select(row => new CatalogRecordKey(row.Source,
            componentGrouping ? "component" : "capability",
            row.Source == "provider" ? Guid.Parse(row.RecordId).ToString("D") : row.RecordId)).ToArray();
        var details = await LoadOrganizationCatalogDetailsAsync(db, tenantId, keys, ct);
        var items = keys.Where(details.ContainsKey).Select(key => details[key].Capability).ToArray();
        return new(items, query.Page, query.PageSize, total, "Available");
    }

    private static async Task<OrganizationCapabilityDetail?> GetOrganizationCatalogAsync(
        AtoCopilotContext db, Guid tenantId, string source, string recordId, string? recordType, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(x => x.Id == tenantId, ct)) return null;
        if (source is not ("local" or "provider")) return null;
        if (source == "provider")
        {
            if (!Guid.TryParse(recordId, out var parsed)) return null;
            recordId = parsed.ToString("D");
        }
        var keys = (recordType is null ? new[] { "capability", "component" } : [recordType])
            .Select(type => new CatalogRecordKey(source, type, recordId)).ToArray();
        var details = await LoadOrganizationCatalogDetailsAsync(db, tenantId, keys, ct);
        return keys.Where(details.ContainsKey).Select(key => details[key]).FirstOrDefault();
    }

    private sealed record CatalogRecordKey(string Source, string RecordType, string Id);

    private static async Task<Dictionary<CatalogRecordKey, OrganizationCapabilityDetail>> LoadOrganizationCatalogDetailsAsync(
        AtoCopilotContext db, Guid tenantId, IReadOnlyList<CatalogRecordKey> keys, CancellationToken ct)
    {
        var result = new Dictionary<CatalogRecordKey, OrganizationCapabilityDetail>();
        if (keys.Count == 0) return result;
        var ids = keys.Select(x => x.Id).ToArray();
        var entries = (await db.OrganizationCatalogEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && ids.Contains(x.RecordId)).ToListAsync(ct))
            .Where(x => keys.Contains(new(x.Source, x.RecordType, x.RecordId)))
            .ToDictionary(x => new CatalogRecordKey(x.Source, x.RecordType, x.RecordId));
        var references = entries.ToDictionary(x => x.Key, x => ReadCatalogComponents(x.Value));
        var allReferences = references.Values.SelectMany(x => x).ToArray();
        var localIds = keys.Where(x => x.Source == "local" && x.RecordType == "capability").Select(x => x.Id).ToArray();
        var providerIds = keys.Where(x => x.Source == "provider" && x.RecordType == "capability")
            .Select(x => Guid.Parse(x.Id)).ToArray();
        var localCapabilities = localIds.Length == 0 ? new List<SecurityCapability>()
            : await db.SecurityCapabilities.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && localIds.Contains(x.Id)).ToListAsync(ct);
        var providerCapabilities = providerIds.Length == 0 ? new List<CspInheritedCapability>()
            : await db.CspInheritedCapabilities.AsNoTracking().Include(x => x.CspInheritedComponent)
                .Where(x => providerIds.Contains(x.Id) && x.Status == CspInheritedCapabilityStatus.Mapped
                    && x.CspInheritedComponent.Status == CspInheritedComponentStatus.Published).ToListAsync(ct);
        var links = localIds.Length == 0 ? new List<ComponentCapabilityLink>()
            : await db.ComponentCapabilityLinks.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && localIds.Contains(x.SecurityCapabilityId)).ToListAsync(ct);
        var mappings = localIds.Length == 0 ? new List<CapabilityControlMapping>()
            : await db.CapabilityControlMappings.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && localIds.Contains(x.SecurityCapabilityId)
                    && x.RegisteredSystemId == null && x.AuthorizationBoundaryDefinitionId == null).ToListAsync(ct);
        var localComponentIds = keys.Where(x => x.Source == "local" && x.RecordType == "component").Select(x => x.Id)
            .Concat(allReferences.Where(x => x.Source == "local").Select(x => x.RecordId))
            .Concat(links.Select(x => x.SystemComponentId)).Distinct().ToArray();
        var providerComponentIds = keys.Where(x => x.Source == "provider" && x.RecordType == "component").Select(x => Guid.Parse(x.Id))
            .Concat(allReferences.Where(x => x.Source == "provider").Select(x => Guid.Parse(x.RecordId)))
            .Concat(providerCapabilities.Select(x => x.CspInheritedComponentId)).Distinct().ToArray();
        var localComponents = localComponentIds.Length == 0 ? new List<SystemComponent>()
            : await db.SystemComponents.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && localComponentIds.Contains(x.Id)
                    && x.RegisteredSystemId == null && x.AuthorizationBoundaryDefinitionId == null).ToListAsync(ct);
        var providerComponents = providerComponentIds.Length == 0 ? new List<CspInheritedComponent>()
            : await db.CspInheritedComponents.AsNoTracking().Where(x => providerComponentIds.Contains(x.Id)
                && x.Status == CspInheritedComponentStatus.Published).ToListAsync(ct);
        var profileIds = providerComponents.Select(x => x.CspProfileId).Distinct().ToArray();
        var profiles = profileIds.Length == 0 ? new Dictionary<Guid, string>()
            : await db.CspProfiles.Where(x => profileIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var components = localComponents.Select(x => new SupportingComponentSummary(x.Id, x.Name,
                x.ComponentType.ToString(), "local", x.Description))
            .Concat(providerComponents.Select(x => new SupportingComponentSummary(x.Id.ToString("D"), x.Name,
                x.ComponentType.ToString(), "provider", x.Description)))
            .ToDictionary(x => new OrganizationCatalogComponentReference(x.Source, x.Id));
        foreach (var key in keys)
        {
            OrganizationCapabilityItem? item = null;
            var support = new List<OrganizationCatalogComponentReference>();
            IEnumerable<string> controls = [];
            string? providerName = null;
            string? sourceReference = null;
            if (key.RecordType == "capability" && key.Source == "local")
            {
                var capability = localCapabilities.SingleOrDefault(x => x.Id == key.Id);
                if (capability is null) continue;
                item = new("local", key.Id, capability.Name, capability.Description, capability.Category,
                    capability.ImplementationStatus.ToString(), false, 0, "organization");
                providerName = NonBlank(capability.Provider);
                support.AddRange(links.Where(x => x.SecurityCapabilityId == key.Id)
                    .Select(x => new OrganizationCatalogComponentReference("local", x.SystemComponentId)));
                controls = mappings.Where(x => x.SecurityCapabilityId == key.Id).Select(x => x.ControlId);
            }
            else if (key.RecordType == "capability")
            {
                var capability = providerCapabilities.SingleOrDefault(x => x.Id == Guid.Parse(key.Id));
                if (capability is null) continue;
                var parent = capability.CspInheritedComponent;
                item = new("provider", key.Id, capability.Name, capability.Description, parent.ComponentType.ToString(),
                    "Available", false, 0, "provider");
                support.Add(new("provider", parent.Id.ToString("D")));
                controls = capability.MappedNistControlIds;
                sourceReference = NonBlank(parent.SourceFileName);
                providerName = profiles.GetValueOrDefault(parent.CspProfileId);
            }
            else if (components.TryGetValue(new(key.Source, key.Id), out var component))
            {
                item = new(key.Source, key.Id, component.Name, component.Description ?? "", component.ComponentType,
                    key.Source == "provider" ? "Published" : localComponents.Single(x => x.Id == key.Id).Status.ToString(),
                    false, 0, key.Source == "provider" ? "provider" : "organization", "component");
            }
            if (item is null) continue;
            if (entries.TryGetValue(key, out var entry))
            {
                support.AddRange(references[key]);
                item = item with
                {
                    OrganizationContribution = entry.OrganizationContribution, OrganizationOwner = entry.OrganizationOwner,
                    IsOrganizationAdopted = key.Source == "provider"
                };
            }
            var supporting = support.Distinct().Where(components.ContainsKey).Select(x => components[x])
                .OrderBy(x => x.Name).ThenBy(x => x.Source).ThenBy(x => x.Id).ToArray();
            var coverage = controls.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToUpperInvariant())
                .Distinct().Order(StringComparer.Ordinal).Select(x => new CapabilityControlCoverage(x, "Undesignated")).ToArray();
            item = PresentCapability(item, supporting, coverage, providerName);
            result.Add(key, new(item, [], [], supporting, coverage, sourceReference, providerName));
        }
        return result;
    }
}
