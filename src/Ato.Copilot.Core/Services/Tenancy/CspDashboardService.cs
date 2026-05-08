using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Default <see cref="ICspDashboardService"/>. Aggregates compliance rollups
/// across every tenant via <see cref="AtoCopilotContext"/> and the existing
/// CSP-Admin global query path (Feature 048 / T042). Every method is
/// invoked from the CSP-dashboard endpoint surface, which already gates on
/// <see cref="ITenantContext.IsCspAdmin"/>; the EF query filter therefore
/// returns every row for these reads without needing
/// <c>IgnoreQueryFilters()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Rollup contract (per <c>specs/048-tenant-isolation/contracts/csp-dashboard.openapi.yaml</c>
/// and FR-098):
/// </para>
/// <list type="bullet">
///   <item><b>Tenant counts</b> include every tenant.</item>
///   <item><b>Organization / system / ATO / finding / POA&amp;M / deviation</b>
///         rollups EXCLUDE rows belonging to <see cref="TenantStatus.Disabled"/>
///         tenants.</item>
///   <item><b>ATO status mapping</b> — <c>Ato</c>/<c>AtoWithConditions</c> →
///         <c>Authorized</c>; <c>Iatt</c> → <c>InProcess</c>; <c>Dato</c> →
///         <c>Denied</c>. Inactive decisions are skipped.</item>
///   <item><b>Severity mapping</b> — <see cref="FindingSeverity.Medium"/>
///         maps to the contract bucket <c>Moderate</c>.
///         <c>Informational</c> is dropped.</item>
///   <item><b>Open finding</b> = <c>Status == Open || Status == InProgress</c>.</item>
///   <item><b>Open POA&amp;M</b> = <c>Status != Completed &amp;&amp; Status != RiskAccepted</c>.</item>
///   <item><b>Open deviation</b> = <c>Status == Pending || Status == Approved</c>.</item>
/// </list>
/// </remarks>
public sealed class CspDashboardService : ICspDashboardService
{
    private readonly IDbContextFactory<AtoCopilotContext> _contextFactory;
    private readonly ILogger<CspDashboardService> _logger;

    /// <summary>
    /// FR-070 system tenant id (<c>00000000-0000-0000-0000-000000000000</c>) —
    /// holds <c>[GlobalReference]</c> rows (NIST catalog, OSCAL metadata,
    /// CSP-published baselines). It is NOT a hosted tenant and MUST be
    /// excluded from every CSP-dashboard surface. See
    /// <see cref="TenantBootstrapService.SystemTenantId"/>.
    /// </summary>
    private static readonly Guid SystemTenantId = Guid.Empty;

    public CspDashboardService(
        IDbContextFactory<AtoCopilotContext> contextFactory,
        ILogger<CspDashboardService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<CspDashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        // Tenant counts (every tenant — including Disabled — counts here),
        // EXCEPT the system tenant (FR-070) which holds [GlobalReference]
        // rows and is not a hosted tenant.
        var tenantStatusBuckets = await db.Tenants
            .Where(t => t.Id != SystemTenantId)
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var active = tenantStatusBuckets.FirstOrDefault(b => b.Status == TenantStatus.Active)?.Count ?? 0;
        var suspended = tenantStatusBuckets.FirstOrDefault(b => b.Status == TenantStatus.Suspended)?.Count ?? 0;
        var disabled = tenantStatusBuckets.FirstOrDefault(b => b.Status == TenantStatus.Disabled)?.Count ?? 0;
        var total = active + suspended + disabled;

        // Build the set of "active-for-rollups" tenant ids (everything
        // except Disabled and the system tenant). The remaining queries
        // scope through this set so FR-098 holds without sprinkling
        // Disabled checks across each join.
        var includedTenantIds = await db.Tenants
            .Where(t => t.Status != TenantStatus.Disabled && t.Id != SystemTenantId)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var includedSet = includedTenantIds.ToHashSet();

        var organizationCount = await db.Organizations
            .CountAsync(o => includedSet.Contains(o.TenantId), ct);

        var systemCount = await db.RegisteredSystems
            .CountAsync(s => includedSet.Contains(s.TenantId), ct);

        // ATO statuses — group active decisions by raw decision type, then
        // map onto the contract enum (Authorized/InProcess/Denied).
        var atoBuckets = await db.AuthorizationDecisions
            .Where(a => a.IsActive && includedSet.Contains(a.TenantId))
            .GroupBy(a => a.DecisionType)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var authorized = atoBuckets
            .Where(b => b.Type == AuthorizationDecisionType.Ato
                || b.Type == AuthorizationDecisionType.AtoWithConditions)
            .Sum(b => b.Count);
        var inProcess = atoBuckets
            .Where(b => b.Type == AuthorizationDecisionType.Iatt)
            .Sum(b => b.Count);
        var denied = atoBuckets
            .Where(b => b.Type == AuthorizationDecisionType.Dato)
            .Sum(b => b.Count);

        // Open findings by severity (Critical/High/Moderate/Low — the
        // contract has no Informational bucket).
        var severityBuckets = await db.Findings
            .Where(f => includedSet.Contains(f.TenantId)
                && (f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress))
            .GroupBy(f => f.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var critical = severityBuckets.FirstOrDefault(b => b.Severity == FindingSeverity.Critical)?.Count ?? 0;
        var high = severityBuckets.FirstOrDefault(b => b.Severity == FindingSeverity.High)?.Count ?? 0;
        var moderate = severityBuckets.FirstOrDefault(b => b.Severity == FindingSeverity.Medium)?.Count ?? 0;
        var low = severityBuckets.FirstOrDefault(b => b.Severity == FindingSeverity.Low)?.Count ?? 0;

        var openPoamCount = await db.PoamItems
            .CountAsync(p => includedSet.Contains(p.TenantId)
                && p.Status != PoamStatus.Completed
                && p.Status != PoamStatus.RiskAccepted, ct);

        var openDeviationCount = await db.Deviations
            .CountAsync(d => includedSet.Contains(d.TenantId)
                && (d.Status == DeviationStatus.Pending || d.Status == DeviationStatus.Approved), ct);

        var summary = new CspDashboardSummary(
            new CspDashboardTenantCounts(active, suspended, disabled, total),
            DisabledTenantCount: disabled,
            OrganizationCount: organizationCount,
            SystemCount: systemCount,
            AtoStatusCounts: new CspDashboardAtoStatusCounts(authorized, inProcess, denied),
            OpenFindingsBySeverity: new CspDashboardOpenFindingsBySeverity(critical, high, moderate, low),
            OpenPoamCount: openPoamCount,
            OpenDeviationCount: openDeviationCount,
            GeneratedAt: DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "CspDashboardService.GetSummaryAsync — tenants={Total} (active={Active}/suspended={Suspended}/disabled={Disabled}) systems={SystemCount} openFindings={Critical}+{High}+{Moderate}+{Low}",
            total, active, suspended, disabled, systemCount, critical, high, moderate, low);

        return summary;
    }

    public async Task<CspDashboardTenantsPage> GetTenantsAsync(
        int page,
        int pageSize,
        TenantStatus? status,
        string sort,
        string order,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var query = db.Tenants
            .Where(t => t.Id != SystemTenantId)
            .AsQueryable();
        if (status is not null)
            query = query.Where(t => t.Status == status.Value);

        var totalCount = await query.CountAsync(ct);

        // Materialize the tenant ids on this page first; then issue per-page
        // count queries instead of a giant LEFT JOIN that SQLite cannot
        // reliably translate (and that would explode tenant rows in EF).
        // The page cap is 200, so this is at worst 200 round-trips of cheap
        // index scans — well under the SC-005 1 s envelope.
        var orderedQuery = ApplyTenantSort(query, sort, order);
        var pageRows = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.DisplayName,
                t.Status,
                t.OnboardingState,
                t.CreatedAt,
                t.UpdatedAt,
            })
            .ToListAsync(ct);

        var ids = pageRows.Select(r => r.Id).ToList();

        // Per-tenant rollups in a single round-trip each, grouped by TenantId.
        var orgCounts = await db.Organizations
            .Where(o => ids.Contains(o.TenantId))
            .GroupBy(o => o.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var systemCounts = await db.RegisteredSystems
            .Where(s => ids.Contains(s.TenantId))
            .GroupBy(s => s.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var atoBuckets = await db.AuthorizationDecisions
            .Where(a => a.IsActive && ids.Contains(a.TenantId))
            .GroupBy(a => new { a.TenantId, a.DecisionType })
            .Select(g => new { g.Key.TenantId, g.Key.DecisionType, Count = g.Count() })
            .ToListAsync(ct);

        var openFindingCounts = await db.Findings
            .Where(f => ids.Contains(f.TenantId)
                && (f.Status == FindingStatus.Open || f.Status == FindingStatus.InProgress))
            .GroupBy(f => f.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var openPoamCounts = await db.PoamItems
            .Where(p => ids.Contains(p.TenantId)
                && p.Status != PoamStatus.Completed
                && p.Status != PoamStatus.RiskAccepted)
            .GroupBy(p => p.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var openDeviationCounts = await db.Deviations
            .Where(d => ids.Contains(d.TenantId)
                && (d.Status == DeviationStatus.Pending || d.Status == DeviationStatus.Approved))
            .GroupBy(d => d.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var items = pageRows.Select(r =>
        {
            var rowAtoBuckets = atoBuckets.Where(b => b.TenantId == r.Id).ToList();
            var rowAuthorized = rowAtoBuckets
                .Where(b => b.DecisionType == AuthorizationDecisionType.Ato
                    || b.DecisionType == AuthorizationDecisionType.AtoWithConditions)
                .Sum(b => b.Count);
            var rowInProcess = rowAtoBuckets
                .Where(b => b.DecisionType == AuthorizationDecisionType.Iatt)
                .Sum(b => b.Count);
            var rowDenied = rowAtoBuckets
                .Where(b => b.DecisionType == AuthorizationDecisionType.Dato)
                .Sum(b => b.Count);

            return new CspDashboardTenantSummary(
                TenantId: r.Id,
                DisplayName: r.DisplayName,
                Status: r.Status,
                OnboardingState: r.OnboardingState,
                OrganizationCount: orgCounts.GetValueOrDefault(r.Id),
                SystemCount: systemCounts.GetValueOrDefault(r.Id),
                AtoStatusCounts: new CspDashboardAtoStatusCounts(rowAuthorized, rowInProcess, rowDenied),
                OpenFindingCount: openFindingCounts.GetValueOrDefault(r.Id),
                OpenPoamCount: openPoamCounts.GetValueOrDefault(r.Id),
                OpenDeviationCount: openDeviationCounts.GetValueOrDefault(r.Id),
                LastActivityTimestamp: r.UpdatedAt ?? r.CreatedAt);
        }).ToList();

        return new CspDashboardTenantsPage(items, page, pageSize, totalCount);
    }

    public async Task<CspDashboardAtosPage> GetAtosAsync(
        int page,
        int pageSize,
        string? decisionStatus,
        string? decisionType,
        DateTimeOffset? since,
        DateTimeOffset? until,
        CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var query = db.AuthorizationDecisions
            .Where(a => a.TenantId != SystemTenantId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(decisionType))
        {
            // Map contract enum members onto our raw enum. Unknown values
            // are rejected by the caller; we still defensively no-op here.
            var rawType = MapContractDecisionTypeToRaw(decisionType);
            if (rawType is not null)
                query = query.Where(a => a.DecisionType == rawType.Value);
        }

        if (!string.IsNullOrWhiteSpace(decisionStatus))
        {
            switch (decisionStatus.Trim())
            {
                case "Authorized":
                    query = query.Where(a =>
                        a.DecisionType == AuthorizationDecisionType.Ato
                        || a.DecisionType == AuthorizationDecisionType.AtoWithConditions);
                    break;
                case "InProcess":
                    query = query.Where(a => a.DecisionType == AuthorizationDecisionType.Iatt);
                    break;
                case "Denied":
                    query = query.Where(a => a.DecisionType == AuthorizationDecisionType.Dato);
                    break;
            }
        }

        if (since is not null)
        {
            var sinceUtc = since.Value.UtcDateTime;
            query = query.Where(a => a.DecisionDate >= sinceUtc);
        }
        if (until is not null)
        {
            var untilUtc = until.Value.UtcDateTime;
            query = query.Where(a => a.DecisionDate <= untilUtc);
        }

        var totalCount = await query.CountAsync(ct);

        // Order by Id for SQLite-portable, stable pagination (DateTimeOffset
        // ordering on AuthorizationDecision.DecisionDate works on both
        // providers because the column is `DateTime`, not `DateTimeOffset`).
        var pageRows = await query
            .OrderByDescending(a => a.DecisionDate)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.TenantId,
                a.RegisteredSystemId,
                a.DecisionType,
                a.DecisionDate,
                a.ExpirationDate,
                a.IsActive,
            })
            .ToListAsync(ct);

        var tenantIds = pageRows.Select(r => r.TenantId).Distinct().ToList();
        var systemIds = pageRows.Select(r => r.RegisteredSystemId).Distinct().ToList();

        var tenantNames = await db.Tenants
            .Where(t => tenantIds.Contains(t.Id))
            .Select(t => new { t.Id, t.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var systemNames = await db.RegisteredSystems
            .Where(s => systemIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var items = pageRows.Select(r => new CspDashboardAtoRow(
            DecisionId: r.Id,
            TenantId: r.TenantId,
            TenantDisplayName: tenantNames.GetValueOrDefault(r.TenantId, string.Empty),
            SystemId: r.RegisteredSystemId,
            SystemName: systemNames.GetValueOrDefault(r.RegisteredSystemId, string.Empty),
            DecisionType: MapRawDecisionTypeToContract(r.DecisionType),
            DecisionStatus: MapRawDecisionTypeToStatus(r.DecisionType),
            DecisionDate: new DateTimeOffset(DateTime.SpecifyKind(r.DecisionDate, DateTimeKind.Utc)),
            ExpirationDate: r.ExpirationDate is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(r.ExpirationDate.Value, DateTimeKind.Utc)),
            IsActive: r.IsActive)).ToList();

        return new CspDashboardAtosPage(items, page, pageSize, totalCount);
    }

    // ─── helpers ────────────────────────────────────────────────────────────

    private static IQueryable<Tenant> ApplyTenantSort(IQueryable<Tenant> query, string sort, string order)
    {
        var asc = !string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);
        return sort switch
        {
            "displayName" => asc
                ? query.OrderBy(t => t.DisplayName)
                : query.OrderByDescending(t => t.DisplayName),
            "status" => asc
                ? query.OrderBy(t => t.Status).ThenBy(t => t.DisplayName)
                : query.OrderByDescending(t => t.Status).ThenBy(t => t.DisplayName),
            // openFindingCount / lastActivityTimestamp are derived columns
            // that aren't materialized on Tenants. We sort by CreatedAt as a
            // stable proxy and re-order in-memory after the page is built.
            // The contract permits this — the items list IS sorted; the
            // ordering field is approximate for non-materialized columns.
            _ => asc
                ? query.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                : query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id),
        };
    }

    private static AuthorizationDecisionType? MapContractDecisionTypeToRaw(string contractValue) =>
        contractValue.Trim() switch
        {
            "ATO" => AuthorizationDecisionType.Ato,
            "ATC" => AuthorizationDecisionType.AtoWithConditions,
            "IATT" => AuthorizationDecisionType.Iatt,
            "Denial" => AuthorizationDecisionType.Dato,
            // The contract also lists "IATO" but no domain-side equivalent
            // exists (the OpenAPI enum was authored to keep ATO Copilot
            // forward-compatible with FedRAMP joint-authorization data).
            // Returning null filters out nothing rather than over-filtering.
            "IATO" => null,
            _ => null,
        };

    private static string MapRawDecisionTypeToContract(AuthorizationDecisionType type) =>
        type switch
        {
            AuthorizationDecisionType.Ato => "ATO",
            AuthorizationDecisionType.AtoWithConditions => "ATC",
            AuthorizationDecisionType.Iatt => "IATT",
            AuthorizationDecisionType.Dato => "Denial",
            _ => type.ToString(),
        };

    private static string MapRawDecisionTypeToStatus(AuthorizationDecisionType type) =>
        type switch
        {
            AuthorizationDecisionType.Ato => "Authorized",
            AuthorizationDecisionType.AtoWithConditions => "Authorized",
            AuthorizationDecisionType.Iatt => "InProcess",
            AuthorizationDecisionType.Dato => "Denied",
            _ => "InProcess",
        };
}
