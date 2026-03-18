using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements NIST 800-53 baseline selection, CNSSI 1253 overlay application,
/// control tailoring, inheritance tracking, and CRM generation.
/// </summary>
public class BaselineService : IBaselineService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReferenceDataService _referenceData;
    private readonly ILogger<BaselineService> _logger;

    public BaselineService(
        IServiceScopeFactory scopeFactory,
        IReferenceDataService referenceData,
        ILogger<BaselineService> logger)
    {
        _scopeFactory = scopeFactory;
        _referenceData = referenceData;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ControlBaseline> SelectBaselineAsync(
        string systemId,
        bool applyOverlay = true,
        string? overlayName = null,
        string selectedBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedBy, nameof(selectedBy));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Verify system exists
        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Require existing security categorization
        var categorization = await context.SecurityCategorizations
            .Include(sc => sc.InformationTypes)
            .Include(sc => sc.RegisteredSystem)
            .FirstOrDefaultAsync(sc => sc.RegisteredSystemId == systemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"System '{systemId}' has no security categorization. Run categorize_system first.");

        // Determine baseline level from categorization
        var baselineLevel = ComplianceFrameworks.DeriveBaselineLevel(categorization.OverallCategorization);

        // Load control IDs from reference data
        var controlIds = _referenceData.GetBaselineControlIds(baselineLevel).ToList();
        if (controlIds.Count == 0)
            throw new InvalidOperationException($"No controls found for baseline level '{baselineLevel}'.");

        // Apply CNSSI 1253 overlay if requested
        string? appliedOverlay = null;
        if (applyOverlay)
        {
            var impactLevel = categorization.DoDImpactLevel;
            var overlayEntries = _referenceData.GetOverlayEntries(impactLevel);

            if (overlayEntries.Count > 0)
            {
                // Add overlay enhancement controls that aren't already in the baseline
                foreach (var entry in overlayEntries)
                {
                    foreach (var enhancement in entry.Enhancements)
                    {
                        if (!controlIds.Contains(enhancement))
                            controlIds.Add(enhancement);
                    }

                    // Ensure the overlay's own control is included
                    if (!controlIds.Contains(entry.ControlId))
                        controlIds.Add(entry.ControlId);
                }

                appliedOverlay = overlayName ?? $"CNSSI 1253 {impactLevel}";
                _logger.LogInformation(
                    "Applied overlay '{Overlay}' with {Count} entries to baseline for system '{SystemId}'.",
                    appliedOverlay, overlayEntries.Count, systemId);
            }
        }

        // Sort control IDs for consistent ordering
        controlIds.Sort(ControlIdComparer.Instance);

        // Remove existing baseline if present (full replace)
        var existing = await context.ControlBaselines
            .Include(b => b.Tailorings)
            .Include(b => b.Inheritances)
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken);

        if (existing != null)
        {
            context.ControlTailorings.RemoveRange(existing.Tailorings);
            context.ControlInheritances.RemoveRange(existing.Inheritances);
            context.ControlBaselines.Remove(existing);
            await context.SaveChangesAsync(cancellationToken);
        }

        // Create new baseline
        var baseline = new ControlBaseline
        {
            RegisteredSystemId = systemId,
            BaselineLevel = baselineLevel,
            OverlayApplied = appliedOverlay,
            TotalControls = controlIds.Count,
            ControlIds = controlIds,
            CreatedBy = selectedBy,
            CreatedAt = DateTime.UtcNow
        };

        context.ControlBaselines.Add(baseline);
        system.ModifiedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        // Auto-populate ControlImplementation records with AI-generated narrative templates
        var existingControlIds = await context.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId)
            .Select(ci => ci.ControlId)
            .ToListAsync(cancellationToken);

        // Use case-insensitive lookup (SQL Server collation is CI, but C# Contains is CS)
        var existingSet = new HashSet<string>(existingControlIds, StringComparer.OrdinalIgnoreCase);

        var newImplementations = new List<ControlImplementation>();
        var now = DateTime.UtcNow;

        foreach (var controlId in controlIds)
        {
            if (existingSet.Contains(controlId))
                continue;

            var family = controlId.Contains('-')
                ? controlId[..controlId.IndexOf('-')]
                : controlId.Length >= 2 ? controlId[..2] : controlId;

            newImplementations.Add(new ControlImplementation
            {
                ControlId = controlId,
                RegisteredSystemId = systemId,
                ImplementationStatus = ImplementationStatus.Planned,
                ApprovalStatus = SspSectionStatus.NotStarted,
                Narrative = SspService.GenerateCustomerNarrativeTemplate(family, controlId, system),
                IsAutoPopulated = true,
                AiSuggested = true,
                AuthoredBy = selectedBy,
                AuthoredAt = now,
                CurrentVersion = 1,
            });
        }

        if (newImplementations.Count > 0)
        {
            context.ControlImplementations.AddRange(newImplementations);
            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Auto-populated {Count} control implementations with AI narrative templates for system '{SystemId}'",
                newImplementations.Count, systemId);
        }

        _logger.LogInformation(
            "Selected {Level} baseline for system '{SystemId}': {Count} controls, overlay={Overlay}",
            baselineLevel, systemId, controlIds.Count, appliedOverlay ?? "none");

        return baseline;
    }

    /// <inheritdoc />
    public async Task<TailoringResult> TailorBaselineAsync(
        string systemId,
        IEnumerable<TailoringInput> tailoringActions,
        string tailoredBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(tailoredBy, nameof(tailoredBy));

        var actions = tailoringActions?.ToList()
            ?? throw new ArgumentNullException(nameof(tailoringActions));

        if (actions.Count == 0)
            throw new InvalidOperationException("At least one tailoring action is required.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var baseline = await context.ControlBaselines
            .Include(b => b.Tailorings)
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No baseline found for system '{systemId}'. Run select_baseline first.");

        // Determine overlay-required controls
        HashSet<string> overlayControls = new();
        if (!string.IsNullOrEmpty(baseline.OverlayApplied))
        {
            var ilMatch = System.Text.RegularExpressions.Regex.Match(baseline.OverlayApplied, @"IL\d+");
            if (ilMatch.Success)
            {
                var entries = _referenceData.GetOverlayEntries(ilMatch.Value);
                foreach (var entry in entries)
                {
                    overlayControls.Add(entry.ControlId);
                    foreach (var enh in entry.Enhancements)
                        overlayControls.Add(enh);
                }
            }
        }

        var result = new TailoringResult { Baseline = baseline };

        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.ControlId))
            {
                result.Rejected.Add(new TailoringActionResult
                {
                    ControlId = action.ControlId ?? "",
                    Action = action.Action,
                    Accepted = false,
                    Reason = "Control ID is required."
                });
                continue;
            }

            if (!Enum.TryParse<TailoringAction>(action.Action, true, out var tailoringAction))
            {
                result.Rejected.Add(new TailoringActionResult
                {
                    ControlId = action.ControlId,
                    Action = action.Action,
                    Accepted = false,
                    Reason = $"Invalid action '{action.Action}'. Valid values: Added, Removed."
                });
                continue;
            }

            var isOverlayRequired = overlayControls.Contains(action.ControlId);

            if (tailoringAction == TailoringAction.Removed)
            {
                if (!baseline.ControlIds.Contains(action.ControlId))
                {
                    result.Rejected.Add(new TailoringActionResult
                    {
                        ControlId = action.ControlId,
                        Action = action.Action,
                        Accepted = false,
                        Reason = $"Control '{action.ControlId}' is not in the baseline."
                    });
                    continue;
                }

                // Warn but still allow removal of overlay-required controls (with rationale documented)
                baseline.ControlIds.Remove(action.ControlId);
                baseline.TailoredOutControls++;
            }
            else // Added
            {
                if (baseline.ControlIds.Contains(action.ControlId))
                {
                    result.Rejected.Add(new TailoringActionResult
                    {
                        ControlId = action.ControlId,
                        Action = action.Action,
                        Accepted = false,
                        Reason = $"Control '{action.ControlId}' is already in the baseline."
                    });
                    continue;
                }

                baseline.ControlIds.Add(action.ControlId);
                baseline.TailoredInControls++;
            }

            // Create tailoring record
            var tailoring = new ControlTailoring
            {
                ControlBaselineId = baseline.Id,
                ControlId = action.ControlId,
                Action = tailoringAction,
                Rationale = action.Rationale ?? "No rationale provided",
                IsOverlayRequired = isOverlayRequired,
                TailoredBy = tailoredBy,
                TailoredAt = DateTime.UtcNow
            };

            context.ControlTailorings.Add(tailoring);

            var actionResult = new TailoringActionResult
            {
                ControlId = action.ControlId,
                Action = action.Action,
                Accepted = true
            };

            if (isOverlayRequired && tailoringAction == TailoringAction.Removed)
            {
                actionResult.Reason = "WARNING: Control is required by overlay. Removal documented with rationale.";
            }

            result.Accepted.Add(actionResult);
        }

        // Update baseline counts
        baseline.TotalControls = baseline.ControlIds.Count;
        baseline.ModifiedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Tailored baseline for system '{SystemId}': {Accepted} accepted, {Rejected} rejected",
            systemId, result.Accepted.Count, result.Rejected.Count);

        return result;
    }

    /// <inheritdoc />
    public async Task<InheritanceResult> SetInheritanceAsync(
        string systemId,
        IEnumerable<InheritanceInput> inheritanceMappings,
        string setBy = "mcp-user",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(setBy, nameof(setBy));

        var mappings = inheritanceMappings?.ToList()
            ?? throw new ArgumentNullException(nameof(inheritanceMappings));

        if (mappings.Count == 0)
            throw new InvalidOperationException("At least one inheritance mapping is required.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var baseline = await context.ControlBaselines
            .Include(b => b.Inheritances)
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No baseline found for system '{systemId}'. Run select_baseline first.");

        var result = new InheritanceResult { Baseline = baseline };

        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.ControlId))
            {
                result.SkippedControls.Add(mapping.ControlId ?? "(empty)");
                continue;
            }

            // Validate control is in the baseline
            if (!baseline.ControlIds.Contains(mapping.ControlId))
            {
                result.SkippedControls.Add(mapping.ControlId);
                continue;
            }

            if (!Enum.TryParse<InheritanceType>(mapping.InheritanceType, true, out var inheritanceType))
            {
                result.SkippedControls.Add(mapping.ControlId);
                continue;
            }

            // Upsert: remove existing inheritance for this control if present
            var existing = baseline.Inheritances
                .FirstOrDefault(i => i.ControlId == mapping.ControlId);

            if (existing != null)
            {
                context.ControlInheritances.Remove(existing);
                baseline.Inheritances.Remove(existing);
            }

            var inheritance = new ControlInheritance
            {
                ControlBaselineId = baseline.Id,
                ControlId = mapping.ControlId,
                InheritanceType = inheritanceType,
                Provider = mapping.Provider,
                CustomerResponsibility = mapping.CustomerResponsibility,
                SetBy = setBy,
                SetAt = DateTime.UtcNow
            };

            context.ControlInheritances.Add(inheritance);
            result.ControlsUpdated++;
        }

        // Recalculate inheritance counts
        baseline.InheritedControls = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Inherited);
        baseline.SharedControls = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Shared);
        baseline.CustomerControls = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Customer);
        baseline.ModifiedAt = DateTime.UtcNow;

        result.InheritedCount = baseline.InheritedControls;
        result.SharedCount = baseline.SharedControls;
        result.CustomerCount = baseline.CustomerControls;

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Set inheritance for system '{SystemId}': {Updated} updated, {Skipped} skipped. I={I} S={S} C={C}",
            systemId, result.ControlsUpdated, result.SkippedControls.Count,
            result.InheritedCount, result.SharedCount, result.CustomerCount);

        return result;
    }

    /// <inheritdoc />
    public async Task<ControlBaseline?> GetBaselineAsync(
        string systemId,
        bool includeDetails = false,
        string? familyFilter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        IQueryable<ControlBaseline> query = context.ControlBaselines;

        if (includeDetails)
        {
            query = query
                .Include(b => b.Tailorings)
                .Include(b => b.Inheritances);
        }

        var baseline = await query
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken);

        if (baseline != null && !string.IsNullOrWhiteSpace(familyFilter))
        {
            // Filter control IDs by family prefix
            baseline.ControlIds = baseline.ControlIds
                .Where(c => ComplianceFrameworks.ExtractControlFamily(c)
                    .Equals(familyFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (includeDetails)
            {
                baseline.Tailorings = baseline.Tailorings
                    .Where(t => ComplianceFrameworks.ExtractControlFamily(t.ControlId)
                        .Equals(familyFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                baseline.Inheritances = baseline.Inheritances
                    .Where(i => ComplianceFrameworks.ExtractControlFamily(i.ControlId)
                        .Equals(familyFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        return baseline;
    }

    /// <inheritdoc />
    public async Task<CrmResult> GenerateCrmAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var baseline = await context.ControlBaselines
            .Include(b => b.Inheritances)
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No baseline found for system '{systemId}'. Run select_baseline first.");

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken);

        // Build inheritance lookup
        var inheritanceLookup = baseline.Inheritances
            .ToDictionary(i => i.ControlId, i => i);

        // Group controls by family
        var familyGroups = baseline.ControlIds
            .GroupBy(c => ComplianceFrameworks.ExtractControlFamily(c))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var familyName = ComplianceFrameworks.ControlFamilyNames
                    .GetValueOrDefault(g.Key, g.Key);

                return new CrmFamilyGroup
                {
                    Family = g.Key,
                    FamilyName = familyName,
                    Controls = g.OrderBy(c => c, ControlIdComparer.Instance)
                        .Select(controlId =>
                        {
                            if (inheritanceLookup.TryGetValue(controlId, out var inheritance))
                            {
                                return new CrmEntry
                                {
                                    ControlId = controlId,
                                    InheritanceType = inheritance.InheritanceType.ToString(),
                                    Provider = inheritance.Provider,
                                    CustomerResponsibility = inheritance.CustomerResponsibility
                                };
                            }

                            return new CrmEntry
                            {
                                ControlId = controlId,
                                InheritanceType = "Undesignated"
                            };
                        })
                        .ToList()
                };
            })
            .ToList();

        var inheritedCount = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Inherited);
        var sharedCount = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Shared);
        var customerCount = baseline.Inheritances.Count(i => i.InheritanceType == InheritanceType.Customer);
        var totalDesignated = inheritedCount + sharedCount + customerCount;
        var undesignatedCount = baseline.TotalControls - totalDesignated;

        var inheritancePercentage = baseline.TotalControls > 0
            ? Math.Round((double)(inheritedCount + sharedCount) / baseline.TotalControls * 100, 1)
            : 0.0;

        return new CrmResult
        {
            SystemId = systemId,
            SystemName = system?.Name ?? "Unknown",
            BaselineLevel = baseline.BaselineLevel,
            TotalControls = baseline.TotalControls,
            InheritedControls = inheritedCount,
            SharedControls = sharedCount,
            CustomerControls = customerCount,
            UndesignatedControls = undesignatedCount,
            InheritancePercentage = inheritancePercentage,
            FamilyGroups = familyGroups
        };
    }

    // ─── Control ID comparer ─────────────────────────────────────────────────

    /// <summary>
    /// Compares NIST control IDs by family, then number, then enhancement.
    /// Example ordering: AC-1, AC-2, AC-2(1), AC-2(2), AC-3, AT-1, …
    /// </summary>
    private class ControlIdComparer : IComparer<string>
    {
        public static readonly ControlIdComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            var (xFamily, xNum, xEnh) = Parse(x);
            var (yFamily, yNum, yEnh) = Parse(y);

            var familyCmp = string.Compare(xFamily, yFamily, StringComparison.Ordinal);
            if (familyCmp != 0) return familyCmp;

            var numCmp = xNum.CompareTo(yNum);
            if (numCmp != 0) return numCmp;

            return xEnh.CompareTo(yEnh);
        }

        private static (string family, int number, int enhancement) Parse(string controlId)
        {
            var dashIndex = controlId.IndexOf('-');
            if (dashIndex < 0) return (controlId, 0, 0);

            var family = controlId[..dashIndex];
            var rest = controlId[(dashIndex + 1)..];

            var parenIndex = rest.IndexOf('(');
            if (parenIndex < 0)
            {
                int.TryParse(rest, out var n);
                return (family, n, 0);
            }

            int.TryParse(rest[..parenIndex], out var num);
            var enhStr = rest[(parenIndex + 1)..].TrimEnd(')');
            int.TryParse(enhStr, out var enh);
            return (family, num, enh);
        }
    }
}
