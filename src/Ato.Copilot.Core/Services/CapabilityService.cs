using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Data.Common;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Service for Security Capability CRUD and control-mapping operations.
/// </summary>
public class CapabilityService
{
    private static readonly Regex ParenthesizedEnhancementRegex = new(@"\(([^)]+)\)", RegexOptions.Compiled);

    private readonly AtoCopilotContext _db;
    private readonly ILogger<CapabilityService> _logger;
    private readonly NarrativeTemplateService _narrativeService;
    private readonly IDeviationService _deviationService;
    private readonly IOrgInheritanceService _orgInheritanceService;

    /// <summary>Initializes a new instance of <see cref="CapabilityService"/>.</summary>
    public CapabilityService(
        AtoCopilotContext db,
        ILogger<CapabilityService> logger,
        NarrativeTemplateService narrativeService,
        IDeviationService deviationService,
        IOrgInheritanceService orgInheritanceService)
    {
        _db = db;
        _logger = logger;
        _narrativeService = narrativeService;
        _deviationService = deviationService;
        _orgInheritanceService = orgInheritanceService;
    }

    // ─── List / Search ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated list of security capabilities with optional filtering.
    /// </summary>
    public async Task<PaginatedResponse<SecurityCapabilityDto>> GetCapabilitiesAsync(
        CapabilityQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _db.SecurityCapabilities.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            q = q.Where(c =>
                c.Name.Contains(term) ||
                c.Description.Contains(term) ||
                c.Provider.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
            q = q.Where(c => c.Category == query.Category);

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse<CapabilityStatus>(query.Status, ignoreCase: true, out var status))
            q = q.Where(c => c.ImplementationStatus == status);

        var totalCount = await q.CountAsync(cancellationToken);

        var startIndex = 0;
        if (!string.IsNullOrEmpty(query.Cursor) && int.TryParse(query.Cursor, out var cursor))
            startIndex = cursor;

        var pageSize = query.EffectivePageSize;

        var capabilities = await q
            .OrderBy(c => c.Name)
            .Skip(startIndex)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var capIds = capabilities.Select(c => c.Id).ToList();

        var mappingCounts = await _db.CapabilityControlMappings
            .Where(m => capIds.Contains(m.SecurityCapabilityId))
            .GroupBy(m => m.SecurityCapabilityId)
            .Select(g => new { CapId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var systemCounts = await _db.ControlImplementations
            .Where(ci => ci.SecurityCapabilityId != null && capIds.Contains(ci.SecurityCapabilityId!))
            .GroupBy(ci => ci.SecurityCapabilityId!)
            .Select(g => new { CapId = g.Key, Count = g.Select(ci => ci.RegisteredSystemId).Distinct().Count() })
            .ToListAsync(cancellationToken);

        // Feature 045: Load linked components for badge display
        var componentLinks = await _db.ComponentCapabilityLinks
            .Where(l => capIds.Contains(l.SecurityCapabilityId))
            .Select(l => new
            {
                l.SecurityCapabilityId,
                l.SystemComponent.Id,
                l.SystemComponent.Name,
                ComponentType = l.SystemComponent.ComponentType.ToString(),
            })
            .ToListAsync(cancellationToken);

        var componentLinksByCapId = componentLinks
            .GroupBy(l => l.SecurityCapabilityId)
            .ToDictionary(g => g.Key, g => g.Select(l => new LinkedComponentDto
            {
                Id = l.Id,
                Name = l.Name,
                ComponentType = l.ComponentType,
            }).ToList());

        var items = capabilities.Select(c => MapToDto(
            c,
            mappingCounts.FirstOrDefault(m => m.CapId == c.Id)?.Count ?? 0,
            systemCounts.FirstOrDefault(s => s.CapId == c.Id)?.Count ?? 0,
            componentLinksByCapId.GetValueOrDefault(c.Id)
        )).ToList();

        var nextCursor = startIndex + pageSize < totalCount
            ? (startIndex + pageSize).ToString()
            : null;

        return new PaginatedResponse<SecurityCapabilityDto>
        {
            Items = items,
            NextCursor = nextCursor,
            TotalCount = totalCount,
        };
    }

    // ─── Get By Id ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a single capability by ID, or null if not found.
    /// </summary>
    public async Task<SecurityCapabilityDto?> GetCapabilityByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var cap = await _db.SecurityCapabilities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (cap is null) return null;

        var mappingCount = await _db.CapabilityControlMappings
            .CountAsync(m => m.SecurityCapabilityId == id, cancellationToken);

        var systemCount = await _db.ControlImplementations
            .Where(ci => ci.SecurityCapabilityId == id)
            .Select(ci => ci.RegisteredSystemId)
            .Distinct()
            .CountAsync(cancellationToken);

        return MapToDto(cap, mappingCount, systemCount);
    }

    // ─── Create ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new security capability. Returns null if name is duplicate (caller should return 409).
    /// </summary>
    public async Task<SecurityCapabilityDto?> CreateCapabilityAsync(
        CreateCapabilityRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var duplicate = await _db.SecurityCapabilities
            .AnyAsync(c => c.Name == request.Name, cancellationToken);

        if (duplicate) return null;

        if (!Enum.TryParse<CapabilityStatus>(request.ImplementationStatus, ignoreCase: true, out var status))
            status = CapabilityStatus.Planned;

        var entity = new SecurityCapability
        {
            Name = request.Name,
            Provider = request.Provider,
            Category = request.Category,
            Description = request.Description,
            ImplementationStatus = status,
            Owner = request.Owner,
            CreatedBy = createdBy,
        };

        _db.SecurityCapabilities.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created capability {CapabilityId} '{Name}'", entity.Id, entity.Name);

        return MapToDto(entity, 0, 0);
    }

    // ─── Update ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates a capability and propagates narrative changes where applicable.
    /// Returns null if capability not found, or (null, true) if name conflicts.
    /// </summary>
    public async Task<(UpdateCapabilityResponse? Result, bool NameConflict)> UpdateCapabilityAsync(
        string id,
        CreateCapabilityRequest request,
        string modifiedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.SecurityCapabilities
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (entity is null) return (null, false);

        var nameConflict = await _db.SecurityCapabilities
            .AnyAsync(c => c.Name == request.Name && c.Id != id, cancellationToken);
        if (nameConflict) return (null, true);

        var descriptionChanged = entity.Description != request.Description ||
                                 entity.Provider != request.Provider;
        var previousStatus = entity.ImplementationStatus;

        entity.Name = request.Name;
        entity.Provider = request.Provider;
        entity.Category = request.Category;
        entity.Description = request.Description;
        entity.Owner = request.Owner;
        entity.ModifiedAt = DateTime.UtcNow;
        entity.ModifiedBy = modifiedBy;

        if (Enum.TryParse<CapabilityStatus>(request.ImplementationStatus, ignoreCase: true, out var status))
            entity.ImplementationStatus = status;

        int narrativesUpdated = 0, narrativesSkipped = 0;
        Dictionary<string, int>? narrativesByBoundary = null;

        if (descriptionChanged)
        {
            var affectedImpls = await _db.ControlImplementations
                .Where(ci => ci.SecurityCapabilityId == id)
                .ToListAsync(cancellationToken);

            narrativesByBoundary = new Dictionary<string, int>();

            // Group by system for per-system transactional batches
            var implsBySystem = affectedImpls.GroupBy(i => i.RegisteredSystemId);
            var changeReason = $"Cascade: capability '{entity.Name}' description/provider changed";

            foreach (var systemGroup in implsBySystem)
            {
                var systemId = systemGroup.Key;
                int systemUpdated = 0, systemSkipped = 0;

                // Query component context once per system
                var componentContexts = await _db.ComponentCapabilityLinks
                    .Where(cl => cl.SecurityCapabilityId == id)
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

                foreach (var impl in systemGroup)
                {
                    if (impl.IsManuallyCustomized)
                    {
                        narrativesSkipped++;
                        systemSkipped++;
                        _db.DashboardActivities.Add(new DashboardActivity
                        {
                            RegisteredSystemId = impl.RegisteredSystemId,
                            EventType = "CompositeNarrativeSkipped",
                            Actor = modifiedBy,
                            Summary = $"Upstream capability '{entity.Name}' changed — review customized narrative for {impl.ControlId}",
                            RelatedEntityType = "ControlImplementation",
                            RelatedEntityId = impl.Id,
                        });
                        continue;
                    }

                    var previousNarrative = impl.Narrative;

                    // Find all mappings for this control + system to build composite narrative
                    var mappings = await _db.CapabilityControlMappings
                        .Include(m => m.SecurityCapability)
                        .Include(m => m.AuthorizationBoundaryDefinition)
                        .Where(m => m.ControlId == impl.ControlId &&
                                    (m.RegisteredSystemId == impl.RegisteredSystemId || m.RegisteredSystemId == null))
                        .ToListAsync(cancellationToken);

                    var nist = await _db.NistControls
                        .AsNoTracking()
                        .FirstOrDefaultAsync(n => n.Id == impl.ControlId, cancellationToken);
                    var controlTitle = nist?.Title ?? impl.ControlId;

                    // Use deterministic enriched templates for bulk cascade (SC-001)
                    if (mappings.Count <= 1)
                    {
                        var boundaryName = mappings.FirstOrDefault()?.AuthorizationBoundaryDefinition?.Name;
                        impl.Narrative = _narrativeService.GenerateEnrichedNarrative(
                            entity.Name, entity.Provider, entity.Description,
                            impl.ControlId, controlTitle,
                            componentContexts.Count > 0 ? componentContexts : null,
                            boundaryName);
                    }
                    else
                    {
                        var contexts = mappings
                            .Select(m =>
                            {
                                // Include component context only for the updated capability
                                var capComponents = m.SecurityCapabilityId == id && componentContexts.Count > 0
                                    ? componentContexts
                                    : null;
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

                    // Create NarrativeVersion to track the change
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
                    narrativesUpdated++;
                    systemUpdated++;

                    // Track per-boundary counts
                    var boundaryNames = mappings
                        .Where(m => m.AuthorizationBoundaryDefinition != null)
                        .Select(m => m.AuthorizationBoundaryDefinition!.Name)
                        .Distinct();

                    foreach (var bName in boundaryNames)
                    {
                        narrativesByBoundary[bName] = narrativesByBoundary.GetValueOrDefault(bName, 0) + 1;
                    }

                    if (mappings.Any(m => m.AuthorizationBoundaryDefinitionId == null))
                    {
                        const string orgWide = "Organization-Wide";
                        narrativesByBoundary[orgWide] = narrativesByBoundary.GetValueOrDefault(orgWide, 0) + 1;
                    }
                }

                _logger.LogInformation(
                    "Cascade for system {SystemId}: {Updated} narratives regenerated, {Skipped} customized skipped",
                    systemId, systemUpdated, systemSkipped);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Feature 044: Re-derive org defaults when ImplementationStatus changes
        if (entity.ImplementationStatus != previousStatus)
        {
            await _orgInheritanceService.DeriveOrgDefaultsAsync(modifiedBy, cancellationToken);
        }

        _logger.LogInformation(
            "Updated capability {CapabilityId} '{Name}': {Updated} narratives regenerated, {Skipped} customized skipped",
            id, entity.Name, narrativesUpdated, narrativesSkipped);

        var mappingCount = await _db.CapabilityControlMappings
            .CountAsync(m => m.SecurityCapabilityId == id, cancellationToken);
        var systemCount = await _db.ControlImplementations
            .Where(ci => ci.SecurityCapabilityId == id)
            .Select(ci => ci.RegisteredSystemId).Distinct()
            .CountAsync(cancellationToken);

        return (new UpdateCapabilityResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Provider = entity.Provider,
            Category = entity.Category,
            CategoryName = ControlFamilies.FamilyNames.GetValueOrDefault(entity.Category, entity.Category),
            Description = entity.Description,
            ImplementationStatus = entity.ImplementationStatus.ToString(),
            Owner = entity.Owner,
            MappedControlCount = mappingCount,
            SystemsUsingCount = systemCount,
            CreatedAt = entity.CreatedAt,
            ModifiedAt = entity.ModifiedAt,
            NarrativesUpdated = narrativesUpdated,
            NarrativesSkipped = narrativesSkipped,
            NarrativesByBoundary = narrativesByBoundary,
        }, false);
    }

    // ─── Impact Preview ──────────────────────────────────────────────────────

    /// <summary>
    /// Dry-run preview showing how many narratives would be regenerated if this capability changes.
    /// Returns null if capability not found.
    /// </summary>
    public async Task<CapabilityImpactPreview?> GetCapabilityImpactPreviewAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.SecurityCapabilities
            .AnyAsync(c => c.Id == id, cancellationToken);
        if (!exists) return null;

        var affectedImpls = await _db.ControlImplementations
            .Where(ci => ci.SecurityCapabilityId == id)
            .Select(ci => new { ci.RegisteredSystemId, ci.IsManuallyCustomized })
            .ToListAsync(cancellationToken);

        var systemNames = await _db.RegisteredSystems
            .Where(s => affectedImpls.Select(i => i.RegisteredSystemId).Distinct().Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var bySystem = affectedImpls
            .GroupBy(i => i.RegisteredSystemId)
            .Select(g => new CapabilitySystemImpactDto
            {
                SystemId = g.Key,
                SystemName = systemNames.GetValueOrDefault(g.Key),
                NarrativeCount = g.Count(i => !i.IsManuallyCustomized),
                CustomSkipped = g.Count(i => i.IsManuallyCustomized),
            })
            .ToList();

        return new CapabilityImpactPreview
        {
            TotalNarratives = bySystem.Sum(s => s.NarrativeCount),
            TotalSystems = bySystem.Count,
            CustomSkipped = bySystem.Sum(s => s.CustomSkipped),
            BySystem = bySystem,
        };
    }

    // ─── Delete ──────────────────────────────────────────────────────────────

    // ─── Capability Coverage ─────────────────────────────────────────────────

    /// <summary>
    /// Returns capability coverage view for a system — capabilities, linked components,
    /// mapped control counts, and narrative generation status.
    /// Returns null if system not found.
    /// </summary>
    public async Task<CapabilityCoverageResponse?> GetCapabilityCoverageAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var system = await _db.RegisteredSystems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, cancellationToken);
        if (system is null) return null;

        // Get all capability mappings for this system (system-scoped + org-wide)
        var mappings = await _db.CapabilityControlMappings
            .Include(m => m.SecurityCapability)
            .Where(m => m.RegisteredSystemId == systemId || m.RegisteredSystemId == null)
            .ToListAsync(cancellationToken);

        // Group by capability
        var capabilityGroups = mappings
            .GroupBy(m => m.SecurityCapabilityId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Also include capabilities linked via SystemCapabilityLink that have no mappings yet
        var linkedCapIds = await _db.SystemCapabilityLinks
            .Where(l => l.RegisteredSystemId == systemId)
            .Select(l => l.SecurityCapabilityId)
            .ToListAsync(cancellationToken);

        var missingCapIds = linkedCapIds.Except(capabilityGroups.Keys).ToList();
        if (missingCapIds.Count > 0)
        {
            var linkedCaps = await _db.SecurityCapabilities
                .Where(c => missingCapIds.Contains(c.Id))
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            foreach (var lc in linkedCaps)
                capabilityGroups[lc.Id] = new List<CapabilityControlMapping>();
        }

        // Get all control implementations for this system
        var implementations = await _db.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var implByControl = implementations.ToDictionary(i => i.ControlId);

        var capabilities = new List<CapabilityCoverageDto>();

        foreach (var kvp in capabilityGroups)
        {
            var capId = kvp.Key;
            var groupMappings = kvp.Value;

            // Resolve the capability entity
            SecurityCapability? cap;
            if (groupMappings.Count > 0 && groupMappings[0].SecurityCapability is not null)
            {
                cap = groupMappings[0].SecurityCapability;
            }
            else
            {
                cap = await _db.SecurityCapabilities
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == capId, cancellationToken);
                if (cap is null) continue;
            }

            var controlIds = groupMappings.Select(m => m.ControlId).Distinct().ToList();
            var primaryRole = groupMappings.Count > 0
                ? groupMappings.OrderByDescending(m => m.Role).First().Role
                : CapabilityMappingRole.Shared;

            // Narrative status
            int populated = 0, custom = 0, empty = 0, aiGenerated = 0;
            foreach (var controlId in controlIds)
            {
                if (implByControl.TryGetValue(controlId, out var impl))
                {
                    if (impl.IsManuallyCustomized)
                        custom++;
                    else if (!string.IsNullOrEmpty(impl.Narrative))
                        populated++;
                    else
                        empty++;

                    if (impl.AiSuggested) aiGenerated++;
                }
                else
                {
                    empty++;
                }
            }

            // Linked components for this system (deduplicate when a component has multiple boundary assignments)
            var components = (await _db.ComponentCapabilityLinks
                .Where(cl => cl.SecurityCapabilityId == cap.Id)
                .Join(_db.ComponentSystemAssignments.Where(a => a.RegisteredSystemId == systemId),
                    cl => cl.SystemComponentId,
                    a => a.SystemComponentId,
                    (cl, a) => new { cl.SystemComponent, a.AuthorizationBoundaryDefinition, a.AuthorizationBoundaryDefinitionId })
                .Select(x => new CoverageComponentDto
                {
                    ComponentId = x.SystemComponent.Id,
                    Name = x.SystemComponent.Name,
                    ComponentType = x.SystemComponent.ComponentType.ToString(),
                    Owner = x.SystemComponent.Owner,
                    Status = x.SystemComponent.Status.ToString(),
                    BoundaryName = x.AuthorizationBoundaryDefinition != null ? x.AuthorizationBoundaryDefinition.Name : null,
                    BoundaryDefinitionId = x.AuthorizationBoundaryDefinitionId,
                })
                .ToListAsync(cancellationToken))
                .GroupBy(c => c.ComponentId)
                .Select(g => g.First())
                .ToList();

            capabilities.Add(new CapabilityCoverageDto
            {
                CapabilityId = cap.Id,
                CapabilityName = cap.Name,
                Provider = cap.Provider,
                Category = cap.Category,
                ImplementationStatus = cap.ImplementationStatus.ToString(),
                Owner = cap.Owner,
                Role = primaryRole.ToString(),
                MappedControlCount = controlIds.Count,
                NarrativeStatus = new NarrativeStatusDto
                {
                    Populated = populated,
                    Custom = custom,
                    Empty = empty,
                    AiGenerated = aiGenerated,
                },
                Components = components,
            });
        }

        // Sort: Primary first, then Supporting, then Shared
        capabilities = capabilities
            .OrderByDescending(c => c.Role == "Primary")
            .ThenByDescending(c => c.Role == "Supporting")
            .ThenBy(c => c.CapabilityName)
            .ToList();

        var totalMapped = capabilities.Sum(c => c.MappedControlCount);
        var totalPopulated = capabilities.Sum(c => c.NarrativeStatus.Populated);
        var totalCustom = capabilities.Sum(c => c.NarrativeStatus.Custom);
        var totalEmpty = capabilities.Sum(c => c.NarrativeStatus.Empty);
        var totalNarratives = totalPopulated + totalCustom + totalEmpty;

        return new CapabilityCoverageResponse
        {
            SystemId = systemId,
            SystemName = system.Name,
            Capabilities = capabilities,
            Summary = new CoverageSummaryDto
            {
                TotalCapabilities = capabilities.Count,
                TotalMappedControls = totalMapped,
                TotalNarrativesPopulated = totalPopulated,
                TotalNarrativesCustom = totalCustom,
                TotalNarrativesEmpty = totalEmpty,
                CoveragePercent = totalNarratives > 0
                    ? Math.Round((totalPopulated + totalCustom) * 100.0 / totalNarratives, 1)
                    : 0,
            },
        };
    }

    // ─── Delete (continued) ──────────────────────────────────────────────────

    /// <summary>
    /// Deletes a capability, nulls out affected ControlImplementation FKs, and creates audit events.
    /// Returns null if not found.
    /// </summary>
    public async Task<DeleteCapabilityResponse?> DeleteCapabilityAsync(
        string id,
        string deletedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.SecurityCapabilities
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (entity is null) return null;

        var affectedImpls = await _db.ControlImplementations
            .Where(ci => ci.SecurityCapabilityId == id)
            .ToListAsync(cancellationToken);

        foreach (var impl in affectedImpls)
        {
            impl.SecurityCapabilityId = null;
            _db.DashboardActivities.Add(new DashboardActivity
            {
                RegisteredSystemId = impl.RegisteredSystemId,
                EventType = "CapabilityDeleted",
                Actor = deletedBy,
                Summary = $"Capability '{entity.Name}' deleted — narrative for {impl.ControlId} flagged for review",
                RelatedEntityType = "ControlImplementation",
                RelatedEntityId = impl.Id,
            });
        }

        // Remove mappings
        var mappings = await _db.CapabilityControlMappings
            .Where(m => m.SecurityCapabilityId == id)
            .ToListAsync(cancellationToken);
        _db.CapabilityControlMappings.RemoveRange(mappings);

        _db.SecurityCapabilities.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);

        // Feature 044: Re-derive org defaults after capability deletion
        await _orgInheritanceService.DeriveOrgDefaultsAsync(deletedBy, cancellationToken);

        _logger.LogInformation(
            "Deleted capability {CapabilityId} '{Name}': {Count} narratives flagged for review",
            id, entity.Name, affectedImpls.Count);

        return new DeleteCapabilityResponse
        {
            DeletedId = id,
            AffectedNarratives = affectedImpls.Count,
            Message = $"Capability deleted. {affectedImpls.Count} control narratives flagged for review.",
        };
    }

    // ─── AI Regeneration ──────────────────────────────────────────────────────

    /// <summary>
    /// Regenerates a single control narrative using AI for a specific system/control.
    /// Returns the updated narrative text, or null if not found.
    /// Returns (null, "AI_NOT_ENABLED") if AI is not available.
    /// </summary>
    public async Task<(string? Narrative, string? ErrorCode)> RegenerateNarrativeWithAiAsync(
        string systemId,
        string controlId,
        string modifiedBy,
        CancellationToken cancellationToken = default)
    {
        var impl = await _db.ControlImplementations
            .FirstOrDefaultAsync(ci => ci.RegisteredSystemId == systemId && ci.ControlId == controlId,
                cancellationToken);
        if (impl is null) return (null, "NOT_FOUND");

        var capId = impl.SecurityCapabilityId;
        var cap = capId is not null
            ? await _db.SecurityCapabilities
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == capId, cancellationToken)
            : null;

        string? narrative;
        bool aiGenerated;

        if (cap is not null)
        {
            // Capability-based regeneration path
            var nist = await _db.NistControls
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == controlId, cancellationToken);
            var controlTitle = nist?.Title ?? controlId;

            // Query component context
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

            var boundaryName = await _db.ComponentSystemAssignments
                .Where(a => a.RegisteredSystemId == systemId && a.AuthorizationBoundaryDefinition != null)
                .Select(a => a.AuthorizationBoundaryDefinition!.Name)
                .FirstOrDefaultAsync(cancellationToken);

            // Fall back to system's first boundary definition if no assignment-level boundary
            boundaryName ??= await _db.AuthorizationBoundaryDefinitions
                .Where(b => b.RegisteredSystemId == systemId)
                .OrderByDescending(b => b.IsPrimary)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(cancellationToken);

            narrative = await _narrativeService.GenerateNarrativeWithAiAsync(
                cap.Name, cap.Provider, cap.Description,
                controlId, controlTitle,
                componentContexts.Count > 0 ? componentContexts : null,
                boundaryName,
                cancellationToken);

            // Fall back to deterministic enriched narrative when AI is not enabled
            narrative ??= _narrativeService.GenerateEnrichedNarrative(
                cap.Name, cap.Provider, cap.Description,
                controlId, controlTitle,
                componentContexts.Count > 0 ? componentContexts : null,
                boundaryName);

            aiGenerated = narrative != _narrativeService.GenerateEnrichedNarrative(
                cap.Name, cap.Provider, cap.Description,
                controlId, controlTitle,
                componentContexts.Count > 0 ? componentContexts : null,
                boundaryName);
        }
        else
        {
            // No capability linked — use generic template regeneration
            var system = await _db.RegisteredSystems
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken);
            if (system is null) return (null, "NOT_FOUND");

            var family = controlId.Contains('-')
                ? controlId[..controlId.IndexOf('-')]
                : controlId.Length >= 2 ? controlId[..2] : controlId;

            var nist = await _db.NistControls
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == controlId, cancellationToken);
            var controlTitle = nist?.Title ?? controlId;

            narrative = $"The {system.Name} system implements {controlTitle} ({controlId}) " +
                $"within the {system.HostingEnvironment ?? "designated"} environment. " +
                "[No security capability is currently linked to this control. " +
                "Assign a capability on the Capabilities page to generate an enriched narrative.]";
            aiGenerated = false;
        }

        // Save old narrative as NarrativeVersion
        var previousNarrative = impl.Narrative;
        if (previousNarrative is not null)
        {
            _db.NarrativeVersions.Add(new NarrativeVersion
            {
                ControlImplementationId = impl.Id,
                VersionNumber = impl.CurrentVersion,
                Content = previousNarrative,
                AuthoredBy = modifiedBy,
                ChangeReason = aiGenerated
                    ? "AI regeneration requested by user"
                    : "Deterministic regeneration requested by user",
            });
            impl.CurrentVersion++;
        }

        impl.Narrative = narrative;
        impl.AiSuggested = aiGenerated;
        impl.ModifiedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "{Mode} narrative for system {SystemId} control {ControlId}",
            aiGenerated ? "AI-regenerated" : "Deterministic-regenerated",
            systemId, controlId);

        return (narrative, null);
    }

    /// <summary>
    /// Bulk-regenerates all narratives linked to a capability for a given system.
    /// Skips manually customized narratives. Uses AI if available, else deterministic.
    /// </summary>
    public async Task<BulkRegenerateResult?> BulkRegenerateNarrativesForCapabilityAsync(
        string systemId,
        string capabilityId,
        string modifiedBy,
        CancellationToken cancellationToken = default)
    {
        var systemExists = await _db.RegisteredSystems
            .AnyAsync(s => s.Id == systemId && s.IsActive, cancellationToken);
        if (!systemExists) return null;

        var cap = await _db.SecurityCapabilities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == capabilityId, cancellationToken);
        if (cap is null) return null;

        // Find all control implementations for this capability + system
        var impls = await _db.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId && ci.SecurityCapabilityId == capabilityId)
            .ToListAsync(cancellationToken);

        if (impls.Count == 0)
            return new BulkRegenerateResult { TotalControls = 0 };

        // Gather component context once (shared across all controls)
        var componentContexts = await _db.ComponentCapabilityLinks
            .Where(cl => cl.SecurityCapabilityId == capabilityId)
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

        var boundaryName = await _db.ComponentSystemAssignments
            .Where(a => a.RegisteredSystemId == systemId && a.AuthorizationBoundaryDefinition != null)
            .Select(a => a.AuthorizationBoundaryDefinition!.Name)
            .FirstOrDefaultAsync(cancellationToken);

        boundaryName ??= await _db.AuthorizationBoundaryDefinitions
            .Where(b => b.RegisteredSystemId == systemId)
            .OrderByDescending(b => b.IsPrimary)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(cancellationToken);

        int regenerated = 0, skippedCustom = 0, failed = 0;
        var regeneratedControlIds = new List<string>();

        foreach (var impl in impls)
        {
            if (impl.IsManuallyCustomized)
            {
                skippedCustom++;
                continue;
            }

            var controlId = impl.ControlId;
            var nist = await _db.NistControls
                .AsNoTracking()
                .FirstOrDefaultAsync(n => n.Id == controlId, cancellationToken);
            var controlTitle = nist?.Title ?? controlId;

            // Try AI first, fall back to deterministic
            string? narrative = await _narrativeService.GenerateNarrativeWithAiAsync(
                cap.Name, cap.Provider, cap.Description,
                controlId, controlTitle,
                componentContexts.Count > 0 ? componentContexts : null,
                boundaryName,
                cancellationToken);

            narrative ??= _narrativeService.GenerateEnrichedNarrative(
                cap.Name, cap.Provider, cap.Description,
                controlId, controlTitle,
                componentContexts.Count > 0 ? componentContexts : null,
                boundaryName);

            // Save version history
            var previousNarrative = impl.Narrative;
            if (previousNarrative is not null)
            {
                _db.NarrativeVersions.Add(new NarrativeVersion
                {
                    ControlImplementationId = impl.Id,
                    VersionNumber = impl.CurrentVersion,
                    Content = previousNarrative,
                    AuthoredBy = modifiedBy,
                    ChangeReason = "Bulk regeneration for capability",
                });
                impl.CurrentVersion++;
            }

            impl.Narrative = narrative;
            impl.AiSuggested = true;
            impl.ModifiedAt = DateTime.UtcNow;
            regenerated++;
            regeneratedControlIds.Add(controlId);
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Bulk regenerated {Regenerated} narratives for capability {CapabilityId} in system {SystemId} (skipped {Skipped} custom)",
            regenerated, capabilityId, systemId, skippedCustom);

        return new BulkRegenerateResult
        {
            TotalControls = impls.Count,
            Regenerated = regenerated,
            SkippedCustom = skippedCustom,
            Failed = failed,
            RegeneratedControlIds = regeneratedControlIds,
        };
    }

    // ─── Mappings ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all control mappings for a given capability.
    /// </summary>
    public async Task<CapabilityMappingsResponse?> GetMappingsAsync(
        string capabilityId,
        CancellationToken cancellationToken = default)
    {
        var cap = await _db.SecurityCapabilities
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == capabilityId, cancellationToken);

        if (cap is null) return null;

        var mappings = await _db.CapabilityControlMappings
            .Where(m => m.SecurityCapabilityId == capabilityId)
            .Include(m => m.RegisteredSystem)
            .Include(m => m.AuthorizationBoundaryDefinition)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var controlIds = mappings.Select(m => m.ControlId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var controlIdLookup = controlIds
            .SelectMany(GetControlIdVariants)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var nistControls = await _db.NistControls
            .Where(n => controlIdLookup.Contains(n.Id))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var nistById = nistControls.ToDictionary(n => NormalizeControlId(n.Id), n => n, StringComparer.OrdinalIgnoreCase);

        // Get implementation status for narrative status
        var implStatuses = await _db.ControlImplementations
            .Where(ci => ci.SecurityCapabilityId == capabilityId && controlIdLookup.Contains(ci.ControlId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var implByControl = implStatuses
            .GroupBy(ci => NormalizeControlId(ci.ControlId))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var dtos = mappings.Select(m =>
        {
            var nist = ResolveByVariants(nistById, m.ControlId);
            var implCandidates = GetControlIdVariants(m.ControlId)
                .SelectMany(v => implByControl.GetValueOrDefault(v) ?? [])
                .ToList();
            var impl = implCandidates
                .FirstOrDefault(ci => m.RegisteredSystemId == null || ci.RegisteredSystemId == m.RegisteredSystemId);

            var narrativeStatus = impl switch
            {
                { IsManuallyCustomized: true } => "Customized",
                { Narrative: not null and not "" } => "Populated",
                _ => "Empty",
            };

            return new CapabilityMappingDto
            {
                Id = m.Id,
                ControlId = FormatControlIdForDisplay(nist?.Id ?? m.ControlId),
                ControlTitle = nist?.Title,
                ControlFamily = nist?.Family ?? (m.ControlId.Contains('-') ? m.ControlId.Split('-')[0] : null),
                Role = m.Role.ToString(),
                RegisteredSystemId = m.RegisteredSystemId,
                RegisteredSystemName = m.RegisteredSystem?.Name,
                BoundaryDefinitionId = m.AuthorizationBoundaryDefinitionId,
                BoundaryDefinitionName = m.AuthorizationBoundaryDefinition?.Name,
                NarrativeStatus = narrativeStatus,
                IsManuallyCustomized = impl?.IsManuallyCustomized ?? false,
            };
        }).ToList();

        return new CapabilityMappingsResponse
        {
            CapabilityId = capabilityId,
            CapabilityName = cap.Name,
            Mappings = dtos,
            TotalMappings = dtos.Count,
        };
    }

    /// <summary>
    /// Creates control mappings for a capability and generates narratives.
    /// Returns null if capability not found.
    /// </summary>
    public async Task<CreateMappingsResponse?> CreateMappingsAsync(
        string capabilityId,
        CreateMappingsRequest request,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        var cap = await _db.SecurityCapabilities
            .FirstOrDefaultAsync(c => c.Id == capabilityId, cancellationToken);

        if (cap is null) return null;

        // Validate control IDs against full catalog while supporting v4/v5 enhancement styles.
        var catalogControls = await _db.NistControls
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var validControls = catalogControls.ToDictionary(n => NormalizeControlId(n.Id), n => n, StringComparer.OrdinalIgnoreCase);

        var warnings = new List<MappingWarning>();
        var created = 0;
        var narrativesGenerated = 0;

        foreach (var item in request.Mappings)
        {
            var normalizedControlId = ResolveCatalogControlId(item.ControlId, validControls);
            if (normalizedControlId is null)
            {
                warnings.Add(new MappingWarning
                {
                    ControlId = item.ControlId,
                    Message = $"Control '{item.ControlId}' not found in NIST control catalog — skipped",
                });
                continue;
            }

            var duplicateMappingExists = await _db.CapabilityControlMappings
                .AnyAsync(m =>
                    m.SecurityCapabilityId == capabilityId &&
                    GetControlIdVariants(normalizedControlId).Contains(m.ControlId) &&
                    m.RegisteredSystemId == item.RegisteredSystemId,
                    cancellationToken);

            if (duplicateMappingExists)
            {
                warnings.Add(new MappingWarning
                {
                    ControlId = item.ControlId,
                    Message = $"Mapping for control '{item.ControlId}' already exists for this capability and scope — skipped",
                });
                continue;
            }

            if (!Enum.TryParse<CapabilityMappingRole>(item.Role, ignoreCase: true, out var role))
                role = CapabilityMappingRole.Supporting;

            // Check duplicate primary
            if (role == CapabilityMappingRole.Primary)
            {
                var existingPrimary = await _db.CapabilityControlMappings
                    .Include(m => m.SecurityCapability)
                    .FirstOrDefaultAsync(m =>
                        GetControlIdVariants(normalizedControlId).Contains(m.ControlId) &&
                        m.RegisteredSystemId == item.RegisteredSystemId &&
                        m.Role == CapabilityMappingRole.Primary &&
                        m.SecurityCapabilityId != capabilityId,
                        cancellationToken);

                if (existingPrimary != null)
                {
                    warnings.Add(new MappingWarning
                    {
                        ControlId = item.ControlId,
                        Message = $"Another capability '{existingPrimary.SecurityCapability.Name}' already claims Primary role for {item.ControlId}",
                    });
                    continue;
                }
            }

            var mapping = new CapabilityControlMapping
            {
                SecurityCapabilityId = capabilityId,
                ControlId = normalizedControlId,
                RegisteredSystemId = item.RegisteredSystemId,
                AuthorizationBoundaryDefinitionId = item.BoundaryDefinitionId,
                Role = role,
                CreatedBy = createdBy,
            };

            _db.CapabilityControlMappings.Add(mapping);
            created++;

            // Generate narrative for matching ControlImplementation(s)
            var nist = validControls[normalizedControlId];
            var targetSystems = item.RegisteredSystemId != null
                ? new List<string> { item.RegisteredSystemId }
                : await _db.RegisteredSystems
                    .Where(s => s.IsActive)
                    .Select(s => s.Id)
                    .ToListAsync(cancellationToken);

            foreach (var sysId in targetSystems)
            {
                var impl = await _db.ControlImplementations
                    .FirstOrDefaultAsync(ci =>
                        ci.RegisteredSystemId == sysId &&
                        GetControlIdVariants(normalizedControlId).Contains(ci.ControlId),
                        cancellationToken);

                if (impl is null)
                {
                    impl = new ControlImplementation
                    {
                        RegisteredSystemId = sysId,
                        ControlId = normalizedControlId,
                        SecurityCapabilityId = capabilityId,
                        AuthoredBy = createdBy,
                    };
                    _db.ControlImplementations.Add(impl);
                }
                else if (impl.IsManuallyCustomized)
                {
                    impl.ControlId = normalizedControlId;
                    // Link but don't overwrite customized narrative
                    if (impl.SecurityCapabilityId == null)
                        impl.SecurityCapabilityId = capabilityId;
                    continue;
                }
                else
                {
                    impl.ControlId = normalizedControlId;
                    impl.SecurityCapabilityId = capabilityId;
                }

                // Query component context for enriched narratives (Feature 036)
                var componentContexts = await _db.ComponentCapabilityLinks
                    .Where(cl => cl.SecurityCapabilityId == capabilityId)
                    .Join(_db.ComponentSystemAssignments.Where(a => a.RegisteredSystemId == sysId),
                        cl => cl.SystemComponentId,
                        a => a.SystemComponentId,
                        (cl, a) => new { cl.SystemComponent, a.AuthorizationBoundaryDefinition })
                    .Select(x => new ComponentContext(
                        x.SystemComponent.Name,
                        x.SystemComponent.ComponentType.ToString(),
                        x.SystemComponent.Owner,
                        x.SystemComponent.PersonName))
                    .ToListAsync(cancellationToken);

                var boundary = await _db.ComponentSystemAssignments
                    .Where(a => a.RegisteredSystemId == sysId)
                    .Select(a => a.AuthorizationBoundaryDefinition)
                    .FirstOrDefaultAsync(cancellationToken);

                var boundaryName = boundary?.Name;
                if (boundaryName is null && item.BoundaryDefinitionId is not null)
                {
                    boundaryName = await _db.AuthorizationBoundaryDefinitions
                        .Where(b => b.Id == item.BoundaryDefinitionId)
                        .Select(b => b.Name)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                // Fall back to system's first boundary definition if no assignment-level boundary
                boundaryName ??= await _db.AuthorizationBoundaryDefinitions
                    .Where(b => b.RegisteredSystemId == sysId)
                    .OrderByDescending(b => b.IsPrimary)
                    .Select(b => b.Name)
                    .FirstOrDefaultAsync(cancellationToken);

                // Try AI-assisted generation for single narratives, fall back to deterministic
                string? narrative = null;
                try
                {
                    narrative = await _narrativeService.GenerateNarrativeWithAiAsync(
                        cap.Name, cap.Provider, cap.Description,
                        normalizedControlId, nist.Title,
                        componentContexts.Count > 0 ? componentContexts : null,
                        boundaryName,
                        cancellationToken);
                }
                catch
                {
                    // AI generation failed, fall through to deterministic
                }

                if (narrative is not null && !string.IsNullOrWhiteSpace(narrative))
                {
                    impl.Narrative = narrative;
                    impl.AiSuggested = true;
                }
                else
                {
                    impl.Narrative = _narrativeService.GenerateEnrichedNarrative(
                        cap.Name, cap.Provider, cap.Description,
                        normalizedControlId, nist.Title,
                        componentContexts.Count > 0 ? componentContexts : null,
                        boundaryName);
                }

                impl.IsAutoPopulated = true;
                impl.ModifiedAt = DateTime.UtcNow;
                narrativesGenerated++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Feature 044: Re-derive org-level inheritance defaults after mapping changes
        await _orgInheritanceService.DeriveOrgDefaultsAsync("system", cancellationToken);

        _logger.LogInformation(
            "Created {Created} mappings for capability {CapabilityId}, generated {Narratives} narratives, {Warnings} warnings",
            created, capabilityId, narrativesGenerated, warnings.Count);

        return new CreateMappingsResponse
        {
            Created = created,
            Warnings = warnings,
            NarrativesGenerated = narrativesGenerated,
        };
    }

    /// <summary>
    /// Updates a single mapping for a capability.
    /// Returns null when the capability or mapping does not exist.
    /// </summary>
    public async Task<CapabilityMappingDto?> UpdateMappingAsync(
        string capabilityId,
        string mappingId,
        UpdateMappingRequest request,
        string modifiedBy,
        CancellationToken cancellationToken = default)
    {
        var capabilityExists = await _db.SecurityCapabilities
            .AnyAsync(c => c.Id == capabilityId, cancellationToken);
        if (!capabilityExists)
            return null;

        var mapping = await _db.CapabilityControlMappings
            .Include(m => m.SecurityCapability)
            .FirstOrDefaultAsync(m => m.Id == mappingId && m.SecurityCapabilityId == capabilityId, cancellationToken);
        if (mapping is null)
            return null;

        if (!string.IsNullOrWhiteSpace(request.ControlId))
        {
            var catalogControls = await _db.NistControls
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            var controlsById = catalogControls.ToDictionary(n => NormalizeControlId(n.Id), n => n, StringComparer.OrdinalIgnoreCase);
            var normalizedControlId = ResolveCatalogControlId(request.ControlId, controlsById);
            if (normalizedControlId is null)
                throw new InvalidOperationException($"Control '{request.ControlId}' not found in NIST control catalog");

            mapping.ControlId = normalizedControlId;
        }

        if (!string.IsNullOrWhiteSpace(request.Role) &&
            Enum.TryParse<CapabilityMappingRole>(request.Role, ignoreCase: true, out var parsedRole))
        {
            mapping.Role = parsedRole;
        }

        if (request.RegisteredSystemId is not null)
        {
            mapping.RegisteredSystemId = string.IsNullOrWhiteSpace(request.RegisteredSystemId)
                ? null
                : request.RegisteredSystemId;
        }

        if (request.BoundaryDefinitionId is not null)
        {
            mapping.AuthorizationBoundaryDefinitionId = string.IsNullOrWhiteSpace(request.BoundaryDefinitionId)
                ? null
                : request.BoundaryDefinitionId;
        }

        if (mapping.Role == CapabilityMappingRole.Primary)
        {
            var existingPrimary = await _db.CapabilityControlMappings
                .Include(m => m.SecurityCapability)
                .FirstOrDefaultAsync(m =>
                    m.Id != mapping.Id &&
                    GetControlIdVariants(mapping.ControlId).Contains(m.ControlId) &&
                    m.RegisteredSystemId == mapping.RegisteredSystemId &&
                    m.Role == CapabilityMappingRole.Primary,
                    cancellationToken);

            if (existingPrimary is not null)
            {
                throw new InvalidOperationException(
                    $"Another capability '{existingPrimary.SecurityCapability.Name}' already claims Primary role for {mapping.ControlId}");
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _orgInheritanceService.DeriveOrgDefaultsAsync(modifiedBy, cancellationToken);

        var updated = await GetMappingsAsync(capabilityId, cancellationToken);
        return updated?.Mappings.FirstOrDefault(m => m.Id == mappingId);
    }

    private static string NormalizeControlId(string controlId)
    {
        return controlId.Trim().ToLowerInvariant();
    }

    private static IEnumerable<string> GetControlIdVariants(string controlId)
    {
        var normalized = NormalizeControlId(controlId);
        yield return normalized;

        if (normalized.Contains('('))
        {
            var dotted = ParenthesizedEnhancementRegex.Replace(normalized, ".$1");
            if (!string.Equals(dotted, normalized, StringComparison.OrdinalIgnoreCase))
                yield return dotted;
        }

        var dotParts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (dotParts.Length > 1)
        {
            var parenthesized = dotParts[0] + string.Concat(dotParts.Skip(1).Select(p => $"({p})"));
            if (!string.Equals(parenthesized, normalized, StringComparison.OrdinalIgnoreCase))
                yield return parenthesized;
        }
    }

    private static string? ResolveCatalogControlId(
        string controlId,
        IDictionary<string, NistControl> controlsById)
    {
        foreach (var candidate in GetControlIdVariants(controlId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (controlsById.TryGetValue(candidate, out var control))
                return NormalizeControlId(control.Id);
        }

        return null;
    }

    private static T? ResolveByVariants<T>(
        IDictionary<string, T> dictionary,
        string controlId)
        where T : class
    {
        foreach (var candidate in GetControlIdVariants(controlId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (dictionary.TryGetValue(candidate, out var value))
                return value;
        }

        return null;
    }

    private static string FormatControlIdForDisplay(string controlId)
    {
        return controlId.ToUpperInvariant();
    }

    /// <summary>
    /// Deletes a single mapping for a capability.
    /// </summary>
    public async Task<bool> DeleteMappingAsync(
        string capabilityId,
        string mappingId,
        string modifiedBy,
        CancellationToken cancellationToken = default)
    {
        var capabilityExists = await _db.SecurityCapabilities
            .AnyAsync(c => c.Id == capabilityId, cancellationToken);
        if (!capabilityExists)
            return false;

        var mapping = await _db.CapabilityControlMappings
            .FirstOrDefaultAsync(m => m.Id == mappingId && m.SecurityCapabilityId == capabilityId, cancellationToken);
        if (mapping is null)
            return false;

        _db.CapabilityControlMappings.Remove(mapping);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            var dbMessage = ex.InnerException is DbException dbEx
                ? dbEx.Message
                : ex.InnerException?.Message ?? ex.Message;

            _logger.LogError(
                ex,
                "Failed deleting capability mapping {MappingId} for capability {CapabilityId}.",
                mappingId,
                capabilityId);

            throw new InvalidOperationException(
                $"Unable to delete this mapping because it is referenced by other records. {dbMessage}",
                ex);
        }

        try
        {
            await _orgInheritanceService.DeriveOrgDefaultsAsync(modifiedBy, cancellationToken);
        }
        catch (Exception ex)
        {
            // Mapping delete already persisted. Keep delete successful and log derivation failure for follow-up.
            _logger.LogError(
                ex,
                "Capability mapping {MappingId} deleted, but org default derivation failed.",
                mappingId);
        }

        return true;
    }

    // ─── Gap Analysis ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the set of control IDs that are covered by capability mappings
    /// for a system, combining boundary-specific and org-wide (null FK) mappings.
    /// When <paramref name="boundaryDefinitionId"/> is specified, includes
    /// boundary-specific + org-wide mappings; boundary-specific takes precedence.
    /// </summary>
    public async Task<HashSet<string>> GetCoveredControlIdsAsync(
        string systemId,
        string? boundaryDefinitionId,
        CancellationToken ct)
    {
        var query = _db.CapabilityControlMappings
            .Where(m => m.RegisteredSystemId == systemId || m.RegisteredSystemId == null);

        if (boundaryDefinitionId is not null)
        {
            // Include boundary-specific + org-wide (null FK = all boundaries)
            query = query.Where(m =>
                m.AuthorizationBoundaryDefinitionId == boundaryDefinitionId ||
                m.AuthorizationBoundaryDefinitionId == null);
        }

        var controlIds = await query
            .Select(m => m.ControlId)
            .Distinct()
            .ToListAsync(ct);

        return new HashSet<string>(controlIds, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns coverage analysis for a system's baseline — which controls have
    /// capability mappings and which are unmapped gaps.
    /// </summary>
    public async Task<GapAnalysisDto?> GetGapAnalysisAsync(
        string systemId,
        string? boundaryDefinitionId = null,
        CancellationToken cancellationToken = default)
    {
        var system = await _db.RegisteredSystems
            .Include(s => s.ControlBaseline)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, cancellationToken);

        if (system?.ControlBaseline is null) return null;

        var baseline = system.ControlBaseline;
        var controlIds = baseline.ControlIds;

        // Get all capability mappings that cover this system (filtered by boundary if specified)
        var mappedSet = await GetCoveredControlIdsAsync(systemId, boundaryDefinitionId, cancellationToken);

        // Get waived controls (approved waivers exclude controls from gap calculations)
        var waivedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (boundaryDefinitionId is not null)
        {
            var waived = await _deviationService.GetWaivedControlsForBoundaryAsync(
                systemId, boundaryDefinitionId, cancellationToken);
            foreach (var id in waived) waivedSet.Add(id);
        }

        // Effective covered = mapped by capability OR waived
        var effectiveSet = new HashSet<string>(mappedSet, StringComparer.OrdinalIgnoreCase);
        foreach (var id in waivedSet) effectiveSet.Add(id);

        // Get NIST control titles for unmapped controls
        var unmappedIds = controlIds.Where(c => !effectiveSet.Contains(c)).ToList();
        var nistControls = await _db.NistControls
            .Where(n => unmappedIds.Contains(n.Id))
            .AsNoTracking()
            .ToDictionaryAsync(n => n.Id, cancellationToken);

        // Group by family
        var familyGroups = controlIds
            .GroupBy(c => c.Contains('-') ? c.Split('-')[0].ToUpperInvariant() : c.ToUpperInvariant())
            .Where(g => ControlFamilies.FamilyNames.ContainsKey(g.Key))
            .OrderBy(g => g.Key);

        var familyBreakdown = familyGroups.Select(g =>
        {
            var total = g.Count();
            var covered = g.Count(c => effectiveSet.Contains(c));
            var waived = g.Count(c => waivedSet.Contains(c));
            var gaps = total - covered;
            var pct = total > 0 ? Math.Round(100.0 * covered / total, 1) : 0;

            return new GapFamilyBreakdownDto
            {
                FamilyCode = g.Key,
                FamilyName = ControlFamilies.FamilyNames.GetValueOrDefault(g.Key, g.Key),
                TotalControls = total,
                CoveredControls = covered,
                WaivedControls = waived,
                GapCount = gaps,
                CoveragePercent = pct,
                IsBelow50 = pct < 50,
                UnmappedControls = g
                    .Where(c => !effectiveSet.Contains(c))
                    .Select(c => new UnmappedControlDto
                    {
                        ControlId = c,
                        ControlTitle = nistControls.GetValueOrDefault(c)?.Title ?? c,
                    })
                    .OrderBy(u => u.ControlId)
                    .ToList(),
                WaivedControlIds = g.Where(c => waivedSet.Contains(c)).OrderBy(c => c).ToList(),
            };
        }).ToList();

        var totalControls = controlIds.Count;
        var totalCovered = controlIds.Count(c => effectiveSet.Contains(c));
        var totalWaived = controlIds.Count(c => waivedSet.Contains(c));

        // Build per-boundary comparison when no boundary filter is specified
        List<BoundaryComparisonItemDto>? boundaryComparison = null;
        if (boundaryDefinitionId is null)
        {
            var boundaries = await _db.AuthorizationBoundaryDefinitions
                .Where(b => b.RegisteredSystemId == systemId)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (boundaries.Count > 1)
            {
                boundaryComparison = [];
                foreach (var boundary in boundaries.OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Name))
                {
                    var bCovered = await GetCoveredControlIdsAsync(systemId, boundary.Id, cancellationToken);
                    var bWaived = await _deviationService.GetWaivedControlsForBoundaryAsync(
                        systemId, boundary.Id, cancellationToken);
                    var bEffective = new HashSet<string>(bCovered, StringComparer.OrdinalIgnoreCase);
                    foreach (var id in bWaived) bEffective.Add(id);
                    var bTotal = controlIds.Count;
                    var bCoveredCount = controlIds.Count(c => bEffective.Contains(c));
                    var bWaivedCount = controlIds.Count(c => bWaived.Contains(c, StringComparer.OrdinalIgnoreCase));
                    boundaryComparison.Add(new BoundaryComparisonItemDto
                    {
                        BoundaryId = boundary.Id,
                        BoundaryName = boundary.Name,
                        BoundaryType = boundary.BoundaryType.ToString(),
                        IsPrimary = boundary.IsPrimary,
                        TotalControls = bTotal,
                        CoveredControls = bCoveredCount,
                        WaivedControls = bWaivedCount,
                        GapCount = bTotal - bCoveredCount,
                        CoveragePercent = bTotal > 0 ? Math.Round(100.0 * bCoveredCount / bTotal, 1) : 0,
                    });
                }
            }
        }

        return new GapAnalysisDto
        {
            SystemId = systemId,
            BaselineLevel = baseline.BaselineLevel,
            TotalBaselineControls = totalControls,
            CoveredControls = totalCovered,
            WaivedControls = totalWaived,
            GapCount = totalControls - totalCovered,
            CoveragePercent = totalControls > 0
                ? Math.Round(100.0 * totalCovered / totalControls, 1) : 0,
            FamilyBreakdown = familyBreakdown,
            BoundaryComparison = boundaryComparison,
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static SecurityCapabilityDto MapToDto(
        SecurityCapability entity, int mappedControlCount, int systemsUsingCount,
        List<LinkedComponentDto>? linkedComponents = null)
    {
        return new SecurityCapabilityDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Provider = entity.Provider,
            Category = entity.Category,
            CategoryName = ControlFamilies.FamilyNames.GetValueOrDefault(entity.Category, entity.Category),
            Description = entity.Description,
            ImplementationStatus = entity.ImplementationStatus.ToString(),
            Owner = entity.Owner,
            MappedControlCount = mappedControlCount,
            SystemsUsingCount = systemsUsingCount,
            CreatedAt = entity.CreatedAt,
            ModifiedAt = entity.ModifiedAt,
            LinkedComponents = linkedComponents,
            SystemCount = systemsUsingCount,
        };
    }
}
