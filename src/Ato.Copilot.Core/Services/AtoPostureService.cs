// =============================================================================
//  AtoPostureService.cs
//  Ato.Copilot.Core — Services
//  Issue #422 — AO Posture API (W10 cATO Gap Closure)
//
//  IAtoPostureService implementation.
//  Aggregates AuthorizationDecision, ComplianceFinding, PoamItem, ConMonPlan,
//  ConMonReport, ComplianceTrendSnapshot into AtoPostureDto.
//  5-minute IMemoryCache per system + role-gated fields.
// =============================================================================

#nullable enable

using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Compliance;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Poam;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services;

/// <summary>
/// Aggregates ATO posture from ConMon, Findings, POA&amp;M, and AuthorizationDecision
/// repositories into a single read-model and evaluates cATO/CSRMC eligibility.
/// </summary>
/// <remarks>
/// Cache key: <c>ato-posture:{systemId}</c> with 5-minute absolute TTL.
/// forceRefresh = true requires ISSM or higher role.
/// AuthorizingOfficial + CsrmcPillarStatus fields are null for callers without AO role.
/// </remarks>
public sealed class AtoPostureService : IAtoPostureService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<AtoCopilotContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AtoPostureService> _logger;

    // Roles authorized to use forceRefresh=true
    private static readonly IReadOnlySet<string> RefreshAllowedRoles =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ISSM", "AO", "AuthorizingOfficial", "SCA", "Engineer"
        };

    private const string AuthorizingOfficialRole = "AuthorizingOfficial";

    public AtoPostureService(
        IDbContextFactory<AtoCopilotContext> dbFactory,
        IMemoryCache cache,
        ILogger<AtoPostureService> logger)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AtoPostureDto?> GetPostureAsync(
        Guid systemId,
        IReadOnlySet<string> callerRoles,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        // Authorization gate: forceRefresh requires ISSM+
        if (forceRefresh && !callerRoles.Any(r => RefreshAllowedRoles.Contains(r)))
            throw new ForbiddenException("forceRefresh requires ISSM or higher role.");

        var cacheKey = $"ato-posture:{systemId}";
        bool isAo = callerRoles.Contains(AuthorizingOfficialRole);

        // Attempt cache hit (unless forceRefresh)
        if (!forceRefresh &&
            _cache.TryGetValue(cacheKey, out AtoPostureDto? cached) &&
            cached is not null)
        {
            _logger.LogDebug("ATO posture cache hit for system {SystemId}", systemId);
            return ApplyRoleGating(cached with { ServedFromCache = true }, isAo);
        }

        // Cache miss — compute from DB
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var system = await db.RegisteredSystems
            .AsNoTracking()
            .Where(s => s.Id == systemId.ToString() && s.IsActive)
            .Select(s => new { s.Id, s.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (system is null) return null;

        var posture = await BuildPostureAsync(db, systemId, system.Name, cancellationToken);

        _cache.Set(cacheKey, posture, CacheTtl);
        _logger.LogDebug("ATO posture computed and cached for system {SystemId}", systemId);

        return ApplyRoleGating(posture with { ServedFromCache = false }, isAo);
    }

    /// <inheritdoc />
    public async Task<CatoEligibilityDto> EvaluateCatoEligibilityAsync(
        Guid systemId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var systemExists = await db.RegisteredSystems
            .AsNoTracking()
            .AnyAsync(s => s.Id == systemId.ToString() && s.IsActive, cancellationToken);

        if (!systemExists)
            throw new SystemNotFoundException(systemId);

        // Criterion 1 — zero open CatI findings
        var catICount = await db.Findings
            .AsNoTracking()
            .CountAsync(f => f.RegisteredSystemId == systemId.ToString() &&
                             f.CatSeverity == CatSeverity.CatI &&
                             f.Status == FindingStatus.Open, cancellationToken);

        // Criterion 2 — zero overdue POA&M items
        var now = DateTime.UtcNow;
        var overduePoamCount = await db.PoamItems
            .AsNoTracking()
            .CountAsync(p => p.RegisteredSystemId == systemId.ToString() &&
                              p.Status == PoamStatus.Delayed &&
                              p.ScheduledCompletionDate < now, cancellationToken);

        // Criterion 3 — active ConMonPlan exists
        var conMonEnabled = await db.ConMonPlans
            .AsNoTracking()
            .AnyAsync(c => c.RegisteredSystemId == systemId.ToString(), cancellationToken);

        // Criterion 4 — all three CSRMC pillars Compliant
        // Phase 1: Pillar 3 = Unknown (no pipeline ingestion history yet — added in Phase 2)
        var pillar3Status = await GetPillar3StatusAsync(systemId, cancellationToken);
        bool allPillarsCompliant = pillar3Status == PillarComplianceStatus.Compliant;

        bool hasZeroCatI = catICount == 0;
        bool hasZeroOverdue = overduePoamCount == 0;
        bool isEligible = hasZeroCatI && hasZeroOverdue && conMonEnabled && allPillarsCompliant;

        var reasons = new List<string>();
        if (!hasZeroCatI)
            reasons.Add($"{catICount} open CAT I finding(s) must be remediated.");
        if (!hasZeroOverdue)
            reasons.Add($"{overduePoamCount} overdue POA&M item(s) must be resolved.");
        if (!conMonEnabled)
            reasons.Add("No active Continuous Monitoring plan exists.");
        if (!allPillarsCompliant)
            reasons.Add($"CSRMC Pillar 3 gap: pipeline not compliant (status: {pillar3Status}).");

        return new CatoEligibilityDto
        {
            IsEligible             = isEligible,
            HasZeroCatIFindings    = hasZeroCatI,
            HasZeroOverduePOAMs    = hasZeroOverdue,
            IsConMonEnabled        = conMonEnabled,
            AllCsrmcPillarsCompliant = allPillarsCompliant,
            IneligibilityReasons   = reasons.AsReadOnly(),
            CheckedAt              = DateTimeOffset.UtcNow,
        };
    }

    /// <inheritdoc />
    public async Task<PillarComplianceStatus> GetPillar3StatusAsync(
        Guid systemId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var systemExists = await db.RegisteredSystems
            .AsNoTracking()
            .AnyAsync(s => s.Id == systemId.ToString() && s.IsActive, cancellationToken);

        if (!systemExists)
            throw new SystemNotFoundException(systemId);

        // TODO(#422-phase2): PipelineIngestionLogs DbSet + entity added in Phase 2.
        // Phase 1 returns Unknown — Pillar 3 compliance requires webhook ingestion history.
        return PillarComplianceStatus.Unknown;
    }

    // ─── Private helpers ────────────────────────────────────────────────────

    private static AtoPostureDto ApplyRoleGating(AtoPostureDto posture, bool isAo)
    {
        return posture with
        {
            AuthorizationStatus = isAo
                ? posture.AuthorizationStatus
                : posture.AuthorizationStatus with { AuthorizingOfficial = null },
            CsrmcPillarStatus = isAo ? posture.CsrmcPillarStatus : null,
        };
    }

    private static async Task<AtoPostureDto> BuildPostureAsync(
        AtoCopilotContext db,
        Guid systemId,
        string systemName,
        CancellationToken ct)
    {
        var systemIdStr = systemId.ToString();

        // ── Authorization ──
        var authDecision = await db.AuthorizationDecisions
            .AsNoTracking()
            .Where(a => a.RegisteredSystemId == systemIdStr && a.IsActive)
            .OrderByDescending(a => a.DecisionDate)
            .FirstOrDefaultAsync(ct);

        var authStatus = BuildAuthorizationStatus(authDecision);

        // ── Compliance summary (from latest ComplianceTrendSnapshot) ──
        var snapshot = await db.ComplianceTrendSnapshots
            .AsNoTracking()
            .Where(s => s.RegisteredSystemId == systemIdStr)
            .OrderByDescending(s => s.CapturedAt)
            .FirstOrDefaultAsync(ct);

        // Derive control counts from ControlBaseline + ControlEffectiveness
        var baseline = await db.ControlBaselines
            .AsNoTracking()
            .Where(cb => cb.RegisteredSystemId == systemIdStr)
            .Select(cb => new { cb.TotalControls })
            .FirstOrDefaultAsync(ct);

        int totalControls = baseline?.TotalControls ?? 0;
        double complianceScoreRaw = snapshot?.ComplianceScore ?? 0;
        var complianceSummary = new ComplianceSummaryDto
        {
            TotalControls   = totalControls,
            Satisfied       = (int)Math.Round(totalControls * complianceScoreRaw / 100.0),
            OtherThanSatisfied = (int)Math.Round(totalControls * (1.0 - complianceScoreRaw / 100.0)),
            NotAssessed     = 0,
            ComplianceScore = (decimal)Math.Round(complianceScoreRaw, 2),
        };

        // ── Findings summary ──
        var findingCounts = await db.Findings
            .AsNoTracking()
            .Where(f => f.RegisteredSystemId == systemIdStr && f.Status == FindingStatus.Open)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                CatI   = g.Count(f => f.CatSeverity == CatSeverity.CatI),
                CatII  = g.Count(f => f.CatSeverity == CatSeverity.CatII),
                CatIII = g.Count(f => f.CatSeverity == CatSeverity.CatIII),
            })
            .FirstOrDefaultAsync(ct);

        var findingsSummary = new FindingsSummaryDto
        {
            CatI   = findingCounts?.CatI   ?? 0,
            CatII  = findingCounts?.CatII  ?? 0,
            CatIII = findingCounts?.CatIII ?? 0,
        };

        // ── POA&M summary ──
        var now = DateTime.UtcNow;
        var poamCounts = await db.PoamItems
            .AsNoTracking()
            .Where(p => p.RegisteredSystemId == systemIdStr)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Open         = g.Count(p => p.Status == PoamStatus.Ongoing),
                Overdue      = g.Count(p => p.Status == PoamStatus.Delayed && p.ScheduledCompletionDate < now),
                Completed    = g.Count(p => p.Status == PoamStatus.Completed),
                RiskAccepted = g.Count(p => p.Status == PoamStatus.RiskAccepted),
            })
            .FirstOrDefaultAsync(ct);

        var poamSummary = new PoamSummaryDto
        {
            Open         = poamCounts?.Open         ?? 0,
            Overdue      = poamCounts?.Overdue       ?? 0,
            Completed    = poamCounts?.Completed     ?? 0,
            RiskAccepted = poamCounts?.RiskAccepted  ?? 0,
        };

        // ── ConMon summary ──
        var conMonPlan = await db.ConMonPlans
            .AsNoTracking()
            .Where(c => c.RegisteredSystemId == systemIdStr)
            .FirstOrDefaultAsync(ct);

        var latestReport = conMonPlan is not null
            ? await db.ConMonReports
                .AsNoTracking()
                .Where(r => r.ConMonPlanId == conMonPlan.Id)
                .OrderByDescending(r => r.GeneratedAt)
                .FirstOrDefaultAsync(ct)
            : null;

        var conMonSummary = new ConMonSummaryDto
        {
            IsEnabled             = conMonPlan is not null,
            LastReportDate        = latestReport is not null
                                    ? new DateTimeOffset(latestReport.GeneratedAt, TimeSpan.Zero)
                                    : null,
            AssessmentFrequency   = conMonPlan?.AssessmentFrequency,
            LatestComplianceScore = latestReport is not null ? (decimal)latestReport.ComplianceScore : null,
            AuthorizedBaselineScore = latestReport?.AuthorizedBaselineScore.HasValue == true
                                      ? (decimal)latestReport.AuthorizedBaselineScore!.Value
                                      : null,
        };

        // ── CSRMC Pillar Status (AO-gated — always computed for cache, stripped on return) ──
        var csrmcStatus = new CsrmcPillarStatusDto
        {
            Pillar1Status = PillarComplianceStatus.Unknown,
            Pillar2Status = PillarComplianceStatus.Unknown,
            Pillar3Status = PillarComplianceStatus.Unknown, // Phase 2 — requires webhook entity
            EvaluatedAt   = DateTimeOffset.UtcNow,
        };

        return new AtoPostureDto
        {
            SystemId            = systemId,
            SystemName          = systemName,
            AuthorizationStatus = authStatus,
            ComplianceSummary   = complianceSummary,
            FindingsSummary     = findingsSummary,
            PoamSummary         = poamSummary,
            ConMonSummary       = conMonSummary,
            CsrmcPillarStatus   = csrmcStatus,
            RetrievedAt         = DateTimeOffset.UtcNow,
            ServedFromCache     = false,
        };
    }

    private static AuthorizationStatusDto BuildAuthorizationStatus(AuthorizationDecision? decision)
    {
        if (decision is null)
            return new AuthorizationStatusDto { IsActive = false, IsExpired = false };

        var now = DateTime.UtcNow;
        bool isExpired = decision.ExpirationDate.HasValue && decision.ExpirationDate.Value < now;
        int? daysUntilExpiration = decision.ExpirationDate.HasValue && !isExpired
            ? (int)(decision.ExpirationDate.Value - now).TotalDays
            : null;

        return new AuthorizationStatusDto
        {
            DecisionType        = decision.DecisionType,
            DecisionDate        = new DateTimeOffset(decision.DecisionDate, TimeSpan.Zero),
            ExpirationDate      = decision.ExpirationDate.HasValue
                                  ? new DateTimeOffset(decision.ExpirationDate.Value, TimeSpan.Zero)
                                  : null,
            IsActive            = decision.IsActive && !isExpired,
            IsExpired           = isExpired,
            DaysUntilExpiration = daysUntilExpiration,
            // AuthorizingOfficial populated here — stripped by ApplyRoleGating for non-AO callers
            AuthorizingOfficial = decision.IssuedByName,
        };
    }
}
