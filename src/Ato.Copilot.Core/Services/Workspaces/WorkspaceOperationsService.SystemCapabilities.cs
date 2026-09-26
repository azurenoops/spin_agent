using System.Text.Json;
using System.Text.Json.Nodes;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Workspaces;

public sealed partial class WorkspaceOperationsService
{
    private sealed record SystemCapabilityMaterial(
        IReadOnlyList<SystemSecurityCapabilityItem> Capabilities,
        IReadOnlyList<SystemSecurityCapabilityItem> Components,
        IReadOnlyList<SystemCapabilityBoundary> Boundaries,
        string BaselineRevision, string RelationshipRevision,
        ControlBaseline? Baseline, CapabilityResponsibilityResponse? Responsibilities,
        IReadOnlyDictionary<string, string> CapabilitySnapshots);

    public async Task<SystemSecurityCapabilityPage> ListSystemSecurityCapabilitiesAsync(
        Guid tenantId, string systemId, SystemSecurityCapabilityQuery query,
        SystemSecurityCapabilityAccess access, CancellationToken ct)
    {
        ValidateSystemCapabilityQuery(query);
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireSystemCapabilityAccessAsync(db, tenantId, systemId, access, false, ct);
        var data = await LoadSystemCapabilityMaterialAsync(db, tenantId, systemId, ct);
        if (query.BoundaryId is not (null or "system-wide" or "unassigned")
            && !data.Boundaries.Any(x => x.Id == query.BoundaryId))
            throw new KeyNotFoundException("Boundary was not found in this system.");
        IEnumerable<SystemSecurityCapabilityItem> rows = query.Grouping == "component" ? data.Components : data.Capabilities;
        rows = rows.Where(x => query.Scope == "applied" ? x.IsApplied : x.IsAvailable);
        if (query.Source is not null) rows = rows.Where(x => x.Source == query.Source);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            rows = rows.Where(x => x.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || x.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        if (query.ComponentType is not null)
            rows = rows.Where(x => x.ComponentType == query.ComponentType
                || x.Components.Any(c => c.ComponentType == query.ComponentType));
        if (query.BoundaryId is { } boundary)
            rows = rows.Where(x => x.Placements.Any(p => boundary == "system-wide" ? p.State == "SystemWide"
                : boundary == "unassigned" ? p.State == "Unassigned" : p.BoundaryId == boundary && p.State == "InScope"));
        var total = rows.Count();
        Func<SystemSecurityCapabilityItem, string> sort = query.Sort switch
        {
            "source" => x => x.SourceName, "status" => x => x.Status,
            "componentType" => x => x.ComponentType ?? "", _ => x => x.Name
        };
        var sorted = query.Direction == "desc"
            ? rows.OrderByDescending(sort, StringComparer.OrdinalIgnoreCase)
            : rows.OrderBy(sort, StringComparer.OrdinalIgnoreCase);
        var page = sorted.ThenBy(x => x.Source, StringComparer.Ordinal).ThenBy(x => x.RecordType, StringComparer.Ordinal)
            .ThenBy(x => x.RecordId, StringComparer.Ordinal).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
        return new(page, query.Page, query.PageSize, total, query.Scope, query.Grouping, access, data.Boundaries);
    }

    public async Task<SystemSecurityCapabilityDetail?> GetSystemSecurityCapabilityAsync(
        Guid tenantId, string systemId, string source, string recordType, string recordId,
        SystemSecurityCapabilityAccess access, CancellationToken ct)
    {
        ValidateSystemRecordKey(source, recordType, recordId);
        recordId = CanonicalRecordId(source, recordId);
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireSystemCapabilityAccessAsync(db, tenantId, systemId, access, false, ct);
        var material = await LoadSystemCapabilityMaterialAsync(db, tenantId, systemId, ct);
        var item = (recordType == "component" ? material.Components : material.Capabilities)
            .SingleOrDefault(x => x.Source == source && x.RecordId == recordId && (x.IsApplied || x.IsAvailable));
        if (item is null) return null;
        var controls = ProjectSystemCapabilityControls(item, material);
        var implementations = await db.ControlImplementations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId && item.ControlIds.Contains(x.ControlId))
            .ToListAsync(ct);
        var implementationIds = implementations.Select(x => x.Id).ToArray();
        var evidence = await db.EvidenceArtifacts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId && !x.IsDeleted
                && (source == "local" && recordType == "capability" && x.SecurityCapabilityId == recordId
                    || x.ControlImplementationId != null && implementationIds.Contains(x.ControlImplementationId)))
            .OrderBy(x => x.FileName).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.FileName, x.UploadedBy, x.ControlImplementationId, x.NarrativeType })
            .ToListAsync(ct);
        var evidenceRows = evidence.Select(x => new SystemCapabilityEvidence(x.Id, SafeFileName(x.FileName),
            x.UploadedBy, "organization", "Linked",
            implementations.SingleOrDefault(i => i.Id == x.ControlImplementationId)?.ControlId,
            x.NarrativeType.ToString(),
            $"/api/dashboard/systems/{Uri.EscapeDataString(systemId)}/evidence/{Uri.EscapeDataString(x.Id)}/download")).ToArray();
        var sourceKind = source == "provider" ? "CspCapability" : "OrganizationCapability";
        var receiptProposalIds = recordType != "capability" ? [] : await db.Set<NarrativeImpactReceipt>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId
                && x.SourceKind == sourceKind && x.SourceId == recordId && item.ControlIds.Contains(x.ControlId))
            .Select(x => x.NarrativeProposalId).Distinct().ToArrayAsync(ct);
        var proposals = recordType != "capability" ? [] : await db.NarrativeProposals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId
                && ((x.ChangeSourceKind == sourceKind && x.ChangeSourceId == recordId) || receiptProposalIds.Contains(x.Id))
                && item.ControlIds.Contains(x.ControlId))
            .ToListAsync(ct);
        var approvedIds = implementations.Where(x => x.ApprovedVersionId != null).Select(x => x.ApprovedVersionId!).ToArray();
        var approved = await db.NarrativeVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && approvedIds.Contains(x.Id) && implementationIds.Contains(x.ControlImplementationId))
            .ToDictionaryAsync(x => x.Id, ct);
        var narratives = implementations.SelectMany(implementation => new[] { "Policy", "Technical" }.Select(type =>
        {
            var relevant = proposals.Where(x => x.ControlId == implementation.ControlId && x.NarrativeType == type)
                .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
            var current = type == "Policy" ? implementation.PolicyNarrative : implementation.TechnicalNarrative;
            string? approvedText = null;
            if (implementation.ApprovedVersionId is { } versionId && approved.TryGetValue(versionId, out var version))
            {
                var snapshot = version.SnapshotJson is null ? null
                    : JsonSerializer.Deserialize<NarrativeContentSnapshot>(version.SnapshotJson);
                approvedText = type == "Policy" ? snapshot?.PolicyNarrative : snapshot?.TechnicalNarrative ?? version.Content;
            }
            var freshness = relevant.Any(x => x.Status is "Draft" or "PendingGeneration" or "GenerationFailed" or "NeedsRevision")
                ? "ReviewRequired" : current == approvedText && approvedText is not null ? "Current" : "NotApproved";
            return new SystemCapabilityNarrative(implementation.ControlId, type, approvedText, current,
                approvedText is null ? "NotApproved" : "Approved", freshness, implementation.CurrentVersion,
                relevant.Select(x => new SystemCapabilityProposal(x.Id, x.Revision, x.Status,
                    false, false, source, recordId)).ToArray(), access.CanAuthorNarratives,
                access.CanAuthorNarratives ? null : "Narrative author permission is required.");
        })).ToArray();
        return new(item, access, material.Baseline?.Id, controls, evidenceRows, narratives,
            material.RelationshipRevision, $"/systems/{Uri.EscapeDataString(systemId)}/inheritance/subscriptions");
    }

    private static string SafeFileName(string value) => value.Replace('\\', '/').Split('/').Last();

    private static void ValidateSystemRecordKey(string source, string type, string id)
    {
        if (source is not ("local" or "provider") || type is not ("capability" or "component")
            || string.IsNullOrWhiteSpace(id) || id.Length > 64 || source == "provider" && !Guid.TryParse(id, out _))
            throw new ArgumentException("A valid source-qualified capability or component is required.");
    }

    private static void ValidateSystemCapabilityQuery(SystemSecurityCapabilityQuery query)
    {
        if (query.Scope is not ("applied" or "available") || query.Grouping is not ("capability" or "component")
            || query.Source is not (null or "local" or "provider") || query.Search?.Length > 200
            || query.ComponentType is not (null or "Person" or "Place" or "Thing" or "Policy")
            || query.Sort is not ("name" or "source" or "status" or "componentType")
            || query.Direction is not ("asc" or "desc") || query.Page < 1 || query.PageSize is < 1 or > 100
            || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            throw new ArgumentException("Invalid selected-system catalog query.");
    }

    private async Task RequireSystemCapabilityAccessAsync(AtoCopilotContext db, Guid tenantId,
        string systemId, SystemSecurityCapabilityAccess access, bool write, CancellationToken ct)
    {
        if (systemAccess is not null)
        {
            if (systemTenant is null || systemTenant.EffectiveTenantId != tenantId)
                throw new KeyNotFoundException("System was not found in this workspace.");
            var current = await systemAccess.GetAccessAsync(tenantId, systemTenant.PersonId, systemId,
                systemTenant.IsCspAdmin, ct);
            access = access with { CanRead = current.Permissions.CanRead, CanManage = current.Permissions.CanManageSystem };
        }
        if (!access.CanRead || tenantId == Guid.Empty || !await db.RegisteredSystems.IgnoreQueryFilters()
                .AnyAsync(x => x.TenantId == tenantId && x.Id == systemId && x.IsActive, ct)
            || !await db.Tenants.AnyAsync(x => x.Id == tenantId && x.Status == TenantStatus.Active, ct))
            throw new KeyNotFoundException("System was not found in this organization.");
        if (write && !access.CanManage)
            throw new UnauthorizedAccessException("Current selected-system management permission is required.");
    }

    private async Task<SystemCapabilityMaterial> LoadSystemCapabilityMaterialAsync(
        AtoCopilotContext db, Guid tenantId, string systemId, CancellationToken ct, bool includeResponsibilities = true)
    {
        var boundaries = await db.AuthorizationBoundaryDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
        var boundaryIds = boundaries.Select(x => x.Id).ToArray();
        var links = await db.SystemCapabilityLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId).ToListAsync(ct);
        var subscriptions = await db.CapabilitySubscriptions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.RoutingTenantId == tenantId && x.RegisteredSystemId == systemId).ToListAsync(ct);
        var activeProviderIds = subscriptions.Where(x => x.IsActive)
            .Select(x => Guid.Parse(x.CspInheritedCapabilityId)).ToHashSet();
        var historicalProviderIds = subscriptions.Select(x => Guid.Parse(x.CspInheritedCapabilityId)).ToArray();
        var assignments = await db.ComponentSystemAssignments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId
                && (x.AuthorizationBoundaryDefinitionId == null || boundaryIds.Contains(x.AuthorizationBoundaryDefinitionId))).ToListAsync(ct);
        var placements = await db.BoundaryComponentAssignments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && boundaryIds.Contains(x.AuthorizationBoundaryDefinitionId)).ToListAsync(ct);
        var locals = await db.SecurityCapabilities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId).ToListAsync(ct);
        var localIds = locals.Select(x => x.Id).ToArray();
        var mappings = await db.CapabilityControlMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && localIds.Contains(x.SecurityCapabilityId)
                && (x.RegisteredSystemId == null || x.RegisteredSystemId == systemId)
                && (x.AuthorizationBoundaryDefinitionId == null || boundaryIds.Contains(x.AuthorizationBoundaryDefinitionId)))
            .ToListAsync(ct);
        var componentLinks = await db.ComponentCapabilityLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && localIds.Contains(x.SecurityCapabilityId)).ToListAsync(ct);
        var localComponents = await db.SystemComponents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && (x.RegisteredSystemId == null || x.RegisteredSystemId == systemId)
                && (x.AuthorizationBoundaryDefinitionId == null || boundaryIds.Contains(x.AuthorizationBoundaryDefinitionId)))
            .ToListAsync(ct);
        var providerCapabilities = await db.CspInheritedCapabilities.AsNoTracking().Include(x => x.CspInheritedComponent)
            .Where(x => x.Status == CspInheritedCapabilityStatus.Mapped && x.CspInheritedComponent.Status == CspInheritedComponentStatus.Published
                || historicalProviderIds.Contains(x.Id)).ToListAsync(ct);
        var providerIds = providerCapabilities.Select(x => x.Id).ToArray();
        var releases = (await db.ProviderCapabilityReleases.AsNoTracking()
            .Where(x => providerIds.Contains(x.CapabilityId)).OrderByDescending(x => x.Revision).ToListAsync(ct))
            .GroupBy(x => x.CapabilityId).ToDictionary(x => x.Key, x => x.First());
        var parentComponentIds = providerCapabilities.Select(c => c.CspInheritedComponentId).ToArray();
        var contributorIds = releases.Values.SelectMany(x => ReadStringArray(x.SnapshotJson, "ContributorsJson"))
            .Where(x => Guid.TryParse(x, out _)).Select(Guid.Parse).ToArray();
        var placedProviderIds = placements.Where(x => x.CspInheritedComponentId.HasValue)
            .Select(p => p.CspInheritedComponentId!.Value).ToArray();
        var providerComponents = await db.CspInheritedComponents.AsNoTracking()
            .Where(x => x.Status == CspInheritedComponentStatus.Published
                || parentComponentIds.Contains(x.Id) || placedProviderIds.Contains(x.Id) || contributorIds.Contains(x.Id)).ToListAsync(ct);
        var profiles = await db.CspProfiles.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var entries = await db.OrganizationCatalogEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.RecordType == "capability").ToListAsync(ct);
        var baseline = await db.ControlBaselines.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Inheritances).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.RegisteredSystemId == systemId, ct);
        CapabilityResponsibilityResponse? responsibilities = null;
        if (includeResponsibilities && activeProviderIds.Count != 0)
            responsibilities = await (responsibilityService
                ?? throw new InvalidOperationException("Responsibility service is required for provider applicability."))
                .PreviewAsync(systemId, ct);

        var components = new Dictionary<(string Source, string Id), SystemCapabilityComponent>();
        IReadOnlyList<SystemCapabilityPlacement> ComponentPlacements(string source, string id, SystemComponent? local)
        {
            var rows = placements.Where(x => source == "local" ? x.SystemComponentId == id : x.CspInheritedComponentId?.ToString("D") == id)
                .Select(x => new SystemCapabilityPlacement(x.Id, x.AuthorizationBoundaryDefinitionId,
                    boundaries.Single(b => b.Id == x.AuthorizationBoundaryDefinitionId).Name,
                    x.IsInScope ? "InScope" : "Excluded",
                    Hash(JsonSerializer.Serialize(new { x.Id, x.IsInScope, x.ExclusionRationale, x.ModifiedAt })))).ToList();
            if (source == "local")
            {
                rows.AddRange(assignments.Where(x => x.SystemComponentId == id).Select(x =>
                    new SystemCapabilityPlacement(x.Id, x.AuthorizationBoundaryDefinitionId,
                        boundaries.SingleOrDefault(b => b.Id == x.AuthorizationBoundaryDefinitionId)?.Name,
                        x.AuthorizationBoundaryDefinitionId is null ? "SystemWide" : "InScope",
                        Hash(JsonSerializer.Serialize(new { x.Id, x.AuthorizationBoundaryDefinitionId, x.CreatedAt })))));
                if (local?.RegisteredSystemId == systemId && !rows.Any(x => x.BoundaryId == local.AuthorizationBoundaryDefinitionId))
                    rows.Add(new($"legacy:{id}", local.AuthorizationBoundaryDefinitionId,
                        boundaries.SingleOrDefault(x => x.Id == local.AuthorizationBoundaryDefinitionId)?.Name,
                        local.AuthorizationBoundaryDefinitionId is null ? "SystemWide" : "InScope",
                        Hash(JsonSerializer.Serialize(new { local.Id, local.AuthorizationBoundaryDefinitionId, local.ModifiedAt }))));
            }
            return rows.Count == 0 ? [new("", null, null, "Unassigned", "")] : rows;
        }
        foreach (var c in localComponents)
            components.Add(("local", c.Id), new("local", "component", c.Id, c.Name, c.Description ?? "",
                c.ComponentType.ToString(), c.SubType, "Organization", "organization",
                Hash(JsonSerializer.Serialize(new { c.Id, c.Name, c.Description, c.ComponentType, c.SubType, c.Status, c.ModifiedAt })),
                ComponentPlacements("local", c.Id, c), []));
        foreach (var c in providerComponents)
            components.Add(("provider", c.Id.ToString("D")), new("provider", "component", c.Id.ToString("D"),
                c.Name, c.Description, c.ComponentType == CspComponentType.Infrastructure ? "Place" : "Thing",
                c.ComponentType.ToString(), profiles.GetValueOrDefault(c.CspProfileId, "Provider"), "provider",
                Hash(JsonSerializer.Serialize(new { c.Id, c.Name, c.Description, c.ComponentType, c.Status, c.UpdatedAt })),
                ComponentPlacements("provider", c.Id.ToString("D"), null), []));
        var capabilities = new List<SystemSecurityCapabilityItem>();
        SystemCapabilityComponent[] Resolve(IEnumerable<(string Source, string Id)> keys) =>
            keys.Distinct().Where(components.ContainsKey).Select(x => components[x])
                .OrderBy(x => x.Name).ThenBy(x => x.Source).ThenBy(x => x.RecordId).ToArray();
        foreach (var local in locals)
        {
            var entry = entries.SingleOrDefault(x => x.Source == "local" && x.RecordId == local.Id);
            var refs = componentLinks.Where(x => x.SecurityCapabilityId == local.Id).Select(x => ("local", x.SystemComponentId))
                .Concat(entry is null ? [] : ReadCatalogComponents(entry).Select(x => (x.Source, x.RecordId)));
            var contributors = Resolve(refs);
            var mapped = mappings.Where(x => x.SecurityCapabilityId == local.Id).OrderBy(x => x.ControlId).ThenBy(x => x.Id).ToArray();
            var controls = mapped.Select(x => x.ControlId.ToUpperInvariant()).Distinct().Order().ToArray();
            var revision = Hash(JsonSerializer.Serialize(new { local.Id, local.Name, local.Description, local.Category,
                local.Provider, local.Owner, local.ImplementationStatus, local.ModifiedAt,
                Controls = mapped.Select(x => new { x.Id, x.ControlId, x.Role, x.RegisteredSystemId, x.AuthorizationBoundaryDefinitionId }),
                Components = contributors.Select(x => new { x.Source, x.RecordId, x.SourceRevision }), Entry = entry?.Revision }));
            capabilities.Add(new("local", "capability", local.Id, local.Name, local.Description, "Organization",
                "organization", revision, links.Any(x => x.SecurityCapabilityId == local.Id),
                local.ImplementationStatus != CapabilityStatus.Deprecated, local.ImplementationStatus.ToString(),
                null, null, contributors, [], contributors.SelectMany(x => x.Placements).Distinct().ToArray(), controls,
                controls.Count(x => baseline is null || !baseline.ControlIds.Contains(x)
                    || !baseline.Inheritances.Any(i => i.TenantId == tenantId && i.ControlId == x))));
        }
        foreach (var provider in providerCapabilities)
        {
            var id = provider.Id.ToString("D");
            releases.TryGetValue(provider.Id, out var release);
            var refs = new List<(string Source, string Id)> { ("provider", provider.CspInheritedComponentId.ToString("D")) };
            refs.AddRange(ReadStringArray(release?.SnapshotJson, "ContributorsJson")
                .Where(x => Guid.TryParse(x, out _)).Select(x => ("provider", Guid.Parse(x).ToString("D"))));
            var sourceContributors = Resolve(refs);
            var supportIds = links.Where(x => ReadSupportIds(x).Contains(id)).Select(x => x.SecurityCapabilityId).ToArray();
            var support = capabilities.Where(x => x.Source == "local" && supportIds.Contains(x.RecordId)).ToArray();
            var contributors = sourceContributors.Concat(support.SelectMany(x => x.Components))
                .DistinctBy(x => (x.Source, x.RecordId)).ToArray();
            var controls = PublishedSystemControls(release?.SnapshotJson, provider.MappedNistControlIds);
            var relevant = responsibilities?.Items.Where(x => x.CapabilityId == provider.Id).ToArray() ?? [];
            var revision = Hash(JsonSerializer.Serialize(new { Snapshot = release?.SnapshotHash ?? Hash(CspResponsibilitySourceTracker.Snapshot(provider)),
                provider.Status, ComponentStatus = provider.CspInheritedComponent.Status,
                Components = sourceContributors.Select(x => new { x.Source, x.RecordId, x.SourceRevision }) }));
            capabilities.Add(new("provider", "capability", id,
                PublishedSystemText(release?.SnapshotJson, "Name", provider.Name),
                PublishedSystemText(release?.SnapshotJson, "Description", provider.Description),
                profiles.GetValueOrDefault(provider.CspInheritedComponent.CspProfileId, "Provider"), "provider", revision,
                activeProviderIds.Contains(provider.Id),
                provider.Status == CspInheritedCapabilityStatus.Mapped && provider.CspInheritedComponent.Status == CspInheritedComponentStatus.Published,
                provider.Status.ToString(), null, null, contributors,
                support.Select(x => new SystemCapabilityRecordReference(x.Source, x.RecordType, x.RecordId, x.Name)).ToArray(),
                contributors.SelectMany(x => x.Placements).Distinct().ToArray(), controls,
                relevant.Count(x => x.State is not ("Applied" or "Ready" or "PreservedOverride" or "Inactive"))));
        }
        var componentRows = components.Values.Select(component =>
        {
            var delivered = capabilities.Where(x => x.IsApplied && x.Components.Any(c => c.Source == component.Source && c.RecordId == component.RecordId)).ToArray();
            var applied = delivered.Length != 0 || component.Placements.Any(x => x.State != "Unassigned");
            var references = delivered.Select(x => new SystemCapabilityRecordReference(x.Source, x.RecordType, x.RecordId, x.Name)).ToArray();
            return new SystemSecurityCapabilityItem(component.Source, "component", component.RecordId, component.Name,
                component.Description, component.SourceName, component.MutationAuthority, component.SourceRevision,
                applied, component.Source == "local" ? localComponents.Single(x => x.Id == component.RecordId).Status != ComponentStatus.Decommissioned
                    : providerComponents.Single(x => x.Id.ToString("D") == component.RecordId).Status == CspInheritedComponentStatus.Published,
                applied ? "Applied" : "Available", component.ComponentType, component.SubType,
                [], references, component.Placements, delivered.SelectMany(x => x.ControlIds).Distinct().Order().ToArray(),
                delivered.Sum(x => x.ReviewRequiredCount));
        }).ToArray();
        capabilities = capabilities.Select(item => item with
        {
            Components = item.Components.Select(c => c with
            {
                Capabilities = componentRows.Single(x => x.Source == c.Source && x.RecordId == c.RecordId).Capabilities
            }).ToArray()
        }).ToList();
        var relationshipRevision = Hash(JsonSerializer.Serialize(new
        {
            Links = links.OrderBy(x => x.Id).Select(x => new { x.Id, x.SecurityCapabilityId, x.SupportingProviderCapabilityIdsJson }),
            Subscriptions = subscriptions.OrderBy(x => x.Id).Select(x => new { x.Id, x.CspInheritedCapabilityId, x.IsActive, x.SubscribedAt }),
            Assignments = assignments.OrderBy(x => x.Id).Select(x => new { x.Id, x.SystemComponentId, x.AuthorizationBoundaryDefinitionId }),
            Placements = placements.OrderBy(x => x.Id).Select(x => new { x.Id, x.SystemComponentId, x.CspInheritedComponentId,
                x.AuthorizationBoundaryDefinitionId, x.IsInScope, x.ModifiedAt }),
            Boundaries = boundaries.OrderBy(x => x.Id).Select(x => new { x.Id, x.Name }),
            LegacyPlacements = localComponents.Where(x => x.RegisteredSystemId == systemId).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.AuthorizationBoundaryDefinitionId })
        }));
        var baselineRevision = Hash(JsonSerializer.Serialize(new { baseline?.Id,
            Controls = baseline?.ControlIds.Order().ToArray(),
            Allocations = baseline?.Inheritances.OrderBy(x => x.Id).Select(x => new { x.Id, x.ControlId, x.InheritanceType, x.Provider, x.CustomerResponsibility }) }));
        return new(capabilities, componentRows, boundaries.Select(x => new SystemCapabilityBoundary(x.Id, x.Name)).ToArray(),
            baselineRevision, relationshipRevision, baseline, responsibilities,
            releases.ToDictionary(x => x.Key.ToString("D"), x => x.Value.SnapshotJson));
    }

    private static string[] ReadSupportIds(SystemCapabilityLink link) =>
        JsonSerializer.Deserialize<string[]>(link.SupportingProviderCapabilityIdsJson)
        ?? throw new InvalidDataException("Stored supporting capability references are invalid.");

    private static SystemCapabilityControl[] ProjectSystemCapabilityControls(
        SystemSecurityCapabilityItem item, SystemCapabilityMaterial material)
    {
        if (item.Source == "provider" && item.RecordType == "capability" && item.IsApplied)
            return (material.Responsibilities?.Items ?? []).Where(x => x.CapabilityId.ToString("D") == item.RecordId)
                .Select(x => new SystemCapabilityControl(x.ControlId, ProviderCoverage(x.SourceSnapshotJson, x.ControlId),
                    x.Allocation?.CustomerResponsibility, x.EffectiveInheritanceType, x.State,
                    x.ReviewedSourceRevision, x.SourceRevision, x.ReviewRevision,
                    RedactSystemSnapshot(x.SourceSnapshotJson), RedactSystemSnapshot(x.ReviewedSourceSnapshotJson),
                    x.ProviderCoverageVerified, x.CustomerDutiesReviewed, x.ReviewNotes)).ToArray();
        return item.ControlIds.Select(control =>
        {
            var allocation = material.Baseline?.Inheritances.SingleOrDefault(x => x.ControlId.Equals(control, StringComparison.OrdinalIgnoreCase));
            return new SystemCapabilityControl(control, item.Source == "provider" ? ProviderCoverage(
                    material.CapabilitySnapshots.GetValueOrDefault(item.RecordId), control) ?? item.Description : null,
                allocation?.CustomerResponsibility, allocation?.InheritanceType.ToString(),
                material.Baseline is null ? "MissingBaseline" : !material.Baseline.ControlIds.Contains(control, StringComparer.OrdinalIgnoreCase)
                    ? "OutsideBaseline" : allocation is null ? "Undesignated" : "Persisted",
                null, item.SourceRevision, null, null, null);
        }).ToArray();
    }

    private static string? ProviderCoverage(string? snapshot, string control)
    {
        if (snapshot is null) return null;
        if (ReadDuties(snapshot).TryGetValue(control, out var descriptionDuty)) return descriptionDuty;
        using var doc = JsonDocument.Parse(snapshot);
        if (doc.RootElement.TryGetProperty("ControlDuties", out var duties) && duties.TryGetProperty(control, out var duty))
            return duty.ValueKind == JsonValueKind.String ? duty.GetString() : duty.GetRawText();
        var source = doc.RootElement.TryGetProperty("Capability", out var capability) ? capability : doc.RootElement;
        return source.TryGetProperty("Description", out var description) ? description.GetString() : null;
    }

    private static string? RedactSystemSnapshot(string? snapshot)
    {
        if (snapshot is null) return null;
        var node = JsonNode.Parse(snapshot) as JsonObject ?? throw new InvalidDataException("Invalid responsibility snapshot.");
        node.Remove("References");
        var source = node["Capability"] as JsonObject ?? node;
        if (source["Component"] is JsonObject component) component.Remove("SourceArtifactReference");
        return node.ToJsonString();
    }

    private static string[] PublishedSystemControls(string? snapshot, IReadOnlyList<string> fallback)
    {
        if (snapshot is not null)
        {
            using var doc = JsonDocument.Parse(snapshot);
            if (doc.RootElement.TryGetProperty("Capability", out var capability)
                && capability.TryGetProperty("Controls", out var controls))
                return controls.EnumerateArray().Select(x => x.GetString()!.ToUpperInvariant()).Distinct().Order().ToArray();
        }
        return fallback.Select(x => x.ToUpperInvariant()).Distinct().Order().ToArray();
    }

    private static string PublishedSystemText(string? snapshot, string property, string fallback)
    {
        if (snapshot is null) return fallback;
        using var doc = JsonDocument.Parse(snapshot);
        return doc.RootElement.TryGetProperty("Capability", out var capability)
            && capability.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()! : fallback;
    }
}
