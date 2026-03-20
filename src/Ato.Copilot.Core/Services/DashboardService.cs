using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Ato.Copilot.Core.Constants;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Dtos.Dashboard;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Service for portfolio-level and system-level dashboard queries.
/// </summary>
public class DashboardService
{
    private readonly AtoCopilotContext _db;
    private readonly ILogger<DashboardService> _logger;

    /// <summary>Initializes a new instance of <see cref="DashboardService"/>.</summary>
    public DashboardService(AtoCopilotContext db, ILogger<DashboardService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Returns paginated portfolio summary for all systems accessible to the caller.
    /// </summary>
    public async Task<PaginatedResponse<PortfolioSystemSummaryDto>> GetPortfolioAsync(
        PortfolioQuery query,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var systemsQuery = _db.RegisteredSystems
            .Where(s => s.IsActive)
            .AsNoTracking();

        // Apply filters
        if (!string.IsNullOrEmpty(query.RmfPhase) &&
            Enum.TryParse<RmfPhase>(query.RmfPhase, ignoreCase: true, out var rmfPhaseFilter))
        {
            systemsQuery = systemsQuery.Where(s => s.CurrentRmfStep == rmfPhaseFilter);
        }

        var totalCount = await systemsQuery.CountAsync(cancellationToken);

        // Load systems with related data
        var systems = await systemsQuery
            .Include(s => s.ControlBaseline)
            .ToListAsync(cancellationToken);

        // Load authorization decisions for all systems
        var systemIds = systems.Select(s => s.Id).ToList();

        var activeDecisions = await _db.AuthorizationDecisions
            .Where(d => systemIds.Contains(d.RegisteredSystemId) && d.IsActive)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var poamCounts = await _db.PoamItems
            .Where(p => systemIds.Contains(p.RegisteredSystemId) && p.Status == PoamStatus.Ongoing)
            .GroupBy(p => p.RegisteredSystemId)
            .Select(g => new
            {
                SystemId = g.Key,
                OpenCount = g.Count(),
                OverdueCount = g.Count(p => p.ScheduledCompletionDate < DateTime.UtcNow)
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Get latest assessment scores per system
        var latestScores = await _db.Assessments
            .Where(a => a.RegisteredSystemId != null && systemIds.Contains(a.RegisteredSystemId!) && a.Status == AssessmentStatus.Completed)
            .GroupBy(a => a.RegisteredSystemId!)
            .Select(g => new
            {
                SystemId = g.Key,
                Latest = g.OrderByDescending(a => a.AssessedAt).First(),
                Prior = g.OrderByDescending(a => a.AssessedAt).Skip(1).FirstOrDefault()
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Get finding counts by CatSeverity per system from latest assessments
        var latestAssessmentIds = latestScores
            .Select(s => s.Latest.Id)
            .ToList();

        var findingCounts = await _db.Findings
            .Where(f => latestAssessmentIds.Contains(f.AssessmentId) && f.Status == FindingStatus.Open && f.CatSeverity != null)
            .GroupBy(f => new { f.AssessmentId, f.CatSeverity })
            .Select(g => new { g.Key.AssessmentId, g.Key.CatSeverity, Count = g.Count() })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Setup completion queries for intake wizard badge
        var boundaryCountsBySystem = await _db.AuthorizationBoundaryDefinitions
            .Where(b => systemIds.Contains(b.RegisteredSystemId))
            .GroupBy(b => b.RegisteredSystemId)
            .Select(g => new { SystemId = g.Key, Count = g.Count() })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var roleCountsBySystem = await _db.RmfRoleAssignments
            .Where(r => systemIds.Contains(r.RegisteredSystemId) && r.IsActive)
            .GroupBy(r => r.RegisteredSystemId)
            .Select(g => new { SystemId = g.Key, Count = g.Count() })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var systemsWithCategorization = await _db.SecurityCategorizations
            .Where(sc => systemIds.Contains(sc.RegisteredSystemId))
            .Select(sc => sc.RegisteredSystemId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var categorizationSet = new HashSet<string>(systemsWithCategorization);

        // Build DTOs
        var summaries = systems.Select(system =>
        {
            var decision = activeDecisions.FirstOrDefault(d => d.RegisteredSystemId == system.Id);
            var poam = poamCounts.FirstOrDefault(p => p.SystemId == system.Id);
            var score = latestScores.FirstOrDefault(s => s.SystemId == system.Id);
            var assessmentId = score?.Latest.Id;

            var complianceScore = score?.Latest.ComplianceScore ?? 0;
            var priorScore = score?.Prior?.ComplianceScore ?? complianceScore;

            var (atoStatus, atoDaysRemaining, atoSeverity) = ComputeAtoFields(decision);

            var baselineLevel = system.ControlBaseline?.BaselineLevel ?? "Unknown";

            // Apply impact level filter on the materialized list
            if (!string.IsNullOrEmpty(query.ImpactLevel) &&
                !baselineLevel.Equals(query.ImpactLevel, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return new PortfolioSystemSummaryDto
            {
                SystemId = system.Id,
                Name = system.Name,
                Acronym = system.Acronym,
                SystemType = system.SystemType.ToString(),
                MissionCriticality = system.MissionCriticality.ToString(),
                HostingEnvironment = system.HostingEnvironment,
                Description = system.Description,
                ImpactLevel = baselineLevel,
                CurrentRmfPhase = system.CurrentRmfStep.ToString(),
                ComplianceScore = Math.Round(complianceScore, 1),
                ComplianceScoreDelta = Math.Round(complianceScore - priorScore, 1),
                AtoExpirationDate = decision?.ExpirationDate,
                AtoStatus = atoStatus,
                AtoDaysRemaining = atoDaysRemaining,
                AtoSeverity = atoSeverity,
                OpenPoamCount = poam?.OpenCount ?? 0,
                OverduePoamCount = poam?.OverdueCount ?? 0,
                CatICounts = findingCounts
                    .Where(f => f.AssessmentId == assessmentId && f.CatSeverity == CatSeverity.CatI)
                    .Sum(f => f.Count),
                CatIICounts = findingCounts
                    .Where(f => f.AssessmentId == assessmentId && f.CatSeverity == CatSeverity.CatII)
                    .Sum(f => f.Count),
                CatIIICounts = findingCounts
                    .Where(f => f.AssessmentId == assessmentId && f.CatSeverity == CatSeverity.CatIII)
                    .Sum(f => f.Count),
                HasBoundary = boundaryCountsBySystem.Any(b => b.SystemId == system.Id && b.Count > 0),
                HasRoles = roleCountsBySystem.Any(r => r.SystemId == system.Id && r.Count > 0),
                HasCategorization = categorizationSet.Contains(system.Id),
                IsSetupComplete = boundaryCountsBySystem.Any(b => b.SystemId == system.Id && b.Count > 0)
                    && roleCountsBySystem.Any(r => r.SystemId == system.Id && r.Count > 0)
                    && categorizationSet.Contains(system.Id),
            };
        })
        .Where(s => s != null)
        .Cast<PortfolioSystemSummaryDto>()
        .ToList();

        // Sort
        summaries = ApplySort(summaries, query.SortBy ?? "name", query.SortDir ?? "asc");

        // Paginate
        var pageSize = query.EffectivePageSize;
        var startIndex = 0;
        if (!string.IsNullOrEmpty(query.Cursor) && int.TryParse(query.Cursor, out var cursorIndex))
        {
            startIndex = cursorIndex;
        }

        var page = summaries.Skip(startIndex).Take(pageSize).ToList();
        var nextCursor = startIndex + pageSize < summaries.Count
            ? (startIndex + pageSize).ToString()
            : null;

        sw.Stop();
        _logger.LogInformation(
            "GetPortfolioAsync completed: {ResultCount}/{TotalCount} systems, sort={SortBy} {SortDir}, duration={Duration}ms",
            page.Count, summaries.Count, query.SortBy, query.SortDir, sw.ElapsedMilliseconds);

        return new PaginatedResponse<PortfolioSystemSummaryDto>
        {
            Items = page,
            NextCursor = nextCursor,
            TotalCount = summaries.Count,
        };
    }

    private static (string atoStatus, int? atoDaysRemaining, string atoSeverity) ComputeAtoFields(
        AuthorizationDecision? decision)
    {
        if (decision is null || decision.ExpirationDate is null)
            return ("None", null, "none");

        var daysRemaining = (int)(decision.ExpirationDate.Value - DateTime.UtcNow).TotalDays;

        if (daysRemaining < 0)
            return ("Expired", daysRemaining, "expired");

        var severity = daysRemaining switch
        {
            > 90 => "green",
            >= 30 => "yellow",
            _ => "red",
        };

        return ("Active", daysRemaining, severity);
    }

    private static List<PortfolioSystemSummaryDto> ApplySort(
        List<PortfolioSystemSummaryDto> items, string sortBy, string sortDir)
    {
        var ascending = !sortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);

        return sortBy.ToLowerInvariant() switch
        {
            "impactlevel" => ascending
                ? items.OrderBy(s => s.ImpactLevel).ToList()
                : items.OrderByDescending(s => s.ImpactLevel).ToList(),
            "rmfphase" => ascending
                ? items.OrderBy(s => s.CurrentRmfPhase).ToList()
                : items.OrderByDescending(s => s.CurrentRmfPhase).ToList(),
            "compliancescore" => ascending
                ? items.OrderBy(s => s.ComplianceScore).ToList()
                : items.OrderByDescending(s => s.ComplianceScore).ToList(),
            "atoexpiration" => ascending
                ? items.OrderBy(s => s.AtoExpirationDate).ToList()
                : items.OrderByDescending(s => s.AtoExpirationDate).ToList(),
            "openpoamcount" => ascending
                ? items.OrderBy(s => s.OpenPoamCount).ToList()
                : items.OrderByDescending(s => s.OpenPoamCount).ToList(),
            _ => ascending
                ? items.OrderBy(s => s.Name).ToList()
                : items.OrderByDescending(s => s.Name).ToList(),
        };
    }

    /// <summary>
    /// Returns full dashboard detail for a single system.
    /// </summary>
    public async Task<SystemDetailDto?> GetSystemDetailAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var system = await _db.RegisteredSystems
            .Include(s => s.ControlBaseline)
            .Include(s => s.SecurityCategorization!)
                .ThenInclude(sc => sc.InformationTypes)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, cancellationToken);

        if (system is null)
            return null;

        var decision = await _db.AuthorizationDecisions
            .Where(d => d.RegisteredSystemId == systemId && d.IsActive)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        // Latest two assessments for score delta
        var assessments = await _db.Assessments
            .Where(a => a.RegisteredSystemId == systemId && a.Status == AssessmentStatus.Completed)
            .OrderByDescending(a => a.AssessedAt)
            .Take(2)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var latestAssessment = assessments.FirstOrDefault();
        var priorAssessment = assessments.Skip(1).FirstOrDefault();

        var complianceScore = latestAssessment?.ComplianceScore ?? 0;
        var priorScore = priorAssessment?.ComplianceScore ?? complianceScore;

        // Finding counts
        int catI = 0, catII = 0, catIII = 0;
        if (latestAssessment != null)
        {
            var counts = await _db.Findings
                .Where(f => f.AssessmentId == latestAssessment.Id && f.Status == FindingStatus.Open && f.CatSeverity != null)
                .GroupBy(f => f.CatSeverity)
                .Select(g => new { CatSev = g.Key, Count = g.Count() })
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            catI = counts.FirstOrDefault(c => c.CatSev == CatSeverity.CatI)?.Count ?? 0;
            catII = counts.FirstOrDefault(c => c.CatSev == CatSeverity.CatII)?.Count ?? 0;
            catIII = counts.FirstOrDefault(c => c.CatSev == CatSeverity.CatIII)?.Count ?? 0;
        }

        // POA&M counts
        var poamQuery = _db.PoamItems
            .Where(p => p.RegisteredSystemId == systemId && p.Status == PoamStatus.Ongoing);
        var openPoams = await poamQuery.CountAsync(cancellationToken);
        var overduePoams = await poamQuery
            .Where(p => p.ScheduledCompletionDate < DateTime.UtcNow)
            .CountAsync(cancellationToken);

        // Narrative coverage
        var baselineControlCount = system.ControlBaseline?.TotalControls ?? 0;
        int totalControls;
        int narrativeCount;
        if (baselineControlCount > 0)
        {
            totalControls = baselineControlCount;
            narrativeCount = await _db.ControlImplementations
                .Where(ci => ci.RegisteredSystemId == systemId && ci.Narrative != null && ci.Narrative != "")
                .CountAsync(cancellationToken);
        }
        else
        {
            // No baseline selected — use total control implementations as denominator
            totalControls = await _db.ControlImplementations
                .Where(ci => ci.RegisteredSystemId == systemId)
                .CountAsync(cancellationToken);
            narrativeCount = totalControls > 0
                ? await _db.ControlImplementations
                    .Where(ci => ci.RegisteredSystemId == systemId && ci.Narrative != null && ci.Narrative != "")
                    .CountAsync(cancellationToken)
                : 0;
        }
        var narrativeCoverage = totalControls > 0
            ? Math.Round(100.0 * narrativeCount / totalControls, 1)
            : 0;

        // Active deviations (Feature 035)
        int activeDeviations;
        try
        {
            activeDeviations = await _db.Deviations
                .CountAsync(d => d.RegisteredSystemId == systemId
                    && (d.Status == DeviationStatus.Pending || d.Status == DeviationStatus.Approved),
                    cancellationToken);
        }
        catch (Microsoft.Data.SqlClient.SqlException)
        {
            activeDeviations = 0;
        }

        // RMF phase progress
        var currentPhaseOrdinal = (int)system.CurrentRmfStep;
        var rmfPhases = Enum.GetValues<RmfPhase>().Select(phase =>
        {
            var ordinal = (int)phase;
            var status = ordinal < currentPhaseOrdinal ? "complete"
                : ordinal == currentPhaseOrdinal ? "current"
                : "upcoming";

            double completionPercent = status switch
            {
                "complete" => 100.0,
                "upcoming" => 0.0,
                _ => phase == RmfPhase.Implement && baselineControlCount > 0
                    ? narrativeCoverage
                    : phase == RmfPhase.Assess && latestAssessment != null
                        ? complianceScore
                        : 50.0,
            };

            return new RmfPhaseProgressDto
            {
                Phase = phase.ToString(),
                Ordinal = ordinal,
                Status = status,
                CompletionPercent = completionPercent,
            };
        }).ToList();

        // Recent activity
        var activities = await _db.DashboardActivities
            .Where(a => a.RegisteredSystemId == systemId)
            .OrderByDescending(a => a.Timestamp)
            .Take(10)
            .AsNoTracking()
            .Select(a => new RecentActivityDto
            {
                Id = a.Id,
                EventType = a.EventType,
                Timestamp = a.Timestamp,
                Actor = a.Actor,
                Summary = a.Summary,
                RelatedEntityType = a.RelatedEntityType,
                RelatedEntityId = a.RelatedEntityId,
            })
            .ToListAsync(cancellationToken);

        var (atoStatus, atoDaysRemaining, atoSeverity) = ComputeAtoFields(decision);
        var baselineLevel = system.ControlBaseline?.BaselineLevel ?? "Unknown";

        return new SystemDetailDto
        {
            SystemId = system.Id,
            Name = system.Name,
            Acronym = system.Acronym,
            SystemType = system.SystemType.ToString(),
            MissionCriticality = system.MissionCriticality.ToString(),
            HostingEnvironment = system.HostingEnvironment,
            ImpactLevel = baselineLevel,
            BaselineLevel = baselineLevel,
            CurrentRmfPhase = system.CurrentRmfStep.ToString(),
            RmfPhaseProgress = rmfPhases,
            KeyMetrics = new KeyMetricsDto
            {
                ComplianceScore = Math.Round(complianceScore, 1),
                ComplianceScoreDelta = Math.Round(complianceScore - priorScore, 1),
                PriorScore = Math.Round(priorScore, 1),
                TotalOpenPoams = openPoams,
                OverduePoams = overduePoams,
                AtoDaysRemaining = atoDaysRemaining,
                AtoSeverity = atoSeverity,
                AtoExpirationDate = decision?.ExpirationDate,
                AtoStatus = atoStatus,
                CatIFindings = catI,
                CatIIFindings = catII,
                CatIIIFindings = catIII,
                TotalFindings = catI + catII + catIII,
                NarrativeCoverage = narrativeCoverage,
                ActiveDeviations = activeDeviations,
            },
            RecentActivity = activities,
            Categorization = system.SecurityCategorization is null ? null : new CategorizationDto
            {
                Confidentiality = system.SecurityCategorization.ConfidentialityImpact.ToString(),
                Integrity = system.SecurityCategorization.IntegrityImpact.ToString(),
                Availability = system.SecurityCategorization.AvailabilityImpact.ToString(),
                Overall = system.SecurityCategorization.OverallCategorization.ToString(),
                FormalNotation = system.SecurityCategorization.FormalNotation,
                DodImpactLevel = system.SecurityCategorization.DoDImpactLevel,
                InformationTypes = system.SecurityCategorization.InformationTypes.Select(it => new InfoTypeDto
                {
                    Name = it.Name,
                    Confidentiality = it.ConfidentialityImpact.ToString(),
                    Integrity = it.IntegrityImpact.ToString(),
                    Availability = it.AvailabilityImpact.ToString(),
                }).ToList(),
            },
        };
    }

    /// <summary>
    /// Returns control family compliance data for heatmap rendering.
    /// </summary>
    public async Task<HeatmapDto?> GetHeatmapAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var system = await _db.RegisteredSystems
            .Include(s => s.ControlBaseline)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, cancellationToken);

        if (system?.ControlBaseline is null)
            return null;

        var baseline = system.ControlBaseline;
        var controlIds = baseline.ControlIds;

        // Group baseline controls by family
        var controlsByFamily = controlIds
            .GroupBy(cid => cid.Length >= 2 ? cid.Split('-')[0] : cid)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Get effectiveness determinations for this system
        var effectiveness = await _db.ControlEffectivenessRecords
            .Where(e => e.RegisteredSystemId == systemId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Group by control, take latest
        var latestEffectiveness = effectiveness
            .GroupBy(e => e.ControlId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.AssessedAt).First());

        var families = controlsByFamily
            .Where(kvp => ControlFamilies.FamilyNames.ContainsKey(kvp.Key))
            .Select(kvp =>
            {
                var familyCode = kvp.Key;
                var familyControls = kvp.Value;
                var total = familyControls.Count;
                var assessed = familyControls.Count(c => latestEffectiveness.ContainsKey(c));
                var satisfied = familyControls.Count(c =>
                    latestEffectiveness.TryGetValue(c, out var eff) &&
                    eff.Determination == EffectivenessDetermination.Satisfied);

                var pct = assessed > 0 ? Math.Round(100.0 * satisfied / assessed, 1) : 0;
                var severity = assessed == 0 ? "gray"
                    : pct >= 80 ? "green"
                    : pct >= 50 ? "yellow"
                    : "red";

                return new HeatmapFamilyDto
                {
                    FamilyCode = familyCode,
                    FamilyName = ControlFamilies.FamilyNames.GetValueOrDefault(familyCode, familyCode),
                    TotalControls = total,
                    AssessedControls = assessed,
                    SatisfiedControls = satisfied,
                    CompliancePercent = pct,
                    Severity = severity,
                };
            })
            .OrderBy(f => f.FamilyCode)
            .ToList();

        return new HeatmapDto
        {
            SystemId = systemId,
            BaselineLevel = baseline.BaselineLevel,
            Families = families,
        };
    }

    /// <summary>
    /// Returns individual controls within a family for heatmap drill-down.
    /// </summary>
    public async Task<HeatmapControlsDto?> GetHeatmapControlsAsync(
        string systemId,
        string familyCode,
        CancellationToken cancellationToken = default)
    {
        var system = await _db.RegisteredSystems
            .Include(s => s.ControlBaseline)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == systemId && s.IsActive, cancellationToken);

        if (system?.ControlBaseline is null)
            return null;

        var controlIds = system.ControlBaseline.ControlIds
            .Where(c => c.StartsWith(familyCode + "-", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (controlIds.Count == 0)
            return null;

        // Get effectiveness
        var effectiveness = await _db.ControlEffectivenessRecords
            .Where(e => e.RegisteredSystemId == systemId && controlIds.Contains(e.ControlId))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var latestEffectiveness = effectiveness
            .GroupBy(e => e.ControlId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.AssessedAt).First());

        // Get control implementations for narratives
        var implementations = await _db.ControlImplementations
            .Where(ci => ci.RegisteredSystemId == systemId && controlIds.Contains(ci.ControlId))
            .Include(ci => ci.SecurityCapability)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var implByControl = implementations.ToDictionary(i => i.ControlId);

        // Get NIST controls for titles
        var nistControls = await _db.NistControls
            .Where(n => controlIds.Contains(n.Id))
            .AsNoTracking()
            .ToDictionaryAsync(n => n.Id, cancellationToken);

        // Get POA&M items for control-level status
        var poamItems = await _db.PoamItems
            .Where(p => p.RegisteredSystemId == systemId && controlIds.Contains(p.SecurityControlNumber))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var poamByControl = poamItems
            .GroupBy(p => p.SecurityControlNumber)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CreatedAt).First());

        var controls = controlIds.Select(controlId =>
        {
            var eff = latestEffectiveness.GetValueOrDefault(controlId);
            var impl = implByControl.GetValueOrDefault(controlId);
            var nist = nistControls.GetValueOrDefault(controlId);
            var poam = poamByControl.GetValueOrDefault(controlId);

            var status = eff?.Determination == EffectivenessDetermination.Satisfied
                ? "Satisfied"
                : eff != null ? "OtherThanSatisfied" : "NotAssessed";

            return new HeatmapControlDto
            {
                ControlId = controlId,
                ControlTitle = nist?.Title ?? controlId,
                ComplianceStatus = status,
                HasNarrative = impl?.Narrative != null && impl.Narrative != "",
                IsManuallyCustomized = impl?.IsManuallyCustomized ?? false,
                SecurityCapabilityName = impl?.SecurityCapability?.Name,
                CatSeverity = eff?.CatSeverity?.ToString(),
                PoamStatus = poam?.Status.ToString(),
            };
        }).OrderBy(c => c.ControlId).ToList();

        return new HeatmapControlsDto
        {
            SystemId = systemId,
            FamilyCode = familyCode,
            FamilyName = ControlFamilies.FamilyNames.GetValueOrDefault(familyCode, familyCode),
            Controls = controls,
        };
    }

    /// <summary>
    /// Returns compliance trend data for a system within a date range, aggregated by granularity.
    /// </summary>
    public async Task<TrendDataDto?> GetTrendsAsync(
        string systemId,
        TrendQuery query,
        CancellationToken cancellationToken = default)
    {
        var systemExists = await _db.RegisteredSystems
            .AnyAsync(s => s.Id == systemId && s.IsActive, cancellationToken);
        if (!systemExists) return null;

        var startDate = query.StartDate ?? DateTime.UtcNow.AddDays(-90);
        var endDate = query.EndDate ?? DateTime.UtcNow;
        var granularity = query.Granularity ?? "Daily";

        var snapshots = await _db.ComplianceTrendSnapshots
            .Where(s => s.RegisteredSystemId == systemId
                     && s.CapturedAt >= startDate
                     && s.CapturedAt <= endDate)
            .OrderBy(s => s.CapturedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var grouped = GroupByGranularity(snapshots, granularity);

        // Compute significant decline (>5% drop between consecutive points)
        var points = new List<TrendDataPointDto>();
        double? previousScore = null;

        foreach (var g in grouped)
        {
            var avgScore = g.Average(s => s.ComplianceScore);
            var isDecline = previousScore.HasValue && (previousScore.Value - avgScore) > 5;

            points.Add(new TrendDataPointDto
            {
                Date = g.Key,
                ComplianceScore = Math.Round(avgScore, 1),
                CatICount = (int)Math.Round(g.Average(s => s.CatICount)),
                CatIICount = (int)Math.Round(g.Average(s => s.CatIICount)),
                CatIIICount = (int)Math.Round(g.Average(s => s.CatIIICount)),
                OpenPoamCount = (int)Math.Round(g.Average(s => s.OpenPoamCount)),
                OverduePoamCount = (int)Math.Round(g.Average(s => s.OverduePoamCount)),
                NarrativeCoverage = Math.Round(g.Average(s => s.NarrativeCoverage), 1),
                IsSignificantDecline = isDecline,
            });

            previousScore = avgScore;
        }

        _logger.LogInformation(
            "GetTrendsAsync: systemId={SystemId}, range={Start}..{End}, granularity={Gran}, points={Count}",
            systemId, startDate, endDate, granularity, points.Count);

        return new TrendDataDto
        {
            SystemId = systemId,
            Granularity = granularity,
            DataPoints = points,
        };
    }

    private static IEnumerable<IGrouping<DateTime, ComplianceTrendSnapshot>> GroupByGranularity(
        List<ComplianceTrendSnapshot> snapshots, string granularity)
    {
        return granularity.ToLowerInvariant() switch
        {
            "weekly" => snapshots.GroupBy(s => StartOfWeek(s.CapturedAt)),
            "monthly" => snapshots.GroupBy(s => new DateTime(s.CapturedAt.Year, s.CapturedAt.Month, 1)),
            "quarterly" => snapshots.GroupBy(s => new DateTime(s.CapturedAt.Year, ((s.CapturedAt.Month - 1) / 3) * 3 + 1, 1)),
            _ => snapshots.GroupBy(s => s.CapturedAt.Date), // Daily
        };
    }

    private static DateTime StartOfWeek(DateTime dt)
    {
        var diff = (7 + (dt.DayOfWeek - DayOfWeek.Monday)) % 7;
        return dt.Date.AddDays(-diff);
    }
}
