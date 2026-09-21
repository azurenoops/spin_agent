# Implementation Plan: CSP and Organization Workspaces

**Branch**: `feature/1002-role-aware-workspaces`

**Date**: 2026-09-21

**Base**: `origin/main` at `c3d74d9b`

**Spec**: [spec.md](spec.md)

**Dependency gates**: [dependencies.md](dependencies.md)

**Status**: Implementation authorized on 2026-09-21; dependency and release gates remain open.

## Summary

Retain existing authentication, domain services and scope-resolving pages.
Introduce one authenticated workspace context for routing, identity display,
scoped permissions and request/cache coordination. Consume prerequisite login,
membership, profile authorization, responsibility and narrative-library work
instead of recreating it in a frontend-only branch.

The user requires independent context per tab and deep link. Ordinary selection
must therefore be carried by each navigation/request and validated by the server;
neither a global active-tenant cookie nor a remembered browser persona can be
authoritative. Support impersonation remains an explicit, separately audited
mode.

## Technical context

**Language/Version**: C# / .NET 9 backend; TypeScript 5.7 / React 19 dashboard (existing stack; proposed feature)
**Primary Dependencies**: Existing ASP.NET Core authorization and EF Core; React Router 7, Axios, MSAL, Vitest and Playwright
**Storage**: Existing tenant, Person and role persistence; any membership/schema migration is owned by #942 and requires its approved contract
**Testing**: xUnit integration/unit tests; Vitest/Testing Library; Playwright; local synthetic identities and data only
**Target Platform**: Existing MCP API and dashboard; no infrastructure/deployment change in the planning phase
**Project Type**: Existing .NET backend and React web application
**Performance Goals**: Preserve repository API latency requirements; one shared workspace-resolution operation per context transition, no repeated CSP capability-probe waterfall
**Constraints**: Independent tabs, server-side isolation, existing login, no implicit privilege escalation, no silent narrative approval
**Scale/Scope**: One hosting CSP, multiple organization isolation tenants and authorized systems, complete RMF persona matrix

The SDK pin was read from [global.json](../../global.json), and frontend versions
from [package.json](../../src/Ato.Copilot.Dashboard/package.json). The technologies
above are not new dependencies or a claim of implemented functionality.

At final validation, PR #1005 had merged and `origin/main` advanced to
`05924787`, one commit ahead of this branch. Source inspection below remains
anchored to `c3d74d9b`. Synchronize/revalidate the merged profile-permission
contract before implementation; no runtime merge was performed during planning.

## Verified frontend foundation

Source inspection on the base revision established:

| Surface | Current behavior | Planned change |
|---|---|---|
| [PortfolioRoute](../../src/Ato.Copilot.Dashboard/src/pages/PortfolioRoute.tsx), [SystemsRoute](../../src/Ato.Copilot.Dashboard/src/pages/SystemsRoute.tsx), [ComponentsRoute](../../src/Ato.Copilot.Dashboard/src/pages/ComponentsRoute.tsx), [CapabilitiesRoute](../../src/Ato.Copilot.Dashboard/src/pages/CapabilitiesRoute.tsx), [ControlsRoute](../../src/Ato.Copilot.Dashboard/src/pages/ControlsRoute.tsx) | CSP endpoint availability plus local impersonation state select provider versus organization pages | Reuse pages, resolve all five from the same server-validated workspace descriptor |
| [useCspDashboardAvailable](../../src/Ato.Copilot.Dashboard/src/components/layout/useCspDashboardAvailable.ts) | Caches a successful/failed endpoint probe in sessionStorage for the tab; failures select non-CSP | Remove this probe as the authority for workspace/role selection; distinguish errors from legitimate organization context |
| [PageLayout](../../src/Ato.Copilot.Dashboard/src/components/layout/PageLayout.tsx) | Shared static navigation; header TenantPicker starts support impersonation | Contextual navigation plus ordinary authorized workspace selector; separately named support entry |
| [TenantPickerPage](../../src/Ato.Copilot.Dashboard/src/features/auth/TenantPickerPage.tsx) | Existing login selection UI posts select-tenant; CSP choice navigates to the root | Reuse existing login continuation and selection contract after #942 alignment; navigate to explicit authorized scope |
| [useMe](../../src/Ato.Copilot.Dashboard/src/features/auth/useMe.ts) | Each consumer owns a fetch; tenant event triggers refetch | Share authenticated context, clear stale results, and resolve scoped permissions coherently |
| [useOrganizationContext](../../src/Ato.Copilot.Dashboard/src/hooks/useOrganizationContext.tsx) | Loads once; display name prefers free-text subgroup | Active label uses server tenant identity; subgroup stays separate and scope-keyed; consume #950 backend correction |
| [SystemRoute](../../src/Ato.Copilot.Dashboard/src/components/SystemRoute.tsx) | URL-derived system fetch supplies chat context; failure is noncritical | Include workspace/system request identity and reject stale results; clear chat/system context during transitions |
| [SystemLayout](../../src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx) | Sidebar priority and mission shortcut use browser settings role | Use effective assignment/permission data; preserve read-authorized navigation and existing review restrictions |
| [useSettings](../../src/Ato.Copilot.Dashboard/src/hooks/useSettings.ts) | Persists a browser role alongside display preferences | Do not treat persisted persona as effective identity or permission |
| [RequireAuth](../../src/Ato.Copilot.Dashboard/src/features/auth/RequireAuth.tsx), [LoginCallbackPage](../../src/Ato.Copilot.Dashboard/src/features/auth/LoginCallbackPage.tsx), [main](../../src/Ato.Copilot.Dashboard/src/main.tsx) | Existing MSAL/cookie-aware server probe and login-config bootstrap | Preserve authentication; integrate context resolution after authenticated entry, not as a second login |

This is code inspection, not a reproduction of deployed behavior. No live
production writes or new runtime tests were performed in this planning phase.

## Verified backend and provider foundations

The delegated read-only traces established these integration points. None of
the referenced tests was executed during planning.

| Surface | Inspected behavior | Planning consequence |
|---|---|---|
| [AuthEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Auth/AuthEndpoints.cs), `GET /api/auth/me` | Returns identity/persona, home/effective tenant, impersonation, PIM roles, CSP/SOC flags and tenant memberships; computes effective tenant using impersonation, remembered tenant and home tenant | Add per-request workspace semantics without treating a remembered hint as authoritative request scope |
| [AuthEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Auth/AuthEndpoints.cs), `POST /api/auth/select-tenant` | Validates selection, audits `TenantSwitch`, optionally issues a remembered-tenant cookie, then returns 204 | Successful selection does not by itself establish a per-tab context for subsequent domain requests |
| [TenantResolutionMiddleware](../../src/Ato.Copilot.Mcp/Middleware/TenantResolutionMiddleware.cs) | Resolves home tenant and applies authorized CSP impersonation; no ordinary per-tab workspace selector was found in the traced path | Validate the new selector after authentication and before effective context/query filters; align `/me` with this same resolution |
| [TenantsEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/TenantsEndpoints.cs) and [TenantImpersonationService](../../src/Ato.Copilot.Mcp/Services/Tenancy/TenantImpersonationService.cs) | Existing CSP-only support start/exit uses a signed cookie and audit events | Reuse support authorization/audit, but explicitly bridge browser-global cookie behavior to request-scoped support mode |
| [MeEndpointTests](../../tests/Ato.Copilot.Tests.Integration/Auth/MeEndpointTests.cs), [SelectTenantEndpointTests](../../tests/Ato.Copilot.Tests.Integration/Auth/SelectTenantEndpointTests.cs), [ImpersonationFlowTests](../../tests/Ato.Copilot.Tests.Integration/Tenancy/ImpersonationFlowTests.cs) | Existing contract/isolation test locations were inspected | Extend these regression surfaces; their existence is not a claim that the new per-tab behavior passes |
| [CapabilityService](../../src/Ato.Copilot.Core/Services/CapabilityService.cs) and [DashboardComponentsEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Dashboard/DashboardComponentsEndpoints.cs) | Organization-owned capability regeneration is invoked through dashboard routes and uses narrative versioning/customization handling | Do not describe this as a verified cross-organization CSP-publication cascade |
| [CspInheritedComponentEndpoints](../../src/Ato.Copilot.Mcp/Endpoints/Csp/CspInheritedComponentEndpoints.cs) | CSP-owned inherited component/capability publish/update routes are separate from the organization-owned capability path | The trace did not establish a provider-publication-to-customer-narrative handoff; integration needs explicit evidence and tests |
| [NarrativeGovernanceService](../../src/Ato.Copilot.Agents/Compliance/Services/NarrativeGovernanceService.cs), [SspModels](../../src/Ato.Copilot.Core/Models/Compliance/SspModels.cs) and [CapabilityServiceBoundaryTests](../../tests/Ato.Copilot.Tests.Unit/Services/CapabilityServiceBoundaryTests.cs) | Existing governance tracks approved snapshots via `ApprovedVersionId`; a boundary test describes regeneration to a draft without replacing the approved snapshot | Reuse version/review semantics; creating a new draft is not equivalent to overwriting the approved version |
| [CspCapabilitiesPage](../../src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/CspCapabilitiesPage.tsx) and [ComponentDetailDrawer](../../src/Ato.Copilot.Dashboard/src/features/csp-inherited-components/ComponentDetailDrawer.tsx) | Provider UI exposes capability `NeedsReview`/remapping work | This queue alone is not evidence of the required customer narrative-change review or Narrative Library workflow |

In particular, the trace found remembered-tenant handling in `/me` without the
same ordinary-selection handling in tenant middleware. The two paths must use a
consistent authorized request context; merely changing the UI selector would be
incomplete.

No reproduction established that CSP publication overwrites approved customer
narratives. The plan treats approval preservation and actual provider-to-customer
change delivery as acceptance gates, not an asserted current overwrite defect.
Likewise, #1001's open issue describes the required library; the new planning
documents themselves are not evidence of existing library implementation.

## Architecture decisions

### Approved membership prerequisite (#942)

The user approved explicit membership records linked to existing
organization-local `Person` records, rather than deriving membership from role
assignments or introducing a global person registry.

- Bind membership to the authenticated directory tenant ID and object ID.
  Contact email, POC fields, client-selected organization and a bare object ID
  without its directory are not membership proof.
- Membership grant/revocation is distinct from RMF role assignment. Granting
  membership does not create administration, authoring, assessment or approval
  roles; revocation must deny ordinary access even if historical roles remain.
- Grant/revoke authority is an authenticated CSP administrator or an
  already-authorized administrator of the selected organization. Do not reuse
  the empty-organization bootstrap shortcut for this operation.
- Existing unmapped users require explicit, audited association; no automatic
  migration grants based on email or directory membership.
- Use additive SQLite/SQL Server schema support, tenant-local Person foreign-key
  validation and an unambiguous active identity/organization association.
  Keep grant/revoke attribution and last-administrator safeguards.

The request transport agreed for backend implementation is:

| Header | Meaning |
|---|---|
| `X-Workspace-Kind` | `csp` or `organization` |
| `X-Workspace-Tenant-Id` | Internal isolation tenant ID for organization requests, not an Entra directory |
| `X-Workspace-Mode` | `ordinary` or `support` |

Support navigation uses `/workspaces/support/organizations/{tenantId}/...`,
distinct from ordinary `/workspaces/organizations/{tenantId}/...`. The URL
expresses intent only; the server still requires a valid actor/target support
session. No support credential is placed in a URL. Ordinary routes never
inherit support mode from a cookie or browser mirror.

Ordinary organization requests require active membership and must ignore support
cookies. Support requests require existing CSP authority and a valid matching
actor/target support session. Invalid or inconsistent selectors are rejected.
Tenant filters and domain role resolution must use the validated selected
organization/Person, not raw `tid` or cross-tenant CSP bypass.

Before frontend integration, the backend work must provide the exact additive
`/me` workspace descriptor and membership-administration DTOs. An authenticated
member whose sign-in directory has no legacy home-tenant row must still reach
membership discovery and the ordinary workspace without fabricated home scope.
Legacy unscoped requests remain a documented compatibility path, not the source
of authority for explicit workspace requests.

Frontend HTTP integration captures the URL-selected workspace before asynchronous
token acquisition, pins that selector through retries, and discards responses
after a workspace change. All existing Axios clients use the shared auth
interceptor, so this is the common integration point rather than copied
per-client header logic. Workspace selectors are sent only to the API origin
configured for the client, not arbitrary external destinations. Unscoped legacy
pages send no workspace selectors. Browser canonical organization URLs always
mean ordinary mode unless an explicit support route is introduced and tested.

### 1. Keep identity, context and operation authorization separate

Authentication establishes the actor. The #942 membership contract establishes
which isolation tenants the actor may access. Workspace selection chooses among
those contexts. The domain operation then checks all applicable system roles,
permissions, lifecycle restrictions and concurrency/review locks.

Expose the complete effective role set plus action permissions. A role label is
display information, not a frontend policy engine. Reuse #968's profile
permission result instead of changing Mission Owner/ISSO authoring rules.

Extend existing auth contracts additively where appropriate, retaining the
existing response envelope. Exact membership DTO and lookup changes must be
agreed with #942 before coding; a provider-admin list of all customers cannot be
relabelled as ordinary memberships.

### 2. Use explicit per-request scope, not global selection

Proposed selectors are workspace kind and, for organization work, internal tenant
ID; system requests also carry/resolve the owning system. The client transports
them centrally through existing authenticated HTTP clients. The server validates
them before constructing the effective tenant context and before any scoped
database query.

The selector is untrusted input. Mismatched route/request tenant, unauthorized
membership, foreign system or ambiguous support mode must be rejected; there is
no fallback to home tenant that makes a failed request look successful.

Before fixing transport names, complete a contract review with #942 covering:

- Existing `/api/auth/me` and `/api/auth/select-tenant` compatibility.
- Explicit ordinary versus support mode and the existing support-session cookie.
- Guest/shared-directory membership keys and revocation.
- Bounded/paginated authorized-context listing.
- Claims/role resolution and tenant filters after context selection.
- Request IDs, expected error envelopes and audit attribution.

A successful ordinary selection response validates context but must not create a
browser-wide support session. A remembered landing preference can be shared,
but it cannot switch an already-open tab.

### 3. Centralize route construction and context lifetime

Introduce one typed route builder and authenticated workspace provider/gate.
Mount organization profile, system, chat and notification state below the
resolved context boundary. Keep public login/bootstrap outside that gate.

On navigation or selection:

1. Prompt for registered unsaved changes when relevant.
2. Resolve/validate target scope; disable protected old-scope content/actions.
3. Invalidate scope-specific caches, clear prior identity/profile/system data,
   abort requests where supported and advance a context-generation key.
4. Commit only responses whose scope/generation matches the active context.
5. Rejoin scoped real-time channels and refetch permitted data.
6. On denial, revocation or failure, show an explicit recovery state. Do not
   restore unrelated data as a success fallback.

The same transition mechanism handles switching, Back/Forward, deep links,
refresh, login/logout and support entry/exit. A full reload is not the
correctness mechanism.

### 4. Keep support mode explicit

Provider oversight does not confer ordinary customer membership. A provider
administrator with legitimate organization membership can choose that ordinary
context. Otherwise, customer support entry uses the existing explicitly
authorized/audited support flow.

Per-tab request mode must prevent a support cookie created in one tab from
silently changing another tab's ordinary workspace. Define target/actor,
expiration, revocation and exit semantics in the server contract before rollout.
Do not encode authentication credentials in a support URL.

The existing cookie-precedence behavior cannot simply remain a global override
for new ordinary-workspace requests: that would contradict the confirmed tab
isolation requirement. A request explicitly entering support mode must validate
the support session and target; an ordinary request must validate ordinary
membership. Reject ambiguous/mismatched mode rather than silently converting
ordinary access into support access. Preserve legacy support semantics only on
the explicitly documented compatibility path.

### 5. Integrate provider review, do not duplicate it

Use #957's persisted responsibility reconciliation for system subscriptions.
Keep baseline prerequisites, unknown allocations, overlap and manual overrides
visible. Never map every provider control to `Inherited`.

Use #1001's source-version/change proposal and Narrative Library contracts.
Provider publication can identify affected systems and create review work but
must not silently modify customer-approved policy/technical narratives or AO
decisions. Organization users see only authorized affected controls and work.

Draft generation may create a new version using existing governance semantics.
Approved-version reads, displayed approved content and exports must remain
pinned to the approved snapshot until authorized acceptance. Tests must verify
that observable contract, rather than incorrectly treating every draft/current
field update as approval or assuming preserved history alone is sufficient.

The exact source event, deduplication and version/concurrency contracts are
dependency integration gates, not assumptions hidden in UI code.

The user approved an explicit persisted #957 confirmation per system,
subscription/capability and control, including reviewed provider version and
provenance. Only effective assigned ISSM/ISSO roles may confirm. Unknown or
conflicting allocations remain review work, and reconciliation must not reuse a
baseline helper that changes narrative implementation status as a side effect.
Source changes and pending narrative impacts must be persisted atomically or
through a durable outbox; a fallible post-commit callback is not sufficient.
Impact marking performs no synchronous model call, preserves approved content
and supports idempotent retry of later proposal generation.

## Navigation and existing-route migration

The following paths are proposed; they are not registered by this planning
branch. `{tenantId}` means internal organization isolation tenant ID.

| Existing route | Planned canonical route / behavior |
|---|---|
| `/`, `/portfolio`, retired `/csp-dashboard` | Resolve authorized context, then replace-redirect to `/workspaces/csp` or `/workspaces/organizations/{tenantId}` |
| `/systems` | `/workspaces/csp/systems` for authorized oversight, or `/workspaces/organizations/{tenantId}/systems` |
| `/systems/new` | Organization-scoped intake only with create permission; do not match `new` as a system ID |
| `/systems/{systemId}/*` | Resolve authorized owning organization, then `/workspaces/organizations/{tenantId}/systems/{systemId}/*`; deny unauthorized ownership instead of guessing |
| `/components`, `/capabilities`, `/controls` | Same functional pages under the appropriate workspace prefix with scope-specific labels and permissions |
| `/capability-library`, `/capability-library/{capabilityId}` | Organization-scoped provider capability consumption; retain capability IDs and authorized system targeting |
| `/csp/inherited-components` | Provider components permalink with provider authorization; no org fallback |
| `/controls/overrides` | Authorized organization review route with existing separation-of-duties policy |
| `/settings/org`, `/settings/azure-subscriptions` | Authorized organization settings under the organization prefix |
| `/audit` | Explicit provider or organization audit scope, filtered/authorized server-side |
| `/admin/imported-documents`, `/admin/templates`, `/admin/migration`, `/admin/knowledge-base` | Gate by each existing operation's policy and deployment/organization ownership; never infer every `/admin` permission from Mission Owner |
| `/login`, `/login/callback`, `/login/error`, `/login/select-tenant` | Retain login URLs and existing authentication continuation; selector returns a validated workspace deep link |
| `/onboarding`, `/onboarding/tenant`, `/onboarding/csp` | Retain lifecycle-aware entry; use #941/#942/#944 policies, with no unknown-user bootstrap grant |
| Narrative Library routes | Consume #1001's approved paths and add explicit provider/organization/system entry points; exact paths remain gated on that contract |

All nested aliases already in [App.tsx](../../src/Ato.Copilot.Dashboard/src/App.tsx)
must remain valid: control-inheritance, categorization, capabilities,
mission-purpose, users-access, environment, data-types, ports-protocols,
leveraged-auth and legal-regulatory. Preserve search/hash and valid deep-link
state; avoid redirect loops and extra history entries.

### Routing foundation contract (implementation increment)

The first routing increment introduces pure typed helpers under
`src/features/workspaces/`:

- Parse a canonical workspace path into a CSP or organization selector and a
  workspace-relative route; return a distinct legacy result for unscoped paths.
- Reject malformed/unknown workspace prefixes, traversal segments, external or
  protocol-relative URLs and invalid organization identifiers explicitly.
- Build canonical workspace URLs while preserving query strings and fragments.
- Resolve existing system route aliases from one mapping, without treating
  `/systems/new` as a system or inferring a system's owning organization.
- Keep parsing/building separate from membership and system authorization.
  A syntactically valid route never grants access.

Tests must exercise every existing alias, both workspace kinds, malformed
inputs, round trips, and independent A/B locations. This increment does not
activate ordinary multi-organization access before the server contract is ready.

Find and migrate hard-coded links, page redirects, chat quick actions,
notification destinations, downloadable links and wizard completion URLs through
the route builder. Do not stop after changing the top-level route definitions.

The dashboard navigation adapter will reuse React Router rather than replacing
browser history or relying on reloads. Under a validated workspace provider it
prefixes application-absolute Link/NavLink/Navigate/useNavigate destinations and
exposes workspace-relative `useLocation` paths to existing pages. Numeric history
navigation, relative destinations, explicit workspace links and login routes
retain React Router semantics. Outside the provider, existing legacy navigation
is unchanged. The provider carries navigation context only, never permission.

## Implementation boundaries

| Layer | Work after approval |
|---|---|
| Core data / membership | Consume #942 resolver and authorized association/repair contract; no speculative parallel membership store |
| MCP middleware/auth | Validate explicit request scope before tenant queries; expose effective scoped permissions; preserve login/public route exemptions and domain authorization |
| Domain services | Integrate #957 responsibility state and #1001 review proposals; preserve customer approval locks and attribution |
| Dashboard auth/routing/layout | Shared workspace state, selector, scope identity, canonical routes, typed navigation and role-based affordances |
| Dashboard feature pages/API clients | Scope data/actions/links consistently; integrate existing server permissions; explicit error states |
| Chat / channels / extensions | Audit affected request context and deep-link producers; update only consumers of changed contracts and rerun their checks if touched |
| Tests | Isolated synthetic fixtures, failing-first unit/integration tests, persona/browser scope journeys and manual acceptance |
| Docs/specs/issues | Keep proposed-versus-shipped status, dependency contracts and task evidence synchronized |

## Dependency-ordered work packages

These are planned tasks, not completed implementation or a generated `tasks.md`.
After design/contract approval, generate the detailed task file and copy each
story's checklist to its approved GitHub sub-issue.

| Order | Work package | Depends on | Required evidence |
|---|---|---|---|
| W0 | Approve design, child stories, membership and permission contracts | User and dependency-owner review | Recorded decisions and actual parent/child links |
| W1 | Integrate/verify login and membership prerequisites | W0; #943/#942; #941/#944 for fresh setup | Ordinary organization login through real middleware, denied unknown identities |
| W2 | Add explicit server-validated workspace/request scope | W1 | Negative scope tests, role/tenant alignment, revocation and support isolation |
| W3 | Add workspace shell/selector and migrate routes/state | W2; #950 integration | All five resolvers agree; two tabs, history, refresh, deep links and stale-response tests |
| W4 | Wire complete scoped role/action affordances | W2; synchronize merged #968/PR #1005 | Role matrix UI tests plus real HTTP permission denial and persistence |
| W5 | Wire responsibilities and provider review work | W2/W3; #957/#1001 contracts | Approval preservation, applicable-control impact, overlap/idempotency/override tests |
| W6 | Integrate scoped Narrative Library | W3; #1001 implementation | Reference visibility, publication, upload/download/job scope, provenance tests |
| W7 | Cross-feature E2E, manual acceptance and release preparation | W3-W6 and required lifecycle work | Test/build reports, user test opportunity, exact PR/push preview |

W4 and dependency-owned W5/W6 work can progress in parallel only after their
contracts stabilize; avoid concurrent edits to the same auth/layout files.

## Verification strategy

### Failing-first unit tests

- Workspace resolver: CSP, ordinary organization, explicit support, zero/one/many
  memberships, multiple effective roles, revoked/disabled contexts.
- Route helpers: every legacy alias, query/hash preservation, unauthorized system
  ownership, new-system route ordering and no redirect loop.
- UI identity/navigation: all role-matrix rows, unknown/loading/error permissions,
  complete effective role set, no administration/approval from Mission Owner.
- Context races: A-to-B-to-A switching, stale successful/failed responses, aborts,
  unmounts, two tabs, signed-out state and stale cached CSP flags.
- Reference/review presentation: distinct responsibility sources, unresolved
  prerequisites and preserved approved versions.

Use AAA sections and synthetic fixture data. Save red/green evidence before
production edits. Coverage targets follow the constitution; see the gate below.

### Real HTTP-pipeline integration tests

Run with tenant-resolution bypass disabled and real local SQLite persistence;
mock external identity validation/Graph/Azure boundaries, not authorization or
tenant middleware. Add SQL Server provider checks through the existing supported
test environment before claiming production-provider compatibility.

Exercise ordinary organization login, shared-directory identities, authorized
guest associations, forged selectors, foreign system IDs, disabled/revoked
membership, provider-private data, support start/expiry/exit, and direct attempts
to perform admin/review/AO operations as Mission Owner.

Check no writes on denial; successful permitted writes survive a fresh read.
Provider updates must leave approved text and authorization decisions unchanged
until explicit version-checked acceptance. Assert isolation for notifications,
exports/downloads and asynchronous review work as well as synchronous JSON.

### Browser E2E

Use the real dashboard and isolated local API with seeded CSP admin,
organization administrator, Mission Owner, System Owner, ISSO, ISSM, SCA, AO,
reader and multi-context identities. Include:

1. Ordinary login -> correct landing -> authorized system -> permitted action.
2. Mission Owner sees mission authoring but no admin/approval authority.
3. Workspace A/B switching, reload, copied URL and Back/Forward.
4. Two simultaneous tabs retain separate contexts, including while a third
   tab uses an explicit support session.
5. Late/failed API response, permission revocation and unsaved-change cancellation.
6. Provider change -> affected customer review -> authorized acceptance.
7. Narrative Library scoping and legacy deep-link migration.

Desktop and narrow/mobile layouts must keep workspace identity and a reachable
navigation/selector. Test keyboard selection, focus, accessible names and error
announcements. Tests must not pass by skipping when fixtures are absent.

Mocked-API browser tests may cover deterministic races/errors, but report them
separately; they do not substitute for ordinary-login/persistence/isolation E2E.

### Commands and expected outcomes

These are planned commands, not commands executed by this planning phase.
Choose focused selectors after the actual test files exist, then run the
required aggregate gates before publishing.

```bash
dotnet build Ato.Copilot.sln
dotnet test Ato.Copilot.sln

cd src/Ato.Copilot.Dashboard
npm exec tsc -- --noEmit
npm run build
npm test
npm run test:coverage
PLAYWRIGHT_BASE_URL=http://localhost:5173 npm run test:e2e
```

Expected: successful build/static checking, passing targeted and aggregate
regression suites, measured coverage, and runnable browser fixtures. Do not
install dependencies unless a manifest changed or a validation command proves
they are missing. Test commands alone do not start the local API/dashboard;
the implementation must deliver a documented isolated fixture/startup workflow.

If extensions or Web Chat contracts change, run their existing type checks,
tests and builds too. Log skipped tests, pre-existing failures and warnings
explicitly; do not report them as a clean feature verification.

### Local manual acceptance

Before declaring the implementation complete, provide exact local startup and
fixture commands, synthetic credentials/identities, expected scopes and a reset
procedure. Let the user perform:

- CSP oversight versus ordinary organization login.
- Mission Owner allowed mission edit and denied administration/approval.
- Two-tab context isolation with refresh/history/deep links.
- Explicit support entry/exit without affecting ordinary tabs.
- Provider-change review with unchanged approved narrative before acceptance.
- Provider/org/system Narrative Library scope and provenance.

Full authenticated-workspace and manual acceptance remain pending. The first
frontend increment has the focused verification evidence below.

### Implementation increment 1: route and permission-view foundation

- Created and verified child issue links #1015-#1019 after exact preview approval.
- Committed the approved design and merged `origin/main` including #1005.
- Added typed canonical CSP/organization URL parsing/building and validation.
  Canonical workspace root routes are **not yet activated**; membership/request
  authorization must be implemented first.
- Replaced copied system-alias redirect logic with the production shared helper.
  Legacy links retain query/fragment, replace history, and handle trailing slash,
  mixed case and encoded aliases without treating `new` as a system.
- Repaired the duplicated Mission Profile form discovered when integrating
  #1005, preserving loading/error guards and server-authoritative read-only UI.
- Recorded failing tests before production changes.

Verified results:

| Check | Result / limitation |
|---|---|
| Focused route/profile unit tests | 118 passed |
| Complete dashboard unit suite using repository Node 20 runtime | 612 passed across 72 files; no skipped tests |
| New workspace helper/component coverage | 100% statements, branches, functions and lines |
| Existing SystemProfile whole-file coverage in focused run | 72.56% statements/lines, 89.09% branches; not claimed as whole-file 100% |
| Browser route migration + existing profile permission scenarios | 8 passed at desktop/mobile widths, using synthetic API responses |
| Dashboard TypeScript check | Passed |
| Production dashboard build | Passed with warnings; same warning categories appear in the retained pre-feature `test-results/981-build.log` |
| Real backend membership/isolation, provider review and library E2E | Not implemented or verified by this increment |

The default machine runtime was Node 26. Its first full-suite run failed 42
storage-dependent tests because `localStorage` was undefined. Node 20 is specified
by the repository Dev Container; running the same suite with that runtime passed
without changing tests or suppressing checks:

```bash
cd src/Ato.Copilot.Dashboard
npm exec --yes --package=node@20 -- node node_modules/vitest/vitest.mjs run
```

Diagnostics remain visible: existing suites emit React `act` and jsdom canvas
messages; build output reports stale Browserslist data, SignalR annotations, CSS
syntax, mixed imports and bundle size. Passing counts are not a zero-warning
release claim.

For a local visual replay of this increment, start the dashboard with the
existing `npm run dev` command, then run:

```bash
cd src/Ato.Copilot.Dashboard
PLAYWRIGHT_BASE_URL=http://localhost:5173 npm run test:e2e:headed -- \
  e2e/tests/workspace-route-migration.spec.ts \
  e2e/tests/mission-profile-permissions.spec.ts
```

These browser fixtures are synthetic and do not prove backend authorization.
Manual ordinary-login/two-tab acceptance will be supplied with the membership
implementation. The user approved adding #942's prerequisite to this branch;
its verified identity/grant contract is the next implementation gate.

### Implementation increment 2: common HTTP scope transport

The shared Axios authentication interceptor now captures canonical workspace
selection before token acquisition and pins it through request retries. Requests
to the configured API receive the agreed kind/tenant/ordinary-mode selectors;
legacy and external/non-API requests do not receive stale workspace headers.
Successful or failed responses from an obsolete workspace are cancelled rather
than returned as current data. Existing MSAL renewal behavior remains tested.

The membership model and administrator grant/revoke authority were explicitly
approved and handed to the backend implementation work. The client selector is
not authorization, and no canonical workspace entry route is activated yet.

Verified after this increment:

- 625 dashboard unit tests passed across 73 files on Node 20, with no skips.
- 100 focused routing/authentication/transport tests passed.
- All three new workspace modules have 100% statement, branch, function and
  line coverage in the focused run.
- All 8 synthetic-API desktop/mobile browser scenarios still pass.
- TypeScript and the production build pass; the previously documented warnings
  remain visible and are not suppressed.

Raw fetch/streaming and SignalR consumers still require scope/lifetime wiring
with the workspace shell. Backend membership enforcement, permission projection
and real-API workspace acceptance are not established by these client tests.

### Implementation increment 3: scope-preserving navigation

Added the React Router navigation adapter and migrated production router
navigation imports without reformatting their files. Under a workspace provider,
application links, active navigation, redirects and imperative navigation retain
the workspace prefix; existing pages receive relative location paths. Outside
the provider, legacy behavior is preserved. Global authentication redirects stay
on the native router.

Capability/component/evidence document links now preserve organization scope,
including native anchors. Tests exercise history replacement/back navigation,
external and login links, query-only destinations, both workspace kinds, and
fail-closed mismatched navigation context. The component-inventory fixture now
schedules mocked polling in an effect and awaits asynchronous dialog clicks
rather than invoking network callbacks during render.

Verification: 635 dashboard unit tests pass on Node 20; the navigation adapter
has 100% focused coverage; all 8 synthetic desktop/mobile browser scenarios
pass. TypeScript and production build pass with the documented build warnings.
The validated membership shell is still required before activating the provider
on canonical workspace entry routes. This adapter does not grant authorization.

### Backend membership checkpoint

The first backend increment adds explicit identity-bound memberships, audited
grant/revoke and Person-contact endpoints, request-scoped tenant/Person
resolution, paginated workspace discovery and additive `/me` descriptors.
`homeTenant`, `effectiveTenant` and `workspace` may be null; multiple authorized
contexts without selectors do not fabricate a selected organization. Membership
alone creates no role assignments.

The backend handoff reports 44 HTTP and 195 unit tests passing, with 82.9%
combined changed-executable-line coverage. The parent independently reran the
22 new membership HTTP cases and 3 schema tests successfully. Actual logs report
a solution build with 5 warnings and no errors. These are checkpoint results,
not full-feature acceptance or achievement of the stricter coverage target.
Live SQL Server and real-Entra manual validation remain unverified.

Canonical activation still has server-side gates: ordinary system visibility
must satisfy the user's newly confirmed applicable-role requirement; unmigrated
write operations remain fail-closed; and SignalR requires validated workspace
handshakes, tenant-scoped delivery and revocation handling. Chat conversation
isolation is not established by the membership REST tests.

Client chat storage must be partitioned by the authenticated directory/object
pair, workspace kind/organization/mode and selected system. Do not copy legacy
browser conversations into a newly authorized scope. Missing qualified identity
disables persistence explicitly rather than guessing a directory. Flush pending
writes to their original key before leaving; never write old state into the new
key. Abort streams on unmount/cancel and ignore obsolete callbacks. Raw streaming
requests must carry the same validated scope selectors as Axios requests.

### Client integration checkpoint

The authenticated shell increment mounts public login/callback/error routes
outside onboarding and organization providers. Canonical workspace routes share
one `/me` request and mount private providers only after `WorkspaceBoundary`
validates the requested context and any system access. Legacy responses without
workspace fields retain their existing route behavior; new-but-incomplete
responses fail closed. Multiple choices use a paginated workspace picker and
preserve the intended relative deep link without guessing a system's organization.
Voluntary workspace changes require explicit discard confirmation: the existing
idle form serializers are private to individual hook instances, not a global
dirty-form registry, so the shell conservatively warns on every switch.
Synthetic browser tests validate routing/state only, not backend isolation;
real-API and local manual acceptance remain release gates.

The membership API client/types, shared `MeProvider`, fail-closed
`WorkspaceBoundary`, explicit-context route resolvers and support URL namespace
are implemented. The boundary requires matching server context and a positive
system-access response before mounting system content. The application-shell
integration is a separate in-progress task; these components alone do not
activate all canonical entry routes.

Chat browser storage is now partitioned by directory/object identity,
workspace/mode and system. Missing qualified identity uses memory only with
visible persistence feedback; legacy histories are not copied. Storage-key
changes reject obsolete setters and preserve pending writes in the original
scope. Stream unmount/replacement discards obsolete callbacks. Streaming requests
carry workspace headers and reject results after scope changes. The existing
120-second handshake timeout also works with an external cancellation signal.

Verified at this checkpoint: 705 dashboard tests across 82 files passed on
Node 20, TypeScript and production build passed, and the 8 legacy-route/profile
desktop/mobile browser regressions passed using synthetic APIs. Full canonical
persona/two-tab browser acceptance and real-server chat/notification validation
remain pending. Dependency restore for merged #1014 reported 13 npm audit
findings (3 low, 4 moderate, 6 high); no automatic audit-fix or suppression was
applied. Previously documented build warnings remain.

Download entry points must use the authenticated HTTP client rather than native
API anchors, because browser navigation cannot carry per-tab workspace headers.
The shared download path must validate the configured API origin, preserve the
request scope through completion, support cancellation, release object URLs and
show failures explicitly. It must not send credentials or workspace metadata to
an arbitrary externally supplied URL.

Support-exit state is cleared only after a confirmed HTTP 204. Failed or
unexpected responses must preserve the local support mirror and must not emit a
tenant-change event that unmounts the retry error. A direct API regression was
reproduced against the original Git source without reverting concurrent edits;
banner-only mocks do not establish this shared-helper behavior.

The support-exit DELETE is a provider lifecycle operation: send ordinary CSP
selectors and omit the organization header, even from a support URL. Otherwise
expired support can be rejected before cleanup. Keep the initiating workspace
and account pinned for cancellation; this narrowly scoped transport exception
does not authorize other provider operations or clear state before HTTP 204.

The scoped Settings panel now displays server identity/roles instead of an
editable browser persona and gates Administration using the selected workspace's
permissions. AO decision/override controls use the system permission projection.
Profile review controls use the complete effective role set rather than a local
ISSM preference. System navigation/focus banners preserve all effective roles,
without inferring authority from a highest global role. Focused regressions pass,
and the current dashboard snapshot passes 783 tests across 93 files. Other
operation-specific affordances and server handoffs still need final integration
review; this checkpoint does not close #1017 or #1002.

Client SignalR connection URLs now carry the explicit workspace query selectors
for notification, SSP, package and import-progress transports; import progress
also uses the existing bearer factory. Connections remain below the keyed
workspace boundary. These client selectors do not replace server authorization:
the notification worker is extending matching checks to the remaining progress
hubs and REST notification operations, and durable support-session revocation
is still required. Focused selector/transport tests (18 cases) and TypeScript
checking passed.

Bearer acquisition now prefers MSAL's active account consistently for Axios,
renewal retries, raw streaming and notification/export consumers, falling back
to the first cached account only when none is active. This aligns transport with
the shell's account-qualified invalidation. Red tests reproduced both old
first-account selections; 24 focused account/transport tests pass. Current
whole-project type checking remains blocked by in-progress domain-affordance
test fixtures owned by the parallel UI task, not marked as a clean full gate.

Focused integration review found a retry identity race: workspace URL matching
alone does not prevent a pending mutation from retrying with a newly selected
MSAL account. Requests must pin the initiating account's qualified identity as
well as workspace, reject identity changes after token acquisition, and reject
obsolete success/error responses before any renewal/retry. Regression tests must
prove no second POST is sent after an account change.

### Planning artifact checks and tooling limitation

Local relative Markdown links and unfilled-template checks passed for the new
planning/architecture documents. The required Copilot context regeneration was
attempted with:

```bash
SPECIFY_FEATURE=078-role-aware-workspaces .specify/scripts/bash/update-agent-context.sh copilot
```

The helper returned exit code 0 but emitted `grep: invalid option` diagnostics
and truncated final `t` characters in extracted fields (`Playwright` and
`contract`). Inspection of the helper shows dash-prefixed patterns passed to
`grep` without `--`, and a whitespace-trimming `sed` expression using `\t`.
Its malformed generated changes were discarded; the existing Copilot context
file was preserved. No helper code was changed in this planning-only task.
Context regeneration needs a separately verified portability fix before it can
be reported clean; a success exit code alone is not proof of correct output.

### Integrated profile-permission regression

After integrating #1005, the first desktop/mobile route-migration browser tests
failed because Mission Profile rendered two identical forms. Inspection found a
second header/form outside the loading/error guard in the merged
`SystemProfile.tsx`; no workspace increment had modified that file. The guarded
copy also retained a browser-persona condition on the read-only badge.

This blocks consuming the scoped Mission Profile permission surface in US3.
Add explicit one-form/loading/error/read-only regression tests, remove the
unguarded duplicate and base the remaining badge on the server-authoritative
read-only state. Do not weaken test selectors to choose an arbitrary duplicate.
This is frontend integration repair, not a change to backend profile authority.

## Constitution check

References: [constitution](../../.specify/memory/constitution.md),
[contributing guide](../../docs/dev/contributing.md),
[simulation ADR](../../docs/architecture/adr-002-simulated-role-header-scope.md).

| Gate | Planning status | Evidence / implementation gate |
|---|---|---|
| I: documentation first | PASS for planning | Specification, dependency plan and proposed architecture precede code |
| II-IV: simplicity, YAGNI, responsibility separation | PASS as proposed | Reuse login/pages/services; centralize shared context and avoid a second permission engine |
| V: BaseAgent/BaseTool | Not applicable to planning | No new agent/tool; any later tool change must obey architecture and envelope rules |
| VI: TDD and acceptance coverage | NOT RUN; implementation gate | Red/green tests and independent acceptance per story required |
| VI: modified-path coverage | PENDING | Constitution states 100% in VI and 80% in later CI gates; plan targets stricter 100%, reports measured results and does not silently lower it |
| Security: authentication/authorization/tenant isolation | DESIGN GATE ONLY | Per-request server validation and real-pipeline tests required; no claim of verified isolation yet |
| VII: logging/audit | PENDING | Actor/scope attribution, explicit failures and no sensitive content logging |
| Local Type-Checking Parity | Not applicable to documentation phase | Required for every source project touched during implementation |
| DevOps: feature/user-story linkage | PASS | Approved child issues #1015-#1019 were created and linked to #1002 |
| DevOps: manual acceptance / release | BLOCKED | User must be offered local tests; no push/PR before exact preview and approval |

Overall: design, implementation authorization and story linkage are recorded.
Each work package must satisfy its dependency contracts before production edits;
test, manual acceptance and release gates remain open.

## Complexity tracking

| Decision | Concrete need | Simpler alternative rejected |
|---|---|---|
| Explicit scoped routes/request context and coordinated caches | User-confirmed independent tabs/deep links and #950 stale-scope behavior | A global selected-tenant cookie or page reload changes scope across tabs and cannot satisfy the requirement |
| Shared effective-context/permission projection | Multiple resolvers and local persona gates must agree with server policy | Per-page endpoint probes or copied role conditionals diverge and cannot establish authorization |

No new framework, query library, parallel membership store or generic policy
engine is proposed. Any schema/API expansion must be justified in its owning
dependency contract before implementation.

## Rollout and rollback

- Deliver compatible server scope/permission contracts before the dashboard
  begins issuing explicit-context requests.
- Preserve authorized legacy links and existing login responses during migration.
- Apply membership repair only through #942's authorized, audited workflow;
  never infer grants from POC email or import arbitrary directory users.
- Keep UI rollout reversible; server authorization/isolation and review locks
  must not be disabled by a feature flag or rollback.
- Roll back to a compatible prior dashboard/API pair using the existing immutable
  image process; do not revert membership audit history or approved narratives.
- This planning-only diff requires no runtime or database rollback.
