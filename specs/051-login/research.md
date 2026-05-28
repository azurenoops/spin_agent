# Phase 0 — Research: First-Class Login Experience

**Feature**: 051-login
**Plan**: [plan.md](./plan.md)
**Spec**: [spec.md](./spec.md)
**Date**: 2026-05-28

This document records the 12 design decisions made before any Phase 1
artifact is written. Each decision follows the structure:

- **Decision**: the chosen path
- **Rationale**: why this path satisfies the spec + Constitution
- **Alternatives considered**: what we evaluated and rejected

## R1. MSAL.js wiring strategy for the existing dashboard SPA

The dashboard today has **no** `@azure/msal-react` dependency; it uses an
axios `interceptors.request.use` callback that reads a bearer token from
`localStorage.getItem('auth_token')` (verified via grep at
`src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/api.ts:140`).
That token landed there because the browser silently negotiates with Entra
in dev / docker-compose mode and the SPA snagged the resulting JWT — that
is exactly the "blank page, browser auth dialog" complaint the source issue
opens with.

**Decision**: Introduce `@azure/msal-browser` 3.x + `@azure/msal-react` 2.x
as the canonical client-side token holder. One global
`PublicClientApplication` instance is created in `main.tsx` and passed into
`<MsalProvider>` at the root of the React tree. The existing
`localStorage.getItem('auth_token')` shortcut in every `*/api.ts` becomes a
dashboard-wide axios interceptor (`src/features/auth/interceptors.ts`) that:

1. **Request side**: calls `instance.acquireTokenSilent({ scopes })` and
   sets `Authorization: Bearer ${token}` on every outbound request.
2. **Response side**: on `401`, calls `acquireTokenSilent()` once and
   retries the original request. On a second failure, calls
   `instance.loginRedirect({ ..., state: window.location.pathname + ... })`
   to preserve the deep link per FR-001 / FR-004.

**Rationale**:

- Matches the Q4 clarification: MSAL.js owns silent refresh, the SPA has
  no bespoke refresh-token storage.
- Eliminates the cross-feature copy-paste of `localStorage.getItem('auth_token')`
  in every API helper (today: 14 grep hits across `src/features/`).
- Aligns with the source issue's repeated reference to MSAL semantics and
  with Feature 003's existing Azure AD options binding on the server side.

**Alternatives considered**:

- *Roll our own wrapper around `acquireTokenSilent`.* Rejected because it
  duplicates what `@azure/msal-react` already provides (React-aware
  context, the `useMsal` hook, `useIsAuthenticated`) and would still
  require us to ship the MSAL.js bundle anyway.
- *Server-side cookie session (BFF pattern).* Rejected for being a
  larger architectural change than this feature needs, requiring a new
  server-side refresh endpoint, and conflicting with the Q4 clarification.
- *Keep `localStorage.getItem('auth_token')` and add a refresh endpoint.*
  Rejected for the same reason — Q4 explicitly removed the bespoke
  refresh-token path.

## R2. Distributed-cache provider selection for the throttle counter

FR-036 mandates the failed-login throttle counter MUST survive a process
restart. The repo already runs Redis 7.4 in `docker-compose.mcp.yml`
(verified 2026-05-28 — a local `stark-redis` container exists from a sibling
workspace but is NOT part of `docker-compose.mcp.yml`; T006 added a proper
`redis` service to this compose so the dependency is self-contained), and prod
deployments already use Azure Cache for Redis.

**Decision**: Bind `IDistributedCache` to
`Microsoft.Extensions.Caching.StackExchangeRedis` in production and
`IDistributedMemoryCache` (in-process) in unit tests + the SQLite-backed
integration test fixture. The dev `docker-compose.mcp.yml` is updated
to point the MCP container at the new in-compose `redis` instance.

**Rationale**:

- Redis is already deployed; no new infrastructure component.
- `IDistributedCache` is the .NET 9 canonical abstraction for this exact
  use case and is interceptable in tests via `IDistributedMemoryCache`.
- TTLs on each throttle entry (60 s) keep the working set bounded under
  attack scenarios.

**Alternatives considered**:

- *In-memory `IMemoryCache`.* Rejected — fails FR-036 (process restart =
  attacker bypass).
- *SQL Server-backed `Microsoft.Extensions.Caching.SqlServer`.* Rejected
  — adds DB write latency (~30–50 ms) to every failed login, blowing the
  5 ms throttle-decision SLA, and SQL Server is a poor fit for high-
  cardinality TTL-keyed counters.
- *Azure Storage table with TTL.* Rejected — same latency story plus an
  unnecessary new resource type.

## R3. Cold-archive sink topology

FR-036a requires rows > 13 months migrate to an immutable cold archive.
SOC tooling must still be able to query the archive. Production runs in
AzureUSGovernment; dev runs locally.

**Decision**: Introduce a thin `ILoginAuditArchiveSink` interface with two
implementations:

- `AzureBlobAppendArchiveSink` — writes one append-blob per
  `{yyyy}/{MM}/login-audit.jsonl` in the `audit-archive` container of the
  deployment's primary AzureUSGovernment storage account. The container
  is configured with **immutable storage with time-based retention** so
  rows cannot be edited or deleted by any role short of subscription
  owner.
- `FileSystemArchiveSink` — writes one JSON-Lines file per
  `archive/LoginAuditEvents/{yyyy}/{MM}/login-audit.jsonl` under the
  process's working directory. Used in dev and in CI tests.

The `LoginAuditArchiveService` (`IHostedService`) wakes once per 24 hours
at low-traffic hours (`02:00 UTC`, configurable), picks the sink via DI
(`Auth:Archive:Sink = AzureBlobAppend | FileSystem`), batches rows in
1,000-row chunks, writes one chunk per `WriteBatchAsync` call, and only
deletes from `LoginAuditEvents` after the sink reports success (acks the
batch by returning the archive blob URL or filename).

**Rationale**:

- Append-blob with immutable storage matches NIST 800-53 AU-9 (3) Audit
  Records Protection From Unauthorized Modification.
- Two-sink design keeps dev local (no Azurite required, no cloud
  credentials in CI) without introducing a third "mock" abstraction.
- 24-hour cadence at 02:00 UTC matches the cold archive's eventual-
  consistency expectations and stays well clear of SOC peak-query hours.

**Alternatives considered**:

- *Single sink with Azurite in dev.* Rejected — introduces a Docker
  dependency in unit tests; CI doesn't have Azurite today.
- *Hot-table only with manual purge.* Rejected — fails FR-036a, requires
  manual ops intervention, and contradicts AU-11 retention.
- *Stream events directly to SIEM (e.g., Sentinel) and skip the hot
  table.* Rejected — Sentinel integration is its own feature; SOC tooling
  also needs same-day forensic queries that don't depend on the SIEM
  pipeline.

## R4. Bot Framework SSO / OAuthPrompt branching

US6 / FR-021 (post-Q1) requires the bot use Bot Framework SSO when the
Teams manifest supports it, and fall back to OAuthPrompt otherwise.
`Auth:TeamsSso:Mode` defaults to `Optional` per Q1.

**Decision**: At first `@mention` from an unlinked user:

1. The bot inspects the incoming activity's `channelData.tenant.id` and
   the configured `Auth:TeamsSso:Mode`.
2. If `Mode = Disabled`, run `OAuthPrompt` unconditionally.
3. If `Mode = Required` and the manifest does NOT advertise SSO
   (`webApplicationInfo.id` missing or empty), startup MUST have already
   failed via the `IValidateOptions<AuthOptions>` validator — so this
   branch is unreachable in a correctly-configured deployment.
4. If `Mode = Required` and the manifest DOES advertise SSO, call
   `getUserToken` against the configured connection name.
5. If `Mode = Optional` (default), attempt `getUserToken` once; on a
   non-token result (Bot Framework returns `null` for tenants without
   SSO consent), fall back to `OAuthPrompt`.

**Rationale**:

- Matches Q1 / FR-021 literally.
- The unreachable-branch case in step 3 is a Constitution § Security
  defense-in-depth: startup validation catches misconfiguration before a
  Teams user sees a broken card.

**Alternatives considered**:

- *Always OAuthPrompt.* Rejected — Q1 says `Optional` (not Disabled) so
  SSO MUST be attempted when available.
- *Per-tenant override.* Rejected by Q1 — deployment-wide only.

## R5. Schema-additions vs `dotnet ef`

The repo's EF model snapshot has drifted across Features 045–050. Feature
050 hit this exact issue and resolved it with an idempotent
`EnsureSchemaAdditions` module rather than `dotnet ef migrations add`.
Feature 051 introduces one new table (`LoginAuditEvents`).

**Decision**: Ship a new
`Ato.Copilot.Core/Data/Migrations/EnsureSchemaAdditions/LoginAuditEventsSchemaAdditions.cs`
that idempotently creates the `LoginAuditEvents` table + composite index
+ `Cascade` FK to `Tenants` on both SqlServer and SQLite providers.
Invoked from `Program.cs`'s `EnsureSchemaAdditionsAsync` after the
Feature 050 `CapabilityHistoryEventsSchemaAdditions` call.

**Rationale**:

- Matches the established Feature 050 pattern (T009 deviation note in
  `specs/050-csp-capability-lifecycle/tasks.md`).
- Avoids a multi-hundred-line migration cascade that would emit unrelated
  table mutations from the drifted snapshot.

**Alternatives considered**:

- *Run `dotnet ef migrations add` and accept the cascade.* Rejected — would
  modify dozens of tables outside Feature 051's scope.
- *Manually hand-write a migration containing only the new table.*
  Rejected — duplicates the idempotency logic the schema-additions
  modules already encapsulate and creates a future
  `dotnet ef migrations add` divergence-on-divergence risk.

## R6. Test strategy and pyramid

Per Constitution § VI, every new code path opens with a failing AAA test.
Spec has 10 user stories, 50 acceptance scenarios, 11 edge cases, and 40
FRs (FR-001 through FR-039 plus FR-007a and FR-036a). Coverage target:
unit + integration ≥ 80% on modified paths; ≥ 100% on
`LoginAuditService.AppendAsync`, `LoginThrottleService.RegisterAttemptAsync`,
`RememberedTenantCookieService.Issue` / `Validate`.

**Decision**: Four-layer pyramid:

| Layer | Project | Approx. test count | Drives |
|---|---|---|---|
| C# unit | `Ato.Copilot.Tests.Unit/Auth/` | ~30 | `LoginAuditService`, `LoginThrottleService`, `RememberedTenantCookieService`, `LoginAuditArchiveService`, simulation-list parsing |
| C# integration | `Ato.Copilot.Tests.Integration/Auth/` | ~20 | All 5 endpoints' happy + error paths via `WebApplicationFactory`; simulation gate at 3 layers; throttle 429 + Retry-After |
| TS component | `src/Ato.Copilot.Dashboard/src/__tests__/auth/` | ~15 | `LoginPage`, `TenantPickerPage`, `ErrorPage`, `AccountMenu`, `ImpersonationBanner`, `SimulationPanel`, `useIdleTimer`, `useLoginRaceListener`, `interceptors` |
| Manual | quickstart.md | 1 per US | End-to-end scenarios against the local docker stack |

VS Code + M365 extension tests reuse the existing mocha / jest setups in
those folders; they add ~5 tests each but do NOT block the dashboard
suite.

**Rationale**:

- AAA enforcement keeps each test readable and intention-explicit per the
  Constitution.
- Distributing tests across the 4 layers means a failure points directly
  at the offending layer.

**Alternatives considered**:

- *Heavy integration, no unit tests.* Rejected — Constitution §IV (SRP)
  requires every service to be unit-testable in isolation.
- *Property-based tests via FsCheck.* Rejected as overkill for endpoint
  contracts; throttle bucket-math is the only candidate and the per-IP /
  per-identity tests give equivalent confidence at 1/10 the maintenance
  cost.

## R7. Throttle bucket-key design

FR-034: per-IP AND per-identity, env-specific thresholds. 60-second
sliding window.

**Decision**: Two Redis keys per attempt:

- `login-throttle:ip:{ip}:{minute-bucket}` — value: count; TTL: 60 s.
- `login-throttle:identity:{oid-or-tid-or-anonymous}:{minute-bucket}` —
  same.

Where `{minute-bucket}` is `UTC-now / 60` (integer minute), and the key
encodes the bucket so two attempts at the boundary land in different
keys (no synchronization needed). `RegisterAttemptAsync` does:

1. `IncrementAsync(ip-key)` and `IncrementAsync(identity-key)` in parallel.
2. If either returns a value > the env-specific threshold, return
   `Throttled(retryAfter = (60 - secondsInCurrentMinute) seconds)`.
3. Otherwise, return `Allowed`.

The "identity" key uses `oid` if Entra issued one, else `tid:{tid}` (so a
flood of failed sign-ins from a single Entra directory still throttles),
else `anonymous` (so a flood from a non-Entra source still throttles
per-IP).

**Rationale**:

- Two-key design naturally satisfies both FR-034 dimensions without a
  composite key.
- Minute-bucket prevents sliding-window race conditions (no lock needed).
- The Allowed / Throttled return tuple is the SRP boundary — the caller
  decides HTTP status code.

**Alternatives considered**:

- *Single composite key `(ip+identity)`.* Rejected — a single attacker IP
  rotating across many identities defeats the limit.
- *Hash-based sliding window via Redis sorted-set.* Rejected — adds
  complexity for a marginal gain over the bucket approach; the spec's
  Retry-After is "minutes", not seconds-precise.

## R8. `RememberedTenantCookie` signing primitive

FR-012: HMAC-signed first-party cookie. No server-side mirror.

**Decision**: HMAC-SHA256 with a 32-byte key drawn from Key Vault under
`Auth:Cookie:SigningKey`. Cookie payload format:

```text
{base64url(tenantIdGuidBytes)}.{base64url(iatMillisecondsBytes)}.{base64url(expMillisecondsBytes)}.{base64url(hmacBytes)}
```

The HMAC is over `tenantId || iat || exp` (concatenated raw bytes). Cookie
attributes: `HttpOnly=false` (the SPA reads it via `js-cookie`),
`Secure=true`, `SameSite=Strict`, `Path=/`, `Domain=<deployment-domain>`,
`Max-Age=Auth:RememberTenantCookieDays * 86400`.

Validation rejects any cookie whose HMAC fails OR whose `exp` is in the
past OR whose `tenantId` does not match an `Active` or `Suspended` tenant
in `Tenants`.

**Rationale**:

- HMAC-SHA256 is the canonical primitive for first-party signed cookies;
  no certificate management overhead.
- Stateless: rotating the signing key invalidates all outstanding cookies
  in O(1) operator time, fitting the "remember on device only" Q3 scope.

**Alternatives considered**:

- *AES-256-GCM encrypted cookie.* Rejected — adds confidentiality that
  isn't needed (the tenantId is not sensitive; it appears in the URL
  bar after the user lands on the dashboard).
- *Browser SubtleCrypto signing.* Rejected — leaks the key to JS.

## R9. `LoginAuditEvent` tenant ownership for forensic queries

Per Q2, pre-session and `NoTenantAssignment` rows belong to
`SYSTEM_TENANT_ID`. Forensic queries need to surface these rows to SOC
tooling without exposing them to every CSP-Admin.

**Decision**: `LoginAuditEvent` is `[TenantScoped]` so the automatic
query filter in `AtoCopilotContext.OnModelCreating` applies. SOC tooling
queries via a dedicated `ListAsync(tenantId = SYSTEM_TENANT_ID, ...)`
method that requires the `Auth.SocAnalyst` role claim (a new claim mapped
in Feature 003's `RoleClaimMappings:Auth.SocAnalyst` config). Without
that claim, calling `ListAsync` with `SYSTEM_TENANT_ID` returns `403`.

**Rationale**:

- Matches Q2 + the existing Feature 048 tenant-isolation pattern.
- Tying SystemTenant reads to a dedicated claim satisfies Zero-Trust
  "Least Privilege".

**Alternatives considered**:

- *Make pre-session rows globally readable.* Rejected — would expose
  failed-login attempt patterns across the whole deployment to every
  CSP-Admin.
- *A separate `system_audit_events` table.* Rejected — two tables for
  the same shape adds query complexity and breaks the single-`AppendAsync`
  contract.

## R10. Idle-timer JS event source

FR-007: track user activity (mouse / keyboard / touch / API success).
FR-007a: silent token renewal MUST NOT reset the idle counter.

**Decision**: A custom React hook `useIdleTimer(timeoutMinutes)`
subscribes to:

- `document.addEventListener('mousemove', ...)`
- `document.addEventListener('keydown', ...)`
- `document.addEventListener('touchstart', ...)`
- `document.addEventListener('click', ...)`
- A custom event `'ato:user-input'` that the axios interceptor fires on
  every **2xx** response (proxy for "user-initiated work in flight") —
  but explicitly NOT on `401` retry success (which is silent renewal,
  not user input).

A single chained `setTimeout(handler, timeoutMs)` reschedules itself on
every activity event; on expiry it calls the sign-out path. No
`setInterval`.

**Rationale**:

- Chained `setTimeout` reschedules at the next event, never both
  "armed" and "fired" simultaneously.
- Distinguishing user-input from silent-renewal at the interceptor layer
  satisfies FR-007a without a global "is this a silent renewal" flag.

**Alternatives considered**:

- *`setInterval` polling.* Rejected — fires even when the tab is
  backgrounded (Page Visibility API throttling helps but isn't
  guaranteed).
- *`IdleCallback` (`requestIdleCallback`).* Rejected — semantically the
  opposite (it fires when the browser IS idle, not the user).

## R11. Storage-event vs BroadcastChannel for login race

Q5 pinned `window.addEventListener('storage', ...)` on the MSAL.js cache
key.

**Decision**: Storage event only. The waiting tab listens via
`window.addEventListener('storage', e => e.key?.startsWith('msal.account.keys'))`
(MSAL.js writes under the `msal.account.keys` prefix). On match, it
reads `useMsal().instance.getAllAccounts()` and, if non-empty, completes
the deep-link redirect.

**Rationale**:

- Q5 explicitly picked Option A.
- Storage events are well-supported across Chromium / Firefox / Safari
  for same-origin same-domain communication.

**Alternatives considered (per Q5 closing)**:

- *BroadcastChannel API.* Rejected by Q5 simplicity argument — MSAL.js's
  localStorage write fires a storage event for free; BroadcastChannel
  would require us to wrap MSAL's cache layer.
- *Polling + focus event.* Rejected by Q5.

## R12. Teams SSO mode enforcement at startup

FR-021 (post-Q1): `Required` mode MUST fail startup if the Teams manifest
does not advertise SSO.

**Decision**: An `IValidateOptions<AuthOptions>` implementation reads
the deployed Teams manifest at startup (path:
`extensions/m365/manifest/manifest.json` in dev, the published
`appPackage.zip` in prod) and asserts that when
`Auth:TeamsSso:Mode = Required`, the manifest's `webApplicationInfo.id`
is non-empty. On failure, the host throws `OptionsValidationException`
with a clear message identifying the missing manifest field and the
config key that drove the requirement.

**Rationale**:

- Catches misconfiguration before a Teams user ever sees a broken card.
- Matches the existing `IValidateOptions<CacAuthOptions>` pattern used
  by Feature 003.

**Alternatives considered**:

- *Runtime check on first SSO attempt.* Rejected — would yield a
  bad-UX failure deep in the bot conversation instead of a clear startup
  signal.
- *Compile-time manifest check.* Rejected — manifests are deploy-time
  artifacts, not compile-time.

## R-Summary — Cross-decision invariants

These hold across all R1–R12:

1. **No bespoke refresh-token storage anywhere in the codebase.** MSAL
   owns silent refresh in the SPA (R1) and the VS Code extension (FR-018);
   the Teams bot relies on Bot Framework's token store (R4); the M365 and
   VS Code surfaces never call our `/api/auth/signout` (that endpoint
   serves the Dashboard only).
2. **Every audit row carries a non-null `EffectiveTenantId`.** Pre-session
   and unmapped events use `SYSTEM_TENANT_ID` (R9). The tenant-scoped
   query filter applies uniformly.
3. **Throttle, audit, and archive are three SRP services.** No service
   crosses its boundary (R6 / R7 / R3). The endpoint layer is the only
   place that decides HTTP status code from a throttle / audit outcome.
4. **The simulation panel's 3-layer gate (server config endpoint, SPA
   route guard, simulate endpoint 404) is the single most important
   security invariant of the feature.** A flash of the panel in
   non-Development is a Constitution § Security violation.
5. **Deployment-wide config wins over per-tenant config for every Auth
   knob.** Idle timeout (C2), throttle thresholds (C5), audit retention
   (Q3), Teams SSO mode (Q1) — all deployment-wide.
