using System.Diagnostics;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Derives org-level inheritance defaults from org-wide capabilities,
/// propagates them to systems on baseline selection, and supports
/// revert-to-org-default operations (Feature 044).
/// </summary>
public class OrgInheritanceService : IOrgInheritanceService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrgInheritanceService> _logger;

    public OrgInheritanceService(
        IServiceScopeFactory scopeFactory,
        ILogger<OrgInheritanceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrgDerivationResult> DeriveOrgDefaultsAsync(
        string derivedBy = "system",
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // 1. Collect all org-wide capability mappings (RegisteredSystemId == null)
        //    from capabilities with ImplementationStatus == Implemented
        var orgMappings = await context.CapabilityControlMappings
            .Include(m => m.SecurityCapability)
            .Where(m => m.RegisteredSystemId == null
                        && m.SecurityCapability.ImplementationStatus == CapabilityStatus.Implemented)
            .ToListAsync(cancellationToken);

        // 2. Group by ControlId
        var groupedByControl = orgMappings
            .GroupBy(m => m.ControlId, StringComparer.OrdinalIgnoreCase);

        // 3. Derive defaults per control using precedence rules
        var derivedDefaults = new List<OrgInheritanceDefault>();
        var now = DateTime.UtcNow;

        foreach (var group in groupedByControl)
        {
            var controlId = group.Key;
            var mappings = group.ToList();

            // Precedence: Primary > Supporting > Shared
            var hasPrimaryOrSupporting = mappings.Any(m =>
                m.Role == CapabilityMappingRole.Primary || m.Role == CapabilityMappingRole.Supporting);

            var inheritanceType = hasPrimaryOrSupporting
                ? InheritanceType.Inherited
                : InheritanceType.Shared;

            // Determine winning role for display
            CapabilityMappingRole winningRole;
            if (mappings.Any(m => m.Role == CapabilityMappingRole.Primary))
                winningRole = CapabilityMappingRole.Primary;
            else if (mappings.Any(m => m.Role == CapabilityMappingRole.Supporting))
                winningRole = CapabilityMappingRole.Supporting;
            else
                winningRole = CapabilityMappingRole.Shared;

            // Merge providers from all contributing capabilities
            var providers = mappings
                .Select(m => m.SecurityCapability.Provider)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var capabilityIds = mappings
                .Select(m => m.SecurityCapabilityId)
                .Distinct()
                .ToList();

            var capabilityNames = mappings
                .Select(m => m.SecurityCapability.Name)
                .Distinct()
                .ToList();

            derivedDefaults.Add(new OrgInheritanceDefault
            {
                ControlId = controlId,
                InheritanceType = inheritanceType,
                Provider = string.Join(", ", providers),
                SourceCapabilityIds = string.Join(",", capabilityIds),
                SourceCapabilityNames = string.Join(", ", capabilityNames),
                MappingRole = winningRole,
                DerivedAt = now,
            });
        }

        // 4. Load existing org defaults for diffing
        var existingDefaults = await context.OrgInheritanceDefaults
            .ToListAsync(cancellationToken);

        var existingByControl = existingDefaults
            .ToDictionary(d => d.ControlId, StringComparer.OrdinalIgnoreCase);

        var derivedByControl = derivedDefaults
            .ToDictionary(d => d.ControlId, StringComparer.OrdinalIgnoreCase);

        // 5. Upsert derived defaults
        var upsertCount = 0;
        foreach (var derived in derivedDefaults)
        {
            if (existingByControl.TryGetValue(derived.ControlId, out var existing))
            {
                // Update existing
                existing.InheritanceType = derived.InheritanceType;
                existing.Provider = derived.Provider;
                existing.SourceCapabilityIds = derived.SourceCapabilityIds;
                existing.SourceCapabilityNames = derived.SourceCapabilityNames;
                existing.MappingRole = derived.MappingRole;
                existing.DerivedAt = now;
            }
            else
            {
                // Insert new
                context.OrgInheritanceDefaults.Add(derived);
            }
            upsertCount++;
        }

        // 6. Remove stale defaults (controls no longer mapped by any org-wide capability)
        var removedCount = 0;
        foreach (var existing in existingDefaults)
        {
            if (!derivedByControl.ContainsKey(existing.ControlId))
            {
                context.OrgInheritanceDefaults.Remove(existing);
                removedCount++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        // 7. Cascade: Update all systems with baselines — push new org defaults
        //    to OrgDerived designations (skip overrides: Manual/ProfileApply/CrmImport/BulkUpdate)
        var affectedSystems = 0;

        var baselines = await context.ControlBaselines
            .Include(b => b.Inheritances)
            .ToListAsync(cancellationToken);

        // Build final org defaults lookup (after save, IDs are stable)
        var finalOrgDefaults = await context.OrgInheritanceDefaults
            .ToDictionaryAsync(d => d.ControlId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var baseline in baselines)
        {
            var baselineControlIds = baseline.ControlIds?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            if (baselineControlIds.Count == 0) continue;

            var systemDesignations = baseline.Inheritances
                .ToDictionary(ci => ci.ControlId, StringComparer.OrdinalIgnoreCase);

            var systemChanged = false;
            foreach (var (controlId, orgDefault) in finalOrgDefaults)
            {
                if (!baselineControlIds.Contains(controlId)) continue;

                if (systemDesignations.TryGetValue(controlId, out var existing))
                {
                    // Skip overrides
                    if (existing.DesignationSource is "Manual" or "ProfileApply" or "CrmImport" or "BulkUpdate")
                        continue;

                    // Update OrgDerived designation
                    existing.InheritanceType = orgDefault.InheritanceType;
                    existing.Provider = orgDefault.Provider;
                    existing.DesignationSource = "OrgDerived";
                    existing.OrgInheritanceDefaultId = orgDefault.Id;
                    existing.SetBy = derivedBy;
                    existing.SetAt = now;
                    systemChanged = true;
                }
                else
                {
                    // New org default for control in baseline — create designation
                    context.ControlInheritances.Add(new ControlInheritance
                    {
                        ControlBaselineId = baseline.Id,
                        ControlId = controlId,
                        InheritanceType = orgDefault.InheritanceType,
                        Provider = orgDefault.Provider,
                        DesignationSource = "OrgDerived",
                        OrgInheritanceDefaultId = orgDefault.Id,
                        SetBy = derivedBy,
                        SetAt = now,
                    });
                    systemChanged = true;
                }
            }

            // Remove OrgDerived designations for controls no longer in org defaults
            foreach (var des in baseline.Inheritances)
            {
                if (des.DesignationSource == "OrgDerived" && !finalOrgDefaults.ContainsKey(des.ControlId))
                {
                    context.ControlInheritances.Remove(des);
                    systemChanged = true;
                }
            }

            if (systemChanged)
            {
                affectedSystems++;
                // Create cascade audit entry per system
                context.InheritanceAuditEntries.Add(new InheritanceAuditEntry
                {
                    ControlId = "ORG-CASCADE",
                    ControlBaselineId = baseline.Id,
                    Actor = derivedBy,
                    NewInheritanceType = "OrgPropagation",
                    ChangeSource = InheritanceChangeSource.OrgPropagation,
                    Timestamp = now,
                });
            }
        }

        if (affectedSystems > 0)
            await context.SaveChangesAsync(cancellationToken);

        var inheritedCount = derivedDefaults.Count(d => d.InheritanceType == InheritanceType.Inherited);
        var sharedCount = derivedDefaults.Count(d => d.InheritanceType == InheritanceType.Shared);

        sw.Stop();
        _logger.LogInformation(
            "Derived org defaults: {DerivedCount} total ({InheritedCount} inherited, {SharedCount} shared), {RemovedCount} removed, cascaded to {AffectedSystems} system(s) in {ElapsedMs}ms. DerivedBy={DerivedBy}",
            upsertCount, inheritedCount, sharedCount, removedCount, affectedSystems, sw.ElapsedMilliseconds, derivedBy);

        return new OrgDerivationResult(
            DerivedCount: upsertCount,
            InheritedCount: inheritedCount,
            SharedCount: sharedCount,
            RemovedCount: removedCount,
            AffectedSystems: affectedSystems,
            DerivedAt: now);
    }

    /// <inheritdoc />
    public async Task<OrgPropagationResult> PropagateToSystemAsync(
        string systemId,
        string baselineId,
        IReadOnlySet<string> baselineControlIds,
        string propagatedBy = "system",
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Load all org defaults
        var orgDefaults = await context.OrgInheritanceDefaults
            .ToListAsync(cancellationToken);

        // Load existing system designations for this baseline
        var existingDesignations = await context.ControlInheritances
            .Where(ci => ci.ControlBaselineId == baselineId)
            .ToListAsync(cancellationToken);

        var existingByControl = existingDesignations
            .ToDictionary(d => d.ControlId, StringComparer.OrdinalIgnoreCase);

        var propagatedControlIds = new List<string>();
        var skippedCount = 0;

        foreach (var orgDefault in orgDefaults)
        {
            // Only propagate for controls that are in this baseline
            if (!baselineControlIds.Contains(orgDefault.ControlId))
                continue;

            // Skip if system already has a designation (override preserved)
            if (existingByControl.TryGetValue(orgDefault.ControlId, out var existing))
            {
                // Only skip if the existing designation is a manual override / profile / import
                var source = existing.DesignationSource;
                if (source is "Manual" or "ProfileApply" or "CrmImport" or "BulkUpdate")
                {
                    skippedCount++;
                    continue;
                }

                // Update existing OrgDerived designation to match current org default
                existing.InheritanceType = orgDefault.InheritanceType;
                existing.Provider = orgDefault.Provider;
                existing.DesignationSource = "OrgDerived";
                existing.OrgInheritanceDefaultId = orgDefault.Id;
                existing.SetBy = propagatedBy;
                existing.SetAt = DateTime.UtcNow;
            }
            else
            {
                // Create new designation from org default
                var inheritance = new ControlInheritance
                {
                    ControlBaselineId = baselineId,
                    ControlId = orgDefault.ControlId,
                    InheritanceType = orgDefault.InheritanceType,
                    Provider = orgDefault.Provider,
                    DesignationSource = "OrgDerived",
                    OrgInheritanceDefaultId = orgDefault.Id,
                    SetBy = propagatedBy,
                    SetAt = DateTime.UtcNow,
                };
                context.ControlInheritances.Add(inheritance);
            }

            // Create audit entry
            context.InheritanceAuditEntries.Add(new InheritanceAuditEntry
            {
                ControlInheritanceId = existingByControl.ContainsKey(orgDefault.ControlId)
                    ? existingByControl[orgDefault.ControlId].Id
                    : Guid.NewGuid().ToString(), // Will match the new entity's Id
                ControlId = orgDefault.ControlId,
                ControlBaselineId = baselineId,
                Actor = propagatedBy,
                PreviousInheritanceType = existingByControl.TryGetValue(orgDefault.ControlId, out var prev)
                    ? prev.InheritanceType.ToString() : null,
                NewInheritanceType = orgDefault.InheritanceType.ToString(),
                PreviousProvider = prev?.Provider,
                NewProvider = orgDefault.Provider,
                ChangeSource = InheritanceChangeSource.OrgDerived,
                Timestamp = DateTime.UtcNow,
            });

            propagatedControlIds.Add(orgDefault.ControlId);
        }

        // Update baseline inheritance counts
        var baseline = await context.ControlBaselines
            .Include(b => b.Inheritances)
            .FirstOrDefaultAsync(b => b.Id == baselineId, cancellationToken);

        if (baseline is not null)
        {
            await context.SaveChangesAsync(cancellationToken);

            // Reload inheritances to get accurate counts
            var allInheritances = await context.ControlInheritances
                .Where(ci => ci.ControlBaselineId == baselineId)
                .ToListAsync(cancellationToken);

            baseline.InheritedControls = allInheritances.Count(i => i.InheritanceType == InheritanceType.Inherited);
            baseline.SharedControls = allInheritances.Count(i => i.InheritanceType == InheritanceType.Shared);
            baseline.CustomerControls = allInheritances.Count(i => i.InheritanceType == InheritanceType.Customer);
            baseline.ModifiedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        sw.Stop();
        _logger.LogInformation(
            "Propagated org defaults to system {SystemId}: {PropagatedCount} propagated, {SkippedCount} skipped (overrides preserved) in {ElapsedMs}ms",
            systemId, propagatedControlIds.Count, skippedCount, sw.ElapsedMilliseconds);

        return new OrgPropagationResult(
            PropagatedCount: propagatedControlIds.Count,
            SkippedCount: skippedCount,
            PropagatedControlIds: propagatedControlIds);
    }

    /// <inheritdoc />
    public async Task<RevertResult> RevertToOrgDefaultsAsync(
        string systemId,
        IReadOnlyList<string> controlIds,
        string revertedBy,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Load the system's baseline
        var baseline = await context.ControlBaselines
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"No baseline found for system '{systemId}'.");

        // Load current org defaults for requested controls
        var orgDefaults = await context.OrgInheritanceDefaults
            .Where(d => controlIds.Contains(d.ControlId))
            .ToDictionaryAsync(d => d.ControlId, cancellationToken);

        // Load existing system designations for requested controls
        var existingDesignations = await context.ControlInheritances
            .Where(ci => ci.ControlBaselineId == baseline.Id && controlIds.Contains(ci.ControlId))
            .ToDictionaryAsync(ci => ci.ControlId, cancellationToken);

        var revertedCount = 0;
        var skipped = new List<RevertSkip>();

        foreach (var controlId in controlIds)
        {
            if (!orgDefaults.TryGetValue(controlId, out var orgDefault))
            {
                skipped.Add(new RevertSkip(controlId, "No org default exists for this control"));
                continue;
            }

            if (!existingDesignations.TryGetValue(controlId, out var existing))
            {
                // No existing designation — create from org default
                var newInheritance = new ControlInheritance
                {
                    ControlBaselineId = baseline.Id,
                    ControlId = controlId,
                    InheritanceType = orgDefault.InheritanceType,
                    Provider = orgDefault.Provider,
                    DesignationSource = "OrgDerived",
                    OrgInheritanceDefaultId = orgDefault.Id,
                    SetBy = revertedBy,
                    SetAt = DateTime.UtcNow,
                };
                context.ControlInheritances.Add(newInheritance);

                context.InheritanceAuditEntries.Add(new InheritanceAuditEntry
                {
                    ControlInheritanceId = newInheritance.Id,
                    ControlId = controlId,
                    ControlBaselineId = baseline.Id,
                    Actor = revertedBy,
                    NewInheritanceType = orgDefault.InheritanceType.ToString(),
                    NewProvider = orgDefault.Provider,
                    ChangeSource = InheritanceChangeSource.Manual, // User-initiated revert
                    Timestamp = DateTime.UtcNow,
                });

                revertedCount++;
                continue;
            }

            // Capture previous values for audit
            var prevType = existing.InheritanceType.ToString();
            var prevProvider = existing.Provider;
            var prevResponsibility = existing.CustomerResponsibility;

            // Revert to org default
            existing.InheritanceType = orgDefault.InheritanceType;
            existing.Provider = orgDefault.Provider;
            existing.CustomerResponsibility = null;
            existing.DesignationSource = "OrgDerived";
            existing.OrgInheritanceDefaultId = orgDefault.Id;
            existing.SetBy = revertedBy;
            existing.SetAt = DateTime.UtcNow;

            // Create audit entry
            context.InheritanceAuditEntries.Add(new InheritanceAuditEntry
            {
                ControlInheritanceId = existing.Id,
                ControlId = controlId,
                ControlBaselineId = baseline.Id,
                Actor = revertedBy,
                PreviousInheritanceType = prevType,
                NewInheritanceType = orgDefault.InheritanceType.ToString(),
                PreviousProvider = prevProvider,
                NewProvider = orgDefault.Provider,
                PreviousCustomerResponsibility = prevResponsibility,
                ChangeSource = InheritanceChangeSource.Manual, // User-initiated revert
                Timestamp = DateTime.UtcNow,
            });

            revertedCount++;
        }

        // Update baseline counts
        await context.SaveChangesAsync(cancellationToken);

        var allInheritances = await context.ControlInheritances
            .Where(ci => ci.ControlBaselineId == baseline.Id)
            .ToListAsync(cancellationToken);

        baseline.InheritedControls = allInheritances.Count(i => i.InheritanceType == InheritanceType.Inherited);
        baseline.SharedControls = allInheritances.Count(i => i.InheritanceType == InheritanceType.Shared);
        baseline.CustomerControls = allInheritances.Count(i => i.InheritanceType == InheritanceType.Customer);
        baseline.ModifiedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reverted {RevertedCount} controls to org defaults for system {SystemId}, {SkippedCount} skipped. RevertedBy={RevertedBy}",
            revertedCount, systemId, skipped.Count, revertedBy);

        return new RevertResult(
            RevertedCount: revertedCount,
            SkippedCount: skipped.Count,
            Skipped: skipped);
    }

    /// <inheritdoc />
    public async Task<OrgDefaultsListResult> GetOrgDefaultsAsync(
        string? familyFilter = null,
        string? typeFilter = null,
        string? search = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var query = context.OrgInheritanceDefaults.AsQueryable();

        // Apply family filter (e.g., "AC" matches "AC-2", "AC-6")
        if (!string.IsNullOrWhiteSpace(familyFilter))
        {
            var prefix = familyFilter.Trim().ToUpperInvariant();
            query = query.Where(d => d.ControlId.ToUpper().StartsWith(prefix + "-") || d.ControlId.ToUpper() == prefix);
        }

        // Apply type filter
        if (!string.IsNullOrWhiteSpace(typeFilter) && Enum.TryParse<InheritanceType>(typeFilter, true, out var filterType))
        {
            query = query.Where(d => d.InheritanceType == filterType);
        }

        // Apply search (control ID or provider)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(d =>
                d.ControlId.Contains(term) ||
                d.Provider.Contains(term) ||
                d.SourceCapabilityNames.Contains(term));
        }

        // Get total count before pagination
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply pagination
        var items = await query
            .OrderBy(d => d.ControlId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Calculate summary from ALL matching items (not just page)
        var allItems = await context.OrgInheritanceDefaults.ToListAsync(cancellationToken);
        var summary = new OrgDefaultsSummary(
            InheritedCount: allItems.Count(d => d.InheritanceType == InheritanceType.Inherited),
            SharedCount: allItems.Count(d => d.InheritanceType == InheritanceType.Shared),
            TotalControls: allItems.Count);

        return new OrgDefaultsListResult(items, totalCount, summary);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Added in Feature 048 (T218) per the FR-110 reuse-first audit. Provides the
    /// SINGLE per-row insert / update path for <c>OrgInheritanceDefault</c>. T225
    /// extends this method to emit a <c>CspCapabilityConsumed</c> domain event when
    /// <see cref="SaveOrgInheritanceDefaultRequest.SourceCspCapabilityId"/> is non-null.
    /// The CSP-FK columns themselves (<c>SourceCspCapabilityId</c>, <c>SourceCspComponentId</c>)
    /// are added to <see cref="OrgInheritanceDefault"/> by T223; this implementation
    /// passes them through verbatim so T223 can flip the FK assignment from no-op
    /// to active without touching this method.
    /// </remarks>
    public async Task<OrgInheritanceDefault> SaveAsync(
        SaveOrgInheritanceDefaultRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ControlId))
        {
            throw new ArgumentException("ControlId is required.", nameof(request));
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var existing = await context.OrgInheritanceDefaults
            .FirstOrDefaultAsync(d => d.ControlId == request.ControlId, cancellationToken);

        var now = DateTime.UtcNow;

        if (existing is null)
        {
            existing = new OrgInheritanceDefault
            {
                ControlId = request.ControlId,
                InheritanceType = request.InheritanceType,
                Provider = request.Provider,
                SourceCapabilityIds = request.SourceCapabilityIds,
                SourceCapabilityNames = request.SourceCapabilityNames,
                MappingRole = request.MappingRole,
                DerivedAt = now,
            };
            context.OrgInheritanceDefaults.Add(existing);
            _logger.LogInformation(
                "OrgInheritanceDefault inserted for {ControlId} by {DerivedBy} (SourceCspCapabilityId={CspCapabilityId})",
                request.ControlId, request.DerivedBy, request.SourceCspCapabilityId);
        }
        else
        {
            existing.InheritanceType = request.InheritanceType;
            existing.Provider = request.Provider;
            existing.SourceCapabilityIds = request.SourceCapabilityIds;
            existing.SourceCapabilityNames = request.SourceCapabilityNames;
            existing.MappingRole = request.MappingRole;
            existing.DerivedAt = now;
            _logger.LogInformation(
                "OrgInheritanceDefault updated for {ControlId} by {DerivedBy} (SourceCspCapabilityId={CspCapabilityId})",
                request.ControlId, request.DerivedBy, request.SourceCspCapabilityId);
        }

        // T223 will assign request.SourceCspCapabilityId / SourceCspComponentId to
        // matching FK columns on OrgInheritanceDefault once those columns exist.
        // T225 will emit CspCapabilityConsumed via IDomainEventDispatcher when
        // request.SourceCspCapabilityId.HasValue.

        await context.SaveChangesAsync(cancellationToken);
        return existing;
    }
}
