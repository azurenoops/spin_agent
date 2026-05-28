# Implementation Plan: First-Class Login Experience Across All Surfaces

**Branch**: `051-login` | **Date**: 2026-05-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/051-login/spec.md`

## Summary

Stand up a first-class **Login** experience across all four ATO Copilot
surfaces (Dashboard, VS Code extension, M365 Teams bot, Web Chat) on top of
the existing CAC/PIV (Feature 003), simulation (Feature 027), and tenancy
(Feature 048) plumbing. The implementation adds **one new entity**
(`LoginAuditEvent`, dual-provider EF Core), **one new background worker**
(`LoginAuditArchiveService` for 13-month-to-cold-archive migration per
FR-036a), **five new HTTP endpoints** (`/api/auth/login-config`,
`/api/auth/me`, `/api/auth/signout`, `/api/auth/select-tenant`,
`/api/auth/simulate`), **one existing options class extended**
(`CacAuthOptions` gains `SimulatedIdentities` list for US7), **one existing
middleware extended** (`CacAuthenticationMiddleware` gains failed-login
throttling per FR-034/035 backed by `IDistributedCache`), and a complete
dashboard SPA rewrite of the unauthenticated landing experience (branded
`/login` page, tenant picker, class-specific error pages, account menu,
impersonation banner, simulation panel hard-gated to `Development`).
MSAL.js (`@azure/msal-react`) is introduced as the canonical client-side
token holder per Q4; no bespoke refresh-token storage. Login-race resolution
uses `window.addEventListener('storage', ...)` on the MSAL cache key per Q5.
Per Q1 / FR-021, Teams SSO defaults to `Optional, deployment-wide`. Per
Q2 / FR-032, all pre-session and unmapped `LoginAuditEvent` rows are owned
by the system tenant.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (backend); TypeScript 5.7 / React 19 (Dashboard); TypeScript 5 / Node 20 LTS (VS Code + M365 extensions)
- C# 13 / .NET 9.0 (backend — `Ato.Copilot.Core`, `Ato.Copilot.Mcp`,
  `Ato.Copilot.Chat`)
- TypeScript 5.7 / React 19 (Dashboard SPA)
- TypeScript 5.x / Node 20 LTS (VS Code extension, M365 Teams bot)

**Primary Dependencies**: ASP.NET Core 9.0 (Minimal APIs), EF Core 9.0 (SqlServer + Sqlite), Microsoft.Identity.Web 3.5+, Microsoft.Extensions.Caching.StackExchangeRedis 9.0 (NEW), Serilog 4.2; @azure/msal-browser 4.x + @azure/msal-react 3.x (NEW — dashboard; React-19-compatible versions); @azure/msal-node 2.x (NEW — VS Code); botbuilder-dialogs (M365)
- **Backend**: ASP.NET Core 9.0 (Minimal APIs), EF Core 9.0 (dual-provider:
  `Microsoft.EntityFrameworkCore.SqlServer` + `.Sqlite`),
  `Microsoft.Identity.Web` 3.5+ (already in repo — JWT validation),
  `Microsoft.Extensions.Caching.StackExchangeRedis` 9.0 (NEW — production
  throttle store; in-memory `IDistributedMemoryCache` in dev), Serilog 4.2,
  xUnit 2.9.3 + FluentAssertions 7.0 + Moq 4.20 (tests).
- **Dashboard**: `@azure/msal-browser` 4.x + `@azure/msal-react` 3.x (NEW —
  canonical client-side token holder per Q4 / FR-007a; pinned to msal-react@^3
  which is the lowest version with a React 19 peer-dependency — msal-react@2
  predates React 19 and rejects install on this dashboard), `react-router-dom`
  7.0 (already), `axios` 1.7 (already; 401-interceptor will gain MSAL
  silent-refresh fallback), `@testing-library/react` 16 + `vitest` 3
  (already), `js-cookie` 3.x (NEW — first-party signed-cookie read on the
  SPA side for the "remember tenant" flag).
- **VS Code extension**: `@azure/msal-node` 2.x (NEW — Public Client
  device-code flow per FR-017/FR-018; cached in VS Code `SecretStorage`).
- **M365 Teams bot**: `botbuilder-dialogs` (already) + `botbuilder-azure`
  (already) — Bot Framework SSO `getUserToken` / OAuthPrompt fallback per
  FR-020/FR-021.

**Storage**: SQLite (dev) / SQL Server (prod) via EF Core (`AtoCopilotContext`); one new table (`LoginAuditEvents`) shipped via `EnsureSchemaAdditions` module; Azure Storage append-blob (prod) + local filesystem (dev) for cold archive; Redis (prod) / in-memory (dev) for throttle counter; HMAC-signed first-party cookie for "remember tenant" (no server mirror)
- EF Core dual-provider — SQLite (dev) / SQL Server (prod) via the existing
  `AtoCopilotContext`.
- **One new table** (`LoginAuditEvents`) shipped via an idempotent
  `EnsureSchemaAdditions` module (`LoginAuditEventsSchemaAdditions.cs`)
  following the Feature 050 pattern — `dotnet ef migrations add` would emit
  a multi-hundred-line cascade because the model snapshot has drifted
  across Features 045–050, so we ship the targeted DDL instead.
- **Cold archive**: Azure Storage append-blob (prod) and local filesystem
  (dev) via a new abstraction `ILoginAuditArchiveSink` with two
  implementations (`AzureBlobAppendArchiveSink`, `FileSystemArchiveSink`).
- **Throttle counter**: `IDistributedCache` — `Microsoft.Extensions.Caching.Memory`
  (`IDistributedMemoryCache`) in dev (already in DI),
  `Microsoft.Extensions.Caching.StackExchangeRedis` in prod (Redis is
  already in the dev docker-compose as `redis` / `ato-copilot-redis`).
- **"Remember tenant" cookie**: first-party signed cookie (HMAC-SHA256 with
  a key drawn from `Auth:Cookie:SigningKey` in Key Vault). No server-side
  mirror.

**Testing**:
- xUnit + FluentAssertions + Moq for unit tests
  (`Ato.Copilot.Tests.Unit/Auth/`).
- `WebApplicationFactory<Program>` for endpoint integration tests
  (`Ato.Copilot.Tests.Integration/Auth/`); throttle tests use
  `IDistributedMemoryCache` to keep CI deterministic.
- `@testing-library/react` + `vitest` for dashboard component tests
  (`src/Ato.Copilot.Dashboard/src/__tests__/auth/`); MSAL.js mocked via
  `@azure/msal-react`'s `MsalProvider` test harness.
- `mocha` (existing) for VS Code extension tests
  (`extensions/vscode/test/auth/`); `vscode-test` for the SecretStorage
  acceptance test.
- `jest` (existing) for M365 bot tests
  (`extensions/m365/test/auth/`).
- Local TypeScript type-check parity per Constitution § Local Type-Checking
  Parity: `npm run typecheck` in `Ato.Copilot.Dashboard`, `npm run compile`
  in `extensions/vscode`, `npm run build` in `extensions/m365`.

**Target Platform**: Linux server (containerized via
`docker-compose.mcp.yml`); Chromium-class browser for Dashboard;
AzureUSGovernment (primary) + AzureCloud (secondary) regions.

**Project Type**: web (existing multi-project monorepo — no new top-level project; Feature 051 touches existing surfaces only)

**Performance Goals**:
- `/login` page first contentful paint ≤ 1 s p95 over a cold load.
- `/api/auth/login-config` ≤ 100 ms p95 (small static-shape JSON from
  `IOptions<AuthOptions>` — no DB, no Graph).
- `/api/auth/me` ≤ 200 ms p95.
- `/api/auth/signout` ≤ 200 ms p95 (revoke session + log audit row).
- `/api/auth/select-tenant` ≤ 300 ms p95 (writes the cookie + audit row).
- `/api/auth/simulate` is Development-only; ≤ 500 ms.
- Throttle decision (in `CacAuthenticationMiddleware`) ≤ 5 ms p95 (a single
  `IDistributedCache.IncrementAsync`).
- `LoginAuditArchiveService` daily migration MUST NOT exceed 30 minutes
  per million rows on production hardware.
- Idle-timer ticks (FR-007) MUST run off the main React render loop — a
  chained `setTimeout` per session, NOT `setInterval`, to avoid wasted
  cycles when the tab is backgrounded.

**Constraints**:
- **§VI TDD non-negotiable** — every new code path opens with a failing
  test using AAA markers.
- **Tenant Isolation non-negotiable** — `LoginAuditEvent` is
  `[TenantScoped]` with the automatic query filter applied by
  `AtoCopilotContext.OnModelCreating`. Pre-session and
  `NoTenantAssignment` rows MUST set `EffectiveTenantId = SYSTEM_TENANT_ID`
  per Q2 / FR-032.
- **Zero-Trust non-negotiable** — every endpoint validates the token on
  every request via the existing `CacAuthenticationMiddleware`; no endpoint
  trusts client-supplied tenant headers; the `RememberedTenantCookie` is
  validated server-side (HMAC + tenant-lifecycle check against `Tenants`)
  before its value is honored.
- **No new agents, no new BaseTool implementations** — Feature 051 is pure
  web infrastructure plus a SPA. No agent reasoning, no MCP envelope.
- **MSAL.js silent refresh non-negotiable** (Q4 / FR-007a) — the SPA MUST
  NOT implement bespoke refresh-token storage. The dashboard axios
  interceptor's only failure-path is `401 → acquireTokenSilent → retry once
  → loginRedirect`.
- **Simulation panel hard-gated to `Development`** (C6 / FR-023/024) — the
  gate MUST live at THREE layers: (1) the SSR / `login-config` endpoint
  omits the panel descriptor entirely outside `Development`, (2) the SPA
  route guard refuses to mount the panel even if the descriptor leaks,
  (3) `POST /api/auth/simulate` returns `404` not `403` outside
  `Development` AND writes a `SimulationBlocked` audit row. A flash of the
  panel in non-Development is a Constitution § Security violation.
- **`@ato sign out` in the VS Code extension MUST clear SecretStorage
  even when the backend is unreachable** (FR-019) — local-only cleanup
  is the contract.
- **Branding falls back gracefully** (FR-002) — missing logo MUST NOT
  render a broken image; missing deployment name MUST fall back to
  "ATO Copilot".
- **Throttle counter persistence non-negotiable** (FR-036) — backed by
  `IDistributedCache` (Redis in prod) so a process restart cannot bypass
  the rate-limit. Unit tests use `IDistributedMemoryCache`.
- **Audit retention is deployment-wide, not per-tenant** (FR-036a) — per
  the Q3 decision; per-tenant overrides are out of scope.

**Scale/Scope**:
- **Login surfaces**: 4 (Dashboard, VS Code, M365 Teams, Web Chat — Web
  Chat redirects to the Dashboard's `/login`).
- **Tenants**: 1 to thousands; bounded by Feature 048's existing envelope.
- **Login events per tenant per day**: typical ≤ 100, peak ≤ 10,000;
  `LoginAuditEvents` hot-table sizing budget 1M rows / tenant / 13
  months (the AU-11 retention horizon).
- **Throttle keys in Redis**: bounded; TTL on each entry = 60 s so the
  working set stays small even under sustained attack.
- **Surfaces touched**:
  - **Backend NEW**: `LoginAuditEvent.cs`, `LoginAuditEventType.cs`,
    `LoginErrorClass.cs`, `LoginSurface.cs`,
    `LoginAuditEventsSchemaAdditions.cs`, `ILoginAuditService.cs`,
    `LoginAuditService.cs`, `ILoginAuditArchiveSink.cs`,
    `AzureBlobAppendArchiveSink.cs`, `FileSystemArchiveSink.cs`,
    `LoginAuditArchiveService.cs` (`IHostedService`), `AuthOptions.cs`,
    `AuthThrottleOptions.cs`, `AuthEndpoints.cs`,
    `LoginThrottleMiddleware.cs`, `RememberedTenantCookieService.cs`.
  - **Backend MODIFY**: `CacAuthenticationMiddleware.cs` (failed-login
    audit + throttle hook), `CacAuthOptions.cs` (gains `SimulatedIdentities`
    list per US7), `Program.cs` (route mapping + schema-additions wiring +
    hosted-service registration), `AtoCopilotContext.cs` (DbSet + index).
  - **Dashboard NEW**: `src/features/auth/` (`LoginPage.tsx`,
    `TenantPickerPage.tsx`, `ErrorPage.tsx`, `AccountMenu.tsx`,
    `ImpersonationBanner.tsx`, `SimulationPanel.tsx`, `useIdleTimer.ts`,
    `useLoginRaceListener.ts`, `authClient.ts`, `msalConfig.ts`,
    `interceptors.ts`).
  - **Dashboard MODIFY**: `App.tsx` (MsalProvider wrap +
    `ProtectedRoute` guard), `main.tsx` (single MSAL instance creation),
    `features/csp-inherited-components/api.ts` and any other `*/api.ts`
    files to wire the silent-refresh axios interceptor.
  - **VS Code extension NEW**: `src/auth/deviceCodeFlow.ts`,
    `src/auth/secretStorage.ts`, `src/auth/signInCommand.ts`,
    `src/auth/signOutCommand.ts`. **MODIFY**: `src/extension.ts`
    (command registration + status-bar item).
  - **M365 Teams bot NEW**: `src/auth/signInCard.ts`,
    `src/auth/oauthPromptFallback.ts`. **MODIFY**: `src/bot.ts`
    (route mention to the sign-in flow on first contact).
- **Code surfaces NOT touched**: agent layer (`Ato.Copilot.Agents`), MCP
  tool envelope, the Compliance / SSP / POAM / Evidence / CSP-inherited-
  components feature areas, the Web Chat React client (it inherits the
  dashboard's MSAL token via the shared cookie + localStorage), the
  BaseAgent / BaseTool abstractions.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| # | Principle / Standard | Verdict | Evidence in spec / plan |
|---|---|---|---|
| I  | Documentation as Source of Truth | PASS | Spec at [spec.md](./spec.md) with 5 resolved clarifications; this plan + [research.md](./research.md) + [data-model.md](./data-model.md) + [contracts/](./contracts/) + [quickstart.md](./quickstart.md) cover every decision. |
| II | Simplicity | PASS | One new entity (`LoginAuditEvent`), one schema-additions module, one hosted service for the daily archive job, five new endpoints. No new agents, no new MCP tools, no new SignalR hubs. MSAL.js silently handles refresh — the SPA has zero refresh-token plumbing (Q4 / FR-007a). |
| III | YAGNI | PASS | Every component is driven by an FR with at least one acceptance scenario. No speculative federation, no SAML, no local accounts, no per-tenant retention. The 8 pre-resolved clarifications + the 5 from `/speckit.clarify` bound the surface. |
| IV | Single Responsibility Principle | PASS | `LoginAuditService.AppendAsync` writes one row + commits — it does NOT validate the token, throttle, or revoke sessions. `LoginThrottleMiddleware` decides "rate-limited or not" — it does NOT write the audit row (that's the failure handler's job). `RememberedTenantCookieService` issues + validates the cookie — it does NOT check tenant lifecycle (caller does). |
| V | BaseAgent / BaseTool Architecture | N/A — no new agents or tools | This feature stands up HTTP endpoints and SPA routes only. No agent reasoning. No MCP tool envelope. |
| VI | Test-Driven Development (NON-NEGOTIABLE) | PASS — enforced by tasks | Every new method opens with a failing AAA test. Test pyramid enumerated in [research.md § R6](./research.md): ~30 unit, ~20 integration, ~15 frontend component tests. |
| VII | Observability & Structured Logging | PASS | Every auth endpoint logs a structured Serilog event with `correlationId`, `surface`, `eventType`, `oid`, `tid`, `effectiveTenantId`. Bearer tokens, refresh tokens, and cert thumbprints MUST NEVER appear in log fields (FR-038). |
| —  | Azure Government & Compliance | PASS | All cloud-aware code paths read `Auth:Cloud` and switch endpoints accordingly. Azure Storage append-blob target in production runs in `AzureUSGovernment` (FR-036a). No new resource type that isn't already in the Azure Gov inventory. |
| —  | Security: Zero-Trust + Tenant Isolation | PASS | Every endpoint validates the token via `CacAuthenticationMiddleware` (existing). `LoginAuditEvent` is `[TenantScoped]`; pre-session rows belong to `SYSTEM_TENANT_ID` (Q2). The `RememberedTenantCookie` is HMAC-signed and validated server-side; clients are never trusted to self-assert their tenant. The simulation panel's three-layer gate satisfies "Assume Breach". |
| —  | Security: Secrets / Transport | PASS | Cookie HMAC key, MSAL client secrets, and SOC append-blob SAS tokens all sourced from Azure Key Vault. No secrets in `appsettings.json` or committed code. |
| —  | Local Type-Checking Parity (NON-NEGOTIABLE) | PASS | All three TS projects' typecheck commands documented in [quickstart.md § 2](./quickstart.md). Each MUST be runnable locally before commit. |
| —  | DevOps: CI/CD Zero Warnings | PASS | Targeting zero new warnings. `@azure/msal-react`'s peer-deps satisfy React 19. |
| —  | DevOps: GitHub Issue Discipline (NON-NEGOTIABLE) | DEFERRED to `/speckit.tasks` | The parent Feature 051 issue (#68 already exists) + 10 User Story sub-issues (US1–US10) MUST be created before tasks begin, with proper parent-child linkage per Constitution § DevOps. |
| —  | Complexity Justification | NOT APPLICABLE | No Simplicity (§II) or YAGNI (§III) deviation. Complexity Tracking table left empty. |

**Gate result**: **PASS** — proceed to Phase 0.

### Post-Design Re-Check (after Phase 1)

*Re-evaluated 2026-05-28 against [research.md](./research.md),
[data-model.md](./data-model.md),
[contracts/http-api.md](./contracts/http-api.md),
[contracts/internal-services.md](./contracts/internal-services.md),
[contracts/frontend-types.md](./contracts/frontend-types.md),
[contracts/vscode-extension.md](./contracts/vscode-extension.md),
[contracts/m365-bot.md](./contracts/m365-bot.md), and
[quickstart.md](./quickstart.md).*

| # | Principle / Standard | Re-Verdict | Post-design evidence |
|---|---|---|---|
| I  | Documentation as Source of Truth | PASS — unchanged | All eight artifacts authored. Cross-reference matrix in [data-model.md § 9](./data-model.md) maps every FR to a concrete artifact section. |
| II | Simplicity | PASS — unchanged | Final entity count = 1 (`LoginAuditEvent`). Final new service interface count = 4 (`ILoginAuditService`, `ILoginAuditArchiveSink`, `ILoginThrottleService`, `IRememberedTenantCookieService`) — each with a single responsibility. Five new endpoints. One new IHostedService (the daily archive job). |
| III | YAGNI | PASS — unchanged | The two-layer sink abstraction (`AzureBlobAppendArchiveSink` + `FileSystemArchiveSink`) is justified by the existing dev / prod parity rule. No third sink, no plugin system. The `RememberedTenantCookie` carries only `tenantId + iat + exp + HMAC` — no version field, no user-id encoding (the cookie name itself is the per-user namespace). |
| IV | Single Responsibility Principle | PASS — unchanged | `LoginAuditService.AppendAsync` does not call `SaveChangesAsync` — the caller's transaction owns the commit (mirrors Feature 050 `CapabilityHistoryService`). `LoginThrottleService.RegisterAttemptAsync` returns Allowed / Throttled — it does NOT decide HTTP status code. |
| V | BaseAgent / BaseTool | N/A — unchanged | No new agents or MCP tools introduced by any Phase 1 artifact. |
| VI | Test-Driven Development (NON-NEGOTIABLE) | PASS — unchanged | Test pyramid pinned per-artifact in research.md § R6. Every new method has at least one failing-test scenario enumerated. |
| VII | Observability & Structured Logging | PASS — unchanged | internal-services.md § 1.4 retains structured `ILogger<LoginAuditService>` and `ILogger<LoginThrottleService>`. No PII / CUI in log fields. |
| —  | Azure Government & Compliance | PASS — unchanged | Verified — no new Azure resources, no new data residency considerations across all eight artifacts. The Azure Storage append-blob target in production is the existing `AzureUSGovernment` regional storage account. |
| —  | Security: Zero-Trust + Tenant Isolation | PASS — unchanged | http-api.md § 1–§ 5 each open with the auth gate and tenant scoping. data-model.md § 1.7 confirms the composite index leads with `TenantId`. internal-services.md § 1.3 pins the SystemTenant ownership rule for pre-session rows. |
| —  | Security: Secrets / Transport | PASS — unchanged | No new secrets introduced beyond the cookie HMAC key, which is read from Key Vault. |
| —  | Local Type-Checking Parity (NON-NEGOTIABLE) | PASS — unchanged | frontend-types.md, vscode-extension.md, and m365-bot.md each produce well-typed TS wire models. quickstart.md § 2 mandates `npm run typecheck` / `npm run compile` / `npm run build` on the three TS projects. |
| —  | DevOps: CI/CD Zero Warnings | PASS — unchanged | No expected warnings from the proposed code. |
| —  | DevOps: GitHub Issue Discipline (NON-NEGOTIABLE) | DEFERRED to `/speckit.tasks` | Status unchanged — parent issue #68 exists; 10 User Story sub-issues MUST be created before tasks begin. |
| —  | Complexity Justification | NOT APPLICABLE — unchanged | Complexity Tracking table remains empty. Zero deviations from §II / §III across all Phase 1 artifacts. |

**Post-Design Gate result**: **PASS** — no new violations introduced by
Phase 0 / Phase 1 artifacts. Proceed to `/speckit.tasks`.

## Project Structure

### Documentation (this feature)

```text
specs/051-login/
├── plan.md                  # This file (/speckit.plan output)
├── spec.md                  # Feature specification (already exists; clarified 2026-05-28)
├── research.md              # Phase 0 — R1–R12 decisions
├── data-model.md            # Phase 1 — LoginAuditEvent schema + indexes + EnsureSchemaAdditions sketch
├── contracts/
│   ├── http-api.md          # Phase 1 — /api/auth/* endpoint contract (5 endpoints)
│   ├── internal-services.md # Phase 1 — ILoginAuditService, ILoginAuditArchiveSink, ILoginThrottleService, IRememberedTenantCookieService, AuthOptions binding
│   ├── frontend-types.md    # Phase 1 — TS types for the SPA (LoginConfig wire shape, TenantOption, ErrorClass enum, IdleState, MSAL configuration shape)
│   ├── vscode-extension.md  # Phase 1 — device-code flow contract + SecretStorage key naming + status-bar transitions
│   └── m365-bot.md          # Phase 1 — Adaptive Card schema + OAuthPrompt fallback contract
├── quickstart.md            # Phase 1 — local verification recipe across all four surfaces
├── checklists/
│   └── requirements.md      # Already exists (16/16 passing, updated post-clarify)
└── tasks.md                 # Phase 2 output (/speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Ato.Copilot.Core/
│   ├── Models/Auth/
│   │   ├── LoginAuditEvent.cs                          # NEW: append-only audit-trail entity (FR-032, Q2)
│   │   ├── LoginAuditEventType.cs                      # NEW: 9-value enum
│   │   ├── LoginErrorClass.cs                          # NEW: 10-value enum (FR-014/FR-015)
│   │   ├── LoginSurface.cs                             # NEW: 4-value enum (Dashboard, VSCode, M365, Chat)
│   │   └── AuthModels.cs                               # MODIFY: SimulatedIdentityDescriptor + AuthMethodDescriptor (US1, US7)
│   ├── Configuration/
│   │   ├── AuthOptions.cs                              # NEW: strongly-typed binding for `Auth:*` (FR-037)
│   │   ├── AuthThrottleOptions.cs                      # NEW: per-env Development / Production thresholds (FR-034)
│   │   └── CacAuthOptions.cs                           # MODIFY: SimulatedIdentity → SimulatedIdentities list (US7 / Feature 027 extension)
│   ├── Interfaces/Auth/
│   │   ├── ILoginAuditService.cs                       # NEW: AppendAsync + ListAsync (mirrors CapabilityHistoryService shape from Feature 050)
│   │   ├── ILoginAuditArchiveSink.cs                   # NEW: WriteBatchAsync — abstracts Azure Blob vs filesystem
│   │   ├── ILoginThrottleService.cs                    # NEW: RegisterAttemptAsync (returns Allowed | Throttled with RetryAfter)
│   │   └── IRememberedTenantCookieService.cs           # NEW: Issue + Validate (HMAC-signed)
│   ├── Services/Auth/
│   │   ├── LoginAuditService.cs                        # NEW
│   │   ├── LoginAuditArchiveService.cs                 # NEW: IHostedService — daily migration of rows > 13 mo (FR-036a)
│   │   ├── AzureBlobAppendArchiveSink.cs               # NEW: prod sink (Azure Storage append-blob)
│   │   ├── FileSystemArchiveSink.cs                    # NEW: dev sink (archive/LoginAuditEvents/{yyyy}/{MM}/)
│   │   ├── LoginThrottleService.cs                     # NEW: IDistributedCache-backed (Redis prod, in-memory dev)
│   │   └── RememberedTenantCookieService.cs            # NEW: HMAC-SHA256 sign + validate
│   └── Data/Context/
│       └── AtoCopilotContext.cs                        # MODIFY: DbSet<LoginAuditEvent>; OnModelCreating composite index (TenantId, OccurredAt DESC)
├── Ato.Copilot.Core/Data/Migrations/EnsureSchemaAdditions/
│   └── LoginAuditEventsSchemaAdditions.cs              # NEW: idempotent SqlServer + SQLite DDL; follows Feature 050 pattern
├── Ato.Copilot.Mcp/
│   ├── Endpoints/Auth/
│   │   └── AuthEndpoints.cs                            # NEW: 5 endpoints (login-config, me, signout, select-tenant, simulate)
│   ├── Middleware/
│   │   ├── CacAuthenticationMiddleware.cs              # MODIFY: failed-login → ILoginThrottleService + ILoginAuditService; SimulatedIdentities support (US7)
│   │   └── LoginThrottleMiddleware.cs                  # NEW: invoked from CacAuthenticationMiddleware on failure paths (per-IP + per-identity)
│   └── Program.cs                                      # MODIFY: route mapping + schema-additions + hosted-service registration + IDistributedCache wiring
├── Ato.Copilot.Dashboard/
│   └── src/
│       ├── main.tsx                                    # MODIFY: instantiate single PublicClientApplication
│       ├── App.tsx                                     # MODIFY: wrap with MsalProvider; add ProtectedRoute guard
│       ├── features/auth/
│       │   ├── LoginPage.tsx                           # NEW (US1)
│       │   ├── TenantPickerPage.tsx                    # NEW (US3)
│       │   ├── ErrorPage.tsx                           # NEW (US4)
│       │   ├── AccountMenu.tsx                         # NEW (US9)
│       │   ├── ImpersonationBanner.tsx                 # NEW (US8)
│       │   ├── SimulationPanel.tsx                     # NEW (US7 — hard-gated to Development)
│       │   ├── useIdleTimer.ts                         # NEW (US2 / FR-007 — chained setTimeout)
│       │   ├── useLoginRaceListener.ts                 # NEW (Q5 — storage-event)
│       │   ├── msalConfig.ts                           # NEW: PublicClientApplication config
│       │   ├── interceptors.ts                         # NEW: axios 401 → acquireTokenSilent → loginRedirect (FR-007a)
│       │   └── authClient.ts                           # NEW: /api/auth/* helpers
│       └── __tests__/auth/
│           ├── LoginPage.test.tsx                      # NEW (US1)
│           ├── TenantPickerPage.test.tsx               # NEW (US3)
│           ├── ErrorPage.test.tsx                      # NEW (US4)
│           ├── AccountMenu.test.tsx                    # NEW (US9)
│           ├── ImpersonationBanner.test.tsx            # NEW (US8)
│           ├── SimulationPanel.test.tsx                # NEW (US7 — environment-gate regression)
│           ├── useIdleTimer.test.tsx                   # NEW (US2 — fake-timer test)
│           ├── useLoginRaceListener.test.tsx           # NEW (Q5 — storage-event test)
│           └── interceptors.test.tsx                   # NEW (FR-007a — 401 retry single-shot)
extensions/vscode/
└── src/
    ├── auth/
    │   ├── deviceCodeFlow.ts                           # NEW (US5 / FR-017)
    │   ├── secretStorage.ts                            # NEW (US5 / FR-018)
    │   ├── signInCommand.ts                            # NEW
    │   └── signOutCommand.ts                           # NEW (FR-019)
    └── extension.ts                                    # MODIFY: command registration + status-bar item
extensions/m365/
└── src/
    ├── auth/
    │   ├── signInCard.ts                               # NEW: Adaptive Card (US6 / FR-020)
    │   └── oauthPromptFallback.ts                      # NEW: OAuthPrompt for tenants without SSO (US6 / FR-021)
    └── bot.ts                                          # MODIFY: route unlinked @mention to sign-in flow

tests/
├── Ato.Copilot.Tests.Unit/Auth/
│   ├── LoginAuditServiceTests.cs                       # NEW
│   ├── LoginThrottleServiceTests.cs                    # NEW
│   ├── RememberedTenantCookieServiceTests.cs           # NEW
│   ├── LoginAuditArchiveServiceTests.cs                # NEW
│   └── CacAuthenticationMiddlewareSimulationListTests.cs # NEW (US7 — list-based simulation)
└── Ato.Copilot.Tests.Integration/Auth/
    ├── AuthEndpointsTests.cs                           # NEW: 5 endpoints' happy + error paths
    ├── ThrottleEndpointTests.cs                        # NEW: Production thresholds → 429 + Retry-After (SC-005)
    ├── SimulationGateTests.cs                          # NEW: 404 outside Development across all 3 layers (SC-006)
    └── LoginAuditArchiveIntegrationTests.cs            # NEW: end-to-end archive cycle with FileSystemArchiveSink in dev
```

**Structure Decision**: Existing multi-project monorepo. All new code lives
under existing project folders matching their layer
(Core / Mcp / Dashboard / extensions/vscode / extensions/m365). The only
new directory is `src/Ato.Copilot.Dashboard/src/features/auth/` which
follows the dashboard's existing `src/features/<area>/` convention.

## Phasing

| Phase | Output | Purpose |
|---|---|---|
| **0** — Research | [research.md](./research.md) | R1 MSAL.js wiring strategy for the existing dashboard SPA, R2 distributed-cache provider selection, R3 cold-archive sink topology, R4 Bot Framework SSO / OAuthPrompt branching, R5 schema-additions vs `dotnet ef`, R6 test strategy, R7 throttle bucket-key design, R8 `RememberedTenantCookie` signing primitive, R9 `LoginAuditEvent` tenant ownership for forensic queries, R10 idle-timer JS event source, R11 storage-event vs BroadcastChannel for login race, R12 Teams SSO mode enforcement at startup. |
| **1** — Design & Contracts | [data-model.md](./data-model.md), [contracts/http-api.md](./contracts/http-api.md), [contracts/internal-services.md](./contracts/internal-services.md), [contracts/frontend-types.md](./contracts/frontend-types.md), [contracts/vscode-extension.md](./contracts/vscode-extension.md), [contracts/m365-bot.md](./contracts/m365-bot.md), [quickstart.md](./quickstart.md) | Pin every wire shape, every internal interface, every UI prop contract, every endpoint envelope, and the manual-verification recipe. |
| **2** — Tasks | tasks.md (`/speckit.tasks` only) | Generate the dependency-ordered task list organized by user story (US1 → US10), with TDD discipline enforced (failing test first per task). |
| **3+** — Implementation | source + test changes | Execute the tasks. Out of scope for `/speckit.plan`. |

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No Constitution violations identified. Table left empty. All deviations
from the strictest reading of §II / §III (e.g., introducing `@azure/msal-react`
as a new client-side dependency rather than rolling our own MSAL.js
wrapper) are justified inline in the relevant FR or the Clarifications
section of [spec.md](./spec.md) and re-confirmed in the Post-Design
Re-Check above after Phase 1 artifacts land.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| _(none)_ | _(none)_ | _(none)_ |
