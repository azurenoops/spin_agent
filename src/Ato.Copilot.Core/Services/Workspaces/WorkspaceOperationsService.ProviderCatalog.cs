using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Workspaces;

public sealed partial class WorkspaceOperationsService
{
    public async Task<ProviderCatalogOverview> GetProviderCatalogOverviewAsync(
        int page, int pageSize, CancellationToken ct)
    {
        var query = new WorkspaceCatalogQuery(page, pageSize).Normalize();
        await using var db = await factory.CreateDbContextAsync(ct);
        var name = await db.Set<CspProfile>().AsNoTracking().Select(x => x.DisplayName).SingleOrDefaultAsync(ct);
        var sources = db.CspInheritedComponents.AsNoTracking().Where(x =>
            !string.IsNullOrWhiteSpace(x.SourceFileName) || !string.IsNullOrWhiteSpace(x.SourceArtifactReference));
        var total = await sources.CountAsync(ct);
        var selected = await sources.OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(string.IsNullOrWhiteSpace(name) ? null : name,
            new(selected.Select(ProviderArtifact).ToArray(), query.Page, query.PageSize, total));
    }

    public async Task<ProviderCapabilityDetail?> GetProviderCapabilityAsync(Guid capabilityId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var capability = await ProviderCapabilities(db).SingleOrDefaultAsync(x => x.Id == capabilityId, ct);
        if (capability is null)
            return null;
        return (await ExpandProviderCapabilitiesAsync(db, [capability], ct)).Single();
    }

    private static IQueryable<CspInheritedCapability> ProviderCapabilities(AtoCopilotContext db) =>
        db.CspInheritedCapabilities.AsNoTracking().Include(x => x.CspInheritedComponent)
            .Where(x => x.Status != CspInheritedCapabilityStatus.Archived);

    private static async Task<IReadOnlyList<ProviderCapabilityDetail>> ExpandProviderCapabilitiesAsync(
        AtoCopilotContext db, IReadOnlyList<CspInheritedCapability> capabilities, CancellationToken ct)
    {
        if (capabilities.Count == 0)
            return [];
        var ids = capabilities.Select(x => x.Id).ToArray();
        var working = await db.ProviderCapabilityWorkingRevisions.AsNoTracking()
            .Where(x => ids.Contains(x.CapabilityId)).ToDictionaryAsync(x => x.CapabilityId, ct);
        var revisions = working.ToDictionary(x => x.Key, x => Project(x.Value));
        var releases = await db.ProviderCapabilityReleases.AsNoTracking()
            .Where(x => ids.Contains(x.CapabilityId)).GroupBy(x => x.CapabilityId)
            .Select(x => new { CapabilityId = x.Key, Revision = x.Max(y => y.Revision) })
            .ToDictionaryAsync(x => x.CapabilityId, x => x.Revision, ct);
        var adoptions = await ProviderAdoptionsAsync(db, ids, false, ct);
        var componentIds = revisions.Values.SelectMany(x => x.Contributors)
            .Select(x => Guid.TryParse(x, out var id) ? (Guid?)id : null)
            .Where(x => x.HasValue).Select(x => x!.Value)
            .Concat(capabilities.Select(x => x.CspInheritedComponentId)).Distinct().ToArray();
        var components = await db.CspInheritedComponents.AsNoTracking()
            .Where(x => componentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return capabilities.Select(capability =>
        {
            revisions.TryGetValue(capability.Id, out var revision);
            var contributors = revision?.Contributors ?? [];
            var resolved = new List<CspInheritedComponent> { capability.CspInheritedComponent };
            var unresolved = new List<string>();
            foreach (var contributor in contributors.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Guid.TryParse(contributor, out var id) && components.TryGetValue(id, out var component))
                {
                    if (resolved.All(x => x.Id != id))
                        resolved.Add(component);
                }
                else
                    unresolved.Add(contributor);
            }
            var summaries = resolved.Select(x => new SupportingComponentSummary(
                x.Id.ToString(), x.Name, x.ComponentType.ToString(), "provider", x.Description)).ToArray();
            var primary = capability.CspInheritedComponent;
            var counts = adoptions.GetValueOrDefault(capability.Id);
            var item = new ProviderCatalogItem("provider", primary.Id, capability.Id, capability.Name,
                capability.Description, primary.Name, primary.ComponentType.ToString(), primary.Status.ToString(),
                capability.Status.ToString(), primary.SourceFormat.ToString(), primary.SourceArtifactReference,
                counts?.Systems ?? 0, revision?.Revision, releases.TryGetValue(capability.Id, out var release) ? release : null,
                summaries, counts?.Organizations ?? 0, revision?.ApprovalState);
            return new ProviderCapabilityDetail(item, summaries, unresolved,
                resolved.Where(x => !string.IsNullOrWhiteSpace(x.SourceFileName)
                    || !string.IsNullOrWhiteSpace(x.SourceArtifactReference)).Select(ProviderArtifact).ToArray(),
                capability.MappedNistControlIds.ToArray());
        }).ToArray();
    }

    private static ProviderSourceArtifact ProviderArtifact(CspInheritedComponent component) =>
        new(component.Id, component.Name, component.SourceFormat.ToString(),
            component.SourceFileName, component.SourceArtifactReference);

    // Reuse the subscriber endpoint's global, tenant-qualified join; tenant filters alone hide CSP aggregates.
    private static IQueryable<ProviderSubscriberRow> ProviderSubscriberRows(AtoCopilotContext db) =>
        db.CapabilitySubscriptions.IgnoreQueryFilters().AsNoTracking().Where(x => x.IsActive)
            .Join(db.Tenants.AsNoTracking(), subscription => subscription.RoutingTenantId,
                tenant => tenant.Id, (subscription, tenant) => new { subscription, tenant })
            .Join(db.RegisteredSystems.IgnoreQueryFilters().AsNoTracking(),
                row => new { TenantId = row.subscription.RoutingTenantId, Id = row.subscription.RegisteredSystemId },
                system => new { system.TenantId, system.Id },
                (row, system) => new ProviderSubscriberRow
                {
                    Subscription = row.subscription, Tenant = row.tenant, System = system
                });

    private static async Task<Dictionary<Guid, ProviderAdoptionCounts>> ProviderAdoptionsAsync(
        AtoCopilotContext db, Guid[] ids, bool groupByComponent, CancellationToken ct)
    {
        if (ids.Length == 0)
            return [];
        var capabilities = db.CspInheritedCapabilities.AsNoTracking()
            .Where(x => groupByComponent ? ids.Contains(x.CspInheritedComponentId) : ids.Contains(x.Id));
        var scopes = ProviderSubscriberRows(db)
            .Join(capabilities, row => row.Subscription.CspInheritedCapabilityId.ToLower(),
                capability => capability.Id.ToString().ToLower(),
                (row, capability) => new
                {
                    RecordId = groupByComponent ? capability.CspInheritedComponentId : capability.Id,
                    OrganizationId = row.Tenant.Id, SystemId = row.System.Id
                }).Distinct();
        var counts = await scopes.GroupBy(x => x.RecordId)
            .Select(x => new
            {
                Id = x.Key, Systems = x.Count(),
                Organizations = x.Select(y => y.OrganizationId).Distinct().Count()
            }).ToListAsync(ct);
        return counts.ToDictionary(x => x.Id, x => new ProviderAdoptionCounts(x.Systems, x.Organizations));
    }

    private sealed record ProviderAdoptionCounts(int Systems, int Organizations);
    private sealed class ProviderSubscriberRow
    {
        public required CapabilitySubscription Subscription { get; init; }
        public required Tenant Tenant { get; init; }
        public required RegisteredSystem System { get; init; }
    }
}
