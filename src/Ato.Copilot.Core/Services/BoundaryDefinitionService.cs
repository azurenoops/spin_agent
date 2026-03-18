using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Dtos.Dashboard;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// CRUD operations for <see cref="AuthorizationBoundaryDefinition"/> entities.
/// Handles orphan reassignment to Primary on delete.
/// </summary>
public class BoundaryDefinitionService
{
    private readonly AtoCopilotContext _db;
    private readonly ILogger<BoundaryDefinitionService> _logger;

    public BoundaryDefinitionService(
        AtoCopilotContext db,
        ILogger<BoundaryDefinitionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>List all boundary definitions for a system with aggregate counts.</summary>
    public async Task<List<BoundaryDefinitionDto>> ListAsync(
        string systemId, CancellationToken ct = default)
    {
        // Check if a Primary boundary exists — if not, null-boundary assignments
        // are counted toward all boundaries (they belong to the system as a whole).
        var hasPrimary = await _db.AuthorizationBoundaryDefinitions
            .AnyAsync(b => b.RegisteredSystemId == systemId && b.IsPrimary, ct);

        var boundaries = await _db.AuthorizationBoundaryDefinitions
            .Where(b => b.RegisteredSystemId == systemId)
            .OrderByDescending(b => b.IsPrimary)
            .ThenBy(b => b.Name)
            .Select(b => new
            {
                Boundary = b,
                ResourceCount = b.AuthorizationBoundaries.Count,
                // Total org-wide component count for this boundary
                TotalComponents =
                    _db.ComponentSystemAssignments.Count(a =>
                        a.AuthorizationBoundaryDefinitionId == b.Id)
                    + ((b.IsPrimary || !hasPrimary)
                        ? _db.ComponentSystemAssignments.Count(a =>
                            a.RegisteredSystemId == systemId
                            && a.AuthorizationBoundaryDefinitionId == null)
                        : 0),
                // Components with at least one capability link
                CoveredComponents =
                    _db.ComponentSystemAssignments
                        .Where(a =>
                            a.AuthorizationBoundaryDefinitionId == b.Id
                            || ((b.IsPrimary || !hasPrimary)
                                && a.RegisteredSystemId == systemId
                                && a.AuthorizationBoundaryDefinitionId == null))
                        .Select(a => a.SystemComponent)
                        .Distinct()
                        .Count(c => c.CapabilityLinks.Any()),
            })
            .ToListAsync(ct);

        return boundaries.Select(r => new BoundaryDefinitionDto(
            r.Boundary.Id,
            r.Boundary.RegisteredSystemId,
            r.Boundary.Name,
            r.Boundary.BoundaryType.ToString(),
            r.Boundary.Description,
            r.Boundary.IsPrimary,
            r.ResourceCount,
            r.TotalComponents,
            r.TotalComponents > 0
                ? Math.Round((decimal)r.CoveredComponents / r.TotalComponents * 100, 1)
                : 0m,
            r.Boundary.CreatedAt)).ToList();
    }

    /// <summary>Get a single boundary definition by ID.</summary>
    public async Task<BoundaryDefinitionDto?> GetByIdAsync(
        string id, CancellationToken ct = default)
    {
        var boundary = await _db.AuthorizationBoundaryDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct);
        if (boundary is null) return null;

        var hasPrimary = await _db.AuthorizationBoundaryDefinitions
            .AnyAsync(b => b.RegisteredSystemId == boundary.RegisteredSystemId && b.IsPrimary, ct);

        var raw = await _db.AuthorizationBoundaryDefinitions
            .Where(b => b.Id == id)
            .Select(b => new
            {
                Boundary = b,
                ResourceCount = b.AuthorizationBoundaries.Count,
                TotalComponents =
                    _db.ComponentSystemAssignments.Count(a =>
                        a.AuthorizationBoundaryDefinitionId == b.Id)
                    + ((b.IsPrimary || !hasPrimary)
                        ? _db.ComponentSystemAssignments.Count(a =>
                            a.RegisteredSystemId == b.RegisteredSystemId
                            && a.AuthorizationBoundaryDefinitionId == null)
                        : 0),
                CoveredComponents =
                    _db.ComponentSystemAssignments
                        .Where(a =>
                            a.AuthorizationBoundaryDefinitionId == b.Id
                            || ((b.IsPrimary || !hasPrimary)
                                && a.RegisteredSystemId == b.RegisteredSystemId
                                && a.AuthorizationBoundaryDefinitionId == null))
                        .Select(a => a.SystemComponent)
                        .Distinct()
                        .Count(c => c.CapabilityLinks.Any()),
            })
            .FirstOrDefaultAsync(ct);

        if (raw is null) return null;

        return new BoundaryDefinitionDto(
            raw.Boundary.Id,
            raw.Boundary.RegisteredSystemId,
            raw.Boundary.Name,
            raw.Boundary.BoundaryType.ToString(),
            raw.Boundary.Description,
            raw.Boundary.IsPrimary,
            raw.ResourceCount,
            raw.TotalComponents,
            raw.TotalComponents > 0
                ? Math.Round((decimal)raw.CoveredComponents / raw.TotalComponents * 100, 1)
                : 0m,
            raw.Boundary.CreatedAt);
    }

    /// <summary>Create a new boundary definition for a system.</summary>
    public async Task<BoundaryDefinitionDto> CreateAsync(
        string systemId,
        CreateBoundaryDefinitionRequest request,
        string createdBy,
        CancellationToken ct = default)
    {
        var system = await _db.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, ct)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Check for duplicate name
        var exists = await _db.AuthorizationBoundaryDefinitions
            .AnyAsync(b => b.RegisteredSystemId == systemId && b.Name == request.Name, ct);
        if (exists)
            throw new InvalidOperationException($"Boundary '{request.Name}' already exists for system '{systemId}'.");

        if (!Enum.TryParse<BoundaryDefinitionType>(request.BoundaryType, true, out var boundaryType))
            throw new ArgumentException($"Invalid boundary type '{request.BoundaryType}'. Must be Physical, Logical, or Hybrid.");

        var entity = new AuthorizationBoundaryDefinition
        {
            RegisteredSystemId = systemId,
            Name = request.Name,
            BoundaryType = boundaryType,
            Description = request.Description,
            IsPrimary = false,
            CreatedBy = createdBy
        };

        _db.AuthorizationBoundaryDefinitions.Add(entity);

        // Audit log
        _db.AuditLogs.Add(new AuditLogEntry
        {
            Action = "BoundaryDefinition.Created",
            UserId = createdBy,
            Details = $"Created boundary '{request.Name}' for system '{system.Name}'.",
            AffectedResources = [entity.Id],
            Outcome = AuditOutcome.Success
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Created boundary definition '{Name}' ({Id}) for system '{SystemId}'",
            entity.Name, entity.Id, systemId);

        return new BoundaryDefinitionDto(
            entity.Id,
            entity.RegisteredSystemId,
            entity.Name,
            entity.BoundaryType.ToString(),
            entity.Description,
            entity.IsPrimary,
            0, 0, 0m,
            entity.CreatedAt);
    }

    /// <summary>Update an existing boundary definition.</summary>
    public async Task<BoundaryDefinitionDto> UpdateAsync(
        string id,
        CreateBoundaryDefinitionRequest request,
        CancellationToken ct = default)
    {
        var entity = await _db.AuthorizationBoundaryDefinitions
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new InvalidOperationException($"Boundary definition '{id}' not found.");

        // Check for duplicate name (excluding self)
        var exists = await _db.AuthorizationBoundaryDefinitions
            .AnyAsync(b => b.RegisteredSystemId == entity.RegisteredSystemId
                        && b.Name == request.Name
                        && b.Id != id, ct);
        if (exists)
            throw new InvalidOperationException($"Boundary '{request.Name}' already exists for this system.");

        if (!Enum.TryParse<BoundaryDefinitionType>(request.BoundaryType, true, out var boundaryType))
            throw new ArgumentException($"Invalid boundary type '{request.BoundaryType}'.");

        entity.Name = request.Name;
        entity.BoundaryType = boundaryType;
        entity.Description = request.Description;
        entity.ModifiedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Updated boundary definition '{Name}' ({Id})", entity.Name, entity.Id);

        return await GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("Boundary not found after update.");
    }

    /// <summary>
    /// Delete a non-Primary boundary definition.
    /// Orphaned components, mappings, and resources are reassigned to the system's Primary boundary.
    /// </summary>
    public async Task<DeleteBoundaryDefinitionResponse> DeleteAsync(
        string id, string deletedBy, CancellationToken ct = default)
    {
        var entity = await _db.AuthorizationBoundaryDefinitions
            .FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new InvalidOperationException($"Boundary definition '{id}' not found.");

        if (entity.IsPrimary)
            throw new InvalidOperationException("Cannot delete the Primary boundary.");

        // Find the Primary boundary for reassignment
        var primary = await _db.AuthorizationBoundaryDefinitions
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == entity.RegisteredSystemId && b.IsPrimary, ct)
            ?? throw new InvalidOperationException("No Primary boundary found for reassignment.");

        // Reassign resources
        var resources = await _db.AuthorizationBoundaries
            .Where(r => r.AuthorizationBoundaryDefinitionId == id)
            .ToListAsync(ct);
        foreach (var r in resources)
            r.AuthorizationBoundaryDefinitionId = primary.Id;

        // Reassign components
        var components = await _db.SystemComponents
            .Where(c => c.AuthorizationBoundaryDefinitionId == id)
            .ToListAsync(ct);
        foreach (var c in components)
            c.AuthorizationBoundaryDefinitionId = primary.Id;

        // Reassign mappings
        var mappings = await _db.CapabilityControlMappings
            .Where(m => m.AuthorizationBoundaryDefinitionId == id)
            .ToListAsync(ct);
        foreach (var m in mappings)
            m.AuthorizationBoundaryDefinitionId = primary.Id;

        _db.AuthorizationBoundaryDefinitions.Remove(entity);

        // Audit log
        _db.AuditLogs.Add(new AuditLogEntry
        {
            Action = "BoundaryDefinition.Deleted",
            UserId = deletedBy,
            Details = $"Deleted boundary '{entity.Name}'. Reassigned {resources.Count} resources, {components.Count} components, {mappings.Count} mappings to Primary.",
            AffectedResources = [entity.Id, primary.Id],
            Outcome = AuditOutcome.Success
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Deleted boundary '{Name}' ({Id}). Reassigned {Resources} resources, {Components} components, {Mappings} mappings to Primary '{PrimaryId}'",
            entity.Name, entity.Id, resources.Count, components.Count, mappings.Count, primary.Id);

        return new DeleteBoundaryDefinitionResponse(
            id,
            components.Count,
            mappings.Count,
            resources.Count,
            primary.Id);
    }
}
