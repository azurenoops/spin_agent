using System.Security.Claims;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Interfaces.Auth;
using Ato.Copilot.Core.Models.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Auth;

/// <summary>
/// Feature 051 (FR-032 / FR-033 / FR-034 / FR-036a) — append-only writer
/// and paginated reader for <see cref="LoginAuditEvent"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AppendAsync"/> deliberately does NOT call
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>. Per
/// <c>contracts/internal-services.md § 1.3</c> and the R6 / Feature-050
/// SRP parity, the caller owns the enclosing transaction so the audit
/// row and any neighbouring state change commit atomically. Failing to
/// call <c>SaveChangesAsync</c> here is the contract — not a bug.
/// </para>
/// <para>
/// <see cref="ListAsync"/> opens its own short-lived DbContext via the
/// injected <see cref="IDbContextFactory{TContext}"/> and relies on the
/// inline <c>HasQueryFilter</c> on <see cref="LoginAuditEvent"/> (see
/// <c>AtoCopilotContext.OnModelCreating</c>) for tenant scoping — it
/// never calls <c>.IgnoreQueryFilters()</c>. The caller-supplied
/// <c>tenantId</c> parameter is applied as an additional <c>Where</c>
/// for documentation; cross-tenant reads are prevented by the filter.
/// </para>
/// <para>
/// <see cref="ListSystemTenantAsync"/> enforces the
/// <c>Auth.SocAnalyst</c> role claim via the injected
/// <see cref="IHttpContextAccessor"/> BEFORE issuing the query, then
/// calls <c>.IgnoreQueryFilters()</c> scoped to
/// <c>EffectiveTenantId == Guid.Empty</c> ONLY. This is the ONLY
/// service-layer path that bypasses the tenant filter (the daily
/// cold-archive job does so too, but that lives in
/// <c>LoginAuditArchiveService</c>).
/// </para>
/// </remarks>
public sealed class LoginAuditService : ILoginAuditService
{
    private readonly ILogger<LoginAuditService> _logger;
    private readonly IDbContextFactory<AtoCopilotContext>? _contextFactory;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    /// <summary>
    /// Production constructor — all three dependencies populated.
    /// </summary>
    public LoginAuditService(
        ILogger<LoginAuditService> logger,
        IDbContextFactory<AtoCopilotContext> contextFactory,
        IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    /// <summary>
    /// Legacy constructor preserved for callers that only need
    /// <see cref="AppendAsync"/> (e.g. write-side unit tests) and do not
    /// want to construct a <see cref="IDbContextFactory{TContext}"/> or
    /// <see cref="IHttpContextAccessor"/>. The read paths
    /// (<see cref="ListAsync"/> / <see cref="ListSystemTenantAsync"/>)
    /// throw when invoked under this constructor.
    /// </summary>
    public LoginAuditService(ILogger<LoginAuditService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _contextFactory = null;
        _httpContextAccessor = null;
    }

    /// <inheritdoc />
    public async Task<LoginAuditEvent> AppendAsync(
        AtoCopilotContext db,
        LoginAuditEventDraft draft,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(draft);

        // Validation mirrors data-model.md § 1.6 — fail fast on the
        // properties that would otherwise overflow the column caps.
        if (draft.Oid is { Length: > 254 })
        {
            throw new ArgumentException(
                "Oid exceeds 254 characters.", nameof(draft));
        }
        if (draft.Tid is { Length: > 254 })
        {
            throw new ArgumentException(
                "Tid exceeds 254 characters.", nameof(draft));
        }
        if (draft.CorrelationId.Length > 64)
        {
            throw new ArgumentException(
                "CorrelationId exceeds 64 characters.", nameof(draft));
        }
        if (draft.SourceIp.Length > 45)
        {
            throw new ArgumentException(
                "SourceIp exceeds 45 characters.", nameof(draft));
        }
        if (draft.UserAgent.Length > 512)
        {
            throw new ArgumentException(
                "UserAgent exceeds 512 characters.", nameof(draft));
        }
        if (draft.MetadataJson is { Length: > 2000 })
        {
            throw new ArgumentException(
                "MetadataJson exceeds 2000 characters.", nameof(draft));
        }

        var entity = new LoginAuditEvent
        {
            Id = Guid.NewGuid(),
            EventType = draft.EventType,
            Oid = draft.Oid,
            Tid = draft.Tid,
            EffectiveTenantId = draft.EffectiveTenantId,
            CorrelationId = draft.CorrelationId,
            SourceIp = draft.SourceIp,
            UserAgent = draft.UserAgent,
            Surface = draft.Surface,
            OccurredAt = DateTimeOffset.UtcNow,
            ErrorClass = draft.ErrorClass,
            MetadataJson = draft.MetadataJson,
        };

        await db.LoginAuditEvents.AddAsync(entity, ct).ConfigureAwait(false);
        // Intentionally NO SaveChangesAsync — caller owns the transaction
        // so the audit row commits atomically with neighbouring state
        // changes. Same shape as Feature 050's CapabilityHistoryService.

        _logger.LogDebug(
            "LoginAuditService.AppendAsync queued {EventType} (Surface={Surface}, Tenant={TenantId})",
            entity.EventType, entity.Surface, entity.EffectiveTenantId);

        return entity;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoginAuditEvent>> ListAsync(
        Guid tenantId,
        DateTimeOffset? since = null,
        int take = 100,
        CancellationToken ct = default)
    {
        if (_contextFactory is null)
        {
            throw new InvalidOperationException(
                "ListAsync requires the production constructor with IDbContextFactory<AtoCopilotContext>.");
        }

        var effectiveTake = NormalizeTake(take);
        var effectiveSince = since ?? DateTimeOffset.MinValue;

        await using var db = await _contextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // The HasQueryFilter on LoginAuditEvent automatically scopes results
        // to the active tenant (TenantFilterEffectiveId). The tenantId
        // parameter is applied as an additional Where for clarity; the
        // security boundary is the filter, not the parameter.
        //
        // SQLite (dev/test) cannot translate DateTimeOffset comparisons to
        // its TEXT storage form, so the `since` predicate is applied
        // client-side after the narrow tenant-scoped query — mirroring
        // the pattern used by AuthEndpoints' PIM-roles read.
        var serverFiltered = await db.LoginAuditEvents
            .AsNoTracking()
            .Where(e => e.EffectiveTenantId == tenantId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return serverFiltered
            .Where(e => e.OccurredAt >= effectiveSince)
            .OrderByDescending(e => e.OccurredAt)
            .Take(effectiveTake)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoginAuditEvent>> ListSystemTenantAsync(
        DateTimeOffset? since = null,
        int take = 100,
        CancellationToken ct = default)
    {
        if (_contextFactory is null)
        {
            throw new InvalidOperationException(
                "ListSystemTenantAsync requires the production constructor with IDbContextFactory<AtoCopilotContext>.");
        }

        EnsureSocAnalystClaim();

        var effectiveTake = NormalizeTake(take);
        var effectiveSince = since ?? DateTimeOffset.MinValue;

        await using var db = await _contextFactory
            .CreateDbContextAsync(ct)
            .ConfigureAwait(false);

        // Bypass the tenant filter ONLY to surface SYSTEM_TENANT_ID rows
        // (pre-session failures, NoTenantAssignment, etc.) — the predicate
        // scope is strictly EffectiveTenantId == Guid.Empty.
        // DateTimeOffset comparison is applied client-side for parity with
        // ListAsync (SQLite dev/test cannot translate >= on DateTimeOffset).
        var serverFiltered = await db.LoginAuditEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(e => e.EffectiveTenantId == Guid.Empty)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return serverFiltered
            .Where(e => e.OccurredAt >= effectiveSince)
            .OrderByDescending(e => e.OccurredAt)
            .Take(effectiveTake)
            .ToList();
    }

    private void EnsureSocAnalystClaim()
    {
        // Defensive — production wires IHttpContextAccessor, but a unit
        // test that constructs the service with the production ctor and
        // a null-pretending HttpContext is treated as "no claim".
        var user = _httpContextAccessor?.HttpContext?.User;
        var hasClaim = user?.Identity?.IsAuthenticated == true
                       && user.IsInRole("Auth.SocAnalyst");
        if (!hasClaim)
        {
            throw new UnauthorizedAccessException(
                "Auth.SocAnalyst claim required to read SYSTEM_TENANT_ID audit rows.");
        }
    }

    private static int NormalizeTake(int take)
    {
        const int defaultTake = 100;
        const int maxTake = 1000;
        if (take <= 0)
        {
            return defaultTake;
        }
        return take > maxTake ? maxTake : take;
    }
}
