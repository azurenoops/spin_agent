# ADR-002 — X-Simulated-Role Header: Dev-Only Scope and Server-Side Enforcement

**Status:** Accepted  
**Date:** 2026-06-09  
**Author:** Batman (Chief Systems Architect)  
**Relates to:** Issue [#364](https://github.com/azurenoops/spin_agent/issues/364), Feature 048  
**Files affected:**  
- `src/Ato.Copilot.Dashboard/src/features/onboarding/api/onboardingApi.ts`  
- `src/Ato.Copilot.Dashboard/src/features/csp-onboarding/api.ts`  
- `src/Ato.Copilot.Dashboard/src/features/tenancy/api.ts`

---

## Context

Three axios API clients inject `X-Simulated-Role` from `localStorage['ato-dashboard-settings'].role` into every outbound request. None of these interceptors are guarded with `import.meta.env.DEV`.

**In production today**, any user who sets `ato-dashboard-settings` in their browser's localStorage can transmit an arbitrary role value (`CSP.Admin`, `ISSO`, `System.Owner`, etc.) as a request header to:
- `/api/onboarding/*`
- `/api/csp/onboarding/*`
- `/api/tenants/*`

Whether the server uses, ignores, or logs this header is undocumented. The ambiguity is the vulnerability — the header's production behavior has no contract.

### Risk Assessment

| Scenario | Impact |
|---|---|
| Server reads `X-Simulated-Role` and uses it for AuthZ in any code path | Direct privilege escalation — any user can claim CSP.Admin |
| Server ignores it in production | No functional harm, but the header leaks role enumeration (attacker can probe which roles exist by reading the localStorage key) |
| Server logs it | Noise in audit logs; could complicate forensic investigation of real auth events |

For a DoD application that supports ATO workflows under NIST RMF, any undocumented privileged header in production is a security finding.

---

## Decision

### 1. Frontend: DEV guard on all three interceptors (immediate fix)

All three `X-Simulated-Role` interceptor registrations MUST be wrapped in `import.meta.env.DEV`:

```ts
if (import.meta.env.DEV) {
  apiClient.interceptors.request.use((config) => {
    try {
      const raw = localStorage.getItem('ato-dashboard-settings');
      if (raw) {
        const settings = JSON.parse(raw) as { role?: string };
        if (settings.role) config.headers['X-Simulated-Role'] = settings.role;
      }
    } catch {
      // ignore parse errors
    }
    return config;
  });
}
```

`import.meta.env.DEV` is `true` only during Vite dev server (`vite dev`). It is `false` in all `vite build` outputs. This is the correct compile-time elimination — no runtime overhead, no prod leakage.

### 2. Server-side: Explicit null-out of `X-Simulated-Role` in non-Development environments

The server (ASP.NET Core MCP API) MUST explicitly strip or null `X-Simulated-Role` from inbound requests when `ASPNETCORE_ENVIRONMENT != Development`. This is the defense-in-depth layer.

Recommended implementation: a middleware shim that runs before any role-reading logic:

```csharp
// In Program.cs or middleware pipeline, before UseAuthorization()
app.Use(async (context, next) =>
{
    if (!app.Environment.IsDevelopment())
    {
        context.Request.Headers.Remove("X-Simulated-Role");
    }
    await next();
});
```

This ensures even if the frontend DEV guard is bypassed (e.g., a future regression, a raw API call, a different client), the production server treats the header as non-existent.

### 3. Document `X-Simulated-Role` in the security architecture

`docs/architecture/security.md` MUST document:
- `X-Simulated-Role` is a **development-only** header
- Frontend: guarded by `import.meta.env.DEV`
- Backend: stripped by middleware in non-Development environments
- The header value maps to: `System.Owner`, `ISSO`, `CSP.Admin` (see Feature 048 role table)

---

## Consequences

**Positive:**
- Eliminates the privilege escalation surface in production
- Defense-in-depth: frontend guard + backend strip — neither alone is sufficient
- Documented contract prevents future regressions

**Negative / Trade-offs:**
- Any developer who forgets the DEV guard in a future new axios client will reintroduce the vulnerability; must be part of PR review checklist
- Backend middleware adds one header-read per request (negligible overhead)

---

## Alternatives Considered

**A. Remove `X-Simulated-Role` entirely** — Rejected. The dev simulation workflow is valuable and actively used. The risk is the missing environment guard, not the header itself.

**B. Frontend-only DEV guard** — Rejected as insufficient. Defense-in-depth requires server-side enforcement. A raw `curl` call, Postman test, or future regression could bypass frontend guards.

**C. Use a signed/HMAC header instead of a plain string** — Considered for future hardening. Out of scope for this fix; the simulated role workflow is dev-only and doesn't need cryptographic integrity.

---

## Enforcement

- PR review checklist: any new axios client that reads `ato-dashboard-settings` from localStorage MUST have the `import.meta.env.DEV` guard
- `docs/architecture/security.md` "CAC Simulation Mode" section MUST be updated to include `X-Simulated-Role` behavior
- Server-side middleware strip MUST be present before any production deployment of the MCP API
