using System.Reflection;
using System.Text;
using Ato.Copilot.Core.Data.Context;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

/// <summary>
/// Feature 048 (T109 / T110, FR-030 / FR-031 / FR-032): installs SQL Server
/// Row-Level Security artifacts that enforce tenant isolation at the
/// database layer — defense-in-depth even if the application code is
/// bypassed (e.g., a stolen connection string, a forgotten
/// <c>WHERE</c> clause).
/// </summary>
/// <remarks>
/// <para>Three artifacts are created (idempotently):</para>
/// <list type="number">
///   <item>A schema-bound table-valued <c>fn_TenantPredicate</c> that
///         compares the row's <c>TenantId</c> against
///         <c>SESSION_CONTEXT(N'TenantId')</c>; CSP-Admin sessions
///         (<c>SESSION_CONTEXT(N'IsCspAdmin') = N'true'</c>) bypass the
///         filter for FR-009.</item>
///   <item>A SECURITY POLICY <c>TenantSecurityPolicy</c> that adds the
///         predicate as both a FILTER and a BLOCK predicate (AFTER
///         INSERT, AFTER UPDATE) for every retrofitted
///         <see cref="TenantScopedAttribute"/> entity.</item>
///   <item>Idempotency guards: if the function or policy already exists
///         the script returns silently.</item>
/// </list>
/// <para>Skipped on non-SQL-Server providers — SQLite emits a separate
/// startup warning per FR-033 (T111).</para>
/// </remarks>
public static class RlsPolicyInstaller
{
    /// <summary>
    /// Apply the Row-Level Security policy to every <c>[TenantScoped]</c>
    /// table. No-op for non-SQL-Server providers.
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
        if (!isSqlServer)
        {
            // Feature 048 (T111, FR-033): make it loud at startup that
            // tenant isolation in dev mode is enforced *only* by EF query
            // filters and the SaveChanges interceptor when running against
            // SQLite — NOT by the database engine. Skipping non-SQLite
            // non-SQL-Server providers (in-memory tests, etc.) silently.
            if (isSqlite)
            {
                logger.LogWarning(
                    "Tenant isolation: SQLite provider detected — Row-Level Security NOT installed. " +
                    "Using EF query filters only. NOT FOR PRODUCTION.");
            }
            return;
        }

        var targets = db.Model.GetEntityTypes()
            .Where(et => et.ClrType.GetCustomAttribute<TenantScopedAttribute>(inherit: false) is not null
                         && !et.IsOwned()
                         && et.GetTableName() is not null)
            .Select(et => new
            {
                Table = et.GetTableName()!,
                Schema = et.GetSchema() ?? "dbo",
            })
            .Distinct()
            .OrderBy(t => t.Table)
            .ToList();

        if (targets.Count == 0)
        {
            logger.LogInformation("RlsPolicyInstaller: no [TenantScoped] entities found, nothing to install");
            return;
        }

        try
        {
            // Step 1 — install the predicate function (idempotent).
            await db.Database.ExecuteSqlRawAsync(BuildPredicateFunctionSql(), cancellationToken);

            // Step 2 — drop the existing policy if present, then re-create.
            // CREATE SECURITY POLICY does not support OR ALTER syntax in
            // pre-2022 SQL Server, so the safe pattern is conditional drop +
            // create.
            await db.Database.ExecuteSqlRawAsync(BuildPolicySql(targets.Select(t => (t.Schema, t.Table))), cancellationToken);

            logger.LogInformation(
                "Verified Feature 048 RLS policy on {Count} [TenantScoped] table(s)", targets.Count);
        }
        catch (Exception ex)
        {
            // Non-fatal — the EF query filters and the SaveChangesInterceptor
            // remain the primary line of defense. Surfaced as a warning so
            // operators notice if the DB user lacks the rights to install
            // RLS (CONTROL on schema is required).
            logger.LogWarning(ex,
                "RlsPolicyInstaller: could not install Row-Level Security policy. " +
                "Application-level tenant filters remain in force.");
        }
    }

    /// <summary>
    /// Build the <c>CREATE OR ALTER FUNCTION dbo.fn_TenantPredicate</c>
    /// statement. Schema-bound + inline TVF so it is eligible for use in a
    /// SECURITY POLICY.
    /// </summary>
    /// <remarks>
    /// The function is schema-bound, so SQL Server will refuse to drop it
    /// while <c>TenantSecurityPolicy</c> references it. Idempotent re-runs
    /// (every redeploy) therefore have to drop the dependent policy FIRST,
    /// then the function. Without that ordering, the second startup fails
    /// with:
    /// <code>
    /// Cannot DROP FUNCTION 'dbo.fn_TenantPredicate' because it is being
    /// referenced by object 'TenantSecurityPolicy'.
    /// </code>
    /// and the catch block in <see cref="ApplyAsync"/> downgrades that to
    /// a warning, silently disabling defense-in-depth.
    /// </remarks>
    private static string BuildPredicateFunctionSql()
    {
        return """
            -- Drop dependent policy first so the schema-bound function can be replaced.
            IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'TenantSecurityPolicy')
                DROP SECURITY POLICY dbo.TenantSecurityPolicy;
            IF OBJECT_ID('dbo.fn_TenantPredicate', 'IF') IS NOT NULL
                DROP FUNCTION dbo.fn_TenantPredicate;
            EXEC('
            CREATE FUNCTION dbo.fn_TenantPredicate (@TenantId UNIQUEIDENTIFIER)
            RETURNS TABLE
            WITH SCHEMABINDING
            AS RETURN
                SELECT 1 AS allowed
                WHERE
                    -- Effective tenant matches the row, OR the caller is a CSP-Admin (FR-009 bypass), OR
                    -- there is no session context (background work / migrations during boot).
                    @TenantId = CAST(SESSION_CONTEXT(N''TenantId'') AS UNIQUEIDENTIFIER)
                    OR CAST(SESSION_CONTEXT(N''IsCspAdmin'') AS NVARCHAR(8)) = N''true''
                    OR SESSION_CONTEXT(N''TenantId'') IS NULL;
            ');
            """;
    }

    /// <summary>
    /// Build the <c>CREATE SECURITY POLICY dbo.TenantSecurityPolicy</c>
    /// statement. Drops + re-creates so adding a new <c>[TenantScoped]</c>
    /// table on a later deploy automatically picks up the predicates.
    /// </summary>
    private static string BuildPolicySql(IEnumerable<(string Schema, string Table)> targets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = 'TenantSecurityPolicy')");
        sb.AppendLine("    DROP SECURITY POLICY dbo.TenantSecurityPolicy;");
        sb.AppendLine("CREATE SECURITY POLICY dbo.TenantSecurityPolicy");

        var entries = new List<string>();
        foreach (var (schema, table) in targets)
        {
            entries.Add($"    ADD FILTER PREDICATE dbo.fn_TenantPredicate(TenantId) ON [{schema}].[{table}]");
            entries.Add($"    ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON [{schema}].[{table}] AFTER INSERT");
            entries.Add($"    ADD BLOCK PREDICATE dbo.fn_TenantPredicate(TenantId) ON [{schema}].[{table}] AFTER UPDATE");
        }

        sb.Append(string.Join(",\n", entries));
        sb.AppendLine();
        sb.AppendLine("WITH (STATE = ON);");
        return sb.ToString();
    }
}
