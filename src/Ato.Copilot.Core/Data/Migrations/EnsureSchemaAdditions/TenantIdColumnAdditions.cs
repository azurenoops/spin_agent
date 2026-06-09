using System.Reflection;
using System.Text;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>
/// Idempotent additive SQL migrations for Feature 048 (T056): adds
/// <c>TenantId UNIQUEIDENTIFIER NULL</c> (and a covering
/// <c>IX_&lt;table&gt;_TenantId</c> index) to every entity decorated with
/// <see cref="TenantScopedAttribute"/>.
/// </summary>
/// <remarks>
/// <para>Reflection-driven: walks the EF Core model and for every CLR type
/// carrying <see cref="TenantScopedAttribute"/> emits provider-aware
/// <c>ALTER TABLE ... ADD TenantId ...</c> + index creation guarded by
/// <c>IF NOT EXISTS</c> patterns appropriate to the active provider
/// (SQL Server / SQLite).</para>
/// <para>Called from <c>Program.cs</c>'s <c>EnsureSchemaAdditionsAsync</c>
/// after <c>EnsureCreatedAsync</c>. Safe to run repeatedly. For fresh dev /
/// test databases the columns already exist (created from the model) and
/// every guarded statement is a no-op; the value of this migration is for
/// production / upgrade scenarios where rows pre-date the tenancy retrofit.</para>
/// <para>See feature 048 spec FR-070 / FR-071 and research.md §14.</para>
/// </remarks>
public static class TenantIdColumnAdditions
{
    /// <summary>
    /// Apply the additive schema migration. No-op when the columns / indexes
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

        if (!isSqlServer && !isSqlite)
        {
            logger.LogWarning(
                "TenantIdColumnAdditions: skipping (unsupported provider {Provider})",
                providerName);
            return;
        }

        // Walk the model and collect every [TenantScoped] entity type +
        // its mapped table name. Entities created fresh by EnsureCreatedAsync
        // already have the column; the per-statement guards handle that.
        // Entities that scope via a domain-specific column instead (e.g.
        // LoginAuditEvent → EffectiveTenantId) have NO mapped `TenantId`
        // property in the EF model. Adding a DB column for them would produce
        // a NULL-write on every INSERT and block MultiTenant boot. Skip them
        // here — the stamping interceptor and query-filter installer already
        // apply the same `FindProperty("TenantId")` guard consistently.
        var targets = db.Model.GetEntityTypes()
            .Where(et => et.ClrType.GetCustomAttribute<TenantScopedAttribute>(inherit: false) is not null
                         && !et.IsOwned()
                         && et.GetTableName() is not null
                         && et.FindProperty("TenantId") is not null)
            .Select(et => new
            {
                Table = et.GetTableName()!,
                Schema = et.GetSchema(),
                ClrType = et.ClrType,
            })
            .Distinct()
            .ToList();

        // Log entities that are [TenantScoped] but excluded (no TenantId model property).
        var excluded = db.Model.GetEntityTypes()
            .Where(et => et.ClrType.GetCustomAttribute<TenantScopedAttribute>(inherit: false) is not null
                         && !et.IsOwned()
                         && et.GetTableName() is not null
                         && et.FindProperty("TenantId") is null)
            .Select(et => et.ClrType.Name)
            .ToList();
        foreach (var name in excluded)
        {
            logger.LogDebug(
                "TenantIdColumnAdditions: skipping {Entity} — [TenantScoped] but no TenantId model property (uses domain-specific tenant column)",
                name);
        }

        if (targets.Count == 0)
        {
            logger.LogDebug("TenantIdColumnAdditions: no [TenantScoped] entities found in model");
            return;
        }

        var altered = 0;
        foreach (var t in targets)
        {
            try
            {
                if (isSqlServer)
                {
                    var script = BuildSqlServerScript(t.Schema ?? "dbo", t.Table);
                    await db.Database.ExecuteSqlRawAsync(script, cancellationToken);
                    altered++;
                }
                else // SQLite — split ALTER and CREATE INDEX so a duplicate-column
                     // error doesn't prevent the index from being verified.
                {
                    // Table name is an EF model identifier (not user input) and
                    // SQL identifiers cannot be parameterized; build the DDL as a
                    // plain string (mirrors the SqlServer branch) so EF1002 does
                    // not flag a non-existent injection vector.
                    if (!await SqliteColumnExistsAsync(db, t.Table, "TenantId", cancellationToken))
                    {
                        var addColumnSql = $"ALTER TABLE \"{t.Table}\" ADD COLUMN \"TenantId\" TEXT NULL;";
                        await db.Database.ExecuteSqlRawAsync(addColumnSql, cancellationToken);
                    }

                    var createIndexSql =
                        $"CREATE INDEX IF NOT EXISTS \"IX_{t.Table}_TenantId\" ON \"{t.Table}\" (\"TenantId\");";
                    await db.Database.ExecuteSqlRawAsync(createIndexSql, cancellationToken);
                    altered++;
                }
            }
            catch (Exception ex)
            {
                // Non-fatal — table may not exist yet (test InMemory provider
                // fakes table presence) or the column may already exist with a
                // slightly different shape.
                logger.LogDebug(ex,
                    "TenantIdColumnAdditions: skip {Table} (likely already in sync)", t.Table);
            }
        }

        logger.LogInformation(
            "Verified Feature 048 TenantId columns on {Count} table(s) ({Provider})",
            altered, providerName);
    }

    /// <summary>
    /// SQLite-specific column existence probe via <c>pragma_table_info</c>.
    /// </summary>
    private static async Task<bool> SqliteColumnExistsAsync(
        AtoCopilotContext db,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}';";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null && Convert.ToInt32(result) > 0;
    }

    /// <summary>
    /// SQL Server: <c>IF COL_LENGTH('schema.table', 'TenantId') IS NULL ALTER TABLE ADD …</c>
    /// followed by an idempotent <c>CREATE INDEX</c> guarded by
    /// <c>sys.indexes</c> existence check.
    /// </summary>
    private static string BuildSqlServerScript(string schema, string table)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"IF COL_LENGTH('{schema}.{table}', 'TenantId') IS NULL");
        sb.AppendLine($"    ALTER TABLE [{schema}].[{table}] ADD TenantId UNIQUEIDENTIFIER NULL;");
        sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_{table}_TenantId' AND object_id = OBJECT_ID('[{schema}].[{table}]'))");
        sb.AppendLine($"    CREATE INDEX [IX_{table}_TenantId] ON [{schema}].[{table}] ([TenantId]);");
        return sb.ToString();
    }
}
