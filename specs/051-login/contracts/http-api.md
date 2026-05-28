# Phase 1 — HTTP API Contract: Login Endpoints

**Feature**: 051-login
**Plan**: [../plan.md](../plan.md)
**Spec**: [../spec.md](../spec.md)
**Data model**: [../data-model.md](../data-model.md)
**Date**: 2026-05-28

This document pins the wire contract for the **five new HTTP endpoints**
introduced under `/api/auth/`. All endpoints live in
`src/Ato.Copilot.Mcp/Endpoints/Auth/AuthEndpoints.cs` and are registered
in `Program.cs` after the existing `CacAuthenticationMiddleware`.

All endpoints emit the standard ATO Copilot envelope from
[Constitution § User Experience Standards](../../../.specify/memory/constitution.md):

```jsonc
{
  "status": "success" | "error",
  "data": <payload>,
  "metadata": { "executionTimeMs": <number>, "correlationId": "<string>" },
  "error": { "errorCode": "<string>", "message": "<string>", "suggestion": "<string>?" }
}
```

## 1. `GET /api/auth/login-config` — public, no auth required

### 1.1 Purpose

Return the deployment's login configuration so the SPA can render the
branded `/login` page. Public because the SPA reaches this BEFORE any
authentication occurs (FR-001 / FR-002 / FR-003).

### 1.2 Request

`GET /api/auth/login-config`

No query parameters. No request body.

### 1.3 Behavior

1. Read `IOptions<AuthOptions>` + the configured deployment branding
   (sourced from Feature 048 deployment config service).
2. Determine whether the simulation panel descriptor should be included:
   `ASPNETCORE_ENVIRONMENT == "Development"` AND
   `CacAuth:SimulationMode == true` AND
   `CacAuth:SimulatedIdentities` is non-empty (per FR-023).
3. Build a flat config object with branding + auth-method descriptors +
   optional simulation panel descriptor.
4. Return 200 with the config.

### 1.4 Response — 200 OK

```jsonc
{
  "status": "success",
  "data": {
    "branding": {
      "deploymentName": "Coastal Watch — ATO Copilot",
      "logoUrl": "/branding/coastal-watch-logo.svg",     // null → SPA falls back to default
      "supportEmail": "ato-support@coastal-watch.gov"
    },
    "defaultMethod": "Cac",                              // "Cac" | "Entra"
    "enabledMethods": [
      { "id": "Cac",   "displayName": "Sign in with CAC/PIV" },
      { "id": "Entra", "displayName": "Sign in with Microsoft" }
      // Simulation descriptor appended only in Development per FR-023.
    ],
    "cloud": "AzureUSGovernment",                        // "AzurePublic" | "AzureUSGovernment"
    "idleTimeoutMinutes": 30,
    "rememberTenantCookieDays": 30,
    "simulation": null,                                  // see § 1.5 for non-null shape
    "msal": {
      "clientId": "<entra-client-id>",
      "authority": "https://login.microsoftonline.us/<tenant-id>",
      "redirectUri": "https://copilot.example/login/callback",
      "postLogoutRedirectUri": "https://copilot.example/login?reason=signed_out"
    }
  },
  "metadata": { "executionTimeMs": 12, "correlationId": "..." }
}
```

### 1.5 Simulation panel descriptor (Development only)

When the simulation panel applies, `data.simulation` is non-null:

```jsonc
{
  "simulation": {
    "identities": [
      { "id": "dev-cspadmin", "displayName": "Dev CSP-Admin", "persona": "CspAdmin", "tenantId": "...", "roles": ["CSP.Admin"] },
      { "id": "dev-isso",     "displayName": "Dev ISSO",      "persona": "Isso",     "tenantId": "...", "roles": ["ISSO"] }
    ]
  }
}
```

When `data.simulation == null`, the SPA route guard MUST refuse to mount
`SimulationPanel.tsx` even if a query parameter or local debug flag tries
to force-render it (FR-023 three-layer gate).

### 1.6 Error responses

| Status | Code | Trigger |
|---|---|---|
| 503 | `MULTI_TENANT_DISABLED` | Single-tenant deployment short-circuit (matches existing CSP endpoint pattern). |
| 500 | `INTERNAL_ERROR` | Branding config read failure — SPA falls back to defaults; this case is logged but rare. |

### 1.7 Cache-Control

`Cache-Control: no-store` — branding can change on deploy and the
simulation descriptor MUST NOT be cached across environments.

## 2. `GET /api/auth/me` — bearer-authenticated

### 2.1 Purpose

Return the authenticated user's identity, persona, home tenant, active
PIM roles, and impersonation state for the account menu (US9 / FR-030).

### 2.2 Request

`GET /api/auth/me`
`Authorization: Bearer ...`

### 2.3 Behavior

1. Existing `CacAuthenticationMiddleware` validates the bearer.
2. Resolve the user's `oid`, `tid` from claims.
3. Look up the user's home tenant via the existing `Tenants` table.
4. Read the active PIM role (existing Feature 003 path).
5. Read the impersonation cookie via existing Feature 048
   helper (production cookie name is `ato-impersonate` per
   `TenantImpersonationService`; spec previously said
   `X-Impersonated-Tenant` — corrected 2026-05-28 during Phase 3
   implementation to match production reality), if present.
6. Emit a `LoginAuditEvent` of type `LoginSuccess` (debounced — at most
   once per session per tenant; tracked via `IDistributedCache` with a
   5-minute TTL keyed on `oid + tenantId`).

### 2.4 Response — 200 OK

```jsonc
{
  "status": "success",
  "data": {
    "oid": "...",
    "displayName": "Jane Spinella",
    "persona": "CspAdmin",
    "homeTenant": { "id": "...", "displayName": "Coastal Watch", "status": "Active" },
    "effectiveTenant": { "id": "...", "displayName": "...", "status": "Active" },
    "isImpersonating": false,
    "impersonation": null,                              // see § 2.5 for non-null shape
    "pimRoles": [
      { "name": "ISSO", "expiresAt": "2026-05-28T17:30:00Z" }
    ],
    "isCspAdmin": true,
    "isSocAnalyst": false
  },
  "metadata": { "executionTimeMs": 18, "correlationId": "..." }
}
```

### 2.5 Impersonation state (when active)

```jsonc
{
  "impersonation": {
    "impersonatedTenant": { "id": "...", "displayName": "T-Eagle", "status": "Active" },
    "startedAt": "2026-05-28T15:00:00Z",
    "expiresAt": "2026-05-28T16:00:00Z"
  }
}
```

### 2.6 Error responses

| Status | Code | Trigger |
|---|---|---|
| 401 | `UNAUTHORIZED` | Missing / invalid bearer. |
| 403 | `NO_TENANT_ASSIGNMENT` | Authenticated identity has no `Tenants` row (FR-015). Triggers a `LoginFailure(NoTenantAssignment)` audit row with `EffectiveTenantId = SYSTEM_TENANT_ID`. |

## 3. `POST /api/auth/signout` — bearer-authenticated

### 3.1 Purpose

Revoke the current session and emit a `SignOut` audit row.

### 3.2 Request

`POST /api/auth/signout`
`Authorization: Bearer ...`

Optional body: `{ "reason": "manual" | "idle_timeout" }` (default
`"manual"`; the SPA sends `"idle_timeout"` from `useIdleTimer.ts`).

### 3.3 Behavior

1. Validate the bearer.
2. Revoke the MSAL cache for the user (best-effort).
3. Delete the impersonation cookie (`ato-impersonate` per
   `TenantImpersonationService`) if present (FR-006).
4. Emit a `SignOut` or `IdleSignOut` audit row depending on body.
5. Return 204.

### 3.4 Response — 204 No Content

No body. The SPA clears its caches and redirects per FR-006 / FR-007.

### 3.5 Error responses

| Status | Code | Trigger |
|---|---|---|
| 401 | `UNAUTHORIZED` | Missing / invalid bearer. |

## 4. `POST /api/auth/select-tenant` — bearer-authenticated

### 4.1 Purpose

Lock the session scope to a tenant the user chose in the picker (US3 /
FR-009) and optionally set the device-only "remember" cookie (FR-012).

### 4.2 Request

`POST /api/auth/select-tenant`
`Authorization: Bearer ...`
`Content-Type: application/json`

```jsonc
{
  "tenantId": "...",
  "remember": false                                     // optional, default false
}
```

### 4.3 Behavior

1. Validate the bearer.
2. Verify the user is a member of the named tenant AND the tenant is
   `Active` or `Suspended` (NOT `Disabled` unless caller is CSP-Admin
   per FR-010).
3. Set the server-side session scope.
4. If `remember = true`, issue the HMAC-signed
   `RememberedTenantCookie` (R8) via `IRememberedTenantCookieService.Issue`.
5. Emit a `TenantSwitch` audit row.
6. Return 204.

### 4.4 Response — 204 No Content

A response cookie (`ato-remembered-tenant`) is set when `remember = true`.

### 4.5 Error responses

| Status | Code | Trigger |
|---|---|---|
| 400 | `VALIDATION_FAILED` | Missing / malformed `tenantId`. |
| 403 | `FORBIDDEN_NOT_TENANT_MEMBER` | User has no membership in the named tenant (and is not CSP-Admin). |
| 404 | `TENANT_NOT_FOUND` | Tenant id does not exist. |
| 409 | `TENANT_DISABLED` | Tenant is `Disabled` and caller is not CSP-Admin. |
| 423 | `TENANT_SUSPENDED_READ_ONLY` | NOT raised by this endpoint — `Suspended` tenants are pickable; the 423 surfaces on subsequent write requests per Feature 048. (Documented here for completeness.) |

## 5. `POST /api/auth/simulate` — Development-only

### 5.1 Purpose

Issue a simulated session for a developer (US7 / FR-024 / FR-025). Hard-
gated to `ASPNETCORE_ENVIRONMENT == "Development"`.

### 5.2 Request

`POST /api/auth/simulate?identityId=<key>`

No body; the `identityId` query parameter selects an entry from
`CacAuth:SimulatedIdentities`.

### 5.3 Behavior

1. **Environment gate**: if `ASPNETCORE_ENVIRONMENT != "Development"`,
   return `404 NOT_FOUND` (pretend the route doesn't exist) AND emit a
   `SimulationBlocked` audit row with `severity=Security` per FR-024.
2. Look up the identity descriptor in `CacAuth:SimulatedIdentities`.
   404 if not found.
3. Issue the normal session cookie with the configured oid / tid /
   tenant / roles for the simulated identity.
4. **Additionally** issue a discrete sentinel cookie named `X-Simulated`
   with value `true` per FR-025 (clarified 2026-05-28 — see spec.md
   analysis C9). Cookie attributes: `HttpOnly=true`, `Secure=true`,
   `SameSite=Strict`, `Path=/`, lifetime tied to the session cookie. The
   SPA and downstream evidence-generation services check the presence of
   this cookie to set `IsSimulation=true` on persisted artifacts per
   Feature 027.
5. Emit a `SimulatedLogin` audit row.
6. Return 204.

### 5.4 Response — 204 No Content

The session cookie AND the `X-Simulated=true` sentinel cookie are set on
the response. The SPA then calls `GET /api/auth/me` to render the
dashboard as that identity.

### 5.5 Error responses

| Status | Code | Trigger |
|---|---|---|
| 404 | (no body) | NOT `404 NOT_FOUND` envelope — a bare 404 so the route looks non-existent to any caller in non-Development. |
| 404 | `SIMULATED_IDENTITY_NOT_FOUND` | Development environment; identityId not in config. |

## 6. Throttling envelope (FR-034 / FR-035)

When `LoginThrottleMiddleware` decides a request exceeds the env-specific
threshold (per [research.md § R7](../research.md)), the response from ANY
auth-gated endpoint becomes:

`HTTP/1.1 429 Too Many Requests`
`Retry-After: <seconds-until-bucket-reset>`

```jsonc
{
  "status": "error",
  "error": {
    "errorCode": "TOO_MANY_LOGINS",
    "message": "Too many sign-in attempts. Try again in {N} minutes.",
    "suggestion": "Wait the suggested time, or contact your administrator if the issue persists."
  },
  "metadata": { "executionTimeMs": 3, "correlationId": "..." }
}
```

The throttle handler also emits one `LoginFailure` audit row per
throttled attempt with `MetadataJson = { "throttled": true, "retryAfterSeconds": <int> }`.

## 7. `GET /api/auth/events` — bearer-authenticated

### 7.1 Purpose

Paginated read of `LoginAuditEvent` rows for the SOC analyst and tenant
member personas (US10 / FR-032 / FR-036a). Two query modes are
multiplexed through the `?systemTenant=` flag:

- **Per-tenant (default)** — returns rows owned by the active tenant
  scope (after impersonation and remember-cookie resolution). The
  automatic tenant query filter on `LoginAuditEvent` guarantees
  cross-tenant isolation regardless of caller intent.
- **SYSTEM_TENANT_ID forensic** — `?systemTenant=true` invokes the
  SOC-analyst read path which `IgnoreQueryFilters()` ONLY to surface
  rows where `EffectiveTenantId = Guid.Empty` (pre-session failures,
  `NoTenantAssignment` events, etc.). Gated by the `Auth.SocAnalyst`
  role claim per [research.md § R9](../research.md).

### 7.2 Request

`GET /api/auth/events?since=<ISO-8601>&take=<int>&systemTenant=<bool>`
`Authorization: Bearer ...`

| Query | Type | Default | Notes |
|---|---|---|---|
| `since` | ISO-8601 datetime | `null` (no lower bound) | Excludes rows with `OccurredAt < since`. |
| `take` | integer 1..1000 | `100` | Caps the response. Values ≤ 0 or > 1000 are coerced to the default / cap. |
| `systemTenant` | boolean | `false` | When `true`, switches to the SOC-analyst read path. |

### 7.3 Behavior

1. Validate the bearer; reject 401 if unauthenticated.
2. If `systemTenant=true`, call `ILoginAuditService.ListSystemTenantAsync`
   which enforces the `Auth.SocAnalyst` claim. On
   `UnauthorizedAccessException`, return 403
   `FORBIDDEN_NOT_SOC_ANALYST`.
3. Otherwise call `ILoginAuditService.ListAsync(activeTenantId, since, take)`.
4. Project each `LoginAuditEvent` into the wire shape below.
5. Return 200 with the envelope.

### 7.4 Response — 200 OK

```jsonc
{
  "status": "success",
  "data": {
    "events": [
      {
        "id": "...",                                  // Guid
        "eventType": "LoginSuccess",                  // see LoginAuditEventType
        "oid": "...",                                 // nullable
        "tid": "...",                                 // nullable
        "effectiveTenantId": "...",                   // Guid; Guid.Empty for system-tenant rows
        "correlationId": "...",
        "sourceIp": "10.0.0.1",
        "userAgent": "Mozilla/5.0",
        "surface": "Dashboard",                       // see LoginSurface
        "occurredAt": "2026-05-28T22:15:32Z",
        "errorClass": null,                           // see LoginErrorClass, set only for LoginFailure
        "metadataJson": null                          // see data-model.md § 1.5
      }
    ],
    "count": 1,                                       // events.length, for telemetry
    "systemTenant": false                             // echoes the requested mode
  },
  "metadata": { "executionTimeMs": 14, "correlationId": "..." }
}
```

### 7.5 Error responses

| Status | Code | Trigger |
|---|---|---|
| 401 | `UNAUTHORIZED` | Missing / invalid bearer. |
| 403 | `FORBIDDEN_NOT_SOC_ANALYST` | `?systemTenant=true` AND caller lacks the `Auth.SocAnalyst` role claim. |

## 8. Cross-reference matrix

| FR | Endpoint section |
|---|---|
| FR-001 / FR-002 / FR-003 / FR-004 | § 1 |
| FR-005 / FR-006 | § 3 |
| FR-007 / FR-007a | § 3 (the `reason=idle_timeout` body) + [contracts/frontend-types.md](./frontend-types.md) for the SPA half |
| FR-009 – FR-013 | § 4 |
| FR-014 – FR-016 | § 1.4 surface + [contracts/frontend-types.md](./frontend-types.md) for the SPA error pages |
| FR-023 – FR-025 | § 1.5 + § 5 (three-layer gate per [research.md § R-Summary item 4](../research.md)) |
| FR-026 – FR-029 | § 2.5 + Feature 048 impersonation endpoint (out of scope here) |
| FR-030 / FR-031 | § 2 |
| FR-032 / FR-033 / FR-034 / FR-035 | § 6 + every endpoint's audit-row contract |
| FR-032 / FR-036 / FR-036a (SOC read path) | § 7 |
| FR-038 | All endpoints — Serilog logging discipline (no token / refresh-token / thumbprint in log fields) |
