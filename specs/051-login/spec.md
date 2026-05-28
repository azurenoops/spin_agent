# Feature Specification: First-Class Login Experience Across All Surfaces

**Feature Branch**: `051-login`
**Created**: 2026-05-28
**Status**: Draft
**Source**: [GitHub issue #68](https://github.com/azurenoops/ato-copilot/issues/68)
**Input**: User description: "ATO Copilot has authentication plumbing today
(CAC/PIV via MSAL — 003-cac-auth-pim, simulation mode for dev — 027-cac-simulation-mode,
tenant isolation + CSP-Admin impersonation — 048-tenant-isolation) but no
first-class **Login** experience for the Dashboard, VS Code extension,
M365/Teams app, or chat web app. Users land on protected pages and the browser
silently negotiates Entra. We need a real login page, a real account/tenant
picker, a real session UX, real error states, and a real sign-out flow —
across all four surfaces."

> **Note on numbering**: GitHub issue #68 titles this work "Feature 051: CAC/Entra Login"
> but suggests the branch `050-login`. Feature 050 is the CSP-Inherited Capability
> Lifecycle (vetting + reparent), already in flight. This spec therefore lands on
> branch `051-login` to preserve the chronological numbering convention.

## Pre-Spec Clarifications (resolved 2026-05-08 in the source issue)

The following decisions were locked in the source issue before this spec was
written. They are repeated here verbatim so a reader does not have to cross-
reference the issue:

| # | Question | Decision |
|---|---|---|
| C1 | Default sign-in method | **Per-deployment config** (`Auth:DefaultMethod = Cac \| Entra`); no per-user override. |
| C2 | Idle-timeout duration | **Per-deployment config** (`Auth:IdleTimeoutMinutes`, default 30). |
| C3 | "Remember tenant" scope | **Per-device only** (signed cookie); no server-side persistence. |
| C4 | VS Code device-code flow | **Use the Entra device-code endpoint directly** — no ATO-Copilot-hosted short-code service. |
| C5 | Throttling thresholds | **Differ between Development and Production** via separate `Auth:Throttle:Development` and `Auth:Throttle:Production` blocks. |
| C6 | Simulation panel exposure | **Hard-gated to `ASPNETCORE_ENVIRONMENT=Development`** — Staging never shows it, even if `SimulationMode=true`. |
| C7 | Multi-account Entra users | **Rely on MSAL's built-in account selector** — no custom picker on `/login`. |
| C8 | Session-lock UX | **Full sign-out on idle**. No soft-lock overlay, no silent renew across the idle threshold. |

> **Still open** (deferred to `/speckit.clarify`): Teams SSO baseline — Required, Optional (default), or Disabled per tenant?

## Clarifications

### Session 2026-05-28

- Q: For US6 / FR-021, what is the default `Auth:TeamsSso:Mode` and at what scope is it configured? → A: **Optional, deployment-wide** — SSO is used when the Teams manifest supports it; OAuthPrompt fallback runs otherwise. Single deployment-wide config (no per-tenant override) keeps parity with the other Auth-stack decisions (C2 idle timeout, C5 throttling, C7 account picker) which are all deployment-wide.
- Q: Who owns `LoginAuditEvent` rows for events fired before a session exists (`SimulationBlocked`, FR-024) or before a tenant mapping resolves (`LoginFailure` with `errorClass = NoTenantAssignment`, FR-015)? → A: **System tenant owns them all**. `EffectiveTenantId = SYSTEM_TENANT_ID` for any pre-session or unmapped audit row, regardless of whether Entra issued an `oid` / `tid` before the failure. Matches Feature 048's bootstrap-phase pattern and keeps SOC tooling's tenant filter simple (a single `TenantId = SYSTEM_TENANT_ID` query finds every security-relevant pre-auth event).
- Q: How long does `LoginAuditEvent` data live? → A: **13 months hot + immutable cold archive (indefinite).** Rows stay queryable in `LoginAuditEvents` for 13 months so SOC tooling and US10 / SC-004 "tune the idle threshold from real data" both work; rows older than 13 months migrate to an immutable append-blob archive (Azure Storage append-blob in production, local filesystem for dev) where SOC analysts can still pull them for forensic investigations but they no longer bloat the operational table. 13 months covers the NIST 800-53 AU-11 12-month interpretation with a 30-day overlap for annual audits.
- Q: How does the dashboard SPA refresh its access token between login and the idle threshold (e.g., access token = 1h, idle = 30 min)? → A: **MSAL.js owns silent refresh under the hood.** The SPA only handles the failure path — `401 from API → acquireTokenSilent() → if-fails interactive loginRedirect()` — there is no bespoke refresh-token storage, no server-side refresh endpoint, and no SPA-level refresh-token cookie. The idle timer (FR-007) tracks **user activity** and is orthogonal to token validity: token renewal can succeed silently many times during an active session, but the idle timer still fires sign-out at `Auth:IdleTimeoutMinutes` of no input. Matches the canonical MSAL.js pattern and the existing Feature 003 `@azure/msal-react` plumbing.
- Q: How does the "login race" edge case detect a session established in a sibling tab? → A: **Storage event on the MSAL cache key.** The waiting tab listens via `window.addEventListener('storage', ...)` for writes to the MSAL.js `localStorage` cache. MSAL.js writes the session cache on successful `loginRedirect`; the storage event fires immediately in every other same-origin tab, the waiting tab's React Router boundary observes the new session, and it completes its deep-link redirect without forcing a second login. No focus event, no polling — the storage event is sufficient and matches Constitution § II Simplicity.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — First-Time Login from the Dashboard (Priority: P1)

A user opens the Dashboard URL. Because there is no active session, they are
routed to a branded `/login` page that explains the deployment ("Coastal Watch
— ATO Copilot") and shows the **deployment's primary auth method as configured
by `Auth:DefaultMethod`** (CAC or Entra). Any other configured methods appear as
secondary buttons. After authenticating, they land on the page they originally
requested (deep-link preservation), not the dashboard root.

**Why this priority**: There is no real login screen today — the very first
impression of the product is a blank page or a browser auth dialog. P1 because
every other login story depends on this surface existing.

**Independent Test**: Set `Auth:DefaultMethod=Cac`, open an incognito browser
to `https://copilot.example/dashboard/systems/abc-123`. Verify: (a) redirect to
`/login`, (b) deployment branding loads from config, (c) the **CAC** button is
the primary action, (d) after sign-in the user lands on `/dashboard/systems/abc-123`
— not `/dashboard`. Repeat with `Auth:DefaultMethod=Entra` and verify the
**Entra** button is primary.

**Acceptance Scenarios**:

1. **Given** an unauthenticated user navigating to any protected route, **When** the React router resolves the route, **Then** the user is redirected to `/login?return=<encoded-original-url>`.
2. **Given** the `/login` page is open, **When** it renders, **Then** the primary button is the method named in `Auth:DefaultMethod`, and any other enabled methods (CAC, Entra, Simulation when allowed) appear as secondary actions.
3. **Given** the user clicks the primary auth button, **When** MSAL completes successfully, **Then** the SPA stores the session, calls `/api/auth/me`, and navigates to the original URL stored in `return`.
4. **Given** sign-in succeeds but the original URL is missing or invalid, **When** routing resolves, **Then** the user lands on the persona-default landing page from 015-persona-workflows.
5. **Given** the deployment has no branding configured, **When** the page renders, **Then** it falls back to "ATO Copilot" without a missing-asset broken layout.

---

### User Story 2 — Sign Out and Idle Sign-Out (Priority: P1)

An authenticated user clicks "Sign out" from the top-right account menu. The
session is invalidated server-side, the local SPA state is cleared (Redux /
React Query caches, localStorage conversation history from 034-dashboard-chat,
and the `ato-impersonate` cookie from 048-tenant-isolation if present),
and the user is redirected to `/login` with a confirmation toast. The same
full-sign-out path runs automatically when the user has been idle past
`Auth:IdleTimeoutMinutes` (default 30).

**Why this priority**: Without an explicit sign-out, users on shared
workstations leak access. Many DoD STIGs require **full sign-out** on idle
rather than a soft-lock — P1 because the security review will block release
without it.

**Independent Test**: Authenticate, perform a write action, click "Sign out";
verify (a) the API rejects the previously valid bearer token with 401, (b)
localStorage no longer contains conversation history, (c) the next request to
any protected page redirects to `/login`. Then authenticate again, leave the
tab idle for `Auth:IdleTimeoutMinutes + 1` minutes; verify the SPA performs
the same full-sign-out and lands on `/login?reason=idle_timeout`.

**Acceptance Scenarios**:

1. **Given** an authenticated session, **When** the user clicks "Sign out", **Then** the SPA calls `POST /api/auth/signout`, the server revokes the refresh token (or the MSAL cache for that user), and the user is redirected to `/login`.
2. **Given** the user signs out during an active CSP-Admin impersonation, **When** sign-out completes, **Then** the `ato-impersonate` cookie is deleted in addition to the auth token.
3. **Given** the user signs out, **When** the dashboard chat panel is open, **Then** in-memory conversation state is cleared, but localStorage history remains tied to the user's `oid` (re-loaded only after the same user signs back in).
4. **Given** a session has been idle past `Auth:IdleTimeoutMinutes` (deployment-wide, default 30), **When** the timer fires, **Then** the SPA performs a **full sign-out** — revoke server session, clear local state, redirect to `/login?reason=idle_timeout`. No overlay, no silent renew, no resume.
5. **Given** the user is mid-edit on a form when idle expires, **When** the timer fires, **Then** the SPA captures the in-flight form state to localStorage **before** sign-out, so on next sign-in the user is offered a "Restore unsaved changes" prompt. This is the only continuity concession given full-sign-out semantics.

---

### User Story 3 — Tenant / Organization Picker on Login (Priority: P1)

A user whose Entra account is a member of more than one tenant or organization
sees a tenant picker immediately after authenticating. They pick a tenant;
their session scope locks to that tenant for the duration. CSP-Admins (Feature
048) see an additional "All Tenants (CSP view)" option that lands them on the
global CSP dashboard. The picker may be skipped on subsequent sessions via a
**device-only signed cookie**.

**Why this priority**: Multi-tenant deployments (048-tenant-isolation) cannot
ship without a way for the user to declare which scope they're operating in.

**Independent Test**: Seed a user with home-tenant `T-Coastal` and home-tenant
`T-Eagle`. Sign in. Verify the picker shows both tenants. Pick `T-Coastal`
with "Remember on this device" unchecked. Sign out and back in; the picker
appears again. Repeat with the box checked; verify the picker is skipped on
the same browser only — clearing cookies brings the picker back, and signing
in from a different device shows the picker as if for the first time.

**Acceptance Scenarios**:

1. **Given** a user with exactly one tenant, **When** they finish authenticating, **Then** the picker is **skipped** and they land on their default page.
2. **Given** a user with two or more tenants, **When** they finish authenticating, **Then** the picker is shown with each tenant's display name and status (Active / Suspended / Disabled).
3. **Given** a tenant is `Suspended` (Feature 048), **When** the picker renders, **Then** that tenant shows a yellow badge "Read-only" and the user can still pick it (writes will return `423 LOCKED`).
4. **Given** a tenant is `Disabled`, **When** the picker renders, **Then** that tenant is hidden for non-CSP-Admin users and visibly grayed out for CSP-Admins.
5. **Given** a CSP-Admin, **When** the picker renders, **Then** an "All Tenants (CSP view)" option appears at the top and selecting it lands them on `/csp/dashboard` from 048-tenant-isolation.
6. **Given** the user checks "Remember my choice on this device", **When** sign-in succeeds, **Then** a first-party signed cookie scoped to the deployment domain stores the chosen tenant for `Auth:RememberTenantCookieDays` (default 30). No server-side flag is set on the user record. Clearing browser cookies forgets the choice.
7. **Given** a remembered tenant is later `Disabled`, **When** the user returns, **Then** the cookie is ignored and the picker is shown again.

---

### User Story 4 — Login Error States (Priority: P1)

When authentication fails, the user sees a clear, action-oriented error page
rather than a stack trace, a blank page, or a redirect loop. Each error class
has a dedicated message: smart card not inserted, certificate expired,
certificate not yet valid, certificate revoked, no tenant assignment, account
disabled, MFA failure, conditional-access block, network failure, and clock
skew. **Multi-account Entra selection is delegated to MSAL's built-in account
selector** — we do not render a custom picker on `/login`.

**Why this priority**: Silent or generic failures are the #1 reported support
call for any CAC-protected app. Without explicit error states, every failure
becomes a help-desk ticket.

**Independent Test**: For each error class, induce the failure (e.g., remove
the smart card mid-auth, present an expired test cert, block the MSAL endpoint
at the network layer) and verify the user sees the matching error UI with the
prescribed action.

**Acceptance Scenarios**:

1. **Given** the user is signed into Entra with multiple work accounts, **When** they click the primary sign-in button, **Then** **MSAL's built-in account selector** is presented (we do not render a custom picker). After they pick an account, normal auth proceeds.
2. **Given** no smart card is inserted, **When** the user clicks "Sign in with CAC", **Then** the page shows "Insert your CAC/PIV card and try again" with a "Retry" button — not a browser-modal certificate prompt loop.
3. **Given** an expired certificate, **When** auth fails, **Then** the page shows "Your certificate expired on {date}. Renew through your sponsor and try again," with no retry button.
4. **Given** the user has no `Tenants` row mapped to their `oid`, **When** auth completes, **Then** the page shows "Your account is authenticated but not provisioned in this deployment. Contact {support email}." — and **does not** silently auto-provision a tenant.
5. **Given** a conditional-access block, **When** auth fails with the Entra error class, **Then** the page surfaces the Entra-provided remediation URL ("Open Entra to resolve") rather than ATO Copilot's generic message.
6. **Given** a clock-skew failure (cert `notBefore` in the future, or token `nbf` not yet reached), **When** detected, **Then** the page shows "Your device clock is off by {N} minutes. Sync the clock and try again."
7. **Given** a network failure reaching the MSAL endpoint, **When** detected, **Then** the page shows "Could not reach the identity provider. Check your connection and try again." — with a Retry button that does NOT spin indefinitely.

---

### User Story 5 — Login from VS Code Extension (Priority: P2)

A developer using the `@ato` VS Code extension is prompted to sign in the first
time they invoke a CAC-gated tool. The extension calls **the Entra device-code
endpoint directly** (`https://microsoft.com/devicelogin` for `AzurePublic`,
`https://microsoft.us/devicelogin` for `AzureUSGovernment`), shows the
user-code in a VS Code modal, opens the browser to the appropriate
device-login URL, and on success caches the token in the VS Code SecretStorage
API. Subsequent invocations are silent until the token expires.

**Why this priority**: P2 because the VS Code extension already has working
auth via the MCP server in dev — but that path doesn't survive into a real
customer's workstation, where token storage and refresh are required.

**Independent Test**: Install the extension, run `@ato run compliance
assessment`, verify a VS Code modal shows the Entra-issued user-code and the
browser opens to `microsoft.com/devicelogin` (Public) or
`microsoft.us/devicelogin` (Gov) per the `Auth:Cloud` config delivered on
first connect. Complete the device-code flow and verify the assessment
completes. Restart VS Code; the same command runs without re-prompting.

**Acceptance Scenarios**:

1. **Given** a fresh VS Code install, **When** the user invokes any `@ato` command, **Then** the extension calls the Entra device-code endpoint **directly** (no ATO-Copilot-hosted short-code service), shows the user-code in a VS Code modal, and opens the browser to `https://microsoft.com/devicelogin` (Public) or `https://microsoft.us/devicelogin` (Gov) based on `Auth:Cloud`.
2. **Given** an active token in SecretStorage, **When** any `@ato` command runs, **Then** the extension silently refreshes the token (if needed) and never blocks the user with a prompt.
3. **Given** a refresh token is rejected (revoked, conditional-access change), **When** the next call fails 401, **Then** the extension shows a single VS Code notification "Sign in again to ATO Copilot" with a button — and never enters a silent retry loop.
4. **Given** the user runs `@ato sign out`, **When** the command completes, **Then** the SecretStorage entry is cleared and the VS Code status bar shows "ATO: signed out".

---

### User Story 6 — Login from M365 / Teams Bot (Priority: P2)

A Teams user `@mentions` the ATO Copilot bot for the first time. The bot
returns an Adaptive Card with a "Sign in" button that triggers a Bot Framework
SSO token exchange (or, for tenants without SSO, an OAuthPrompt). Once signed
in, the user's Teams identity is linked to their ATO Copilot identity and
subsequent messages are silently authenticated.

**Why this priority**: P2 because Teams SSO is a different code path from the
SPA flow and many customers will not enable it on day one. (Teams SSO baseline
— Required vs Optional vs Disabled — is **deferred to `/speckit.clarify`**.)

**Independent Test**: From a fresh Teams tenant, install the bot, mention it,
verify the sign-in card appears, complete the flow, verify the next message is
silently authenticated. Confirm the link survives `Teams → restart`.

**Acceptance Scenarios**:

1. **Given** the bot is installed in a Teams tenant, **When** a user `@mentions` it without an existing link, **Then** the bot replies with an Adaptive Card containing a sign-in button — never a free-text "please sign in" string.
2. **Given** Teams SSO is enabled in the bot manifest, **When** the user clicks the sign-in button, **Then** the token exchange completes silently using their existing Teams session.
3. **Given** the link is established, **When** the user messages from a different Teams client (mobile vs desktop), **Then** the same identity link applies — no re-prompt.
4. **Given** the user is signed in but the impersonated tenant has been disabled, **When** they message the bot, **Then** the bot replies with the standard "Tenant disabled" error from Feature 048 — not a Bot Framework stack trace.

---

### User Story 7 — Dev Simulation Login Without Restart (Priority: P2)

A developer running with `ASPNETCORE_ENVIRONMENT=Development` and
`CacAuth:SimulationMode = true` (027-cac-simulation-mode) sees a "Simulation
Mode — pick an identity" panel on `/login`, listing the identities defined in
`CacAuth:SimulatedIdentities`. This requires extending Feature 027 from
"single identity per startup" to a curated **list** of identities the
developer can switch between **on the login page**, not at runtime through the
API. The panel is **hard-gated to `Development` only**: setting
`SimulationMode=true` in any other environment is silently ignored at the UI
and API layers.

**Why this priority**: P2 because Feature 027 already supports the
single-identity-per-startup case; this story is a developer-experience
improvement, not a blocker.

**Independent Test**: Configure 3 simulated identities in
`appsettings.Development.json`. Open `/login` with
`ASPNETCORE_ENVIRONMENT=Development`. Verify the panel appears, lists all 3
identities, and clicking one signs in as that identity without restarting the
server. Restart with `ASPNETCORE_ENVIRONMENT=Staging` and the same
`SimulationMode=true` config — verify the panel does not appear and `POST
/api/auth/simulate` returns 404.

**Acceptance Scenarios**:

1. **Given** `ASPNETCORE_ENVIRONMENT=Development`, `SimulationMode=true`, and 3 configured identities, **When** `/login` renders, **Then** the panel lists all 3 with persona, roles, and tenant.
2. **Given** `ASPNETCORE_ENVIRONMENT` is anything other than `Development` (including `Staging`, `Production`, `AzureGov`), **When** `/login` renders, **Then** the simulation panel is **never** shown — even if `CacAuth:SimulationMode=true` is mistakenly left in config. The hard gate is environment, not the config flag.
3. **Given** `ASPNETCORE_ENVIRONMENT != Development`, **When** any client `POST`s to `/api/auth/simulate`, **Then** the endpoint returns `404 NotFound` (not 403 — pretend it doesn't exist) and writes a security audit row.
4. **Given** the developer picks an identity, **When** the SPA calls `POST /api/auth/simulate?identityId=...`, **Then** the server issues a session cookie marked `X-Simulated=true` and writes the audit row required by Feature 027.
5. **Given** the simulated session is active, **When** any compliance evidence is generated, **Then** the evidence is auto-flagged `IsSimulation=true` per Feature 027 and excluded from real RMF artifact bundles.

---

### User Story 8 — CSP-Admin Tenant Switching Mid-Session (Priority: P2)

A CSP-Admin already signed in to the CSP global view clicks "Switch into
tenant" from the tenant list and is taken into the chosen tenant's scope
without re-authenticating. The header shows a persistent "Impersonating:
T-Eagle" banner with a one-click "Exit impersonation" action.

**Why this priority**: P2 because Feature 048's `POST /api/tenants/{id}/impersonate`
already exists at the API level; this story adds the UX surface that makes it
usable without curl.

**Independent Test**: Sign in as CSP-Admin, click into a tenant, verify the
banner appears, verify all data scoping reflects the impersonated tenant,
click "Exit", verify scope returns to CSP global.

**Acceptance Scenarios**:

1. **Given** an active CSP-Admin session, **When** the user clicks "Switch into tenant" from the tenants list, **Then** the SPA calls the Feature 048 impersonation endpoint and updates client-side scope without a page reload.
2. **Given** an active impersonation, **When** any page renders, **Then** a persistent yellow banner shows the impersonated tenant's name and a real-time elapsed timer.
3. **Given** the impersonation session expires (configurable, default 1 hour), **When** the next request fires, **Then** the SPA auto-exits impersonation, refreshes the page, and shows a toast "Impersonation ended automatically".
4. **Given** the user clicks "Exit impersonation", **When** the request completes, **Then** the cookie is cleared and the user is returned to the page they were on **before** entering impersonation, not the CSP root.
5. **Given** an idle-timeout fires during impersonation (per US2), **When** the SPA performs the full sign-out, **Then** the impersonation cookie is cleared in the same atomic step.

---

### User Story 9 — Account Menu and Profile Surface (Priority: P3)

The top-right account menu shows the signed-in user's display name, persona
(015-persona-workflows), home tenant, active PIM roles (003-cac-auth-pim), and
links to "My profile", "Switch tenant", "Sign out". Hovering a PIM role shows
its expiration time. The menu is keyboard-navigable and screen-reader friendly.

**Why this priority**: P3 because the menu is polish, not critical-path. Users
can sign out via direct URL until the menu lands.

**Independent Test**: With an active PIM role, open the account menu, verify
display name, persona, tenant, and PIM expiration are all rendered and
accessible via keyboard tab order.

**Acceptance Scenarios**:

1. **Given** an authenticated user, **When** they click their avatar, **Then** the menu shows display name, persona, home tenant, and any active PIM role with expiration.
2. **Given** a CSP-Admin in impersonation, **When** they open the menu, **Then** the menu shows both their real identity **and** the impersonated tenant.
3. **Given** keyboard navigation, **When** the user tabs into the menu, **Then** all menu items are reachable via Tab / Shift-Tab and dismissible via Esc.
4. **Given** a screen reader, **When** the menu opens, **Then** ARIA attributes correctly announce role, expiration, and tenant.

---

### User Story 10 — Audit Trail of Every Login Event (Priority: P1)

Every authentication event (success, failure, sign-out, **idle sign-out**,
impersonation start, impersonation end, tenant switch, simulated login,
blocked simulation attempt) writes a structured audit row containing user
`oid`, `tid`, `correlationId`, source IP, user agent, surface (Dashboard / VS
Code / M365 / Chat), and event type. Failed logins are rate-limited per
identity + per IP using **environment-specific thresholds** drawn from
`Auth:Throttle:Development` or `Auth:Throttle:Production`.

**Why this priority**: P1 because the security review for any DoD deployment
will require it explicitly.

**Independent Test**: Generate one event of each type and verify each produces
a row in `LoginAuditEvents` with the expected schema. With
`ASPNETCORE_ENVIRONMENT=Production` and the default `Auth:Throttle:Production
= { PerIpPerMinute: 20, PerIdentityPerMinute: 10 }`, trigger 11 failed
sign-ins for the same identity within 60 seconds; verify the 11th is
throttled with `429 TOO_MANY_LOGINS` and a `Retry-After` header. Repeat with
`Development` and the higher default; verify the same identity can perform
100 attempts/minute before throttling.

**Acceptance Scenarios**:

1. **Given** a successful login, **When** the audit subscriber fires, **Then** a row exists with `eventType=LoginSuccess`, full identity, surface, and correlation ID.
2. **Given** a failed login, **When** the audit row is written, **Then** the row contains the **error class** (e.g., `CertExpired`) but **not** the cert thumbprint or any PII beyond `oid` (if Entra issued one before failure).
3. **Given** failed-login throttling is configured separately for dev and prod, **When** the threshold for the active environment is exceeded, **Then** the server returns `429` with a `Retry-After` header and the user-facing message "Too many sign-in attempts. Try again in {N} minutes." Default thresholds: **Development** = 100 / min / IP, 100 / min / identity (so test suites don't trip throttling); **Production** = 20 / min / IP, 10 / min / identity.
4. **Given** a CSP-Admin impersonation event, **When** the audit row is written, **Then** it includes both the real `oid` and `effective.tenantId` per Feature 048's audit contract.
5. **Given** an idle sign-out fires (per US2), **When** the audit row is written, **Then** `eventType=IdleSignOut` is recorded with the session start time and idle duration so admins can tune `Auth:IdleTimeoutMinutes` from real usage data.
6. **Given** a `POST /api/auth/simulate` arrives in a non-Development environment, **When** the request is rejected with 404, **Then** an audit row `eventType=SimulationBlocked` is written with `severity=Security` so SOC tooling can alert on it.

---

### Edge Cases

- **Login race** — the user opens two browser tabs to protected URLs before authenticating. Both redirect to `/login`. After they sign in in one tab, the other tab MUST detect the established session via a `window.addEventListener('storage', ...)` listener on the MSAL.js `localStorage` cache key. On that event, the waiting tab MUST complete its deep-link redirect without forcing a second login. Focus events and polling are NOT used.
- **Deep-link with a hash route** — `/login?return=/dashboard/systems/abc#components`. The fragment MUST survive the redirect.
- **Deep-link to a route the user is not authorized for** — sign-in succeeds, the user lands on the requested URL, then the route's authz guard renders the standard 403 page from 048-tenant-isolation. The login flow does **not** silently substitute a default route.
- **Idle expiration during a long-running upload** — the upload's request bearer is still valid until the server-side session expires; the SPA does **not** abort an in-flight write. The idle timer resets on the next user interaction post-completion.
- **Tenant picker after `/login` but before the picker mounts** — the user closes the tab. On return, they MUST be re-shown the picker (the session is not "locked to a tenant" until the picker action POSTs).
- **CSP-Admin who is a member of zero tenants** — the picker still shows the "All Tenants (CSP view)" option as the only choice. Selecting it lands on the CSP global dashboard.
- **VS Code device-code where the user closes the browser before completing** — the extension MUST surface a single "Sign-in cancelled. Run `@ato sign in` to try again." notification — not an indefinite spinner.
- **Teams bot signed-in user whose underlying Entra account is later disabled** — the next message returns the standard "Account disabled" error; the bot does **not** silently bounce or retry.
- **Simulation panel rendered briefly before the environment gate completes** — the panel MUST be guarded server-side at render time (env in the SSR / config endpoint), not client-side by a `useEffect`. A flash of the panel in non-Development is a security defect.
- **Audit-throttle counter cleared by app restart** — the throttle store MUST be backed by a persistent or distributed store (e.g., `IDistributedCache`) so a restart cannot be used to bypass throttling.
- **Branding asset missing in a configured deployment** — render the configured deployment **name** as text and the default ATO Copilot logo. Do **not** render a broken image.

## Requirements *(mandatory)*

### Functional Requirements

#### A. Login page & deep-link preservation (US1)

- **FR-001**: System MUST redirect unauthenticated requests to any protected route to `/login?return=<URL-encoded-original-path-and-query-and-hash>`.
- **FR-002**: The `/login` page MUST render the deployment branding (name, logo) sourced from configuration; missing branding MUST fall back to "ATO Copilot" plus the default logo with no broken-image artifacts.
- **FR-003**: The primary CTA on `/login` MUST be the method named in `Auth:DefaultMethod` (`Cac` or `Entra`). Any other enabled methods MUST appear as secondary buttons. Disabled methods MUST NOT appear at all.
- **FR-004**: After a successful sign-in, the SPA MUST navigate to the URL stored in the `return` query parameter. When `return` is missing, malformed, or external, the SPA MUST navigate to the persona-default landing page from 015-persona-workflows.

#### B. Sign-out & idle sign-out (US2)

- **FR-005**: `POST /api/auth/signout` MUST tear down the server-side session for the calling identity and return `204 NoContent`. For the stateless MSAL.js dashboard flow (FR-007a) there is no server-held refresh token to revoke; the endpoint's server-side work is therefore limited to (a) clearing any first-party auth cookies set by this feature, (b) clearing the `ato-impersonate` cookie if present, and (c) writing the `SignOut` / `IdleSignOut` audit row. Token-cache eviction on the client is the SPA's responsibility via `msalInstance.logoutRedirect`.
- **FR-006**: On sign-out, the SPA MUST clear Redux / React Query caches, in-memory chat state, any first-party auth or impersonation cookies, and the `ato-impersonate` cookie if present.
- **FR-007**: The SPA MUST track user activity (mouse / keyboard / touch / API success) and, after `Auth:IdleTimeoutMinutes` of inactivity, perform the same full sign-out path as the explicit sign-out — no soft-lock overlay, no silent renew. The redirect URL is `/login?reason=idle_timeout`.
- **FR-007a**: Access-token renewal MUST be performed by MSAL.js's silent-renewal path (`acquireTokenSilent`); the SPA MUST NOT implement bespoke refresh-token storage or call a server-side refresh endpoint. The dashboard's axios interceptor MUST handle a `401` response by invoking `acquireTokenSilent` and retrying the original request once; if `acquireTokenSilent` fails, the interceptor MUST trigger an interactive `loginRedirect` (preserving the originating URL per FR-001 / FR-004). The idle timer (FR-007) is orthogonal to token validity — a silently-renewed token MUST NOT reset the idle counter; only genuine user input resets it.
- **FR-008**: Before performing an idle sign-out, the SPA MUST persist the current form's in-flight state to localStorage under a key namespaced by `oid` so a "Restore unsaved changes" prompt can offer recovery on next sign-in.

#### C. Tenant picker (US3)

- **FR-009**: After authentication, if the user is a member of two or more non-`Disabled` tenants (or is a CSP-Admin), the SPA MUST render a tenant picker before navigating to any protected route.
- **FR-010**: The picker MUST list each tenant's display name and lifecycle status from Feature 048; tenants with status `Disabled` MUST be hidden from non-CSP-Admin users and rendered grayed-out for CSP-Admins.
- **FR-011**: A CSP-Admin MUST also see an "All Tenants (CSP view)" option that, when selected, lands on `/csp/dashboard` from Feature 048.
- **FR-012**: When the user opts into "Remember my choice on this device", the server MUST issue a first-party signed cookie scoped to the deployment domain, valid for `Auth:RememberTenantCookieDays` (default 30). No server-side preference flag MUST be written to the user record.
- **FR-013**: If a remembered tenant is later `Disabled`, the cookie MUST be ignored and the picker re-shown on the next sign-in.

#### D. Login error states (US4)

- **FR-014**: The system MUST classify CAC failures into at least: `NoCardInserted`, `CertExpired`, `CertNotYetValid`, `CertRevoked`, `ClockSkew`. Each MUST render its own dedicated error page with a class-specific recovery action.
- **FR-015**: The system MUST classify Entra failures into at least: `NoTenantAssignment`, `AccountDisabled`, `MfaFailure`, `ConditionalAccessBlock`, `NetworkFailure`. `ConditionalAccessBlock` MUST surface the Entra-provided remediation URL.
- **FR-016**: Multi-account Entra selection MUST be delegated to MSAL's built-in account selector. The `/login` page MUST NOT render a custom account picker.

#### E. VS Code extension login (US5)

- **FR-017**: The VS Code extension MUST invoke the Entra device-code endpoint directly. The cloud target MUST be derived from `Auth:Cloud` (`AzurePublic` → `https://microsoft.com/devicelogin`; `AzureUSGovernment` → `https://microsoft.us/devicelogin`).
- **FR-018**: The extension MUST cache tokens in VS Code's `SecretStorage` API. Refresh MUST be silent for active tokens; a single-shot notification with a "Sign in again" button MUST appear on `401` only when the refresh path fails.
- **FR-019**: `@ato sign out` MUST clear the SecretStorage entry and update the VS Code status bar.

#### F. M365 / Teams bot login (US6)

- **FR-020**: First-mention by an unlinked Teams user MUST trigger an Adaptive Card with a sign-in button — never a free-text prompt.
- **FR-021**: `Auth:TeamsSso:Mode` MUST default to `Optional` and MUST be configured deployment-wide (no per-tenant override). When `Optional` and the Teams app manifest supports SSO, the token exchange MUST be silent; otherwise the OAuthPrompt fallback MUST run. When set to `Required`, deployments without a manifest configured for SSO MUST fail at startup with a clear log line. When set to `Disabled`, the OAuthPrompt path MUST always be used regardless of manifest capability.
- **FR-022**: Identity links MUST be Teams-tenant-wide, not per-client (mobile, desktop, web). One sign-in MUST satisfy all clients of the same Teams user.

#### G. Dev simulation login (US7)

- **FR-023**: The simulation panel MUST render only when `ASPNETCORE_ENVIRONMENT == Development` AND `CacAuth:SimulationMode == true`. Any other environment MUST suppress the panel in BOTH the SSR / config endpoint AND the SPA.
- **FR-024**: `POST /api/auth/simulate` MUST return `404 NotFound` (not 403) outside `Development` regardless of config. Each rejected request MUST write a `SimulationBlocked` audit row with `severity=Security`.
- **FR-025**: A successful simulation login MUST set a session cookie named `X-Simulated` with the value `true` (alongside the normal session cookie), AND tag all evidence generated by the session with `IsSimulation=true` per Feature 027. (Clarified 2026-05-28 from "`X-Simulated=true` session-cookie attribute" — "attribute" was incorrect HTTP terminology; the intent is a discrete cookie named `X-Simulated`.)

#### H. CSP-Admin tenant switching (US8)

- **FR-026**: The SPA MUST expose a "Switch into tenant" affordance from the CSP-Admin tenants list that calls Feature 048's impersonation endpoint without a full page reload.
- **FR-027**: While impersonating, a persistent yellow banner MUST show the impersonated tenant's name and a real-time elapsed timer.
- **FR-028**: Impersonation MUST auto-expire on the configured session-lifetime (default 1 hour). On auto-expiry the SPA MUST refresh the page and show a "Impersonation ended automatically" toast.
- **FR-029**: "Exit impersonation" MUST return the user to the URL they were on before entering impersonation, not the CSP root.

#### I. Account menu (US9)

- **FR-030**: The top-right account menu MUST show display name, persona (015-persona-workflows), home tenant, and any active PIM role with expiration.
- **FR-031**: The menu MUST be keyboard-navigable (Tab / Shift-Tab / Esc) and ARIA-compliant (role, expiration, tenant announced).

#### J. Login audit trail (US10)

- **FR-032**: Every authentication event MUST write one structured row to `LoginAuditEvents` (`eventType`, `oid`, `tid`, `effectiveTenantId`, `correlationId`, `sourceIp`, `userAgent`, `surface`, `occurredAt`, optional `errorClass`, optional `metadataJson`). `effectiveTenantId` MUST be non-null on every row: pre-session events (`SimulationBlocked`, FR-024) and unmapped events (`LoginFailure` with `errorClass = NoTenantAssignment`, FR-015) MUST use `SYSTEM_TENANT_ID`. The captured Entra `tid` remains available for forensic context but does NOT determine row ownership.
- **FR-033**: Failed-login audit rows MUST contain the error class but MUST NOT contain cert thumbprints or any PII beyond `oid`.
- **FR-034**: Failed logins MUST be throttled per identity and per source IP. Thresholds MUST be drawn from `Auth:Throttle:Development` when `ASPNETCORE_ENVIRONMENT=Development` and from `Auth:Throttle:Production` for **every other environment value** (Staging, Production, Test, ...). Defaults: Development 100 / min / IP and 100 / min / identity; Production 20 / min / IP and 10 / min / identity.
- **FR-035**: When throttled, the server MUST return `429 TOO_MANY_LOGINS` with a `Retry-After` header and the user-facing message "Too many sign-in attempts. Try again in {N} minutes."
- **FR-036**: The throttle counter MUST be backed by a persistent or distributed store so a process restart cannot be used to bypass throttling.
- **FR-036a**: `LoginAuditEvent` rows MUST be retained for **13 months in the hot operational table** (`LoginAuditEvents`) so SOC tooling and SC-004 "tune the idle threshold from real data" both work over a full annual window with a 30-day overlap. Rows older than 13 months MUST be migrated to an **immutable append-only cold archive** (Azure Storage append-blob in production, local filesystem under `archive/LoginAuditEvents/{yyyy}/{MM}/` for dev). Migration MUST run as a daily background job at low-traffic hours; once archived, rows MUST be deletable from the hot table without losing forensic capability. Archive rows MUST remain queryable by SOC tooling but MUST NOT participate in the hot-table tenant query filter.

#### K. Cross-cutting

- **FR-037**: Configuration sections `Auth:DefaultMethod`, `Auth:IdleTimeoutMinutes`, `Auth:RememberTenantCookieDays`, `Auth:Cloud`, `Auth:VSCode:DeviceCodeProvider`, `Auth:Simulation`, `Auth:Throttle:{Development,Production}`, and `Auth:TeamsSso` MUST be validated at startup; invalid values MUST fail startup with a clear log line.
- **FR-038**: All login endpoints MUST emit Serilog structured logs with `correlationId`, `surface`, `eventType`, redacting sensitive headers and never logging the bearer token, refresh token, or cert thumbprint.
- **FR-039**: The login page, account menu, picker, and impersonation banner MUST meet WCAG 2.1 AA contrast and keyboard-navigation requirements.

### Key Entities

- **`LoginAuditEvent`** — append-only audit row (Feature 010-style telemetry table). Fields: `Id` (Guid), `EventType` (enum: `LoginSuccess`, `LoginFailure`, `SignOut`, `IdleSignOut`, `ImpersonationStart`, `ImpersonationEnd`, `TenantSwitch`, `SimulatedLogin`, `SimulationBlocked`), `Oid` (string?), `Tid` (string?), `EffectiveTenantId` (Guid; **non-null**), `CorrelationId` (string), `SourceIp` (string), `UserAgent` (string), `Surface` (enum: `Dashboard`, `VSCode`, `M365`, `Chat`), `OccurredAt` (DateTimeOffset), `ErrorClass` (string?), `MetadataJson` (string?). Tenant-scoped by `EffectiveTenantId`. **Pre-session events** (`SimulationBlocked` and `LoginFailure` with `ErrorClass = NoTenantAssignment`) and any other row where the tenant cannot be resolved MUST set `EffectiveTenantId = SYSTEM_TENANT_ID` so the tenant query filter still applies uniformly. The captured Entra `Tid` (the user's home directory) remains in the `Tid` column for forensic context but does NOT participate in row ownership.
- **`LoginAttemptCounter`** — distributed-cache counter keyed by `(identity, IP, minute-bucket)` used by the throttle middleware. Not a database entity. Detailed contract: [contracts/internal-services.md § 2](./contracts/internal-services.md). (Cross-reference added 2026-05-28 per analysis C15 so the entity is not overlooked by implementers reading data-model.md.)
- **`RememberedTenantCookie`** — first-party signed cookie payload (`tenantId` Guid, `iat`, `exp`, HMAC). Owned by the device only; no server-side mirror.
- **`AuthConfiguration`** — strongly-typed `Auth` options binding for the configuration surface enumerated in the Configuration Surface block below. Validated at startup.

### Configuration Surface

```jsonc
// appsettings.json
{
  "Auth": {
    "DefaultMethod": "Cac",                 // Cac | Entra — per-deployment
    "IdleTimeoutMinutes": 30,               // per-deployment, full sign-out on expire
    "RememberTenantCookieDays": 30,         // device cookie only; no server persistence
    "Cloud": "AzureUSGovernment",           // AzurePublic | AzureUSGovernment

    "VSCode": {
      "DeviceCodeProvider": "EntraDirect"   // only supported value; reserved for forward compat
    },

    "Simulation": {
      // Simulation panel is gated by the existing Feature 027 `CacAuth:SimulationMode`
      // flag plus `ASPNETCORE_ENVIRONMENT == Development`. The legacy duplicate
      // `Auth:Simulation:Enabled` flag was removed 2026-05-28 (analysis C10);
      // `Auth:Simulation:Identities` REMAINS only as a forward-compat placeholder
      // and SHOULD NOT be populated in this feature — simulated identities are
      // configured via `CacAuth:SimulatedIdentities` per Feature 027.
      "Identities": []
    },

    "Throttle": {
      "Development": { "PerIpPerMinute": 100, "PerIdentityPerMinute": 100 },
      "Production":  { "PerIpPerMinute": 20,  "PerIdentityPerMinute": 10  }
    },

    "TeamsSso": {
      // Deployment-wide; no per-tenant override (clarification 2026-05-28 Q1).
      "Mode": "Optional"                    // Required | Optional (default) | Disabled
    }
  }
}
```

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 95% of first-time users complete the `/login → tenant picker → landing page` flow on the first attempt without contacting support.
- **SC-002**: Time from `/login` page load to authenticated landing page is under 5 seconds at the 95th percentile across CAC and Entra paths (excluding the user-driven cert-PIN entry time).
- **SC-003**: Support tickets categorized as "generic auth failure / blank page / loop" drop to zero within one release cycle of this feature being deployed; every failure surfaces a class-specific error page instead.
- **SC-004**: Idle-timeout audit rows (US10 / FR-032) cover 100% of expired sessions in production after one week of usage, providing real data for tuning `Auth:IdleTimeoutMinutes`.
- **SC-005**: In Production, a single identity attempting 11 failed sign-ins in 60 seconds receives a `429 TOO_MANY_LOGINS` response on the 11th attempt with a `Retry-After` header — measured by a synthetic security probe in CI.
- **SC-006**: The simulation panel is **never** visible in any environment other than `Development`; 100% of `POST /api/auth/simulate` requests outside `Development` return `404` AND write a `SimulationBlocked` audit row.
- **SC-007**: All login surfaces (Dashboard, VS Code, M365, Chat) emit one structured audit row per authentication event with `correlationId` traceable across surfaces.
- **SC-008**: WCAG 2.1 AA conformance checks (contrast, keyboard navigation, ARIA) pass for the `/login` page, account menu, tenant picker, and impersonation banner.
- **SC-009**: A CSP-Admin can switch into a tenant, perform an action, and exit back to the original page in under 10 seconds end-to-end (FR-026 → FR-029).
- **SC-010**: The VS Code extension's "Sign in again" prompt fires exactly once per `401` (no silent retry loops); the failure case is testable without a live MSAL endpoint.

## Assumptions

- The branding configuration source (deployment name, logo URL, support email) is already provided by Feature 048 or an equivalent deployment-config service. This spec consumes that config; it does not introduce a new branding API.
- The throttle store will reuse the same `IDistributedCache` already configured for the dashboard (in-memory in dev, Redis in production) per the project's existing caching conventions.
- The Web Chat surface (`Ato.Copilot.Chat`) already participates in the dashboard's Identity middleware and does NOT need a separately-rendered login page; reaching `/chat` while unauthenticated MUST redirect to the dashboard's `/login` exactly as any other protected route does.
- The "persona-default landing page" referenced in FR-004 is provided by Feature 015 (persona workflows); this spec consumes that resolver, it does not invent persona resolution.
- The "session" on the server side is the existing cookie-auth / JWT bearer model used by Feature 003 — this feature does not introduce a new session protocol.

## Dependencies

This feature builds on, and does NOT modify the contracts of:

- **Feature 003 (cac-auth-pim)** — CAC/PIV MSAL plumbing, PIM role activation, JWT issuance. We consume the existing token-issue path; new behavior is purely the UX wrapper around it.
- **Feature 015 (persona-workflows)** — persona resolution for the post-login landing page.
- **Feature 027 (cac-simulation-mode)** — single-identity simulation today. US7 extends this to a curated **list** the developer can switch between at the login page.
- **Feature 034 (dashboard-chat)** — localStorage conversation history keyed by `oid`. US2 clears in-memory state on sign-out but preserves the per-user history.
- **Feature 048 (tenant-isolation)** — tenant lifecycle states (`Active`, `Suspended`, `Disabled`), CSP-Admin impersonation endpoint, and audit-row tenant-attribution fields.

## Out of Scope

- **New IdPs** (SAML 2.0, custom OIDC providers other than Entra) — defer to a future feature.
- **Local-account / username+password sign-in** — explicitly not supported. The product is CAC + Entra only.
- **Self-service tenant signup** — Feature 048 covers tenant provisioning by CSP-Admin or onboarding wizard; this feature does not add a "Sign up" link.
- **Password reset / passkey enrollment** — those are Entra-managed flows; we link out, we don't host them.
- **Soft-lock / overlay-style session lock** — explicitly rejected by clarification C8; we do **full sign-out** on idle.
- **Custom Entra account picker on `/login`** — explicitly rejected by clarification C7; we use **MSAL's built-in account selector**.
- **Per-user "remember tenant" preferences** — we use a per-device cookie only (clarification C3).
- **ATO-Copilot-hosted short-code service for VS Code device login** — explicitly rejected by clarification C4; we use Entra's device-code endpoint directly.
- **Per-tenant idle timeouts** — out of scope; deployment-wide only (clarification C2).
- **Login throttling beyond per-identity / per-IP** — geo-blocking, device fingerprinting, and risk-based auth are owned by Entra's conditional-access policies, not this feature.
- **Variable / tenant-configurable audit retention** — the 13-month hot + indefinite cold archive policy (FR-036a) is deployment-wide. Per-tenant retention overrides are out of scope; tenants requiring different retention horizons MUST work directly with their deployment operator.
