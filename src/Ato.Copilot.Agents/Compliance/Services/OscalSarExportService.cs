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
/// Produces OSCAL 1.1.2 Assessment Results (SAR) JSON from compliance assessment data.
/// Feature 076 — T005.
/// </summary>
public class OscalSarExportService : IOscalSarExportService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OscalSarExportService> _logger;

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

    public OscalSarExportService(IServiceScopeFactory scopeFactory, ILogger<OscalSarExportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<OscalSarExportResult> ExportAsync(
        string systemId,
        bool prettyPrint = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId, nameof(systemId));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtoCopilotContext>();
        var warnings = new List<string>();

        var system = await db.RegisteredSystems
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId, cancellationToken)
            ?? throw new InvalidOperationException($"SYSTEM_NOT_FOUND: System '{systemId}' not found.");

        var assessment = await db.Assessments
            .AsNoTracking()
            .Include(a => a.Findings)
            .Where(a => a.RegisteredSystemId == systemId && a.Status == AssessmentStatus.Completed)
            .OrderByDescending(a => a.CompletedAt ?? a.AssessedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (assessment == null)
        {
            warnings.Add("No completed assessment found — exporting empty SAR skeleton.");
        }

        var registry = new PackageUuidRegistry(systemId);
        var sarUuid = registry.AssessmentResultsUuid.ToString();
        var lastModified = DateTimeOffset.UtcNow.ToString("o");

        var findings = assessment?.Findings ?? new List<ComplianceFinding>();

        var observations = findings.Select((f, i) => BuildObservation(f, registry)).ToList();
        var risks       = findings.Where(f => f.CatSeverity.HasValue)
                                  .Select(f => BuildRisk(f, registry, observations)).ToList();
        var oscalFindings = findings.Select(f => BuildFinding(f, registry, observations, risks)).ToList();

        var result = new OscalAssessmentResultsRoot
        {
            AssessmentResults = new OscalAssessmentResults
            {
                Uuid = sarUuid,
                Metadata = BuildMetadata(system, lastModified),
                ImportAp = new OscalHref { Href = $"#{registry.SapUuid}" },
                Results = assessment == null ? new() : new List<OscalAssessmentResult>
                {
                    new()
                    {
                        Uuid = Guid.NewGuid().ToString(),
                        Title = $"Assessment Results — {system.Name}",
                        Description = assessment.ExecutiveSummary.Length > 0
                            ? assessment.ExecutiveSummary
                            : $"Assessment completed {assessment.CompletedAt?.ToString("d") ?? assessment.AssessedAt.ToString("d")}.",
                        Start = assessment.AssessedAt.ToString("o"),
                        End   = assessment.CompletedAt?.ToString("o"),
                        ReviewedControls = new OscalReviewedControls
                        {
                            ControlSelections = new List<OscalControlSelection>
                            {
                                new() { IncludeAll = new { } }
                            }
                        },
                        Observations  = observations.Count > 0 ? observations : null,
                        Risks         = risks.Count > 0 ? risks : null,
                        Findings      = oscalFindings.Count > 0 ? oscalFindings : null,
                    }
                }
            }
        };

        var opts = prettyPrint ? PrettyOpts : CompactOpts;
        var json = JsonSerializer.Serialize(result, opts);

        _logger.LogInformation("OSCAL SAR export for {SystemId}: {Findings} findings, {Risks} risks",
            systemId, findings.Count, risks.Count);

        return new OscalSarExportResult(json, warnings, findings.Count, observations.Count, risks.Count);
    }

    internal static OscalDocumentMetadata BuildMetadata(RegisteredSystem system, string lastModified) =>
        new()
        {
            Title = $"{system.Name} — Security Assessment Results",
            LastModified = lastModified,
            Version = "1.0",
            OscalVersion = OscalVersion
        };

    internal static OscalObservation BuildObservation(ComplianceFinding finding, PackageUuidRegistry registry) =>
        new()
        {
            Uuid = registry.GetOrCreate("observation", finding.Id).ToString(),
            Title = finding.Title,
            Description = finding.Description,
            Methods = new List<string> { "EXAMINE" },
            Types  = new List<string> { "finding" },
            Collected = finding.DiscoveredAt.ToString("o"),
            Subjects = new List<OscalSubjectReference>
            {
                new() { SubjectUuid = registry.GetOrCreate("component", finding.ResourceId).ToString(), Type = "component" }
            }
        };

    internal static OscalRisk BuildRisk(
        ComplianceFinding finding,
        PackageUuidRegistry registry,
        List<OscalObservation> observations)
    {
        var obsUuid = registry.GetOrCreate("observation", finding.Id).ToString();
        return new OscalRisk
        {
            Uuid = registry.GetOrCreate("risk", finding.Id).ToString(),
            Title = finding.Title,
            Description = finding.Description,
            Statement = finding.RemediationGuidance.Length > 0
                ? finding.RemediationGuidance
                : $"Remediate {finding.ControlId} finding on resource {finding.ResourceId}.",
            Status = "open",
            Characterizations = new List<OscalRiskCharacterization>
            {
                new()
                {
                    Facets = new List<OscalRiskFacet>
                    {
                        new() { Name = "likelihood", System = FedRampNs, Value = MapSeverityToLevel(finding.Severity) },
                        new() { Name = "impact",     System = FedRampNs, Value = MapCatToLevel(finding.CatSeverity) }
                    }
                }
            },
            RelatedObservations = observations
                .Where(o => o.Uuid == obsUuid)
                .Select(o => new OscalUuidRef { ObservationUuid = o.Uuid })
                .ToList()
        };
    }

    internal static OscalFinding BuildFinding(
        ComplianceFinding finding,
        PackageUuidRegistry registry,
        List<OscalObservation> observations,
        List<OscalRisk> risks)
    {
        var obsUuid  = registry.GetOrCreate("observation", finding.Id).ToString();
        var riskUuid = registry.GetOrCreate("risk", finding.Id).ToString();
        var controlId = finding.ControlId.ToLowerInvariant().Replace(" ", "-");

        return new OscalFinding
        {
            Uuid = registry.GetOrCreate("finding", finding.Id).ToString(),
            Title = finding.Title,
            Description = finding.Description,
            Target = new OscalFindingTarget
            {
                Type     = "statement-id",
                TargetId = $"{controlId}_smt",
                Status = new OscalFindingStatus
                {
                    State   = finding.Status == FindingStatus.Remediated ? "satisfied" : "not-satisfied",
                    Remarks = finding.Status.ToString()
                }
            },
            RelatedObservations = new List<OscalUuidRef> { new() { ObservationUuid = obsUuid } },
            RelatedRisks        = finding.CatSeverity.HasValue
                ? new List<OscalUuidRef> { new() { RiskUuid = riskUuid } }
                : null
        };
    }

    internal static string MapCatToLevel(CatSeverity? cat) => cat switch
    {
        CatSeverity.CatI   => "high",
        CatSeverity.CatII  => "moderate",
        CatSeverity.CatIII => "low",
        _                  => "low"
    };

    internal static string MapSeverityToLevel(FindingSeverity sev) => sev switch
    {
        FindingSeverity.Critical => "high",
        FindingSeverity.High     => "high",
        FindingSeverity.Medium   => "moderate",
        _                        => "low"
    };
}
