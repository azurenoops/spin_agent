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
    private readonly NarrativeTemplateService _narrativeService;

    /// <summary>Initializes a new instance of <see cref="ComponentService"/>.</summary>
    public ComponentService(AtoCopilotContext db, ILogger<ComponentService> logger, NarrativeTemplateService narrativeService)
    {
        _db = db;
        _logger = logger;
        _narrativeService = narrativeService;
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

        IQueryable<SystemComponent> q = _db.SystemComponents
            .Where(c => c.RegisteredSystemId == systemId);

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
            PolicyCount = await allComponents.CountAsync(c => c.ComponentType == ComponentType.Policy, cancellationToken),
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
            .AsNoTracking()
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
            PersonName = request.PersonName,
            Email = request.Email,
            RmfRoleName = compType == ComponentType.Person ? request.RmfRole : null,
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

        // Create system assignment so the component appears in system-scoped queries
        _db.ComponentSystemAssignments.Add(new ComponentSystemAssignment
        {
            SystemComponentId = entity.Id,
            RegisteredSystemId = systemId,
            AuthorizationBoundaryDefinitionId = entity.AuthorizationBoundaryDefinitionId,
            CreatedBy = createdBy,
        });

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

        // Auto-assign RMF role for Person components when specified
        if (compType == ComponentType.Person
            && !string.IsNullOrWhiteSpace(request.RmfRole)
            && Enum.TryParse<RmfRole>(request.RmfRole, ignoreCase: true, out var rmfRole))
        {
            var userId = request.Email
                ?? request.PersonName?.Replace(" ", ".").ToLowerInvariant()
                ?? request.Name.Replace(" ", ".").ToLowerInvariant();

            _db.RmfRoleAssignments.Add(new RmfRoleAssignment
            {
                RegisteredSystemId = systemId,
                RmfRole = rmfRole,
                UserId = userId,
                UserDisplayName = request.PersonName ?? request.Name,
                AssignedBy = createdBy,
            });
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Auto-assigned RMF role {Role} to {User} for system {SystemId}",
                rmfRole, userId, systemId);
        }

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
        entity.PersonName = request.PersonName;
        entity.Email = request.Email;
        entity.AuthorizationBoundaryDefinitionId = request.BoundaryDefinitionId;
        entity.ModifiedAt = DateTime.UtcNow;

        if (Enum.TryParse<ComponentType>(request.ComponentType, ignoreCase: true, out var compType))
        {
            entity.ComponentType = compType;
            entity.RmfRoleName = compType == ComponentType.Person ? request.RmfRole : null;
        }
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
            PersonName = entity.PersonName,
            Email = entity.Email,
            RmfRole = entity.RmfRoleName,
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

    // ─── Azure Component Import (Feature 040) ───────────────────────────────

    /// <summary>
    /// Bulk imports Azure resources as org-wide "Thing" SystemComponents.
    /// Deduplicates by matching AzureResourceId (scoped to null RegisteredSystemId for org-wide).
    /// </summary>
    public async Task<ImportAzureComponentsResult> ImportAzureComponentsAsync(
        List<AzureImportResource> resources,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var imported = new List<SystemComponentDto>();
        var skipped = new List<SkippedResource>();

        var resourceIds = resources.Select(r => r.ResourceId).ToList();
        var existingIds = await _db.SystemComponents
            .Where(c => c.RegisteredSystemId == null && c.AzureResourceId != null && resourceIds.Contains(c.AzureResourceId))
            .Select(c => c.AzureResourceId!)
            .ToListAsync(cancellationToken);
        var existingSet = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var r in resources)
        {
            if (existingSet.Contains(r.ResourceId))
            {
                skipped.Add(new SkippedResource { ResourceId = r.ResourceId, Reason = "Already exists as component" });
                continue;
            }

            var component = new SystemComponent
            {
                Name = r.Name,
                ComponentType = ComponentType.Thing,
                SubType = r.Type,
                AzureResourceId = r.ResourceId,
                AzureResourceType = r.Type,
                AzureResourceGroup = r.ResourceGroup,
                AzureLocation = r.Location,
                RegisteredSystemId = null,
                Status = ComponentStatus.Active,
                CreatedBy = createdBy,
            };
            _db.SystemComponents.Add(component);
            imported.Add(MapToDto(component));
            existingSet.Add(r.ResourceId);
        }

        if (imported.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Imported {Imported} org-wide components, skipped {Skipped}", imported.Count, skipped.Count);

        return new ImportAzureComponentsResult
        {
            Imported = imported.Count,
            Skipped = skipped.Count,
            SkippedDetails = skipped,
            Components = imported,
        };
    }

    /// <summary>
    /// Imports Azure resources as system-scoped "Thing" SystemComponents.
    /// Detects org-library duplicates and supports assigning existing org components.
    /// </summary>
    public async Task<ImportAzureComponentsResult> ImportSystemAzureComponentsAsync(
        string systemId,
        List<AzureImportResource> resources,
        List<string>? assignExistingOrgComponents,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var imported = new List<SystemComponentDto>();
        var skipped = new List<SkippedResource>();
        var assignedFromOrg = 0;

        // Assign existing org components to this system
        if (assignExistingOrgComponents?.Count > 0)
        {
            foreach (var orgCompId in assignExistingOrgComponents)
            {
                var exists = await _db.Set<ComponentSystemAssignment>()
                    .AnyAsync(a => a.SystemComponentId == orgCompId && a.RegisteredSystemId == systemId, cancellationToken);
                if (!exists)
                {
                    _db.Set<ComponentSystemAssignment>().Add(new ComponentSystemAssignment
                    {
                        SystemComponentId = orgCompId,
                        RegisteredSystemId = systemId,
                        CreatedBy = createdBy,
                    });
                    assignedFromOrg++;
                }
            }
        }

        var resourceIds = resources.Select(r => r.ResourceId).ToList();
        var existingIds = await _db.SystemComponents
            .Where(c => c.RegisteredSystemId == systemId && c.AzureResourceId != null && resourceIds.Contains(c.AzureResourceId))
            .Select(c => c.AzureResourceId!)
            .ToListAsync(cancellationToken);
        var existingSet = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var r in resources)
        {
            if (existingSet.Contains(r.ResourceId))
            {
                skipped.Add(new SkippedResource { ResourceId = r.ResourceId, Reason = "Already exists as system component" });
                continue;
            }

            var component = new SystemComponent
            {
                Name = r.Name,
                ComponentType = ComponentType.Thing,
                SubType = r.Type,
                AzureResourceId = r.ResourceId,
                AzureResourceType = r.Type,
                AzureResourceGroup = r.ResourceGroup,
                AzureLocation = r.Location,
                RegisteredSystemId = systemId,
                Status = ComponentStatus.Active,
                CreatedBy = createdBy,
            };
            _db.SystemComponents.Add(component);
            imported.Add(MapToDto(component));
            existingSet.Add(r.ResourceId);
        }

        if (imported.Count > 0 || assignedFromOrg > 0)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("System {SystemId}: imported {Imported} components, assigned {FromOrg} from org, skipped {Skipped}",
            systemId, imported.Count, assignedFromOrg, skipped.Count);

        return new ImportAzureComponentsResult
        {
            Imported = imported.Count,
            AssignedFromOrg = assignedFromOrg,
            Skipped = skipped.Count,
            SkippedDetails = skipped,
            Components = imported,
        };
    }

    // ─── Boundary Component Assignment (Feature 040 — US3) ─────────────────

    /// <summary>
    /// Lists components assigned to a boundary definition with scope, filter, and pagination.
    /// </summary>
    public async Task<BoundaryComponentListResponse> ListBoundaryComponentsAsync(
        string boundaryId,
        BoundaryComponentQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<BoundaryComponentAssignment> q = _db.BoundaryComponentAssignments
            .Where(a => a.AuthorizationBoundaryDefinitionId == boundaryId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(a => a.SystemComponent!.Name.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.TypeFilter))
        {
            if (Enum.TryParse<ComponentType>(query.TypeFilter, true, out var ct))
                q = q.Where(a => a.SystemComponent!.ComponentType == ct);
        }

        if (!string.IsNullOrWhiteSpace(query.ScopeFilter))
        {
            var inScope = query.ScopeFilter.Equals("InScope", StringComparison.OrdinalIgnoreCase);
            q = q.Where(a => a.IsInScope == inScope);
        }

        var totalCount = await q.CountAsync(cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var items = await q
            .OrderBy(a => a.SystemComponent!.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new BoundaryComponentDto
            {
                AssignmentId = a.Id,
                ComponentId = a.SystemComponentId,
                ComponentName = a.SystemComponent!.Name,
                ComponentType = a.SystemComponent.ComponentType.ToString(),
                SubType = a.SystemComponent.SubType,
                IsInScope = a.IsInScope,
                ExclusionRationale = a.ExclusionRationale,
                InheritanceProvider = a.InheritanceProvider,
                AzureResourceId = a.SystemComponent.AzureResourceId,
                AzureResourceType = a.SystemComponent.AzureResourceType,
                AzureResourceGroup = a.SystemComponent.AzureResourceGroup,
                AzureLocation = a.SystemComponent.AzureLocation,
                CreatedAt = a.CreatedAt,
                CreatedBy = a.CreatedBy,
            })
            .ToListAsync(cancellationToken);

        return new BoundaryComponentListResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>
    /// Assigns a component to a boundary definition. Returns null if duplicate.
    /// </summary>
    public async Task<(BoundaryComponentDto? Dto, string? Error)> AssignComponentToBoundaryAsync(
        string boundaryId,
        string componentId,
        bool isInScope,
        string? exclusionRationale,
        string? inheritanceProvider,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        if (!isInScope && string.IsNullOrWhiteSpace(exclusionRationale))
            return (null, "RATIONALE_REQUIRED");

        var exists = await _db.BoundaryComponentAssignments
            .AnyAsync(a => a.SystemComponentId == componentId && a.AuthorizationBoundaryDefinitionId == boundaryId, cancellationToken);
        if (exists)
            return (null, "DUPLICATE_ASSIGNMENT");

        var component = await _db.SystemComponents.FindAsync(new object[] { componentId }, cancellationToken);
        if (component == null)
            return (null, "NOT_FOUND");

        var assignment = new BoundaryComponentAssignment
        {
            SystemComponentId = componentId,
            AuthorizationBoundaryDefinitionId = boundaryId,
            IsInScope = isInScope,
            ExclusionRationale = isInScope ? null : exclusionRationale,
            InheritanceProvider = inheritanceProvider,
            CreatedBy = createdBy,
        };

        _db.BoundaryComponentAssignments.Add(assignment);
        await _db.SaveChangesAsync(cancellationToken);

        return (new BoundaryComponentDto
        {
            AssignmentId = assignment.Id,
            ComponentId = componentId,
            ComponentName = component.Name,
            ComponentType = component.ComponentType.ToString(),
            SubType = component.SubType,
            IsInScope = assignment.IsInScope,
            ExclusionRationale = assignment.ExclusionRationale,
            InheritanceProvider = assignment.InheritanceProvider,
            AzureResourceId = component.AzureResourceId,
            AzureResourceType = component.AzureResourceType,
            AzureResourceGroup = component.AzureResourceGroup,
            AzureLocation = component.AzureLocation,
            CreatedAt = assignment.CreatedAt,
            CreatedBy = assignment.CreatedBy,
        }, null);
    }

    /// <summary>
    /// Updates an existing boundary-component assignment's scope and rationale.
    /// </summary>
    public async Task<(BoundaryComponentDto? Dto, string? Error)> UpdateBoundaryAssignmentAsync(
        string assignmentId,
        bool isInScope,
        string? exclusionRationale,
        string? inheritanceProvider,
        string modifiedBy,
        CancellationToken cancellationToken = default)
    {
        if (!isInScope && string.IsNullOrWhiteSpace(exclusionRationale))
            return (null, "RATIONALE_REQUIRED");

        var assignment = await _db.BoundaryComponentAssignments
            .Include(a => a.SystemComponent)
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment == null)
            return (null, "NOT_FOUND");

        assignment.IsInScope = isInScope;
        assignment.ExclusionRationale = isInScope ? null : exclusionRationale;
        assignment.InheritanceProvider = inheritanceProvider;
        assignment.ModifiedAt = DateTime.UtcNow;
        assignment.ModifiedBy = modifiedBy;

        await _db.SaveChangesAsync(cancellationToken);

        return (new BoundaryComponentDto
        {
            AssignmentId = assignment.Id,
            ComponentId = assignment.SystemComponentId,
            ComponentName = assignment.SystemComponent!.Name,
            ComponentType = assignment.SystemComponent.ComponentType.ToString(),
            SubType = assignment.SystemComponent.SubType,
            IsInScope = assignment.IsInScope,
            ExclusionRationale = assignment.ExclusionRationale,
            InheritanceProvider = assignment.InheritanceProvider,
            AzureResourceId = assignment.SystemComponent.AzureResourceId,
            AzureResourceType = assignment.SystemComponent.AzureResourceType,
            AzureResourceGroup = assignment.SystemComponent.AzureResourceGroup,
            AzureLocation = assignment.SystemComponent.AzureLocation,
            CreatedAt = assignment.CreatedAt,
            CreatedBy = assignment.CreatedBy,
        }, null);
    }

    /// <summary>
    /// Removes a component from a boundary (deletes the assignment only, not the component).
    /// </summary>
    public async Task<bool> RemoveComponentFromBoundaryAsync(
        string assignmentId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _db.BoundaryComponentAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment == null) return false;

        _db.BoundaryComponentAssignments.Remove(assignment);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // ─── Finding–Component Resolution (Feature 040 US6) ─────────────────────

    /// <summary>
    /// Match unlinked ComplianceFinding rows to SystemComponents by
    /// ResourceId → AzureResourceId within the same system.
    /// Returns the number of findings linked.
    /// </summary>
    public async Task<int> ResolveFindingComponentsAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        // Build a lookup of AzureResourceId → ComponentId for components in this system
        // (both system-scoped AND org-wide components assigned to boundaries of this system)
        var systemComponentMap = await _db.SystemComponents
            .Where(c => c.RegisteredSystemId == systemId && c.AzureResourceId != null)
            .Select(c => new { c.AzureResourceId, c.Id })
            .ToDictionaryAsync(c => c.AzureResourceId!, c => c.Id, cancellationToken);

        // Also include org-wide components that are assigned to boundaries in this system
        var orgComponentMap = await _db.BoundaryComponentAssignments
            .Where(a => a.AuthorizationBoundaryDefinition!.RegisteredSystemId == systemId)
            .Where(a => a.SystemComponent!.AzureResourceId != null && a.SystemComponent.RegisteredSystemId == null)
            .Select(a => new { a.SystemComponent!.AzureResourceId, a.SystemComponent.Id })
            .Distinct()
            .ToDictionaryAsync(c => c.AzureResourceId!, c => c.Id, cancellationToken);

        // Merge (system-level takes precedence)
        foreach (var kv in orgComponentMap)
        {
            systemComponentMap.TryAdd(kv.Key, kv.Value);
        }

        if (systemComponentMap.Count == 0) return 0;

        // Find all unlinked findings for assessments in this system
        var unlinkFindings = await _db.Set<ComplianceFinding>()
            .Where(f => f.ComponentId == null)
            .Where(f => _db.Set<ComplianceAssessment>()
                .Any(a => a.RegisteredSystemId == systemId && a.Findings.Any(af => af.Id == f.Id)))
            .ToListAsync(cancellationToken);

        int linked = 0;
        foreach (var finding in unlinkFindings)
        {
            if (!string.IsNullOrEmpty(finding.ResourceId) &&
                systemComponentMap.TryGetValue(finding.ResourceId, out var componentId))
            {
                finding.ComponentId = componentId;
                linked++;
            }
        }

        if (linked > 0) await _db.SaveChangesAsync(cancellationToken);
        return linked;
    }

    /// <summary>
    /// When a new component is created with an AzureResourceId, retroactively link
    /// unlinked findings in the same system.
    /// Returns the number of findings linked.
    /// </summary>
    public async Task<int> RetroactiveLinkComponentAsync(
        string componentId,
        CancellationToken cancellationToken = default)
    {
        var component = await _db.SystemComponents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == componentId, cancellationToken);
        if (component == null || string.IsNullOrEmpty(component.AzureResourceId))
            return 0;

        // Determine which system(s) this component is in
        var systemIds = new List<string>();
        if (component.RegisteredSystemId != null)
        {
            systemIds.Add(component.RegisteredSystemId);
        }
        else
        {
            // Org-wide: find all systems with boundary assignments for this component
            systemIds = await _db.BoundaryComponentAssignments
                .Where(a => a.SystemComponentId == componentId)
                .Select(a => a.AuthorizationBoundaryDefinition!.RegisteredSystemId!)
                .Where(sid => sid != null)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        if (systemIds.Count == 0) return 0;

        // Find unlinked findings whose ResourceId matches this component's AzureResourceId
        var findings = await _db.Set<ComplianceFinding>()
            .Where(f => f.ComponentId == null && f.ResourceId == component.AzureResourceId)
            .Where(f => _db.Set<ComplianceAssessment>()
                .Any(a => systemIds.Contains(a.RegisteredSystemId!) && a.Findings.Any(af => af.Id == f.Id)))
            .ToListAsync(cancellationToken);

        foreach (var f in findings) f.ComponentId = componentId;

        if (findings.Count > 0) await _db.SaveChangesAsync(cancellationToken);
        return findings.Count;
    }

    /// <summary>
    /// Re-links all findings in the system for a given component by clearing and re-resolving ComponentId (FR-027).
    /// </summary>
    public async Task<int> RelinkComponentFindingsAsync(
        string systemId,
        string componentId,
        CancellationToken cancellationToken = default)
    {
        var component = await _db.SystemComponents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == componentId, cancellationToken);
        if (component == null || string.IsNullOrEmpty(component.AzureResourceId))
            return 0;

        // Clear existing links TO this component in this system's findings
        var existingLinked = await _db.Set<ComplianceFinding>()
            .Where(f => f.ComponentId == componentId)
            .Where(f => _db.Set<ComplianceAssessment>()
                .Any(a => a.RegisteredSystemId == systemId && a.Findings.Any(af => af.Id == f.Id)))
            .ToListAsync(cancellationToken);

        foreach (var f in existingLinked) f.ComponentId = null;

        if (existingLinked.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        // Re-resolve all unlinked findings in the system
        return await ResolveFindingComponentsAsync(systemId, cancellationToken);
    }

    /// <summary>
    /// Get per-component risk summary for an assessment (or all active assessments in a system).
    /// </summary>
    public async Task<ComponentRiskSummaryResponse> GetComponentRiskSummaryAsync(
        string systemId,
        string? assessmentId = null,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ComplianceFinding> findingsQuery;

        if (!string.IsNullOrEmpty(assessmentId))
        {
            findingsQuery = _db.Set<ComplianceFinding>()
                .Where(f => _db.Set<ComplianceAssessment>()
                    .Any(a => a.Id == assessmentId && a.Findings.Any(af => af.Id == f.Id)));
        }
        else
        {
            findingsQuery = _db.Set<ComplianceFinding>()
                .Where(f => _db.Set<ComplianceAssessment>()
                    .Any(a => a.RegisteredSystemId == systemId
                         && a.Status == AssessmentStatus.Completed
                         && a.Findings.Any(af => af.Id == f.Id)));
        }

        var allFindings = await findingsQuery.AsNoTracking().ToListAsync(cancellationToken);

        var linkedFindings = allFindings.Where(f => f.ComponentId != null).ToList();
        var unlinkedCount = allFindings.Count(f => f.ComponentId == null);

        // Load component info for linked findings
        var componentIds = linkedFindings.Select(f => f.ComponentId!).Distinct().ToList();
        var components = componentIds.Count > 0
            ? await _db.SystemComponents
                .Where(c => componentIds.Contains(c.Id))
                .AsNoTracking()
                .ToDictionaryAsync(c => c.Id, cancellationToken)
            : new Dictionary<string, SystemComponent>();

        var now = DateTime.UtcNow;
        var componentRisks = linkedFindings
            .GroupBy(f => f.ComponentId!)
            .Select(g =>
            {
                components.TryGetValue(g.Key, out var comp);
                var openFindings = g.Where(f => f.Status != FindingStatus.Remediated && f.Status != FindingStatus.FalsePositive).ToList();
                var highestSev = openFindings.Count > 0
                    ? openFindings.Min(f => f.Severity)
                    : FindingSeverity.Low;

                return new ComponentRiskDto
                {
                    ComponentId = g.Key,
                    ComponentName = comp?.Name ?? "Unknown",
                    ComponentType = comp?.ComponentType.ToString() ?? "Thing",
                    OpenFindingCount = openFindings.Count,
                    HighestSeverity = highestSev.ToString(),
                    OverdueRemediationCount = openFindings.Count(f =>
                        f.Status == FindingStatus.InProgress || f.Status == FindingStatus.Open),
                };
            })
            .OrderByDescending(r => r.OpenFindingCount)
            .ToList();

        return new ComponentRiskSummaryResponse
        {
            ComponentRisks = componentRisks,
            UnlinkedFindingCount = unlinkedCount,
            TotalFindingCount = allFindings.Count,
        };
    }

    // ─── Org-Wide Component Library (Feature 036) ────────────────────────────

    /// <summary>
    /// Returns paginated org-wide components with system assignments and capability links.
    /// </summary>
    public async Task<OrgComponentListResponse> GetAllComponentsAsync(
        OrgComponentQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<SystemComponent> q = _db.SystemComponents
            .Where(c => c.RegisteredSystemId == null);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(c => c.Name.Contains(term) || (c.Description != null && c.Description.Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(query.Type) &&
            Enum.TryParse<ComponentType>(query.Type, ignoreCase: true, out var typeFilter))
            q = q.Where(c => c.ComponentType == typeFilter);

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse<ComponentStatus>(query.Status, ignoreCase: true, out var statusFilter))
            q = q.Where(c => c.Status == statusFilter);

        var totalCount = await q.CountAsync(cancellationToken);

        var page = Math.Max(1, query.Page ?? 1);
        var pageSize = Math.Clamp(query.PageSize ?? 50, 1, 200);

        var components = await q
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(c => c.CapabilityLinks).ThenInclude(cl => cl.SecurityCapability)
            .Include(c => c.SystemAssignments).ThenInclude(a => a.RegisteredSystem)
            .Include(c => c.SystemAssignments).ThenInclude(a => a.AuthorizationBoundaryDefinition)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var items = components.Select(MapToOrgDto).ToList();

        return new OrgComponentListResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>
    /// Gets a single org-wide component by ID with full details.
    /// </summary>
    public async Task<OrgComponentDto?> GetComponentByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.SystemComponents
            .Include(c => c.CapabilityLinks).ThenInclude(cl => cl.SecurityCapability)
            .Include(c => c.SystemAssignments).ThenInclude(a => a.RegisteredSystem)
            .Include(c => c.SystemAssignments).ThenInclude(a => a.AuthorizationBoundaryDefinition)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        return entity is null ? null : MapToOrgDto(entity);
    }

    /// <summary>
    /// Creates an org-wide component (no systemId required).
    /// </summary>
    public async Task<OrgComponentDto> CreateOrgComponentAsync(
        CreateComponentRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<ComponentType>(request.ComponentType, ignoreCase: true, out var compType))
            compType = ComponentType.Thing;

        if (!Enum.TryParse<ComponentStatus>(request.Status, ignoreCase: true, out var compStatus))
            compStatus = ComponentStatus.Active;

        var entity = new SystemComponent
        {
            Name = request.Name,
            ComponentType = compType,
            SubType = request.SubType,
            Description = request.Description,
            Owner = request.Owner,
            PersonName = request.PersonName,
            Email = request.Email,
            Status = compStatus,
            CreatedBy = createdBy,
        };

        _db.SystemComponents.Add(entity);

        foreach (var capId in request.LinkedCapabilityIds)
        {
            var capExists = await _db.SecurityCapabilities.AnyAsync(c => c.Id == capId, cancellationToken);
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

        _logger.LogInformation("Created org-wide component {ComponentId} '{Name}'", entity.Id, entity.Name);

        var created = await _db.SystemComponents
            .Include(c => c.CapabilityLinks).ThenInclude(cl => cl.SecurityCapability)
            .Include(c => c.SystemAssignments).ThenInclude(a => a.RegisteredSystem)
            .Include(c => c.SystemAssignments).ThenInclude(a => a.AuthorizationBoundaryDefinition)
            .FirstAsync(c => c.Id == entity.Id, cancellationToken);

        return MapToOrgDto(created);
    }

    /// <summary>
    /// Updates an org-wide component. Returns null if not found.
    /// When name, description, or owner changes, cascades narrative regeneration through linked capabilities.
    /// </summary>
    public async Task<OrgComponentDto?> UpdateOrgComponentAsync(
        string id,
        CreateComponentRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.SystemComponents
            .Include(c => c.CapabilityLinks)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (entity is null) return null;

        var cascadeNeeded = entity.Name != request.Name ||
                            entity.Description != request.Description ||
                            entity.Owner != request.Owner;

        entity.Name = request.Name;
        entity.SubType = request.SubType;
        entity.Description = request.Description;
        entity.Owner = request.Owner;
        entity.PersonName = request.PersonName;
        entity.Email = request.Email;
        entity.AuthorizationBoundaryDefinitionId = request.BoundaryDefinitionId;
        entity.ModifiedAt = DateTime.UtcNow;

        if (Enum.TryParse<ComponentType>(request.ComponentType, ignoreCase: true, out var compType))
        {
            entity.ComponentType = compType;
            entity.RmfRoleName = compType == ComponentType.Person ? request.RmfRole : null;
        }
        if (Enum.TryParse<ComponentStatus>(request.Status, ignoreCase: true, out var compStatus))
            entity.Status = compStatus;

        _db.ComponentCapabilityLinks.RemoveRange(entity.CapabilityLinks);
        foreach (var capId in request.LinkedCapabilityIds)
        {
            var capExists = await _db.SecurityCapabilities.AnyAsync(c => c.Id == capId, cancellationToken);
            if (capExists)
            {
                _db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                {
                    SystemComponentId = entity.Id,
                    SecurityCapabilityId = capId,
                });
            }
        }

        // Cascade narrative regeneration when component metadata changes
        if (cascadeNeeded)
        {
            // Save component changes first so cascade queries see updated metadata
            await _db.SaveChangesAsync(cancellationToken);
            await CascadeNarrativeRegenerationForComponentAsync(id, "system", cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var updated = await _db.SystemComponents
            .Include(c => c.CapabilityLinks).ThenInclude(cl => cl.SecurityCapability)
            .Include(c => c.SystemAssignments).ThenInclude(a => a.RegisteredSystem)
            .Include(c => c.SystemAssignments).ThenInclude(a => a.AuthorizationBoundaryDefinition)
            .AsNoTracking()
            .FirstAsync(c => c.Id == id, cancellationToken);

        return MapToOrgDto(updated);
    }

    /// <summary>
    /// Lists org-wide component assignments for a specific boundary definition.
    /// Includes assignments with null boundary when the target boundary is Primary,
    /// or when no Primary boundary exists for the system.
    /// </summary>
    public async Task<List<OrgComponentDto>> GetComponentsByBoundaryAsync(
        string boundaryDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var boundary = await _db.AuthorizationBoundaryDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == boundaryDefinitionId, cancellationToken);
        if (boundary is null) return [];

        var hasPrimary = await _db.AuthorizationBoundaryDefinitions
            .AnyAsync(b => b.RegisteredSystemId == boundary.RegisteredSystemId && b.IsPrimary, cancellationToken);

        var includeNullBoundary = boundary.IsPrimary || !hasPrimary;

        // Find component IDs that belong to this boundary
        var componentIds = await _db.ComponentSystemAssignments
            .AsNoTracking()
            .Where(a =>
                a.AuthorizationBoundaryDefinitionId == boundaryDefinitionId
                || (includeNullBoundary
                    && a.RegisteredSystemId == boundary.RegisteredSystemId
                    && a.AuthorizationBoundaryDefinitionId == null))
            .Select(a => a.SystemComponentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (componentIds.Count == 0) return [];

        // Load the components with their navigation properties (avoids cyclic Include)
        var components = await _db.SystemComponents
            .AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .Include(c => c.CapabilityLinks)
                .ThenInclude(cl => cl.SecurityCapability)
            .Include(c => c.SystemAssignments)
                .ThenInclude(sa => sa.RegisteredSystem)
            .Include(c => c.SystemAssignments)
                .ThenInclude(sa => sa.AuthorizationBoundaryDefinition)
            .ToListAsync(cancellationToken);

        return components.Select(MapToOrgDto).ToList();
    }

    /// <summary>
    /// Assigns an org-wide component to a system with boundary scope.
    /// Returns null if component or system not found. Returns the assignment or null if duplicate.
    /// </summary>
    public async Task<(SystemAssignmentDto? Assignment, string? Error)> AssignToSystemAsync(
        string componentId,
        AssignComponentRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var componentExists = await _db.SystemComponents
            .AnyAsync(c => c.Id == componentId, cancellationToken);
        if (!componentExists) return (null, "Component not found");

        var systemExists = await _db.RegisteredSystems
            .AnyAsync(s => s.Id == request.RegisteredSystemId && s.IsActive, cancellationToken);
        if (!systemExists) return (null, "System not found");

        // Check duplicate
        var duplicate = await _db.ComponentSystemAssignments
            .AnyAsync(a =>
                a.SystemComponentId == componentId &&
                a.RegisteredSystemId == request.RegisteredSystemId &&
                a.AuthorizationBoundaryDefinitionId == request.AuthorizationBoundaryDefinitionId,
                cancellationToken);
        if (duplicate) return (null, "Assignment already exists");

        var assignment = new ComponentSystemAssignment
        {
            SystemComponentId = componentId,
            RegisteredSystemId = request.RegisteredSystemId,
            AuthorizationBoundaryDefinitionId = request.AuthorizationBoundaryDefinitionId,
            CreatedBy = createdBy,
        };

        _db.ComponentSystemAssignments.Add(assignment);
        await _db.SaveChangesAsync(cancellationToken);

        // Cascade narrative regeneration for new system assignment
        await CascadeNarrativeRegenerationForComponentAsync(componentId, createdBy, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var created = await _db.ComponentSystemAssignments
            .Include(a => a.RegisteredSystem)
            .Include(a => a.AuthorizationBoundaryDefinition)
            .AsNoTracking()
            .FirstAsync(a => a.Id == assignment.Id, cancellationToken);

        _logger.LogInformation("Assigned component {ComponentId} to system {SystemId}",
            componentId, request.RegisteredSystemId);

        return (new SystemAssignmentDto
        {
            Id = created.Id,
            RegisteredSystemId = created.RegisteredSystemId,
            SystemName = created.RegisteredSystem?.Name,
            BoundaryDefinitionId = created.AuthorizationBoundaryDefinitionId,
            BoundaryName = created.AuthorizationBoundaryDefinition?.Name,
        }, null);
    }

    /// <summary>
    /// Removes a system assignment. Returns false if not found.
    /// </summary>
    public async Task<bool> RemoveAssignmentAsync(
        string componentId,
        string assignmentId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _db.ComponentSystemAssignments
            .FirstOrDefaultAsync(a => a.Id == assignmentId && a.SystemComponentId == componentId,
                cancellationToken);

        if (assignment is null) return false;

        _db.ComponentSystemAssignments.Remove(assignment);
        await _db.SaveChangesAsync(cancellationToken);

        // Cascade narrative regeneration after boundary change
        await CascadeNarrativeRegenerationForComponentAsync(componentId, "system", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Removed assignment {AssignmentId} from component {ComponentId}",
            assignmentId, componentId);
        return true;
    }

    // ─── Cascade Narrative Regeneration ──────────────────────────────────────

    /// <summary>
    /// Regenerates all narratives affected by a component change.
    /// Traverses ComponentCapabilityLink → CapabilityControlMapping → ControlImplementation,
    /// per-system with NarrativeVersion creation. Uses deterministic templates only.
    /// </summary>
    private async Task CascadeNarrativeRegenerationForComponentAsync(
        string componentId,
        string modifiedBy,
        CancellationToken cancellationToken)
    {
        // Find all capabilities linked to this component
        var linkedCapabilityIds = await _db.ComponentCapabilityLinks
            .Where(cl => cl.SystemComponentId == componentId)
            .Select(cl => cl.SecurityCapabilityId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (linkedCapabilityIds.Count == 0) return;

        // Find all control implementations for these capabilities
        var affectedImpls = await _db.ControlImplementations
            .Where(ci => ci.SecurityCapabilityId != null && linkedCapabilityIds.Contains(ci.SecurityCapabilityId))
            .ToListAsync(cancellationToken);

        int totalUpdated = 0, totalSkipped = 0;
        var implsBySystem = affectedImpls.GroupBy(i => i.RegisteredSystemId);
        var changeReason = "Cascade: linked component metadata changed";

        foreach (var systemGroup in implsBySystem)
        {
            var systemId = systemGroup.Key;
            int systemUpdated = 0, systemSkipped = 0;

            foreach (var impl in systemGroup)
            {
                if (impl.IsManuallyCustomized)
                {
                    totalSkipped++;
                    systemSkipped++;
                    continue;
                }

                var previousNarrative = impl.Narrative;
                var capId = impl.SecurityCapabilityId!;

                var cap = await _db.SecurityCapabilities
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == capId, cancellationToken);
                if (cap is null) continue;

                // Query component context for this capability + system
                var componentContexts = await _db.ComponentCapabilityLinks
                    .Where(cl => cl.SecurityCapabilityId == capId)
                    .Join(_db.ComponentSystemAssignments.Where(a => a.RegisteredSystemId == systemId),
                        cl => cl.SystemComponentId,
                        a => a.SystemComponentId,
                        (cl, a) => new { cl.SystemComponent, a.AuthorizationBoundaryDefinition })
                    .Select(x => new ComponentContext(
                        x.SystemComponent.Name,
                        x.SystemComponent.ComponentType.ToString(),
                        x.SystemComponent.Owner,
                        x.SystemComponent.PersonName))
                    .ToListAsync(cancellationToken);

                var mappings = await _db.CapabilityControlMappings
                    .Include(m => m.SecurityCapability)
                    .Include(m => m.AuthorizationBoundaryDefinition)
                    .Where(m => m.ControlId == impl.ControlId &&
                                (m.RegisteredSystemId == systemId || m.RegisteredSystemId == null))
                    .ToListAsync(cancellationToken);

                var nist = await _db.NistControls
                    .AsNoTracking()
                    .FirstOrDefaultAsync(n => n.Id == impl.ControlId, cancellationToken);
                var controlTitle = nist?.Title ?? impl.ControlId;

                if (mappings.Count <= 1)
                {
                    var boundaryName = mappings.FirstOrDefault()?.AuthorizationBoundaryDefinition?.Name;
                    impl.Narrative = _narrativeService.GenerateEnrichedNarrative(
                        cap.Name, cap.Provider, cap.Description,
                        impl.ControlId, controlTitle,
                        componentContexts.Count > 0 ? componentContexts : null,
                        boundaryName);
                }
                else
                {
                    var contexts = mappings
                        .Select(m =>
                        {
                            var capComponents = m.SecurityCapabilityId == capId && componentContexts.Count > 0
                                ? componentContexts : null;
                            return new BoundaryMappingContext(
                                m.SecurityCapability.Name,
                                m.SecurityCapability.Provider,
                                m.SecurityCapability.Description,
                                m.AuthorizationBoundaryDefinition?.Name,
                                capComponents);
                        })
                        .ToList();

                    impl.Narrative = _narrativeService.GenerateCompositeNarrative(
                        impl.ControlId, controlTitle, contexts);
                }

                if (previousNarrative is not null)
                {
                    _db.NarrativeVersions.Add(new NarrativeVersion
                    {
                        ControlImplementationId = impl.Id,
                        VersionNumber = impl.CurrentVersion,
                        Content = previousNarrative,
                        AuthoredBy = modifiedBy,
                        ChangeReason = changeReason,
                    });
                    impl.CurrentVersion++;
                }

                impl.ModifiedAt = DateTime.UtcNow;
                totalUpdated++;
                systemUpdated++;
            }

            _logger.LogInformation(
                "Component cascade for system {SystemId}: {Updated} narratives regenerated, {Skipped} customized skipped",
                systemId, systemUpdated, systemSkipped);
        }

        _logger.LogInformation(
            "Component {ComponentId} cascade complete: {Total} narratives updated, {Skipped} customized skipped",
            componentId, totalUpdated, totalSkipped);
    }

    /// <summary>
    /// Impact preview for component changes — dry-run count of affected narratives.
    /// Returns null if component not found.
    /// </summary>
    public async Task<ComponentImpactPreview?> GetComponentImpactPreviewAsync(
        string componentId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.SystemComponents
            .AnyAsync(c => c.Id == componentId, cancellationToken);
        if (!exists) return null;

        var linkedCapabilityIds = await _db.ComponentCapabilityLinks
            .Where(cl => cl.SystemComponentId == componentId)
            .Select(cl => cl.SecurityCapabilityId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (linkedCapabilityIds.Count == 0)
        {
            return new ComponentImpactPreview
            {
                TotalNarratives = 0,
                TotalSystems = 0,
                CustomSkipped = 0,
            };
        }

        var affectedImpls = await _db.ControlImplementations
            .Where(ci => ci.SecurityCapabilityId != null && linkedCapabilityIds.Contains(ci.SecurityCapabilityId))
            .Select(ci => new { ci.RegisteredSystemId, ci.IsManuallyCustomized })
            .ToListAsync(cancellationToken);

        var systemNames = await _db.RegisteredSystems
            .Where(s => affectedImpls.Select(i => i.RegisteredSystemId).Distinct().Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var bySystem = affectedImpls
            .GroupBy(i => i.RegisteredSystemId)
            .Select(g => new SystemImpactDto
            {
                SystemId = g.Key,
                SystemName = systemNames.GetValueOrDefault(g.Key),
                NarrativeCount = g.Count(i => !i.IsManuallyCustomized),
                CustomSkipped = g.Count(i => i.IsManuallyCustomized),
            })
            .ToList();

        return new ComponentImpactPreview
        {
            TotalNarratives = bySystem.Sum(s => s.NarrativeCount),
            TotalSystems = bySystem.Count,
            CustomSkipped = bySystem.Sum(s => s.CustomSkipped),
            BySystem = bySystem,
        };
    }

    /// <summary>
    /// Returns system-scoped components via ComponentSystemAssignment join.
    /// </summary>
    public async Task<ComponentInventoryResponse?> GetSystemScopedComponentsAsync(
        string systemId,
        ComponentQuery query,
        CancellationToken cancellationToken = default)
    {
        var systemExists = await _db.RegisteredSystems
            .AnyAsync(s => s.Id == systemId && s.IsActive, cancellationToken);
        if (!systemExists) return null;

        // Query components assigned to this system via ComponentSystemAssignment
        var assignedComponentIds = _db.ComponentSystemAssignments
            .Where(a => a.RegisteredSystemId == systemId)
            .Select(a => a.SystemComponentId);

        IQueryable<SystemComponent> q = _db.SystemComponents
            .Where(c => assignedComponentIds.Contains(c.Id))
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

        var totalCount = await q.CountAsync(cancellationToken);

        // Summary counts (unfiltered for this system)
        var allForSystem = _db.SystemComponents
            .Where(c => assignedComponentIds.Contains(c.Id));
        var summary = new ComponentSummaryDto
        {
            PersonCount = await allForSystem.CountAsync(c => c.ComponentType == ComponentType.Person, cancellationToken),
            PlaceCount = await allForSystem.CountAsync(c => c.ComponentType == ComponentType.Place, cancellationToken),
            ThingCount = await allForSystem.CountAsync(c => c.ComponentType == ComponentType.Thing, cancellationToken),
            TotalCount = await allForSystem.CountAsync(cancellationToken),
        };

        var startIndex = 0;
        if (!string.IsNullOrEmpty(query.Cursor) && int.TryParse(query.Cursor, out var cursor))
            startIndex = cursor;

        var pageSize = query.EffectivePageSize;

        var components = await q
            .OrderBy(c => c.Name)
            .Skip(startIndex)
            .Take(pageSize)
            .Include(c => c.CapabilityLinks).ThenInclude(cl => cl.SecurityCapability)
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
    /// Imports Entra ID users/groups as org-wide Person SystemComponents.
    /// Deduplicates by AzureResourceId (Entra object ID).
    /// </summary>
    public async Task<ImportAzureComponentsResult> ImportEntraIdPeopleAsync(
        List<EntraImportPerson> people,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var imported = new List<SystemComponentDto>();
        var skipped = new List<SkippedResource>();

        var objectIds = people.Select(p => p.EntraObjectId).ToList();
        var existingIds = await _db.SystemComponents
            .Where(c => c.RegisteredSystemId == null && c.AzureResourceId != null && objectIds.Contains(c.AzureResourceId))
            .Select(c => c.AzureResourceId!)
            .ToListAsync(cancellationToken);
        var existingSet = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var p in people)
        {
            if (existingSet.Contains(p.EntraObjectId))
            {
                skipped.Add(new SkippedResource { ResourceId = p.EntraObjectId, Reason = "Already exists as component" });
                continue;
            }

            var component = new SystemComponent
            {
                Name = p.DisplayName,
                ComponentType = ComponentType.Person,
                SubType = p.Kind == "Group" ? "EntraId/Group" : "EntraId/User",
                AzureResourceId = p.EntraObjectId,
                AzureResourceType = p.Kind == "Group" ? "EntraId/Group" : "EntraId/User",
                PersonName = p.DisplayName,
                Email = p.Email,
                RegisteredSystemId = null,
                Status = ComponentStatus.Active,
                CreatedBy = createdBy,
            };
            _db.SystemComponents.Add(component);
            imported.Add(MapToDto(component));
            existingSet.Add(p.EntraObjectId);
        }

        if (imported.Count > 0)
            await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Imported {Imported} Entra ID people, skipped {Skipped}", imported.Count, skipped.Count);

        return new ImportAzureComponentsResult
        {
            Imported = imported.Count,
            Skipped = skipped.Count,
            SkippedDetails = skipped,
            Components = imported,
        };
    }

    private static OrgComponentDto MapToOrgDto(SystemComponent entity)
    {
        return new OrgComponentDto
        {
            Id = entity.Id,
            Name = entity.Name,
            ComponentType = entity.ComponentType.ToString(),
            SubType = entity.SubType,
            Description = entity.Description,
            Owner = entity.Owner,
            PersonName = entity.PersonName,
            Email = entity.Email,
            RmfRole = entity.RmfRoleName,
            Status = entity.Status.ToString(),
            CreatedAt = entity.CreatedAt,
            CreatedBy = entity.CreatedBy,
            ModifiedAt = entity.ModifiedAt,
            SystemAssignments = entity.SystemAssignments.Select(a => new SystemAssignmentDto
            {
                Id = a.Id,
                RegisteredSystemId = a.RegisteredSystemId,
                SystemName = a.RegisteredSystem?.Name,
                BoundaryDefinitionId = a.AuthorizationBoundaryDefinitionId,
                BoundaryName = a.AuthorizationBoundaryDefinition?.Name,
            }).ToList(),
            CapabilityLinks = entity.CapabilityLinks.Select(cl => new LinkedCapabilityDto
            {
                CapabilityId = cl.SecurityCapabilityId,
                CapabilityName = cl.SecurityCapability.Name,
            }).ToList(),
        };
    }
}
