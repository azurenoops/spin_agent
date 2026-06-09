using System.Data.Common;
using Ato.Copilot.Core.Interfaces.Tenancy;
using Microsoft.AspNetCore.Http;
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
/// <para>Allow-list: background services (<see cref="IHttpContextAccessor.HttpContext"/>
/// is <c>null</c>) are never blocked — the guard only fires when there is an
/// active HTTP context, which means a real web request is in flight without a
/// tenant on the accessor.</para>
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
        ThrowIfTenantFilterBypass(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ThrowIfTenantFilterBypass(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    // ─── Guard implementation ─────────────────────────────────────────────

    private void ThrowIfTenantFilterBypass(DbCommand command)
    {
        // Allow-list: no active HTTP request (background service, CLI,
        // hosted service, startup path) → let the query proceed.
        if (_httpContextAccessor.HttpContext is null)
        {
            return;
        }

        // Allow-list: accessor already has a tenant context → EF filter is live.
        if (_accessor.Current is not null)
        {
            return;
        }

        // We are inside a real HTTP request with no ambient tenant context.
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
}
