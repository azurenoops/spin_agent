# SECURITY.md — ATO Copilot Security Engineering Notes

This file records security vulnerabilities, their root causes, fixes, and reviewer
checklists so future contributors can catch similar patterns during code review.

---

## Scope-to-AsyncLocal Bridge Bugs

**Discovered:** 2026-05-29 (Feature 051 live sign-off, 3-tenant Flankspeed portfolio)  
**Severity:** P1 — silent cross-tenant data leak  
**Fixed in:** PR #97 (commit `0ff3242`)  
**Hardened in:** PR #100-followup (`sec/wave8-100-tenant-scope-hardening`)  
**Related issues:** #100

### What happened

The `[TenantScoped]` EF Core query filter reads the active tenant from
`ITenantContextAccessor.Current` (an `AsyncLocal<ContextHolder?>`-backed singleton).
`TenantResolutionMiddleware` was correctly resolving the tenant context per request
(audit logs proved `EffectiveTenantId` was set) but **never calling
`tenantAccessor.Push(ctx)`** to bridge the resolved context into the `AsyncLocal`.

With nothing on the `AsyncLocal`, `AtoCopilotContext.TenantFilterDisabled` evaluated
to `true` on **every query** and every `[TenantScoped]` read returned rows from **all
tenants**. The bug was invisible in:

- Audit logs (showed the correct `EffectiveTenantId` — data was resolved but not bridged)
- The highest-visibility endpoint (`/api/dashboard/portfolio` CSP-Admin rollup) — it
  computes its own `includedSet` and AND-s it in, so it happened to look correct
- Existing integration tests in `TenantQueryFilterTests` — they called `accessor.Push()`
  directly, exercising the filter machinery but never the middleware-to-accessor bridge

### Root cause pattern

```
HTTP Request
    → TenantResolutionMiddleware
          Stage D: resolves ITenantContext (scoped service, correct EffectiveTenantId)
          ❌ MISSING: tenantAccessor.Push(ctx)    ← the bridge
          → next middleware
    → EF query
          AtoCopilotContext.TenantFilterDisabled
              = (accessor.Current is null)         ← AsyncLocal is empty
              = true                               ← filter OFF — all tenants visible
```

### Fix

`TenantResolutionMiddleware.cs` — after Stage D and before `await _next(httpContext)`:

```csharp
// Stage E: bridge resolved ITenantContext into the AsyncLocal-backed accessor
// so that EF Core query filters and the stamping interceptor can read it.
// The Push() is IDisposable and MUST wrap the next() call so the AsyncLocal
// flows through every async continuation spawned within this request lifetime.
using var _ = tenantAccessor.Push(ctx);
await _next(httpContext);
```

**Critical:** the `Push()` disposable **must wrap `await _next()`**, not just sit
before it. If the `using` block ends before `_next` returns, the AsyncLocal is popped
before EF queries execute.

### Defense-in-depth (added in Wave 8)

1. **`TenantScopedQueryGuardInterceptor`** (`src/Ato.Copilot.Core/Data/Interceptors/`)  
   A `DbCommandInterceptor` that throws `InvalidOperationException` when a query
   executes inside an HTTP request (`IHttpContextAccessor.HttpContext != null`) but
   `accessor.Current is null`. Background services (`HttpContext == null`) are
   allow-listed and never blocked.

2. **`TenantScopedEndpointHttpPipelineTests`** (`tests/.../Tenancy/`)  
   Integration tests that drive tenant-scoped endpoints through the real HTTP pipeline
   (not `accessor.Push()` shortcuts) and assert cross-tenant isolation is enforced.

### Code review checklist

When reviewing any middleware that touches `ITenantContext` or `ITenantContextAccessor`:

- [ ] Does the middleware call `accessor.Push(ctx)` **before** `await _next(...)`?
- [ ] Is the `Push()` return value captured in a `using` block that **wraps** the
      `_next` call (not just placed before it)?
- [ ] Is there a unit test that verifies the accessor has a non-null `Current` inside
      the `_next` delegate (not just after the middleware constructor runs)?
- [ ] Does the middleware bypass any `[TenantScoped]` endpoint without pushing context?
      If so, is that intentional (health checks, auth endpoints)?
- [ ] Are new background services that read `[TenantScoped]` entities using
      `IServiceScopeFactory` + explicit `accessor.Push()` per job (not relying on HTTP context)?

### AsyncLocal semantics reminder

`AsyncLocal<T>` flows **down** the async call chain from a `Task.Run` / `await`
continuation. Changes made **after** a continuation starts do NOT flow back up.
`Push()` must be called before any `await` that could spawn the EF query:

```csharp
// ✅ Correct — Push wraps the await chain
using var _ = accessor.Push(ctx);
await DoWorkThatQuerysDatabaseAsync();   // sees ctx

// ❌ Wrong — Push happens after the await boundary
await DoWorkThatQuerysDatabaseAsync();   // does NOT see ctx
using var _ = accessor.Push(ctx);
```

---

## Reporting Security Issues

To report a security vulnerability in ATO Copilot, contact the maintainers directly
via the channel defined in the project's contributor guide (`docs/dev/contributing.md`).
Do **not** open a public GitHub issue for security vulnerabilities.
