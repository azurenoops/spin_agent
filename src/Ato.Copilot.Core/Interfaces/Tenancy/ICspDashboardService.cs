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
    /// tenant, joined with <see cref="Tenant"/> for the <c>orgDisplayName</c>
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

    /// <summary>
    /// Returns a paginated list of registered systems across every active
    /// tenant, joined with <see cref="Tenant"/> for the
    /// <c>orgDisplayName</c> projection. Powers the CSP-level
    /// <c>/systems</c> page in the dashboard. Excludes the FR-070 system
    /// tenant and (per FR-098) <see cref="TenantStatus.Disabled"/> tenants.
    /// </summary>
    /// <param name="page">1-based page index. Negative or zero is rejected by the caller.</param>
    /// <param name="pageSize">Page size; capped at 200 by the caller.</param>
    /// <param name="impactLevel">Optional baseline-level filter (Low/Moderate/High).</param>
    /// <param name="rmfPhase">Optional <c>RmfPhase</c> filter.</param>
    /// <param name="sort">Sort field — <c>name|orgDisplayName|impactLevel|rmfPhase|complianceScore|atoExpiration|openPoamCount</c>.</param>
    /// <param name="order">Sort direction — <c>asc</c> or <c>desc</c>.</param>
    Task<CspDashboardSystemsPage> GetSystemsAsync(
        int page,
        int pageSize,
        string? impactLevel,
        string? rmfPhase,
        string sort,
        string order,
        CancellationToken ct = default);

    /// <summary>
    /// Provisions a brand-new mission-owner organization (== <see cref="Tenant"/>
    /// row) under the CSP. Created in <see cref="TenantStatus.Active"/> with
    /// <see cref="OnboardingState.Pending"/> so the CSP-Admin can immediately
    /// impersonate it to walk the per-tenant onboarding wizard. Audit fields
    /// (<c>CreatedBy</c>, <c>UpdatedBy</c>) are stamped with <paramref name="actor"/>.
    /// </summary>
    /// <param name="displayName">
    /// User-supplied org display name. Required, 1–256 chars. Must be unique
    /// (case-insensitive) — duplicates throw <see cref="InvalidOperationException"/>
    /// which the endpoint maps to a 422.
    /// </param>
    /// <param name="legalEntityName">Optional legal entity name.</param>
    /// <param name="primaryPocName">Optional primary POC display name.</param>
    /// <param name="primaryPocEmail">Optional primary POC email.</param>
    /// <param name="actor">Auditable actor (oid / sub from the bearer token).</param>
    Task<Tenant> CreateTenantAsync(
        string displayName,
        string? legalEntityName,
        string? primaryPocName,
        string? primaryPocEmail,
        string actor,
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
    string orgDisplayName,
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

/// <summary>
/// Per-system row in <c>GET /api/csp/dashboard/systems</c>. Carries the
/// minimum compliance KPIs needed for the cross-tenant overview table —
/// the per-tenant deep-detail breakdowns (CatI/II/III, HasBoundary, etc.)
/// remain on the tenant-scoped <c>/api/dashboard/portfolio</c> contract.
/// </summary>
public sealed record CspDashboardSystemRow(
    string SystemId,
    string Name,
    string? Acronym,
    Guid TenantId,
    string orgDisplayName,
    string ImpactLevel,
    string CurrentRmfPhase,
    double ComplianceScore,
    DateTime? AtoExpirationDate,
    string AtoStatus,
    int? AtoDaysRemaining,
    string AtoSeverity,
    int OpenPoamCount,
    int OverduePoamCount);

/// <summary>Pagination envelope for systems.</summary>
public sealed record CspDashboardSystemsPage(
    IReadOnlyList<CspDashboardSystemRow> Items,
    int Page,
    int PageSize,
    int TotalCount);
