using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Implements FIPS 199 / SP 800-60 security categorization:
/// information type management, high-water-mark computation, DoD IL derivation,
/// and heuristic info type suggestions.
/// </summary>
public class CategorizationService : ICategorizationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CategorizationService> _logger;
    private readonly IPrivacyService _privacyService;
    private readonly IAlertManager? _alertManager;

    public CategorizationService(
        IServiceScopeFactory scopeFactory,
        ILogger<CategorizationService> logger,
        IPrivacyService privacyService,
        IAlertManager? alertManager = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _privacyService = privacyService;
        _alertManager = alertManager;
    }

    /// <inheritdoc />
    public async Task<SecurityCategorization> CategorizeSystemAsync(
        string systemId,
        IEnumerable<InformationTypeInput> informationTypes,
        string categorizedBy,
        bool isNationalSecuritySystem = false,
        string? justification = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(categorizedBy, nameof(categorizedBy));

        var infoTypeList = informationTypes?.ToList()
            ?? throw new ArgumentNullException(nameof(informationTypes));

        if (infoTypeList.Count == 0)
            throw new InvalidOperationException("At least one information type is required for categorization.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        var existing = await context.SecurityCategorizations
            .Include(sc => sc.InformationTypes)
            .FirstOrDefaultAsync(sc => sc.RegisteredSystemId == systemId, cancellationToken);
        var previousSummary = existing == null ? null : CreateSummary(existing);
        var previousInformationTypes = existing?.InformationTypes.ToList() ?? [];
        var previousInformationTypesJson = SerializeInformationTypes(previousInformationTypes);
        var previousCategorizedBy = existing?.CategorizedBy;
        var previousCategorizedAt = existing?.CategorizedAt;
        var previousJustification = existing?.Justification;
        var changedAt = DateTime.UtcNow;

        var categorization = existing ?? new SecurityCategorization { RegisteredSystemId = systemId };
        if (existing != null)
        {
            context.InformationTypes.RemoveRange(previousInformationTypes);
            existing.InformationTypes.Clear();
        }

        categorization.IsNationalSecuritySystem = isNationalSecuritySystem;
        categorization.Justification = justification?.Trim();
        categorization.CategorizedBy = categorizedBy;
        categorization.CategorizedAt = changedAt;
        categorization.ModifiedAt = existing == null ? null : changedAt;

        // Update system-level NSS flag
        system.IsNationalSecuritySystem = isNationalSecuritySystem;

        // Add information types
        foreach (var input in infoTypeList)
        {
            ValidateInformationTypeInput(input);

            var infoType = new InformationType
            {
                SecurityCategorizationId = categorization.Id,
                Sp80060Id = input.Sp80060Id.Trim(),
                Name = input.Name.Trim(),
                Category = input.Category?.Trim(),
                ConfidentialityImpact = ParseImpact(input.ConfidentialityImpact, "confidentiality_impact"),
                IntegrityImpact = ParseImpact(input.IntegrityImpact, "integrity_impact"),
                AvailabilityImpact = ParseImpact(input.AvailabilityImpact, "availability_impact"),
                UsesProvisionalImpactLevels = input.UsesProvisional,
                AdjustmentJustification = input.AdjustmentJustification?.Trim()
            };

            categorization.InformationTypes.Add(infoType);
        }

        if (existing == null)
            context.SecurityCategorizations.Add(categorization);

        var newSummary = CreateSummary(categorization);
        var latestVersion = await context.CategorizationHistoryEntries
            .Where(entry => entry.RegisteredSystemId == systemId)
            .Select(entry => (int?)entry.Version)
            .MaxAsync(cancellationToken) ?? 0;
        if (existing != null && latestVersion == 0 && previousSummary != null)
        {
            context.CategorizationHistoryEntries.Add(new CategorizationHistoryEntry
            {
                TenantId = system.TenantId,
                RegisteredSystemId = systemId,
                Version = 1,
                ChangedBy = previousCategorizedBy!,
                ChangedAt = previousCategorizedAt!.Value,
                Justification = previousJustification,
                NewConfidentialityImpact = previousSummary.ConfidentialityImpact,
                NewIntegrityImpact = previousSummary.IntegrityImpact,
                NewAvailabilityImpact = previousSummary.AvailabilityImpact,
                NewOverallImpact = previousSummary.OverallCategorization,
                NewInformationTypesJson = previousInformationTypesJson,
            });
            latestVersion = 1;
        }

        context.CategorizationHistoryEntries.Add(new CategorizationHistoryEntry
        {
            TenantId = system.TenantId,
            RegisteredSystemId = systemId,
            Version = latestVersion + 1,
            ChangedBy = categorizedBy,
            ChangedAt = changedAt,
            Justification = justification?.Trim(),
            PreviousConfidentialityImpact = previousSummary?.ConfidentialityImpact,
            PreviousIntegrityImpact = previousSummary?.IntegrityImpact,
            PreviousAvailabilityImpact = previousSummary?.AvailabilityImpact,
            PreviousOverallImpact = previousSummary?.OverallCategorization,
            NewConfidentialityImpact = newSummary.ConfidentialityImpact,
            NewIntegrityImpact = newSummary.IntegrityImpact,
            NewAvailabilityImpact = newSummary.AvailabilityImpact,
            NewOverallImpact = newSummary.OverallCategorization,
            PreviousInformationTypesJson = previousInformationTypesJson,
            NewInformationTypesJson = SerializeInformationTypes(categorization.InformationTypes),
        });

        system.ModifiedAt = changedAt;
        await context.SaveChangesAsync(cancellationToken);

        if (previousSummary != null &&
            previousSummary.OverallCategorization != newSummary.OverallCategorization &&
            system.CurrentRmfStep is RmfPhase.Authorize or RmfPhase.Monitor &&
            _alertManager != null)
        {
            await _alertManager.CreateAlertAsync(new ComplianceAlert
            {
                Type = AlertType.Violation,
                Severity = AlertSeverity.High,
                Title = $"Security categorization changed for {system.Name}",
                Description = "An impact-level change requires Authorizing Official review.",
                SubscriptionId = string.Empty,
                AffectedResources = [systemId],
                RegisteredSystemId = systemId,
                ActorId = categorizedBy,
                AssignedTo = "AO",
                RecommendedAction = "Review the categorization change and determine whether reauthorization is required.",
                ChangeDetails = JsonSerializer.Serialize(new
                {
                    previousImpact = previousSummary.OverallCategorization.ToString(),
                    newImpact = newSummary.OverallCategorization.ToString(),
                    justification = justification?.Trim(),
                }),
            }, cancellationToken);
        }

        // Invalidate existing PTA when info types change (Feature 021)
        try
        {
            await _privacyService.InvalidatePtaAsync(systemId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // No existing PTA to invalidate — not an error
        }

        // Reload with navigation for computed properties
        var result = await context.SecurityCategorizations
            .Include(sc => sc.InformationTypes)
            .Include(sc => sc.RegisteredSystem)
            .FirstAsync(sc => sc.Id == categorization.Id, cancellationToken);

        _logger.LogInformation(
            "System '{SystemId}' categorized: C={C} I={I} A={A}, Overall={Overall}, IL={IL}",
            systemId,
            result.ConfidentialityImpact,
            result.IntegrityImpact,
            result.AvailabilityImpact,
            result.OverallCategorization,
            result.DoDImpactLevel);

        return result;
    }

    /// <inheritdoc />
    public async Task<SecurityCategorization?> GetCategorizationAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        return await context.SecurityCategorizations
            .Include(sc => sc.InformationTypes)
            .Include(sc => sc.RegisteredSystem)
            .FirstOrDefaultAsync(sc => sc.RegisteredSystemId == systemId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategorizationHistoryEntry>> GetCategorizationHistoryAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var entries = await context.CategorizationHistoryEntries
            .Where(entry => entry.RegisteredSystemId == systemId)
            .OrderByDescending(entry => entry.Version)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (entries.Count > 0)
            entries[0].IsCurrent = true;

        return entries;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SuggestedInformationType>> SuggestInfoTypesAsync(
        string systemId,
        string? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();

        var system = await context.RegisteredSystems
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"System '{systemId}' not found.");

        // Heuristic suggestion based on system type and description
        var searchText = $"{system.Name} {system.Description} {additionalContext}".ToLowerInvariant();
        var suggestions = new List<SuggestedInformationType>();

        foreach (var entry in Sp80060Catalog)
        {
            double confidence = 0.0;
            string? rationale = null;

            // Keyword matching in system description
            foreach (var keyword in entry.Keywords)
            {
                if (searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    confidence += 0.25;
                    rationale = $"System description matches keyword '{keyword}'";
                }
            }

            // System type affinity
            if (entry.SystemTypeAffinity.Contains(system.SystemType))
            {
                confidence += 0.15;
                rationale ??= $"Common info type for {system.SystemType} systems";
            }

            // Mission criticality boost
            if (system.MissionCriticality == MissionCriticality.MissionCritical && entry.IsCriticalDefault)
            {
                confidence += 0.10;
            }

            if (confidence > 0.0)
            {
                suggestions.Add(new SuggestedInformationType
                {
                    Sp80060Id = entry.Id,
                    Name = entry.Name,
                    Category = entry.Category,
                    Confidence = Math.Min(confidence, 1.0),
                    Rationale = rationale,
                    DefaultConfidentialityImpact = entry.DefaultC.ToString(),
                    DefaultIntegrityImpact = entry.DefaultI.ToString(),
                    DefaultAvailabilityImpact = entry.DefaultA.ToString()
                });
            }
        }

        return suggestions
            .OrderByDescending(s => s.Confidence)
            .Take(10)
            .ToList();
    }

    /// <inheritdoc />
    public CategorizationSummary ComputeHighWaterMark(
        IEnumerable<InformationTypeInput> informationTypes,
        bool isNationalSecuritySystem = false)
    {
        var infoTypeList = informationTypes?.ToList()
            ?? throw new ArgumentNullException(nameof(informationTypes));

        if (infoTypeList.Count == 0)
            throw new InvalidOperationException("At least one information type is required.");

        var maxC = ImpactValue.Low;
        var maxI = ImpactValue.Low;
        var maxA = ImpactValue.Low;

        foreach (var it in infoTypeList)
        {
            var c = ParseImpact(it.ConfidentialityImpact, "confidentiality_impact");
            var i = ParseImpact(it.IntegrityImpact, "integrity_impact");
            var a = ParseImpact(it.AvailabilityImpact, "availability_impact");

            if (c > maxC) maxC = c;
            if (i > maxI) maxI = i;
            if (a > maxA) maxA = a;
        }

        var overall = (ImpactValue)Math.Max(Math.Max((int)maxC, (int)maxI), (int)maxA);

        return new CategorizationSummary
        {
            ConfidentialityImpact = maxC,
            IntegrityImpact = maxI,
            AvailabilityImpact = maxA,
            OverallCategorization = overall,
            DoDImpactLevel = ComplianceFrameworks.DeriveImpactLevel(overall, isNationalSecuritySystem, null),
            NistBaseline = ComplianceFrameworks.DeriveBaselineLevel(overall),
            FormalNotation = ComplianceFrameworks.FormatFips199Notation("System", maxC, maxI, maxA),
            InformationTypeCount = infoTypeList.Count
        };
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private static ImpactValue ParseImpact(string value, string paramName)
    {
        if (Enum.TryParse<ImpactValue>(value, true, out var impact))
            return impact;

        throw new InvalidOperationException(
            $"Invalid {paramName} '{value}'. Valid values: Low, Moderate, High.");
    }

    private static void ValidateInformationTypeInput(InformationTypeInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Sp80060Id))
            throw new InvalidOperationException("Information type 'sp800_60_id' is required.");
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new InvalidOperationException("Information type 'name' is required.");
        if (!input.UsesProvisional && string.IsNullOrWhiteSpace(input.AdjustmentJustification))
            throw new InvalidOperationException(
                $"Adjustment justification is required for non-provisional info type '{input.Sp80060Id}'.");
    }

    private static CategorizationSummary CreateSummary(SecurityCategorization categorization) => new()
    {
        ConfidentialityImpact = categorization.ConfidentialityImpact,
        IntegrityImpact = categorization.IntegrityImpact,
        AvailabilityImpact = categorization.AvailabilityImpact,
        OverallCategorization = categorization.OverallCategorization,
        DoDImpactLevel = categorization.DoDImpactLevel,
        NistBaseline = categorization.NistBaseline,
        FormalNotation = categorization.FormalNotation,
        InformationTypeCount = categorization.InformationTypes.Count,
    };

    private static string SerializeInformationTypes(IEnumerable<InformationType> informationTypes) =>
        JsonSerializer.Serialize(informationTypes.Select(item => new
        {
            item.Sp80060Id,
            item.Name,
            item.Category,
            confidentialityImpact = item.ConfidentialityImpact.ToString(),
            integrityImpact = item.IntegrityImpact.ToString(),
            availabilityImpact = item.AvailabilityImpact.ToString(),
            item.UsesProvisionalImpactLevels,
            item.AdjustmentJustification,
        }));

    // ─── SP 800-60 Catalog (heuristic lookup) ───────────────────────────────

    private record Sp80060Entry(
        string Id,
        string Name,
        string Category,
        ImpactValue DefaultC,
        ImpactValue DefaultI,
        ImpactValue DefaultA,
        string[] Keywords,
        SystemType[] SystemTypeAffinity,
        bool IsCriticalDefault);

    private static readonly Sp80060Entry[] Sp80060Catalog =
    [
        new("C.2.8.1", "Personnel Administration", "Mission Based Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Low,
            ["personnel", "hr", "human resources", "employee", "staff"],
            [SystemType.MajorApplication, SystemType.Enclave], false),

        new("C.3.5.1", "System Development", "Management and Support Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Low,
            ["development", "software", "devops", "ci/cd", "deployment", "code"],
            [SystemType.MajorApplication, SystemType.PlatformIt], false),

        new("C.3.5.2", "Lifecycle/Change Management", "Management and Support Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Low,
            ["lifecycle", "change management", "configuration", "release"],
            [SystemType.PlatformIt, SystemType.Enclave], false),

        new("C.3.5.8", "Information Security", "Management and Support Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Low,
            ["security", "cybersecurity", "infosec", "compliance", "ato", "rmf"],
            [SystemType.MajorApplication, SystemType.Enclave, SystemType.PlatformIt], true),

        new("D.1.1", "Strategic Planning", "Mission Based Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Low,
            ["strategic", "planning", "mission", "operations", "command"],
            [SystemType.MajorApplication], true),

        new("D.2.1", "Legislative Relations", "Mission Based Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Low,
            ["legislative", "congress", "policy", "regulatory", "governance"],
            [SystemType.MajorApplication], false),

        new("D.3.5", "Financial Program Management", "Mission Based Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Low,
            ["financial", "budget", "accounting", "funds", "procurement", "finance"],
            [SystemType.MajorApplication], false),

        new("D.8.1", "Inventory Control", "Mission Based Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Low,
            ["inventory", "asset", "supply chain", "logistics", "warehouse"],
            [SystemType.MajorApplication, SystemType.Enclave], false),

        new("D.14.1", "General Purpose Data Sharing", "Mission Based Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Low,
            ["data sharing", "integration", "api", "portal", "collaboration"],
            [SystemType.MajorApplication, SystemType.PlatformIt], false),

        new("C.3.3.1", "System and Network Monitoring", "Management and Support Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Moderate,
            ["monitoring", "siem", "network", "infrastructure", "soc", "observability"],
            [SystemType.Enclave, SystemType.PlatformIt], true),

        new("C.2.4.1", "Communications Management", "Management and Support Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Low,
            ["communications", "email", "messaging", "teams", "chat"],
            [SystemType.Enclave, SystemType.PlatformIt], false),

        new("C.3.1.1", "Credential Management", "Management and Support Information Types",
            ImpactValue.High, ImpactValue.High, ImpactValue.Moderate,
            ["credential", "identity", "authentication", "pki", "certificate", "cac", "piv"],
            [SystemType.Enclave, SystemType.PlatformIt], true),

        new("C.3.4.1", "Contingency Planning", "Management and Support Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Moderate,
            ["contingency", "disaster recovery", "backup", "continuity", "coop"],
            [SystemType.Enclave, SystemType.PlatformIt], true),

        new("D.9.1", "Health Care Administration", "Mission Based Information Types",
            ImpactValue.Moderate, ImpactValue.High, ImpactValue.Moderate,
            ["health", "medical", "healthcare", "patient", "clinical", "phi"],
            [SystemType.MajorApplication], false),

        new("D.11.1", "Criminal Investigation", "Mission Based Information Types",
            ImpactValue.High, ImpactValue.High, ImpactValue.Moderate,
            ["criminal", "investigation", "law enforcement", "forensic"],
            [SystemType.MajorApplication, SystemType.Enclave], false),

        new("C.2.1.1", "Customer Services", "Management and Support Information Types",
            ImpactValue.Moderate, ImpactValue.Moderate, ImpactValue.Moderate,
            ["customer", "service desk", "helpdesk", "ticketing", "support"],
            [SystemType.MajorApplication, SystemType.PlatformIt], false),
    ];
}
