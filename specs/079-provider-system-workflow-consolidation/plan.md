# Implementation Plan: Provider-to-Mission Workflow Consolidation

**Feature**: 079 | **Spec**: [spec.md](spec.md)  
**Status**: Application implementation authorized 2026-09-26; acceptance pending  
**Baseline**: `13204325f21d2ff7e0028e0065a52bb795cb5bee`

## Summary

### Workspace header consolidation

Retire `WorkspaceHeader` from `ApplicationFrame` after moving its information and
authorized links into `AccountMenu`. `PageLayout` owns the switch-dialog state
and narrative-library icon next to Chat. Extract reusable authorized-choice
rendering from `WorkspacePicker` so first-login routing remains compatible while
switching uses a modal. Reuse `SetupDialog` focus trapping and cancellation.
Choice reads support AbortSignal and fail visibly; opening/cancelling performs
no selection, impersonation or cookie mutation. A confirmed switch navigates
only this tab to the selected authorized workspace root. Current-workspace
selection simply stays on the current route. Preserve unrelated local proposals.

### Personal Settings consolidation

Audit found `SettingsPanel` duplicates notification preference reads/writes with
silent catches, while `NotificationSettingsPanel` already has authenticated,
workspace-keyed request cancellation and explicit save handling. Reuse and
strengthen the latter, retiring the former duplicate. Reuse `SetupDialog` for
drawer focus trapping, Escape, responsive scrolling and return focus.
Use server workspace permissions for administration links; remove the no-session
administrative fallback. Keep operational navigation in organization/provider
administration, catalog, assessment/risk and export workspaces.

The current Settings hook stores browser-local values; searches found several
controls with no consumers. Do not perpetuate no-op controls. Trace and test
retained display preference consumers; retain historical storage keys for
compatibility but limit reset to an allowlist of personal display/presentation
keys. Existing local catalog filters must not be represented as organizational
baseline policy. Dates shown in authorization artifacts are not reformatted
by personal preferences.

### Shared system environment implementation slice

#### Superseding independence correction

The prior UI makes the provider card a projection of subscription attachments
and hides independent `EnvironmentAssociations` in history. Its selector requires
a provider allocation; this incorrectly treats valid unlinked hosting records as
reconciliation errors. Replace that coupling, not the underlying retained records.

Reuse provider scope/mission relationship and responsibility-review services for
independent provider consumption. Reuse canonical environment registration,
discovery, scope history and entitlement for subscription attachment. Make their
association optional, versioned and many-to-many where required. Allocation-based
attachment must not create hosting implicitly; unlink/detach/relationship removal
must not cascade to the opposite record. Preserve verified historic links and
surface only actual invalid references.

Implement backend/typed contracts with failing isolation/lifecycle tests in
parallel with parent-owned UI tests and the two mock-directed sections. Use one
subscription wizard across sources and preserve per-subscription scope/retry
state for multiple selection. The canonical batch validates every selection and
saves atomically, with the same payload/replay key retained after an unconfirmed
response. No silent bulk success or partial hosted relationship creation.
Verify assessments/monitoring still consume canonical subscriptions independent
of optional provider links. Existing broad-collector limits remain explicit.
Migration is additive, with no inferred ownership or lost reviewed versions.
No external issue writes or push without approval.

Retirement: `EnvironmentAssociations.tsx` has no remaining production imports
after `ProfileSectionForm` renders the independent provider/subscription
composition. Remove this obsolete allocation-first drawer and its component
suite after the replacement provider tests pass. Preserve `ProviderScopeReview`,
canonical relationship APIs and retained records. Replace the old form suite's
drawer-specific assertions with documentation preservation/integration assertions;
provider selection/review/lifecycle tests now belong to `ProviderServicesScopes`.
The old automatic copy-to-deployment action is not invoked by either new save
flow; deployment text remains user-authored and separately saved.

Current audit: provider `ProviderHostingAssignment` is system-specific technical
scope; it has no organization-level subscription allocation identity.
`AzureSubscriptionRegistration` already owns organization subscription identity,
cloud and Azure directory. Assessment config separately edits an owned
`AzureEnvironmentProfile.SubscriptionIds`; scope is not coupled to hosting.
The monitoring scope already reads boundary-component assignments, while a
legacy subscription resolver chooses only the first matching system.

Add a minimal organization allocation record and a tenant/system attachment
reference with optional provider fields, referencing—not cloning—the canonical
subscription registration. Persist explicit authorized selected resources and
immutable/revisioned scope history; project old AzureProfile fields only for
compatibility, never as a second editable list. Reuse provider hosting/mission
review and boundary services, Azure registration and access probes.

Separate management entitlement (MO/SO/ISSM as explicit server capability) from
existing assessment execution permission and organization registration authority.
Do not relax Azure cloud, directory, resource or collector checks. Workflow:
provider allocation/verified provenance -> eligible organization selection ->
resource scope -> atomic idempotent apply -> separate scope/access/review steps.
Scope discovery is read-only; future group resources are not automatically
included. Withdrawal/replacement requires impacted-system review and blocks
future access while retaining history.

First implement typed backend contract and tests, then consumers and UI:
Provider hosting before Connected environments, three-step wizard, separate
documentation actions, existing shell/logo. Preserve drafts through environment
actions. Use existing organization onboarding/settings for subscriptions missing
from registration; do not fake provisioning, Azure consent or ownership.
External FAST transport is supported only through verified source/provenance
contracts; absent credentials/mapping remain explicitly unavailable.

Verification uses synthetic provider-only, organization-only and mixed systems,
three subscriptions, shared allocation with distinct resource scopes, concurrency,
retry, withdrawal and denied operations. Live Azure collection must not be
claimed from those tests. Additive schema modules preserve legacy attachments
as unreviewed/reconciliation-required rather than silently mapped by names.

### Internal package acceptance follow-through

1. Reproduce and repair stale `CapabilityResponsibilityResponse` fixture calls,
   which omit the added `BaselineName` positional argument (the compiler reports
   the final required `PendingImpacts` argument). Preserve empty pending-impact
   collections and production contracts.
2. Run builds serially (`-m:1`) and execute tests with `--no-build` after the
   successful build to avoid parallel MVC manifest generation.
3. Add an isolated real-export acceptance fixture. Use canonical service/API
   operations for authorship/review/finalization; actual exporters and schemas
   remain registered. Trace distinctive synthetic source values into actual
   output, with no existing AO decision for InitialSubmission.
4. Close safe documented validation/resolution gaps using existing source
   semantics. Do not impose physical hardware requirements on managed services
   or claim unknown inventory coverage as a pass.
5. Revalidate changes, preserve previous artifacts/history, and document local
   reproducibility plus the external eMASS acceptance gate.

No live demo edits, external submission, issue publication or push is authorized
by this internal acceptance implementation.

### September 29 package-readiness implementation slice

Implement the [approved readiness contract](contracts/package-readiness-experience.md)
under existing US1/US4 and issues #1042/#1043/#1046. This is a replacement of
the passive Documents readiness body, not a redesign of authoring, assessment,
submission or decision workflows.

1. **Inspect/baseline**: preserve the dirty worktree; inspect the current Legacy
   selection, services, issue hierarchy and actual output path. Capture solution
   build/test baseline before modifying behavior.
2. **Server facts**: instrument the existing package validators with explicit
   check outcomes; add a purpose/source-bound retained readiness-run store.
   Reuse retained archive/change context resolution and existing document,
   role, exchange and decision records. Project current action permissions
   separately from immutable evaluated facts.
3. **Freshness/integrity**: deterministic source identities before/after checks,
   stale/concurrent outcomes, immutable history, and generation guards at
   enqueue/worker completion. Validate emitted artifact bytes; failed checks
   cannot yield a Completed package.
4. **UI**: preserve purpose in URL/navigation, require explicit purpose changes,
   implement the task-oriented mock, direct-link drawer, scoped return link,
   supporting-record summary and independent progress/RMF history. Keep
   existing export, preview, reconciliation and decision screens accessible.
5. **Verification**: synthetic purpose/conditional/concurrency/security tests,
   source-to-preview/export assertions, actual archive inspection, keyboard and
   desktop/mobile browser checks, then local deployment and manual walkthrough.

**Storage decision**: existing `PackageValidationResult` requires a package FK,
so a standalone check cannot be retained without a fake package or destructive
FK migration. An additive tenant-scoped readiness-run record is justified by
the explicit history requirement; retain the original package-linked results.
No new database provider, framework, package manager or external connector.

**Complexity justification (II/III)**: a fingerprint/check-history projection
is necessary to distinguish current from stale results and bind exports. The
rejected simpler alternative (browser timestamps or document counts) cannot
detect concurrent edits or prove which records were evaluated. Use concrete
existing record types rather than a generic workflow/rules engine.

**Validation commands**: `dotnet build Ato.Copilot.sln`,
`dotnet test Ato.Copilot.sln`, Dashboard `npx tsc -b`, targeted Vitest and
Playwright suites. Expected result is passing changed paths and no new warnings;
baseline failures/warnings must be reported separately, never suppressed.
Rollback uses the previously recorded API/Dashboard image tags; additive
history records and existing artifacts are retained, not deleted.

The user explicitly authorized implementation of the screen/route contract on
2026-09-26. Current work starts with mock-defined workspace navigation and real
provider/System routes plus tested export integrity. Historical planning-only
checkpoint results below remain historical, not current completion claims.
External writes and pushes still require separate approval.

Shared-shell decision: reuse `PageLayout`, `PageHero`, scoped navigation and
server workspace context. Replace CSP navigation composition with the mock's
five task destinations and provider context header; keep operational
administration/oversight reachable rather than deleting its functions. Systems
retain their own scoped navigation. Test context-specific rendering, active
routes and mobile links before cutover; do not copy the mock's simulation toolbar
or synthetic identities into production.

Verification repair: the source-upload test helper installed Node WebCrypto
directly into jsdom. Its native digest rejects jsdom FileReader ArrayBuffers
from a different realm before requests are made. Adapt bytes in the existing
test-only helper to a Node Buffer, preserving real SHA-256 calculation. Do not
alter browser upload hashing or substitute a fake digest to make tests pass.

Regression-fixture reconciliation: baseline reproduction identified obsolete
test doubles, not a reason to waive authentication/setup coverage. Preserve the
actual Axios `isAxiosError` export when mocking requests, supply the current
directory-connection API, reset provisioning doubles between scenarios, and
return consistent saved operations after key-based navigation. Tests must await
the actual asynchronously loaded control, not just its immediately rendered
container. Production authorization and enrollment rules remain unchanged.

Styling-build repair: Tailwind interpreted the narrative parser's `[-:\s]`
regular-expression text as an arbitrary CSS declaration, producing invalid
`-: \s` output. An equivalent regex alternation avoids that false class
candidate without changing the parser's accepted leading punctuation/whitespace.

Deliver the mock-defined UI through a coordinated refactor, retaining working
domain services and security/history guarantees. Begin with a failing synthetic
CSP -> Mission Owner -> document/export acceptance test. Repair only its
necessary foundations, demonstrate the complete slice, then expand the UI.
Do not use a broad backend rewrite or design-system project as a prerequisite.

The selected baseline was 75 commits ahead of this session's original
`bd06f9d9`; it has now been integrated by local fast-forward. No push occurred.
Source-inspected behavior is cataloged in [research.md](research.md), not claimed
as runtime verified.

## Technical context

**Language/Version**: C# / .NET 9 backend; TypeScript 5.7 / React 19 dashboard
**Primary Dependencies**: Existing ASP.NET Core, EF Core, React Router, Vite, Vitest, Playwright, MkDocs Material
**Storage**: Existing SQLite / SQL Server and retained file storage; additive version/history metadata and scoped workflow records
**Project Type**: Existing multi-project application; implemented changes with verification and release gates tracked in tasks.md
**Testing**: Existing xUnit, FluentAssertions, Moq, Vitest, Testing Library, Playwright

No new package manager, cloud SDK, application dependency, or storage provider
is proposed. Dashboard scripts were inspected: use `npx tsc --noEmit`, not a
nonexistent `npm run typecheck` script on this baseline.

## Constitution Check

| Gate | Plan disposition |
|---|---|
| I Documentation as truth | This spec, contracts, mock index and ADR precede behavior changes. |
| II/III Simplicity/YAGNI | Reuse proven services; every abstraction needs repeated concrete use. No generic multicloud framework. |
| IV SRP | Shared presentation and provenance projection have bounded responsibilities. |
| V BaseAgent/BaseTool | Any changed MCP tool retains existing base classes and envelope. |
| VI TDD | Red-green-refactor, AAA, synthetic data, and 100% modified-path coverage required by that section. A later quality-gate table says 80%; use the stricter 100%, do not amend the Constitution here. |
| Security/tenant isolation | Production-host read/write/job/export/download tests; no UI permission authority. |
| GitHub discipline | Existing issue parents retained; new umbrella/story publication is an open approval gate. No claim that proposed IDs exist. |
| Local typechecking | Run each touched TS project's actual checker during behavioral implementation. |
| User review | Distinguish implemented, automated passed, user accepted; provide exact local role/route/fixture. |

Planning can proceed. Implementation cannot be called release-ready while
issue linkage, required external contracts, tests or manual acceptance are open.
Guidance: [Constitution](../../.specify/memory/constitution.md), especially Core
Principles, Security, Development Workflow, and DevOps.

## Artifact index

- [Research/architecture/current routes](research.md)
- [Data model and authority](data-model.md)
- [Screen and route migration](contracts/screen-route-migration.md)
- [Status/readiness semantics](contracts/status-semantics.md)
- [Provider/mission API compatibility](contracts/provider-mission-api.md)
- [Document field lineage](contracts/document-lineage.md)
- [Migration/rollout/rollback](contracts/rollout-migration.md)
- [Tasks and test matrix](tasks.md)
- [Local fixture and review](quickstart.md)
- [Exact GitHub write preview](github-issue-preview.md)
- [Architecture decision](../../docs/architecture/adr-004-provider-mission-lineage.md)

## Phases and reviewable PRs

Each PR includes reused/consolidated/removed/deferred code, failing tests before
production changes, compatible contracts, documentation, and manual review.
Numbers below are local PR boundaries, not created GitHub PRs.

| PR | Phase/story | Bounded scope | Dependencies / exit |
|---|---|---|---|
| 0 | Planning | Baseline, copy design, spec/contracts/ADR, issue preview, agent context | No app changes; issue posting remains separate approval. |
| 1A | 1/US1 | Package purposes and retained validation context (#1039) | Introduce US2 failing fixture first; initial package creates no AO decision. |
| 1B | 1/US1 | Reviewed provider authorization -> stable export references (#1040) | 1A; exact metadata and referenced parties resolve; coordinate #970/#764. |
| 1C | 1/US1 | Approved profile/structured children and duties -> documents (#1041) | 1B; coordinate #969; drafts do not overwrite approved exports. |
| 1D | 1/US1 | Server purpose-specific readiness and unavailable/error states (#1042/#1043) | 1A-1C; parity across Overview/Documents/eMASS. |
| 1E | 1/US2 | Adapt existing presentation primitives for the slice's mock screens | Only demonstrated repeated use; no speculative component framework. |
| 2A | 2/US2 | Receipt -> real worker -> private review -> immutable release | Test fixture starts before 1A; preserve retry/coverage/publication behavior. |
| 2B | 2/US2 | Allocation -> association -> pinned adoption -> duties/evidence | 2A; canonical responsibility handoff and evidence access tests. |
| 2C | 2/US2 | Mock-defined previews -> actual package and export handoff | 1A-1E,2B; production-host + browser + artifact checks and local user review. |
| 3A | 3/US3 | Provider offering/source/scope routes and small onboarding intake | 2C; mock fidelity; old route redirects; portal review not wizard review. |
| 3B | 3/US3 | Capabilities, release review, evidence and findings screens | 3A; existing services; explicit distribution and reviewed closure. |
| 3C | 3/US3 | Mission relationships, changes entry, administration/history | 3B; provider-scoped visibility, distinct customer decisions. |
| 4A | 4/US4 | Systems definition and controls/evidence groups | 2C; can follow 3A independently if no shared-file conflicts. |
| 4B | 4/US4 | Assessment/risk and package/eMASS screens | 4A; preserve specialist actions; no second readiness engine. |
| 4C | 4/US4 | Overview/team/history and retirement of replaced routes/state | 4B; all old deep links tested; all 30 page targets reviewed. |
| 5A | 5/US5 | Production ConMon route parity, attribution and collection health (#1045) | 2C/4C; two systems sharing subscription stay distinct. |
| 5B | 5/US5 | Executable rules/evaluations and document impact review (#1044) | 5A; replay/disabled rules, provider vs mission disposition, no AO automation. |
| 6A | 6/US6 | Concrete manual SaaS/service types and management arrangements | Slice complete; explicit contract approval; no fictitious Azure IDs/connectors. |

Phase 1 and Phase 2 are one slice-first sequence: tests expose the missing
connections before implementations are selected. Phase 2C is the gate for broad
screen replacement, not permission to diverge from the mocks beforehand.

## Reuse and replacement rule

For each affected unit, complete:

1. Baseline SHA and current files/symbols/execution path.
2. Target mock screen and observable behavior.
3. Keep/strengthen/refactor/merge/replace/remove, with rejected simpler alternative.
4. Imports, callers, APIs, jobs, MCP/extension consumers and security dependencies.
5. Record migration, retained IDs/history, compatibility and rollback.
6. Tests, measured outputs, local user-review route and expected results.
7. Old implementation to retire, owning issue/PR, and measurable exit condition.

Coexisting placement, subscription and adoption models may have distinct
purposes. Do not label all three obsolete or delete them solely to simplify UI.
Refactor duplicate entry/competing writes after tracing their actual contracts.
A rebuilt page must still call the canonical services.

## Risks and decisions requiring input

- Evidence redistribution requires approved sharing rules; default denial
  remains until configured. Decide permitted copies versus references before 2B.
- Target eMASS format/receiving workflow is unknown. Internal schema validation
  cannot establish receipt/import acceptance; record real outcomes separately.
- Required artifact applicability must be approved per package purpose, not
  assumed universally from historical feature text.
- Role conflicts with mock actions need explicit scoped policy decisions. Never
  grant narrative/responsibility/AO authority from Mission Owner or admin labels.
- Mock suites may omit production edge states. Preserve the design and seek
  focused approval for necessary additions, not an agent-invented redesign.
- The copied design archive includes an onboarding companion that appeared
  after the initial inventory. Copying does not authorize its full redesign.
- Legacy-data collisions must be inspected on a safe copy before migration.

## Complexity tracking

### SSP reference presentation decision

Add an SSP-only presentation component using the existing parsed preview and
field renderer. Keep SAP/SAR/POA&M presentation, APIs, source selection, retention,
and export behavior intact. Match the supplied DOCX's styling and section order;
use a responsive continuous document rather than asserting Word pagination.
Unmapped records remain in the complete generated appendix. Government logos,
template example signatures and legacy instructional boilerplate are not system
records and are not inserted. No new parser, package, or document engine is needed.

Constitution check: documentation precedes code (§I); existing services and a
single presentation component satisfy §II–IV; failing presentation tests precede
implementation (§VI); server authorization and source provenance are unchanged.
Verify focused unit tests, Dashboard `tsc --noEmit`, production build and synthetic
desktop/mobile browser tests. Required backend commands remain
`dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln` (expected: pass).
Rollback reverts only this viewer component, its integration, scoped styles and
associated documentation/tests; it does not modify any system records.

No blanket exception requested. A shared readiness projection is justified by
three existing divergent consumers; an evidence/provenance extension is
justified by the broken provider-to-document relationship. Final entity shapes
must reuse existing identity/version/storage machinery. Any further abstraction
requires an updated decision record before implementation.

## Validation and rollback for this planning delivery

The planning-only results below are historical. Application implementation and
the latest local verification are recorded in the
[implementation checkpoint](tasks.md#implementation-checkpoint-and-release-gates).
Full SQL Server execution is currently environment-blocked; user acceptance and
external publishing remain separate gates.

Check copied source/destination hashes, local Markdown links, screen coverage,
`git diff --check`, and `python3 -m mkdocs build` with output outside tracked
`site/`. Run the existing agent-context script using `SPECIFY_FEATURE` for 079.
No .NET/TS application tests are necessary for these documentation-only edits.
No runtime success is implied.

Rollback removes only this delivery's new documentation and reverts its narrow
doc/config/generated-context changes; it does not remove the integrated baseline
or modify sibling checkout work. Do not perform rollback without approval.

### Planning checkpoint results

- Local fast-forward completed at the selected full baseline SHA; no remote
  branch was changed.
- Copied 198 source design files, excluding `.DS_Store`; all SHA-256 comparisons
  matched. The initial inventory was smaller; the additional onboarding
  reference is explicitly not a newly authorized application workstream.
- Local links in this new spec set, the design index and ADR resolved in the
  repository. The matrix contains all 36 provider and 30 Systems target rows.
- `git diff --check` passed. No application source, dependency manifest,
  runtime database or deployment configuration was changed.
- The required agent-context script ran and generated the expected Feature 079
  entries; its manual block was verified byte-for-byte unchanged. It emitted
  two `grep: invalid option` diagnostics: the existing deduplication checks pass
  strings beginning with `-` without an option terminator. This is not a clean
  generator run; no script fix or repeated regeneration was performed.
- MkDocs was missing from the default Python, so tooling was installed into an
  isolated session virtual environment, not the repository or global Python.
  The build then succeeded with **131 warnings**. These include retained
  README/index prototype collisions and repository-relative source/spec links
  outside MkDocs' document root. The new navigation opens the preserved HTML
  prototypes directly; source/spec links are for repository browsing, not a
  claim that spec-kit pages are published in the generated site.
- Both copied prototype entry pages loaded in the integrated browser. The first
  provider open failed at browser navigation; navigating the already-open page
  succeeded. This is a smoke check only, not a rerun of the prototypes' full
  visual/interaction suites or production acceptance.
- Application tests, live external integrations, new application screenshots,
  GitHub writes, commits and pushes were not performed. User acceptance remains
  open; follow [quickstart](quickstart.md) for local review.
