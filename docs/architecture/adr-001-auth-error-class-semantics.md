# ADR-001 — Auth Error Class Semantics: 401 vs 403 in RequireAuth

**Status:** Accepted  
**Date:** 2026-06-09  
**Author:** Batman (Chief Systems Architect)  
**Relates to:** Issue [#362](https://github.com/azurenoops/spin_agent/issues/362), Feature 051, Feature 048  
**Files affected:** `src/Ato.Copilot.Dashboard/src/features/auth/RequireAuth.tsx`

---

## Context

`RequireAuth.tsx` probes `GET /api/auth/me` to gate every protected route. Prior to this ADR, both HTTP 401 and HTTP 403 responses triggered `instance.loginRedirect()`.

This conflation causes a **permanent redirect loop** for any user who holds a valid Entra account but has no SPIN Agent tenant assignment:

1. User hits protected route → `GET /api/auth/me` returns **403** (authenticated, no tenant)
2. `RequireAuth` calls `loginRedirect` → Entra authenticates the user again
3. User returns with a fresh token → `GET /api/auth/me` returns **403** again
4. Loop never terminates — browser eventually surfaces "Too many redirects"

This is a security boundary ambiguity: **401 means "who are you?" — 403 means "I know who you are; you don't have access here."** Treating them identically collapses a meaningful distinction that determines the correct UX path.

---

## Decision

**401 and 403 MUST trigger different behaviors in `RequireAuth` and in all future auth-gated components.**

### Canonical Error Class Contract

| HTTP Status | Semantic | Client Action |
|-------------|----------|---------------|
| `401 Unauthorized` | Unauthenticated — no valid session/token | `loginRedirect()` to re-establish identity |
| `403 Forbidden` | Authenticated but unauthorized — no tenant assignment or insufficient role | Navigate to `/login/error?errorClass=<reason>` — **no MSAL redirect** |

### `RequireAuth.tsx` Implementation

```ts
if (status === 401) {
  setState('redirecting');
  const deepLink =
    window.location.pathname + window.location.search + window.location.hash;
  void instance.loginRedirect({ scopes: DEFAULT_API_SCOPES, state: deepLink });
} else if (status === 403) {
  // Authenticated but no tenant assignment — route to error page, not MSAL.
  navigate('/login/error?errorClass=NoTenantAssignment', { replace: true });
}
```

### Error Page Query Params

The `/login/error` page MUST accept `?errorClass=` and render contextual guidance:

| `errorClass` | Display message |
|---|---|
| `NoTenantAssignment` | "Your account does not have access to a SPIN Agent tenant. Contact your system administrator." |
| `InsufficientRole` | "You do not have the required role for this resource. Contact your tenant administrator." |
| *(default / missing)* | Generic auth failure message |

### Server-Side Contract (non-negotiable)

The `GET /api/auth/me` endpoint MUST return:

- **`401`** — token absent, expired, or invalid signature
- **`403`** — valid token, authenticated principal, but `TenantMemberships` is empty or the caller is not in a required PIM role

No other 4xx status is permitted from `/api/auth/me`. The SPA contract depends on this being strictly enforced.

---

## Consequences

**Positive:**
- Eliminates the redirect loop for valid Entra users without tenant assignments
- Establishes a clear semantic contract all future protected routes can rely on
- `/login/error` can display actionable guidance rather than looping

**Negative / Trade-offs:**
- All future protected route components MUST follow this two-branch pattern; a code review gate is required to enforce it
- The `/login/error` page must be extended to accept and render `errorClass` (see Issue #362 fix)

---

## Alternatives Considered

**A. Single redirect for all 4xx** — Rejected. Already proven to cause infinite loops; harms valid users.

**B. Client-side Entra group membership check before probing `/me`** — Rejected. Requires reading group claims from MSAL's local cache, which doesn't reflect server-side tenant assignment state. Server is the source of truth.

**C. 403 → navigate to `/unauthorized` (separate page)** — Acceptable, but `/login/error?errorClass=` provides richer extensibility without additional routes.

---

## Enforcement

- Code review MUST reject any PR that adds `status === 403` → `loginRedirect()` in any component
- `RequireAuth.tsx` is the **single gating component** for all protected routes — do not replicate auth logic in individual page components
- ADR-001 MUST be referenced in Feature 051 spec and any auth-adjacent feature specs going forward
