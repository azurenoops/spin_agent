using System.Diagnostics;
using System.Security.Claims;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Mcp.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Ato.Copilot.Mcp.Endpoints.Csp;

/// <summary>
/// CSP-Admin cross-tenant dashboard endpoints (Feature 048 US8 / FR-094 /
/// FR-097 / FR-098). Mirrors
/// <c>specs/048-tenant-isolation/contracts/csp-dashboard.openapi.yaml</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>SingleTenant short-circuit</b> — every route returns
///         <c>404 SINGLE_TENANT_MODE</c> when
///         <c>DeploymentOptions.Mode == DeploymentMode.SingleTenant</c>.</item>
///   <item><b>503 CSP_ONBOARDING_INCOMPLETE</b> — already enforced by
///         <c>TenantResolutionMiddleware</c>'s FR-090 gate before this group's
///         handlers execute (the gate runs prior to route matching).</item>
///   <item><b>403 FORBIDDEN_NOT_CSP_ADMIN</b> — every handler short-circuits
///         when <c>ITenantContext.IsCspAdmin == false</c>.</item>
/// </list>
/// </remarks>
public static class CspDashboardEndpoints
{
    private static readonly string[] AllowedSortFields =
    {
        "displayName",
        "status",
        "openFindingCount",
        "lastActivityTimestamp",
    };

    private static readonly string[] AllowedSortOrders = { "asc", "desc" };

    private static readonly string[] AllowedSystemSortFields =
    {
        "name",
        "orgDisplayName",
        "impactLevel",
        "rmfPhase",
        "complianceScore",
        "atoExpiration",
        "openPoamCount",
    };

    private static readonly string[] AllowedDecisionStatuses =
    {
        "Authorized",
        "InProcess",
        "Denied",
    };

    private static readonly string[] AllowedDecisionTypes =
    {
        "ATO",
        "IATO",
        "IATT",
        "ATC",
        "Denial",
    };

    public static IEndpointRouteBuilder MapCspDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/csp/dashboard")
            .WithTags("CSP Dashboard");

        group.MapGet("/summary", GetSummaryAsync).WithName("GetCspDashboardSummary");
        group.MapGet("/tenants", GetTenantsAsync).WithName("GetCspDashboardTenants");
        group.MapPost("/tenants", CreateTenantAsync).WithName("CreateCspDashboardTenant");
        group.MapGet("/atos", GetAtosAsync).WithName("GetCspDashboardAtos");
        group.MapGet("/systems", GetSystemsAsync).WithName("GetCspDashboardSystems");

        return app;
    }

    // ─── handlers ───────────────────────────────────────────────────────────

    private static async Task<IResult> GetSummaryAsync(
        HttpContext http,
        ITenantContext tenantCtx,
        ICspDashboardService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var shortCircuit))
            return shortCircuit;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        var summary = await service.GetSummaryAsync(ct);
        return Success(sw, BuildSummaryDto(summary));
    }

    private static async Task<IResult> GetTenantsAsync(
        HttpContext http,
        ITenantContext tenantCtx,
        ICspDashboardService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct,
        int? page = null,
        int? pageSize = null,
        string? status = null,
        string? sort = null,
        string? order = null)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var shortCircuit))
            return shortCircuit;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        // ─── validation (422 VALIDATION_FAILED) ────────────────────────────
        var (resolvedPage, pageError) = ResolvePage(page);
        if (pageError is not null) return ValidationError(sw, pageError);

        var (resolvedPageSize, pageSizeError) = ResolvePageSize(pageSize);
        if (pageSizeError is not null) return ValidationError(sw, pageSizeError);

        TenantStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TenantStatus>(status, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(typeof(TenantStatus), parsed))
            {
                return ValidationError(sw,
                    $"status '{status}' is not a valid TenantStatus value (Active|Suspended|Disabled).");
            }
            statusFilter = parsed;
        }

        var resolvedSort = string.IsNullOrWhiteSpace(sort) ? "displayName" : sort;
        if (!AllowedSortFields.Contains(resolvedSort, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationError(sw,
                $"sort '{sort}' is not a valid sort field (displayName|status|openFindingCount|lastActivityTimestamp).");
        }

        var resolvedOrder = string.IsNullOrWhiteSpace(order) ? "asc" : order;
        if (!AllowedSortOrders.Contains(resolvedOrder, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationError(sw, $"order '{order}' must be 'asc' or 'desc'.");
        }

        // ─── query + project ───────────────────────────────────────────────
        var result = await service.GetTenantsAsync(
            resolvedPage,
            resolvedPageSize,
            statusFilter,
            resolvedSort,
            resolvedOrder,
            ct);

        return Success(sw, BuildTenantsPageDto(result));
    }

    private static async Task<IResult> GetAtosAsync(
        HttpContext http,
        ITenantContext tenantCtx,
        ICspDashboardService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct,
        int? page = null,
        int? pageSize = null,
        string? decisionStatus = null,
        string? decisionType = null,
        string? since = null,
        string? until = null)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var shortCircuit))
            return shortCircuit;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        // ─── validation ────────────────────────────────────────────────────
        var (resolvedPage, pageError) = ResolvePage(page);
        if (pageError is not null) return ValidationError(sw, pageError);

        var (resolvedPageSize, pageSizeError) = ResolvePageSize(pageSize);
        if (pageSizeError is not null) return ValidationError(sw, pageSizeError);

        if (!string.IsNullOrWhiteSpace(decisionStatus)
            && !AllowedDecisionStatuses.Contains(decisionStatus, StringComparer.Ordinal))
        {
            return ValidationError(sw,
                $"decisionStatus '{decisionStatus}' is not valid (Authorized|InProcess|Denied).");
        }
        if (!string.IsNullOrWhiteSpace(decisionType)
            && !AllowedDecisionTypes.Contains(decisionType, StringComparer.Ordinal))
        {
            return ValidationError(sw,
                $"decisionType '{decisionType}' is not valid (ATO|IATO|IATT|ATC|Denial).");
        }

        DateTimeOffset? sinceTs = null;
        if (!string.IsNullOrWhiteSpace(since))
        {
            if (!DateTimeOffset.TryParse(since, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var parsedSince))
            {
                return ValidationError(sw, $"since '{since}' is not a valid ISO-8601 date-time.");
            }
            sinceTs = parsedSince;
        }
        DateTimeOffset? untilTs = null;
        if (!string.IsNullOrWhiteSpace(until))
        {
            if (!DateTimeOffset.TryParse(until, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var parsedUntil))
            {
                return ValidationError(sw, $"until '{until}' is not a valid ISO-8601 date-time.");
            }
            untilTs = parsedUntil;
        }

        var result = await service.GetAtosAsync(
            resolvedPage,
            resolvedPageSize,
            decisionStatus,
            decisionType,
            sinceTs,
            untilTs,
            ct);

        return Success(sw, BuildAtosPageDto(result));
    }

    private static async Task<IResult> GetSystemsAsync(
        HttpContext http,
        ITenantContext tenantCtx,
        ICspDashboardService service,
        IOptions<DeploymentOptions> deployment,
        CancellationToken ct,
        int? page = null,
        int? pageSize = null,
        string? impactLevel = null,
        string? rmfPhase = null,
        string? sort = null,
        string? order = null)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var shortCircuit))
            return shortCircuit;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        // ─── validation ────────────────────────────────────────────────────
        var (resolvedPage, pageError) = ResolvePage(page);
        if (pageError is not null) return ValidationError(sw, pageError);

        var (resolvedPageSize, pageSizeError) = ResolvePageSize(pageSize);
        if (pageSizeError is not null) return ValidationError(sw, pageSizeError);

        var resolvedSort = string.IsNullOrWhiteSpace(sort) ? "name" : sort;
        if (!AllowedSystemSortFields.Contains(resolvedSort, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationError(sw,
                $"sort '{sort}' is not a valid sort field (name|orgDisplayName|impactLevel|rmfPhase|complianceScore|atoExpiration|openPoamCount).");
        }

        var resolvedOrder = string.IsNullOrWhiteSpace(order) ? "asc" : order;
        if (!AllowedSortOrders.Contains(resolvedOrder, StringComparer.OrdinalIgnoreCase))
        {
            return ValidationError(sw, $"order '{order}' must be 'asc' or 'desc'.");
        }

        var result = await service.GetSystemsAsync(
            resolvedPage,
            resolvedPageSize,
            impactLevel,
            rmfPhase,
            resolvedSort,
            resolvedOrder,
            ct);

        return Success(sw, BuildSystemsPageDto(result));
    }

    /// <summary>
    /// <c>POST /api/csp/dashboard/tenants</c> — provision a brand-new
    /// mission-owner organization (== <see cref="Tenant"/> row). Created in
    /// <see cref="TenantStatus.Active"/> with
    /// <see cref="OnboardingState.Pending"/> so the CSP-Admin can immediately
    /// impersonate it and walk the per-tenant onboarding wizard. Gated on
    /// <see cref="ITenantContext.IsCspAdmin"/>; rejects duplicate display
    /// names with <c>422 VALIDATION_FAILED</c>.
    /// </summary>
    private static async Task<IResult> CreateTenantAsync(
        HttpContext http,
        ITenantContext tenantCtx,
        ICspDashboardService service,
        IOptions<DeploymentOptions> deployment,
        CreateCspTenantRequest? body,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (ShouldShortCircuitSingleTenant(deployment, out var shortCircuit))
            return shortCircuit;
        if (!tenantCtx.IsCspAdmin) return ForbiddenNotCspAdmin(sw);

        if (body is null)
            return ValidationError(sw, "Request body is required.");
        if (string.IsNullOrWhiteSpace(body.DisplayName))
            return ValidationError(sw, "displayName is required.");
        if (body.DisplayName.Trim().Length > 256)
            return ValidationError(sw, "displayName must be 256 characters or fewer.");
        if (!string.IsNullOrWhiteSpace(body.PrimaryPocEmail) && !body.PrimaryPocEmail.Contains('@'))
            return ValidationError(sw, "primaryPocEmail must be a valid email address.");

        var actor = ResolveActor(http);
        try
        {
            var tenant = await service.CreateTenantAsync(
                body.DisplayName,
                body.LegalEntityName,
                body.PrimaryPocName,
                body.PrimaryPocEmail,
                actor,
                ct);
            return Results.Json(new
            {
                status = "success",
                data = new
                {
                    tenantId = tenant.Id,
                    displayName = tenant.DisplayName,
                    status = tenant.Status.ToString(),
                    onboardingState = tenant.OnboardingState.ToString(),
                    createdAt = tenant.CreatedAt,
                    createdBy = tenant.CreatedBy,
                },
                metadata = new
                {
                    executionTimeMs = sw.ElapsedMilliseconds,
                    timestamp = DateTimeOffset.UtcNow,
                },
            }, statusCode: StatusCodes.Status201Created);
        }
        catch (InvalidOperationException ex)
        {
            return ValidationError(sw, ex.Message);
        }
        catch (ArgumentException ex)
        {
            return ValidationError(sw, ex.Message);
        }
    }

    /// <summary>
    /// Request body for <c>POST /api/csp/dashboard/tenants</c>.
    /// </summary>
    public sealed record CreateCspTenantRequest(
        string DisplayName,
        string? LegalEntityName,
        string? PrimaryPocName,
        string? PrimaryPocEmail);

    private static string ResolveActor(HttpContext http)
    {
        var raw = http.User.FindFirstValue("oid")
            ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? http.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(raw) ? "system" : raw;
    }

    // ─── DTO projections (camelCase contract envelope) ─────────────────────

    private static object BuildSummaryDto(CspDashboardSummary s) => new
    {
        tenantCounts = new
        {
            active = s.TenantCounts.Active,
            suspended = s.TenantCounts.Suspended,
            disabled = s.TenantCounts.Disabled,
            total = s.TenantCounts.Total,
        },
        disabledTenantCount = s.DisabledTenantCount,
        organizationCount = s.OrganizationCount,
        systemCount = s.SystemCount,
        atoStatusCounts = new
        {
            authorized = s.AtoStatusCounts.Authorized,
            inProcess = s.AtoStatusCounts.InProcess,
            denied = s.AtoStatusCounts.Denied,
        },
        openFindingsBySeverity = new
        {
            critical = s.OpenFindingsBySeverity.Critical,
            high = s.OpenFindingsBySeverity.High,
            moderate = s.OpenFindingsBySeverity.Moderate,
            low = s.OpenFindingsBySeverity.Low,
        },
        openPoamCount = s.OpenPoamCount,
        openDeviationCount = s.OpenDeviationCount,
        generatedAt = s.GeneratedAt,
    };

    private static object BuildTenantsPageDto(CspDashboardTenantsPage page) => new
    {
        items = page.Items.Select(t => new
        {
            tenantId = t.TenantId,
            displayName = t.DisplayName,
            status = t.Status.ToString(),
            onboardingState = t.OnboardingState.ToString(),
            organizationCount = t.OrganizationCount,
            systemCount = t.SystemCount,
            atoStatusCounts = new
            {
                authorized = t.AtoStatusCounts.Authorized,
                inProcess = t.AtoStatusCounts.InProcess,
                denied = t.AtoStatusCounts.Denied,
            },
            openFindingCount = t.OpenFindingCount,
            openPoamCount = t.OpenPoamCount,
            openDeviationCount = t.OpenDeviationCount,
            lastActivityTimestamp = t.LastActivityTimestamp,
        }).ToArray(),
        page = page.Page,
        pageSize = page.PageSize,
        totalCount = page.TotalCount,
    };

    private static object BuildAtosPageDto(CspDashboardAtosPage page) => new
    {
        items = page.Items.Select(a => new
        {
            decisionId = a.DecisionId,
            tenantId = a.TenantId,
            orgDisplayName = a.orgDisplayName,
            systemId = a.SystemId,
            systemName = a.SystemName,
            decisionStatus = a.DecisionStatus,
            decisionType = a.DecisionType,
            decisionDate = a.DecisionDate,
            expirationDate = a.ExpirationDate,
            isActive = a.IsActive,
        }).ToArray(),
        page = page.Page,
        pageSize = page.PageSize,
        totalCount = page.TotalCount,
    };

    private static object BuildSystemsPageDto(CspDashboardSystemsPage page) => new
    {
        items = page.Items.Select(s => new
        {
            systemId = s.SystemId,
            name = s.Name,
            acronym = s.Acronym,
            tenantId = s.TenantId,
            orgDisplayName = s.orgDisplayName,
            impactLevel = s.ImpactLevel,
            currentRmfPhase = s.CurrentRmfPhase,
            complianceScore = s.ComplianceScore,
            atoExpirationDate = s.AtoExpirationDate,
            atoStatus = s.AtoStatus,
            atoDaysRemaining = s.AtoDaysRemaining,
            atoSeverity = s.AtoSeverity,
            openPoamCount = s.OpenPoamCount,
            overduePoamCount = s.OverduePoamCount,
        }).ToArray(),
        page = page.Page,
        pageSize = page.PageSize,
        totalCount = page.TotalCount,
    };

    // ─── helpers ───────────────────────────────────────────────────────────

    private static bool ShouldShortCircuitSingleTenant(
        IOptions<DeploymentOptions> deployment, out IResult result)
    {
        if (deployment.Value.Mode == DeploymentMode.SingleTenant)
        {
            result = Error(StatusCodes.Status404NotFound, "SINGLE_TENANT_MODE",
                "CSP dashboard is unavailable in SingleTenant deployments.");
            return true;
        }
        result = null!;
        return false;
    }

    private static (int Resolved, string? Error) ResolvePage(int? page)
    {
        if (page is null) return (1, null);
        if (page.Value < 1) return (0, "page must be >= 1.");
        return (page.Value, null);
    }

    private static (int Resolved, string? Error) ResolvePageSize(int? pageSize)
    {
        if (pageSize is null) return (50, null);
        if (pageSize.Value < 1) return (0, "pageSize must be >= 1.");
        if (pageSize.Value > 200) return (0, "pageSize must be <= 200.");
        return (pageSize.Value, null);
    }

    private static IResult Success(Stopwatch sw, object data) =>
        Results.Json(new
        {
            status = "success",
            data,
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
        }, statusCode: StatusCodes.Status200OK);

    private static IResult ForbiddenNotCspAdmin(Stopwatch sw) =>
        Results.Json(new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error = new
            {
                errorCode = "FORBIDDEN_NOT_CSP_ADMIN",
                message = "Operation requires CSP.Admin role.",
            },
        }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult ValidationError(Stopwatch sw, string message) =>
        Results.Json(new
        {
            status = "error",
            metadata = new
            {
                executionTimeMs = sw.ElapsedMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
            },
            error = new { errorCode = "VALIDATION_FAILED", message },
        }, statusCode: StatusCodes.Status422UnprocessableEntity);

    private static IResult Error(int statusCode, string code, string message) =>
        Results.Json(new
        {
            status = "error",
            error = new { errorCode = code, message },
        }, statusCode: statusCode);
}
