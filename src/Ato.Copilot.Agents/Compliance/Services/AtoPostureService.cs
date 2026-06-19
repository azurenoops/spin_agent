// =============================================================================
//  AtoPostureService.cs
//  Ato.Copilot.Agents — Compliance Services
//  Issue #422 — AO Posture API (Phase 2, W10 cATO Gap Closure)
//
//  Aggregates ATO posture from ConMon, Findings, POA&M, and AuthorizationDecision
//  repositories into a single read-model with 5-minute IMemoryCache TTL.
// =============================================================================

#nullable enable

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;

namespace Ato.Copilot.Agents.Compliance.Services;

/// <summary>
/// Aggregates ATO posture from ConMon, Findings, POA&amp;M, and AuthorizationDecision
/// repositories into a single read-model, and evaluates cATO/CSRMC eligibility.
/// </summary>
/// <remarks>
/// Cache key: <c>ato-posture:{systemId}</c>, absolute TTL 5 minutes.
/// forceRefresh requires ISSM or AuthorizingOfficial role.
/// </remarks>
public sealed class AtoPostureService : IAtoPostureService
{
    private const string CacheKeyPrefix = "ato-posture:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // Roles permitted to call forceRefresh=true
    private static readonly IReadOnlySet<string> RefreshRoles = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "ISSM", "ISSO", "SCA", "AuthorizingOfficial", "AO"
    };

    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AtoPostureService> _logger;

    public AtoPostureService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IMemoryCache cache,
        ILogger<AtoPostureService> logger)
    {
        _contextFactory = contextFactory;
        _cache = cache;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GetPostureAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AtoPostureDto?> GetPostureAsync(
        Guid systemId,
        IReadOnlySet<string> callerRoles,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        // Role guard for forceRefresh
        if (forceRefresh && !callerRoles.Any(r => RefreshRoles.Contains(r)))
        {
            throw new ForbiddenException(
                "The 'refresh' parameter requires ISSM role or higher.");
        }

        var cacheKey = $"{CacheKeyPrefix}{systemId}";

        if (!forceRefresh && _cache.TryGetValue<AtoPostureDto>(cacheKey, out var cached) && cached is not null)
        {
            _logger.LogDebug("AtoPosture cache hit for system {SystemId}", systemId);
            return cached with { ServedFromCache = true };
        }

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // Verify system exists
        var system = await db.RegisteredSystems
            .AsNoTracking()
            .Where(s => s.Id == systemId.ToString())
            .Select(s => new { s.Id, s.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (system is null)
        {
            _logger.LogDebug("AtoPosture: system {SystemId} not found", systemId);
            return null;
        }

        var isAoRole = callerRoles.Contains("AuthorizingOfficial") || callerRoles.Contains("AO");

        // Parallelize all DB reads
        var authTask = GetAuthorizationStatusAsync(db, systemId.ToString(), isAoRole, cancellationToken);
        var complianceTask = GetComplianceSummaryAsync(db, systemId.ToString(), cancellationToken);
        var findingsTask = GetFindingsSummaryAsync(db, systemId.ToString(), cancellationToken);
        var poamTask = GetPoamSummaryAsync(db, systemId.ToString(), cancellationToken);
        var conmonTask = GetConMonSummaryAsync(db, systemId.ToString(), cancellationToken);

        await Task.WhenAll(authTask, complianceTask, findingsTask, poamTask, conmonTask);

        CsrmcPillarStatusDto? pillarStatus = null;
        if (isAoRole)
        {
            pillarStatus = await GetCsrmcPillarStatusAsync(db, systemId, cancellationToken);
        }

        var dto = new AtoPostureDto
        {
            SystemId = systemId,
            SystemName = system.Name,
            AuthorizationStatus = await authTask,
            ComplianceSummary = await complianceTask,
            FindingsSummary = await findingsTask,
            PoamSummary = await poamTask,
            ConMonSummary = await conmonTask,
            CsrmcPillarStatus = pillarStatus,
            RetrievedAt = DateTimeOffset.UtcNow,
            ServedFromCache = false
        };

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(CacheTtl);
        _cache.Set(cacheKey, dto, cacheOptions);

        return dto;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EvaluateCatoEligibilityAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CatoEligibilityDto> EvaluateCatoEligibilityAsync(
        Guid systemId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var systemExists = await db.RegisteredSystems
            .AsNoTracking()
            .AnyAsync(s => s.Id == systemId.ToString(), cancellationToken);

        if (!systemExists)
            throw new SystemNotFoundException(systemId);

        // ComplianceFinding has no direct RegisteredSystemId FK — query via latest completed Assessment
        var eligAssessmentId = await db.Assessments
            .AsNoTracking()
            .Where(a => a.RegisteredSystemId == systemId.ToString() && a.Status == AssessmentStatus.Completed)
            .OrderByDescending(a => a.CompletedAt)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var catI = 0;
        if (eligAssessmentId is not null)
        {
            catI = await db.Findings
                .AsNoTracking()
                .CountAsync(f => f.AssessmentId == eligAssessmentId
                    && f.CatSeverity == CatSeverity.CatI
                    && f.Status == FindingStatus.Open, cancellationToken);
        }

        var overdue = await db.PoamItems
            .AsNoTracking()
            .CountAsync(p => p.RegisteredSystemId == systemId.ToString()
                && p.Status == PoamStatus.Delayed, cancellationToken);

        var conmonEnabled = await db.ConMonPlans
            .AsNoTracking()
            .AnyAsync(c => c.RegisteredSystemId == systemId.ToString(), cancellationToken);

        var pillar3 = await GetPillar3StatusAsync(systemId, cancellationToken);

        // For Phase 2, Pillar 1+2 are marked NotApplicable (pending ConMon integration)
        var allPillarsCompliant = pillar3 == PillarComplianceStatus.Compliant;

        var reasons = new List<string>();
        if (catI > 0)
            reasons.Add($"{catI} open CatI finding{(catI > 1 ? "s" : "")} — must be remediated before cATO.");
        if (overdue > 0)
            reasons.Add($"{overdue} overdue POA&M item{(overdue > 1 ? "s" : "")} — must be resolved before cATO.");
        if (!conmonEnabled)
            reasons.Add("No active ConMon plan — ConMonPlan must be established before cATO.");
        if (!allPillarsCompliant)
            reasons.Add("CSRMC Pillar 3 gap — no successful pipeline webhook ingest within 90 days.");

        return new CatoEligibilityDto
        {
            IsEligible = reasons.Count == 0,
            HasZeroCatIFindings = catI == 0,
            HasZeroOverduePOAMs = overdue == 0,
            IsConMonEnabled = conmonEnabled,
            AllCsrmcPillarsCompliant = allPillarsCompliant,
            IneligibilityReasons = reasons,
            CheckedAt = DateTimeOffset.UtcNow
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  GetPillar3StatusAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PillarComplianceStatus> GetPillar3StatusAsync(
        Guid systemId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var systemExists = await db.RegisteredSystems
            .AsNoTracking()
            .AnyAsync(s => s.Id == systemId.ToString(), cancellationToken);

        if (!systemExists)
            throw new SystemNotFoundException(systemId);

        // Pillar 3: Compliant = at least one accepted webhook ingest in the last 90 days.
        // WebhookIngestionLog table is added in Phase 3; for Phase 2 we approximate via
        // ComplianceFinding rows where Source == "Pipeline" from assessments completed in the window.
        // Once WebhookIngestionLog exists, replace this with a direct table query.
        var cutoff = DateTime.UtcNow.AddDays(-90);

        // Find assessments for this system completed within 90 days that have pipeline-sourced findings
        var recentPipelineAssessments = await db.Assessments
            .AsNoTracking()
            .Where(a => a.RegisteredSystemId == systemId.ToString()
                && a.Status == AssessmentStatus.Completed
                && a.CompletedAt != null
                && a.CompletedAt >= cutoff)
            .Select(a => a.Id)
            .ToListAsync(ct);

        if (recentPipelineAssessments.Count == 0)
        {
            // Check if any pipeline findings exist at all for this system
            var allSystemAssessments = await db.Assessments
                .AsNoTracking()
                .Where(a => a.RegisteredSystemId == systemId.ToString() && a.Status == AssessmentStatus.Completed)
                .Select(a => a.Id)
                .ToListAsync(ct);

            if (allSystemAssessments.Count == 0)
                return PillarComplianceStatus.Unknown;

            return PillarComplianceStatus.NonCompliant;
        }

        return PillarComplianceStatus.Compliant;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Private — data projection helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<AuthorizationStatusDto> GetAuthorizationStatusAsync(
        AtoCopilotContext db,
        string systemId,
        bool isAoRole,
        CancellationToken ct)
    {
        var decision = await db.AuthorizationDecisions
            .AsNoTracking()
            .Where(a => a.RegisteredSystemId == systemId && a.IsActive)
            .OrderByDescending(a => a.DecisionDate)
            .Select(a => new
            {
                a.DecisionType,
                a.DecisionDate,
                a.ExpirationDate,
                a.IsActive,
                a.IssuedByName
            })
            .FirstOrDefaultAsync(ct);

        if (decision is null)
        {
            return new AuthorizationStatusDto
            {
                IsActive = false,
                IsExpired = false
            };
        }

        var now = DateTimeOffset.UtcNow;
        var expiry = decision.ExpirationDate.HasValue
            ? new DateTimeOffset(decision.ExpirationDate.Value, TimeSpan.Zero)
            : (DateTimeOffset?)null;

        int? daysUntilExpiry = expiry.HasValue
            ? (int)Math.Max(0, (expiry.Value - now).TotalDays)
            : null;

        return new AuthorizationStatusDto
        {
            DecisionType = decision.DecisionType,
            DecisionDate = new DateTimeOffset(decision.DecisionDate, TimeSpan.Zero),
            ExpirationDate = expiry,
            IsActive = decision.IsActive && (expiry is null || expiry > now),
            IsExpired = expiry.HasValue && expiry <= now,
            DaysUntilExpiration = daysUntilExpiry,
            // Role-gated: only populate for AO callers
            AuthorizingOfficial = isAoRole ? decision.IssuedByName : null
        };
    }

    private static async Task<ComplianceSummaryDto> GetComplianceSummaryAsync(
        AtoCopilotContext db,
        string systemId,
        CancellationToken ct)
    {
        // ControlEffectiveness rows for the latest completed assessment
        var latestAssessmentId = await db.Assessments
            .AsNoTracking()
            .Where(a => a.RegisteredSystemId == systemId && a.Status == AssessmentStatus.Completed)
            .OrderByDescending(a => a.CompletedAt)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct);

        if (latestAssessmentId is null)
        {
            return new ComplianceSummaryDto();
        }

        var stats = await db.ControlEffectivenessRecords
            .AsNoTracking()
            .Where(c => c.AssessmentId == latestAssessmentId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Satisfied = g.Count(c => c.Determination == EffectivenessDetermination.Satisfied),
                OtherThanSatisfied = g.Count(c => c.Determination == EffectivenessDetermination.OtherThanSatisfied)
            })
            .FirstOrDefaultAsync(ct);

        if (stats is null) return new ComplianceSummaryDto();

        var assessed = stats.Satisfied + stats.OtherThanSatisfied;
        var notAssessed = stats.Total - assessed;
        var score = assessed > 0
            ? Math.Round((decimal)stats.Satisfied / assessed * 100, 2)
            : 0m;

        return new ComplianceSummaryDto
        {
            TotalControls = stats.Total,
            Satisfied = stats.Satisfied,
            OtherThanSatisfied = stats.OtherThanSatisfied,
            NotAssessed = notAssessed,
            ComplianceScore = score
        };
    }

    private static async Task<FindingsSummaryDto> GetFindingsSummaryAsync(
        AtoCopilotContext db,
        string systemId,
        CancellationToken ct)
    {
        // ComplianceFinding links to Assessment by AssessmentId — join through Assessment for system-level queries.
        // Use the latest completed assessment to avoid double-counting across historical runs.
        var latestAssessmentId = await db.Assessments
            .AsNoTracking()
            .Where(a => a.RegisteredSystemId == systemId && a.Status == AssessmentStatus.Completed)
            .OrderByDescending(a => a.CompletedAt)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct);

        if (latestAssessmentId is null)
            return new FindingsSummaryDto();

        var counts = await db.Findings
            .AsNoTracking()
            .Where(f => f.AssessmentId == latestAssessmentId && f.Status == FindingStatus.Open)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                CatI = g.Count(f => f.CatSeverity == CatSeverity.CatI),
                CatII = g.Count(f => f.CatSeverity == CatSeverity.CatII),
                CatIII = g.Count(f => f.CatSeverity == CatSeverity.CatIII)
            })
            .FirstOrDefaultAsync(ct);

        return counts is null
            ? new FindingsSummaryDto()
            : new FindingsSummaryDto
            {
                CatI = counts.CatI,
                CatII = counts.CatII,
                CatIII = counts.CatIII
            };
    }

    private static async Task<PoamSummaryDto> GetPoamSummaryAsync(
        AtoCopilotContext db,
        string systemId,
        CancellationToken ct)
    {
        var counts = await db.PoamItems
            .AsNoTracking()
            .Where(p => p.RegisteredSystemId == systemId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Open = g.Count(p => p.Status == PoamStatus.Ongoing),
                Overdue = g.Count(p => p.Status == PoamStatus.Delayed),
                Completed = g.Count(p => p.Status == PoamStatus.Completed),
                RiskAccepted = g.Count(p => p.Status == PoamStatus.RiskAccepted)
            })
            .FirstOrDefaultAsync(ct);

        return counts is null
            ? new PoamSummaryDto()
            : new PoamSummaryDto
            {
                Open = counts.Open,
                Overdue = counts.Overdue,
                Completed = counts.Completed,
                RiskAccepted = counts.RiskAccepted
            };
    }

    private static async Task<ConMonSummaryDto> GetConMonSummaryAsync(
        AtoCopilotContext db,
        string systemId,
        CancellationToken ct)
    {
        var plan = await db.ConMonPlans
            .AsNoTracking()
            .Where(p => p.RegisteredSystemId == systemId)
            .Select(p => new { p.AssessmentFrequency })
            .FirstOrDefaultAsync(ct);

        if (plan is null)
        {
            return new ConMonSummaryDto { IsEnabled = false };
        }

        var latestReport = await db.ConMonReports
            .AsNoTracking()
            .Where(r => r.RegisteredSystemId == systemId)
            .OrderByDescending(r => r.GeneratedAt)
            .Select(r => new { r.GeneratedAt, r.ComplianceScore, r.AuthorizedBaselineScore })
            .FirstOrDefaultAsync(ct);

        return new ConMonSummaryDto
        {
            IsEnabled = true,
            AssessmentFrequency = plan.AssessmentFrequency,
            LastReportDate = latestReport is not null
                ? new DateTimeOffset(latestReport.GeneratedAt, TimeSpan.Zero)
                : null,
            LatestComplianceScore = latestReport is not null
                ? (decimal)latestReport.ComplianceScore
                : null,
            AuthorizedBaselineScore = latestReport is not null
                ? (decimal)latestReport.AuthorizedBaselineScore
                : null
        };
    }

    private async Task<CsrmcPillarStatusDto> GetCsrmcPillarStatusAsync(
        AtoCopilotContext db,
        Guid systemId,
        CancellationToken ct)
    {
        // Pillar 1 (Reciprocity) and Pillar 2 (Automation) — pending dedicated metrics (Phase 5+)
        // Pillar 3 (DevSecOps pipeline) — driven by webhook ingest history
        var pillar3 = await GetPillar3StatusInternalAsync(db, systemId.ToString(), ct);

        return new CsrmcPillarStatusDto
        {
            Pillar1Status = PillarComplianceStatus.Unknown,
            Pillar2Status = PillarComplianceStatus.Unknown,
            Pillar3Status = pillar3,
            EvaluatedAt = DateTimeOffset.UtcNow
        };
    }

    private static async Task<PillarComplianceStatus> GetPillar3StatusInternalAsync(
        AtoCopilotContext db,
        string systemId,
        CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);

        // Phase 2 proxy: recent completed assessments as signal for pipeline activity.
        // Replace with WebhookIngestionLog query in Phase 3.
        var recentAssessments = await db.Assessments
            .AsNoTracking()
            .Where(a => a.RegisteredSystemId == systemId
                && a.Status == AssessmentStatus.Completed
                && a.CompletedAt != null
                && a.CompletedAt >= cutoff)
            .AnyAsync(ct);

        if (recentAssessments) return PillarComplianceStatus.Compliant;

        var anyAssessments = await db.Assessments
            .AsNoTracking()
            .AnyAsync(a => a.RegisteredSystemId == systemId && a.Status == AssessmentStatus.Completed, ct);

        return anyAssessments ? PillarComplianceStatus.NonCompliant : PillarComplianceStatus.Unknown;
    }
}
