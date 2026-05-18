using System.Data;
using System.Data.Common;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Interceptors;

/// <summary>
/// Feature 048 (T107, FR-030 / FR-031 / FR-032): EF Core
/// <see cref="DbConnectionInterceptor"/> that publishes the current
/// tenancy claims to SQL Server's <c>SESSION_CONTEXT</c> store every time
/// EF opens a connection. The values are then read by the SQL Server
/// Row-Level Security FILTER + BLOCK predicates installed by
/// <c>RlsPolicyInstaller</c> (T109).
/// </summary>
/// <remarks>
/// <para>Three keys are written every time:</para>
/// <list type="bullet">
///   <item><c>TenantId</c> — the effective tenant currently being acted on
///         (impersonation-aware via <see cref="ITenantContext.EffectiveTenantId"/>).</item>
///   <item><c>EffectiveTenantId</c> — duplicate kept under the explicit
///         name for diagnostic / future-proofing reasons (the RLS predicate
///         can use either).</item>
///   <item><c>IsCspAdmin</c> — the literal string <c>'true'</c> when the
///         caller is a CSP-Admin (FR-009 bypass), absent otherwise.</item>
/// </list>
/// <para>The interceptor is a <b>pure no-op</b> for non-SQL-Server providers
/// (SQLite dev mode, in-memory tests) so it can be registered unconditionally
/// without side effects. Per FR-033, SQLite emits a separate startup warning
/// because RLS is unavailable on that provider.</para>
/// </remarks>
public sealed class SqlServerSessionContextConnectionInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContextAccessor _accessor;
    private readonly ILogger<SqlServerSessionContextConnectionInterceptor> _logger;

    public SqlServerSessionContextConnectionInterceptor(
        ITenantContextAccessor accessor,
        ILogger<SqlServerSessionContextConnectionInterceptor> logger)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await PublishSessionContextAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        // Sync path is rarely hit by EF Core 9 but kept for completeness.
        PublishSessionContext(connection);
        base.ConnectionOpened(connection, eventData);
    }

    private async Task PublishSessionContextAsync(DbConnection connection, CancellationToken ct)
    {
        if (!IsSqlServer(connection)) return;

        var ctx = _accessor.Current;
        if (ctx is null)
        {
            // No tenancy context yet (startup / migration / background work
            // before the first request) → publish nothing. The RLS BLOCK
            // predicate will then deny tenant-scoped writes by default,
            // which is the desired safe-default per FR-031.
            return;
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "EXEC sp_set_session_context @key, @value, @readOnly;";
            AddNVarChar(cmd, "@key", "TenantId");
            AddNVarChar(cmd, "@value", ctx.EffectiveTenantId.ToString());
            AddBit(cmd, "@readOnly", true);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            await using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = "EXEC sp_set_session_context @key, @value, @readOnly;";
            AddNVarChar(cmd2, "@key", "EffectiveTenantId");
            AddNVarChar(cmd2, "@value", ctx.EffectiveTenantId.ToString());
            AddBit(cmd2, "@readOnly", true);
            await cmd2.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            if (ctx.IsCspAdmin)
            {
                await using var cmd3 = connection.CreateCommand();
                cmd3.CommandText = "EXEC sp_set_session_context @key, @value, @readOnly;";
                AddNVarChar(cmd3, "@key", "IsCspAdmin");
                AddNVarChar(cmd3, "@value", "true");
                AddBit(cmd3, "@readOnly", true);
                await cmd3.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Don't fail the request — RLS predicates will simply deny rows.
            _logger.LogWarning(ex,
                "Failed to publish SESSION_CONTEXT for tenant {TenantId}", ctx.EffectiveTenantId);
        }
    }

    private void PublishSessionContext(DbConnection connection)
    {
        if (!IsSqlServer(connection)) return;
        var ctx = _accessor.Current;
        if (ctx is null) return;

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "EXEC sp_set_session_context @key, @value, @readOnly;";
            AddNVarChar(cmd, "@key", "TenantId");
            AddNVarChar(cmd, "@value", ctx.EffectiveTenantId.ToString());
            AddBit(cmd, "@readOnly", true);
            cmd.ExecuteNonQuery();

            using var cmd2 = connection.CreateCommand();
            cmd2.CommandText = "EXEC sp_set_session_context @key, @value, @readOnly;";
            AddNVarChar(cmd2, "@key", "EffectiveTenantId");
            AddNVarChar(cmd2, "@value", ctx.EffectiveTenantId.ToString());
            AddBit(cmd2, "@readOnly", true);
            cmd2.ExecuteNonQuery();

            if (ctx.IsCspAdmin)
            {
                using var cmd3 = connection.CreateCommand();
                cmd3.CommandText = "EXEC sp_set_session_context @key, @value, @readOnly;";
                AddNVarChar(cmd3, "@key", "IsCspAdmin");
                AddNVarChar(cmd3, "@value", "true");
                AddBit(cmd3, "@readOnly", true);
                cmd3.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to publish SESSION_CONTEXT for tenant {TenantId}", ctx.EffectiveTenantId);
        }
    }

    private static bool IsSqlServer(DbConnection connection)
    {
        // Match by full type name to avoid taking a hard reference on
        // Microsoft.Data.SqlClient from this layer.
        var name = connection.GetType().FullName ?? string.Empty;
        return name.Contains("SqlConnection", StringComparison.Ordinal)
               && !name.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddNVarChar(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = DbType.String;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static void AddBit(DbCommand cmd, string name, bool value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = DbType.Boolean;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
