using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements SSP authoring: narrative CRUD, AI suggestions, inherited auto-population,
/// progress tracking, and SSP Markdown document generation.
/// </summary>
/// <remarks>Feature 015 Phase 7 (US5).</remarks>
public class SspService : ISspService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SspService> _logger;

    public SspService(
        IServiceScopeFactory scopeFactory,
        ILogger<SspService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ControlImplementation> WriteNarrativeAsync(
        string systemId,
        string controlId,
        string narrative,
        string? status = null,
        string authoredBy = "mcp-user",
        int? expectedVersion = null,
        string? changeReason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId, nameof(controlId));
        ArgumentException.ThrowIfNullOrWhiteSpace(narrative, nameof(narrative));

        var implStatus = ParseImplementationStatus(status);

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        // Verify system exists
        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Verify control is in baseline (if baseline exists)
        var baseline = await context.ControlBaselines
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken);

        if (baseline != null && !baseline.ControlIds.Contains(controlId.ToUpperInvariant()) &&
            !baseline.ControlIds.Contains(controlId))
        {
            _logger.LogWarning(
                "Control '{ControlId}' not in baseline for system '{SystemId}', proceeding anyway",
                controlId, systemId);
        }

        // Check for existing narrative (upsert)
        var existing = await context.ControlImplementations
            .FirstOrDefaultAsync(ci =>
                ci.RegisteredSystemId == systemId && ci.ControlId == controlId,
                cancellationToken);

        if (existing != null)
        {
            // Guard: reject writes when narrative is under review (FR-010)
            if (existing.ApprovalStatus == SspSectionStatus.UnderReview)
                throw new InvalidOperationException(
                    $"UNDER_REVIEW: Cannot modify narrative for control '{controlId}' while it is under review.");

            // Optimistic concurrency check (FR-017)
            if (expectedVersion.HasValue && expectedVersion.Value != existing.CurrentVersion)
                throw new InvalidOperationException(
                    $"CONCURRENCY_CONFLICT: Expected version {expectedVersion.Value} but current version is {existing.CurrentVersion}. " +
                    $"Last modified by '{existing.AuthoredBy}' at {existing.ModifiedAt?.ToString("O") ?? existing.AuthoredAt.ToString("O")}.");

            existing.Narrative = narrative.Trim();
            existing.ImplementationStatus = implStatus;
            existing.ModifiedAt = DateTime.UtcNow;
            existing.AiSuggested = false;
            existing.IsAutoPopulated = false;
            existing.CurrentVersion += 1;
            existing.ApprovalStatus = SspSectionStatus.Draft;
            existing.AuthoredBy = authoredBy;

            // Create immutable version snapshot
            var version = new NarrativeVersion
            {
                ControlImplementationId = existing.Id,
                VersionNumber = existing.CurrentVersion,
                Content = narrative.Trim(),
                Status = SspSectionStatus.Draft,
                AuthoredBy = authoredBy,
                AuthoredAt = DateTime.UtcNow,
                ChangeReason = changeReason
            };
            context.NarrativeVersions.Add(version);

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated narrative for control '{ControlId}' in system '{SystemId}' (version={Version})",
                controlId, systemId, existing.CurrentVersion);

            return existing;
        }

        // Create new narrative
        var implementation = new ControlImplementation
        {
            RegisteredSystemId = systemId,
            ControlId = controlId.Trim(),
            Narrative = narrative.Trim(),
            ImplementationStatus = implStatus,
            AuthoredBy = authoredBy,
            AuthoredAt = DateTime.UtcNow,
            CurrentVersion = 1,
            ApprovalStatus = SspSectionStatus.Draft
        };

        context.ControlImplementations.Add(implementation);

        // Create initial version snapshot
        var initialVersion = new NarrativeVersion
        {
            ControlImplementationId = implementation.Id,
            VersionNumber = 1,
            Content = narrative.Trim(),
            Status = SspSectionStatus.Draft,
            AuthoredBy = authoredBy,
            AuthoredAt = DateTime.UtcNow,
            ChangeReason = changeReason ?? "Initial narrative"
        };
        context.NarrativeVersions.Add(initialVersion);

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created narrative for control '{ControlId}' in system '{SystemId}' (status={Status}, version=1)",
            controlId, systemId, implStatus);

        return implementation;
    }

    /// <inheritdoc />
    public async Task<NarrativeSuggestion> SuggestNarrativeAsync(
        string systemId,
        string controlId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(controlId, nameof(controlId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Check if there's an inheritance mapping for this control
        var baseline = await context.ControlBaselines
            .Include(b => b.Inheritances)
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken);

        var inheritance = baseline?.Inheritances
            .FirstOrDefault(i => i.ControlId == controlId);

        // Build suggestion based on system context and control type
        var sb = new StringBuilder();
        var references = new List<string>();
        double confidence = 0.5;

        if (inheritance?.InheritanceType == InheritanceType.Inherited)
        {
            var provider = inheritance.Provider ?? "the cloud service provider (CSP)";
            sb.AppendLine($"This control is fully inherited from {provider}.");
            sb.AppendLine();
            sb.AppendLine($"The {system.Name} system inherits the implementation of {controlId} from {provider}, ");
            sb.AppendLine($"which maintains a FedRAMP High authorization. The CSP is responsible for the full ");
            sb.AppendLine($"implementation and ongoing assessment of this control within the {system.HostingEnvironment} environment.");
            confidence = 0.85;
            references.Add($"FedRAMP High Authorization — {provider}");
            references.Add($"Control Inheritance Matrix for {system.Name}");
        }
        else if (inheritance?.InheritanceType == InheritanceType.Shared)
        {
            var provider = inheritance.Provider ?? "the cloud service provider (CSP)";
            sb.AppendLine($"This is a shared control between {provider} and {system.Name}.");
            sb.AppendLine();
            sb.AppendLine($"CSP Responsibility: {provider} provides the underlying infrastructure ");
            sb.AppendLine($"and platform-level implementation of {controlId}.");
            sb.AppendLine();
            sb.AppendLine($"Customer Responsibility: The {system.Name} team is responsible for ");
            sb.Append(inheritance.CustomerResponsibility ?? $"configuring and managing the application-level aspects of {controlId}.");
            confidence = 0.75;
            references.Add($"FedRAMP Shared Responsibility — {provider}");
            references.Add($"Customer Responsibility Matrix for {system.Name}");
        }
        else
        {
            // Customer-implemented control — provide template based on control family
            var family = controlId.Split('-')[0].ToUpperInvariant();
            var narrative = GenerateCustomerNarrativeTemplate(family, controlId, system);
            sb.Append(narrative);
            confidence = 0.55;
            references.Add($"NIST SP 800-53 Rev. 5 — {controlId}");
            references.Add($"{system.Name} System Architecture");
        }

        return new NarrativeSuggestion
        {
            ControlId = controlId,
            Narrative = sb.ToString().Trim(),
            Confidence = confidence,
            References = references
        };
    }

    /// <inheritdoc />
    public async Task<BatchPopulateResult> BatchPopulateNarrativesAsync(
        string systemId,
        string? inheritanceType = null,
        string authoredBy = "mcp-user",
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var baseline = await context.ControlBaselines
            .Include(b => b.Inheritances)
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"No baseline found for system '{systemId}'. Select a baseline first.");

        // Get existing narratives to skip
        var existingControlIds = await context.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId)
            .Select(ci => ci.ControlId)
            .ToListAsync(cancellationToken);

        var existingSet = new HashSet<string>(existingControlIds, StringComparer.OrdinalIgnoreCase);

        // Filter inheritance records
        var inheritances = baseline.Inheritances.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(inheritanceType))
        {
            if (!Enum.TryParse<InheritanceType>(inheritanceType, true, out var parsedType))
                throw new InvalidOperationException($"Invalid inheritance_type: '{inheritanceType}'. Use 'Inherited' or 'Shared'.");

            inheritances = inheritances.Where(i => i.InheritanceType == parsedType);
        }
        else
        {
            // Default: both Inherited and Shared
            inheritances = inheritances.Where(i =>
                i.InheritanceType == InheritanceType.Inherited ||
                i.InheritanceType == InheritanceType.Shared);
        }

        var result = new BatchPopulateResult();
        var inheritanceList = inheritances.ToList();
        var totalToProcess = inheritanceList.Count;
        var processed = 0;

        progress?.Report($"Starting batch populate for {totalToProcess} controls...");

        foreach (var inh in inheritanceList)
        {
            processed++;
            if (existingSet.Contains(inh.ControlId))
            {
                result.SkippedCount++;
                result.SkippedControlIds.Add(inh.ControlId);
                continue;
            }

            var narrative = GenerateInheritedNarrative(inh, system.Name, system.HostingEnvironment);

            var implementation = new ControlImplementation
            {
                RegisteredSystemId = systemId,
                ControlId = inh.ControlId,
                Narrative = narrative,
                ImplementationStatus = inh.InheritanceType == InheritanceType.Inherited
                    ? ImplementationStatus.Implemented
                    : ImplementationStatus.PartiallyImplemented,
                IsAutoPopulated = true,
                AuthoredBy = authoredBy,
                AuthoredAt = DateTime.UtcNow
            };

            context.ControlImplementations.Add(implementation);
            existingSet.Add(inh.ControlId);
            result.PopulatedCount++;
            result.PopulatedControlIds.Add(inh.ControlId);

            // Report progress every 10 controls or on the last one
            if (processed % 10 == 0 || processed == totalToProcess)
            {
                progress?.Report($"Populated {result.PopulatedCount}/{totalToProcess} controls ({processed * 100 / totalToProcess}%)");
            }
        }

        if (result.PopulatedCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Batch populated {Count} narratives for system '{SystemId}' (skipped {Skipped})",
            result.PopulatedCount, systemId, result.SkippedCount);

        return result;
    }

    /// <inheritdoc />
    public async Task<NarrativeProgress> GetNarrativeProgressAsync(
        string systemId,
        string? familyFilter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var baseline = await context.ControlBaselines
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"No baseline found for system '{systemId}'.");

        var controlIds = baseline.ControlIds;
        if (!string.IsNullOrWhiteSpace(familyFilter))
        {
            controlIds = controlIds
                .Where(c => c.StartsWith(familyFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        // Get all narratives for this system
        var narratives = await context.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId)
            .ToListAsync(cancellationToken);

        var narrativeMap = narratives
            .ToDictionary(n => n.ControlId, n => n, StringComparer.OrdinalIgnoreCase);

        var progress = new NarrativeProgress { SystemId = systemId };
        var familyGroups = controlIds
            .GroupBy(c => c.Split('-')[0].ToUpperInvariant())
            .OrderBy(g => g.Key);

        foreach (var familyGroup in familyGroups)
        {
            var fp = new FamilyProgress { Family = familyGroup.Key };

            foreach (var controlId in familyGroup)
            {
                fp.Total++;
                if (narrativeMap.TryGetValue(controlId, out var n) && !string.IsNullOrWhiteSpace(n.Narrative))
                {
                    if (n.ImplementationStatus == ImplementationStatus.Implemented ||
                        n.ImplementationStatus == ImplementationStatus.NotApplicable)
                    {
                        fp.Completed++;
                    }
                    else
                    {
                        fp.Draft++;
                    }
                }
                else
                {
                    fp.Missing++;
                }
            }

            progress.FamilyBreakdowns.Add(fp);
            progress.TotalControls += fp.Total;
            progress.CompletedNarratives += fp.Completed;
            progress.DraftNarratives += fp.Draft;
            progress.MissingNarratives += fp.Missing;
        }

        progress.OverallPercentage = progress.TotalControls > 0
            ? Math.Round((double)(progress.CompletedNarratives + progress.DraftNarratives) / progress.TotalControls * 100, 2)
            : 0;

        return progress;
    }

    /// <inheritdoc />
    public async Task<SspDocument> GenerateSspAsync(
        string systemId,
        string format = "markdown",
        IEnumerable<string>? sections = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var categorization = await context.SecurityCategorizations
            .Include(sc => sc.InformationTypes)
            .FirstOrDefaultAsync(sc => sc.RegisteredSystemId == systemId, cancellationToken);

        var baseline = await context.ControlBaselines
            .Include(b => b.Inheritances)
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken);

        var narratives = await context.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId)
            .OrderBy(ci => ci.ControlId)
            .ToListAsync(cancellationToken);

        // Load approved NarrativeVersion content for SSP generation (Feature 024)
        var approvedVersionIds = narratives
            .Where(ci => !string.IsNullOrWhiteSpace(ci.ApprovedVersionId))
            .Select(ci => ci.ApprovedVersionId!)
            .ToList();

        var approvedVersions = approvedVersionIds.Count > 0
            ? await context.Set<NarrativeVersion>()
                .AsNoTracking()
                .Where(nv => approvedVersionIds.Contains(nv.Id))
                .ToDictionaryAsync(nv => nv.ControlImplementationId, nv => nv.Content, cancellationToken)
            : new Dictionary<string, string>();

        var roles = await context.RmfRoleAssignments
            .Where(r => r.RegisteredSystemId == systemId && r.IsActive)
            .ToListAsync(cancellationToken);

        var interconnections = await context.SystemInterconnections
            .Include(ic => ic.Agreements)
            .Where(ic => ic.RegisteredSystemId == systemId && ic.Status != InterconnectionStatus.Terminated)
            .ToListAsync(cancellationToken);

        var boundaryDefinitions = await context.AuthorizationBoundaryDefinitions
            .Where(bd => bd.RegisteredSystemId == systemId)
            .OrderBy(bd => bd.IsPrimary ? 0 : 1).ThenBy(bd => bd.Name)
            .ToListAsync(cancellationToken);

        var boundaries = await context.AuthorizationBoundaries
            .Include(b => b.AuthorizationBoundaryDefinition)
            .Where(b => b.RegisteredSystemId == systemId && b.IsInBoundary)
            .ToListAsync(cancellationToken);

        var inventoryItems = await context.InventoryItems
            .Include(i => i.BoundaryResource)
            .Where(i => i.RegisteredSystemId == systemId && i.Status != InventoryItemStatus.Decommissioned)
            .OrderBy(i => i.Type).ThenBy(i => i.ItemName)
            .ToListAsync(cancellationToken);

        var components = await context.SystemComponents
            .Where(c => c.RegisteredSystemId == systemId && c.Status == ComponentStatus.Active)
            .OrderBy(c => c.ComponentType).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var sspSections = await context.SspSections
            .Where(s => s.RegisteredSystemId == systemId)
            .ToDictionaryAsync(s => s.SectionNumber, cancellationToken);

        var contingencyPlan = await context.ContingencyPlanReferences
            .FirstOrDefaultAsync(c => c.RegisteredSystemId == systemId, cancellationToken);

        var tailorings = await context.ControlTailorings
            .Where(t => t.ControlBaselineId == (baseline != null ? baseline.Id : ""))
            .ToListAsync(cancellationToken);

        // Resolve which section numbers to include
        var sectionList = sections?.ToList();
        var includeAll = sectionList == null || sectionList.Count == 0;
        var includedNumbers = new HashSet<int>();

        if (includeAll)
        {
            for (var i = 1; i <= 13; i++) includedNumbers.Add(i);
        }
        else
        {
            foreach (var key in sectionList!)
            {
                var num = ResolveSectionKey(key);
                if (num.HasValue)
                    includedNumbers.Add(num.Value);
            }
        }

        var doc = new SspDocument
        {
            SystemId = systemId,
            SystemName = system.Name,
            Format = format
        };

        var sb = new StringBuilder();
        var includedSectionKeys = new List<string>();

        progress?.Report("Loading system data for SSP generation...");

        // ─── YAML Front-Matter (T044) ────────────────────────────────────
        var approvedCount = sspSections.Values.Count(s => s.Status == SspSectionStatus.Approved);
        var narrativeCompletion = baseline != null && baseline.TotalControls > 0
            ? Math.Round((double)narratives.Count(n => !string.IsNullOrWhiteSpace(n.Narrative)) / baseline.TotalControls * 100, 1)
            : 0;

        sb.AppendLine("---");
        sb.AppendLine($"document_version: \"1.0\"");
        sb.AppendLine($"generated_at: \"{DateTime.UtcNow:O}\"");
        sb.AppendLine($"system_name: \"{system.Name}\"");
        sb.AppendLine($"system_id: \"{system.Id}\"");
        sb.AppendLine($"categorization_level: \"{categorization?.OverallCategorization.ToString().ToLowerInvariant() ?? "not-determined"}\"");
        sb.AppendLine($"baseline_level: \"{baseline?.BaselineLevel ?? "none"}\"");
        sb.AppendLine($"narrative_completion_percent: {narrativeCompletion}");
        sb.AppendLine($"section_completion: \"{approvedCount}/13\"");
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine("# System Security Plan (SSP)");
        sb.AppendLine();

        // ─── Generate each section in NIST 800-18 order (T043/T045) ──────
        for (var sectionNum = 1; sectionNum <= 13; sectionNum++)
        {
            if (!includedNumbers.Contains(sectionNum)) continue;

            var title = SectionTitles[sectionNum];
            var sectionKey = SectionKeyToNumber.FirstOrDefault(kvp => kvp.Value == sectionNum && !kvp.Key.Equals("system_information", StringComparison.OrdinalIgnoreCase) && !kvp.Key.Equals("baseline", StringComparison.OrdinalIgnoreCase) && !kvp.Key.Equals("controls", StringComparison.OrdinalIgnoreCase)).Key
                ?? SectionKeyToNumber.First(kvp => kvp.Value == sectionNum).Key;
            includedSectionKeys.Add(sectionKey);

            progress?.Report($"Generating §{sectionNum} {title}...");

            // Completeness markers (T045)
            string marker = "";
            if (sspSections.TryGetValue(sectionNum, out var storedSection))
            {
                if (storedSection.Status == SspSectionStatus.Draft)
                    marker = " [UNAPPROVED]";
                else if (storedSection.Status == SspSectionStatus.UnderReview)
                    marker = " [UNAPPROVED]";
                if (storedSection.HasManualOverride && AutoGeneratedSections.Contains(sectionNum))
                    marker += " [MANUAL OVERRIDE]";
            }
            else if (AuthoredSections.Contains(sectionNum) || sectionNum == HybridSection)
            {
                marker = " [NOT STARTED]";
            }

            sb.AppendLine($"## {sectionNum}. {title}{marker}");
            sb.AppendLine();

            // Generate content based on section type
            string content;
            if (AutoGeneratedSections.Contains(sectionNum))
            {
                content = sectionNum switch
                {
                    1 => GenerateSection1Content(system, roles),
                    2 => GenerateSection2Content(categorization),
                    3 => GenerateSection3Content(roles),
                    4 => GenerateSection4Content(system),
                    7 => GenerateSection7Content(interconnections, system, storedSection),
                    9 => GenerateSection9Content(baseline, narratives),
                    10 => GenerateSection10Content(system, baseline, narratives, approvedVersions),
                    11 => GenerateSection11Content(boundaryDefinitions, boundaries, inventoryItems, components, storedSection),
                    _ => ""
                };

                // If there's a manual override in stored section, use that instead
                if (storedSection?.HasManualOverride == true && !string.IsNullOrWhiteSpace(storedSection.Content))
                    content = storedSection.Content;
            }
            else if (sectionNum == HybridSection)
            {
                var autoContent = GenerateSection6Content(system, storedSection);
                content = autoContent;
            }
            else // Authored sections
            {
                if (storedSection != null && !string.IsNullOrWhiteSpace(storedSection.Content))
                {
                    content = sectionNum switch
                    {
                        5 => storedSection.Content,
                        8 => storedSection.Content,
                        12 => GenerateSection12Content(roles, storedSection),
                        13 => GenerateSection13Content(contingencyPlan, storedSection),
                        _ => storedSection.Content
                    };
                }
                else
                {
                    content = "[Section not yet authored]";
                    doc.Warnings.Add($"§{sectionNum} {title}: authored content is missing.");
                }
            }

            sb.AppendLine(content);
            sb.AppendLine();

            // Track control statistics from §10
            if (sectionNum == 10)
            {
                var controlIds = baseline?.ControlIds ?? new List<string>();
                doc.TotalControls = controlIds.Count;
                var narrativeMap = narratives.ToDictionary(n => n.ControlId, StringComparer.OrdinalIgnoreCase);
                doc.ControlsWithNarratives = controlIds.Count(c => narrativeMap.ContainsKey(c));
                doc.ControlsMissingNarratives = doc.TotalControls - doc.ControlsWithNarratives;

                if (doc.ControlsMissingNarratives > 0)
                    doc.Warnings.Add($"{doc.ControlsMissingNarratives} controls are missing implementation narratives.");
            }
        }

        doc.Content = sb.ToString();
        doc.Sections = includedSectionKeys;

        _logger.LogInformation(
            "Generated 13-section SSP for system '{SystemId}': {Sections} sections, {Controls} controls, {Warnings} warnings",
            systemId, includedSectionKeys.Count, doc.TotalControls, doc.Warnings.Count);

        return doc;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<(int SectionNumber, string Content)> StreamSspSectionsAsync(
        string systemId,
        IEnumerable<string>? sections = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var categorization = await context.SecurityCategorizations
            .Include(sc => sc.InformationTypes)
            .FirstOrDefaultAsync(sc => sc.RegisteredSystemId == systemId, cancellationToken);

        var baseline = await context.ControlBaselines
            .Include(b => b.Inheritances)
            .FirstOrDefaultAsync(b => b.RegisteredSystemId == systemId, cancellationToken);

        var narratives = await context.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId)
            .OrderBy(ci => ci.ControlId)
            .ToListAsync(cancellationToken);

        var approvedVersionIds = narratives
            .Where(ci => !string.IsNullOrWhiteSpace(ci.ApprovedVersionId))
            .Select(ci => ci.ApprovedVersionId!)
            .ToList();

        var approvedVersions = approvedVersionIds.Count > 0
            ? await context.Set<NarrativeVersion>()
                .AsNoTracking()
                .Where(nv => approvedVersionIds.Contains(nv.Id))
                .ToDictionaryAsync(nv => nv.ControlImplementationId, nv => nv.Content, cancellationToken)
            : new Dictionary<string, string>();

        var roles = await context.RmfRoleAssignments
            .Where(r => r.RegisteredSystemId == systemId && r.IsActive)
            .ToListAsync(cancellationToken);

        var interconnections = await context.SystemInterconnections
            .Include(ic => ic.Agreements)
            .Where(ic => ic.RegisteredSystemId == systemId && ic.Status != InterconnectionStatus.Terminated)
            .ToListAsync(cancellationToken);

        var boundaryDefinitions = await context.AuthorizationBoundaryDefinitions
            .Where(bd => bd.RegisteredSystemId == systemId)
            .OrderBy(bd => bd.IsPrimary ? 0 : 1).ThenBy(bd => bd.Name)
            .ToListAsync(cancellationToken);

        var boundaries = await context.AuthorizationBoundaries
            .Include(b => b.AuthorizationBoundaryDefinition)
            .Where(b => b.RegisteredSystemId == systemId && b.IsInBoundary)
            .ToListAsync(cancellationToken);

        var inventoryItems = await context.InventoryItems
            .Include(i => i.BoundaryResource)
            .Where(i => i.RegisteredSystemId == systemId && i.Status != InventoryItemStatus.Decommissioned)
            .OrderBy(i => i.Type).ThenBy(i => i.ItemName)
            .ToListAsync(cancellationToken);

        var components = await context.SystemComponents
            .Where(c => c.RegisteredSystemId == systemId && c.Status == ComponentStatus.Active)
            .OrderBy(c => c.ComponentType).ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var sspSections = await context.SspSections
            .Where(s => s.RegisteredSystemId == systemId)
            .ToDictionaryAsync(s => s.SectionNumber, cancellationToken);

        var contingencyPlan = await context.ContingencyPlanReferences
            .FirstOrDefaultAsync(c => c.RegisteredSystemId == systemId, cancellationToken);

        var sectionList = sections?.ToList();
        var includeAll = sectionList == null || sectionList.Count == 0;
        var includedNumbers = new HashSet<int>();

        if (includeAll)
        {
            for (var i = 1; i <= 13; i++) includedNumbers.Add(i);
        }
        else
        {
            foreach (var key in sectionList!)
            {
                var num = ResolveSectionKey(key);
                if (num.HasValue) includedNumbers.Add(num.Value);
            }
        }

        for (var sectionNum = 1; sectionNum <= 13; sectionNum++)
        {
            if (!includedNumbers.Contains(sectionNum)) continue;
            cancellationToken.ThrowIfCancellationRequested();

            sspSections.TryGetValue(sectionNum, out var storedSection);

            string content;
            if (AutoGeneratedSections.Contains(sectionNum))
            {
                content = sectionNum switch
                {
                    1 => GenerateSection1Content(system, roles),
                    2 => GenerateSection2Content(categorization),
                    3 => GenerateSection3Content(roles),
                    4 => GenerateSection4Content(system),
                    7 => GenerateSection7Content(interconnections, system, storedSection),
                    9 => GenerateSection9Content(baseline, narratives),
                    10 => GenerateSection10Content(system, baseline, narratives, approvedVersions),
                    11 => GenerateSection11Content(boundaryDefinitions, boundaries, inventoryItems, components, storedSection),
                    _ => ""
                };
                if (storedSection?.HasManualOverride == true && !string.IsNullOrWhiteSpace(storedSection.Content))
                    content = storedSection.Content;
            }
            else if (sectionNum == HybridSection)
            {
                content = GenerateSection6Content(system, storedSection);
            }
            else
            {
                if (storedSection != null && !string.IsNullOrWhiteSpace(storedSection.Content))
                {
                    content = sectionNum switch
                    {
                        5 => storedSection.Content,
                        8 => storedSection.Content,
                        12 => GenerateSection12Content(roles, storedSection),
                        13 => GenerateSection13Content(contingencyPlan, storedSection),
                        _ => storedSection.Content
                    };
                }
                else
                {
                    content = "[Section not yet authored]";
                }
            }

            yield return (sectionNum, content);
        }
    }

    // ─── §10 Control Implementations Generator ──────────────────────────────

    private static string GenerateSection10Content(
        RegisteredSystem system,
        ControlBaseline? baseline,
        List<ControlImplementation> narratives,
        Dictionary<string, string> approvedVersions)
    {
        var sb = new StringBuilder();
        var controlIds = baseline?.ControlIds ?? new List<string>();

        if (controlIds.Count == 0)
        {
            sb.AppendLine("*No control baseline selected. Control implementations cannot be generated.*");
            return sb.ToString();
        }

        var narrativeMap = narratives.ToDictionary(n => n.ControlId, n => n, StringComparer.OrdinalIgnoreCase);
        var inheritanceMap = baseline?.Inheritances
            ?.ToDictionary(i => i.ControlId, i => i, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ControlInheritance>(StringComparer.OrdinalIgnoreCase);

        var familyGroups = controlIds
            .GroupBy(c => c.Split('-')[0].ToUpperInvariant())
            .OrderBy(g => g.Key);

        foreach (var family in familyGroups)
        {
            var familyTotal = family.Count();
            var familyWithNarrative = family.Count(c => narrativeMap.ContainsKey(c));
            var familyPercent = familyTotal > 0 ? Math.Round((double)familyWithNarrative / familyTotal * 100, 0) : 0;

            sb.AppendLine($"### {family.Key} Family ({familyPercent}% documented)");
            sb.AppendLine();

            foreach (var controlId in family.OrderBy(c => c))
            {
                sb.AppendLine($"#### {controlId}");
                sb.AppendLine();

                if (narrativeMap.TryGetValue(controlId, out var impl))
                {
                    sb.AppendLine($"**Status**: {impl.ImplementationStatus}");
                    if (inheritanceMap.TryGetValue(controlId, out var inh))
                        sb.AppendLine($"**Responsibility**: {inh.InheritanceType}");

                    // Prefer approved version content; fall back to draft narrative
                    if (approvedVersions.TryGetValue(impl.Id, out var approvedContent))
                    {
                        sb.AppendLine($"**Approval Status**: Approved");
                        sb.AppendLine();
                        sb.AppendLine(approvedContent);
                    }
                    else if (!string.IsNullOrWhiteSpace(impl.Narrative))
                    {
                        sb.AppendLine($"**Approval Status**: {impl.ApprovalStatus} ⚠️ No approved version");
                        sb.AppendLine();
                        sb.AppendLine(impl.Narrative);
                    }
                    else
                    {
                        sb.AppendLine();
                        sb.AppendLine("*No narrative provided.*");
                    }
                }
                else
                {
                    if (inheritanceMap.TryGetValue(controlId, out var inh))
                        sb.AppendLine($"**Responsibility**: {inh.InheritanceType}");
                    sb.AppendLine();
                    sb.AppendLine("*Implementation narrative not yet documented.*");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────

    private static ImplementationStatus ParseImplementationStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return ImplementationStatus.Implemented;

        return status.ToLowerInvariant() switch
        {
            "implemented" => ImplementationStatus.Implemented,
            "partiallyimplemented" or "partially_implemented" => ImplementationStatus.PartiallyImplemented,
            "planned" => ImplementationStatus.Planned,
            "notapplicable" or "not_applicable" => ImplementationStatus.NotApplicable,
            _ => throw new InvalidOperationException(
                $"Invalid status: '{status}'. Valid values: Implemented, PartiallyImplemented, Planned, NotApplicable")
        };
    }

    private static string GenerateInheritedNarrative(
        ControlInheritance inheritance,
        string systemName,
        string hostingEnvironment)
    {
        var provider = inheritance.Provider ?? "the cloud service provider (CSP)";

        if (inheritance.InheritanceType == InheritanceType.Inherited)
        {
            return $"This control is fully inherited from {provider}. " +
                   $"The {systemName} system operates within the {hostingEnvironment} environment, " +
                   $"where {provider} maintains the complete implementation of {inheritance.ControlId} " +
                   $"as part of its FedRAMP High authorization.";
        }

        var customerPart = !string.IsNullOrWhiteSpace(inheritance.CustomerResponsibility)
            ? inheritance.CustomerResponsibility
            : $"configuring application-level settings for {inheritance.ControlId}";

        return $"This control is shared between {provider} and {systemName}. " +
               $"{provider} provides the platform-level implementation within {hostingEnvironment}. " +
               $"The {systemName} team is responsible for {customerPart}.";
    }

    internal static string GenerateCustomerNarrativeTemplate(
        string family,
        string controlId,
        RegisteredSystem system)
    {
        return family switch
        {
            "AC" => $"The {system.Name} system implements {controlId} through Azure Active Directory " +
                     $"(Entra ID) integration within the {system.HostingEnvironment} environment. " +
                     "Access control policies are enforced through conditional access policies, " +
                     "role-based access control (RBAC), and multi-factor authentication (MFA).",

            "AU" => $"The {system.Name} system implements {controlId} using Azure Monitor, " +
                     "Log Analytics, and Microsoft Defender for Cloud. Audit logs are retained " +
                     "per DoD retention requirements and are protected against unauthorized modification.",

            "CM" => $"The {system.Name} system implements {controlId} through Azure Policy, " +
                     "Azure Resource Manager templates, and Infrastructure as Code (IaC) practices. " +
                     "Configuration baselines are enforced and monitored continuously.",

            "IA" => $"The {system.Name} system implements {controlId} using Azure Active Directory " +
                     $"(Entra ID) with CAC/PIV authentication in the {system.HostingEnvironment} " +
                     "environment. Identity verification follows DoD identity proofing standards.",

            "SC" => $"The {system.Name} system implements {controlId} through Azure network security " +
                     "controls including Network Security Groups (NSGs), Azure Firewall, and TLS 1.2+ " +
                     "encryption for all data in transit.",

            "SI" => $"The {system.Name} system implements {controlId} using Microsoft Defender for Cloud, " +
                     "Azure Security Center, and automated vulnerability scanning. System integrity is " +
                     "monitored continuously with alerts for anomalous behavior.",

            _ => $"The {system.Name} system implements {controlId} within the {system.HostingEnvironment} " +
                  "environment. [Implementation details to be documented by the system engineering team.]"
        };
    }

    // ─── NIST 800-18 Section Title Map (Feature 022) ─────────────────────────

    internal static readonly IReadOnlyDictionary<int, string> SectionTitles = new Dictionary<int, string>
    {
        [1] = "System Identification",
        [2] = "Security Categorization",
        [3] = "System Owner / Authorizing Official",
        [4] = "Information System Type",
        [5] = "General Description / Purpose",
        [6] = "System Environment",
        [7] = "System Interconnections",
        [8] = "Related Laws / Regulations",
        [9] = "Minimum Security Controls",
        [10] = "Control Implementations",
        [11] = "Authorization Boundary",
        [12] = "Personnel Security",
        [13] = "Contingency Plan"
    };

    /// <summary>Auto-generated section numbers (content generated from entity data).</summary>
    private static readonly HashSet<int> AutoGeneratedSections = new() { 1, 2, 3, 4, 7, 9, 10, 11 };

    /// <summary>Authored sections requiring manual content.</summary>
    private static readonly HashSet<int> AuthoredSections = new() { 5, 8, 12, 13 };

    /// <summary>Hybrid section (auto-populated + authored narrative).</summary>
    private const int HybridSection = 6;

    // ─── Section Key Map (Feature 022 — T042) ───────────────────────────────

    /// <summary>New section keys for NIST 800-18 §1–§13.</summary>
    internal static readonly IReadOnlyDictionary<string, int> SectionKeyToNumber = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["system_identification"] = 1,
        ["categorization"] = 2,
        ["personnel"] = 3,
        ["system_type"] = 4,
        ["description"] = 5,
        ["environment"] = 6,
        ["interconnections"] = 7,
        ["laws_regulations"] = 8,
        ["minimum_controls"] = 9,
        ["control_implementations"] = 10,
        ["authorization_boundary"] = 11,
        ["personnel_security"] = 12,
        ["contingency_plan"] = 13,
        // Backward-compatible old keys
        ["system_information"] = 1,
        ["baseline"] = 9,
        ["controls"] = 10
    };

    /// <summary>
    /// Resolve a section key (new or old) to a section number.
    /// Returns null if the key is not recognized.
    /// </summary>
    internal static int? ResolveSectionKey(string key) =>
        SectionKeyToNumber.TryGetValue(key, out var num) ? num : null;

    // ─── SSP Section Management Methods (Feature 022) ────────────────────────

    /// <inheritdoc />
    public async Task<SspSection> WriteSspSectionAsync(
        string registeredSystemId,
        int sectionNumber,
        string? content,
        string authoredBy,
        int? expectedVersion = null,
        bool submitForReview = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredSystemId, nameof(registeredSystemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(authoredBy, nameof(authoredBy));

        if (sectionNumber < 1 || sectionNumber > 13)
            throw new InvalidOperationException($"INVALID_SECTION_NUMBER: Section number must be between 1 and 13, got {sectionNumber}.");

        // Authored and hybrid sections require content
        if ((AuthoredSections.Contains(sectionNumber) || sectionNumber == HybridSection)
            && string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("CONTENT_REQUIRED: Authored section requires content.");
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == registeredSystemId, cancellationToken)
            ?? throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{registeredSystemId}' not found.");

        var existing = await context.SspSections
            .FirstOrDefaultAsync(s => s.RegisteredSystemId == registeredSystemId
                && s.SectionNumber == sectionNumber, cancellationToken);

        bool isAutoGen = AutoGeneratedSections.Contains(sectionNumber);

        if (existing != null)
        {
            // Concurrency check
            if (expectedVersion.HasValue && expectedVersion.Value != existing.Version)
                throw new InvalidOperationException(
                    $"CONCURRENCY_CONFLICT: Expected version {expectedVersion.Value} but current is {existing.Version}.");

            existing.Content = content;
            existing.AuthoredBy = authoredBy;
            existing.AuthoredAt = DateTime.UtcNow;
            existing.Version++;
            existing.Status = SspSectionStatus.Draft; // Reset to Draft on any update

            // Auto-gen section with user-provided content = manual override
            if (isAutoGen && !string.IsNullOrWhiteSpace(content))
                existing.HasManualOverride = true;

            if (submitForReview)
            {
                if (existing.Status != SspSectionStatus.Draft)
                    throw new InvalidOperationException(
                        "INVALID_STATUS_FOR_SUBMIT: Only sections in Draft status can be submitted for review.");
                existing.Status = SspSectionStatus.UnderReview;
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated SSP section §{SectionNumber} for system '{SystemId}' (v{Version})",
                sectionNumber, registeredSystemId, existing.Version);

            return existing;
        }

        // Create new section
        var section = new SspSection
        {
            RegisteredSystemId = registeredSystemId,
            SectionNumber = sectionNumber,
            SectionTitle = SectionTitles[sectionNumber],
            Content = content,
            Status = SspSectionStatus.Draft,
            IsAutoGenerated = isAutoGen,
            HasManualOverride = isAutoGen && !string.IsNullOrWhiteSpace(content),
            AuthoredBy = authoredBy,
            AuthoredAt = DateTime.UtcNow,
            Version = 1
        };

        if (submitForReview)
        {
            section.Status = SspSectionStatus.UnderReview;
        }

        context.SspSections.Add(section);
        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created SSP section §{SectionNumber} '{Title}' for system '{SystemId}'",
            sectionNumber, section.SectionTitle, registeredSystemId);

        return section;
    }

    /// <inheritdoc />
    public async Task<SspSection> ReviewSspSectionAsync(
        string registeredSystemId,
        int sectionNumber,
        string decision,
        string reviewer,
        string? comments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredSystemId, nameof(registeredSystemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(decision, nameof(decision));
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewer, nameof(reviewer));

        if (sectionNumber < 1 || sectionNumber > 13)
            throw new InvalidOperationException($"INVALID_SECTION_NUMBER: Section number must be between 1 and 13, got {sectionNumber}.");

        var normalizedDecision = decision.ToLowerInvariant();
        if (normalizedDecision != "approve" && normalizedDecision != "request_revision")
            throw new InvalidOperationException(
                $"INVALID_DECISION: Decision must be 'approve' or 'request_revision', got '{decision}'.");

        if (normalizedDecision == "request_revision" && string.IsNullOrWhiteSpace(comments))
            throw new InvalidOperationException("COMMENTS_REQUIRED: Comments are required when requesting revision.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var section = await context.SspSections
            .FirstOrDefaultAsync(s => s.RegisteredSystemId == registeredSystemId
                && s.SectionNumber == sectionNumber, cancellationToken)
            ?? throw new InvalidOperationException(
                $"SECTION_NOT_FOUND: Section §{sectionNumber} not found for system '{registeredSystemId}'.");

        if (section.Status != SspSectionStatus.UnderReview)
            throw new InvalidOperationException(
                $"INVALID_STATUS_FOR_REVIEW: Section must be in UnderReview status to review. Current status: {section.Status}.");

        section.ReviewedBy = reviewer;
        section.ReviewedAt = DateTime.UtcNow;

        if (normalizedDecision == "approve")
        {
            section.Status = SspSectionStatus.Approved;
        }
        else // request_revision
        {
            section.Status = SspSectionStatus.Draft;
            section.ReviewerComments = comments;
        }

        await context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reviewed SSP section §{SectionNumber} for system '{SystemId}': {Decision}",
            sectionNumber, registeredSystemId, normalizedDecision);

        return section;
    }

    /// <inheritdoc />
    public async Task<SspCompletenessReport> GetSspCompletenessAsync(
        string registeredSystemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredSystemId, nameof(registeredSystemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == registeredSystemId, cancellationToken)
            ?? throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{registeredSystemId}' not found.");

        var existingSections = await context.SspSections
            .Where(s => s.RegisteredSystemId == registeredSystemId)
            .ToListAsync(cancellationToken);

        var sectionMap = existingSections.ToDictionary(s => s.SectionNumber);
        var summaries = new List<SspSectionSummary>();
        var blockingIssues = new List<string>();
        var approvedCount = 0;

        for (int i = 1; i <= 13; i++)
        {
            if (sectionMap.TryGetValue(i, out var section))
            {
                var wordCount = section.Content?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
                summaries.Add(new SspSectionSummary(
                    SectionNumber: i,
                    SectionTitle: SectionTitles[i],
                    Status: section.Status.ToString(),
                    IsAutoGenerated: section.IsAutoGenerated,
                    HasManualOverride: section.HasManualOverride,
                    AuthoredBy: section.AuthoredBy,
                    AuthoredAt: section.AuthoredAt,
                    WordCount: wordCount,
                    Version: section.Version));

                if (section.Status == SspSectionStatus.Approved)
                    approvedCount++;
                else
                    blockingIssues.Add($"§{i} {SectionTitles[i]}: status is {section.Status}");
            }
            else
            {
                summaries.Add(new SspSectionSummary(
                    SectionNumber: i,
                    SectionTitle: SectionTitles[i],
                    Status: SspSectionStatus.NotStarted.ToString(),
                    IsAutoGenerated: AutoGeneratedSections.Contains(i),
                    HasManualOverride: false,
                    AuthoredBy: null,
                    AuthoredAt: null,
                    WordCount: 0,
                    Version: 0));
                blockingIssues.Add($"§{i} {SectionTitles[i]}: not started");
            }
        }

        var readinessPercent = Math.Round(approvedCount / 13.0 * 100, 1);

        return new SspCompletenessReport(
            SystemName: system.Name,
            OverallReadinessPercent: readinessPercent,
            ApprovedCount: approvedCount,
            TotalSections: 13,
            Sections: summaries,
            BlockingIssues: blockingIssues);
    }

    // ─── Section Content Generators (Feature 022 Phase 4) ────────────────────

    /// <summary>§1 System Identification: name, acronym, DitprId, EmassId, type, criticality, hosting, RMF step.</summary>
    internal static string GenerateSection1Content(RegisteredSystem system, List<RmfRoleAssignment> roles)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**System Name**: {system.Name}");
        if (!string.IsNullOrWhiteSpace(system.Acronym))
            sb.AppendLine($"**Acronym**: {system.Acronym}");
        if (!string.IsNullOrWhiteSpace(system.DitprId))
            sb.AppendLine($"**DITPR ID**: {system.DitprId}");
        if (!string.IsNullOrWhiteSpace(system.EmassId))
            sb.AppendLine($"**eMASS ID**: {system.EmassId}");
        sb.AppendLine($"**System Type**: {system.SystemType}");
        sb.AppendLine($"**Mission Criticality**: {system.MissionCriticality}");
        sb.AppendLine($"**Hosting Environment**: {system.HostingEnvironment}");
        sb.AppendLine($"**Current RMF Step**: {system.CurrentRmfStep}");
        if (!string.IsNullOrWhiteSpace(system.Description))
        {
            sb.AppendLine();
            sb.AppendLine($"**Description**: {system.Description}");
        }

        if (roles.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Key Personnel");
            sb.AppendLine();
            sb.AppendLine("| Role | Name | User ID |");
            sb.AppendLine("|------|------|---------|");
            foreach (var role in roles.OrderBy(r => r.RmfRole))
            {
                sb.AppendLine($"| {role.RmfRole} | {role.UserDisplayName ?? "—"} | {role.UserId} |");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>§2 Security Categorization: FIPS 199 notation, impact levels, information types, FIPS 200 reference.</summary>
    internal static string GenerateSection2Content(SecurityCategorization? categorization)
    {
        if (categorization == null)
            return "*Security categorization has not been performed.*";

        var sb = new StringBuilder();
        sb.AppendLine($"**FIPS 199 Notation**: {categorization.FormalNotation}");
        sb.AppendLine($"**Overall Categorization**: {categorization.OverallCategorization}");
        sb.AppendLine($"**DoD Impact Level**: {categorization.DoDImpactLevel}");
        sb.AppendLine($"**Recommended Baseline**: {categorization.NistBaseline}");
        sb.AppendLine();
        sb.AppendLine("**FIPS 200 Reference**: Per FIPS 200 *Minimum Security Requirements for Federal Information and Information Systems*, " +
            $"this system requires a minimum of {categorization.NistBaseline} security controls based on its {categorization.OverallCategorization} categorization.");

        if (categorization.InformationTypes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Information Types");
            sb.AppendLine();
            sb.AppendLine("| SP 800-60 ID | Name | C | I | A |");
            sb.AppendLine("|-------------|------|---|---|---|");
            foreach (var it in categorization.InformationTypes.OrderBy(i => i.Sp80060Id))
            {
                sb.AppendLine($"| {it.Sp80060Id} | {it.Name} | {it.ConfidentialityImpact} | {it.IntegrityImpact} | {it.AvailabilityImpact} |");
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>§3 System Owner / Authorizing Official: detailed role descriptions per NIST 800-18 §2.3.</summary>
    internal static string GenerateSection3Content(List<RmfRoleAssignment> roles)
    {
        if (roles.Count == 0)
            return "*No personnel assignments have been made. Assign System Owner, Authorizing Official, ISSO, and ISSM roles.*";

        var sb = new StringBuilder();
        var roleDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemOwner"] = "Responsible for the overall procurement, development, integration, modification, operation, maintenance, and retirement of the information system.",
            ["AuthorizingOfficial"] = "Senior official with the authority to formally assume responsibility for operating the information system at an acceptable level of risk.",
            ["Isso"] = "Responsible for ensuring the appropriate operational security posture is maintained for the information system.",
            ["Issm"] = "Responsible for the information security program management activities for the organization's information systems.",
            ["Sca"] = "Conducts independent assessments of security controls to determine their effectiveness.",
        };

        foreach (var role in roles.OrderBy(r => r.RmfRole))
        {
            sb.AppendLine($"### {role.RmfRole}");
            sb.AppendLine();
            sb.AppendLine($"**Name**: {role.UserDisplayName ?? "TBD"}");
            sb.AppendLine($"**User ID**: {role.UserId}");
            sb.AppendLine($"**Assigned**: {role.AssignedAt:yyyy-MM-dd}");
            if (roleDescriptions.TryGetValue(role.RmfRole.ToString(), out var desc))
                sb.AppendLine($"**Responsibilities**: {desc}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>§4 Information System Type: Major Application vs GSS, mission criticality, operational status.</summary>
    internal static string GenerateSection4Content(RegisteredSystem system)
    {
        var sb = new StringBuilder();
        var typeLabel = system.SystemType switch
        {
            SystemType.MajorApplication => "Major Application",
            SystemType.Enclave => "Enclave / General Support System",
            SystemType.PlatformIt => "Platform IT / Shared Service",
            _ => system.SystemType.ToString()
        };
        sb.AppendLine($"**Information System Type**: {typeLabel}");
        sb.AppendLine($"**Mission Criticality**: {system.MissionCriticality}");
        sb.AppendLine($"**National Security System**: {(system.IsNationalSecuritySystem ? "Yes" : "No")}");

        if (system.OperationalStatus != null)
            sb.AppendLine($"**Operational Status**: {system.OperationalStatus}");
        if (system.OperationalDate.HasValue)
            sb.AppendLine($"**Operational Date**: {system.OperationalDate.Value:yyyy-MM-dd}");
        if (system.DisposalDate.HasValue)
            sb.AppendLine($"**Planned Disposal Date**: {system.DisposalDate.Value:yyyy-MM-dd}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>§5 General Description / Purpose: authored section with template fallback.</summary>
    internal static string GenerateSection5Content(SspSection? section, RegisteredSystem system)
    {
        if (section != null && !string.IsNullOrWhiteSpace(section.Content))
            return section.Content;

        if (!string.IsNullOrWhiteSpace(system.Description))
            return system.Description;

        return "[Section not yet authored]";
    }

    /// <summary>§6 System Environment: hybrid — auto HostingEnvironment + authored narrative.</summary>
    internal static string GenerateSection6Content(RegisteredSystem system, SspSection? section)
    {
        var sb = new StringBuilder();
        sb.AppendLine("### Cloud / Hosting Environment");
        sb.AppendLine();
        sb.AppendLine($"**Hosting Environment**: {system.HostingEnvironment}");

        if (section != null && !string.IsNullOrWhiteSpace(section.Content))
        {
            sb.AppendLine();
            sb.AppendLine("### Environment Details");
            sb.AppendLine();
            sb.AppendLine(section.Content);
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("[Authored environment details not yet provided. Include physical security, logical environment, " +
                "hardware/software inventory summary, and network topology.]");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>§7 System Interconnections: auto-generator from SystemInterconnection + InterconnectionAgreement data.</summary>
    internal static string GenerateSection7Content(
        List<SystemInterconnection> interconnections,
        RegisteredSystem system,
        SspSection? section)
    {
        var sb = new StringBuilder();

        // Check for manual override
        if (section is { HasManualOverride: true, Content: not null })
        {
            sb.AppendLine(section.Content);
            return sb.ToString().TrimEnd();
        }

        if (interconnections.Count == 0)
        {
            if (system.HasNoExternalInterconnections)
                sb.AppendLine("This system has no external interconnections. All data processing occurs within the system authorization boundary.");
            else
                sb.AppendLine("*System interconnections have not been documented.*");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("### Interconnection Summary");
        sb.AppendLine();
        sb.AppendLine($"The {system.Name} system maintains {interconnections.Count} interconnection(s) with external systems.");
        sb.AppendLine();
        sb.AppendLine("| Target System | Acronym | Connection Type | Data Flow | Classification | Agreement Status | Security Measures |");
        sb.AppendLine("|---------------|---------|----------------|-----------|----------------|-----------------|-------------------|");
        foreach (var ic in interconnections)
        {
            var agreementStatus = ic.Agreements
                .Any(a => a.Status == AgreementStatus.Signed) ? "Signed"
                : ic.Agreements.Any() ? ic.Agreements.First().Status.ToString()
                : "None";

            var measures = ic.SecurityMeasures.Count > 0
                ? string.Join(", ", ic.SecurityMeasures)
                : "—";

            sb.AppendLine($"| {ic.TargetSystemName} | {ic.TargetSystemAcronym ?? "—"} | {ic.InterconnectionType} | {ic.DataFlowDirection} | {ic.DataClassification} | {agreementStatus} | {measures} |");
        }

        // Narrative summary of information sharing policies
        var signedCount = interconnections.Count(ic => ic.Agreements.Any(a => a.Status == AgreementStatus.Signed));
        sb.AppendLine();
        sb.AppendLine("### Information Sharing Policies");
        sb.AppendLine();
        sb.AppendLine($"Of {interconnections.Count} interconnection(s), {signedCount} have signed interconnection security agreements (ISAs). " +
            "All interconnections are subject to the organization's information sharing policies and require documented security measures.");

        return sb.ToString().TrimEnd();
    }

    /// <summary>§8 Related Laws / Regulations: authored section.</summary>
    internal static string GenerateSection8Content(SspSection? section)
    {
        if (section != null && !string.IsNullOrWhiteSpace(section.Content))
            return section.Content;

        return "[Section not yet authored]";
    }

    /// <summary>§9 Minimum Security Controls: baseline + tailoring + implementation counts.</summary>
    internal static string GenerateSection9Content(
        ControlBaseline? baseline,
        List<ControlImplementation> narratives)
    {
        if (baseline == null)
            return "*Control baseline has not been selected.*";

        var sb = new StringBuilder();
        sb.AppendLine($"**Baseline Level**: {baseline.BaselineLevel}");
        if (!string.IsNullOrWhiteSpace(baseline.OverlayApplied))
            sb.AppendLine($"**Overlay Applied**: {baseline.OverlayApplied}");
        sb.AppendLine($"**Total Controls**: {baseline.TotalControls}");
        sb.AppendLine($"**Customer Controls**: {baseline.CustomerControls}");
        sb.AppendLine($"**Inherited Controls**: {baseline.InheritedControls}");
        sb.AppendLine($"**Shared Controls**: {baseline.SharedControls}");

        // Tailoring summary
        if (baseline.Tailorings != null && baseline.Tailorings.Count > 0)
        {
            var added = baseline.Tailorings.Where(t => t.Action == TailoringAction.Added).ToList();
            var removed = baseline.Tailorings.Where(t => t.Action == TailoringAction.Removed).ToList();

            sb.AppendLine();
            sb.AppendLine("### Tailored Controls");
            sb.AppendLine();
            if (added.Count > 0)
            {
                sb.AppendLine($"**Controls Added ({added.Count}):**");
                foreach (var t in added.OrderBy(t => t.ControlId))
                    sb.AppendLine($"- {t.ControlId}: {t.Rationale ?? "No rationale provided"}");
            }
            if (removed.Count > 0)
            {
                sb.AppendLine($"**Controls Removed ({removed.Count}):**");
                foreach (var t in removed.OrderBy(t => t.ControlId))
                    sb.AppendLine($"- {t.ControlId}: {t.Rationale ?? "No rationale provided"}");
            }
        }

        // Implementation status summary
        if (narratives.Count > 0)
        {
            var implemented = narratives.Count(n => n.ImplementationStatus == ImplementationStatus.Implemented);
            var partial = narratives.Count(n => n.ImplementationStatus == ImplementationStatus.PartiallyImplemented);
            var planned = narratives.Count(n => n.ImplementationStatus == ImplementationStatus.Planned);
            var na = narratives.Count(n => n.ImplementationStatus == ImplementationStatus.NotApplicable);

            sb.AppendLine();
            sb.AppendLine("### Implementation Status Summary");
            sb.AppendLine();
            sb.AppendLine($"| Status | Count |");
            sb.AppendLine($"|--------|-------|");
            sb.AppendLine($"| Implemented | {implemented} |");
            sb.AppendLine($"| Partially Implemented | {partial} |");
            sb.AppendLine($"| Planned | {planned} |");
            sb.AppendLine($"| Not Applicable | {na} |");
        }

        sb.AppendLine();
        sb.AppendLine("*See §10 Control Implementations for full control-level detail.*");

        return sb.ToString().TrimEnd();
    }

    /// <summary>§11 Authorization Boundary: resource inventory + HW/SW inventory tables + authored boundary narrative.</summary>
    /// <remarks>Feature 033: multi-boundary support. Single boundary renders flat for backward compatibility.</remarks>
    internal static string GenerateSection11Content(
        List<AuthorizationBoundaryDefinition> boundaryDefinitions,
        List<AuthorizationBoundary> boundaries,
        List<InventoryItem> inventoryItems,
        List<SystemComponent> components,
        SspSection? section)
    {
        var sb = new StringBuilder();

        if (section != null && !string.IsNullOrWhiteSpace(section.Content))
        {
            sb.AppendLine("### Boundary Description");
            sb.AppendLine();
            sb.AppendLine(section.Content);
            sb.AppendLine();
        }

        if (boundaries.Count == 0 && boundaryDefinitions.Count == 0)
        {
            if (section == null || string.IsNullOrWhiteSpace(section.Content))
                sb.AppendLine("*Authorization boundary has not been defined.*");
            return sb.ToString().TrimEnd();
        }

        // ─── Single-boundary backward compatibility (T057) ──────────────────
        if (boundaryDefinitions.Count <= 1)
        {
            AppendFlatResourceInventory(sb, boundaries, inventoryItems);
            AppendFlatComponentInventory(sb, components);
            return sb.ToString().TrimEnd();
        }

        // ─── Multi-boundary organized output (T056) ─────────────────────────
        sb.AppendLine($"**Authorization Boundaries**: {boundaryDefinitions.Count}");
        sb.AppendLine();

        // Build lookup: boundary resource → definition id
        var resourceDefLookup = boundaries
            .Where(b => b.AuthorizationBoundaryDefinitionId != null)
            .ToLookup(b => b.AuthorizationBoundaryDefinitionId!);

        // Build inventory lookup via boundary resource chain
        var inventoryByDefId = inventoryItems
            .Where(i => i.BoundaryResource?.AuthorizationBoundaryDefinitionId != null)
            .ToLookup(i => i.BoundaryResource!.AuthorizationBoundaryDefinitionId!);

        var componentsByDefId = components
            .Where(c => c.AuthorizationBoundaryDefinitionId != null)
            .ToLookup(c => c.AuthorizationBoundaryDefinitionId!);

        foreach (var def in boundaryDefinitions)
        {
            sb.AppendLine($"### {def.Name} ({def.BoundaryType})");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(def.Description))
            {
                sb.AppendLine(def.Description);
                sb.AppendLine();
            }

            var defBoundaries = resourceDefLookup[def.Id].ToList();
            var defInventory = inventoryByDefId[def.Id].ToList();
            var defComponents = componentsByDefId[def.Id].ToList();

            // Resource table
            if (defBoundaries.Count > 0)
            {
                sb.AppendLine($"**Resources**: {defBoundaries.Count}");
                sb.AppendLine();
                sb.AppendLine("| Resource ID | Resource Type | Display Name | Inheritance Provider |");
                sb.AppendLine("|-------------|---------------|--------------|---------------------|");
                foreach (var b in defBoundaries.OrderBy(b => b.ResourceType).ThenBy(b => b.ResourceName))
                {
                    sb.AppendLine($"| {b.ResourceId} | {b.ResourceType} | {b.ResourceName ?? "—"} | {b.InheritanceProvider ?? "—"} |");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("*No resources assigned to this boundary.*");
                sb.AppendLine();
            }

            // Component inventory
            if (defComponents.Count > 0)
            {
                sb.AppendLine($"**Components**: {defComponents.Count}");
                sb.AppendLine();
                sb.AppendLine("| Name | Type | Sub-Type | Description | Owner |");
                sb.AppendLine("|------|------|----------|-------------|-------|");
                foreach (var c in defComponents.OrderBy(c => c.ComponentType).ThenBy(c => c.Name))
                {
                    sb.AppendLine($"| {c.Name} | {c.ComponentType} | {c.SubType ?? "—"} | {c.Description ?? "—"} | {c.Owner ?? "—"} |");
                }
                sb.AppendLine();
            }

            // HW/SW inventory for this boundary
            AppendInventoryTables(sb, defInventory);
        }

        // Unassigned resources (null FK — legacy/org-wide)
        var unassignedBoundaries = boundaries.Where(b => b.AuthorizationBoundaryDefinitionId == null).ToList();
        var unassignedInventory = inventoryItems
            .Where(i => i.BoundaryResource == null || i.BoundaryResource.AuthorizationBoundaryDefinitionId == null)
            .ToList();
        var unassignedComponents = components.Where(c => c.AuthorizationBoundaryDefinitionId == null).ToList();

        if (unassignedBoundaries.Count > 0 || unassignedComponents.Count > 0)
        {
            sb.AppendLine("### Unassigned Resources");
            sb.AppendLine();
            sb.AppendLine("*The following resources have not been assigned to a specific boundary.*");
            sb.AppendLine();

            if (unassignedBoundaries.Count > 0)
            {
                sb.AppendLine("| Resource ID | Resource Type | Display Name | Inheritance Provider |");
                sb.AppendLine("|-------------|---------------|--------------|---------------------|");
                foreach (var b in unassignedBoundaries.OrderBy(b => b.ResourceType).ThenBy(b => b.ResourceName))
                {
                    sb.AppendLine($"| {b.ResourceId} | {b.ResourceType} | {b.ResourceName ?? "—"} | {b.InheritanceProvider ?? "—"} |");
                }
                sb.AppendLine();
            }

            if (unassignedComponents.Count > 0)
            {
                sb.AppendLine("| Name | Type | Sub-Type | Description | Owner |");
                sb.AppendLine("|------|------|----------|-------------|-------|");
                foreach (var c in unassignedComponents.OrderBy(c => c.ComponentType).ThenBy(c => c.Name))
                {
                    sb.AppendLine($"| {c.Name} | {c.ComponentType} | {c.SubType ?? "—"} | {c.Description ?? "—"} | {c.Owner ?? "—"} |");
                }
                sb.AppendLine();
            }

            AppendInventoryTables(sb, unassignedInventory);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Renders the original flat resource + HW/SW inventory format for single-boundary backward compatibility.</summary>
    private static void AppendFlatResourceInventory(StringBuilder sb, List<AuthorizationBoundary> boundaries, List<InventoryItem> inventoryItems)
    {
        var inBoundary = boundaries.Where(b => b.IsInBoundary).ToList();
        var excluded = boundaries.Where(b => !b.IsInBoundary).ToList();

        sb.AppendLine("### Resource Inventory");
        sb.AppendLine();
        sb.AppendLine($"**Total Resources**: {boundaries.Count} ({inBoundary.Count} in-boundary, {excluded.Count} excluded)");
        sb.AppendLine();

        var byType = inBoundary.GroupBy(b => b.ResourceType).OrderBy(g => g.Key);
        sb.AppendLine("| Resource ID | Resource Type | Display Name | Inheritance Provider |");
        sb.AppendLine("|-------------|---------------|--------------|---------------------|");
        foreach (var group in byType)
        {
            foreach (var b in group.OrderBy(b => b.ResourceName))
            {
                sb.AppendLine($"| {b.ResourceId} | {b.ResourceType} | {b.ResourceName ?? "—"} | {b.InheritanceProvider ?? "—"} |");
            }
        }

        if (excluded.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Excluded Resources");
            sb.AppendLine();
            sb.AppendLine("| Resource ID | Resource Type | Exclusion Rationale |");
            sb.AppendLine("|-------------|---------------|---------------------|");
            foreach (var b in excluded.OrderBy(b => b.ResourceType).ThenBy(b => b.ResourceName))
            {
                sb.AppendLine($"| {b.ResourceId} | {b.ResourceType} | {b.ExclusionRationale ?? "—"} |");
            }
        }

        AppendInventoryTables(sb, inventoryItems);
    }

    /// <summary>Renders flat component inventory table.</summary>
    private static void AppendFlatComponentInventory(StringBuilder sb, List<SystemComponent> components)
    {
        if (components.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine("### Component Inventory");
        sb.AppendLine();
        sb.AppendLine("| Name | Type | Sub-Type | Description | Owner |");
        sb.AppendLine("|------|------|----------|-------------|-------|");
        foreach (var c in components.OrderBy(c => c.ComponentType).ThenBy(c => c.Name))
        {
            sb.AppendLine($"| {c.Name} | {c.ComponentType} | {c.SubType ?? "—"} | {c.Description ?? "—"} | {c.Owner ?? "—"} |");
        }
    }

    /// <summary>Appends HW/SW inventory tables for a list of inventory items.</summary>
    private static void AppendInventoryTables(StringBuilder sb, List<InventoryItem> inventoryItems)
    {
        var hwItems = inventoryItems.Where(i => i.Type == InventoryItemType.Hardware).ToList();
        var swItems = inventoryItems.Where(i => i.Type == InventoryItemType.Software).ToList();

        if (hwItems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("#### Hardware Inventory");
            sb.AppendLine();
            sb.AppendLine("| Name | Manufacturer | Model | Serial Number | Function | IP Address | MAC Address | Location |");
            sb.AppendLine("|------|-------------|-------|---------------|----------|------------|-------------|----------|");
            foreach (var h in hwItems)
            {
                sb.AppendLine($"| {h.ItemName} | {h.Manufacturer ?? "—"} | {h.Model ?? "—"} | {h.SerialNumber ?? "—"} | {h.HardwareFunction?.ToString() ?? "—"} | {h.IpAddress ?? "—"} | {h.MacAddress ?? "—"} | {h.Location ?? "—"} |");
            }
        }

        if (swItems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("#### Software Inventory");
            sb.AppendLine();
            sb.AppendLine("| Name | Vendor | Version | Patch Level | Function | License Type |");
            sb.AppendLine("|------|--------|---------|-------------|----------|-------------|");
            foreach (var s in swItems)
            {
                sb.AppendLine($"| {s.ItemName} | {s.Vendor ?? "—"} | {s.Version ?? "—"} | {s.PatchLevel ?? "—"} | {s.SoftwareFunction?.ToString() ?? "—"} | {s.LicenseType ?? "—"} |");
            }
        }

        if (hwItems.Count == 0 && swItems.Count == 0 && inventoryItems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("*No hardware/software inventory items have been registered. Use `inventory_auto_seed` to populate from boundary resources.*");
        }
    }

    /// <summary>§12 Personnel Security: auto role list + authored screening/access/training/separation procedures.</summary>
    internal static string GenerateSection12Content(
        List<RmfRoleAssignment> roles,
        SspSection? section)
    {
        var sb = new StringBuilder();

        if (roles.Count > 0)
        {
            sb.AppendLine("### Assigned Security Roles");
            sb.AppendLine();
            sb.AppendLine("| Role | Name | User ID | Status |");
            sb.AppendLine("|------|------|---------|--------|");
            foreach (var role in roles.OrderBy(r => r.RmfRole))
            {
                var status = role.IsActive ? "Active" : "Inactive";
                sb.AppendLine($"| {role.RmfRole} | {role.UserDisplayName ?? "—"} | {role.UserId} | {status} |");
            }
            sb.AppendLine();
        }

        if (section != null && !string.IsNullOrWhiteSpace(section.Content))
        {
            sb.AppendLine("### Screening, Access, and Training Requirements");
            sb.AppendLine();
            sb.AppendLine(section.Content);
        }
        else
        {
            sb.AppendLine("[Personnel security procedures not yet authored. Include screening requirements, " +
                "access agreements, training requirements, and separation procedures.]");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>§13 Contingency Plan: metadata from ContingencyPlanReference + authored narrative.</summary>
    internal static string GenerateSection13Content(
        ContingencyPlanReference? contingencyPlan,
        SspSection? section)
    {
        var sb = new StringBuilder();

        if (contingencyPlan != null)
        {
            sb.AppendLine("### Contingency Plan Reference");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(contingencyPlan.DocumentTitle))
                sb.AppendLine($"**Document Title**: {contingencyPlan.DocumentTitle}");
            if (!string.IsNullOrWhiteSpace(contingencyPlan.DocumentLocation))
                sb.AppendLine($"**Document Location**: {contingencyPlan.DocumentLocation}");
            if (!string.IsNullOrWhiteSpace(contingencyPlan.DocumentVersion))
                sb.AppendLine($"**Version**: {contingencyPlan.DocumentVersion}");
            if (contingencyPlan.LastTestedDate.HasValue)
                sb.AppendLine($"**Last Tested**: {contingencyPlan.LastTestedDate.Value:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(contingencyPlan.TestType))
                sb.AppendLine($"**Test Type**: {contingencyPlan.TestType}");
            if (!string.IsNullOrWhiteSpace(contingencyPlan.RecoveryTimeObjective))
                sb.AppendLine($"**Recovery Time Objective (RTO)**: {contingencyPlan.RecoveryTimeObjective}");
            if (!string.IsNullOrWhiteSpace(contingencyPlan.RecoveryPointObjective))
                sb.AppendLine($"**Recovery Point Objective (RPO)**: {contingencyPlan.RecoveryPointObjective}");
            if (!string.IsNullOrWhiteSpace(contingencyPlan.AlternateProcessingSite))
                sb.AppendLine($"**Alternate Processing Site**: {contingencyPlan.AlternateProcessingSite}");
            if (!string.IsNullOrWhiteSpace(contingencyPlan.BackupProceduresSummary))
            {
                sb.AppendLine();
                sb.AppendLine($"**Backup Procedures**: {contingencyPlan.BackupProceduresSummary}");
            }
            sb.AppendLine();
        }

        if (section != null && !string.IsNullOrWhiteSpace(section.Content))
        {
            sb.AppendLine("### Contingency Plan Details");
            sb.AppendLine();
            sb.AppendLine(section.Content);
        }
        else if (contingencyPlan == null)
        {
            sb.AppendLine("[Contingency plan not yet documented. Include document reference, testing schedule, " +
                "RTO/RPO targets, alternate processing site, and backup procedures.]");
        }

        return sb.ToString().TrimEnd();
    }
}
