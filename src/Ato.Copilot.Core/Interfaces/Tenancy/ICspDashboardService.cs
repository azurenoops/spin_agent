using Ato.Copilot.Core.Models.Tenancy;

namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// CSP-Admin cross-tenant operational dashboard service (Feature 048 US8 /
/// FR-094 / FR-098). Aggregates per-tenant compliance signals (tenants,
/// systems, ATOs, findings, POA&amp;Ms, deviations) into roll-up DTOs that
/// power the all-up dashboard. <c>Disabled</c> tenants are visible in the
/// per-tenant lists but excluded from every roll-up except the
/// <c>Disabled</c> bucket.
/// </summary>
/// <remarks>
/// Implementations issue every query against the canonical
/// <c>AtoCopilotContext</c>; they MUST NOT call <c>IgnoreQueryFilters()</c>.
/// The Feature 048 / T042 query filter already returns every row when
/// <see cref="ITenantContext.IsCspAdmin"/> is <c>true</c> AND
/// <see cref="ITenantContext.ImpersonatedTenantId"/> is <c>null</c>, which
/// is the only state in which this surface is reachable (the endpoints gate
/// every request on <c>IsCspAdmin</c>).
/// </remarks>
public interface ICspDashboardService
{
    /// <summary>
    /// Returns every cross-tenant roll-up needed by the dashboard summary
    /// (tenant counts, organization/system totals, ATO statuses, open
    /// findings by severity, open POA&amp;M / deviation counts).
    /// </summary>
    Task<CspDashboardSummary> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated list of tenants with per-tenant KPI projections.
    /// </summary>
    /// <param name="page">1-based page index. Negative or zero is rejected by the caller.</param>
    /// <param name="pageSize">Page size; capped at 200 by the caller.</param>
    /// <param name="status">Optional <see cref="TenantStatus"/> filter.</param>
    /// <param name="sort">Sort field — <c>displayName|status|openFindingCount|lastActivityTimestamp</c>.</param>
    /// <param name="order">Sort direction — <c>asc</c> or <c>desc</c>.</param>
    Task<CspDashboardTenantsPage> GetTenantsAsync(
        int page,
        int pageSize,
        TenantStatus? status,
        string sort,
        string order,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a paginated list of authorization decisions across every
    /// tenant, joined with <see cref="Tenant"/> for the <c>tenantDisplayName</c>
    /// projection.
    /// </summary>
    /// <param name="decisionStatus">Roll-up filter — <c>Authorized|InProcess|Denied</c>.</param>
    /// <param name="decisionType">Raw filter — <c>ATO|IATO|IATT|ATC|Denial</c>.</param>
    Task<CspDashboardAtosPage> GetAtosAsync(
        int page,
        int pageSize,
        string? decisionStatus,
        string? decisionType,
        DateTimeOffset? since,
        DateTimeOffset? until,
        CancellationToken ct = default);
}

/// <summary>Tenant counts grouped by lifecycle status.</summary>
public sealed record CspDashboardTenantCounts(
    int Active,
    int Suspended,
    int Disabled,
    int Total);

/// <summary>ATO statuses rolled up across active decisions.</summary>
public sealed record CspDashboardAtoStatusCounts(
    int Authorized,
    int InProcess,
    int Denied);

/// <summary>Open findings rolled up by contract severity.</summary>
public sealed record CspDashboardOpenFindingsBySeverity(
    int Critical,
    int High,
    int Moderate,
    int Low);

/// <summary>Top-level summary projection for <c>GET /api/csp/dashboard/summary</c>.</summary>
public sealed record CspDashboardSummary(
    CspDashboardTenantCounts TenantCounts,
    int DisabledTenantCount,
    int OrganizationCount,
    int SystemCount,
    CspDashboardAtoStatusCounts AtoStatusCounts,
    CspDashboardOpenFindingsBySeverity OpenFindingsBySeverity,
    int OpenPoamCount,
    int OpenDeviationCount,
    DateTimeOffset GeneratedAt);

/// <summary>Per-tenant row in <c>GET /api/csp/dashboard/tenants</c>.</summary>
public sealed record CspDashboardTenantSummary(
    Guid TenantId,
    string DisplayName,
    TenantStatus Status,
    OnboardingState OnboardingState,
    int OrganizationCount,
    int SystemCount,
    CspDashboardAtoStatusCounts AtoStatusCounts,
    int OpenFindingCount,
    int OpenPoamCount,
    int OpenDeviationCount,
    DateTimeOffset? LastActivityTimestamp);

/// <summary>Pagination envelope for tenants.</summary>
public sealed record CspDashboardTenantsPage(
    IReadOnlyList<CspDashboardTenantSummary> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>Authorization-decision row in <c>GET /api/csp/dashboard/atos</c>.</summary>
public sealed record CspDashboardAtoRow(
    string DecisionId,
    Guid TenantId,
    string TenantDisplayName,
    string SystemId,
    string SystemName,
    string DecisionType,
    string DecisionStatus,
    DateTimeOffset DecisionDate,
    DateTimeOffset? ExpirationDate,
    bool IsActive);

/// <summary>Pagination envelope for ATOs.</summary>
public sealed record CspDashboardAtosPage(
    IReadOnlyList<CspDashboardAtoRow> Items,
    int Page,
    int PageSize,
    int TotalCount);
