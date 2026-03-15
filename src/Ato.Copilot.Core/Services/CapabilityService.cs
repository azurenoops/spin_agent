using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Service for Security Capability CRUD and control-mapping operations.
/// </summary>
public class CapabilityService
{
    private readonly AtoCopilotContext _db;
    private readonly ILogger<CapabilityService> _logger;
    private readonly NarrativeTemplateService _narrativeService;

    /// <summary>Initializes a new instance of <see cref="CapabilityService"/>.</summary>
    public CapabilityService(
        AtoCopilotContext db,
        ILogger<CapabilityService> logger,
        NarrativeTemplateService narrativeService)
    {
        _db = db;
        _logger = logger;
        _narrativeService = narrativeService;
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

        var items = capabilities.Select(c => MapToDto(
            c,
            mappingCounts.FirstOrDefault(m => m.CapId == c.Id)?.Count ?? 0,
            systemCounts.FirstOrDefault(s => s.CapId == c.Id)?.Count ?? 0
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

        if (descriptionChanged)
        {
            var affectedImpls = await _db.ControlImplementations
                .Where(ci => ci.SecurityCapabilityId == id)
                .ToListAsync(cancellationToken);

            foreach (var impl in affectedImpls)
            {
                if (impl.IsManuallyCustomized)
                {
                    narrativesSkipped++;
                    _db.DashboardActivities.Add(new DashboardActivity
                    {
                        RegisteredSystemId = impl.RegisteredSystemId,
                        EventType = "CapabilityChanged",
                        Actor = modifiedBy,
                        Summary = $"Upstream capability '{entity.Name}' changed — review customized narrative for {impl.ControlId}",
                        RelatedEntityType = "ControlImplementation",
                        RelatedEntityId = impl.Id,
                    });
                    continue;
                }

                var nist = await _db.NistControls
                    .AsNoTracking()
                    .FirstOrDefaultAsync(n => n.Id == impl.ControlId, cancellationToken);

                impl.Narrative = _narrativeService.GenerateNarrative(
                    entity.Name,
                    entity.Provider,
                    entity.Description,
                    impl.ControlId,
                    nist?.Title ?? impl.ControlId);
                impl.ModifiedAt = DateTime.UtcNow;
                narrativesUpdated++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

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
        }, false);
    }

    // ─── Delete ──────────────────────────────────────────────────────────────

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
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var controlIds = mappings.Select(m => m.ControlId).Distinct().ToList();
        var nistControls = await _db.NistControls
            .Where(n => controlIds.Contains(n.Id))
            .AsNoTracking()
            .ToDictionaryAsync(n => n.Id, cancellationToken);

        // Get implementation status for narrative status
        var implStatuses = await _db.ControlImplementations
            .Where(ci => ci.SecurityCapabilityId == capabilityId && controlIds.Contains(ci.ControlId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var implByControl = implStatuses
            .GroupBy(ci => ci.ControlId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var dtos = mappings.Select(m =>
        {
            var nist = nistControls.GetValueOrDefault(m.ControlId);
            var impl = implByControl.GetValueOrDefault(m.ControlId)?
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
                ControlId = m.ControlId,
                ControlTitle = nist?.Title,
                ControlFamily = nist?.Family ?? (m.ControlId.Contains('-') ? m.ControlId.Split('-')[0] : null),
                Role = m.Role.ToString(),
                RegisteredSystemId = m.RegisteredSystemId,
                RegisteredSystemName = m.RegisteredSystem?.Name,
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

        var requestedControlIds = request.Mappings.Select(m => m.ControlId).Distinct().ToList();

        // Validate control IDs exist
        var validControls = await _db.NistControls
            .Where(n => requestedControlIds.Contains(n.Id))
            .AsNoTracking()
            .ToDictionaryAsync(n => n.Id, cancellationToken);

        var warnings = new List<MappingWarning>();
        var created = 0;
        var narrativesGenerated = 0;

        foreach (var item in request.Mappings)
        {
            if (!validControls.ContainsKey(item.ControlId))
            {
                warnings.Add(new MappingWarning
                {
                    ControlId = item.ControlId,
                    Message = $"Control '{item.ControlId}' not found in NIST control catalog — skipped",
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
                        m.ControlId == item.ControlId &&
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
                }
            }

            var mapping = new CapabilityControlMapping
            {
                SecurityCapabilityId = capabilityId,
                ControlId = item.ControlId,
                RegisteredSystemId = item.RegisteredSystemId,
                Role = role,
                CreatedBy = createdBy,
            };

            _db.CapabilityControlMappings.Add(mapping);
            created++;

            // Generate narrative for matching ControlImplementation(s)
            var nist = validControls[item.ControlId];
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
                        ci.ControlId == item.ControlId,
                        cancellationToken);

                if (impl is null)
                {
                    impl = new ControlImplementation
                    {
                        RegisteredSystemId = sysId,
                        ControlId = item.ControlId,
                        SecurityCapabilityId = capabilityId,
                        AuthoredBy = createdBy,
                    };
                    _db.ControlImplementations.Add(impl);
                }
                else if (impl.IsManuallyCustomized)
                {
                    // Link but don't overwrite customized narrative
                    if (impl.SecurityCapabilityId == null)
                        impl.SecurityCapabilityId = capabilityId;
                    continue;
                }
                else
                {
                    impl.SecurityCapabilityId = capabilityId;
                }

                impl.Narrative = _narrativeService.GenerateNarrative(
                    cap.Name,
                    cap.Provider,
                    cap.Description,
                    item.ControlId,
                    nist.Title);
                impl.IsAutoPopulated = true;
                impl.ModifiedAt = DateTime.UtcNow;
                narrativesGenerated++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

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

    // ─── Gap Analysis ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns coverage analysis for a system's baseline — which controls have
    /// capability mappings and which are unmapped gaps.
    /// </summary>
    public async Task<GapAnalysisDto?> GetGapAnalysisAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var system = await _db.RegisteredSystems
            .Include(s => s.ControlBaseline)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, cancellationToken);

        if (system?.ControlBaseline is null) return null;

        var baseline = system.ControlBaseline;
        var controlIds = baseline.ControlIds;

        // Get all capability mappings that cover this system (system-scoped + org-wide)
        var mappedControlIds = await _db.CapabilityControlMappings
            .Where(m => m.RegisteredSystemId == systemId || m.RegisteredSystemId == null)
            .Select(m => m.ControlId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var mappedSet = new HashSet<string>(mappedControlIds, StringComparer.OrdinalIgnoreCase);

        // Get NIST control titles for unmapped controls
        var unmappedIds = controlIds.Where(c => !mappedSet.Contains(c)).ToList();
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
            var covered = g.Count(c => mappedSet.Contains(c));
            var gaps = total - covered;
            var pct = total > 0 ? Math.Round(100.0 * covered / total, 1) : 0;

            return new GapFamilyBreakdownDto
            {
                FamilyCode = g.Key,
                FamilyName = ControlFamilies.FamilyNames.GetValueOrDefault(g.Key, g.Key),
                TotalControls = total,
                CoveredControls = covered,
                GapCount = gaps,
                CoveragePercent = pct,
                IsBelow50 = pct < 50,
                UnmappedControls = g
                    .Where(c => !mappedSet.Contains(c))
                    .Select(c => new UnmappedControlDto
                    {
                        ControlId = c,
                        ControlTitle = nistControls.GetValueOrDefault(c)?.Title ?? c,
                    })
                    .OrderBy(u => u.ControlId)
                    .ToList(),
            };
        }).ToList();

        var totalControls = controlIds.Count;
        var totalCovered = controlIds.Count(c => mappedSet.Contains(c));

        return new GapAnalysisDto
        {
            SystemId = systemId,
            BaselineLevel = baseline.BaselineLevel,
            TotalBaselineControls = totalControls,
            CoveredControls = totalCovered,
            GapCount = totalControls - totalCovered,
            CoveragePercent = totalControls > 0
                ? Math.Round(100.0 * totalCovered / totalControls, 1) : 0,
            FamilyBreakdown = familyBreakdown,
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static SecurityCapabilityDto MapToDto(
        SecurityCapability entity, int mappedControlCount, int systemsUsingCount)
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
        };
    }
}
