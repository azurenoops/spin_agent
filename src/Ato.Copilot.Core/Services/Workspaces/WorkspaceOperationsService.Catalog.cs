using System.Text.Json;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Workspaces;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Core.Services.Workspaces;

public sealed partial class WorkspaceOperationsService
{
    public async Task<OrganizationCatalogAdditionResult> AddOrganizationCatalogAsync(
        Guid tenantId, OrganizationCatalogAdditionRequest request, string actor, CancellationToken ct)
    {
        request = NormalizeCatalogAddition(request);
        var intent = JsonSerializer.Serialize(request);
        await using var strategyDb = await factory.CreateDbContextAsync(ct);
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            if (!await db.Tenants.AnyAsync(x => x.Id == tenantId, ct))
                throw new KeyNotFoundException("Organization was not found.");
            var replay = await db.OrganizationCatalogAdditions.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.IdempotencyKey == request.IdempotencyKey, ct);
            if (replay is not null)
                return ReplayCatalogAddition(replay, intent);

            OrganizationCatalogAdditionResult? result = null;
            var insertingEntry = false;
            try
            {
                // All authored rows, relationships and the replay receipt commit in one EF transaction.
                result = await StageCatalogAdditionAsync(db, tenantId, request, actor, ct);
                db.OrganizationCatalogAdditions.Add(new OrganizationCatalogAddition
                {
                    TenantId = tenantId, IdempotencyKey = request.IdempotencyKey,
                    IntentJson = intent, ResultJson = JsonSerializer.Serialize(result), CreatedBy = actor
                });
                insertingEntry = db.ChangeTracker.Entries<OrganizationCatalogEntry>()
                    .Any(x => x.State == EntityState.Added);
                await db.SaveChangesAsync(ct);
                return result;
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
            {
                db.ChangeTracker.Clear();
                var winner = await db.OrganizationCatalogAdditions.IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(x => x.TenantId == tenantId
                        && x.IdempotencyKey == request.IdempotencyKey, ct);
                if (winner is not null) return ReplayCatalogAddition(winner, intent);
                if (result is null) throw;
                await RequireUniqueCatalogNameAsync(db, tenantId, request, result.RecordId, ct);
                if (insertingEntry && await db.OrganizationCatalogEntries.IgnoreQueryFilters().AnyAsync(x =>
                    x.TenantId == tenantId && x.Source == result.Source && x.RecordType == result.RecordType
                    && x.RecordId == result.RecordId, ct))
                    throw new InvalidOperationException("Catalog entry changed concurrently. Reload before adding again.");
                throw;
            }
        });
    }

    private static OrganizationCatalogAdditionResult ReplayCatalogAddition(
        OrganizationCatalogAddition operation, string intent)
    {
        if (operation.IntentJson != intent)
            throw new InvalidOperationException("Idempotency key was already used for a different catalog addition.");
        return (JsonSerializer.Deserialize<OrganizationCatalogAdditionResult>(operation.ResultJson)
            ?? throw new InvalidOperationException("Catalog addition result is unavailable.")) with { Existing = true };
    }

    private static OrganizationCatalogAdditionRequest NormalizeCatalogAddition(OrganizationCatalogAdditionRequest request)
    {
        if (request.Source is not ("local" or "provider") || request.RecordType is not ("capability" or "component"))
            throw new ArgumentException("Source must be local or provider; record type must be capability or component.");
        var key = CatalogText(request.IdempotencyKey, "Idempotency key", 100);
        var recordId = string.IsNullOrWhiteSpace(request.RecordId) ? null
            : CatalogText(request.RecordId, "Record ID", 36);
        if (recordId is not null && request.Source == "provider")
            recordId = CatalogProviderId(recordId).ToString("D");
        if (request.Components is null || request.NewComponents is null
            || request.Components.Count > 100 || request.NewComponents.Count > 100
            || request.Components.Any(x => x is null) || request.NewComponents.Any(x => x is null))
            throw new ArgumentException("Component collections are required and may each contain at most 100 components.");
        if (request.RecordType == "component" && (request.Components.Count > 0 || request.NewComponents.Count > 0))
            throw new ArgumentException("Supporting components can be selected only for a capability.");
        if (request.Source == "provider" && (recordId is null || request.Capability is not null || request.Component is not null))
            throw new ArgumentException("Provider adoption requires a source ID and cannot edit provider fields.");
        if (request.RecordType == "capability" && request.Component is not null
            || request.RecordType == "component" && request.Capability is not null
            || request.Source == "local" && recordId is null
                && (request.RecordType == "capability" ? request.Capability is null : request.Component is null))
            throw new ArgumentException("Supply the matching local definition or an existing record ID.");
        var refs = request.Components.Select(x =>
        {
            if (x.Source is not ("local" or "provider"))
                throw new ArgumentException("Component source must be local or provider.");
            var id = CatalogText(x.RecordId, "Component ID", 36);
            return x with { RecordId = x.Source == "provider" ? CatalogProviderId(id).ToString("D") : id };
        }).Distinct().OrderBy(x => x.Source, StringComparer.Ordinal).ThenBy(x => x.RecordId, StringComparer.Ordinal).ToArray();
        return request with
        {
            IdempotencyKey = key, RecordId = recordId, Components = refs,
            Capability = request.Capability is null ? null : NormalizeCatalogCapability(request.Capability),
            Component = request.Component is null ? null : NormalizeCatalogComponent(request.Component),
            NewComponents = request.NewComponents.Select(NormalizeCatalogComponent).ToArray(),
            OrganizationContribution = CatalogText(request.OrganizationContribution, "Organization contribution", 2000),
            Owner = CatalogText(request.Owner, "Organization owner", 200)
        };
    }

    private static OrganizationCatalogCapabilityRequest NormalizeCatalogCapability(OrganizationCatalogCapabilityRequest value)
    {
        var normalized = NormalizeInlineCapability(new(value.Name, value.Provider, value.Category,
            value.Description, value.ImplementationStatus, value.Owner));
        if (!Enum.IsDefined(Enum.Parse<CapabilityStatus>(normalized.ImplementationStatus)))
            throw new ArgumentException("Capability implementation status is invalid.");
        return new(normalized.Name, normalized.Provider, normalized.Category, normalized.Description,
            normalized.ImplementationStatus, normalized.Owner);
    }

    private static OrganizationCatalogComponentRequest NormalizeCatalogComponent(OrganizationCatalogComponentRequest value)
    {
        if (!Enum.TryParse<ComponentType>(value.ComponentType, true, out var type) || !Enum.IsDefined(type))
            throw new ArgumentException("Component type must be Person, Place, Thing or Policy.");
        return new(CatalogText(value.Name, "Component name", 200), type.ToString(),
            CatalogText(value.Description, "Component description", 2000), CatalogText(value.Owner, "Component owner", 200));
    }

    private static string CatalogText(string? value, string name, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > max)
            throw new ArgumentException($"{name} is required and must not exceed {max} characters.");
        return trimmed;
    }

    private static Guid CatalogProviderId(string id) => Guid.TryParse(id, out var parsed) && parsed != Guid.Empty
        ? parsed : throw new ArgumentException("Provider record ID must be a GUID.");

    private static async Task<OrganizationCatalogAdditionResult> StageCatalogAdditionAsync(
        AtoCopilotContext db, Guid tenantId, OrganizationCatalogAdditionRequest request, string actor, CancellationToken ct)
    {
        var components = request.Components.ToList();
        foreach (var reference in components)
            await RequireCatalogComponentAsync(db, tenantId, reference, ct);
        var id = request.RecordId ?? Guid.NewGuid().ToString("D");
        await RequireUniqueCatalogNameAsync(db, tenantId, request, id, ct);
        string name;
        if (request.Source == "provider")
        {
            var providerId = CatalogProviderId(id);
            if (request.RecordType == "component")
                name = (await RequireCatalogComponentAsync(db, tenantId, new("provider", id), ct)).Name;
            else
            {
                var provider = await db.CspInheritedCapabilities.AsNoTracking()
                    .Include(x => x.CspInheritedComponent)
                    .SingleOrDefaultAsync(x => x.Id == providerId && x.Status == CspInheritedCapabilityStatus.Mapped
                        && x.CspInheritedComponent.Status == CspInheritedComponentStatus.Published, ct)
                    ?? throw new KeyNotFoundException("Published provider capability was not found.");
                name = provider.Name;
                components.Add(new("provider", provider.CspInheritedComponentId.ToString("D")));
            }
        }
        else if (request.RecordType == "component")
        {
            var component = request.RecordId is null ? NewCatalogComponent(tenantId, id, request.Component!, actor)
                : await db.SystemComponents.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                    x.TenantId == tenantId && x.Id == id && x.RegisteredSystemId == null
                    && x.AuthorizationBoundaryDefinitionId == null, ct)
                    ?? throw new KeyNotFoundException("Organization component was not found.");
            if (request.RecordId is null) db.SystemComponents.Add(component);
            else if (request.Component is not null)
            {
                component.Name = request.Component.Name;
                component.ComponentType = Enum.Parse<ComponentType>(request.Component.ComponentType);
                component.Description = request.Component.Description;
                component.Owner = request.Component.Owner;
                component.ModifiedAt = DateTime.UtcNow;
            }
            name = component.Name;
        }
        else
        {
            var capability = request.RecordId is null
                ? new SecurityCapability { TenantId = tenantId, Id = id, CreatedBy = actor }
                : await db.SecurityCapabilities.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Organization capability was not found.");
            if (request.RecordId is null) db.SecurityCapabilities.Add(capability);
            if (request.Capability is { } definition)
            {
                capability.Name = definition.Name;
                capability.Provider = definition.Provider;
                capability.Category = definition.Category;
                capability.Description = definition.Description;
                capability.Owner = definition.Owner;
                capability.ImplementationStatus = Enum.Parse<CapabilityStatus>(definition.ImplementationStatus);
                if (request.RecordId is not null)
                {
                    capability.ModifiedAt = DateTime.UtcNow;
                    capability.ModifiedBy = actor;
                }
            }
            name = capability.Name;
        }
        foreach (var definition in request.NewComponents)
        {
            var component = NewCatalogComponent(tenantId, Guid.NewGuid().ToString("D"), definition, actor);
            db.SystemComponents.Add(component);
            components.Add(new("local", component.Id));
        }
        var entry = await db.OrganizationCatalogEntries.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Source == request.Source
                && x.RecordType == request.RecordType && x.RecordId == id, ct);
        if (entry is null)
        {
            entry = new OrganizationCatalogEntry
            {
                TenantId = tenantId, Source = request.Source, RecordType = request.RecordType,
                RecordId = id, CreatedBy = actor
            };
            db.OrganizationCatalogEntries.Add(entry);
        }
        // Additions are additive: re-authoring a contribution must not unlink existing reusable support.
        var previousComponents = ReadCatalogComponents(entry);
        foreach (var reference in previousComponents)
            await RequireCatalogComponentAsync(db, tenantId, reference, ct);
        components.AddRange(previousComponents);
        entry.SupportingComponentsJson = JsonSerializer.Serialize(components.Distinct()
            .OrderBy(x => x.Source, StringComparer.Ordinal).ThenBy(x => x.RecordId, StringComparer.Ordinal));
        entry.OrganizationContribution = request.OrganizationContribution;
        entry.OrganizationOwner = request.Owner;
        entry.UpdatedBy = actor;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        entry.Revision++;
        if (request.Source == "local" && request.RecordType == "capability")
        {
            var existing = await db.ComponentCapabilityLinks.IgnoreQueryFilters()
                .Where(x => x.TenantId == tenantId && x.SecurityCapabilityId == id)
                .Select(x => x.SystemComponentId).ToListAsync(ct);
            foreach (var component in components.Where(x => x.Source == "local").Distinct()
                .Where(x => !existing.Contains(x.RecordId, StringComparer.Ordinal)))
                db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                {
                    TenantId = tenantId, SecurityCapabilityId = id, SystemComponentId = component.RecordId
                });
        }
        return new(request.Source, request.RecordType, id, name, false);
    }

    private static SystemComponent NewCatalogComponent(Guid tenantId, string id,
        OrganizationCatalogComponentRequest definition, string actor) => new()
    {
        TenantId = tenantId, Id = id, Name = definition.Name, ComponentType = Enum.Parse<ComponentType>(definition.ComponentType),
        Description = definition.Description, Owner = definition.Owner, CreatedBy = actor
    };

    private static async Task RequireUniqueCatalogNameAsync(AtoCopilotContext db, Guid tenantId,
        OrganizationCatalogAdditionRequest request, string recordId, CancellationToken ct)
    {
        if (request.Source == "local" && request.RecordType == "capability" && request.Capability is { } definition
            && await db.SecurityCapabilities.IgnoreQueryFilters().AnyAsync(x =>
                x.TenantId == tenantId && x.Id != recordId && x.Name == definition.Name, ct))
            throw new InvalidOperationException("An organization capability with this name already exists.");
    }

    private static IReadOnlyList<OrganizationCatalogComponentReference> ReadCatalogComponents(OrganizationCatalogEntry entry) =>
        JsonSerializer.Deserialize<OrganizationCatalogComponentReference[]>(entry.SupportingComponentsJson)
            ?? throw new InvalidOperationException("Organization supporting component references are unavailable.");

    private static async Task<SupportingComponentSummary> RequireCatalogComponentAsync(
        AtoCopilotContext db, Guid tenantId, OrganizationCatalogComponentReference reference, CancellationToken ct)
    {
        var component = await FindCatalogComponentAsync(db, tenantId, reference, ct);
        return component ?? throw new KeyNotFoundException("Organization or published provider component was not found.");
    }

    private static async Task<SupportingComponentSummary?> FindCatalogComponentAsync(
        AtoCopilotContext db, Guid tenantId, OrganizationCatalogComponentReference reference, CancellationToken ct)
    {
        if (reference.Source == "local")
            return await db.SystemComponents.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Id == reference.RecordId
                    && x.RegisteredSystemId == null && x.AuthorizationBoundaryDefinitionId == null)
                .Select(x => new SupportingComponentSummary(x.Id, x.Name, x.ComponentType.ToString(), "local", x.Description))
                .SingleOrDefaultAsync(ct);
        var id = CatalogProviderId(reference.RecordId);
        var provider = await db.CspInheritedComponents.AsNoTracking()
            .Where(x => x.Id == id && x.Status == CspInheritedComponentStatus.Published)
            .Select(x => new SupportingComponentSummary(x.Id.ToString(), x.Name, x.ComponentType.ToString(), "provider", x.Description))
            .SingleOrDefaultAsync(ct);
        return provider is null ? null : provider with { Id = id.ToString("D") };
    }
}
