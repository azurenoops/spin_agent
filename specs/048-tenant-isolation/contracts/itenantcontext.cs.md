# Contract: `ITenantContext` and `ITenantContextAccessor`

**Feature**: `048-tenant-isolation` | **Date**: 2026-05-07
**Spec references**: FR-010, FR-011, FR-024, edge case "MCP tool invoked from VS Code extension or Teams bot".

This contract is the **single source of truth** for how application code reads
the resolved tenant scope of a request.

---

## Namespace

```text
Ato.Copilot.Core.Interfaces.Tenancy
```

## Types

```csharp
namespace Ato.Copilot.Core.Interfaces.Tenancy;

/// <summary>
/// Per-request resolved tenant scope. Lifetime: <see cref="ServiceLifetime.Scoped"/>.
/// Populated by <c>TenantResolutionMiddleware</c> after authentication and
/// before authorization. Consumed by EF query filters, the SaveChanges
/// interceptor, the SQL Server connection interceptor, every dashboard
/// endpoint, and every MCP tool.
/// </summary>
public interface ITenantContext
{
    /// <summary>The user's home tenant.</summary>
    /// <remarks>
    /// Resolved in this order:
    ///   1. Entra <c>tid</c> claim → <c>Tenants.EntraTenantId</c> lookup.
    ///   2. <c>X-Tenant-Id</c> header (only honored in dev/simulation mode).
    ///   3. Singleton default tenant in <see cref="DeploymentMode.SingleTenant"/>.
    /// Throws <see cref="MissingTenantClaimException"/> if none resolve.
    /// </remarks>
    Guid TenantId { get; }

    /// <summary>Optional sub-organization scope. Null = tenant-level.</summary>
    Guid? OrganizationId { get; }

    /// <summary>True when the principal carries the <c>CSP.Admin</c> role.</summary>
    bool IsCspAdmin { get; }

    /// <summary>The tenant the principal is currently impersonating, if any.</summary>
    Guid? ImpersonatedTenantId { get; }

    /// <summary>
    /// The value used by query filters and stamping:
    /// <c>ImpersonatedTenantId ?? TenantId</c>.
    /// </summary>
    Guid EffectiveTenantId { get; }

    /// <summary>
    /// Lifecycle status of the effective tenant. Cached for 30 seconds in
    /// <see cref="IMemoryCache"/> by <c>TenantResolutionMiddleware</c> so
    /// downstream code does not re-query the DB on every read.
    /// </summary>
    TenantStatus Status { get; }
}

/// <summary>
/// Accessor for code paths that have no <see cref="HttpContext"/> — for
/// example MCP tools invoked through <c>Ato.Copilot.Channels</c> from the
/// VS Code extension or M365 Teams bot, and background services such as the
/// compliance watch worker. Lifetime: <see cref="ServiceLifetime.Singleton"/>.
/// </summary>
public interface ITenantContextAccessor
{
    /// <summary>The current ambient context, or <c>null</c> if none has been pushed.</summary>
    ITenantContext? Current { get; }

    /// <summary>
    /// Pushes <paramref name="context"/> as the ambient scope. Disposing the
    /// returned <see cref="IDisposable"/> pops it. Implemented over
    /// <see cref="AsyncLocal{T}"/>.
    /// </summary>
    IDisposable Push(ITenantContext context);
}
```

## Concrete enum

```csharp
public enum TenantStatus { Active = 0, Suspended = 1, Disabled = 2 }
```

## Exceptions

```csharp
public sealed class MissingTenantClaimException : Exception { /* 401 MISSING_TENANT_CLAIM */ }
public sealed class TenantSuspendedException     : Exception { /* 423 TENANT_SUSPENDED      */ }
public sealed class TenantDisabledException      : Exception { /* 401 TENANT_DISABLED       */ }
public sealed class TenantConsistencyException   : Exception { /* 409 CROSS_TENANT_REFERENCE_REJECTED */ }
public sealed class NotCspAdminException         : Exception { /* 403 FORBIDDEN_NOT_CSP_ADMIN */ }
```

## DI registration (in `Program.cs`)

```csharp
services.AddScoped<ITenantContext, TenantContext>();
services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();

services.AddDbContext<AtoCopilotContext>((sp, options) =>
{
    options.AddInterceptors(
        sp.GetRequiredService<TenantStampingSaveChangesInterceptor>(),
        sp.GetRequiredService<SqlServerSessionContextConnectionInterceptor>());
});
```

## Usage rules

1. **Endpoints, services, and tools MUST resolve `ITenantContext` via constructor injection.**
   Do NOT read `HttpContext.User.FindFirst("tid")` directly; that's the middleware's job.

2. **Background services MUST acquire scope via `ITenantContextAccessor.Push`.**
   Pattern:

   ```csharp
   using var _ = _accessor.Push(new TenantContext(/* … */));
   await DoWorkAsync(/* uses scoped DI */);
   ```

3. **MCP tools MUST NOT add `Guid tenantId` parameters to their public surface.**
   The context flows automatically through DI.

4. **Tests MUST inject a `FakeTenantContext` (xUnit fixture).**
   The integration test base supplies one with switchable `TenantId` /
   `IsCspAdmin` / `ImpersonatedTenantId`.

5. **Never serialize an `ITenantContext` instance to disk or wire.**
   It is a request-scoped runtime concept, not a persistence model.
