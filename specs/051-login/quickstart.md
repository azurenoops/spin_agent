# Phase 1 — Quickstart: Verify Feature 051 Locally

**Feature**: 051-login
**Plan**: [plan.md](./plan.md)
**Spec**: [spec.md](./spec.md)
**Date**: 2026-05-28

This recipe walks through every user story (US1–US10) end-to-end against
the local docker-compose stack. Run it after `/speckit.tasks` produces
`tasks.md` and the implementation is at "Phase 8 ready" — the same gate
used by Feature 050.

**Prerequisites**: Docker Desktop running; `dotnet 9.0` SDK; `node 20`;
the local `ato-copilot` repo at branch `051-login`.

## 0. Prepare the stack

```bash
# Bring up the Docker stack with Redis + MCP + Web Chat
docker compose -f docker-compose.mcp.yml up --build -d

# Wait for the MCP container to report ready
docker compose -f docker-compose.mcp.yml logs -f ato-copilot-mcp | grep "Now listening on"

# Verify the new LoginAuditEvents table exists
docker exec -it stark-sql /opt/mssql-tools18/bin/sqlcmd -S localhost \
  -U sa -P "$SA_PASSWORD" -C -d AtoCopilot \
  -Q "SELECT name FROM sys.tables WHERE name = 'LoginAuditEvents';"
# Expect: 1 row with name = LoginAuditEvents

# Verify Redis is in-network and reachable
docker exec ato-copilot-redis redis-cli PING
# Expect: PONG
```

If any step fails, STOP and read the failing container's logs. Do NOT
proceed.

## 1. US1 — Branded `/login` page (FR-001 — FR-003)

```bash
# Open the dashboard in a private window
open "http://localhost:5174/login"
```

**Verify**:

- The page shows the deployment name from `Auth:Branding:DeploymentName`.
- The Sign In button reflects `Auth:DefaultMethod = "Cac"`.
- No bearer token is in `localStorage` (open DevTools → Application →
  Local Storage; verify `auth_token` is absent).
- The URL is exactly `/login` — no auto-redirect on first visit.

## 2. US2 — Deep-link preservation (FR-004)

```bash
# Open a deep-linked URL while signed out
open "http://localhost:5174/dashboard/systems/abc-123/controls"
```

**Verify**:

- The browser lands on `/login?returnUrl=%2Fdashboard%2Fsystems%2Fabc-123%2Fcontrols`.
- After successful sign-in (US4), the SPA navigates to the original deep
  link, NOT to `/dashboard`.

## 3. US3 — Tenant picker + remember (FR-009 — FR-013)

Pre-req: configure a test user with membership in two tenants (the
existing `seed-systems.sh` covers this).

```bash
# Sign in as the multi-tenant user
open "http://localhost:5174/login"
# Choose the Entra path
```

**Verify**:

- After authentication, the SPA lands on `/login/select-tenant` with
  both tenants visible.
- Each tenant shows a status badge (`Active`, `Suspended`, `Disabled`).
- A `Disabled` tenant is grayed out and not clickable (unless the user
  is CSP-Admin).
- The "Remember this tenant on this device" checkbox is BELOW the list.
- Select one tenant + check the box.
- The browser sets a cookie `ato-remembered-tenant=<4-part-hmac>`.
- On the next sign-in (via incognito-but-share-cookie OR new tab same
  domain), the picker is SKIPPED — the SPA goes straight to the
  dashboard for the remembered tenant.

## 4. US4 — Sign-in success path (FR-005)

**Verify**:

- After successful sign-in, the dashboard renders.
- A row appears in `LoginAuditEvents` with `EventType = LoginSuccess`,
  the user's `oid`, and `Surface = Dashboard`:

  ```sql
  SELECT TOP 5 EventType, Oid, Surface, OccurredAt
  FROM LoginAuditEvents
  ORDER BY OccurredAt DESC;
  ```

## 5. US5 — Sign-out (FR-006)

**Verify**:

- Click the account-menu sign-out item.
- The browser navigates to `/login?reason=signed_out`.
- `localStorage` is empty (MSAL cache cleared).
- The `ato-remembered-tenant` cookie is NOT cleared (per device-only
  contract — sign-out from one user does not forget the tenant).
- A `SignOut` audit row appears.

## 6. US5b — Idle timeout (FR-007 / FR-007a)

```bash
# For fast iteration, override IdleTimeoutMinutes to 1
docker compose -f docker-compose.mcp.yml exec ato-copilot-mcp \
  sh -c 'echo "Auth__IdleTimeoutMinutes=1" >> /app/.env'
docker compose -f docker-compose.mcp.yml restart ato-copilot-mcp
```

**Verify**:

- Sign in, then leave the tab idle for 60 seconds.
- At T-30s before expiry, an "Are you still there?" modal renders.
- Click nothing; wait the rest.
- The SPA calls `POST /api/auth/signout {"reason": "idle_timeout"}`.
- An `IdleSignOut` audit row appears.
- Reset `IdleTimeoutMinutes` to 30 before continuing.

**FR-007a verification**:

- Sign in.
- Open DevTools Network tab and force a `401` on any API call (use the
  browser's "Block request URL" debug tool on `/api/me`).
- Observe MSAL's silent renewal retry — there is NO `ato:user-input`
  event dispatched on that retry.

## 7. US5c — Cross-tab login race (FR-008)

**Verify**:

- Open two tabs at `/login`.
- Sign in via Tab A.
- Tab B's `storage` event handler fires; Tab B navigates to the dashboard
  WITHOUT the user having to click again.

## 8. US6 — VS Code sign-in (FR-017 — FR-020)

```bash
cd extensions/vscode
npm run compile
code --extensionDevelopmentPath=./
```

**Verify in the launched VS Code window**:

- Status bar shows `$(account) ATO: Sign In`.
- Open Chat → `@ato sign in`.
- A device-code notification appears with the verification URL + code.
- Click "Open Sign-In Page"; complete the device-code grant in the browser.
- Status bar updates to `$(verified) ATO: <displayName>`.
- `@ato switch tenant` lists the user's tenants and re-prompts device
  code for the chosen target.
- After `@ato sign out`, status bar reverts to `$(account) ATO: Sign In`.

## 9. US6b — M365 Teams bot (FR-021 — FR-022)

```bash
# Re-run the existing M365 dev setup
cd extensions/m365
npm install
npm run dev
# Use Teams App Test Tool to load the manifest
```

**Verify**:

- First `@ATO Copilot` mention triggers either Bot Framework SSO
  (`Auth:TeamsSso:Mode = Optional`) OR `OAuthPrompt` fallback.
- After successful auth, subsequent mentions DO NOT re-prompt.
- Switching to a different Teams tenant DOES re-prompt.

**Negative test for `Mode = Required` startup validation**:

```bash
# Set Mode = Required without webApplicationInfo.id in manifest
export Auth__TeamsSso__Mode=Required
# Remove webApplicationInfo from manifest.json
docker compose -f docker-compose.mcp.yml restart ato-copilot-mcp
```

- Startup MUST fail with `OptionsValidationException` and a message
  pointing at the missing `webApplicationInfo.id`.
- Revert.

## 10. US7 — Simulation panel (FR-023 — FR-025)

**Verify in Development environment**:

```bash
docker compose -f docker-compose.mcp.yml exec ato-copilot-mcp env | grep ASPNETCORE_ENVIRONMENT
# Expect: ASPNETCORE_ENVIRONMENT=Development
```

- Open `/login`. A "Developer Simulation" section appears below the
  Sign In button.
- It lists the configured `SimulatedIdentities`.
- Pick one. The SPA calls `POST /api/auth/simulate?identityId=<key>`.
- The dashboard renders as that identity.
- The cookie's `X-Simulated` attribute is true (DevTools → Application →
  Cookies).
- A `SimulatedLogin` audit row appears.

**Verify gating in non-Development**:

```bash
docker compose -f docker-compose.mcp.yml exec ato-copilot-mcp \
  sh -c 'export ASPNETCORE_ENVIRONMENT=Staging'
docker compose -f docker-compose.mcp.yml restart ato-copilot-mcp
```

- Open `/login` again. The simulation section MUST be absent.
- `curl -X POST http://localhost:5005/api/auth/simulate?identityId=dev-cspadmin`
  MUST return `404` with no body (route appears non-existent).
- A `SimulationBlocked` audit row appears with `severity = Security`.
- Revert to Development.

## 11. US8 — Impersonation banner (FR-026 — FR-029)

Pre-req: sign in as a CSP-Admin user (existing `seed-dashboard.sh`).

**Verify**:

- Use the existing Feature 048 impersonation endpoint to impersonate a
  customer tenant.
- A sticky banner shows "Impersonating {tenantName} — Exit" + countdown
  to auto-end.
- `ImpersonationStart` audit row written.
- Click "Exit". `ImpersonationEnd` row written with `reason = manual`.
- Repeat; let it auto-end. `ImpersonationEnd` row written with
  `reason = expired`.

## 12. US9 — Account menu (FR-030 — FR-031)

**Verify**:

- The header dropdown shows the user's display name, persona, home
  tenant, active PIM role (if any), and the sign-out item.
- Active PIM role disappears when the JIT window expires (test by
  setting an expired role in `JitRequestEntity`).

## 13. US10 — Per-class error pages (FR-014 — FR-016)

For each `ErrorClass`, induce the failure and verify the SPA renders the
correct canned copy:

| Class | How to induce |
|---|---|
| `NoCardInserted` | Click CAC button without inserting a card |
| `CertExpired` | Use a test CAC with an expired cert |
| `CertNotYetValid` | Use a test CAC with a future `notBefore` |
| `CertRevoked` | Use a test CAC in the configured revocation list |
| `ClockSkew` | `sudo date -u 0501120000` (skew 5+ minutes); revert after |
| `NoTenantAssignment` | Sign in with an Entra account that has no `Tenants` row |
| `AccountDisabled` | Disable the test account in Entra |
| `MfaFailure` | Cancel the MFA prompt |
| `ConditionalAccessBlock` | Use a non-compliant device |
| `NetworkFailure` | `docker network disconnect bridge ato-copilot-mcp` and retry; reconnect after |

**Verify** each error page:

- Shows the canned copy from `errorCopy.ts` for that class.
- Shows the `correlationId`.
- Shows the support email link from `Auth:Branding:SupportEmail`.
- A `LoginFailure` audit row appears with `ErrorClass = <enum>` and
  `MetadataJson` containing the privacy-preserving payload (no cert
  thumbprint, no PII beyond what FR-033 allows).

## 14. Throttle (FR-034 — FR-035)

```bash
# In Development, the throttle threshold is 100/min (per AuthThrottleOptions)
# In Production it's 10/min IP and 5/min identity
# For this test, simulate Production:
docker compose -f docker-compose.mcp.yml exec ato-copilot-mcp \
  sh -c 'export ASPNETCORE_ENVIRONMENT=Production'
docker compose -f docker-compose.mcp.yml restart ato-copilot-mcp

# Hit a bad-cert endpoint 11 times in 60 seconds
for i in {1..11}; do curl -s -o /dev/null -w "%{http_code}\n" \
  -X POST http://localhost:5005/api/auth/signin-cac \
  -H "X-Test-Bad-Cert: 1"; done
# Expect: ten 401s then one 429
```

**Verify**:

- The 11th response is `429` with `Retry-After: <seconds>` header.
- Response envelope: `error.errorCode = TOO_MANY_LOGINS`.
- One `LoginFailure` audit row per throttled attempt with
  `MetadataJson.throttled = true`.
- After the bucket resets (wait the indicated seconds), attempts
  succeed again.

## 15. Cold archive (FR-036a)

```bash
# Insert a row dated 14 months ago to trigger the archive job
docker exec -it stark-sql /opt/mssql-tools18/bin/sqlcmd -S localhost \
  -U sa -P "$SA_PASSWORD" -C -d AtoCopilot \
  -Q "INSERT INTO LoginAuditEvents (Id, EventType, EffectiveTenantId, CorrelationId, SourceIp, UserAgent, Surface, OccurredAt) VALUES (NEWID(), 'LoginSuccess', '00000000-0000-0000-0000-000000000000', 'qs-archive-test', '127.0.0.1', 'quickstart/1.0', 'Dashboard', DATEADD(DAY, -400, SYSUTCDATETIME()))"

# Trigger the archive service (override RunHourUtc to current hour)
docker compose -f docker-compose.mcp.yml restart ato-copilot-mcp
# Wait for the next scheduled run OR call the manual trigger if added

# Verify the row migrated to the FileSystem sink
ls -lh ./archive/LoginAuditEvents/
# Expect a .jsonl file containing the test row

# Verify the hot table no longer has the row
sqlcmd ... -Q "SELECT COUNT(*) FROM LoginAuditEvents WHERE CorrelationId = 'qs-archive-test';"
# Expect: 0
```

## 16. SOC analyst read (R9 / FR-039)

```bash
# Without Auth.SocAnalyst claim
curl http://localhost:5005/api/auth/events?systemTenant=true \
  -H "Authorization: Bearer $TOKEN_WITHOUT_CLAIM"
# Expect: 403 FORBIDDEN_NOT_SOC_ANALYST

# With Auth.SocAnalyst claim
curl http://localhost:5005/api/auth/events?systemTenant=true \
  -H "Authorization: Bearer $TOKEN_WITH_CLAIM"
# Expect: 200 with rows where EffectiveTenantId = SYSTEM_TENANT_ID
```

## 17. Tear-down

```bash
docker compose -f docker-compose.mcp.yml down
```

## 17a. WCAG 2.1 AA accessibility — manual CLI sweep (FR-039 / T144a)

The automated `vitest-axe` suite in
`src/Ato.Copilot.Dashboard/src/__tests__/auth/a11y.test.tsx` covers
component-level a11y. A live CLI pass against the rendered SPA catches
regressions axe-core cannot see in jsdom (focus ring contrast, real
fonts, scroll behaviour, etc.).

```bash
# Light pass — no install needed, runs on the live dev server.
cd src/Ato.Copilot.Dashboard
npm run dev &              # serves on http://localhost:5173

# Standalone HTML-accessibility CLI (one-shot npx invocation).
npx pa11y@latest \
  --standard WCAG2AA \
  --runner axe \
  http://localhost:5173/login

# Repeat for the other surfaces. /select-tenant + /login/error require a
# session; if you don't have one handy, log in via the simulation panel
# (Development env) and re-run.
npx pa11y@latest --standard WCAG2AA --runner axe http://localhost:5173/select-tenant
npx pa11y@latest --standard WCAG2AA --runner axe \
  "http://localhost:5173/login/error?errorClass=ClockSkew&correlationId=abc"
```

Tick the box in § 18 once each command exits 0 with no `Error: ...`
findings. (Warnings are acceptable; only `Error`-severity issues block
sign-off per FR-039.)

## 18. Sign-off checklist

- [ ] US1 — Branded `/login` page renders
- [ ] US2 — Deep links survive sign-in
- [ ] US3 — Tenant picker + remember cookie
- [ ] US4 — Sign-in writes `LoginSuccess` row
- [ ] US5 — Sign-out + idle timeout
- [ ] US5b — Idle timer respects silent renewal (FR-007a)
- [ ] US5c — Cross-tab login race resolves
- [ ] US6 (VS Code) — Device code flow works per-tenant
- [ ] US6 (M365) — SSO / OAuthPrompt flow works; identity persists per Teams tenant
- [ ] US6 (M365) — Required mode startup-fails without manifest
- [ ] US7 — Simulation panel gated 3 layers (FR-023 — FR-025)
- [ ] US8 — Impersonation banner + audit rows
- [ ] US9 — Account menu shows persona, tenant, PIM role
- [ ] US10 — All 10 error classes render correct copy
- [ ] Throttle returns 429 + Retry-After + audit rows (FR-034)
- [ ] Cold archive migrates rows > 13 months (FR-036a)
- [ ] SOC analyst read requires `Auth.SocAnalyst` claim (R9)
- [ ] WCAG 2.1 AA — `pa11y --standard WCAG2AA` clean on `/login`, `/select-tenant`, `/login/error` (§ 17a)
