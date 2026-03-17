using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Service for System Component CRUD (Person/Place/Thing inventory).
/// </summary>
public class ComponentService
{
    private readonly AtoCopilotContext _db;
    private readonly ILogger<ComponentService> _logger;

    /// <summary>Initializes a new instance of <see cref="ComponentService"/>.</summary>
    public ComponentService(AtoCopilotContext db, ILogger<ComponentService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Returns paginated components for a system with summary counts.
    /// </summary>
    public async Task<ComponentInventoryResponse?> GetComponentsAsync(
        string systemId,
        ComponentQuery query,
        CancellationToken cancellationToken = default)
    {
        var systemExists = await _db.RegisteredSystems
            .AnyAsync(s => s.Id == systemId && s.IsActive, cancellationToken);

        if (!systemExists) return null;

        var q = _db.SystemComponents
            .Where(c => c.RegisteredSystemId == systemId)
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Type) &&
            Enum.TryParse<ComponentType>(query.Type, ignoreCase: true, out var typeFilter))
            q = q.Where(c => c.ComponentType == typeFilter);

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse<ComponentStatus>(query.Status, ignoreCase: true, out var statusFilter))
            q = q.Where(c => c.Status == statusFilter);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(c => c.Name.Contains(term) || (c.Description != null && c.Description.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(query.BoundaryDefinitionId))
            q = q.Where(c => c.AuthorizationBoundaryDefinitionId == query.BoundaryDefinitionId);

        var totalCount = await q.CountAsync(cancellationToken);

        // Summary counts (unfiltered)
        var allComponents = _db.SystemComponents.Where(c => c.RegisteredSystemId == systemId);
        var summary = new ComponentSummaryDto
        {
            PersonCount = await allComponents.CountAsync(c => c.ComponentType == ComponentType.Person, cancellationToken),
            PlaceCount = await allComponents.CountAsync(c => c.ComponentType == ComponentType.Place, cancellationToken),
            ThingCount = await allComponents.CountAsync(c => c.ComponentType == ComponentType.Thing, cancellationToken),
            TotalCount = await allComponents.CountAsync(cancellationToken),
        };

        var startIndex = 0;
        if (!string.IsNullOrEmpty(query.Cursor) && int.TryParse(query.Cursor, out var cursor))
            startIndex = cursor;

        var pageSize = query.EffectivePageSize;

        var components = await q
            .OrderBy(c => c.Name)
            .Skip(startIndex)
            .Take(pageSize)
            .Include(c => c.CapabilityLinks)
                .ThenInclude(cl => cl.SecurityCapability)
            .Include(c => c.AuthorizationBoundaryDefinition)
            .ToListAsync(cancellationToken);

        var items = components.Select(MapToDto).ToList();

        var nextCursor = startIndex + pageSize < totalCount
            ? (startIndex + pageSize).ToString() : null;

        return new ComponentInventoryResponse
        {
            SystemId = systemId,
            Summary = summary,
            Items = items,
            NextCursor = nextCursor,
            TotalCount = totalCount,
        };
    }

    /// <summary>
    /// Creates a new component with optional capability links.
    /// </summary>
    public async Task<SystemComponentDto?> CreateComponentAsync(
        string systemId,
        CreateComponentRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var systemExists = await _db.RegisteredSystems
            .AnyAsync(s => s.Id == systemId && s.IsActive, cancellationToken);
        if (!systemExists) return null;

        if (!Enum.TryParse<ComponentType>(request.ComponentType, ignoreCase: true, out var compType))
            compType = ComponentType.Thing;

        if (!Enum.TryParse<ComponentStatus>(request.Status, ignoreCase: true, out var compStatus))
            compStatus = ComponentStatus.Active;

        var entity = new SystemComponent
        {
            RegisteredSystemId = systemId,
            Name = request.Name,
            ComponentType = compType,
            SubType = request.SubType,
            Description = request.Description,
            Owner = request.Owner,
            Status = compStatus,
            CreatedBy = createdBy,
            AuthorizationBoundaryDefinitionId = request.BoundaryDefinitionId,
        };

        // Default to Primary boundary if not specified
        if (string.IsNullOrEmpty(entity.AuthorizationBoundaryDefinitionId))
        {
            var primary = await _db.AuthorizationBoundaryDefinitions
                .Where(b => b.RegisteredSystemId == systemId && b.IsPrimary)
                .Select(b => b.Id)
                .FirstOrDefaultAsync(cancellationToken);
            entity.AuthorizationBoundaryDefinitionId = primary;
        }

        _db.SystemComponents.Add(entity);

        // Link capabilities
        foreach (var capId in request.LinkedCapabilityIds)
        {
            var capExists = await _db.SecurityCapabilities
                .AnyAsync(c => c.Id == capId, cancellationToken);
            if (capExists)
            {
                _db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                {
                    SystemComponentId = entity.Id,
                    SecurityCapabilityId = capId,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created component {ComponentId} '{Name}' for system {SystemId}",
            entity.Id, entity.Name, systemId);

        // Reload with links
        var created = await _db.SystemComponents
            .Include(c => c.CapabilityLinks).ThenInclude(cl => cl.SecurityCapability)
            .Include(c => c.AuthorizationBoundaryDefinition)
            .FirstAsync(c => c.Id == entity.Id, cancellationToken);

        return MapToDto(created);
    }

    /// <summary>
    /// Updates a component. Returns null if not found.
    /// </summary>
    public async Task<SystemComponentDto?> UpdateComponentAsync(
        string id,
        CreateComponentRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.SystemComponents
            .Include(c => c.CapabilityLinks)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (entity is null) return null;

        entity.Name = request.Name;
        entity.SubType = request.SubType;
        entity.Description = request.Description;
        entity.Owner = request.Owner;
        entity.ModifiedAt = DateTime.UtcNow;

        if (Enum.TryParse<ComponentType>(request.ComponentType, ignoreCase: true, out var compType))
            entity.ComponentType = compType;
        if (Enum.TryParse<ComponentStatus>(request.Status, ignoreCase: true, out var compStatus))
            entity.Status = compStatus;

        // Reconcile capability links
        _db.ComponentCapabilityLinks.RemoveRange(entity.CapabilityLinks);
        foreach (var capId in request.LinkedCapabilityIds)
        {
            var capExists = await _db.SecurityCapabilities
                .AnyAsync(c => c.Id == capId, cancellationToken);
            if (capExists)
            {
                _db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                {
                    SystemComponentId = entity.Id,
                    SecurityCapabilityId = capId,
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Reload
        var updated = await _db.SystemComponents
            .Include(c => c.CapabilityLinks).ThenInclude(cl => cl.SecurityCapability)
            .Include(c => c.AuthorizationBoundaryDefinition)
            .AsNoTracking()
            .FirstAsync(c => c.Id == id, cancellationToken);

        return MapToDto(updated);
    }

    /// <summary>
    /// Deletes a component and flags linked capabilities if component was Active.
    /// Returns null if not found.
    /// </summary>
    public async Task<DeleteComponentResponse?> DeleteComponentAsync(
        string id,
        string deletedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.SystemComponents
            .Include(c => c.CapabilityLinks).ThenInclude(cl => cl.SecurityCapability)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (entity is null) return null;

        var flagged = new List<FlaggedCapabilityDto>();

        if (entity.Status == ComponentStatus.Active && entity.CapabilityLinks.Any())
        {
            foreach (var link in entity.CapabilityLinks)
            {
                flagged.Add(new FlaggedCapabilityDto
                {
                    CapabilityId = link.SecurityCapabilityId,
                    CapabilityName = link.SecurityCapability.Name,
                    Message = "Linked component removed — review capability",
                });

                _db.DashboardActivities.Add(new DashboardActivity
                {
                    RegisteredSystemId = entity.RegisteredSystemId,
                    EventType = "ComponentDeleted",
                    Actor = deletedBy,
                    Summary = $"Component '{entity.Name}' deleted — capability '{link.SecurityCapability.Name}' flagged for review",
                    RelatedEntityType = "SecurityCapability",
                    RelatedEntityId = link.SecurityCapabilityId,
                });
            }
        }

        _db.ComponentCapabilityLinks.RemoveRange(entity.CapabilityLinks);
        _db.SystemComponents.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted component {ComponentId} '{Name}': {FlaggedCount} capabilities flagged",
            id, entity.Name, flagged.Count);

        return new DeleteComponentResponse
        {
            DeletedId = id,
            FlaggedCapabilities = flagged,
        };
    }

    private static SystemComponentDto MapToDto(SystemComponent entity)
    {
        return new SystemComponentDto
        {
            Id = entity.Id,
            Name = entity.Name,
            ComponentType = entity.ComponentType.ToString(),
            SubType = entity.SubType,
            Description = entity.Description,
            Owner = entity.Owner,
            Status = entity.Status.ToString(),
            BoundaryDefinitionId = entity.AuthorizationBoundaryDefinitionId,
            BoundaryDefinitionName = entity.AuthorizationBoundaryDefinition?.Name,
            LinkedCapabilities = entity.CapabilityLinks.Select(cl => new LinkedCapabilityDto
            {
                CapabilityId = cl.SecurityCapabilityId,
                CapabilityName = cl.SecurityCapability.Name,
            }).ToList(),
            CreatedAt = entity.CreatedAt,
            ModifiedAt = entity.ModifiedAt,
        };
    }
}
