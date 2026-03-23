using System.Diagnostics;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Ato.Copilot.Mcp.Services;

/// <summary>
/// Orchestrates the full import pipeline for CSP profile and CRM imports:
/// CSP/CRM → Components → Capabilities → Control Mappings → Org Inheritance → Narratives.
/// All changes are persisted in a single <see cref="AtoCopilotContext.SaveChangesAsync"/> call.
/// </summary>
public class CapabilityImportService
{
    private readonly AtoCopilotContext _db;
    private readonly CspProfileService _cspProfileService;
    private readonly IOrgInheritanceService _orgInheritanceService;
    private readonly NarrativeTemplateService _narrativeService;
    private readonly ILogger<CapabilityImportService> _logger;

    /// <summary>
    /// Initializes the import service with all required dependencies.
    /// </summary>
    public CapabilityImportService(
        AtoCopilotContext db,
        CspProfileService cspProfileService,
        IOrgInheritanceService orgInheritanceService,
        NarrativeTemplateService narrativeService,
        ILogger<CapabilityImportService> logger)
    {
        _db = db;
        _cspProfileService = cspProfileService;
        _orgInheritanceService = orgInheritanceService;
        _narrativeService = narrativeService;
        _logger = logger;
    }

    // ─── Dedup Helpers (T007) ───────────────────────────────────────────────

    /// <summary>
    /// Finds an existing org-wide Thing component by name, or creates a new one.
    /// Dedup key: (Name, ComponentType == Thing, RegisteredSystemId == null).
    /// </summary>
    public async Task<SystemComponent> FindOrCreateComponentAsync(
        string name, string? description, CancellationToken ct = default)
    {
        var existing = await _db.SystemComponents
            .FirstOrDefaultAsync(c =>
                c.Name == name
                && c.ComponentType == ComponentType.Thing
                && c.RegisteredSystemId == null, ct);

        if (existing is not null) return existing;

        var component = new SystemComponent
        {
            Name = name,
            ComponentType = ComponentType.Thing,
            Description = description,
            Status = ComponentStatus.Active,
            CreatedBy = "capability-import",
        };
        _db.SystemComponents.Add(component);
        return component;
    }

    /// <summary>
    /// Finds an existing capability by (Name, Provider) case-insensitive, or creates a new one.
    /// </summary>
    public async Task<SecurityCapability> FindOrCreateCapabilityAsync(
        string name, string provider, string category, string description,
        CancellationToken ct = default)
    {
        var existing = await _db.SecurityCapabilities
            .FirstOrDefaultAsync(c =>
                c.Name.ToLower() == name.ToLower()
                && c.Provider.ToLower() == provider.ToLower(), ct);

        if (existing is not null) return existing;

        var capability = new SecurityCapability
        {
            Name = name,
            Provider = provider,
            Category = category,
            Description = description,
            ImplementationStatus = CapabilityStatus.Implemented,
            Owner = "capability-import",
            CreatedBy = "capability-import",
        };
        _db.SecurityCapabilities.Add(capability);
        return capability;
    }

    // ─── CSP Import Pipeline (T008, T009) ───────────────────────────────────

    /// <summary>
    /// Imports a CSP profile through the full capabilities pipeline.
    /// Creates components, capabilities, control mappings, org inheritance defaults, and narratives.
    /// </summary>
    public async Task<CspImportResult> ImportCspProfileAsync(
        string profileId, string conflictResolution = "skip", CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var profile = _cspProfileService.GetProfile(profileId);
        if (profile is null)
            throw new KeyNotFoundException($"CSP profile '{profileId}' not found");

        _logger.LogInformation("Starting CSP profile import: {ProfileName}", profile.Name);

        // Load NIST control titles for narrative generation
        var nistControls = await _db.NistControls
            .ToDictionaryAsync(n => n.Id, n => n.Title, StringComparer.OrdinalIgnoreCase, ct);

        var componentsCreated = 0;
        var componentsReused = 0;
        var capabilitiesCreated = 0;
        var capabilitiesReused = 0;
        var controlMappingsCreated = 0;
        var conflicts = 0;
        var skipped = 0;

        // Process each service in the profile
        foreach (var service in profile.Services)
        {
            _logger.LogInformation("Processing service: {ServiceName} ({ControlCount} controls)",
                service.Name, service.Controls.Count);

            // 1. Find or create component for this service
            var component = await FindOrCreateComponentAsync(service.Name, service.Description, ct);
            if (_db.Entry(component).State == EntityState.Added)
                componentsCreated++;
            else
                componentsReused++;

            // 2. Derive category code from controls (first control's family prefix)
            var categoryCode = service.Controls.Count > 0
                ? ExtractFamilyCode(service.Controls[0].ControlId)
                : "XX";

            // 3. Find or create capability: "{Provider} / {Category}"
            var capabilityName = $"{service.Name} / {service.Category}";
            var capability = await FindOrCreateCapabilityAsync(
                capabilityName, profile.Provider, categoryCode, service.Description, ct);

            if (_db.Entry(capability).State == EntityState.Added)
                capabilitiesCreated++;
            else
                capabilitiesReused++;

            // 4. Link component to capability
            var linkExists = await _db.ComponentCapabilityLinks
                .AnyAsync(l => l.SystemComponentId == component.Id
                    && l.SecurityCapabilityId == capability.Id, ct);
            if (!linkExists)
            {
                _db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                {
                    SystemComponentId = component.Id,
                    SecurityCapabilityId = capability.Id,
                });
            }

            // 5. Create control mappings
            foreach (var control in service.Controls)
            {
                // Check for existing Primary mapping from another capability
                var existingPrimary = await _db.CapabilityControlMappings
                    .AnyAsync(m => m.ControlId.ToLower() == control.ControlId.ToLower()
                        && m.RegisteredSystemId == null
                        && m.Role == CapabilityMappingRole.Primary
                        && m.SecurityCapabilityId != capability.Id, ct);

                var role = existingPrimary ? CapabilityMappingRole.Supporting : CapabilityMappingRole.Primary;

                if (existingPrimary && conflictResolution == "skip")
                {
                    // Check if this exact mapping exists
                    var existingMapping = await _db.CapabilityControlMappings
                        .AnyAsync(m => m.SecurityCapabilityId == capability.Id
                            && m.ControlId.ToLower() == control.ControlId.ToLower()
                            && m.RegisteredSystemId == null, ct);
                    if (existingMapping)
                    {
                        skipped++;
                        continue;
                    }
                    conflicts++;
                }

                // Check for duplicate mapping
                var dupMapping = await _db.CapabilityControlMappings
                    .AnyAsync(m => m.SecurityCapabilityId == capability.Id
                        && m.ControlId.ToLower() == control.ControlId.ToLower()
                        && m.RegisteredSystemId == null, ct);
                if (dupMapping) continue;

                var mapping = new CapabilityControlMapping
                {
                    SecurityCapabilityId = capability.Id,
                    ControlId = control.ControlId,
                    RegisteredSystemId = null,
                    Role = role,
                    CreatedBy = "capability-import",
                };
                _db.CapabilityControlMappings.Add(mapping);
                controlMappingsCreated++;
            }
        }

        // If the profile has no services[] but has flat controls[], handle gracefully
        if (profile.Services.Count == 0 && profile.Controls.Count > 0)
        {
            _logger.LogInformation("Profile uses flat controls format, creating single capability group");
            var component = await FindOrCreateComponentAsync(profile.Provider, profile.Description, ct);
            if (_db.Entry(component).State == EntityState.Added)
                componentsCreated++;
            else
                componentsReused++;

            var capability = await FindOrCreateCapabilityAsync(
                profile.Name, profile.Provider, "XX", profile.Description, ct);
            if (_db.Entry(capability).State == EntityState.Added)
                capabilitiesCreated++;
            else
                capabilitiesReused++;

            var linkExists = await _db.ComponentCapabilityLinks
                .AnyAsync(l => l.SystemComponentId == component.Id
                    && l.SecurityCapabilityId == capability.Id, ct);
            if (!linkExists)
            {
                _db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                {
                    SystemComponentId = component.Id,
                    SecurityCapabilityId = capability.Id,
                });
            }

            foreach (var control in profile.Controls)
            {
                var dupMapping = await _db.CapabilityControlMappings
                    .AnyAsync(m => m.SecurityCapabilityId == capability.Id
                        && m.ControlId.ToLower() == control.ControlId.ToLower()
                        && m.RegisteredSystemId == null, ct);
                if (dupMapping) continue;

                _db.CapabilityControlMappings.Add(new CapabilityControlMapping
                {
                    SecurityCapabilityId = capability.Id,
                    ControlId = control.ControlId,
                    RegisteredSystemId = null,
                    Role = CapabilityMappingRole.Primary,
                    CreatedBy = "capability-import",
                });
                controlMappingsCreated++;
            }
        }

        // 6. Persist all changes (single transaction)
        _logger.LogInformation("Saving import: {Components} components, {Capabilities} capabilities, {Mappings} mappings",
            componentsCreated, capabilitiesCreated, controlMappingsCreated);
        await _db.SaveChangesAsync(ct);

        // 7. Generate narratives for new mappings
        var narrativesGenerated = await GenerateNarrativesForNewMappingsAsync(nistControls, ct);

        // 8. Derive org inheritance defaults
        var orgResult = await _orgInheritanceService.DeriveOrgDefaultsAsync("capability-import", ct);

        // 9. Count affected systems
        var systemsAffected = orgResult.AffectedSystems;

        sw.Stop();
        _logger.LogInformation(
            "CSP profile import complete: {ProfileName} — {ComponentsCreated} new / {ComponentsReused} reused components, " +
            "{CapabilitiesCreated} new / {CapabilitiesReused} reused capabilities, " +
            "{MappingsCreated} mappings, {Narratives} narratives, {Systems} systems affected — {Elapsed:N1}s",
            profile.Name, componentsCreated, componentsReused,
            capabilitiesCreated, capabilitiesReused,
            controlMappingsCreated, narrativesGenerated, systemsAffected,
            sw.Elapsed.TotalSeconds);

        return new CspImportResult
        {
            ProfileName = profile.Name,
            ComponentsCreated = componentsCreated,
            ComponentsReused = componentsReused,
            CapabilitiesCreated = capabilitiesCreated,
            CapabilitiesReused = capabilitiesReused,
            ControlMappingsCreated = controlMappingsCreated,
            OrgDefaultsDerived = orgResult.DerivedCount,
            SystemsAffected = systemsAffected,
            NarrativesGenerated = narrativesGenerated,
            Conflicts = conflicts,
            Skipped = skipped,
            DryRun = false,
        };
    }

    /// <summary>
    /// Previews a CSP profile import without persisting any changes.
    /// </summary>
    public async Task<CspImportPreview> ImportCspProfilePreviewAsync(
        string profileId, string conflictResolution = "skip", CancellationToken ct = default)
    {
        var profile = _cspProfileService.GetProfile(profileId);
        if (profile is null)
            throw new KeyNotFoundException($"CSP profile '{profileId}' not found");

        var componentsToCreate = 0;
        var componentsToReuse = 0;
        var capabilitiesToCreate = 0;
        var capabilitiesToReuse = 0;
        var controlMappingsToCreate = 0;
        var conflicts = 0;
        var conflictDetails = new List<ConflictDetail>();

        var services = profile.Services.Count > 0
            ? profile.Services
            : new List<CspProfileService.CspService>
              {
                  new()
                  {
                      Name = profile.Provider,
                      Category = "General",
                      Description = profile.Description,
                      Controls = profile.Controls,
                  }
              };

        foreach (var service in services)
        {
            // Check component existence
            var componentExists = await _db.SystemComponents
                .AnyAsync(c => c.Name == service.Name
                    && c.ComponentType == ComponentType.Thing
                    && c.RegisteredSystemId == null, ct);
            if (componentExists) componentsToReuse++;
            else componentsToCreate++;

            // Check capability existence
            var capabilityName = profile.Services.Count > 0
                ? $"{service.Name} / {service.Category}"
                : profile.Name;
            var capabilityExists = await _db.SecurityCapabilities
                .AnyAsync(c => c.Name.ToLower() == capabilityName.ToLower()
                    && c.Provider.ToLower() == profile.Provider.ToLower(), ct);
            if (capabilityExists) capabilitiesToReuse++;
            else capabilitiesToCreate++;

            // Check control mapping conflicts
            foreach (var control in service.Controls)
            {
                var existingPrimary = await _db.CapabilityControlMappings
                    .Where(m => m.ControlId.ToLower() == control.ControlId.ToLower()
                        && m.RegisteredSystemId == null
                        && m.Role == CapabilityMappingRole.Primary)
                    .Select(m => new { m.SecurityCapabilityId, m.Role })
                    .FirstOrDefaultAsync(ct);

                if (existingPrimary is not null)
                {
                    conflicts++;
                    conflictDetails.Add(new ConflictDetail
                    {
                        ControlId = control.ControlId,
                        ExistingRole = "Primary",
                        NewRole = "Primary",
                        Resolution = "Will assign as Supporting",
                    });
                }

                controlMappingsToCreate++;
            }
        }

        // Count affected systems
        var systemsAffected = await _db.Set<RegisteredSystem>().CountAsync(ct);

        return new CspImportPreview
        {
            ProfileName = profile.Name,
            ComponentsToCreate = componentsToCreate,
            ComponentsToReuse = componentsToReuse,
            CapabilitiesToCreate = capabilitiesToCreate,
            CapabilitiesToReuse = capabilitiesToReuse,
            ControlMappingsToCreate = controlMappingsToCreate,
            Conflicts = conflicts,
            ConflictDetails = conflictDetails,
            SystemsAffected = systemsAffected,
            DryRun = true,
        };
    }

    // ─── CRM Import Pipeline (T024, T025) ───────────────────────────────────

    /// <summary>
    /// Imports CRM data through the full capabilities pipeline.
    /// Groups rows by provider (one component per provider) and provider+NIST family (one capability per group).
    /// </summary>
    public async Task<CrmImportResult> ImportCrmAsync(
        string fileName,
        IReadOnlyList<CrmImportRow> rows,
        string conflictResolution = "skip",
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Starting CRM import: {FileName} ({RowCount} rows)", fileName, rows.Count);

        var nistControls = await _db.NistControls
            .ToDictionaryAsync(n => n.Id, n => n, StringComparer.OrdinalIgnoreCase, ct);

        var componentsCreated = 0;
        var componentsReused = 0;
        var capabilitiesCreated = 0;
        var capabilitiesReused = 0;
        var controlMappingsCreated = 0;
        var unmatchedRows = 0;
        var conflicts = 0;

        // Group by provider
        var providerGroups = rows.GroupBy(r =>
            string.IsNullOrWhiteSpace(r.Provider) ? "" : r.Provider.Trim(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var providerGroup in providerGroups)
        {
            var providerName = string.IsNullOrEmpty(providerGroup.Key)
                ? "Unspecified Provider"
                : providerGroup.Key;

            // Create component per provider (skip for empty provider)
            SystemComponent? component = null;
            if (!string.IsNullOrEmpty(providerGroup.Key))
            {
                component = await FindOrCreateComponentAsync(providerName, null, ct);
                if (_db.Entry(component).State == EntityState.Added)
                    componentsCreated++;
                else
                    componentsReused++;
            }

            // Group by provider + NIST family
            var familyGroups = providerGroup.GroupBy(r =>
            {
                var controlId = r.ControlId?.Trim() ?? "";
                return ExtractFamilyCode(controlId);
            }, StringComparer.OrdinalIgnoreCase);

            foreach (var familyGroup in familyGroups)
            {
                var familyCode = familyGroup.Key;
                // Map family code to family name using NIST data
                var familyName = GetFamilyName(familyCode);
                var capabilityName = $"{providerName} / {familyName}";

                var capability = await FindOrCreateCapabilityAsync(
                    capabilityName, providerName, familyCode, $"Imported from CRM: {providerName} {familyName} controls", ct);

                if (_db.Entry(capability).State == EntityState.Added)
                    capabilitiesCreated++;
                else
                    capabilitiesReused++;

                // Link component to capability
                if (component is not null)
                {
                    var linkExists = await _db.ComponentCapabilityLinks
                        .AnyAsync(l => l.SystemComponentId == component.Id
                            && l.SecurityCapabilityId == capability.Id, ct);
                    if (!linkExists)
                    {
                        _db.ComponentCapabilityLinks.Add(new ComponentCapabilityLink
                        {
                            SystemComponentId = component.Id,
                            SecurityCapabilityId = capability.Id,
                        });
                    }
                }

                // Create control mappings
                foreach (var row in familyGroup)
                {
                    var controlId = row.ControlId?.Trim() ?? "";

                    // Validate control exists in NIST catalog
                    if (!nistControls.ContainsKey(controlId))
                    {
                        unmatchedRows++;
                        continue;
                    }

                    // Check for duplicate mapping
                    var dupMapping = await _db.CapabilityControlMappings
                        .AnyAsync(m => m.SecurityCapabilityId == capability.Id
                            && m.ControlId.ToLower() == controlId.ToLower()
                            && m.RegisteredSystemId == null, ct);
                    if (dupMapping) continue;

                    // Check Primary conflict
                    var existingPrimary = await _db.CapabilityControlMappings
                        .AnyAsync(m => m.ControlId.ToLower() == controlId.ToLower()
                            && m.RegisteredSystemId == null
                            && m.Role == CapabilityMappingRole.Primary
                            && m.SecurityCapabilityId != capability.Id, ct);

                    var role = existingPrimary ? CapabilityMappingRole.Supporting : CapabilityMappingRole.Primary;
                    if (existingPrimary) conflicts++;

                    _db.CapabilityControlMappings.Add(new CapabilityControlMapping
                    {
                        SecurityCapabilityId = capability.Id,
                        ControlId = controlId,
                        RegisteredSystemId = null,
                        Role = role,
                        CreatedBy = "capability-import",
                    });
                    controlMappingsCreated++;
                }
            }
        }

        _logger.LogInformation("Saving CRM import: {Components} components, {Capabilities} capabilities, {Mappings} mappings, {Unmatched} unmatched",
            componentsCreated, capabilitiesCreated, controlMappingsCreated, unmatchedRows);
        await _db.SaveChangesAsync(ct);

        var narrativesGenerated = await GenerateNarrativesForNewMappingsAsync(
            nistControls.ToDictionary(kv => kv.Key, kv => kv.Value.Title, StringComparer.OrdinalIgnoreCase), ct);

        var orgResult = await _orgInheritanceService.DeriveOrgDefaultsAsync("capability-import", ct);

        sw.Stop();
        _logger.LogInformation(
            "CRM import complete: {FileName} — {ComponentsCreated} new / {ComponentsReused} reused components, " +
            "{CapabilitiesCreated} new / {CapabilitiesReused} reused capabilities, " +
            "{MappingsCreated} mappings, {Unmatched} unmatched, {Narratives} narratives — {Elapsed:N1}s",
            fileName, componentsCreated, componentsReused,
            capabilitiesCreated, capabilitiesReused,
            controlMappingsCreated, unmatchedRows, narrativesGenerated,
            sw.Elapsed.TotalSeconds);

        return new CrmImportResult
        {
            FileName = fileName,
            RowsParsed = rows.Count,
            ComponentsCreated = componentsCreated,
            ComponentsReused = componentsReused,
            CapabilitiesCreated = capabilitiesCreated,
            CapabilitiesReused = capabilitiesReused,
            ControlMappingsCreated = controlMappingsCreated,
            UnmatchedRows = unmatchedRows,
            OrgDefaultsDerived = orgResult.DerivedCount,
            SystemsAffected = orgResult.AffectedSystems,
            NarrativesGenerated = narrativesGenerated,
            Conflicts = conflicts,
            DryRun = false,
        };
    }

    /// <summary>
    /// Previews a CRM import without persisting any changes.
    /// </summary>
    public async Task<CrmImportPreview> ImportCrmPreviewAsync(
        string fileName,
        IReadOnlyList<CrmImportRow> rows,
        string conflictResolution = "skip",
        CancellationToken ct = default)
    {
        var nistControls = await _db.NistControls
            .Select(n => n.Id)
            .ToListAsync(ct);
        var validControlIds = new HashSet<string>(nistControls, StringComparer.OrdinalIgnoreCase);

        var componentsToCreate = 0;
        var componentsToReuse = 0;
        var capabilitiesToCreate = 0;
        var capabilitiesToReuse = 0;
        var controlMappingsToCreate = 0;
        var unmatchedRows = 0;
        var conflicts = 0;
        var conflictDetails = new List<ConflictDetail>();

        var providerGroups = rows.GroupBy(r =>
            string.IsNullOrWhiteSpace(r.Provider) ? "" : r.Provider.Trim(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var providerGroup in providerGroups)
        {
            var providerName = string.IsNullOrEmpty(providerGroup.Key)
                ? "Unspecified Provider"
                : providerGroup.Key;

            if (!string.IsNullOrEmpty(providerGroup.Key))
            {
                var componentExists = await _db.SystemComponents
                    .AnyAsync(c => c.Name == providerName
                        && c.ComponentType == ComponentType.Thing
                        && c.RegisteredSystemId == null, ct);
                if (componentExists) componentsToReuse++;
                else componentsToCreate++;
            }

            var familyGroups = providerGroup.GroupBy(r =>
                ExtractFamilyCode(r.ControlId?.Trim() ?? ""),
                StringComparer.OrdinalIgnoreCase);

            foreach (var familyGroup in familyGroups)
            {
                var familyName = GetFamilyName(familyGroup.Key);
                var capabilityName = $"{providerName} / {familyName}";

                var capabilityExists = await _db.SecurityCapabilities
                    .AnyAsync(c => c.Name.ToLower() == capabilityName.ToLower()
                        && c.Provider.ToLower() == providerName.ToLower(), ct);
                if (capabilityExists) capabilitiesToReuse++;
                else capabilitiesToCreate++;

                foreach (var row in familyGroup)
                {
                    var controlId = row.ControlId?.Trim() ?? "";
                    if (!validControlIds.Contains(controlId))
                    {
                        unmatchedRows++;
                        continue;
                    }

                    var existingPrimary = await _db.CapabilityControlMappings
                        .AnyAsync(m => m.ControlId.ToLower() == controlId.ToLower()
                            && m.RegisteredSystemId == null
                            && m.Role == CapabilityMappingRole.Primary, ct);

                    if (existingPrimary)
                    {
                        conflicts++;
                        conflictDetails.Add(new ConflictDetail
                        {
                            ControlId = controlId,
                            ExistingRole = "Primary",
                            NewRole = "Primary",
                            Resolution = "Will assign as Supporting",
                        });
                    }

                    controlMappingsToCreate++;
                }
            }
        }

        var systemsAffected = await _db.Set<RegisteredSystem>().CountAsync(ct);

        return new CrmImportPreview
        {
            FileName = fileName,
            RowsParsed = rows.Count,
            ComponentsToCreate = componentsToCreate,
            ComponentsToReuse = componentsToReuse,
            CapabilitiesToCreate = capabilitiesToCreate,
            CapabilitiesToReuse = capabilitiesToReuse,
            ControlMappingsToCreate = controlMappingsToCreate,
            UnmatchedRows = unmatchedRows,
            Conflicts = conflicts,
            ConflictDetails = conflictDetails,
            SystemsAffected = systemsAffected,
            DryRun = true,
        };
    }

    // ─── Coverage Computation (T017) ────────────────────────────────────────

    /// <summary>
    /// Computes org-wide and optional per-system capability coverage statistics.
    /// </summary>
    public async Task<CoverageResponse> ComputeCoverageAsync(
        bool includePerSystem = false, bool includePerFamily = true, CancellationToken ct = default)
    {
        var totalCapabilities = await _db.SecurityCapabilities.CountAsync(ct);

        // Get all distinct control IDs that have org-wide mappings
        var mappedControlIds = await _db.CapabilityControlMappings
            .Where(m => m.RegisteredSystemId == null)
            .Select(m => m.ControlId)
            .Distinct()
            .ToListAsync(ct);
        var mappedControls = mappedControlIds.Count;

        // Determine baseline denominator
        // 1. Try highest active system baseline
        var highestBaseline = await GetHighestBaselineAsync(ct);

        // 2. Fall back to CSP profile declared baseline
        string? baselineLevel = highestBaseline?.Level;
        int? baselineControlCount = highestBaseline?.ControlCount;

        if (baselineLevel is null)
        {
            var cspProfile = _cspProfileService.GetProfiles().FirstOrDefault();
            if (cspProfile is not null)
            {
                baselineLevel = cspProfile.BaselineLevel;
                baselineControlCount = await _db.NistControls
                    .Where(n => n.Baselines.Contains(cspProfile.BaselineLevel))
                    .CountAsync(ct);
            }
        }

        double? coveragePercent = baselineControlCount > 0
            ? Math.Round((double)mappedControls / baselineControlCount.Value * 100, 1)
            : null;
        int? unmappedControls = baselineControlCount.HasValue
            ? baselineControlCount.Value - mappedControls
            : null;

        // Per-family breakdown
        var perFamily = new List<FamilyCoverage>();
        if (includePerFamily && baselineLevel is not null)
        {
            var allBaselineControls = await _db.NistControls
                .Where(n => n.Baselines.Contains(baselineLevel))
                .Select(n => new { n.Id, n.Family })
                .ToListAsync(ct);

            var mappedSet = new HashSet<string>(mappedControlIds, StringComparer.OrdinalIgnoreCase);
            var familyGroups = allBaselineControls.GroupBy(c => c.Family.ToUpperInvariant());

            foreach (var group in familyGroups.OrderBy(g => g.Key))
            {
                var total = group.Count();
                var mapped = group.Count(c => mappedSet.Contains(c.Id));
                perFamily.Add(new FamilyCoverage
                {
                    Family = group.Key,
                    Mapped = mapped,
                    Total = total,
                    Percent = total > 0 ? Math.Round((double)mapped / total * 100, 1) : 0,
                });
            }
        }

        // Per-system breakdown
        var perSystem = new List<SystemCoverage>();
        if (includePerSystem)
        {
            var systems = await _db.Set<RegisteredSystem>()
                .Select(s => new { s.Id, s.Name })
                .ToListAsync(ct);

            foreach (var system in systems)
            {
                // Get system's baseline control count
                var systemBaseline = await GetSystemBaselineAsync(system.Id, ct);
                if (systemBaseline is null) continue;

                var systemMapped = await _db.CapabilityControlMappings
                    .Where(m => m.RegisteredSystemId == null || m.RegisteredSystemId == system.Id)
                    .Select(m => m.ControlId)
                    .Distinct()
                    .CountAsync(ct);

                perSystem.Add(new SystemCoverage
                {
                    SystemId = system.Id,
                    SystemName = system.Name,
                    BaselineLevel = systemBaseline.Level,
                    CoveragePercent = systemBaseline.ControlCount > 0
                        ? Math.Round((double)systemMapped / systemBaseline.ControlCount * 100, 1) : 0,
                    MappedControls = systemMapped,
                    TotalControls = systemBaseline.ControlCount,
                });
            }
        }

        return new CoverageResponse
        {
            OrgWide = new OrgWideCoverage
            {
                TotalCapabilities = totalCapabilities,
                MappedControls = mappedControls,
                UnmappedControls = unmappedControls,
                CoveragePercent = coveragePercent,
                BaselineLevel = baselineLevel,
                BaselineControlCount = baselineControlCount,
                PerFamily = perFamily,
            },
            PerSystem = perSystem,
        };
    }

    // ─── Private Helpers ────────────────────────────────────────────────────

    private async Task<int> GenerateNarrativesForNewMappingsAsync(
        Dictionary<string, string> nistControlTitles, CancellationToken ct)
    {
        // Find all control implementations that need narratives updated
        // We regenerate narratives for all org-wide mappings that belong to implemented capabilities
        var mappingsNeedingNarratives = await _db.CapabilityControlMappings
            .Include(m => m.SecurityCapability)
            .Where(m => m.RegisteredSystemId == null
                && m.SecurityCapability.ImplementationStatus == CapabilityStatus.Implemented)
            .ToListAsync(ct);

        var count = 0;
        foreach (var mapping in mappingsNeedingNarratives)
        {
            nistControlTitles.TryGetValue(mapping.ControlId, out var controlTitle);
            controlTitle ??= mapping.ControlId;

            // Get component contexts for this capability
            var componentContexts = await _db.ComponentCapabilityLinks
                .Where(l => l.SecurityCapabilityId == mapping.SecurityCapabilityId)
                .Select(l => new ComponentContext(
                    l.SystemComponent.Name,
                    l.SystemComponent.ComponentType.ToString(),
                    l.SystemComponent.Owner,
                    l.SystemComponent.PersonName))
                .ToListAsync(ct);

            var narrative = _narrativeService.GenerateEnrichedNarrative(
                mapping.SecurityCapability.Name,
                mapping.SecurityCapability.Provider,
                mapping.SecurityCapability.Description,
                mapping.ControlId,
                controlTitle,
                componentContexts.Count > 0 ? componentContexts : null,
                null);

            // This generates the narrative but doesn't directly persist it on the mapping
            // The OrgInheritanceService.DeriveOrgDefaultsAsync handles setting narratives on ControlInheritance records
            count++;
        }

        return count;
    }

    private async Task<BaselineInfo?> GetHighestBaselineAsync(CancellationToken ct)
    {
        // Find the highest baseline level from ControlBaselines
        var baselines = await _db.ControlBaselines
            .Select(b => b.BaselineLevel)
            .Where(b => !string.IsNullOrEmpty(b))
            .Distinct()
            .ToListAsync(ct);

        if (baselines.Count == 0) return null;

        // Prioritize: High > Moderate > Low
        var highestLevel = baselines
            .OrderByDescending(b => b switch
            {
                "High" => 3, "Moderate" => 2, "Low" => 1, _ => 0
            })
            .First();

        var controlCount = await _db.NistControls
            .Where(n => n.Baselines.Contains(highestLevel))
            .CountAsync(ct);

        return new BaselineInfo(highestLevel, controlCount);
    }

    private async Task<BaselineInfo?> GetSystemBaselineAsync(string systemId, CancellationToken ct)
    {
        var baseline = await _db.ControlBaselines
            .Where(b => b.RegisteredSystemId == systemId)
            .Select(b => new { b.BaselineLevel })
            .FirstOrDefaultAsync(ct);

        if (baseline?.BaselineLevel is null) return null;

        var controlCount = await _db.NistControls
            .Where(n => n.Baselines.Contains(baseline.BaselineLevel))
            .CountAsync(ct);

        return new BaselineInfo(baseline.BaselineLevel, controlCount);
    }

    private static string ExtractFamilyCode(string controlId)
    {
        if (string.IsNullOrEmpty(controlId)) return "XX";
        var dashIndex = controlId.IndexOf('-');
        return dashIndex > 0 ? controlId[..dashIndex].ToUpperInvariant() : "XX";
    }

    private static string GetFamilyName(string familyCode) => familyCode.ToUpperInvariant() switch
    {
        "AC" => "Access Control",
        "AT" => "Awareness and Training",
        "AU" => "Audit and Accountability",
        "CA" => "Assessment, Authorization, and Monitoring",
        "CM" => "Configuration Management",
        "CP" => "Contingency Planning",
        "IA" => "Identification and Authentication",
        "IR" => "Incident Response",
        "MA" => "Maintenance",
        "MP" => "Media Protection",
        "PE" => "Physical and Environmental Protection",
        "PL" => "Planning",
        "PM" => "Program Management",
        "PS" => "Personnel Security",
        "PT" => "Personally Identifiable Information Processing",
        "RA" => "Risk Assessment",
        "SA" => "System and Services Acquisition",
        "SC" => "System and Communications Protection",
        "SI" => "System and Information Integrity",
        "SR" => "Supply Chain Risk Management",
        _ => familyCode,
    };

    // ─── DTOs ───────────────────────────────────────────────────────────────

    private record BaselineInfo(string Level, int ControlCount);
}

// ─── Import DTOs ────────────────────────────────────────────────────────────

/// <summary>Result of a CSP profile import operation.</summary>
public class CspImportResult
{
    public string ProfileName { get; set; } = string.Empty;
    public int ComponentsCreated { get; set; }
    public int ComponentsReused { get; set; }
    public int CapabilitiesCreated { get; set; }
    public int CapabilitiesReused { get; set; }
    public int ControlMappingsCreated { get; set; }
    public int OrgDefaultsDerived { get; set; }
    public int SystemsAffected { get; set; }
    public int NarrativesGenerated { get; set; }
    public int Conflicts { get; set; }
    public int Skipped { get; set; }
    public bool DryRun { get; set; }
}

/// <summary>Preview result for a dry-run CSP profile import.</summary>
public class CspImportPreview
{
    public string ProfileName { get; set; } = string.Empty;
    public int ComponentsToCreate { get; set; }
    public int ComponentsToReuse { get; set; }
    public int CapabilitiesToCreate { get; set; }
    public int CapabilitiesToReuse { get; set; }
    public int ControlMappingsToCreate { get; set; }
    public int Conflicts { get; set; }
    public List<ConflictDetail> ConflictDetails { get; set; } = new();
    public int SystemsAffected { get; set; }
    public bool DryRun { get; set; }
}

/// <summary>Result of a CRM import operation.</summary>
public class CrmImportResult
{
    public string FileName { get; set; } = string.Empty;
    public int RowsParsed { get; set; }
    public int ComponentsCreated { get; set; }
    public int ComponentsReused { get; set; }
    public int CapabilitiesCreated { get; set; }
    public int CapabilitiesReused { get; set; }
    public int ControlMappingsCreated { get; set; }
    public int UnmatchedRows { get; set; }
    public int OrgDefaultsDerived { get; set; }
    public int SystemsAffected { get; set; }
    public int NarrativesGenerated { get; set; }
    public int Conflicts { get; set; }
    public bool DryRun { get; set; }
}

/// <summary>Preview result for a dry-run CRM import.</summary>
public class CrmImportPreview
{
    public string FileName { get; set; } = string.Empty;
    public int RowsParsed { get; set; }
    public int ComponentsToCreate { get; set; }
    public int ComponentsToReuse { get; set; }
    public int CapabilitiesToCreate { get; set; }
    public int CapabilitiesToReuse { get; set; }
    public int ControlMappingsToCreate { get; set; }
    public int UnmatchedRows { get; set; }
    public int Conflicts { get; set; }
    public List<ConflictDetail> ConflictDetails { get; set; } = new();
    public int SystemsAffected { get; set; }
    public bool DryRun { get; set; }
    public List<string> DetectedColumns { get; set; } = new();
    public List<Dictionary<string, string>> SampleRows { get; set; } = new();
}

/// <summary>Details of a single control mapping conflict.</summary>
public class ConflictDetail
{
    public string ControlId { get; set; } = string.Empty;
    public string ExistingRole { get; set; } = string.Empty;
    public string NewRole { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
}

/// <summary>Coverage statistics response.</summary>
public class CoverageResponse
{
    public OrgWideCoverage OrgWide { get; set; } = new();
    public List<SystemCoverage> PerSystem { get; set; } = new();
}

/// <summary>Org-wide coverage statistics.</summary>
public class OrgWideCoverage
{
    public int TotalCapabilities { get; set; }
    public int MappedControls { get; set; }
    public int? UnmappedControls { get; set; }
    public double? CoveragePercent { get; set; }
    public string? BaselineLevel { get; set; }
    public int? BaselineControlCount { get; set; }
    public List<FamilyCoverage> PerFamily { get; set; } = new();
}

/// <summary>Coverage breakdown per NIST family.</summary>
public class FamilyCoverage
{
    public string Family { get; set; } = string.Empty;
    public int Mapped { get; set; }
    public int Total { get; set; }
    public double Percent { get; set; }
}

/// <summary>Per-system coverage breakdown.</summary>
public class SystemCoverage
{
    public string SystemId { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;
    public string BaselineLevel { get; set; } = string.Empty;
    public double CoveragePercent { get; set; }
    public int MappedControls { get; set; }
    public int TotalControls { get; set; }
}

/// <summary>Parsed row from a CRM spreadsheet before processing.</summary>
public class CrmImportRow
{
    public string ControlId { get; set; } = string.Empty;
    public string InheritanceType { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? CustomerResponsibility { get; set; }
}

/// <summary>Request body for CSP profile import endpoint.</summary>
public class CspProfileImportRequest
{
    public string ProfileId { get; set; } = string.Empty;
    public string? ConflictResolution { get; set; }
    public bool? DryRun { get; set; }
}

public class LinkComponentCapabilitiesRequest
{
    public List<string> CapabilityIds { get; set; } = new();
}
