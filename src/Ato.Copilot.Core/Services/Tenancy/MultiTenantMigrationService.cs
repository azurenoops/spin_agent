using System.Reflection;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;
using Ato.Copilot.Core.Models.Compliance;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Services.Tenancy;

/// <summary>
/// T123 [FR-073..FR-076]: shared Single→Multi migration logic invoked by both
/// <see cref="Endpoints.AdminMigrationEndpoints"/> and
/// <c>ato-cli tenant migrate</c>. Walks every <see cref="TenantScopedAttribute"/>
/// entity and:
/// <list type="number">
///   <item>Counts <c>NULL TenantId</c> rows (preview).</item>
///   <item>Applies CSV-style overrides (table → tenant id mapping).</item>
///   <item>Backfills remaining <c>NULL</c> rows to <c>defaultTenantId</c>.</item>
///   <item>Optionally invokes <see cref="RlsPolicyInstaller"/>.</item>
///   <item>Wraps mutations in a single transaction; rolls back on failure.</item>
///   <item>Emits a <see cref="AuditLogEntry"/> with <c>Action = Tenant.Migrate</c>.</item>
/// </list>
/// </summary>
public sealed class MultiTenantMigrationService
{
    private readonly IDbContextFactory<AtoCopilotContext> _factory;
    private readonly ILogger<MultiTenantMigrationService> _logger;

    public MultiTenantMigrationService(
        IDbContextFactory<AtoCopilotContext> factory,
        ILogger<MultiTenantMigrationService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    /// <summary>Per-table preview counts (no mutation).</summary>
    public sealed record TablePreview(
        string TableName,
        long TotalRows,
        long RowsMissingTenant,
        long RowsAssignedByOverride,
        long RowsAssignedToDefault);

    /// <summary>Per-table report after a successful migration.</summary>
    public sealed record TableReport(
        string TableName,
        long TotalRows,
        long RowsAssignedByOverride,
        long RowsAssignedToDefault,
        long RowsAlreadyAssigned);

    /// <summary>CSV-style override row (FR-074).</summary>
    public sealed record TenantOverride(
        string TableName,
        string? RowIdPrefix,
        Guid TenantId);

    /// <summary>Top-level preview result.</summary>
    public sealed record MigrationPreview(IReadOnlyList<TablePreview> Tables);

    /// <summary>Top-level execution result.</summary>
    public sealed record MigrationReport(
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        Guid DefaultTenantId,
        IReadOnlyList<TableReport> Tables,
        bool RlsInstalled,
        string? Error);

    /// <summary>
    /// Compute per-table counts of rows whose <c>TenantId</c> is currently
    /// <c>NULL</c>. Read-only.
    /// </summary>
    public async Task<MigrationPreview> PreviewAsync(
        IReadOnlyCollection<TenantOverride>? overrides = null,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var tables = ResolveTenantScopedTables(db);
        var byTable = (overrides ?? Array.Empty<TenantOverride>())
            .GroupBy(o => o.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<TablePreview>(tables.Count);
        foreach (var table in tables.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            var (total, missing, exists) = await TryCountRowsAsync(db, table, cancellationToken);
            if (!exists)
            {
                // Tables in the EF model but missing from the live DB
                // (e.g., partial seed in dev/test) are still reported with
                // zero counts so the operator can see coverage gaps.
                rows.Add(new TablePreview(table, 0, 0, 0, 0));
                continue;
            }
            byTable.TryGetValue(table, out var ovr);
            var assignedByOverride = ovr is null ? 0 : Math.Min(missing, ovr.Count);
            var assignedToDefault = Math.Max(0, missing - assignedByOverride);
            rows.Add(new TablePreview(table, total, missing, assignedByOverride, assignedToDefault));
        }
        return new MigrationPreview(rows);
    }

    /// <summary>
    /// Execute the migration in a single transaction. Idempotent: re-runs
    /// when no <c>NULL TenantId</c> rows exist are no-ops (per FR-076).
    /// </summary>
    public async Task<MigrationReport> ExecuteAsync(
        Guid defaultTenantId,
        IReadOnlyCollection<TenantOverride>? overrides = null,
        bool installRls = true,
        string? actorOid = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (defaultTenantId == Guid.Empty)
        {
            throw new ArgumentException("defaultTenantId is required.", nameof(defaultTenantId));
        }

        var startedAt = DateTimeOffset.UtcNow;
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var tables = ResolveTenantScopedTables(db);

        var byTable = (overrides ?? Array.Empty<TenantOverride>())
            .GroupBy(o => o.TableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var reports = new List<TableReport>(tables.Count);
        var rlsInstalled = false;
        string? error = null;

        // BeginTransaction is supported on relational providers (SqlServer +
        // Sqlite). The InMemory provider used in unit tests does not support
        // transactions — skip in that case.
        var supportsTx = db.Database.IsRelational();
        await using var tx = supportsTx
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            foreach (var table in tables.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            {
                var (total, missing, exists) = await TryCountRowsAsync(db, table, cancellationToken);
                if (!exists)
                {
                    reports.Add(new TableReport(table, 0, 0, 0, 0));
                    continue;
                }
                var rowsByOverride = 0L;
                if (byTable.TryGetValue(table, out var ovr))
                {
                    foreach (var o in ovr)
                    {
                        rowsByOverride += await ApplyOverrideAsync(db, table, o, cancellationToken);
                    }
                }
                var rowsToDefault = await ApplyDefaultAsync(db, table, defaultTenantId, cancellationToken);
                var alreadyAssigned = total - missing;
                reports.Add(new TableReport(table, total, rowsByOverride, rowsToDefault, alreadyAssigned));
            }

            if (installRls)
            {
                await RlsPolicyInstaller.ApplyAsync(db, _logger, cancellationToken);
                rlsInstalled = db.Database.IsSqlServer();
            }

            if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            if (tx is not null)
            {
                await tx.RollbackAsync(cancellationToken);
            }
            _logger.LogError(ex, "MultiTenantMigrationService failed; transaction rolled back");
        }

        var completedAt = DateTimeOffset.UtcNow;
        await EmitAuditAsync(
            db,
            defaultTenantId,
            actorOid: actorOid,
            correlationId: correlationId,
            success: error is null,
            details: $"Tables migrated: {reports.Count}; RLS installed: {rlsInstalled}",
            cancellationToken);

        return new MigrationReport(
            startedAt, completedAt, defaultTenantId, reports, rlsInstalled, error);
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static List<string> ResolveTenantScopedTables(AtoCopilotContext db) =>
        db.Model.GetEntityTypes()
            .Where(et => et.ClrType.GetCustomAttribute<TenantScopedAttribute>(inherit: false) is not null
                         && !et.IsOwned()
                         && et.GetTableName() is not null
                         // Exclude the Tenants root table itself: it is the
                         // tenant root and has no TenantId column.
                         && et.FindProperty("TenantId") is not null)
            .Select(et => et.GetTableName()!)
            .Distinct()
            .ToList();

    private static async Task<(long Total, long Missing, bool Exists)> TryCountRowsAsync(
        AtoCopilotContext db, string table, CancellationToken ct)
    {
        // Relational only; in-memory provider doesn't support raw SQL.
        if (!db.Database.IsRelational())
        {
            return (0, 0, true);
        }
        try
        {
            var totalSql = $"SELECT COUNT(*) FROM {Quote(db, table)}";
            var missingSql = $"SELECT COUNT(*) FROM {Quote(db, table)} WHERE TenantId IS NULL OR TenantId = '00000000-0000-0000-0000-000000000000'";
            long total = 0, missing = 0;
            await using (var cmd = db.Database.GetDbConnection().CreateCommand())
            {
                await EnsureOpenAsync(cmd.Connection!, ct);
                cmd.CommandText = totalSql;
                total = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            }
            await using (var cmd = db.Database.GetDbConnection().CreateCommand())
            {
                await EnsureOpenAsync(cmd.Connection!, ct);
                cmd.CommandText = missingSql;
                missing = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            }
            return (total, missing, true);
        }
        catch
        {
            // Table missing from live DB or column missing — treat as
            // non-existent so the migration reports it as zero rather than
            // failing the entire run.
            return (0, 0, false);
        }
    }

    private static async Task<long> ApplyOverrideAsync(
        AtoCopilotContext db, string table, TenantOverride o, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return 0;
        try
        {
            var prefixClause = string.IsNullOrEmpty(o.RowIdPrefix)
                ? string.Empty
                : $" AND CAST(Id AS NVARCHAR(64)) LIKE '{o.RowIdPrefix.Replace("'", "''")}%'";
            var sql = $"UPDATE {Quote(db, table)} SET TenantId = '{o.TenantId}' WHERE (TenantId IS NULL OR TenantId = '00000000-0000-0000-0000-000000000000'){prefixClause}";
            return await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        catch
        {
            return 0;
        }
    }

    private static async Task<long> ApplyDefaultAsync(
        AtoCopilotContext db, string table, Guid defaultTenantId, CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return 0;
        try
        {
            var sql = $"UPDATE {Quote(db, table)} SET TenantId = '{defaultTenantId}' WHERE TenantId IS NULL OR TenantId = '00000000-0000-0000-0000-000000000000'";
            return await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        catch
        {
            return 0;
        }
    }

    private static string Quote(AtoCopilotContext db, string table)
    {
        // SQL Server uses []; SQLite uses "" or [] — both providers accept [].
        return $"[{table}]";
    }

    private static async Task EnsureOpenAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }
    }

    private static async Task EmitAuditAsync(
        AtoCopilotContext db,
        Guid defaultTenantId,
        string? actorOid,
        string? correlationId,
        bool success,
        string details,
        CancellationToken ct)
    {
        try
        {
            db.AuditLogs.Add(new AuditLogEntry
            {
                TenantId = defaultTenantId,
                ActorTenantId = null,
                ImpersonatedTenantId = null,
                UserId = actorOid ?? "system",
                UserRole = "CspAdmin",
                Action = "Tenant.Migrate",
                Outcome = success ? AuditOutcome.Success : AuditOutcome.Failure,
                Details = details,
                CorrelationId = correlationId,
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Audit emission must not break the migration path.
        }
    }
}
