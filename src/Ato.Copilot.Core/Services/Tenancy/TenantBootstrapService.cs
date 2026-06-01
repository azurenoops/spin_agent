using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// Ensures system-level seed rows (e.g., the system tenant
/// <c>00000000-0000-0000-0000-000000000000</c>) exist at startup.
/// Idempotent — safe to call repeatedly.
/// See feature 048 spec FR-070 and data-model.md §1.3.
/// </summary>
public static class TenantBootstrapService
{
    /// <summary>
    /// The id of the special "system tenant" that owns all
    /// <see cref="Models.Tenancy.Attributes.GlobalReferenceAttribute"/>
    /// rows. <c>00000000-0000-0000-0000-000000000000</c>.
    /// </summary>
    public static readonly Guid SystemTenantId = Guid.Empty;

    /// <summary>
    /// Display name of the system tenant.
    /// </summary>
    public const string SystemOrgDisplayName = "Ato.Copilot.System";

    /// <summary>
    /// Conventional default tenant id for SingleTenant deployments.
    /// <c>00000000-0000-0000-0000-000000000001</c>. Used when
    /// <c>Deployment:DefaultTenantId</c> is not configured.
    /// See feature 048 spec FR-070 / FR-072 and the dev-mode simulated
    /// CAC identity in <c>appsettings.Development.json</c>.
    /// </summary>
    public static readonly Guid DefaultTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Display name of the conventional default tenant. Human-readable —
    /// this is what CSP-Admins see in the organization picker. The system
    /// tenant (<see cref="SystemTenantId"/>) keeps its spec-pinned literal
    /// name (<see cref="SystemOrgDisplayName"/>) because it is filtered out
    /// of the picker entirely.
    /// </summary>
    public const string DefaultOrgDisplayName = "Ato.Copilot.Default";

    /// <summary>
    /// Creates the system tenant row if it does not yet exist. Safe to call
    /// repeatedly. Returns true if a row was created on this call.
    /// </summary>
    public static async Task<bool> EnsureSystemTenantAsync(
        AtoCopilotContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var exists = await db.Tenants
                .AsNoTracking()
                .AnyAsync(t => t.Id == SystemTenantId, cancellationToken);

            if (exists)
            {
                return false;
            }

            db.Tenants.Add(new Tenant
            {
                Id = SystemTenantId,
                DisplayName = SystemOrgDisplayName,
                Status = TenantStatus.Active,
                OnboardingState = OnboardingState.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = "system",
                TimeZone = "UTC",
                DefaultClassificationLevel = ClassificationLevel.Unclassified,
            });

            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Bootstrapped system tenant {SystemTenantId} ({DisplayName})",
                SystemTenantId, SystemOrgDisplayName);
            return true;
        }
        catch (Exception ex)
        {
            // Non-fatal at bootstrap; log so operators can investigate.
            logger.LogWarning(ex, "Failed to bootstrap system tenant — non-fatal at first run");
            return false;
        }
    }

    /// <summary>
    /// T060: Backfills <c>OrganizationContext.TenantId</c> rows that were
    /// originally written holding the Entra <c>tid</c> rather than a row from
    /// <c>Tenants</c>. For each <c>OrganizationContext</c> whose
    /// <c>TenantId</c> does not match any <c>Tenants.Id</c> but does match a
    /// <c>Tenants.EntraTenantId</c>, the column is updated in place.
    /// Idempotent — when no candidate rows remain the operation is a no-op.
    /// See feature 048 FR-005.
    /// </summary>
    public static async Task<int> BackfillOrganizationContextTenantIdsAsync(
        AtoCopilotContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            // Materialize known Tenants.Id set + EntraTenantId mapping in one round-trip.
            var tenants = await db.Tenants
                .AsNoTracking()
                .Select(t => new { t.Id, t.EntraTenantId })
                .ToListAsync(cancellationToken);

            if (tenants.Count == 0)
            {
                return 0;
            }

            var tenantIds = tenants.Select(t => t.Id).ToHashSet();
            var entraToInternal = tenants
                .Where(t => t.EntraTenantId.HasValue)
                .GroupBy(t => t.EntraTenantId!.Value)
                .ToDictionary(g => g.Key, g => g.First().Id);

            // Pull only candidate rows: OrganizationContexts whose TenantId is
            // not already a known Tenants.Id (i.e., needs migration).
            var candidates = await db.OrganizationContexts
                .Where(oc => !tenantIds.Contains(oc.TenantId))
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0)
            {
                return 0;
            }

            var updated = 0;
            foreach (var oc in candidates)
            {
                if (entraToInternal.TryGetValue(oc.TenantId, out var internalId))
                {
                    oc.TenantId = internalId;
                    oc.UpdatedAt = DateTimeOffset.UtcNow;
                    updated++;
                }
            }

            if (updated > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "Backfilled OrganizationContext.TenantId for {Count} row(s) (Entra tid → Tenants.Id)",
                    updated);
            }

            return updated;
        }
        catch (Exception ex)
        {
            // Non-fatal — log and continue. The next startup will retry.
            logger.LogWarning(ex,
                "BackfillOrganizationContextTenantIdsAsync failed — non-fatal, will retry next start");
            return 0;
        }
    }

    /// <summary>
    /// Phase 5 / T081–T083 (FR-070, FR-071): in <c>SingleTenant</c> mode this
    /// ensures a default tenant row exists and backfills every retrofitted
    /// <c>[TenantScoped]</c> table's NULL <c>TenantId</c> values to that default
    /// tenant. In <c>MultiTenant</c> mode the method scans the same tables and
    /// fails fast (throws <see cref="InvalidOperationException"/>) if any
    /// <c>TenantId</c> is still NULL — operators must run the migration tool
    /// (T078+) before bringing the host up.
    /// </summary>
    /// <param name="db">Application <see cref="AtoCopilotContext"/>.</param>
    /// <param name="isSingleTenantMode">
    /// <c>true</c> when <c>Deployment:Mode</c> is <c>SingleTenant</c>;
    /// <c>false</c> when it is <c>MultiTenant</c>. Bound from
    /// <c>ATO_DEPLOYMENT__MODE</c>.
    /// </param>
    /// <param name="defaultTenantIdOverride">
    /// Optional override of the default tenant id (binds from
    /// <c>Deployment:DefaultTenantId</c>). When <c>null</c>,
    /// <see cref="DefaultTenantId"/> is used.
    /// </param>
    /// <param name="logger">Application logger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="DefaultTenantBootstrapReport"/> describing what changed
    /// during the call.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when running in MultiTenant mode and any retrofitted table
    /// still has rows with <c>TenantId IS NULL</c>.
    /// </exception>
    public static async Task<DefaultTenantBootstrapReport> EnsureDefaultTenantAndBackfillAsync(
        AtoCopilotContext db,
        bool isSingleTenantMode,
        Guid? defaultTenantIdOverride,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        var defaultId = defaultTenantIdOverride ?? DefaultTenantId;

        var providerName = db.Database.ProviderName ?? string.Empty;
        var isSqlServer = providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
        var isSqlite = providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
        if (!isSqlServer && !isSqlite)
        {
            logger.LogWarning(
                "EnsureDefaultTenantAndBackfillAsync: skipping (unsupported provider {Provider})",
                providerName);
            return new DefaultTenantBootstrapReport(
                Created: false,
                DefaultTenantId: defaultId,
                RowsBackfilled: 0,
                TablesTouched: Array.Empty<string>(),
                NullTenantIdRowsFound: 0);
        }

        // ─── Discover [TenantScoped] tables via the EF model ────────────
        // Only tables whose EF model maps a conventional `TenantId` property
        // participate in the NULL-count guard / backfill. Entities that scope
        // via a domain-specific column instead (e.g. Feature 051
        // LoginAuditEvent → EffectiveTenantId) have NO mapped `TenantId`; the
        // stamping interceptor and the query-filter installer already skip
        // them by the same `FindProperty("TenantId")` check, and so does
        // MultiTenantMigrationService.ResolveTenantScopedTables. Without this
        // clause the guard counts an orphan/legacy `TenantId` column on such a
        // table and aborts MultiTenant boot on rows it can never legitimately
        // stamp (pre-session / failed-login audit rows).
        var tenantScopedTables = db.Model.GetEntityTypes()
            .Where(et => et.ClrType.GetCustomAttribute<TenantScopedAttribute>(inherit: false) is not null
                         && !et.IsOwned()
                         && et.GetTableName() is not null
                         && et.FindProperty("TenantId") is not null)
            .Select(et => et.GetTableName()!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ─── MultiTenant mode: scan and fail-fast on any NULL rows ──────
        if (!isSingleTenantMode)
        {
            var nullCount = await CountNullTenantIdRowsAsync(
                db, tenantScopedTables, isSqlServer, cancellationToken);

            if (nullCount > 0)
            {
                logger.LogError(
                    "MultiTenant boot blocked: {NullCount} row(s) still have NULL TenantId across {TableCount} retrofitted table(s). " +
                    "Run the multi-tenant migration tool (ato-cli migrate-to-multi-tenant) to assign tenants before starting in MultiTenant mode.",
                    nullCount, tenantScopedTables.Count);
                throw new InvalidOperationException(
                    $"MultiTenant boot aborted: {nullCount} row(s) across {tenantScopedTables.Count} table(s) still have NULL TenantId. " +
                    "Run the migration tool first.");
            }

            return new DefaultTenantBootstrapReport(
                Created: false,
                DefaultTenantId: defaultId,
                RowsBackfilled: 0,
                TablesTouched: Array.Empty<string>(),
                NullTenantIdRowsFound: 0);
        }

        // ─── SingleTenant mode: ensure default tenant + backfill ────────
        var created = await EnsureDefaultTenantRowAsync(db, defaultId, logger, cancellationToken);

        var (rowsBackfilled, tablesTouched) = await BackfillNullTenantIdRowsAsync(
            db, tenantScopedTables, defaultId, isSqlServer, cancellationToken);

        if (rowsBackfilled > 0)
        {
            // Single FR-070 acceptance-scenario log line.
            logger.LogInformation(
                "Migrated {Count} rows to default tenant {DefaultTenantId}",
                rowsBackfilled, defaultId);
        }

        return new DefaultTenantBootstrapReport(
            Created: created,
            DefaultTenantId: defaultId,
            RowsBackfilled: rowsBackfilled,
            TablesTouched: tablesTouched,
            NullTenantIdRowsFound: 0);
    }

    /// <summary>
    /// Legacy display names previously used for the default tenant row.
    /// When the row exists but still carries one of these literals, the
    /// bootstrap will rename it in place to <see cref="DefaultOrgDisplayName"/>
    /// so existing deployments pick up the rebrand without manual SQL. A row
    /// that has been customized (any other name) is left untouched.
    /// </summary>
    private static readonly HashSet<string> LegacyDefaultDisplayNames = new(StringComparer.Ordinal)
    {
        "Default Tenant",
    };

    private static async Task<bool> EnsureDefaultTenantRowAsync(
        AtoCopilotContext db,
        Guid defaultId,
        ILogger logger,
        CancellationToken ct)
    {
        var existing = await db.Tenants
            .FirstOrDefaultAsync(t => t.Id == defaultId, ct);
        if (existing is not null)
        {
            var changed = false;

            // Idempotent rename of legacy seed rows so existing deployments
            // pick up the rebrand without manual SQL or a forced reseed.
            if (LegacyDefaultDisplayNames.Contains(existing.DisplayName)
                && existing.DisplayName != DefaultOrgDisplayName)
            {
                var previous = existing.DisplayName;
                existing.DisplayName = DefaultOrgDisplayName;
                changed = true;
                logger.LogInformation(
                    "Renamed default tenant {DefaultTenantId} display name '{Previous}' -> '{Current}'",
                    defaultId, previous, DefaultOrgDisplayName);
            }

            // Idempotent backfill: deployments bootstrapped before this stamp
            // was introduced have EntraTenantId = NULL on the default-tenant
            // row, which makes the TenantResolutionMiddleware lookup
            // `Tenants WHERE EntraTenantId = tid` miss for CAC simulation and
            // any caller whose `tid` matches the SingleTenant sentinel. Stamp
            // it on the next startup so the request pipeline self-heals.
            if (existing.EntraTenantId is null)
            {
                existing.EntraTenantId = defaultId;
                changed = true;
                logger.LogInformation(
                    "Backfilled EntraTenantId on default tenant {DefaultTenantId} for SingleTenant deployment",
                    defaultId);
            }

            if (changed)
            {
                existing.UpdatedAt = DateTimeOffset.UtcNow;
                existing.UpdatedBy = "system";
                await db.SaveChangesAsync(ct);
            }
            return false;
        }

        db.Tenants.Add(new Tenant
        {
            Id = defaultId,
            // Stamp EntraTenantId = Id so that the TenantResolutionMiddleware
            // can resolve the SingleTenant default row via the well-known
            // sentinel `tid` claim (CAC simulation, dev tooling, and any
            // caller whose tid matches the default). Without this, the
            // resolution lookup `Tenants WHERE EntraTenantId = tid` misses
            // and the request 401s with TENANT_NOT_PROVISIONED.
            EntraTenantId = defaultId,
            DisplayName = DefaultOrgDisplayName,
            Status = TenantStatus.Active,
            OnboardingState = OnboardingState.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = "system",
            TimeZone = "UTC",
            DefaultClassificationLevel = ClassificationLevel.Unclassified,
        });
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Bootstrapped default tenant {DefaultTenantId} ({DisplayName}) for SingleTenant deployment",
            defaultId, DefaultOrgDisplayName);
        return true;
    }

    private static async Task<long> CountNullTenantIdRowsAsync(
        AtoCopilotContext db,
        IReadOnlyList<string> tables,
        bool isSqlServer,
        CancellationToken ct)
    {
        long total = 0;
        foreach (var table in tables)
        {
            try
            {
                var sql = isSqlServer
                    ? $"SELECT COUNT_BIG(*) FROM [{table}] WHERE [TenantId] IS NULL"
                    : $"SELECT COUNT(*) FROM \"{table}\" WHERE \"TenantId\" IS NULL";
                total += await ExecuteScalarLongAsync(db, sql, ct);
            }
            catch
            {
                // Table may not have a TenantId column on this DB yet (test
                // EnsureCreated may have used the model schema, in which case
                // the column is non-NULL by EF default). Treat as 0 NULLs.
            }
        }
        return total;
    }

    private static async Task<(int RowsBackfilled, IReadOnlyList<string> TablesTouched)>
        BackfillNullTenantIdRowsAsync(
            AtoCopilotContext db,
            IReadOnlyList<string> tables,
            Guid defaultId,
            bool isSqlServer,
            CancellationToken ct)
    {
        var touched = new List<string>();
        var total = 0;

        foreach (var table in tables)
        {
            try
            {
                var sql = isSqlServer
                    ? $"UPDATE [{table}] SET [TenantId] = '{defaultId}' WHERE [TenantId] IS NULL"
                    : $"UPDATE \"{table}\" SET \"TenantId\" = '{defaultId}' WHERE \"TenantId\" IS NULL";
                var affected = await db.Database.ExecuteSqlRawAsync(sql, ct);
                if (affected > 0)
                {
                    total += affected;
                    touched.Add(table);
                }
            }
            catch
            {
                // Best-effort — table or column may not exist yet on this DB.
            }
        }

        return (total, touched);
    }

    private static async Task<long> ExecuteScalarLongAsync(
        AtoCopilotContext db,
        string sql,
        CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result == DBNull.Value) return 0;
        return Convert.ToInt64(result);
    }
}

/// <summary>
/// Result of a <see cref="TenantBootstrapService.EnsureDefaultTenantAndBackfillAsync"/>
/// invocation. Used by tests and by startup logging.
/// </summary>
/// <param name="Created">
/// <c>true</c> when this call inserted a new default-tenant row;
/// <c>false</c> when one already existed.
/// </param>
/// <param name="DefaultTenantId">
/// The resolved default tenant id (override or
/// <see cref="TenantBootstrapService.DefaultTenantId"/>).
/// </param>
/// <param name="RowsBackfilled">Total rows whose <c>TenantId</c> changed from NULL.</param>
/// <param name="TablesTouched">Tables that had at least one row backfilled.</param>
/// <param name="NullTenantIdRowsFound">
/// In MultiTenant mode, the count of rows with NULL <c>TenantId</c> that
/// caused the call to abort. <c>0</c> in SingleTenant mode.
/// </param>
public sealed record DefaultTenantBootstrapReport(
    bool Created,
    Guid DefaultTenantId,
    int RowsBackfilled,
    IReadOnlyList<string> TablesTouched,
    long NullTenantIdRowsFound);
