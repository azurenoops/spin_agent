using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Ato.Copilot.Core.Data.Interceptors;

/// <summary>
/// Issue #100 — Defense-in-depth: EF Core command interceptor that fails fast
/// when a query is executing inside an HTTP request scope but the ambient
/// <see cref="ITenantContextAccessor.Current"/> is <c>null</c>.
///
/// <para>This catches the exact class of bug discovered on 2026-05-29 where
/// <c>TenantResolutionMiddleware</c> resolved the tenant context correctly but
/// never called <c>accessor.Push(ctx)</c>, silently disabling the tenant query
/// filter on every <c>[TenantScoped]</c> entity for the duration of that
/// request.</para>
///
/// <para>Allow-list 1: background services (<see cref="IHttpContextAccessor.HttpContext"/>
/// is <c>null</c>) are never blocked — the guard only fires when there is an
/// active HTTP context, which means a real web request is in flight without a
/// tenant on the accessor.</para>
///
/// <para>Allow-list 2: <c>[GlobalReference]</c> entities (NIST controls, CspProfile,
/// Tenant, NistControl catalog, framework tables, etc.) do NOT have a tenant query
/// filter and are intentionally cross-tenant-readable. When the <em>only</em> entity
/// types touched by a command are <c>[GlobalReference]</c>, the guard is skipped.
/// This is required for <c>TenantResolutionMiddleware</c> Stage A0, which reads
/// <c>CspProfile</c> (a <c>[GlobalReference]</c> entity) before
/// <c>accessor.Push(ctx)</c> runs at Stage E. See issue #396.</para>
///
/// <para>Registration: added alongside <see cref="TenantStampingSaveChangesInterceptor"/>
/// in <c>CoreServiceExtensions.AddAtoCopilotCore()</c>.</para>
/// </summary>
/// <remarks>
/// See <c>SECURITY.md § Scope-to-AsyncLocal Bridge Bugs</c> for background.
/// </remarks>
public sealed class TenantScopedQueryGuardInterceptor : DbCommandInterceptor
{
    private readonly ITenantContextAccessor _accessor;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<TenantScopedQueryGuardInterceptor> _logger;

    // Lazy-initialized set of table names (lower-case) that belong exclusively to
    // [GlobalReference] entities. Built once from the EF model on the first request
    // that hits the guard (i.e. first time the context is available). Thread-safe
    // via ConcurrentDictionary<string, byte> used as a set.
    //
    // Key: lower-cased table name. Value: 1 = [GlobalReference], 0 = [TenantScoped] / unknown.
    // The map is populated lazily from eventData.Context the first time the guard trips
    // (HttpContext != null AND accessor.Current == null), so it has zero overhead on the
    // happy path (accessor already populated).
    private readonly ConcurrentDictionary<string, byte> _globalRefTableNames = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _modelLoaded;

    public TenantScopedQueryGuardInterceptor(
        ITenantContextAccessor accessor,
        IHttpContextAccessor httpContextAccessor,
        ILogger<TenantScopedQueryGuardInterceptor> logger)
    {
        _accessor = accessor;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <inheritdoc/>
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        ThrowIfTenantFilterBypass(command, eventData);
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfTenantFilterBypass(command, eventData);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    // ─── Guard implementation ─────────────────────────────────────────────

    private void ThrowIfTenantFilterBypass(DbCommand command, CommandEventData eventData)
    {
        // Allow-list 1: no active HTTP request (background service, CLI,
        // hosted service, startup path) → let the query proceed.
        if (_httpContextAccessor.HttpContext is null)
        {
            return;
        }

        // Allow-list 2: accessor already has a tenant context → EF filter is live.
        if (_accessor.Current is not null)
        {
            return;
        }

        // Allow-list 3: [GlobalReference] entities. These are intentionally
        // cross-tenant-readable (no HasQueryFilter, no RLS policy). The guard
        // MUST NOT fire for them — doing so breaks TenantResolutionMiddleware
        // Stage A0 (CspProfile lookup) and any other pre-resolution EF reads.
        //
        // Strategy: build a lazy-initialized table-name → isGlobalReference map
        // from the EF model. If ALL tables touched by this SQL command are in the
        // [GlobalReference] set, skip the throw.
        if (IsGlobalReferenceOnlyQuery(command, eventData))
        {
            return;
        }

        // We are inside a real HTTP request with no ambient tenant context AND the
        // query touches at least one [TenantScoped] table.
        // This is the exact signature of the SEC-P1 bug (issue #100):
        // middleware resolved the tenant but never called accessor.Push().
        //
        // Log at Error (not just Warning) so the bug is impossible to miss
        // in structured log pipelines, then throw so the request surface
        // returns 500 rather than silently leaking cross-tenant rows.
        var path = _httpContextAccessor.HttpContext.Request.Path;
        var method = _httpContextAccessor.HttpContext.Request.Method;

        _logger.LogError(
            "[SEC] TenantScopedQueryGuardInterceptor: query executing inside HTTP request " +
            "{Method} {Path} with no ambient ITenantContextAccessor.Current. " +
            "Tenant query filter is DISABLED — possible cross-tenant data leak. " +
            "Ensure TenantResolutionMiddleware calls accessor.Push() before invoking next. " +
            "SQL: {Sql}",
            method, path, command.CommandText[..Math.Min(200, command.CommandText.Length)]);

        throw new InvalidOperationException(
            $"[SEC] A [TenantScoped] EF query was executed inside HTTP request " +
            $"{method} {path} without an ambient ITenantContextAccessor.Current. " +
            $"The tenant query filter is not active — this would silently return cross-tenant rows. " +
            $"Root cause: TenantResolutionMiddleware did not call accessor.Push(ctx) before " +
            $"invoking the next middleware. See SECURITY.md § Scope-to-AsyncLocal Bridge Bugs.");
    }

    // ─── [GlobalReference] table detection ───────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if every EF entity type referenced by <paramref name="command"/>
    /// is annotated with <c>[GlobalReference]</c>. A <c>true</c> result means the guard
    /// should be bypassed — there is no tenant filter to enforce and no cross-tenant
    /// data leak risk.
    /// </summary>
    /// <remarks>
    /// Table names are extracted from the SQL <c>FROM</c> clause via a simple
    /// bracket/quote-aware token scan. This is intentionally conservative: if we
    /// cannot identify the table (e.g., a raw SQL query with an unusual quoting style),
    /// we return <c>false</c> (i.e., we enforce the guard). False-positives from parse
    /// failures are safe; false-negatives would be a security regression.
    ///
    /// The EF model scan (lazy init) runs once per <see cref="DbContext"/> type and
    /// is thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
    /// </remarks>
    private bool IsGlobalReferenceOnlyQuery(DbCommand command, CommandEventData eventData)
    {
        // Populate the model cache on first invocation.
        if (!_modelLoaded && eventData.Context is { } ctx)
        {
            PopulateGlobalRefTableNames(ctx);
        }

        // Extract table names from the SQL text. If we can't find any recognizable
        // table references → be conservative and let the guard proceed (return false).
        var tableNames = ExtractTableNames(command.CommandText);
        if (tableNames.Count == 0)
        {
            return false;
        }

        // The command is [GlobalReference]-only if EVERY table in the SQL is in the
        // global-ref set. A single [TenantScoped] table → guard fires.
        foreach (var table in tableNames)
        {
            if (!_globalRefTableNames.ContainsKey(table))
            {
                // Not in the [GlobalReference] set → assume [TenantScoped] or unknown.
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Lazily populates <see cref="_globalRefTableNames"/> from the EF Core model.
    /// Thread-safe (ConcurrentDictionary; worst case: two threads both populate once,
    /// result is idempotent).
    /// </summary>
    private void PopulateGlobalRefTableNames(DbContext context)
    {
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (clrType is null) continue;

            var isGlobal = clrType.GetCustomAttributes(typeof(GlobalReferenceAttribute), inherit: false).Length > 0;
            if (!isGlobal) continue;

            var tableName = entityType.GetTableName();
            if (tableName is null) continue;

            // Value byte: 1 = confirmed [GlobalReference]. The dictionary acts as a set.
            _globalRefTableNames.TryAdd(tableName, 1);
        }

        _modelLoaded = true;
    }

    /// <summary>
    /// Extracts unqualified table names from a SQL command text.
    ///
    /// <para>Handles the two quoting styles EF Core emits:</para>
    /// <list type="bullet">
    ///   <item>SQL Server: <c>[TableName]</c></item>
    ///   <item>SQLite / generic: <c>"TableName"</c></item>
    /// </list>
    ///
    /// <para>Only table names that appear after <c>FROM</c> or <c>JOIN</c>
    /// keywords are extracted. Schema prefixes (e.g. <c>[dbo].[TableName]</c>) are
    /// stripped — only the final token is kept.</para>
    /// </summary>
    private static HashSet<string> ExtractTableNames(string sql)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(sql)) return names;

        // Tokenize on whitespace / newlines and scan for FROM/JOIN keywords.
        var tokens = sql.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            var tok = tokens[i];
            if (!tok.Equals("FROM", StringComparison.OrdinalIgnoreCase) &&
                !tok.Equals("JOIN", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = tokens[i + 1];

            // Strip schema prefix: "[dbo].[TableName]" → "[TableName]"
            // Handles both "." separator and AS alias immediately following.
            var dotIdx = candidate.LastIndexOf('.');
            if (dotIdx >= 0)
            {
                candidate = candidate[(dotIdx + 1)..];
            }

            // Strip SQL Server brackets: [TableName] → TableName
            if (candidate.StartsWith('[') && candidate.EndsWith(']'))
            {
                candidate = candidate[1..^1];
            }
            // Strip double-quotes: "TableName" → TableName
            else if (candidate.StartsWith('"') && candidate.EndsWith('"'))
            {
                candidate = candidate[1..^1];
            }

            // Ignore subquery / CTE starts
            if (candidate.StartsWith('(') || string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            names.Add(candidate);
        }

        return names;
    }
}
