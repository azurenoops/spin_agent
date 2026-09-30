# Onboarding consolidation: implementation gate and shared contracts

**Date**: 2026-09-30
**Branch**: `078-onboarding-consolidation`
**Inspected base**: `052120a18647bf0078afceacd7b14d0fcea8aab0`
**Status**: Supported local implementation verified; user acceptance and release gates remain open

**Review checkpoint:** The exact contract packet was presented on September 30.
The approval tool initially reported the user unavailable. At 09:16 the user
explicitly directed autonomous implementation and decisions rather than further
planning. Local implementation now proceeds from these amendments; external
writes, pushes, live deployment and user acceptance remain separate gates.

The user requested implementation of the September 27 session plan on a new
branch. That plan explicitly requires closing its held dependency inventory and
presenting exact amendments before affected behavioral implementation. This
document and its domain contracts implement that first gate.

**Final local checkpoint:** shared start/resume, provider, organization/tenant,
system draft/source review and minimum document/monitoring handoffs are
implemented. The final affected integration suite passes 84 tests, the full
backend unit suite 7,254, the required SQL Server upgrade suite three and the
coordinated browser suite 29. See the
[validation guide](../../../docs/guides/onboarding-consolidation.md) for exact
full-suite limitations, scoped coverage, supported source formats and manual
review instructions. The proposed-contract/history sections below retain their
planning context; they are not claims that every wider audit dependency or live
integration is complete.

## Phase 0 review decisions

The source inventory is now recorded in the three domain contracts. It changes
several assumptions in the preliminary plan; these decisions require review
before application behavior changes:

1. **Saved drafts need explicit private persistence.** Pre-creation organization
   drafts cannot reuse a provisioning row that requires an existing tenant.
   Provider drafts cannot be put on the globally readable provider profile.
   Use provider-private draft records and tenant/system-owned partial fields;
   preserve canonical create/receipt/grant services.
2. **Receipt recovery is not currently browser-restart durable.** Add exact
   persisted intent/key/manifest reconciliation, not another receipt engine.
   The provider trace also identifies an associated-key construction of 152
   characters against a server limit of 100; reproduce it with real key tests
   before fixing. This is a source finding, not a passing runtime acceptance.
3. **Trusted upload handling policy is not established by current classification
   fields.** The provider contract proposes operator-owned policy, explicit
   Unknown/Expired states and fail-closed upload/analysis checks. Enabling the
   new guard intentionally rejects undeclared legacy uploads with an actionable
   error; this rollout change is not silently inferred from the mock.
4. **Mission import destinations need repair before handoff.** Existing
   registration import clients disagree with wire contracts; the services do
   not constitute a full SSP/eMASS package import. Bind receipts/proposals to the
   exact selected system, preserve approved content and explicitly apply only
   supported reviewed fields. Original files/unmapped content remain visible.
5. **The current todo service computes work; it is not a durable task store.**
   Store only finite setup choices/deferral intent on the system and project
   them through TodoService. Do not repurpose force-advance phase debt or add a
   second task engine. Purpose-specific readiness remains #1042's work.
6. **Ordinary workspace wrappers and completion/discard paths need regressions.**
   Preserve current server action permissions when making onboarding endpoints
   usable in the correct workspace. Save/finish/open-system must never invoke
   discard; tests must reproduce the traced paths before production fixes.
7. **Monitoring and document outputs remain separate acceptance gates.**
   No subscription-wide health/eligibility shortcut, no fabricated authorization
   fields, and no claim of live Azure/Entra/eMASS verification from fixtures.

The detailed contracts include exact additive routes/DTOs, ownership, revision/
idempotency semantics, SQLite/SQL Server upgrades, legacy adapters and test
mapping. Their behavior is proposed, not already implemented. The generator
prerequisite repair and its passing tests are the only executable changes in
this Phase 0 checkpoint.

## Required designs and boundary decisions

- [Onboarding](../../../docs/design/onboarding-mock/index.html) is the required
  UX, with its [22-state inventory](../../../docs/design/onboarding-mock/screens/index.json).
- [Provider destination](../../../docs/design/provider-workspace-mock/README.md)
  and [Systems destination](../../../docs/design/system-overview-mock/README.md)
  are coordinated references. Scope is their minimum useful review/task/document
  handoff, not the complete destination redesign.
- Reuse unassociated canonical provider receipts when context is unknown;
  no fabricated boundary, automatic association or publication.
- Explicit server-confirmed partial draft save is required for Save & finish
  later. In-page navigation retains unsaved edits but does not claim persistence.
- Setup completion is not membership, duty acceptance, approved source content,
  a release, successful collection, eMASS import acceptance or authorization.
- Retain separate deployment, provider, tenant, organization, system and actor
  identities. One presentation frame is not one security/state engine.

## Domain evidence and amendments

The following documents own exact current routes, models, commands, migration
and test cases; new contracts in them are proposals, not existing APIs:

1. [Provider and receipt consolidation](onboarding-provider-consolidation.md).
2. [Organization and tenant consolidation](onboarding-organization-consolidation.md).
3. [System, general onboarding and destination consolidation](onboarding-system-consolidation.md).

Do not copy those DTOs into this shared contract or introduce a universal draft
blob. Each domain remains responsible for validation, partial save, committed
operations, source history, authorization and version fences.

### Cross-domain review corrections

- Private provider/organization draft fields must use the existing provider-owned
  private filtering pattern and explicit server-resolved ProviderId predicates.
  They are not public `[GlobalReference]` data and must not be embedded in the
  globally readable provider profile.
- Command replay returns an immutable committed outcome without reexecuting
  writes, but current actor/access/action availability is freshly projected.
  Projection failure cannot turn a committed operation into "not committed" or
  display another administrator's historical authority as current.
- Organization live-administrator lists are paged; availability and conflict
  predicates apply to the full authorized dataset, not just the displayed page.
- Provider receipt-only intake does not remove publication context checks.
  New setup receipt requirements must be versioned without retroactively
  inventing context or invalidating historical legacy releases.

## Verified common execution path

Evidence is source-level unless a run is explicitly reported.

| Surface | Actual caller/behavior | Disposition |
|---|---|---|
| [ApplicationFrame](../../../src/Ato.Copilot.Dashboard/src/ApplicationFrame.tsx) | Unscoped app wraps CSP then tenant guards; provider workspace uses CSP guard; organization manager uses tenant guard; general OnboardingGate mounts only without a workspace session | Retain this distinction. Do not mount a universal setup gate around all active users |
| [ApplicationRoutes](../../../src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx) | Authenticated `/onboarding`, `/onboarding/tenant`, `/onboarding/csp`; `/systems/new` uses SystemsNewRoute; system destinations live under SystemLayout | Retain compatibility. New setup/resume presentation must use these domain entries and preserve the selected system layout |
| [workspaceNavigation](../../../src/Ato.Copilot.Dashboard/src/features/workspaces/workspaceNavigation.tsx) | Prefixes local application links/navigation with the active workspace, preserves query/hash/state, leaves global/login/external links intact, rejects mismatched context | Reuse for all setup and handoff links; no manually concatenated tenant URLs |
| [workspaceRoutes](../../../src/Ato.Copilot.Dashboard/src/features/workspaces/workspaceRoutes.ts) | Distinguishes provider, ordinary organization and support paths; validates URL segments and retains legacy system aliases | Retain support boundary and aliases; no return-to redirect that switches authority |
| [WorkspaceHeader](../../../src/Ato.Copilot.Dashboard/src/features/workspaces/WorkspaceHeader.tsx) | Shows actual workspace/system/effective roles and permission-gated membership/settings links; current switching dialog warns about unsaved work | Retain current context. Setup's saved-exit guard must integrate with switching instead of silently losing edits |
| [SetupDialog](../../../src/Ato.Copilot.Dashboard/src/features/workspace-operations/SetupDialog.tsx) | Portaled native modal; busy close guard, focus cycling/restoration, backdrop/Escape handling and drawer/center variants | Retain for recovery/confirmation dialogs; do not reimplement focus behavior in each wizard |

Confirmed current route destinations:

| Contribution | Existing route relative to workspace |
|---|---|
| Provider records and sources | `/authorizations/*` |
| Tenant activation | `/onboarding/tenant` |
| General bootstrap / rerun | `/onboarding` |
| Provider profile setup | `/onboarding/csp` |
| System entry / overview | `/systems/new`, `/systems/:id` |
| Hosting association | `/systems/:id/profile/EnvironmentAndDeployment/hosting` |
| Capability adoption and duties | `/systems/:id/security-capabilities/*`, `/systems/:id/inheritance/subscriptions` |
| Document preparation and eMASS status | `/systems/:id/documents`, `/systems/:id/emass/status` |
| Monitoring / assessment connection | `/systems/:id/conmon`, `/systems/:id/assessments/environment` |
| Team | `/systems/:id/roles` |
| Retained import/template/subscription management | `/admin/imported-documents`, `/admin/templates`, `/settings/azure-subscriptions` |
| Organization configuration and membership | `/settings/org`, `/settings/memberships`, `/organizations/:organizationId/memberships` |

Reachability does not establish permission or working downstream behavior.
Domain contracts specify those separately. Keep unavailable/denied destinations
truthful rather than rendering a successful zero or simulated completion.

## Shared presentation contract

Extract a presentational setup frame only when provider and system adapters
both consume it. Reuse SetupDialog and existing production theme/brand assets.

Inputs are typed presentation values and callbacks: title, description,
breadcrumb, current step ID, ordered visible steps with allowed navigation,
save state/time, busy state, error summary, primary/secondary actions and
content/support panels. The frame has no HTTP client, domain validator,
permission inference, percentage calculator or side-effecting step transition.

Required behavior:

- Guided header and Save & finish later, numbered steps, breadcrumb/eyebrow,
  title/help, white content panels, side guidance, statuses and footer match the
  mock hierarchy. Preserve responsive stacking at intermediate widths.
- Active step is exposed accessibly. Navigation uses stable IDs and browser
  history, not an array index persisted as authoritative progress.
- Async transitions disable duplicate commands, announce outcomes and focus
  the error or next heading appropriately.
- A save failure keeps entered fields and explicit unsaved status; it cannot
  dismiss/navigate with a saved message.
- Confirmation dialogs describe only actual writes; no simulated approval.
- The design toolbar/gallery controls and sample names/counts are not shipped.
- Server unavailability and forbidden access remain distinct from empty data.

### Screen ownership and interaction acceptance

| Mock IDs | Domain adapter / required behavior | Existing owner |
|---|---|---|
| `start`, `resume` | Authorized choices and named saved records; no role-selection privilege grant; resume exact operation | #1025 |
| `p-details`, `p-access`, `p-offering` | Provider identity, actual access, optional offering | #1026; activation dependencies remain #1036/#941/#944 |
| `p-sources`, `p-uncertain`, `p-review`, `p-ready` | Receipt-only or associated intake, original-request reconciliation, truthful outcome and same-receipt portal handoff | #1026; publication remains #1028 |
| `o-details`, `o-admin`, `directory-offline` | Authorized reuse/create, separate contact/admin choice and truthful discovery fallback | #1031/#942 |
| `o-review`, `o-ready`, `o-repair` | Actual saved creation/enrollment, current role facts and actor-specific handoff | #1031 |
| `s-details`, `s-team`, `s-sources` | Draft objective/contact, effective roles and optional mission proposals | #1046 with existing intake/import contracts |
| `s-hosting`, `s-connect`, `s-review`, `s-ready` | Exact hosting choice, independent monitoring facts, open work and document contribution | #1046/#1042; capability and monitoring semantics remain #1037/#1044/#1045 |

These are states, not 22 new routes. Each implemented state requires a real
data/permission contract, functional test and 1440px/390px visual comparison.
Substantive departures require explicit review; component reuse is not
permission to retain the obsolete UX.

## GitHub reconciliation

On September 30 the GitHub API returned actual child relationships:

- #1002 -> #1015-1019, #1025-1035, #1037; all returned open.
- #1038 -> #1039-1046; all returned open.
- #1036, #942, #939, #938, #941 and #944 were individually checked and are open.
  Their last-updated dates precede the session's September 27 issue-body review.

The origin still names `azurenoops/ato-copilot`; GitHub issue operations use its
canonical repository `azurenoops/spin_agent`. No issue was written, moved,
reopened or closed. An open issue is not proof a historical defect remains.

Keep the existing division of ownership: #1039 package purpose, #1040 source
authorization metadata, #1041 approved-profile output, #1042 submission
readiness, #1043 unavailable checks, #1044 executable rules, #1045 scoped
telemetry and #1046 system navigation. Do not implement these twice inside
onboarding or claim a setup form closes their acceptance.

Before any external update, preview exact comment/body/checklist/parent content
and obtain approval. No push is authorized by local implementation approval.

## Validation and delivery gates

Existing reuse baseline run on September 30:

```bash
cd src/Ato.Copilot.Dashboard
npm test -- src/__tests__/workspaces/SetupDialog.test.tsx \
  src/__tests__/routing/workspaceNavigation.test.tsx \
  src/__tests__/routing/workspaceRoutes.test.ts
```

Result: **86 tests passed, three files**. The initial invocation from repository
root failed because the root has no package.json; the command was corrected to
the documented Dashboard working directory, with no installation or workaround.
This is baseline evidence, not proof of the new setup UX.

### Agent-context generation prerequisite repair

The required command was attempted with the existing feature selected:

```bash
SPECIFY_FEATURE=078-role-aware-workspaces \
  .specify/scripts/bash/update-agent-context.sh copilot
```

The initial script printed two `grep: invalid option` errors but still reported
success. Sequential inspection of `update_existing_agent_file` showed its exact-match
checks pass entries beginning with `- ` to `grep -Fxq` without an option
terminator. The error is interpreted as "entry absent" and appends duplicate
technology lines. This run's generated-only changes were discarded; the
pre-existing instructions and manual sections were preserved.

A failing-first isolated fixture also reproduced macOS `sed` stripping a final
`t` from `React`: `[ \t]` is not portable whitespace syntax in this script.
The three-line generator repair uses a `grep` option terminator and POSIX
`[[:space:]]`. The regression is part of the existing Dashboard Vitest suite,
not a separate unexecuted test runner:

```bash
cd src/Ato.Copilot.Dashboard
npm test -- src/__tests__/config/agentContext.test.ts \
  src/__tests__/workspaces/SetupDialog.test.tsx \
  src/__tests__/routing/workspaceNavigation.test.tsx \
  src/__tests__/routing/workspaceRoutes.test.ts
npx tsc --noEmit
```

Result after repair: **87 tests passed in four files; strict type check passed**.
The Node-based fixture initially exposed missing Node type declarations;
`@types/node` was added as a dev dependency with the lockfile updated through
npm, then types were rerun successfully. npm reported 13 dependency audit
findings; they were not triaged or changed by this prerequisite repair.

The Dashboard production build also passed. It emitted warnings about stale
Browserslist data, SignalR PURE annotations, a CSS identifier, mixed
static/dynamic imports and bundle size. Their baseline classification was not
verified in this pass; the build is not described as warning-free.

The feature context command then completed without the grep errors and retained
full `Playwright`/`contract` values. Existing historical truncated entries were
not manually rewritten. Generated changes are from the script only; manual
instruction sections are preserved. This tooling fix does not clear the
application contract-review gate or claim new onboarding behavior.

Behavioral PRs must first add failing AAA regressions to their mapped suites,
then implement and verify. Use deterministic synthetic fixtures. Required
milestones remain:

```bash
dotnet build Ato.Copilot.sln
dotnet test Ato.Copilot.sln
cd src/Ato.Copilot.Dashboard
npx tsc --noEmit
npm test
npm run build
```

Expect clean build/types and passing tests; report actual failures and baseline
comparison rather than suppressing them. Type-check every other touched TS
client independently. Do not run shared Docker/SQL fixtures concurrently or
restart shared infrastructure to hide failures.

For each increment supply real local seed/run instructions, role, route, fault
case, expected retained IDs/outcomes and document/monitoring output. Track
implemented, checks passed, locally tested and user accepted separately.

Phase 0 does not ship application behavior. The contract-review gate is cleared
only after the three detailed domain amendments are reconciled and presented.
Expand/reconcile/switch/retire migrations and old-client/deep-link tests precede
any deletion. Historical receipts, grants, decisions, approved content and audit
are never deleted merely because their editor is retired.
