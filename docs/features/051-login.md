# Feature 051 — First-Class Login Experience

**Status**: MVP code-complete on branch `051-login` (35 commits).
Manual sign-off pending per quickstart § 18.

**Spec**: [specs/051-login/spec.md](https://github.com/azurenoops/ato-copilot/blob/051-login/specs/051-login/spec.md)
**Plan**: [specs/051-login/plan.md](https://github.com/azurenoops/ato-copilot/blob/051-login/specs/051-login/plan.md)
**Tasks**: [specs/051-login/tasks.md](https://github.com/azurenoops/ato-copilot/blob/051-login/specs/051-login/tasks.md)

## Overview

Security Posture Intelligence Navigator had authentication plumbing (CAC/PIV MSAL — Feature 003,
simulation mode for dev — Feature 027, tenant isolation + CSP-Admin
impersonation — Feature 048) but **no first-class Login experience**.
Users landed on protected pages and the browser silently negotiated
Entra. Feature 051 ships the missing surface across all four channels.

## Supported surfaces

| Surface | What it adds | Code path |
| --- | --- | --- |
| **Dashboard** (React 19 + MSAL.js) | Branded `/login`, tenant picker, account menu, idle-timeout sign-out, 10 per-class error pages, dev simulation panel, CSP-Admin impersonation banner with countdown | `src/Ato.Copilot.Dashboard/src/features/auth/*` |
| **VS Code extension** | Entra device-code sign-in (cloud-aware), token cache in SecretStorage, `@ato sign in / sign out / switch tenant` commands, status-bar state, single-shot 401 re-auth notification | `extensions/vscode/src/auth/*` |
| **M365 / Teams bot** | Adaptive Card sign-in, Teams SSO with OAuthPrompt fallback, Teams-tenant-wide identity link, manifest validator (Required mode fails startup without an SSO entry) | `extensions/m365/src/auth/*` |
| **Web Chat (`Ato.Copilot.Chat`)** | Redirects unauthenticated traffic to the dashboard's `/login` — no separate login surface needed per spec assumptions | (no new code; shares the dashboard route) |

## Key features

### 1. Branded login + deep-link preservation (US1)

`GET /api/auth/login-config` returns the deployment's branding, the
configured `Auth:DefaultMethod`, the enabled auth-method descriptors,
and the MSAL config. The SPA hits this BEFORE auth so the
`/login?return=<original-url>` page can render without a session.

### 2. Sign-out + idle sign-out (US2)

`POST /api/auth/signout` writes a `SignOut` or `IdleSignOut` audit row
and clears the impersonation cookie. The dashboard's `useIdleTimer`
hook tracks user input and fires a full sign-out (not a soft lock) at
`Auth:IdleTimeoutMinutes` (default 30). Per FR-008 the in-flight form
state is persisted to localStorage namespaced by `oid` so the user
can recover on next sign-in.

### 3. Tenant picker + remember-on-device (US3)

After auth, users with ≥ 2 memberships (or CSP-Admins) see a picker.
"Remember on this device" issues a first-party HMAC-SHA256-signed
cookie (`ato-remembered-tenant`) valid for
`Auth:RememberTenantCookieDays` (default 30). No server-side preference
flag; clearing browser cookies forgets the choice.

### 4. Per-class error pages (US4)

10 error classes — `NoCardInserted`, `CertExpired`, `CertNotYetValid`,
`CertRevoked`, `ClockSkew`, `NoTenantAssignment`, `AccountDisabled`,
`MfaFailure`, `ConditionalAccessBlock`, `NetworkFailure`. Each renders
its own copy from `errorCopy.ts` with the recovery action.

### 5. Audit + throttle + cold archive (US10)

- `LoginAuditEvents` table (`AtoCopilotContext` DbSet) stamped per
  request via `ILoginAuditService.AppendAsync`.
- Per-IP + per-identity throttle backed by `IDistributedCache` (Redis
  in prod, in-memory in dev/test). Defaults: Production 20/min/IP +
  10/min/identity; Development 100/min for both. Per analysis C11 any
  non-`Development` env (Staging, Production, Testing, ...) uses the
  Production bucket — no silent dev-threshold inheritance.
- `LoginThrottleMiddleware` (Phase 13.1) wraps `/api/auth/*`. Peek →
  short-circuit 429 + `Retry-After` BEFORE the request runs.
  Register-after on 401 / 403 NO_TENANT_ASSIGNMENT only (analysis C17).
- `LoginAuditArchiveService` hosted service drains rows older than 13
  months to an immutable cold archive (`AzureBlobAppendArchiveSink` in
  production, `FileSystemArchiveSink` in dev under
  `archive/LoginAuditEvents/{yyyy}/{MM}/`).

## How to enable simulation mode in dev

Three layers (per analysis C10) MUST all be true:

1. `ASPNETCORE_ENVIRONMENT=Development`
2. `CacAuth:SimulationMode = true`
3. `CacAuth:SimulatedIdentities` is non-empty

```jsonc
// src/Ato.Copilot.Mcp/appsettings.Development.json
{
  "CacAuth": {
    "SimulationMode": true,
    "SimulatedIdentities": [
      {
        "IdentityId": "dev-cspadmin",
        "DisplayName": "Dev CSP-Admin",
        "Persona": "CspAdmin",
        "Oid": "00000000-0000-0000-0000-000000000010",
        "Tid": "11111111-1111-1111-1111-111111111111",
        "TenantId": "11111111-1111-1111-1111-111111111111",
        "Roles": ["CSP.Admin"]
      }
    ]
  }
}
```

The dashboard shows a "Developer simulation" panel on `/login`
listing each identity. Picking one POSTs to `/api/auth/simulate` and
the server issues an `ato-simulation` cookie + a discrete
`X-Simulated=true` sentinel cookie. Evidence generated by a simulated
session is auto-flagged `IsSimulation=true` and excluded from real
RMF bundles (per Feature 027).

In any non-Development environment the panel is hidden by the server
(login-config descriptor is `null`) AND `POST /api/auth/simulate`
returns `404 NotFound` (not 403) AND a `SimulationBlocked` audit row
is written.

## How to test the cross-tab login race

The MSAL.js cache lives in `localStorage`. The
`useLoginRaceListener` hook subscribes to `window.storage` events on
the MSAL cache key. When one tab completes a login, the storage event
fires in every other same-origin tab; the listener completes the
waiting tab's deep-link redirect without forcing a second login.

1. Open `/dashboard/systems/abc-123` in two browser tabs while signed
   out. Both redirect to `/login?return=...`.
2. Sign in in tab A. The MSAL cache is written.
3. Tab B's storage event fires → the React Router boundary resolves
   the session → tab B navigates to its captured `return` URL with no
   second login.

## How to deploy

The default `docker-compose.mcp.yml` bundles a Redis service for the
throttle counter. In production, point `ConnectionStrings:Redis` at
your Azure Managed Redis instance. Set `Auth:Cookie:SigningKey` from
Azure Key Vault — the startup options validator fails fast outside
`Development` if the key is missing.

## Related features

- Feature 003 — CAC/PIV authentication + PIM role activation
- Feature 015 — Persona workflows (post-login landing page resolver)
- Feature 027 — CAC simulation mode (single-identity-per-startup; US7
  extends this to a curated list)
- Feature 034 — Dashboard chat (localStorage keyed by `oid`; cleared
  on sign-out)
- Feature 048 — Tenant isolation + CSP-Admin impersonation
