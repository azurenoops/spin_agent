using Ato.Copilot.Core.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>
/// Idempotent additive SQL migration for Feature 048 (T073): adds
/// <c>ActorTenantId</c> and <c>ImpersonatedTenantId</c> columns to the
/// <c>AuditLogs</c> table plus the two composite indexes
/// <c>IX_AuditLogs_TenantId_Timestamp</c> and
/// <c>IX_AuditLogs_ActorTenantId_Timestamp</c>.
/// </summary>
/// <remarks>
/// <para>The base <c>TenantId</c> column is added by the generic
/// <see cref="TenantIdColumnAdditions"/>. This file extends only the
/// audit-log-specific attribution columns described in spec FR-052 and
/// data-model.md §6.</para>
/// <para>Called from <c>Program.cs</c>'s <c>EnsureSchemaAdditionsAsync</c>
/// after the generic tenant-column pass. Safe to re-run; every statement is
/// guarded.</para>
/// </remarks>
public static class AuditLogTenantAttributionAdditions
{
    /// <summary>
    /// Apply the additive schema migration. No-op when columns / indexes
    /// already exist.
    /// </summary>
    public static async Task ApplyAsync(
        AtoCopilotContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);

        var providerName = db.Database.ProviderName ?? string.Empty;
        var isSqlServer = providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
        var isSqlite = providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isSqlServer)
            {
                await db.Database.ExecuteSqlRawAsync(SqlServerScript, cancellationToken);
            }
            else if (isSqlite)
            {
                await ApplySqliteAsync(db, cancellationToken);
            }
            else
            {
                logger.LogWarning(
                    "AuditLogTenantAttributionAdditions: skipping (unsupported provider {Provider})",
                    providerName);
                return;
            }

            logger.LogInformation(
                "Verified Feature 048 audit-log tenant-attribution columns on {Provider}",
                providerName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "AuditLogTenantAttributionAdditions encountered an error — non-fatal if columns already exist");
        }
    }

    // ─── SQLite (dev / test) ─────────────────────────────────────────────────
    // SQLite ALTER TABLE has no IF NOT EXISTS; probe pragma_table_info first
    // and only add columns that are missing. Indexes use IF NOT EXISTS.
    private static async Task ApplySqliteAsync(
        AtoCopilotContext db,
        CancellationToken cancellationToken)
    {
        // 1. Determine which columns already exist on AuditLogs.
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = db.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info('AuditLogs')";
            if (cmd.Connection!.State != System.Data.ConnectionState.Open)
            {
                await cmd.Connection.OpenAsync(cancellationToken);
            }
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                // PRAGMA table_info schema: cid, name, type, notnull, dflt_value, pk
                existingColumns.Add(reader.GetString(1));
            }
        }

        if (!existingColumns.Contains("ActorTenantId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"AuditLogs\" ADD COLUMN \"ActorTenantId\" TEXT NULL",
                cancellationToken);
        }
        if (!existingColumns.Contains("ImpersonatedTenantId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"AuditLogs\" ADD COLUMN \"ImpersonatedTenantId\" TEXT NULL",
                cancellationToken);
        }
        // T117/T118 [US6]: correlation id for stitching audit rows to a workflow.
        if (!existingColumns.Contains("CorrelationId"))
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"AuditLogs\" ADD COLUMN \"CorrelationId\" TEXT NULL",
                cancellationToken);
        }

        // 2. Indexes (IF NOT EXISTS handles re-runs).
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_TenantId_Timestamp\" " +
            "ON \"AuditLogs\" (\"TenantId\", \"Timestamp\")",
            cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_ActorTenantId_Timestamp\" " +
            "ON \"AuditLogs\" (\"ActorTenantId\", \"Timestamp\")",
            cancellationToken);
    }

    // ─── SQL Server (production) ─────────────────────────────────────────────
    private const string SqlServerScript = """
        IF COL_LENGTH('AuditLogs', 'ActorTenantId') IS NULL
            ALTER TABLE AuditLogs ADD ActorTenantId UNIQUEIDENTIFIER NULL;

        IF COL_LENGTH('AuditLogs', 'ImpersonatedTenantId') IS NULL
            ALTER TABLE AuditLogs ADD ImpersonatedTenantId UNIQUEIDENTIFIER NULL;

        IF COL_LENGTH('AuditLogs', 'CorrelationId') IS NULL
            ALTER TABLE AuditLogs ADD CorrelationId NVARCHAR(128) NULL;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes
                       WHERE name = 'IX_AuditLogs_TenantId_Timestamp'
                         AND object_id = OBJECT_ID(N'dbo.AuditLogs'))
            CREATE INDEX IX_AuditLogs_TenantId_Timestamp
                ON AuditLogs (TenantId, Timestamp);

        IF NOT EXISTS (SELECT 1 FROM sys.indexes
                       WHERE name = 'IX_AuditLogs_ActorTenantId_Timestamp'
                         AND object_id = OBJECT_ID(N'dbo.AuditLogs'))
            CREATE INDEX IX_AuditLogs_ActorTenantId_Timestamp
                ON AuditLogs (ActorTenantId, Timestamp);
        """;

    // ─── SQLite (dev / test) ─────────────────────────────────────────────────
    // SQLite implementation is in ApplySqliteAsync above (PRAGMA-driven).
}
