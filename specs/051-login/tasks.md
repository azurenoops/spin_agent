# Tasks: First-Class Login Experience

**Feature**: 051-login
**Plan**: [plan.md](./plan.md)
**Spec**: [spec.md](./spec.md)
**Research**: [research.md](./research.md)
**Data model**: [data-model.md](./data-model.md)
**Contracts**: [contracts/](./contracts/)
**Quickstart**: [quickstart.md](./quickstart.md)
**Date**: 2026-05-28

**Tests**: Tests ARE included — Constitution §VI mandates Red-Green-Refactor
with AAA markers. Every implementation task is preceded by its failing test
task. Tasks tagged `[TDD-Test]` are RED-phase tests that MUST fail before
the implementation task in the same story is started.

**Organization**: Tasks are grouped by user story (Phases 3–12). Each story
maps to a P1/P2/P3 priority from spec.md and is independently testable per
quickstart.md.

## Format: `- [ ] T### [P?] [Story?] Description with file path`

- **[P]**: Parallelizable (different files, no dependencies on incomplete tasks)
- **[Story]**: `[US1]`–`[US10]`. Setup / Foundational / Polish phases carry NO story label.
- File paths are workspace-relative.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Branch, packages, DI scaffolding shared by every later phase.

- [X] T001 Verify branch `051-login` is checked out and clean: `git status --short && git rev-parse --abbrev-ref HEAD`
- [X] T002 [P] Add NuGet package `Microsoft.Extensions.Caching.StackExchangeRedis` 9.0 to `src/Ato.Copilot.Mcp/Ato.Copilot.Mcp.csproj`. **Commit `packages.lock.json` delta in the same atomic change per analysis C16.**
- [X] T003 [P] Add npm packages `@azure/msal-browser@^4` `@azure/msal-react@^3` `js-cookie@^3` `@types/js-cookie@^3` to [src/Ato.Copilot.Dashboard/package.json](src/Ato.Copilot.Dashboard/package.json) via `npm install`. **NOTE (2026-05-28 verification): msal-react@^2 predates React 19 (peer requires React 16/17/18); the dashboard ships React 19 — use msal-react@^3 (lowest version with React 19 peer) + msal-browser@^4 (msal-react@^3 peer requirement). The public API surface used in our contracts (`MsalProvider`, `useMsal`, `useIsAuthenticated`, `PublicClientApplication`) is identical between v2/v3/v5.** Commit `package-lock.json` delta in the same atomic change per analysis C16.
- [X] T004 [P] Add npm package `@azure/msal-node@^2` to [extensions/vscode/package.json](extensions/vscode/package.json) via `npm install`. **Commit `package-lock.json` delta in the same atomic change (C16).**
- [X] T005 [P] Create folder skeletons: `src/Ato.Copilot.Core/Models/Auth/`, `src/Ato.Copilot.Core/Interfaces/Auth/`, `src/Ato.Copilot.Core/Services/Auth/`, `src/Ato.Copilot.Core/Configuration/Auth/`, `src/Ato.Copilot.Core/Data/Migrations/EnsureSchemaAdditions/`, `src/Ato.Copilot.Mcp/Endpoints/Auth/`, `src/Ato.Copilot.Dashboard/src/features/auth/`, `extensions/vscode/src/auth/`, `extensions/m365/src/auth/`, `tests/Ato.Copilot.Tests.Unit/Auth/`, `tests/Ato.Copilot.Tests.Integration/Auth/`, `src/Ato.Copilot.Dashboard/src/__tests__/auth/`
- [X] T006 [P] Add a `redis` service (image `redis:7.4-alpine`, container `ato-copilot-redis`, append-only persistence, LRU cap 256MB) to [docker-compose.mcp.yml](docker-compose.mcp.yml); wire `depends_on: redis: condition: service_healthy` onto the `ato-copilot` service block; set `ATO_CONNECTIONSTRINGS__REDIS=redis:6379` env var. (Foreign `stark-redis` from a sibling workspace was NOT used — the compose dependency is self-contained.)
- [X] T007 [P] Create `dotnet test` filter file [tests/Ato.Copilot.Tests.Unit/Auth/.gitkeep](tests/Ato.Copilot.Tests.Unit/Auth/.gitkeep) so empty folder commits cleanly
- [X] T008 Run `dotnet build Ato.Copilot.sln` once to confirm packages restore and project compiles before any Auth code is added

**Checkpoint**: Build green, packages restored, folder skeleton in place.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entity, audit-write service, options binding, MSAL Provider mount, axios
interceptor base. EVERY user story phase depends on this phase.

### 2.1 Enums + entity

- [X] T009 [P] Create [src/Ato.Copilot.Core/Models/Auth/LoginAuditEventType.cs](src/Ato.Copilot.Core/Models/Auth/LoginAuditEventType.cs) — enum with 9 values per [data-model.md § 1.3](specs/051-login/data-model.md)
- [X] T010 [P] Create [src/Ato.Copilot.Core/Models/Auth/LoginErrorClass.cs](src/Ato.Copilot.Core/Models/Auth/LoginErrorClass.cs) — enum with 10 values per [data-model.md § 1.4](specs/051-login/data-model.md)
- [X] T011 [P] Create [src/Ato.Copilot.Core/Models/Auth/LoginSurface.cs](src/Ato.Copilot.Core/Models/Auth/LoginSurface.cs) — enum `Dashboard | VSCode | M365 | Chat`
- [X] T012 Create [src/Ato.Copilot.Core/Models/Auth/LoginAuditEvent.cs](src/Ato.Copilot.Core/Models/Auth/LoginAuditEvent.cs) — entity per [data-model.md § 1.12](specs/051-login/data-model.md) with `[TenantScoped]` attribute and all 12 fields
- [X] T013 [TDD-Test] Create [tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditEventTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditEventTests.cs) — assert `[TenantScoped]` attribute present, validation on `CorrelationId` ≤ 64 chars, `SourceIp` ≤ 45, `UserAgent` ≤ 512, `MetadataJson` ≤ 2000; assert `Id` defaults to `Guid.NewGuid()`. RED.
- [X] T014 Make T013 pass — adjust attributes on `LoginAuditEvent.cs` as needed. GREEN.

### 2.2 EF Core wiring

- [X] T015 Add `DbSet<LoginAuditEvent> LoginAuditEvents` and `OnModelCreating` block per [data-model.md § 2](specs/051-login/data-model.md) to [src/Ato.Copilot.Core/Data/AtoCopilotContext.cs](src/Ato.Copilot.Core/Data/AtoCopilotContext.cs)
- [X] T016 [TDD-Test] [tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditEventModelBuilderTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditEventModelBuilderTests.cs) — assert the three indexes exist, leading column of `IX_LoginAuditEvents_Tenant_Occurred` is `EffectiveTenantId`, FK `OnDelete=Cascade` to `Tenants`, enum columns use string conversion. RED.
- [X] T017 Make T016 pass. GREEN.

### 2.3 EnsureSchemaAdditions

- [X] T018 Create [src/Ato.Copilot.Core/Data/Migrations/EnsureSchemaAdditions/LoginAuditEventsSchemaAdditions.cs](src/Ato.Copilot.Core/Data/Migrations/EnsureSchemaAdditions/LoginAuditEventsSchemaAdditions.cs) — idempotent DDL for SQL Server + SQLite per [data-model.md § 3](specs/051-login/data-model.md)
- [X] T019 Wire `LoginAuditEventsSchemaAdditions.ApplyAsync` into `EnsureSchemaAdditionsAsync` in [src/Ato.Copilot.Mcp/Program.cs](src/Ato.Copilot.Mcp/Program.cs) after the Feature 050 `CapabilityHistoryEventsSchemaAdditions` call
- [X] T020 [TDD-Test] [tests/Ato.Copilot.Tests.Integration/Auth/LoginAuditEventsSchemaAdditionsTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/LoginAuditEventsSchemaAdditionsTests.cs) — boot a SQLite `AtoCopilotContext`, run `ApplyAsync` twice, assert no exception and `LoginAuditEvents` table + 3 indexes exist. RED then GREEN.

### 2.4 Options binding

- [X] T021 [P] Create [src/Ato.Copilot.Core/Configuration/Auth/AuthOptions.cs](src/Ato.Copilot.Core/Configuration/Auth/AuthOptions.cs) — root + nested per [contracts/internal-services.md § 5.1](specs/051-login/contracts/internal-services.md)
- [X] T022 [P] Create [src/Ato.Copilot.Core/Configuration/Auth/AuthOptionsValidator.cs](src/Ato.Copilot.Core/Configuration/Auth/AuthOptionsValidator.cs) — `IValidateOptions<AuthOptions>` per [contracts/internal-services.md § 5.2](specs/051-login/contracts/internal-services.md) and [research.md § R12](specs/051-login/research.md)
- [X] T023 [TDD-Test] [tests/Ato.Copilot.Tests.Unit/Auth/AuthOptionsValidatorTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/AuthOptionsValidatorTests.cs) — assert missing `Cookie.SigningKey` fails outside Development, `IdleTimeoutMinutes` range, `Throttle.Production.PerIpPerMinute > 0`, `TeamsSso.Mode = Required` requires `ConnectionName`. RED then GREEN.
- [X] T024 Bind `AuthOptions` in [src/Ato.Copilot.Mcp/Program.cs](src/Ato.Copilot.Mcp/Program.cs) with `.AddOptions<AuthOptions>().Bind(config.GetSection("Auth")).ValidateOnStart()` and register the validator

### 2.5 Audit write service (`AppendAsync` only)

- [X] T025 Create [src/Ato.Copilot.Core/Interfaces/Auth/ILoginAuditService.cs](src/Ato.Copilot.Core/Interfaces/Auth/ILoginAuditService.cs) + `LoginAuditEventDraft` record per [contracts/internal-services.md § 1.2](specs/051-login/contracts/internal-services.md)
- [X] T026 [TDD-Test] [tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditServiceAppendTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditServiceAppendTests.cs) — assert `AppendAsync` does NOT call `SaveChangesAsync`, populates `Id` + `OccurredAt`, accepts `SYSTEM_TENANT_ID` for `EffectiveTenantId`, rejects `Oid` > 254 chars. RED.
- [X] T027 Create [src/Ato.Copilot.Core/Services/Auth/LoginAuditService.cs](src/Ato.Copilot.Core/Services/Auth/LoginAuditService.cs) — implement `AppendAsync` only (List methods stubbed `throw new NotImplementedException` for now; T084/T085 wire them). GREEN T026.
- [X] T028 Register `services.AddScoped<ILoginAuditService, LoginAuditService>()` in [src/Ato.Copilot.Mcp/Program.cs](src/Ato.Copilot.Mcp/Program.cs)

### 2.6 Throttle service + distributed cache

- [X] T029 [P] Create [src/Ato.Copilot.Core/Interfaces/Auth/ILoginThrottleService.cs](src/Ato.Copilot.Core/Interfaces/Auth/ILoginThrottleService.cs) + `LoginThrottleDecision` record per [contracts/internal-services.md § 2.2](specs/051-login/contracts/internal-services.md)
- [X] T030 [TDD-Test] [tests/Ato.Copilot.Tests.Unit/Auth/LoginThrottleServiceTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/LoginThrottleServiceTests.cs) — backed by `IDistributedMemoryCache`. **Defaults under test MUST match spec.md FR-034 (analysis C2): Production = 20/min/IP + 10/min/identity; Development = 100/min/IP + 100/min/identity.** Assert 21st failed attempt within 60s on the same IP returns `Allowed=false` with `RetryAfter` ≤ 60s; per-identity counter separate from per-IP; `ResetIdentityAsync` clears identity but not IP. **Add explicit test cases that read `AuthThrottleOptions` defaults and assert the documented values match FR-034.** RED.
- [X] T031 Create [src/Ato.Copilot.Core/Services/Auth/LoginThrottleService.cs](src/Ato.Copilot.Core/Services/Auth/LoginThrottleService.cs) — implement bucket-key design per [research.md § R7](specs/051-login/research.md). GREEN T030.
- [X] T032 Register `services.AddSingleton<ILoginThrottleService, LoginThrottleService>()` and add `IDistributedMemoryCache` (dev) / `AddStackExchangeRedisCache` (prod) per [contracts/internal-services.md § 6](specs/051-login/contracts/internal-services.md) in [src/Ato.Copilot.Mcp/Program.cs](src/Ato.Copilot.Mcp/Program.cs)

### 2.7 Dashboard MSAL provider + interceptor scaffold

- [X] T033 [P] Create [src/Ato.Copilot.Dashboard/src/features/auth/types.ts](src/Ato.Copilot.Dashboard/src/features/auth/types.ts) — all TS types per [contracts/frontend-types.md § 1 + § 2](specs/051-login/contracts/frontend-types.md)
- [X] T034 [P] Create [src/Ato.Copilot.Dashboard/src/features/auth/msalConfig.ts](src/Ato.Copilot.Dashboard/src/features/auth/msalConfig.ts) — `buildMsalConfig(login: LoginConfig)` per [contracts/frontend-types.md § 3.1](specs/051-login/contracts/frontend-types.md)
- [X] T035 [P] Create [src/Ato.Copilot.Dashboard/src/features/auth/LoginConfigContext.tsx](src/Ato.Copilot.Dashboard/src/features/auth/LoginConfigContext.tsx) — `LoginConfigProvider` + `useLoginConfig()` hook (throws if not provided)
- [X] T036 Create [src/Ato.Copilot.Dashboard/src/features/auth/interceptors.ts](src/Ato.Copilot.Dashboard/src/features/auth/interceptors.ts) — `attachAuthInterceptor(axios, msal, scopes)` per [contracts/frontend-types.md § 3.3](specs/051-login/contracts/frontend-types.md); dispatches `'ato:user-input'` ONLY on non-silent-renewal 2xx
- [X] T037 [TDD-Test] [src/Ato.Copilot.Dashboard/src/__tests__/auth/interceptors.test.ts](src/Ato.Copilot.Dashboard/src/__tests__/auth/interceptors.test.ts) — Vitest: assert `acquireTokenSilent` called per request, `Authorization: Bearer` header set, 401 triggers single retry, retried request tagged `_silentRenewal=true`, NO `'ato:user-input'` dispatched on the silent-renewal success path. RED then GREEN.
- [X] T038 Modify [src/Ato.Copilot.Dashboard/src/main.tsx](src/Ato.Copilot.Dashboard/src/main.tsx) — fetch `GET /api/auth/login-config` (using bootstrap axios without bearer), instantiate `PublicClientApplication`, wrap `<App />` in `<MsalProvider>` and `<LoginConfigProvider>` per [contracts/frontend-types.md § 3.2](specs/051-login/contracts/frontend-types.md)

### 2.8 Audit-trail middleware foundation

- [X] T039 Create [src/Ato.Copilot.Mcp/Middleware/LoginAuditContextAccessor.cs](src/Ato.Copilot.Mcp/Middleware/LoginAuditContextAccessor.cs) — extracts `SourceIp`, `UserAgent`, `CorrelationId` from `HttpContext` for endpoint handlers to feed into `LoginAuditEventDraft`
- [X] T040 [TDD-Test] [tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditContextAccessorTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditContextAccessorTests.cs) — assert IPv6 forwarded headers honored, UA truncated to 512 chars, missing CorrelationId synthesized. RED then GREEN.

**Checkpoint**: Foundation green. `dotnet build && dotnet test` for `tests/Ato.Copilot.Tests.Unit/Auth/` passes. `npm --prefix src/Ato.Copilot.Dashboard run typecheck` passes. No user-story work has started yet.

---

## Phase 3: User Story 1 — First-Time Login from the Dashboard (P1)

**Goal**: Unauthenticated user lands on branded `/login` page, signs in
via the configured method, and reaches their dashboard (deep link preserved).

**Independent test criteria**: [quickstart.md § 1, § 2, § 4](specs/051-login/quickstart.md).

### 3.1 Endpoint: GET /api/auth/login-config

- [X] T041 [TDD-Test] [US1] [tests/Ato.Copilot.Tests.Integration/Auth/LoginConfigEndpointTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/LoginConfigEndpointTests.cs) — `GET /api/auth/login-config` returns 200 with `branding`, `defaultMethod`, `enabledMethods`, `msal`, `cloud`, `idleTimeoutMinutes`, `rememberTenantCookieDays`, `simulation=null` when env=Production; `Cache-Control: no-store`. RED.
- [X] T042 [US1] Create [src/Ato.Copilot.Mcp/Endpoints/Auth/AuthEndpoints.cs](src/Ato.Copilot.Mcp/Endpoints/Auth/AuthEndpoints.cs) — register endpoint group `/api/auth`; implement `GET /login-config` per [contracts/http-api.md § 1](specs/051-login/contracts/http-api.md). GREEN T041.
- [X] T043 [US1] Register the endpoint group in [src/Ato.Copilot.Mcp/Program.cs](src/Ato.Copilot.Mcp/Program.cs)

### 3.2 Endpoint: GET /api/auth/me

- [X] T044 [TDD-Test] [US1] [tests/Ato.Copilot.Tests.Integration/Auth/MeEndpointTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/MeEndpointTests.cs) — `GET /api/auth/me` returns 200 with full `MeResponse` for authenticated user, 401 without bearer, 403 `NO_TENANT_ASSIGNMENT` + writes audit row with `SYSTEM_TENANT_ID` for tenant-less identity, debounced `LoginSuccess` audit row (one per 5-min window). RED.
- [X] T045 [US1] Implement `GET /api/auth/me` in [src/Ato.Copilot.Mcp/Endpoints/Auth/AuthEndpoints.cs](src/Ato.Copilot.Mcp/Endpoints/Auth/AuthEndpoints.cs) per [contracts/http-api.md § 2](specs/051-login/contracts/http-api.md). GREEN T044.

### 3.3 Dashboard `LoginPage`

- [X] T046 [TDD-Test] [P] [US1] [src/Ato.Copilot.Dashboard/src/__tests__/auth/LoginPage.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/LoginPage.test.tsx) — renders deployment name + logo from `LoginConfig.branding`, renders one button per `enabledMethods` entry, hides simulation panel when `simulation=null`. RED.
- [X] T047 [US1] Create [src/Ato.Copilot.Dashboard/src/features/auth/LoginPage.tsx](src/Ato.Copilot.Dashboard/src/features/auth/LoginPage.tsx). GREEN T046.
- [X] T048 [P] [US1] Create [src/Ato.Copilot.Dashboard/src/features/auth/errorCopy.ts](src/Ato.Copilot.Dashboard/src/features/auth/errorCopy.ts) — stub map `ErrorClass → { title, message }` (US4 fills the copy)

### 3.4 Dashboard `LoginCallbackPage` + deep-link

- [X] T049 [TDD-Test] [US1] [src/Ato.Copilot.Dashboard/src/__tests__/auth/LoginCallbackPage.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/LoginCallbackPage.test.tsx) — awaits `handleRedirectPromise`, navigates to `state` when present (deep link), else to `/dashboard`. RED.
- [X] T050 [US1] Create [src/Ato.Copilot.Dashboard/src/features/auth/LoginCallbackPage.tsx](src/Ato.Copilot.Dashboard/src/features/auth/LoginCallbackPage.tsx). GREEN T049.
- [X] T051 [P] [US1] Create [src/Ato.Copilot.Dashboard/src/features/auth/RequireAuth.tsx](src/Ato.Copilot.Dashboard/src/features/auth/RequireAuth.tsx) — gate component that calls `loginRedirect({ state: pathname + search })` when `!useIsAuthenticated()`
- [X] T052 [US1] Add `/login` and `/login/callback` routes + wrap protected routes in `<RequireAuth>` in [src/Ato.Copilot.Dashboard/src/App.tsx](src/Ato.Copilot.Dashboard/src/App.tsx)
- [X] T053 [US1] Replace `localStorage.getItem('auth_token')` with the new MSAL interceptor in every `*/api.ts` (14 files — use `grep -rl "localStorage.getItem.*auth_token" src/Ato.Copilot.Dashboard/src` to enumerate; each file deletes its inline header logic and relies on the global interceptor configured in T038)

### 3.6 Cross-tab login race (relocated from Phase 5 per analysis C3)

The `useLoginRaceListener` hook + its tests originally landed under US3,
but the underlying edge case (sibling tab completes deep-link after
sign-in elsewhere) belongs to US1's deep-link preservation contract.
These tasks run as part of US1 and unblock the MVP without requiring
US3 to be in scope. **Note (C3): legacy task IDs T074–T076 are RETIRED;
these replacement IDs T053a–T053c carry the work.**

- [X] T053a [TDD-Test] [P] [US1] [src/Ato.Copilot.Dashboard/src/__tests__/auth/useLoginRaceListener.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/useLoginRaceListener.test.tsx) — synthetic `StorageEvent` with `key='msal.account.keys.0'` and non-empty accounts calls `onLoginCompletedInAnotherTab`; storage event on an unrelated key does NOT. RED.
- [X] T053b [US1] Create [src/Ato.Copilot.Dashboard/src/features/auth/useLoginRaceListener.ts](src/Ato.Copilot.Dashboard/src/features/auth/useLoginRaceListener.ts) per [contracts/frontend-types.md § 4.2](specs/051-login/contracts/frontend-types.md). GREEN T053a.
- [X] T053c [US1] Mount the listener on `LoginPage` AND on `LoginCallbackPage` so a parallel-tab sign-in advances the waiting tab to its deep link without requiring user click.

### 3.7 Manual sign-off

- [ ] T054 [US1] Execute [quickstart.md § 1, § 2, § 4](specs/051-login/quickstart.md) against the local docker stack and tick the boxes
  > **Pending live verification** — quickstart § 1/2/4 is a live-Docker test that the user runs locally after Phase 3 commits land. Skipped per Phase 3 commit-strategy instructions.

**Checkpoint US1**: Branded `/login` renders, deep link preserved through MSAL flow, `LoginSuccess` audit row written. MVP candidate.

---

## Phase 4: User Story 2 — Sign Out and Idle Sign-Out (P1)

**Goal**: Explicit sign-out + 30-minute idle timeout both revoke session
and emit audit rows. Silent token renewal does NOT reset the idle counter.

**Independent test criteria**: [quickstart.md § 5, § 6](specs/051-login/quickstart.md).

### 4.1 Endpoint: POST /api/auth/signout

- [X] T055 [TDD-Test] [US2] [tests/Ato.Copilot.Tests.Integration/Auth/SignOutEndpointTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/SignOutEndpointTests.cs) — `POST /api/auth/signout` returns 204, writes `SignOut` row (default reason); writes `IdleSignOut` row when body is `{"reason":"idle_timeout"}`; deletes `X-Impersonated-Tenant` cookie when present; 401 without bearer. RED.
- [X] T056 [US2] Implement `POST /api/auth/signout` per [contracts/http-api.md § 3](specs/051-login/contracts/http-api.md). GREEN T055.

### 4.2 `useIdleTimer` hook

- [X] T057 [TDD-Test] [P] [US2] [src/Ato.Copilot.Dashboard/src/__tests__/auth/useIdleTimer.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/useIdleTimer.test.tsx) — Vitest fake timers: assert timer resets on `mousemove`/`keydown`/`touchstart`/`'ato:user-input'`; fires `'ato:idle-warning'` 60s before expiry; calls `POST /api/auth/signout {"reason":"idle_timeout"}` on expiry; does NOT reset on a `'ato:user-input'` event tagged `silentRenewal=true`. RED.
- [X] T058 [US2] Create [src/Ato.Copilot.Dashboard/src/features/auth/useIdleTimer.ts](src/Ato.Copilot.Dashboard/src/features/auth/useIdleTimer.ts) per [contracts/frontend-types.md § 4.1](specs/051-login/contracts/frontend-types.md). GREEN T057.

### 4.3 `IdleWarningModal`

- [X] T059 [TDD-Test] [P] [US2] [src/Ato.Copilot.Dashboard/src/__tests__/auth/IdleWarningModal.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/IdleWarningModal.test.tsx) — renders on `'ato:idle-warning'`, shows countdown, "Stay signed in" button dispatches `'ato:user-input'`. RED.
- [X] T060 [US2] Create [src/Ato.Copilot.Dashboard/src/features/auth/IdleWarningModal.tsx](src/Ato.Copilot.Dashboard/src/features/auth/IdleWarningModal.tsx). GREEN T059.
- [X] T061 [US2] Mount `<IdleWarningModal />` + `useIdleTimer(login.idleTimeoutMinutes)` in `<AppShell />` (or equivalent authenticated root) in [src/Ato.Copilot.Dashboard/src/App.tsx](src/Ato.Copilot.Dashboard/src/App.tsx)

### 4.4 Sign-out wiring

- [X] T062 [US2] Stub `AccountMenu.tsx` sign-out button — full menu lands in US9; this task adds just the button at the existing header location and wires it to `POST /api/auth/signout` then `msalInstance.logoutRedirect()` (file: [src/Ato.Copilot.Dashboard/src/features/auth/AccountMenu.tsx](src/Ato.Copilot.Dashboard/src/features/auth/AccountMenu.tsx))

### 4.5 FR-008 — Restore unsaved changes on idle sign-out (analysis C1)

FR-008 requires the SPA to persist in-flight form state to localStorage
before idle sign-out, namespaced by `oid`, then offer a "Restore unsaved
changes" prompt on next sign-in. The original tasks list missed this
requirement entirely. Tasks below close the gap.

- [X] T062a [TDD-Test] [P] [US2] [src/Ato.Copilot.Dashboard/src/__tests__/auth/useIdleFormStateBackup.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/useIdleFormStateBackup.test.tsx) — Vitest with fake timers: assert (a) a registered form serializer is invoked synchronously on `'ato:idle-warning'` before the timer expires; (b) the serialized snapshot is written to `localStorage` under key `ato.unsavedChanges.{oid}.{formId}` with a wall-clock timestamp; (c) `useIdleFormStateBackup` returns `{ register, unregister }` for components to opt in; (d) on explicit (non-idle) sign-out, the snapshot is purged. RED.
- [X] T062b [US2] Create [src/Ato.Copilot.Dashboard/src/features/auth/useIdleFormStateBackup.ts](src/Ato.Copilot.Dashboard/src/features/auth/useIdleFormStateBackup.ts) — thin React hook that subscribes to `'ato:idle-warning'` and walks registered serializers; persists JSON snapshots; co-located helper `purgeUnsavedChanges(oid)` for explicit sign-out. GREEN T062a.
- [X] T062c [TDD-Test] [P] [US2] [src/Ato.Copilot.Dashboard/src/__tests__/auth/RestoreUnsavedChangesPrompt.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/RestoreUnsavedChangesPrompt.test.tsx) — component renders ONLY when `localStorage` has at least one `ato.unsavedChanges.{me.oid}.*` key; lists each affected form with its timestamp; "Restore" emits `'ato:restore-unsaved'` CustomEvent with the snapshot; "Discard" purges the key; ignores keys belonging to a different `oid`. RED.
- [X] T062d [US2] Create [src/Ato.Copilot.Dashboard/src/features/auth/RestoreUnsavedChangesPrompt.tsx](src/Ato.Copilot.Dashboard/src/features/auth/RestoreUnsavedChangesPrompt.tsx) and mount it inside `<AppShell />` so it surfaces right after sign-in completes. GREEN T062c. Document in [contracts/frontend-types.md § 4](specs/051-login/contracts/frontend-types.md) (add hook + component signatures).

### 4.6 Manual sign-off

- [ ] T063 [US2] Execute [quickstart.md § 5, § 6](specs/051-login/quickstart.md) — Pending live verification (deferred per Phase 3 T054 pattern; requires Docker stack with real Entra tenant)

**Checkpoint US2**: Sign-out works; idle timer fires; silent renewal does NOT reset.

---

## Phase 5: User Story 3 — Tenant / Organization Picker on Login (P1)

**Goal**: Multi-tenant user picks a tenant on first sign-in; optional
"remember on this device" HMAC cookie skips the picker next time.

**Independent test criteria**: [quickstart.md § 3, § 7](specs/051-login/quickstart.md).

### 5.1 `IRememberedTenantCookieService`

- [X] T064 [P] [US3] Create [src/Ato.Copilot.Core/Interfaces/Auth/IRememberedTenantCookieService.cs](src/Ato.Copilot.Core/Interfaces/Auth/IRememberedTenantCookieService.cs) per [contracts/internal-services.md § 3.2](specs/051-login/contracts/internal-services.md)
- [X] T065 [TDD-Test] [US3] [tests/Ato.Copilot.Tests.Unit/Auth/RememberedTenantCookieServiceTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/RememberedTenantCookieServiceTests.cs) — assert `Issue` produces 4-part base64url string, `Validate` round-trips, tampered HMAC rejected (null), expired cookie rejected (null), wrong signing key rejected (null), never throws. RED.
- [X] T066 [US3] Create [src/Ato.Copilot.Core/Services/Auth/RememberedTenantCookieService.cs](src/Ato.Copilot.Core/Services/Auth/RememberedTenantCookieService.cs) — HMAC-SHA256 per [research.md § R8](specs/051-login/research.md). GREEN T065.
- [X] T067 [US3] Register `services.AddSingleton<IRememberedTenantCookieService, RememberedTenantCookieService>()` in [src/Ato.Copilot.Mcp/Program.cs](src/Ato.Copilot.Mcp/Program.cs)

### 5.2 Endpoint: POST /api/auth/select-tenant

- [X] T068 [TDD-Test] [US3] [tests/Ato.Copilot.Tests.Integration/Auth/SelectTenantEndpointTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/SelectTenantEndpointTests.cs) — 204 on valid tenant + member; sets `ato-remembered-tenant` cookie when `remember=true`; 403 `FORBIDDEN_NOT_TENANT_MEMBER` when not a member; 409 `TENANT_DISABLED` when Disabled and caller is not CSP-Admin; 404 on unknown tenant; emits `TenantSwitch` audit row. RED.
- [X] T069 [US3] Implement `POST /api/auth/select-tenant` per [contracts/http-api.md § 4](specs/051-login/contracts/http-api.md). GREEN T068.

### 5.3 Bootstrap honoring of remembered cookie

- [X] T070 [US3] Extend `GET /api/auth/me` handler to read the remembered cookie via `IRememberedTenantCookieService.Validate`; if present + tenant is `Active` (NOT `Suspended` or `Disabled`) + user is a member, set the effective tenant scope without requiring a `/select-tenant` call. **Per FR-013 (analysis C5): when the validated cookie points at a tenant whose current status is `Disabled`, the cookie MUST be ignored and the user routed to the picker; no `TenantSwitch` audit row is written for the ignored cookie.** Test: extend [tests/Ato.Copilot.Tests.Integration/Auth/MeEndpointTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/MeEndpointTests.cs) with three scenarios: (a) remembered cookie + Active tenant skips picker, (b) remembered cookie + Disabled tenant routes to picker with no `TenantSwitch` row, (c) remembered cookie + tampered HMAC routes to picker.

### 5.4 Dashboard `TenantPickerPage`

- [X] T071 [TDD-Test] [P] [US3] [src/Ato.Copilot.Dashboard/src/__tests__/auth/TenantPickerPage.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/TenantPickerPage.test.tsx) — renders one row per tenant with status badge; `Disabled` rows are hidden for non-CSP-Admin AND rendered grayed-out (disabled) for CSP-Admin per FR-010; "Remember on this device" checkbox below list; `POST /api/auth/select-tenant` body matches selection + remember flag. **Per FR-011 (analysis C4): when `me.isCspAdmin === true`, an extra row labeled "All Tenants (CSP view)" MUST render AND clicking it MUST navigate to `/csp/dashboard` (Feature 048 root) without calling `/select-tenant`.** RED.
- [X] T072 [US3] Create [src/Ato.Copilot.Dashboard/src/features/auth/TenantPickerPage.tsx](src/Ato.Copilot.Dashboard/src/features/auth/TenantPickerPage.tsx). GREEN T071.
- [X] T073 [US3] Add `/login/select-tenant` route (wrapped in `<RequireAuth>`) in [src/Ato.Copilot.Dashboard/src/App.tsx](src/Ato.Copilot.Dashboard/src/App.tsx); auto-redirect to it from `LoginCallbackPage` when `me.homeTenant === null` and the user has > 1 tenant membership

### 5.5 (Retired) `useLoginRaceListener` hook — moved to Phase 3 (US1)

T074, T075, T076 were originally listed here. Analysis C3 (2026-05-28)
identified the cross-tab race as US1 work (deep-link preservation),
not US3 (tenant picker). The replacement tasks T053a–T053c live in
Phase 3 § 3.6. **Do NOT re-introduce work under T074–T076.**

### 5.6 Manual sign-off

- [ ] T077 [US3] Execute [quickstart.md § 3, § 7](specs/051-login/quickstart.md) — Pending live verification (deferred like T054 / T063; requires a real tenant with > 1 membership and a real disabled tenant in the deployment).

**Checkpoint US3**: Picker shows tenants, remember cookie round-trips, cross-tab race resolves.

---

## Phase 6: User Story 4 — Login Error States (P1)

**Goal**: Each of the 10 `ErrorClass` values renders a class-specific page
with correlation id + support link, AND writes a `LoginFailure` audit row.

**Independent test criteria**: [quickstart.md § 13](specs/051-login/quickstart.md).

### 6.1 Server: error classification on the existing CAC + Entra paths

- [X] T078 [TDD-Test] [US4] [tests/Ato.Copilot.Tests.Integration/Auth/LoginErrorClassificationTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/LoginErrorClassificationTests.cs) + [tests/Ato.Copilot.Tests.Unit/Auth/LoginErrorClassifierTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/LoginErrorClassifierTests.cs) — unit suite covers all 10 `LoginErrorClass` paths against the pure `LoginErrorClassifier` (FR-014/FR-015 + FR-033 privacy invariant); integration suite covers the one wired end-to-end class today (`NoTenantAssignment` via `/api/auth/me`). Other classes are wired-only and deferred to live induction per quickstart § 13. RED → GREEN.
- [X] T079 [US4] Extend [src/Ato.Copilot.Mcp/Middleware/CacAuthenticationMiddleware.cs](src/Ato.Copilot.Mcp/Middleware/CacAuthenticationMiddleware.cs) — added pure `LoginErrorClassifier` in [src/Ato.Copilot.Core/Services/Auth/LoginErrorClassifier.cs](src/Ato.Copilot.Core/Services/Auth/LoginErrorClassifier.cs) covering the full 10-class taxonomy; wired the `MfaFailure` path (amr missing) to call `ILoginAuditService.AppendAsync` with FR-033-safe `MetadataJson`. Cert/OCSP/clock-skew classifier branches are unit-tested but require a real cert-auth handler on the gateway TLS endpoint (Phase 13).
- [X] T080 [US4] Documented Entra/JWT failure → `LoginErrorClass` map at the auth-scheme registration in [src/Ato.Copilot.Mcp/Program.cs](src/Ato.Copilot.Mcp/Program.cs). `NoTenantAssignment` is wired via `AuthEndpoints.GetMeAsync`; `AccountDisabled` / `ConditionalAccessBlock` are deferred to Phase 13 (no `JwtBearerEvents` in current pipeline — auth is via `CacPassthroughAuthHandler`).

### 6.2 Dashboard `LoginErrorPage` + canned copy

- [X] T081 [P] [US4] Replaced stub copy in [src/Ato.Copilot.Dashboard/src/features/auth/errorCopy.ts](src/Ato.Copilot.Dashboard/src/features/auth/errorCopy.ts) with canonical 10-entry map per FR-014 / FR-015 / FR-016.
- [X] T082 [TDD-Test] [P] [US4] [src/Ato.Copilot.Dashboard/src/__tests__/auth/LoginErrorPage.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/LoginErrorPage.test.tsx) — 17 tests: one per `ErrorClass` value asserting title+body, plus correlation-id rendering, mailto/fallback wording, `ConditionalAccessBlock` remediation-URL link, generic fallback for unknown class, and "Try again" CTA wiring `loginRedirect`. RED → GREEN.
- [X] T083 [US4] Created [src/Ato.Copilot.Dashboard/src/features/auth/LoginErrorPage.tsx](src/Ato.Copilot.Dashboard/src/features/auth/LoginErrorPage.tsx) (Tailwind, reads query params via `useSearchParams`, dispatches `loginRedirect` on Try-Again, surfaces Conditional-Access remediation URL when present) and registered `<Route path="/login/error" element={<LoginErrorPage />} />` in [src/Ato.Copilot.Dashboard/src/App.tsx](src/Ato.Copilot.Dashboard/src/App.tsx) (public route — must NOT be wrapped in `RequireAuth` or failed-auth users loop forever).

### 6.3 Manual sign-off

- [ ] T084 [US4] Execute [quickstart.md § 13](specs/051-login/quickstart.md) for each of the 10 classes — **Pending live verification** (deferred to live-Docker run; classes that require real CAC cert state or Conditional Access policy cannot be simulated in the test harness).

**Checkpoint US4**: Every error class renders correct copy; `LoginFailure` row with correct `ErrorClass` written.

---

## Phase 7: User Story 10 — Audit Trail of Every Login Event (P1)

**Goal**: SOC analysts can query the audit trail; old rows migrate to
immutable cold storage. (Write-side already wired in Phase 2.)

**Independent test criteria**: [quickstart.md § 15, § 16](specs/051-login/quickstart.md).

### 7.1 Audit read service

- [X] T085 [TDD-Test] [US10] [tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditServiceListTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditServiceListTests.cs) — `ListAsync(tenantId, since, take)` returns rows in descending `OccurredAt`, respects `[TenantScoped]` filter (other-tenant rows excluded); `ListSystemTenantAsync` throws `UnauthorizedAccessException` without `Auth.SocAnalyst` claim; with claim, returns rows where `EffectiveTenantId = SYSTEM_TENANT_ID` only. RED.
- [X] T086 [US10] Implement `ListAsync` + `ListSystemTenantAsync` in [src/Ato.Copilot.Core/Services/Auth/LoginAuditService.cs](src/Ato.Copilot.Core/Services/Auth/LoginAuditService.cs) per [contracts/internal-services.md § 1.3](specs/051-login/contracts/internal-services.md) and [research.md § R9](specs/051-login/research.md). GREEN T085.
- [X] T087 [TDD-Test] [P] [US10] [tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditServiceSurfaceTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditServiceSurfaceTests.cs) — reflection assertion: public interface methods are EXACTLY `{ AppendAsync, ListAsync, ListSystemTenantAsync }`. RED then GREEN.

### 7.2 SOC-analyst claim mapping

- [X] T088 [US10] **Verify (analysis C13): read [src/Ato.Copilot.Core/Configuration/RoleClaimMappings*](src/Ato.Copilot.Core/Configuration/) to confirm Feature 003 already exposes a `RoleClaimMappings:Auth.SocAnalyst` config slot. If absent, this task MUST extend Feature 003's claim resolver to recognize `Auth.SocAnalyst` as a first-class role claim before adding the slot to appsettings.** Add `RoleClaimMappings:Auth.SocAnalyst` config slot to [src/Ato.Copilot.Mcp/appsettings.json](src/Ato.Copilot.Mcp/appsettings.json) and wire it through the existing Feature 003 role-claim resolver so endpoints can check the claim

### 7.3 Read endpoint

- [X] T089 [TDD-Test] [US10] [tests/Ato.Copilot.Tests.Integration/Auth/LoginAuditEventsEndpointTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/LoginAuditEventsEndpointTests.cs) — `GET /api/auth/events?since=&take=` returns tenant rows for CSP-Admin or tenant member; `GET /api/auth/events?systemTenant=true` returns 403 `FORBIDDEN_NOT_SOC_ANALYST` without the claim; 200 with it. RED.
- [X] T090 [US10] Add the endpoint to [src/Ato.Copilot.Mcp/Endpoints/Auth/AuthEndpoints.cs](src/Ato.Copilot.Mcp/Endpoints/Auth/AuthEndpoints.cs). GREEN T089.

### 7.4 Archive sinks

- [X] T091 [P] [US10] Create [src/Ato.Copilot.Core/Interfaces/Auth/ILoginAuditArchiveSink.cs](src/Ato.Copilot.Core/Interfaces/Auth/ILoginAuditArchiveSink.cs) per [contracts/internal-services.md § 4.2](specs/051-login/contracts/internal-services.md)
- [X] T092 [TDD-Test] [P] [US10] [tests/Ato.Copilot.Tests.Unit/Auth/FileSystemArchiveSinkTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/FileSystemArchiveSinkTests.cs) — writes one JSON-Lines file per `{root}/{yyyy}/{MM}/login-audit-{guid}.jsonl`; returns the file path; second call appends without overwrite. RED then GREEN.
- [X] T093 [P] [US10] Create [src/Ato.Copilot.Core/Services/Auth/FileSystemArchiveSink.cs](src/Ato.Copilot.Core/Services/Auth/FileSystemArchiveSink.cs)
- [X] T094 [TDD-Test] [P] [US10] [tests/Ato.Copilot.Tests.Unit/Auth/AzureBlobAppendArchiveSinkTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/AzureBlobAppendArchiveSinkTests.cs) — uses Azurite-free mock (`Mock<BlobAppendClient>`): asserts append blob path = `audit-archive/{yyyy}/{MM}/login-audit.jsonl`, retries on transient 5xx, throws on 4xx. RED then GREEN.
- [X] T095 [P] [US10] Create [src/Ato.Copilot.Core/Services/Auth/AzureBlobAppendArchiveSink.cs](src/Ato.Copilot.Core/Services/Auth/AzureBlobAppendArchiveSink.cs)

### 7.5 Archive hosted service

- [X] T096 [P] [US10] Create [src/Ato.Copilot.Core/Interfaces/Auth/ILoginAuditArchiveService.cs](src/Ato.Copilot.Core/Interfaces/Auth/ILoginAuditArchiveService.cs)
- [X] T097 [TDD-Test] [US10] [tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditArchiveServiceTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/LoginAuditArchiveServiceTests.cs) — seeds 2,500 rows older than 13 months + 100 rows newer: assert exactly 2,500 archived in three batches of {1000, 1000, 500}; newer rows untouched; sink failure aborts batch + retains rows; `RunHourUtc` honored. RED.
- [X] T098 [US10] Create [src/Ato.Copilot.Core/Services/Auth/LoginAuditArchiveService.cs](src/Ato.Copilot.Core/Services/Auth/LoginAuditArchiveService.cs) per [contracts/internal-services.md § 4.4](specs/051-login/contracts/internal-services.md). GREEN T097.
- [X] T099 [US10] Register `services.AddHostedService<LoginAuditArchiveService>()` + sink chosen by `Auth:Archive:Sink` in [src/Ato.Copilot.Mcp/Program.cs](src/Ato.Copilot.Mcp/Program.cs)

### 7.6 Manual sign-off

- [ ] T100 [US10] Execute [quickstart.md § 15, § 16](specs/051-login/quickstart.md) — **Pending live verification.** Code path + automated coverage land in T085–T099 (Phase 7.1–7.5); end-to-end SOC-analyst playthrough + 13-month retention sweep validation are deferred until a live Foundry deployment is available.

**Checkpoint US10**: Read endpoint enforces claim; archive job moves > 13-month rows.

---

## Phase 8: User Story 5 — Login from VS Code Extension (P2)

**Goal**: VS Code extension uses MSAL Node device-code flow with per-tenant
`SecretStorage`. Status bar reflects state. `@ato sign in / sign out / switch tenant`.

**Independent test criteria**: [quickstart.md § 8](specs/051-login/quickstart.md).

### 8.1 MSAL Node configuration

- [X] T101 [P] [US5] Create [extensions/vscode/src/auth/msalNode.ts](extensions/vscode/src/auth/msalNode.ts) — `buildMsalNodeConfig` + `VsCodeLoginConfig` per [contracts/vscode-extension.md § 2](specs/051-login/contracts/vscode-extension.md). **Per FR-017 (analysis C14): include the `cloud` field on `VsCodeLoginConfig` and implement the validator described in [contracts/vscode-extension.md § 2.1](specs/051-login/contracts/vscode-extension.md) that maps `AzurePublic` → `https://microsoft.com/devicelogin` and `AzureUSGovernment` → `https://microsoft.us/devicelogin`.**
- [X] T102 [P] [US5] Create [extensions/vscode/src/auth/secretStorage.ts](extensions/vscode/src/auth/secretStorage.ts) — per-tenant token + account persistence using the `ato.auth.token.{tenantId}` / `ato.auth.account.{tenantId}` key naming per [contracts/vscode-extension.md § 5](specs/051-login/contracts/vscode-extension.md)
- [X] T103 [TDD-Test] [P] [US5] [extensions/vscode/test/auth/secretStorage.test.ts](extensions/vscode/test/auth/secretStorage.test.ts) — mocha + `vscode-test`: persist + read + delete per tenant key, signing out one tenant leaves another intact. RED then GREEN.

### 8.2 Sign-in / out / switch commands

- [X] T104 [TDD-Test] [US5] [extensions/vscode/test/auth/signInCommand.test.ts](extensions/vscode/test/auth/signInCommand.test.ts) — mocks `PublicClientApplication`: assert device-code callback shows the notification with verification URL + code, copies code to clipboard on action click, persists token under correct tenant key, updates status bar to `signedIn`. **Per FR-017 (analysis C14): add two cases asserting `cloud=AzurePublic` requires the response's `verificationUri` to start with `https://microsoft.com/devicelogin` and `cloud=AzureUSGovernment` requires `https://microsoft.us/devicelogin`; a mismatched URL aborts sign-in with a clear error.** RED.
- [X] T105 [US5] Create [extensions/vscode/src/auth/signInCommand.ts](extensions/vscode/src/auth/signInCommand.ts) per [contracts/vscode-extension.md § 3.2](specs/051-login/contracts/vscode-extension.md). GREEN T104.
- [X] T106 [P] [US5] Create [extensions/vscode/src/auth/signOutCommand.ts](extensions/vscode/src/auth/signOutCommand.ts) per [contracts/vscode-extension.md § 3.3](specs/051-login/contracts/vscode-extension.md)
- [X] T107 [P] [US5] Create [extensions/vscode/src/auth/switchTenantCommand.ts](extensions/vscode/src/auth/switchTenantCommand.ts) per [contracts/vscode-extension.md § 3.4](specs/051-login/contracts/vscode-extension.md)
- [X] T108 [P] [US5] Create [extensions/vscode/src/auth/statusBar.ts](extensions/vscode/src/auth/statusBar.ts) — 4 states per [contracts/vscode-extension.md § 4](specs/051-login/contracts/vscode-extension.md)
- [X] T109 [US5] Register `ato.signIn`, `ato.signOut`, `ato.switchTenant` commands + activate the status bar item in [extensions/vscode/src/extension.ts](extensions/vscode/src/extension.ts) and add them to [extensions/vscode/package.json](extensions/vscode/package.json) `contributes.commands`
- [X] T110 [P] [US5] Replace any direct bearer-token reads in [extensions/vscode/src/](extensions/vscode/src/) with a `getActiveTenantToken(context)` helper that calls `pca.acquireTokenSilent` first and falls back to device-code per FR-018 / R-Summary item 1

### 8.3 Manual sign-off

- [ ] T111 [US5] Execute [quickstart.md § 8](specs/051-login/quickstart.md)
  > **Pending live verification** — quickstart § 8 is a live VS Code Extension Host + Docker stack test that requires manual execution after Phase 8 commits land. Deferred per Phase 8 commit-strategy instructions.

**Checkpoint US5**: Device-code flow works; per-tenant tokens persisted; status bar live.

---

## Phase 9: User Story 6 — Login from M365 / Teams Bot (P2)

**Goal**: Teams bot uses Bot Framework SSO when available, falls back to
`OAuthPrompt`. `Auth:TeamsSso:Mode` controls behavior. `Required` mode
fails startup if manifest does not advertise SSO.

**Independent test criteria**: [quickstart.md § 9](specs/051-login/quickstart.md).

### 9.1 Manifest validator (startup-fail)

- [X] T112 [TDD-Test] [US6] [tests/Ato.Copilot.Tests.Unit/Auth/TeamsManifestValidatorTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/TeamsManifestValidatorTests.cs) — `Mode=Required` + manifest with empty `webApplicationInfo.id` → `OptionsValidationException` at `IValidateOptions<AuthOptions>.Validate`; `Mode=Required` + populated manifest → success; `Mode=Optional` regardless of manifest → success. RED.
- [X] T113 [US6] Extend [src/Ato.Copilot.Core/Configuration/Auth/AuthOptionsValidator.cs](src/Ato.Copilot.Core/Configuration/Auth/AuthOptionsValidator.cs) to read the Teams manifest (path injected via `IHostEnvironment.ContentRootPath`) and enforce the Required-mode rule per [research.md § R12](specs/051-login/research.md). GREEN T112.

### 9.2 Auth dispatcher

- [X] T114 [P] [US6] Create [extensions/m365/src/auth/identityStore.ts](extensions/m365/src/auth/identityStore.ts) per [contracts/m365-bot.md § 3.2](specs/051-login/contracts/m365-bot.md) — in-memory `Map` for dev + `IConversationStateManager` adapter for prod
- [X] T115 [TDD-Test] [US6] [extensions/m365/test/auth/dispatcher.test.ts](extensions/m365/test/auth/dispatcher.test.ts) — jest: `Mode=Disabled` always returns null (→ OAuthPrompt); `Mode=Optional` + `getUserToken` returns token → returns token; `Mode=Optional` + `getUserToken` returns null → returns null (→ fallback); `Mode=Required` + `getUserToken` throws → re-throws (unreachable case). RED.
- [X] T116 [US6] Create [extensions/m365/src/auth/dispatcher.ts](extensions/m365/src/auth/dispatcher.ts) per [contracts/m365-bot.md § 3.1](specs/051-login/contracts/m365-bot.md). GREEN T115.

### 9.3 OAuthPrompt fallback wiring

- [X] T117 [US6] Modify [extensions/m365/src/bot.ts](extensions/m365/src/bot.ts) — first `@mention` runs `AuthDispatcher.resolveToken`; on null, run existing `OAuthPrompt` dialog with the sign-in Adaptive Card from [contracts/m365-bot.md § 3.3](specs/051-login/contracts/m365-bot.md); persist into `identityStore` on success **— NOTE: actual entrypoint is `extensions/m365/src/index.ts` (Express-based, no separate `bot.ts`). Dispatcher + sign-in card wired into the existing `POST /api/messages` handler. Gate is opt-in via `AUTH_TEAMS_SSO_MODE` env var (defaults to `Disabled` to preserve legacy behavior).**
- [X] T118 [US6] Add intent handler for "sign out" — calls `adapter.signOutUser` + `identityStore.delete` per [contracts/m365-bot.md § 5](specs/051-login/contracts/m365-bot.md)

### 9.4 Manual sign-off

- [ ] T119 [US6] Execute [quickstart.md § 9](specs/051-login/quickstart.md) **— Pending live verification (requires deployed Teams app + bot tunnel).**

**Checkpoint US6**: Teams bot SSO + OAuthPrompt fallback works; `Required` mode fails startup correctly.

---

## Phase 10: User Story 7 — Dev Simulation Login Without Restart (P2)

**Goal**: In Development only, a developer picks a configured simulated
identity to sign in instantly. Three-layer gate (config endpoint omits panel,
SPA route guard refuses to mount, simulate endpoint returns 404) blocks
non-Development.

**Independent test criteria**: [quickstart.md § 10](specs/051-login/quickstart.md).

### 10.1 Server: extend simulated-identity config

- [X] T120 [P] [US7] Create [src/Ato.Copilot.Core/Configuration/Auth/SimulatedIdentityDescriptor.cs](src/Ato.Copilot.Core/Configuration/Auth/SimulatedIdentityDescriptor.cs) — record per [data-model.md § 4](specs/051-login/data-model.md)
- [X] T121 [US7] Extend the existing Feature 027 `CacAuthOptions` to expose `SimulatedIdentities: SimulatedIdentityDescriptor[]` (replacing the single-identity shape); preserve backward-compat by treating a missing list as `[]`

### 10.2 Endpoint: POST /api/auth/simulate

- [X] T122 [TDD-Test] [US7] [tests/Ato.Copilot.Tests.Integration/Auth/SimulateEndpointTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/SimulateEndpointTests.cs) — `ASPNETCORE_ENVIRONMENT=Development` + known identity → 204 + session cookie issued **AND a discrete `X-Simulated=true` cookie issued with `HttpOnly=true, Secure=true, SameSite=Strict` per FR-025 (analysis C9)** + `SimulatedLogin` row; unknown identity → 404 `SIMULATED_IDENTITY_NOT_FOUND`; `ASPNETCORE_ENVIRONMENT=Staging` → bare 404 (no envelope) + `SimulationBlocked` audit row with `MetadataJson.environment="Staging"`. RED. **Implementation note**: Development-branch coverage (known/unknown identity, missing query, X-Simulated cookie issuance) lives in [tests/Ato.Copilot.Tests.Unit/Auth/SimulationGateTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/SimulationGateTests.cs) because flipping `ASPNETCORE_ENVIRONMENT` mid-test destabilises the `WebApplicationFactory` listener (the env var is process-global and bootstrap Serilog reads it directly). The integration test exercises Layer 3 of the gate (non-Development → bare 404 + audit row), which is the security-critical path.
- [X] T123 [US7] Implement `POST /api/auth/simulate` per [contracts/http-api.md § 5](specs/051-login/contracts/http-api.md). **Issue the `X-Simulated` discrete cookie (not a cookie attribute) per the analysis C9 clarification of FR-025.** GREEN T122.

### 10.3 SimulationBlocked logging severity

- [X] T124 [US7] Wire a Serilog scope tag `severity=Security` on the `SimulationBlocked` log line (per FR-024) at the endpoint, so downstream SIEM can elevate the event

### 10.4 Server: extend login-config response

- [X] T125 [US7] Extend the `GET /api/auth/login-config` handler to include `simulation: { identities: [...] }` ONLY when `env=Development AND CacAuth:SimulationMode=true AND CacAuth:SimulatedIdentities non-empty`. **Per analysis C10: the gate uses Feature 027's `CacAuth:SimulationMode` ONLY — do NOT add or check a parallel `Auth:Simulation:Enabled` flag.** **Implementation note**: extracted to a pure-function `SimulationGate.ShouldSurfaceDescriptor` + `BuildDescriptor` helper unit-tested in [tests/Ato.Copilot.Tests.Unit/Auth/SimulationGateTests.cs](tests/Ato.Copilot.Tests.Unit/Auth/SimulationGateTests.cs) (Development × mode × identities matrix). The integration test in [tests/Ato.Copilot.Tests.Integration/Auth/LoginConfigEndpointTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/LoginConfigEndpointTests.cs) verifies the wired non-Development path (env=Testing → simulation is null).

### 10.5 Dashboard: `SimulationPanel`

- [X] T126 [TDD-Test] [P] [US7] [src/Ato.Copilot.Dashboard/src/__tests__/auth/SimulationPanel.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/SimulationPanel.test.tsx) — when `useLoginConfig().simulation` is null, component returns `null` even if a `force` prop is passed (route guard); when non-null, lists identities + click POSTs to `/api/auth/simulate`. RED.
- [X] T127 [US7] Create [src/Ato.Copilot.Dashboard/src/features/auth/SimulationPanel.tsx](src/Ato.Copilot.Dashboard/src/features/auth/SimulationPanel.tsx). GREEN T126.
- [X] T128 [US7] Mount `<SimulationPanel />` on `LoginPage` below the Sign In buttons; guard with `useLoginConfig().simulation != null`

### 10.6 Manual sign-off

- [ ] T129 [US7] Execute [quickstart.md § 10](specs/051-login/quickstart.md) **— Pending live verification** (requires Docker MCP stack + dashboard + manual flips of `ASPNETCORE_ENVIRONMENT` to confirm all 3 gates behave as specified in vivo).

**Checkpoint US7**: Simulation panel renders in Development only; all 3 gates active.

---

## Phase 11: User Story 8 — CSP-Admin Tenant Switching Mid-Session (P2)

**Goal**: CSP-Admin impersonates a customer tenant via the existing Feature
048 endpoint; the dashboard shows a sticky banner with countdown; auto-end
on expire / sign-out / idle.

**Independent test criteria**: [quickstart.md § 11](specs/051-login/quickstart.md).

### 11.1 Pre-impersonation URL capture (FR-029, analysis C6)

- [ ] T129a [TDD-Test] [P] [US8] [src/Ato.Copilot.Dashboard/src/__tests__/auth/preImpersonationUrl.test.ts](src/Ato.Copilot.Dashboard/src/__tests__/auth/preImpersonationUrl.test.ts) — Vitest: assert the "Switch into tenant" affordance writes the current `window.location.pathname + search + hash` to a session-scoped key (`ato.preImpersonationUrl`) BEFORE issuing the Feature 048 impersonate request; assert key removed on explicit Exit; assert key removed on auto-expire. RED.
- [ ] T129b [US8] Create [src/Ato.Copilot.Dashboard/src/features/auth/preImpersonationUrl.ts](src/Ato.Copilot.Dashboard/src/features/auth/preImpersonationUrl.ts) — thin getter/setter/clearer for the sessionStorage key. GREEN T129a.

### 11.2 Audit rows for impersonation transitions

- [ ] T130 [TDD-Test] [US8] [tests/Ato.Copilot.Tests.Integration/Auth/ImpersonationAuditTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/ImpersonationAuditTests.cs) — POST to the existing Feature 048 impersonate endpoint writes `ImpersonationStart` audit row; DELETE / Exit writes `ImpersonationEnd` with `reason=manual`; auto-expiry writes `ImpersonationEnd` with `reason=expired`; idle sign-out also closes impersonation with `reason=idle_timeout`. RED.
- [ ] T131 [US8] Extend the existing Feature 048 impersonation start handler in [src/Ato.Copilot.Mcp/Endpoints/](src/Ato.Copilot.Mcp/Endpoints/) (locate via `grep -rln "Impersonate" src/Ato.Copilot.Mcp/Endpoints/`) to write `ImpersonationStart` via `ILoginAuditService`. GREEN T130 (start).
- [ ] T132 [US8] Extend the impersonation-end paths (manual + expiry + idle) to write `ImpersonationEnd` with the correct `reason`. GREEN T130 (end).

### 11.3 Dashboard `ImpersonationBanner`

- [ ] T133 [TDD-Test] [P] [US8] [src/Ato.Copilot.Dashboard/src/__tests__/auth/ImpersonationBanner.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/ImpersonationBanner.test.tsx) — renders when `me.isImpersonating === true`; shows impersonated-tenant name + countdown computed from `expiresAt`; "Exit" button calls the Feature 048 end endpoint then refetches `/me`. **Per FR-029 (analysis C6): on Exit click, the SPA MUST navigate to the value of `sessionStorage.getItem('ato.preImpersonationUrl')` if present, falling back to the persona-default landing page when the key is absent or stale (not to `/csp/dashboard` or `/`).** RED.
- [ ] T134 [US8] Create [src/Ato.Copilot.Dashboard/src/features/auth/ImpersonationBanner.tsx](src/Ato.Copilot.Dashboard/src/features/auth/ImpersonationBanner.tsx). GREEN T133.
- [ ] T135 [US8] Mount `<ImpersonationBanner />` at the top of `AppShell` (sticky); refetch on `'ato:tenant-changed'` event. **Wire the Exit click handler to read + clear `sessionStorage['ato.preImpersonationUrl']` and call `navigate()` to that URL per FR-029 (analysis C6).**

### 11.4 Manual sign-off

- [ ] T136 [US8] Execute [quickstart.md § 11](specs/051-login/quickstart.md)

**Checkpoint US8**: Banner appears; countdown ticks; all three end-paths write the correct audit row.

---

## Phase 12: User Story 9 — Account Menu and Profile Surface (P3)

**Goal**: Header dropdown shows display name, persona, home tenant, active
PIM role (with expiry), sign-out button. (Sign-out button already wired in
Phase 4 — this phase fills the rest.)

**Independent test criteria**: [quickstart.md § 12](specs/051-login/quickstart.md).

### 12.1 `useMe` hook

- [ ] T137 [TDD-Test] [P] [US9] [src/Ato.Copilot.Dashboard/src/__tests__/auth/useMe.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/useMe.test.tsx) — React Query wrapper around `GET /api/auth/me`; 5-min stale time; refetch on window focus; refetch on `'ato:tenant-changed'` custom event. RED.
- [ ] T138 [US9] Create [src/Ato.Copilot.Dashboard/src/features/auth/useMe.ts](src/Ato.Copilot.Dashboard/src/features/auth/useMe.ts) per [contracts/frontend-types.md § 4.3](specs/051-login/contracts/frontend-types.md). GREEN T137.

### 12.2 Account menu

- [ ] T139 [TDD-Test] [US9] [src/Ato.Copilot.Dashboard/src/__tests__/auth/AccountMenu.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/AccountMenu.test.tsx) — renders displayName + persona + homeTenant.displayName; shows each PIM role with `expiresAt`; auto-hides PIM rows whose `expiresAt` is in the past; sign-out button still works (regression-guard for Phase 4 wire-up). **Per FR-031 (analysis C8): assert `aria-expanded` toggles on open/close, Esc key closes the menu, focus is trapped while open, Tab/Shift-Tab cycles inside the menu, AND a screen-reader live-region announces the active PIM role's expiry on render (`aria-live="polite"`).** RED.
- [ ] T140 [US9] Extend [src/Ato.Copilot.Dashboard/src/features/auth/AccountMenu.tsx](src/Ato.Copilot.Dashboard/src/features/auth/AccountMenu.tsx) — replace the Phase-4 stub with the full menu. GREEN T139.

### 12.3 Manual sign-off

- [ ] T141 [US9] Execute [quickstart.md § 12](specs/051-login/quickstart.md)

**Checkpoint US9**: Profile surface displays all expected fields.

---

## Phase 13: Polish & Cross-Cutting Concerns

### 13.1 Throttle middleware integration

- [ ] T142 [TDD-Test] [tests/Ato.Copilot.Tests.Integration/Auth/LoginThrottleMiddlewareTests.cs](tests/Ato.Copilot.Tests.Integration/Auth/LoginThrottleMiddlewareTests.cs) — 21st failed-login attempt from same IP in Production env (analysis C2 — spec FR-034 default is 20/min/IP) returns 429 with `Retry-After` header + `error.errorCode=TOO_MANY_LOGINS` envelope + `LoginFailure` audit row with `MetadataJson.throttled=true`. **Per analysis C11: also assert that `ASPNETCORE_ENVIRONMENT=Staging` uses the Production block (NOT Development), so the 21st attempt also throttles.** RED.
- [ ] T143 Create [src/Ato.Copilot.Mcp/Middleware/LoginThrottleMiddleware.cs](src/Ato.Copilot.Mcp/Middleware/LoginThrottleMiddleware.cs) — wraps the auth-gated endpoints, calls `ILoginThrottleService.RegisterAttemptAsync` per [contracts/http-api.md § 6](specs/051-login/contracts/http-api.md). **Per analysis C17: increment the throttle counter ONLY on response statuses `401 UNAUTHORIZED` AND `403 FORBIDDEN_NO_TENANT_ASSIGNMENT` (failed-auth signals); a 2xx, 4xx-validation, or 5xx response MUST NOT increment the counter. Unit-test the signal selector in isolation.** GREEN T142.
- [ ] T144 Wire the middleware AFTER `CacAuthenticationMiddleware` in [src/Ato.Copilot.Mcp/Program.cs](src/Ato.Copilot.Mcp/Program.cs)

### 13.2 Logging discipline (FR-038)

- [ ] T145 Audit every Serilog `LogXxx` call introduced by this feature (greppable: `grep -rn "LogInformation\|LogWarning\|LogError" src/Ato.Copilot.Core/Services/Auth/ src/Ato.Copilot.Mcp/Endpoints/Auth/ src/Ato.Copilot.Mcp/Middleware/`); confirm no access tokens, refresh tokens, cert thumbprints, or `MetadataJson` raw payloads appear in log message arguments; add `LogContext.PushProperty("Surface", ...)` scopes where missing

### 13.3 WCAG 2.1 AA accessibility (FR-039, analysis C7)

- [ ] T144a [TDD-Test] [P] [src/Ato.Copilot.Dashboard/src/__tests__/auth/a11y.test.tsx](src/Ato.Copilot.Dashboard/src/__tests__/auth/a11y.test.tsx) — wire `jest-axe` (`vitest-axe`) and run `axe` against `<LoginPage />`, `<TenantPickerPage />`, `<AccountMenu />` (opened state), `<ImpersonationBanner />`, `<LoginErrorPage errorClass="ClockSkew" />`, `<IdleWarningModal />`, and `<RestoreUnsavedChangesPrompt />`. Assert zero violations of category `wcag2a, wcag2aa, wcag21a, wcag21aa, cat.aria, cat.color`. RED initially; **fix the components, not the test**, until GREEN. Add a Lighthouse / pa11y CLI run to [quickstart.md](specs/051-login/quickstart.md) as a manual sign-off step (operator runs `npx pa11y http://localhost:5174/login` and asserts no errors).

### 13.4 Local type-check parity

- [ ] T146 [P] Run `npm --prefix src/Ato.Copilot.Dashboard run typecheck` and fix any new errors
- [ ] T147 [P] Run `npm --prefix extensions/vscode run compile` and fix any new errors
- [ ] T148 [P] Run `npm --prefix extensions/m365 run build` and fix any new errors

### 13.5 GitHub issue discipline (Constitution NON-NEGOTIABLE)

- [ ] T149 Open the Feature 051 parent issue on GitHub with the spec summary + link to [specs/051-login/spec.md](specs/051-login/spec.md), [plan.md](specs/051-login/plan.md), [tasks.md](specs/051-login/tasks.md). PREVIEW the body to the user before creating per non-negotiable rule #10.
- [ ] T150 [P] Open 10 sub-issues (one per User Story US1–US10), each linked to the parent via "Parent: #<id>" reference per Constitution § DevOps GitHub Issue Discipline. PREVIEW each body before creating.

### 13.6 Documentation

- [ ] T151 [P] Add `docs/features/051-login.md` to MkDocs with screenshots from [quickstart.md](specs/051-login/quickstart.md) sign-off and a link back to the spec
- [ ] T152 [P] Update [docs/architecture/](docs/architecture/) `auth.md` (or create) with a sequence diagram of the Dashboard MSAL flow + VS Code device-code flow + M365 SSO branching
- [ ] T153 [P] Update [extensions/vscode/README.md](extensions/vscode/README.md) with the new `@ato sign in / sign out / switch tenant` commands
- [ ] T154 [P] Update [extensions/m365/README.md](extensions/m365/README.md) with the `Auth:TeamsSso:Mode` 3-mode setting

### 13.7 Final verification

- [ ] T155 Run full unit test pass: `dotnet test tests/Ato.Copilot.Tests.Unit/Ato.Copilot.Tests.Unit.csproj --filter "FullyQualifiedName~Auth"`
- [ ] T156 Run full integration test pass: `dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj --filter "FullyQualifiedName~Auth"`
- [ ] T157 Run full dashboard test pass: `npm --prefix src/Ato.Copilot.Dashboard test -- --run`
- [ ] T158 Run VS Code extension tests: `npm --prefix extensions/vscode test`
- [ ] T159 Run M365 bot tests: `npm --prefix extensions/m365 test`
- [ ] T160 Bring up `docker compose -f docker-compose.mcp.yml up --build -d` and walk through every quickstart section. Tick the sign-off checklist at [quickstart.md § 18](specs/051-login/quickstart.md).
- [ ] T161 Commit + request user approval to push (per non-negotiable rule #9)

**Checkpoint Polish**: All tests green; documentation updated; GitHub issues filed; quickstart fully signed off.

---

## Dependencies

### Phase order (must complete left → right)

```text
Phase 1 → Phase 2 → ┬→ Phase 3 (US1) ─┬→ Phase 4 (US2) ─┬→ Phase 5 (US3) ─┬→ Phase 6 (US4)
                    │                  │                  │                  │
                    │                  │                  │                  ↓
                    └→ Phase 7 (US10) ─┘                  └─────────────────→ Phase 11 (US8)*
                                                                              │
        Phase 8 (US5)  ←┐                                                     │
        Phase 9 (US6)  ←┤── all P2 stories can start any time after Phase 2   │
        Phase 10 (US7) ←┘                                                     │
                                                                              ↓
                                                                       Phase 12 (US9)
                                                                              │
                                                                              ↓
                                                                        Phase 13 (Polish)
```

\* Phase 11 (US8 — impersonation banner) depends on Phase 3 (`MeResponse.impersonation` field).

### Story-level dependency notes

- **US1 → US2 / US3 / US4 / US10**: US2/US3/US4 reuse `LoginPage` and the MSAL plumbing from US1. US10's read endpoint reuses `MeResponse` for caller-identity gating.
- **US2 → US9**: US9 extends the account-menu stub built in US2 (T062 vs T140).
- **US3 → US8**: US8 reuses the tenant-switching plumbing from US3 (`MeResponse.effectiveTenant`).
- **US4 → US7**: US7's `SimulationBlocked` shares the `LoginFailure`-style envelope from US4.
- **US10 ← Phase 2**: Audit-write half lands in Phase 2 (foundational); only the read + archive halves belong to US10.

### Parallel opportunities

Within Phase 2:
- T009, T010, T011 in parallel (different enum files)
- T021, T022, T029 in parallel (different files)
- T033, T034, T035 in parallel (different dashboard files)

Within Phase 3 (US1):
- T046, T049 in parallel (LoginPage test, LoginCallbackPage test)
- T053a in parallel with the page-test pair (login-race hook test)

Within Phase 4 (US2):
- T057, T059, T062a, T062c in parallel (independent test files)

Within Phase 5 (US3):
- T064, T071 in parallel (interface, page test)

Within Phase 7 (US10):
- T091, T092, T094, T096 in parallel (interface + sink tests)
- T093, T095 in parallel (sink impls)

Within Phase 8 (US5):
- T101, T102, T106, T107, T108, T110 in parallel (all separate files)

Within Phase 13:
- T146, T147, T148 in parallel (three typecheck runs)
- T150, T151, T152, T153, T154 in parallel (issues + docs are independent)

### Cross-story parallel opportunities (after Phase 2 done)

- Phase 3 (US1) and Phase 7 (US10 read+archive halves) can run in parallel — different files
- Phase 8 (US5), Phase 9 (US6), Phase 10 (US7) can ALL run in parallel after Phase 2 — different surfaces (VS Code / M365 / Dashboard sim panel)

---

## Implementation Strategy

### MVP scope (P1 only)

**Phases 1, 2, 3, 4, 5, 6, 7** — Setup + Foundation + US1 + US2 + US3 + US4 + US10.

This delivers the four critical P1 outcomes:

1. Branded `/login` page with sign-in + deep-link preservation (US1)
2. Explicit + idle sign-out, with silent-renewal exception (US2)
3. Tenant picker + remember cookie (US3)
4. Per-class error pages with audit rows (US4)
5. Full audit trail with SOC-analyst read + 13-month archive (US10)

Approx. tasks T001–T100 (100 tasks).

### Incremental P2 delivery

After MVP ships, the P2 phases can land in any order:

- **Phase 8 (US5 — VS Code)** lands independently — touches only `extensions/vscode/`.
- **Phase 9 (US6 — M365)** lands independently — touches only `extensions/m365/` + the `AuthOptionsValidator` for manifest-startup check.
- **Phase 10 (US7 — Simulation)** lands independently — extends an existing Feature-027 path + adds one endpoint.
- **Phase 11 (US8 — Impersonation)** lands after US3 (depends on `MeResponse.effectiveTenant`).

### P3 follow-up

- **Phase 12 (US9 — Account menu)** lands last; cosmetic on top of the existing US2 stub.

### Polish before ship

- **Phase 13** runs at the end. Throttle middleware (T142–T144) is the gating P1 NFR — without it, FR-034 is unmet.

---

## Format Validation

All 165 active tasks above (T074–T076 retired per analysis C3) conform to:

- ✅ Checkbox `- [ ]` prefix on every task
- ✅ Sequential `T###` IDs starting at T001
- ✅ `[P]` marker on parallelizable tasks (different files, no in-phase dependencies)
- ✅ `[USn]` story label on every story-phase task; NO story label on Setup / Foundational / Polish
- ✅ Exact file path or shell command in every description
- ✅ TDD pairing: every implementation task is preceded by its `[TDD-Test]` RED-phase test

## Task count summary

| Phase | Title | Tasks | Story |
|---|---|---|---|
| 1 | Setup | T001–T008 (8) | — |
| 2 | Foundational | T009–T040 (32) | — |
| 3 | First-Time Login (Dashboard) | T041–T054 + T053a–T053c (17) | US1 P1 |
| 4 | Sign Out + Idle | T055–T063 + T062a–T062d (13) | US2 P1 |
| 5 | Tenant Picker | T064–T077 (11 active; T074–T076 RETIRED per C3) | US3 P1 |
| 6 | Login Error States | T078–T084 (7) | US4 P1 |
| 7 | Audit Trail | T085–T100 (16) | US10 P1 |
| 8 | VS Code Sign-In | T101–T111 (11) | US5 P2 |
| 9 | M365 Teams Bot | T112–T119 (8) | US6 P2 |
| 10 | Dev Simulation | T120–T129 (10) | US7 P2 |
| 11 | CSP-Admin Impersonation | T129a–T129b + T130–T136 (9) | US8 P2 |
| 12 | Account Menu | T137–T141 (5) | US9 P3 |
| 13 | Polish | T142–T161 + T144a (21) | — |
| **Total** | | **165 active** (T074–T076 retired) | |

**Parallel opportunities**: 41 tasks marked `[P]`.

**Analysis remediation applied (2026-05-28)**:

- **C1** (CRITICAL FR-008): added T062a–T062d in Phase 4
- **C2** (CRITICAL throttle defaults): tightened T030 + T142 to FR-034 values (20 IP / 10 identity in Production)
- **C3** (login race misplacement): T074–T076 retired; T053a–T053c added to Phase 3 (US1)
- **C4** (FR-011 "All Tenants (CSP view)" row): tightened T071
- **C5** (FR-013 Disabled tenant cookie ignore): tightened T070
- **C6** (FR-029 return URL): added T129a–T129b; tightened T133 + T135
- **C7** (FR-039 WCAG): added T144a
- **C8** (FR-031 keyboard / ARIA): tightened T139
- **C9** (FR-025 `X-Simulated` as cookie name): tightened T122 + T123
- **C10** (duplicate simulation flag): tightened T125; contracts/internal-services.md updated
- **C11** (non-Dev → Production throttle block): tightened T142
- **C12** (FR-005 wording): spec.md FR-005 reworded
- **C13** (Feature 003 RoleClaimMappings verification): tightened T088
- **C14** (Cloud → device-code URL mapping): tightened T101 + T104; contracts/vscode-extension.md § 2.1 added
- **C15** (LoginAttemptCounter cross-ref): added data-model.md § 4.1
- **C16** (lockfile commits): tightened T002 + T003 + T004
- **C17** (throttle counter signal selection): tightened T143

**Independent test criteria per story**: every story phase ends with a manual sign-off task linking to its quickstart section.

**Suggested MVP**: P1 phases only (Phases 1–7) — ~104 tasks, delivers FR-001 through FR-016 + FR-030 through FR-036a.
