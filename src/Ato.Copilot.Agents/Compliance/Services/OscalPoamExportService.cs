using System.Text.Json;
using System.Text.Json.Serialization;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Produces OSCAL 1.1.2 POA&amp;M JSON from PoamItem entities.
/// Feature 076 — T006.
/// </summary>
public class OscalPoamExportService : IOscalPoamExportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OscalPoamExportService> _logger;

    internal const string OscalVersion = "1.1.2";
    internal const string FedRampNs = "https://fedramp.gov/ns/oscal";

    private static readonly JsonSerializerOptions PrettyOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
    };

    private static readonly JsonSerializerOptions CompactOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower
    };

    public OscalPoamExportService(IServiceScopeFactory scopeFactory, ILogger<OscalPoamExportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<OscalPoamExportResult> ExportAsync(
        string systemId,
        bool prettyPrint = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var db   = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var warnings = new List<string>();

        var system = await db.RegisteredSystems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{systemId}' not found.");

        var items = await db.PoamItems
            .AsNoTracking()
            .Include(p => p.Milestones)
            .Where(p => p.RegisteredSystemId == systemId && p.Status != PoamStatus.Completed)
            .OrderBy(p => p.ScheduledCompletionDate)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
            warnings.Add("No active POA&M items found for this system.");

        var registry = new PackageUuidRegistry(systemId);
        var poamUuid = registry.PoamUuid.ToString();
        var now = DateTimeOffset.UtcNow;

        var poamItems = items.Select(item => BuildPoamItem(item, registry)).ToList();
        var risks = items
            .Where(i => i.Milestones.Count > 0)
            .Select(i => BuildRiskWithMilestones(i, registry))
            .ToList();

        var totalMilestones = items.Sum(i => i.Milestones.Count);

        var root = new OscalPoamRoot
        {
            PlanOfActionAndMilestones = new OscalPoam
            {
                Uuid = poamUuid,
                Metadata = new OscalDocumentMetadata
                {
                    Title = $"{system.Name} — Plan of Action and Milestones",
                    LastModified = now.ToString("o"),
                    Version = "1.0",
                    OscalVersion = OscalVersion
                },
                SystemId = new OscalSystemId
                {
                    IdentifierType = "https://fedramp.gov",
                    Id = system.Acronym ?? system.Id
                },
                Risks     = risks.Count > 0 ? risks : null,
                PoamItems = poamItems
            }
        };

        var opts = prettyPrint ? PrettyOpts : CompactOpts;
        var json = JsonSerializer.Serialize(root, opts);

        _logger.LogInformation("OSCAL POA&M export for {SystemId}: {Items} items, {Milestones} milestones",
            systemId, items.Count, totalMilestones);

        return new OscalPoamExportResult(json, warnings, items.Count, totalMilestones);
    }

    internal static OscalPoamItem BuildPoamItem(PoamItem item, PackageUuidRegistry registry)
    {
        var riskUuid = registry.GetOrCreate("poam-risk", item.Id).ToString();
        return new OscalPoamItem
        {
            Uuid = registry.GetOrCreate("poam-item", item.Id).ToString(),
            Title = $"{item.SecurityControlNumber} — {item.WeaknessSource}",
            Description = item.Weakness,
            Props = new List<OscalProp>
            {
                new() { Name = "vendor-dependency", Ns = FedRampNs,
                        Value = (item.DeviationId != null).ToString().ToLower() },
                new() { Name = "cat-severity", Ns = FedRampNs,
                        Value = item.CatSeverity.ToString().ToLower() }
            },
            RelatedFindings = item.FindingId != null
                ? new List<OscalUuidRef> { new() { FindingUuid = registry.GetOrCreate("finding", item.FindingId).ToString() } }
                : new List<OscalUuidRef>(), // OSCAL 1.1.0+ must be present, not null
            RelatedRisks = item.Milestones.Count > 0
                ? new List<OscalUuidRef> { new() { RiskUuid = riskUuid } }
                : null,
            Remarks = item.Comments
        };
    }

    internal static OscalRisk BuildRiskWithMilestones(PoamItem item, PackageUuidRegistry registry)
    {
        var tasks = item.Milestones
            .Select(m => new OscalRemediationTask
            {
                Uuid  = registry.GetOrCreate("milestone", m.Id).ToString(),
                Type  = "milestone",
                Title = m.Description.Length > 100 ? m.Description[..97] + "..." : m.Description,
                Timing = new OscalTaskTiming
                {
                    OnDate = new OscalOnDate { Date = m.TargetDate.ToString("yyyy-MM-dd") }
                }
            })
            .ToList();

        return new OscalRisk
        {
            Uuid = registry.GetOrCreate("poam-risk", item.Id).ToString(),
            Title = $"Risk — {item.SecurityControlNumber}: {item.WeaknessSource}",
            Description = item.Weakness,
            Statement = $"Scheduled completion: {item.ScheduledCompletionDate:d}.",
            Status = "remediating",
            Remediations = new List<OscalRemediation>
            {
                new()
                {
                    Uuid     = registry.GetOrCreate("remediation", item.Id).ToString(),
                    Lifecycle = "planned",
                    Title    = $"Remediation plan for {item.SecurityControlNumber}",
                    Description = item.Comments ?? "Remediation in progress.",
                    Tasks    = tasks
                }
            }
        };
    }
}
