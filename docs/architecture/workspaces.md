# CSP and Organization Workspaces (Approved Design)

## Draft workspace-testing checkpoint

Local SQLite startup requires SQLite-compatible boundary-migration sentinel DDL.
The real hosted migration, including repeat startup, is covered by a SQLite
regression test rather than only simulating its data transformation in memory.
Development simulation may start before its configured organization exists.
Such pre-workspace login events belong to the existing system audit tenant;
simulation must not create organizations or memberships to satisfy an audit FK.
The selected simulation cookie must be resolved on subsequent requests, rather
than authenticating as the legacy default identity. For local verification,
select CSP Admin, then check that `/api/auth/me` identifies that administrator
and offers the provider workspace. An unknown selection must return 401.
Organization personas still require explicit memberships and system roles.
The identity-selection regression first passed 34 simulation tests. Live
verification then exposed a missing `CspProfiles` table: SQLite startup uses
migrations and explicit schema additions, whereas only SQL Server runs the
missing-model-table pass. The tenancy schema module now creates `CspProfiles`
idempotently without inserting a profile row. Real SQLite startup/read/restart
tests cover both deployment modes and preserve an explicitly created profile.
The combined focused run passed 47 tests. The local Dashboard proxy then
returned 204 for simulated sign-in and 200 for `/api/auth/me`, with the selected
CSP Admin identity and an ordinary Hosting CSP workspace. CSP onboarding remains
Pending; this API smoke check does not replace browser/manual acceptance.

This branch is being published for local workspace testing, not as a completed
or merge-ready implementation of #1002. The latest completed backend run built
with zero errors and 121 warnings; 6,357 unit tests passed, while integration
tests recorded 1,223 passes, 64 failures and 20 skips. The last full Dashboard
run passed 1,160 tests. Subsequent focused checks are reported separately.

Use an isolated development database and the existing [local setup](../../README.md#quick-start);
do not point this draft's schema upgrades at production data. Test ordinary
organization entry, workspace switching in two tabs, system-role restrictions,
support exit, and the provider/organization/system narrative library entry
points described below. Synthetic browser fixtures are not proof of live
identity-provider or persistence behavior.

Known open gates include the remaining integration failures, authorized write
routes blocked before their existing policies, build warnings, final coverage,
and live/manual acceptance. The agreed next bounded investigation is a role
assignment returning HTTP 500 where its contract expects 403; that fix is not
part of this initial testing checkpoint. (Approved Design)

**Status:** Design approved by the user on 2026-09-21 for issue
[#1002](https://github.com/azurenoops/spin_agent/issues/1002).
Implementation was subsequently authorized. Release verification and publishing
approval remain pending. This page does not describe a shipped feature.

## Purpose

The dashboard already resolves provider and organization variants of portfolio,
systems, components, capabilities and control pages. The proposed change makes
that distinction a consistent authenticated workspace, rather than deriving
navigation from a cached CSP endpoint probe or support impersonation.

The hosting CSP remains a single provider per deployment. Customer organizations
are isolation tenants; organizational subgroups and Entra directories are
different concepts and must not supply the active-organization label.

## Intended experiences

| Workspace | Responsibilities |
|---|---|
| CSP | Provider capabilities, shared-control definitions, provider evidence and narratives, customer organizations and authorized cross-organization oversight |
| Organization | Authorized systems, mission context, boundaries, subscribed capabilities, customer/shared responsibilities, narratives, evidence, assessments, remediation and monitoring |

Organization access does not confer administration or approval. Mission Owners
receive authorized mission/system responsibilities, not organization
administration, SCA review authority or AO approval authority. Multi-role users
retain their valid scoped permissions without turning a global persona into a
permission grant.

## Context is explicit and independent per tab

The user confirmed that separate tabs and copied deep links must retain
independent authorized contexts.

The proposed route families are:

- `/workspaces/csp/...`
- `/workspaces/organizations/{tenantId}/...`
- `/workspaces/organizations/{tenantId}/systems/{systemId}/...`
- `/workspaces/support/organizations/{tenantId}/...` (explicit audited support only)

These identifiers select context; they never authorize it. Each request must
validate the authenticated identity, membership, owning system and operation.
No authentication tokens belong in a route.

The workspace header shows the workspace, active organization, selected system
and effective roles. Loading, denied, missing and revoked contexts are explicit.
The previous organization's data must not remain visible under a new label.

Switching in one tab must not mutate ordinary scope in another tab. A remembered
organization may suggest a landing page only after revalidation. It cannot
override an authorized explicit deep link.

## Support is not ordinary workspace selection

CSP support impersonation remains a separate audited operation, visibly marked
with actor, target and expiration. An ordinary workspace switch neither starts
impersonation nor silently uses an impersonation cookie created in another tab.
Ending/expiring support must remove support-derived access.

The exact request/session migration is gated on the membership and scope
contracts; this document does not claim the current cookie-based behavior meets
the independent-tab requirement.

## Provider changes and customer approval

A provider mapping is not proof of inheritance or implementation. The customer
experience must show persisted Inherited, Shared, Customer or Undesignated
responsibility and its source. Missing baseline/allocation and pending review
are distinct states.

Provider changes should lead to affected-control review work. Approved customer
narratives remain unchanged until an authorized reviewer accepts a versioned
replacement. Authorization decisions are outside automatic propagation.

The Narrative Library from #1001 supplies provider/capability,
organization and system references. Those inputs remain references, not
implementation evidence. Library integration is a dependency, not a renamed
existing component-document list or a placeholder navigation link.

## Delivery dependencies

| Concern | Tracked work |
|---|---|
| Public login bootstrap and fresh CSP lifecycle | #943, #941, #944 |
| Authorized ordinary organization membership | #942 |
| Correct organization identity and stale-context handling | #950 |
| Server-authoritative Mission Profile permissions | #968, implemented by merged PR #1005; feature-branch synchronization pending |
| Responsibility-aware system inheritance | #957 |
| State-aware narrative review and scoped Narrative Library | #1001 |

Issue states and detailed merge gates belong to the feature planning artifacts.
An open dependency is not assumed to be implemented, and this feature cannot
be declared complete while required scope/permission/review integrations remain
missing.

## Validation and rollout

Planned verification includes unit tests, real local HTTP-pipeline integration
tests with tenant-resolution bypass disabled, browser persona journeys,
two-tab/history/deep-link cases and explicit negative authorization tests.
Synthetic UI mocks do not establish backend isolation or real persistence.

Legacy routes will resolve and redirect through authorized context. Previously
stored browser persona/settings values do not grant permissions. Rollout must
be coordinated between server and dashboard, with no production authorization
bypass and no automatic approval of customer artifacts.

Design and implementation approval are recorded. Dependency contracts, testing,
manual acceptance and publishing approval remain gates. Implementation progress
must not be confused with a shipped feature.

### First implementation increment

The branch now has typed workspace URL helpers and shared legacy system-alias
redirects that preserve query strings/fragments. It also repairs a duplicate
Mission Profile form encountered while integrating the server-permission fix.
Canonical workspace root routes and ordinary multi-organization selection now
consume the implemented workspace membership/request-scope contract.

Focused unit and synthetic-API browser tests verify this foundation, not the
complete authenticated workspace feature. The user authorized implementing the
#942 membership prerequisite on the same branch; provider responsibilities and
Narrative Library integration retain their separate dependency gates.

### Authenticated shell increment

Public login, callback and error routes do not mount organization, chat or
onboarding providers. Authenticated routes share `/me`. Canonical routes require
matching server workspace data and, for system routes, an authorized
`workspace-access` response before private providers or domain pages mount.
Incomplete new responses fail closed; genuinely old unscoped responses retain
legacy navigation. A legacy system bookmark with multiple authorized contexts
opens the explicit picker rather than guessing from a saved organization.

The shared identity lifetime includes the URL target and MSAL account identity
(issuer environment, directory, local object ID and home-account ID). Changing
the account invalidates the previous identity and private providers even when
the workspace URL is unchanged. Active-account events also invalidate that
lifetime; public routes still leave `/me` disabled. The authentication transport
owns which MSAL account supplies tokens, not the workspace shell.

The header labels provider, organization or audited-support mode, the active
server organization, selected system and all effective roles. Switching uses
React Router history and a paginated authorized-workspace picker, not selection
cookies or impersonation. A confirmation warns about unsaved changes on every
voluntary switch because form snapshot registries are local to individual forms.
Cancel keeps the current form mounted.

Organization membership administration is at `settings/memberships`; provider
administration is at `organizations/{organizationId}/memberships`, both beneath
their canonical workspace root. Server membership-management permissions gate
entry; provider routes additionally resolve the organization display name from
the tenancy API. Directory IDs are entered explicitly, not inferred from internal
organization identifiers.
Successful self-revocation in the ordinary organization workspace refreshes the
shared identity so its previous permissions and private providers are invalidated
immediately. The wrapper matches the organization, object ID and directory ID;
when the directory is absent, it also requires the current Person ID instead.
This fallback triggers a refresh only and never grants authority. Revoking another
person's membership or working in support mode does not remount the acting user's shell.

Provider organization/system rows open ordinary membership context. Their
separate **Audited support** actions require confirmation and use the existing
support endpoints. The server-driven support banner also renders for cookie-only
authentication. Ordinary selection does not end support in another tab.

Workspace support cookies are backed by a durable, actor-bound authorization
record. Exiting support revokes that presented session before deleting its cookie;
replaying the captured token must fail on another application instance as well
as on an existing hub connection. The server does not positively cache support
authorization. Existing stateless workspace support tokens are not automatically
enrolled by the schema upgrade: start a new audited support session after rollout.
Ordinary tabs and independently issued support sessions are not implicitly revoked.

Local synthetic checks (from `src/Ato.Copilot.Dashboard`):

```bash
npm exec tsc -- --noEmit
npm exec --yes --package=node@20 -- node node_modules/vitest/vitest.mjs run
# Start a dedicated server on a free port in a separate terminal:
npm exec --yes --package=node@20 -- node node_modules/vite/bin/vite.js \
  --host 127.0.0.1 --port 5186 --strictPort
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5186 \
  npm exec --yes --package=node@20 -- node node_modules/@playwright/test/cli.js \
  test e2e/tests/workspace-shell.spec.ts \
  e2e/tests/workspace-route-migration.spec.ts \
  e2e/tests/mission-profile-permissions.spec.ts --project=chromium
```

For manual acceptance against a local API, sign in normally as a Mission Owner,
open an assigned system's mission profile, confirm the organization and complete
role set, make an unsaved edit and cancel **Switch workspace**. Then confirm a
switch, refresh, use browser Back, and repeat in a second tab with another
authorized organization. As a CSP administrator, compare ordinary organization
entry with explicitly confirmed support and verify the banner/exit behavior.
Try a foreign organization/system URL and confirm recovery appears without
private page data. Synthetic browser journeys validate UI routing and request
ordering only; they do not establish backend isolation or production readiness.

### Notification transport and preferences

The notification center first requests
`GET /api/dashboard/notifications/capabilities` with the ordinary authenticated
API transport and current workspace selectors. REST access is based on the
server session; an MSAL account is not required for cookie/simulation sessions.
The response binds the recipient to the authenticated actor. The client never
substitutes a placeholder recipient or sends a different account's object ID.
Notification and progress clients share capability-response validation; a
real-time client connects only to its explicitly advertised hub path.

When real-time delivery is unavailable or disconnected and the server declares
`rest-polling`, the client polls at the supplied interval (currently 30 seconds).
It displays the reason real-time delivery is unavailable. A healthy registered
real-time connection suspends that fallback timer.
A SignalR connection is attempted only after the server confirms bearer
readiness. Token acquisition remains pinned to the original account, and each
reconnect rechecks capabilities. Workspace/account changes abort REST work,
discard late results, clear notification state and stop the previous connection.
Errors are visible with retry rather than presented as an empty successful list.
On narrow screens, the panel spans the header width with viewport gutters so
read and retry actions remain visible; desktop keeps the bell-anchored popover.

Preferences use the API's actual fields: POA&M overdue alerts, ATO expiration
alerts, compliance drift alerts and warning days. Email/Teams/Slack delivery
configuration is not supported by this preference endpoint and is not offered
by this panel. A failed preference read cannot silently create editable defaults.
Saving and loading are canceled when the organization context changes.

The preference database key is now unique on `(TenantId, UserId)`. Startup creates
that index before dropping the old global `UserId` index in a transaction.
Existing values are preserved, and migration errors fail startup. SQLite fresh
creation, upgrade, duplicate rejection and repeat startup have automated
coverage; execution of this upgrade against SQL Server still requires validation.

For local manual acceptance, use one authorized cookie/simulation identity with
membership in two organizations. Open the notification panel in each tab,
confirm each request has that tab's workspace selectors, and confirm a bearer-only
real-time notice appears without a failed SignalR connection. Wait 30 seconds
for an authorized list refresh. Mark a notification read, save different
preferences in each organization, reload, and verify the values remain separate.
Revoke membership and confirm the next refresh removes data and displays denial.

### Package, SSP and scan progress

Progress clients validate bearer readiness and the exact advertised hub before
connecting. Cookie-only sessions instead use the existing authenticated status
endpoints:

- Package: `GET /api/v1/systems/{systemId}/packages/{packageId}`
- SSP export: `GET /api/dashboard/systems/{systemId}/exports/{exportId}`
- Scan import: `GET /api/dashboard/systems/{systemId}/scans/import/{importId}/status`

Personal-notification `rest.available` and polling recommendations do not
authorize or schedule these separate resources. Default/scan polling uses five
seconds; the package shortcut retains its three-second cadence. Terminal states
stop polling. Status errors, including 401/403/404, stop automatic retries and
remain visible. Context changes cancel requests and prevent stale downloads or
completion callbacks.

SSP status polling cannot supply a percentage or detailed failure reason because
those fields are absent from the API response. It displays indeterminate status
and generic failure text rather than inventing details. Package real-time events
are hints to refetch authorized status; event-provided download URLs do not
override the authenticated download route.

For local acceptance, repeat package generation, ordinary SSP export and scan
import with cookie-only and bearer sessions. Switch to another authorized
organization while an operation is pending and verify no old-context completion
or download appears there. Revoke access or remove the import job and verify
polling stops with a visible error. Automated client tests use synthetic data;
live cookie/bearer acceptance remains outstanding.
