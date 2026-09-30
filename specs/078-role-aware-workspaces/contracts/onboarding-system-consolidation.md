# System onboarding consolidation — Phase 0 contract

**Status:** supported local system setup/source increment implemented and
targeted checks pass; wider migration/output/monitoring gates remain open.
**Evidence date:** September 30, 2026.
**Checkout inspected:** `078-onboarding-consolidation`,
`052120a18647bf0078afceacd7b14d0fcea8aab0`,
origin `https://github.com/azurenoops/ato-copilot.git`.

This is the system/general-onboarding portion of the approved session plan's
Phase 0. It closes its intake/import/task/scope evidence holds. It does not
authorize application changes or retire endpoints. Provider intake and
organization creation/membership amendments belong to their separate contracts.
Existing untracked design work was not modified.

### Local implementation checkpoint (September 30)

The authorized frontend increment now adapts the existing IntakeWizard to
seven system states through shared SetupFrame. Canonical client definitions
are `D/features/onboarding/systemSetupApi.ts`; `D/api/systemSetup.ts` is only
a compatibility re-export. Ready-state links come from current server
projection, not browser role inference. Setup explicitly states that no
document was generated/approved and that selected-system import application
and system-scoped collection are unavailable until their safeguards exist.

Failing-first regressions reproduced completion invoking discard, hidden
general completion failure, duplicate admin import UI contract drift, and
PDF import continuing after a failed correction save. Those fixes and draft
save/retry/all-seven-state/current-handoff regressions pass **12 tests in four
focused Vitest files**. TypeScript passed before the latest sibling edits;
the most recent whole-dashboard run is blocked by three TS2532 errors in the
organization-owned `TenantWizard/index.tsx`, reported to that owner.
No system backend production changes or dotnet runs have been made at this
checkpoint. Parent will capture runtime RED for
`FullyQualifiedName~SystemSetupContractTests` before backend edits.
These fixture-level results do not establish live API persistence, visual
acceptance, generated-document correctness or monitoring readiness.

Backend RED continuation: with the serialized .NET slot granted, sequential
host logs identified the disposed-provider symptom as an earlier build missing
`Tenants.OnboardingDraftJson` during bootstrap/fixture seed. Rebuilding current
organization schema hooks restored startup with **no fixture/auth changes**.
The six SystemSetupContractTests then produced the intended **5 failures /
1 pass**: absent GET routes return 404 and the absent create route fails the
coarse workspace authorization gate. Backend implementation proceeds from
that runtime RED, not from the earlier host-startup error.

The expanded system HTTP suite then captured **8 intended failures / 1 pass**.
Backend files now provide a bounded `SystemSetupService` adapter over existing
RegisteredSystem records (legacy IRmfLifecycleService/MCP signatures unchanged),
typed draft commands, shared create-access projection, actor-bound replay,
opaque-ID reads, strong revision checks, finite TodoService preparation intent
and explicit unknown monitoring state. Selected-system upload currently returns
`503 SYSTEM_SOURCE_REVIEW_UNAVAILABLE` without storing a file; legacy admin
imports use the consolidated existing typed UI. Full worker/review application
is not delivered by this checkpoint.

Two SQLite schema tests pass: repeated additive upgrade retains an opaque
legacy ID, and creation keys are unique within tenant/actor scope. The
integration project compiles. The parent-owned model, schema-startup, DI and
endpoint hooks still need wiring before the new HTTP suite can turn GREEN;
no shared Context/Program changes were made by the system agent. Source-to-
generated-document correctness and unconfirmed technical metadata in legacy
output projections remain release gates, not claimed completed.

Browser checkpoint: `e2e/tests/system-onboarding-consolidation.spec.ts` passes
four real-SPA tests against the parent's local preview at port 5179, using
`installWorkspaceFixture` and explicitly synthetic canonical API responses.
All seven states were captured at 1440px and 390px, plus saved-exit/reload,
permission revocation and lost-response/same-key retry. Screenshots are in
Playwright's per-test output directories. This exposed and fixed the old
Portfolio form/chrome remaining behind the new guided setup frame. The
accessible browser combobox selector was corrected without weakening the
contact assertion. TypeScript passes. These tests do not establish live
authentication, storage, cloud connectivity or successful generated artifacts.

The positive source-to-document requirement was covered by newly written
`SystemSourceReviewTests`: original bytes/hash, explicit target-bound field
review/apply, retained setup identity, duplicate/wrong-system prevention and
actual generated DOCX content with source provenance. They await the wired
system HTTP baseline and a serialized runtime RED run before positive source
implementation. The former honest-503 state is not feature completion.

Once the system hooks were wired, sequential startup logs exposed a separate
real migration dependency: the setup uniqueness index referenced TenantId
before the legacy RegisteredSystems table had received Feature 048's tenant
column retrofit. A focused legacy-table regression is added before fixing the
order inside the owned schema module; tenant ownership/backfill stays with the
existing canonical tenant migration rather than a fabricated setup tenant.

### Verified supported increment (supersedes the earlier checkpoint limits)

The wired setup HTTP baseline passed **9 tests**, then positive source tests
reproduced the intended 503 RED before implementation. The implemented
selected-system source path now retains a digital SSP PDF or single-system
XLSX up to **4 MiB**, using the existing source tables, `IFileStorageProvider`
keys, SHA-256 and canonical parsers. It performs bounded inline analysis,
not the legacy unsafe commit worker. Upload returns 201/200 receipt (not the
proposed asynchronous 202/job contract). Larger files and ZIP packages remain
unsupported in this selected-system increment; bulk admin APIs are retained.

Session additions actually implemented: TargetSystemId, RequestKey,
RequestPayloadHash, RequestActorPersonId, ReviewRevision, ReviewProposalJson,
ReviewSnapshotJson and immutable ApplyReceiptJson. A receiving row is saved
before storage/analysis; the same key/bytes can reconcile/retry. A confirmed
receipt requires retained bytes and saved analysis outcome. Parsed identity
fields are name/acronym; unsupported extracted values remain visible. No
system is created or matched by name during receipt or apply.

`SystemSourcePanel` uploads only on explicit action; save-later does not upload.
`SystemSourceReview` is a separate saved route at
`/systems/{id}/setup?source={kind}&receipt={sessionId}`. Review defaults to
**keep current** and requires an explicit checkbox before applying decisions.
Preview binds source revision/hash, exact opaque target/fingerprint and actor.
Apply is transactional, checks concurrent identity changes, retains original
before/after provenance, and rejects identity changes when an approved SSP or
profile baseline exists. Legacy bulk commit/correction services and the legacy
commit worker now reject bound selected-system receipts, preventing an
alternate mutation path.

`SystemSourceReadProjection` feeds receipt/tasks and actual
`DocumentTemplateService` output. A real generated DOCX test verifies the
original setup name and mission purpose, explicitly reviewed acronym, source
SHA-256 and truthful “Not recorded (setup draft)” technical metadata. This
proves the supported reviewed **source-field → document** path, not full SSP
approval, receiving-profile acceptance or eMASS submission. Tenant-specific
organization lookup replaces the stale singleton assumption in this rendering
path. Source review never approves a document or authorization decision.

Monitoring projects actual saved AzureProfile presence; an HTTP regression
verifies present configuration remains `access=notChecked`,
`collection=unknown`, `evaluation=unknown`. No fake per-system telemetry or
rule engine was added. The existing assessment-access check remains an
explicit distinct action. Finite preparation/source tasks stay in TodoService;
source review tasks derive from actual pending/failed receipts and disappear
after applied review, rather than treating every source as permanently blocked.

Final targeted verification:

- **28 integration tests GREEN**: system draft/access/replay/opaque IDs,
  saved monitoring configuration, XLSX/PDF receipts, exact-byte recovery,
  wrong-system/stale/approved-baseline rejection, legacy-bound-commit denial,
  real DOCX provenance, and existing eMASS/SSP PDF endpoint compatibility.
- **14 selected unit tests GREEN**: schema order/replay/key uniqueness and
  legacy import services. Existing compiler warnings remain visible; no new
  system-file warning was suppressed.
- **14 frontend tests GREEN** across five focused files; TypeScript passes.
- **5 real-SPA synthetic Playwright scenarios GREEN** at the parent's local
  preview, covering seven screens at 1440/390, save/reload, revocation,
  uncertainty, and explicit source-review/document handoff. Screenshots are
  retained in per-test output; these mocks do not prove live auth/cloud.

Remaining explicit limitations: SQL Server migration/retry execution is not
tested here; large/background source analysis and source replacement remain
dependent work; system resource-scoped collection/evaluation and all export
formats/receiving-profile gates are not claimed complete. The user must still
review and manually exercise the local seven-screen result. No commit, push,
deployment, role grant, automatic approval or eMASS submission was performed.

### Phase 6 caller-backed UI retirement

Current imports were rechecked after replacement tests passed. PortfolioDashboard
is the sole production caller of IntakeWizard/useIntakeWizard. The eight old
step editors, old CompletionSummary and WizardStepper are imported only by the
old IntakeWizard branch and their old component tests; BaselineManagement has
its own canonical editor and does not import the step component. The
WorkspaceOperationsPage's similarly named local SetupWizardState belongs to
capability setup and is not the removed WizardState type.

Retirement removes the old step reducer/data/completed-booleans, cancel cleanup,
auto-advance completion view, stepper and all ten old step files. The retained
IntakeWizard is a thin entry adapter to the single server-backed journey;
useIntakeWizard owns only open/close presentation state. Legacy/unscoped intake
entry now explains that an authorized organization workspace must be chosen,
rather than reviving an independent editing engine. `/systems/new` and existing
domain APIs remain, with no record deletion. The RED regression confirms the
old fallback would still render Step 1 outside that workspace.

Obsolete tests removed: old reducer transitions/skip-means-complete, old stepper,
old registration POST form, old boundary step editor, and copied categorization
math unrelated to production code. The two old assignment-step assertions are
removed from MissionOwnerWorkflows; its readiness dialog and organization-role
assertions remain. CompletionSafety retains the general completion-error
regression; the actual new ready-state test now asserts opening the saved
system closes presentation without any non-GET request.

Retained: general OnboardingGate/Shell/Modal and their seven step components
have real bootstrap/admin consumers; canonical Step3/4 also serve both retained
admin import entry adapters. TenantWizard remains a separate activation domain.
Capability setup, profile, categorization/baseline, roles, privacy/interconnection,
boundary, import/storage/worker, authorization and MCP services remain. Source
receipts, grants, reviews and all historical records are untouched. The unused
general useOnboardingState hook is retired; the navigator constants still used
by the general modal are retained.

Organization-owner coordination: after TenantWizard replacement, its prior
JobStatusPanel/SkipStepModal calls no longer exist. The organization owner
reported a full caller search with no remaining references and owns retiring
those two UI components. This supersedes the historical Phase 0 caller row
below; no new system consumer was introduced. BackgroundJobProgress still
has live canonical Step3/4 import consumers and is retained with job services.

Post-retirement verification: **17 frontend tests in seven files GREEN**,
TypeScript GREEN, and all **five synthetic browser journeys GREEN** again.
No imports of the removed step editors, stepper, reducer DTOs or cleanup hook
remain. The historical Phase 0 inventory below intentionally records the old
source locations for audit; those rows are not a claim the retired files
still exist. The actionable test commands below use the replacement suites.

Screenshot output correction: the earlier default Playwright output could be
removed by another runner. A fresh five-test system browser run passed using
`--output=test-results/system-onboarding`; 18 PNGs were verified present there
afterward. Future runs must use that domain directory, not the shared
test-results root. Surviving paths (relative to Dashboard):

- `test-results/system-onboarding/tests-system-onboarding-co-57cd0-es-at-1440px-synthetic-API--chromium/`
  contains all seven `s-*.png` desktop screens.
- `test-results/system-onboarding/tests-system-onboarding-co-0b703-ces-at-390px-synthetic-API--chromium/`
  contains all seven mobile screens.
- `test-results/system-onboarding/tests-system-onboarding-co-d6e74-ment-handoff-synthetic-API--chromium/`
  contains `s-sources-confirmed.png` and `source-review-applied.png`.
- The same domain directory contains per-test `s-forbidden.png` and
  `s-uncertain.png`. These are synthetic API screenshots, not live-cloud proof.

Dual-role guard review: `WorkspaceService.ResolveAsync:150-162` explicitly
sets context.IsCspAdmin=false for a persisted ordinary organization membership,
even when the authenticated user has global CSP.Admin. Provider workspace and
validated support mode set it true separately. SystemSetupService.Actor's
guard therefore rejects oversight/support contexts, not legitimate dual-role
ordinary members. No production guard was removed. Added
`SystemSetupContractTests.DualCspAndOrganizationIssm_UsesOrdinaryMembershipWithoutCrossTenantOversight`
to lock in create-access parity and cross-target rejection through the real
resolver; execution is pending the serialized .NET slot, not claimed passed.

Final dual-role rerun attempt: system schema tests pass **3/3** on the current
tree. The integration compilation was blocked before test execution by a
concurrent provider-owned assertion at
`ProviderSetupReceiptContractTests.cs:101` (`GuidAssertions.BeNull`, CS1061).
That owner was notified; no system production or auth guard was changed to
work around it. The previously passing system/source runtime evidence remains
valid for that earlier tree, while the added dual-role regression remains
unverified until the shared test project compiles again.

Review follow-up tests READY (not yet executed; parent owns .NET): confirmation
must include the identity fingerprint actually reviewed, including when a
canonical system update changed Name/Description without SetupRevision;
validated explicit type/criticality/hosting updates must atomically clear only
their setup “unconfirmed” markers; and default DOCX must retain a 700-character
mission purpose and all three reviewed source hashes, not truncate values at
500 characters. These are tightly coupled correctness fixes, not a new export
engine. Tests are named
`Confirm_RejectsIdentityEditedThroughCanonicalSystemUpdateAfterReview`,
`ExplicitTechnicalUpdates_ClearOnlyValidatedMarkers_AndRenderRecordedValues`,
and `DefaultDocx_PreservesLongPurposeAndEveryReviewedSourceHash`.

Parent captured all three intended runtime failures before production edits.
Fixes are now code-READY for the parent's combined GREEN run:

- Confirm requires the reviewed identityRevision; it is part of the command
  hash and compared to canonical identity even when no draft input is supplied.
  The frontend sends it and the existing successful-confirm fixture now does
  too. Replay still returns only the prior write outcome/current projection.
- The canonical system PUT collects only explicitly supplied, validated
  systemType/missionCriticality/hostingEnvironment fields. In the same EF save
  it removes only those unconfirmed markers, increments SetupRevision and
  invalidates the prior setup-command receipt. Invalid/absent fields do not
  clear markers; concurrent saves return 409. Legacy records without setup
  metadata retain their behavior.
- Built-in DOCX no longer truncates merge values at 500 characters. It writes
  full XML-escaped paragraphs with whitespace preservation, including each
  source provenance entry and the full mission purpose. No unrelated export
  format or template engine was introduced.

No .NET command was run by the system agent for these production fixes;
compile/runtime GREEN is pending the parent-owned lane.

Parent subsequently reported combined HTTP **81/81 GREEN, zero skips**,
including these review corrections. Required-Docker SQL Server then reproduced
a distinct first-upgrade failure: one batch added SetupRequestKey and compiled
the filtered index referencing that new column. SQL Server binds that index
before the ALTER takes effect. Fix: execute the guarded RegisteredSystems
column additions and guarded index creation as two awaited SQL commands.
Source-session upgrades already execute each column ALTER before their
separate index command; retain that ordering. The real SQL Server retained-
draft/source regression is unchanged, with parent-owned required-Docker rerun
pending. No fixture or index assertion is weakened.

Full-integration cover-page fixture follow-up: the actual failing DOCX emitted
the missing-organization placeholder. Sequential fixture inspection showed
RegisteredSystem.TenantId was left Guid.Empty while OrganizationContext used a
different random tenant; the unscoped InMemory fixture had no stamping
interceptor. The old singleton read concealed that mismatch. Fix only the
fixture: use the same explicit tenant for the system and its organization,
seed an unrelated organization's context as an exclusion check, and retain the
expected organization-name assertion. Keep the production tenant predicate.
Selector: `FullyQualifiedName~OrganizationContextSspCoverPageTests`; parent
owns execution while full integration continues.

**Phase 0 verification boundary:** the baseline facts below were verified by
sequential source inspection, not by executing the application. No build,
test, live Azure call, database migration, commit or external write was
performed for the original Phase 0 document. Subsequent local implementation
verification is recorded separately in the checkpoint above.
“Source-confirmed” is not a reproduced runtime defect, a passing test, or a
claim that a complete user journey works. Regression reproductions and the
user's local acceptance remain implementation gates.

Paths in evidence tables are repository-relative. To keep tables readable:

- `D/` = `src/Ato.Copilot.Dashboard/src/`
- `M/` = `src/Ato.Copilot.Mcp/`
- `C/` = `src/Ato.Copilot.Core/`
- `A/` = `src/Ato.Copilot.Agents/`
- `O/` = `A/Compliance/Services/Onboarding/`
- `ME/` = `M/Endpoints/`
- `T/` = `tests/`

Line ranges refer to the inspected checkout, not to generated documentation.

## 1. Governing decisions and reconciled specification drift

1. Adapt `IntakeWizard`/`useIntakeWizard`; do not introduce a second system
   wizard, workflow runner, task database, or readiness calculator.
2. Implement the seven **system** mock states, not the old eight-step UI:
   `s-details`, `s-team`, `s-sources`, `s-hosting`, `s-connect`, `s-review`,
   `s-ready`. Source:
   `docs/design/onboarding-mock/app.js:88-120` and
   `docs/design/onboarding-mock/screens/index.json:92-133`.
3. Save & finish later is an explicit server draft save. Back/Next within
   unsaved form sections preserves edits in memory. Neither action grants a
   role, commits an import, adopts a capability, approves a document, advances
   RMF, or starts collection.
4. Setup completion means a retained identity, preparation objective/contact,
   explicit choices and a truthful remaining-work projection. No fabricated
   authorization decision is required for an initial ATO objective; the
   “maintain” objective does not establish an existing ATO or cATO.
5. Preserve all existing system IDs, tenant ownership, source bytes/hashes,
   roles, revisions, approvals, assessments, exports and phase debt.

| Contract read | Current discrepancy / amendment proposed here |
|---|---|
| `specs/042-system-intake-wizard/spec.md:185-220`, `data-model.md:1-68`, `contracts/dashboard-api.md` | Original seven steps, unique name, preserved step data and cancel-only-unsaved semantics are requirements, not an accurate account of today's eight-step implementation. Role picker now uses `Person`, not Person-typed `SystemComponent`. New resumable draft is additive; no historical “Setup Incomplete” claim can be inferred from browser flags. |
| `specs/047-onboarding-wizard/spec.md:265-298`, `contracts/imports-api.yaml:1-85`, `contracts/progress-events.md:1-85` | Background completion, reload recovery, fully wired evidence seeds/cascades and the example `jobId`/preview payloads exceed the inspected implementation. The wire inventory below takes precedence for compatibility; corrected contracts must distinguish legacy and additive routes. |
| `specs/078-role-aware-workspaces/spec.md` “Onboarding consolidation”, `plan.md` same heading | Mock fidelity, explicit server save, separate authority and no duplicate engine are retained. This document is evidence closure, not a claim of Phase 4/5 completion. |
| `specs/078-role-aware-workspaces/contracts/system-security-capabilities.md:1-65` | Existing Environment primary task combines hosting and capabilities. System onboarding deliberately reuses its **hosting-only** compatible mode; adoption/duty confirmation remain separate destination actions. Keep old URLs and `#azure-assessment-environment` redirect. |
| `specs/078-role-aware-workspaces/contracts/package-imports.md` | Provider package receipt/review is a different domain. Do not send mission SSP/eMASS sources through CSP package endpoints or manufacture a provider release. |

Constitution gate: this is the paper trail before code. Later behavioral work
requires failing AAA tests first, real HTTP permission tests, relational
SQLite/SQL Server replay/migration tests, scoped workers, appropriate TypeScript
checks and local user review. No new feature number or external issue write is
introduced here.

## 2. Intake: complete surface → API → service → persistence trace

`ApplicationRoutes.tsx:60-63` protects `/systems/new` with `RequireAuth`.
`SystemsNewRoute.tsx` redirects to the existing Systems page with `openWizard`;
`PortfolioDashboard.tsx:34-46,213-225` owns the hook and modal.
Dashboard API paths below have prefix `/api/dashboard`; roles and onboarding
paths are exceptions explicitly named. `api/portfolio.ts:55-100` does not
implement a separate intake API.

| Named surface / state | Source-confirmed write/read chain and retained records | Disposition |
|---|---|---|
| `SystemRegistration` | `D/components/wizard/steps/SystemRegistration.tsx:33-105`: local form, validation, optional `/ai/system-description`, then **POST** `/systems` on Next. `ME/Dashboard/DashboardSystemsEndpoints.cs:143-209` → `IRmfLifecycleService.RegisterSystemAsync`; `A/Compliance/Services/RmfLifecycleService.cs:33-104` inserts `RegisteredSystem`, sets `Prepare`, saves, then best-effort role snapshot from `OrganizationContext`. Endpoint separately saves `DashboardActivity`. | Repurpose for `s-details`; save existing ID on revisit, not POST again. Identity fields remain canonical. AI suggestion is not reviewed content. |
| `SecurityCapabilities` | `D/.../steps/SecurityCapabilities.tsx:60-112,138-207`: GET capability links, library, org capabilities, subscriptions; selected CSP IDs POST `/systems/{id}/capability-subscriptions`; org IDs POST `/systems/{id}/capability-links`; DELETE local link. `ME/CapabilitySubscriptionEndpoints.cs:59-110` → `ICapabilityResponsibilityService`; `ME/Dashboard/DashboardComponentsEndpoints.cs:444-529` → `SystemCapabilityLinkService`. `C/Services/SystemCapabilityLinkService.cs:25-110` inserts links and may create `ControlImplementation` stubs from org mappings. Parallel selection can partially succeed. | Move detailed application to Security capabilities; keep real links/subscriptions. Do not execute on setup save or equate a link/stub with an implemented control. |
| `SystemComponents` | `D/.../steps/SystemComponents.tsx:1-94`: GET system components and org component search, POST `/components/{id}/assignments` or `/systems/{id}/components` immediately. `ME/Dashboard/DashboardComponentsEndpoints.cs:104-117,533-598` → `ComponentService`; `C/Services/ComponentService.cs:111-179,1477-1515` writes `SystemComponent`, `ComponentSystemAssignment` and optional `ComponentCapabilityLink`; creation may use existing primary boundary. | Move detailed maintenance to Components; retain entities/assignments. A component labelled Person is not membership or a grant. |
| `AuthorizationBoundaries` | `D/.../steps/AuthorizationBoundaries.tsx:1-112`: GET/POST system `/boundary-definitions`, GET candidates/components, POST boundary components; for non-CSP selection also POST `/boundary-definitions/{id}/resources`. `ME/Dashboard/DashboardBoundaryEndpoints.cs:31-85,190-250`; `.../DashboardAzureDiscoveryEndpoints.cs:221-295` → `BoundaryDefinitionService` / `ComponentService`; named definitions, `BoundaryComponentAssignment`, legacy resource `AuthorizationBoundary`, audit. `C/Services/BoundaryDefinitionService.cs:138-184` initially writes `IsPrimary=false`. | Retain multiple boundaries, exclusions and component provenance. Move full editing to Boundary; hosting allocation is not boundary creation or coverage. Two writes are not an atomic boundary transaction today. |
| `AssignRoles` | `D/.../steps/AssignRoles.tsx:1-60`: GET `/api/roles/system/{id}` plus **admin-only** `/api/onboarding/persons`; POST `/api/roles/system/{id}` with `{role,personId}`, then re-read. `ME/SystemRolesEndpoints.cs:87-140` → unified reader, caller resolver, assignment authorization and SoD detector; canonical `SystemRoleAssignment`/`Person`, with org/legacy fallback. | `s-team` shows effective source/scope. Save preparation contact separately; explicit grants remain Team actions with their own policy. Do not clone Persons or reuse admin-only discovery for ordinary system owners without an authorized read contract. |
| `VerifyRoles` | `D/.../steps/VerifyRoles.tsx:12-71`: read-only role GET, filters `not-assigned`; Next is enabled even with no assignments. No persisted verification record. | Consolidate with `s-team`; don't label a click an approval or all required roles satisfied. |
| `SetCategorization` | `D/.../steps/SetCategorization.tsx:67-87,124-165`: hydrate system detail, POST `/systems/{id}/categorization`, then optional POST `/baseline`; local info-type catalog and provisional impacts. `ME/Dashboard/DashboardCategorizationEndpoints.cs:32-141,171-216` → `ICategorizationService`, `IBaselineService`, activity/snapshots. Recategorization can automatically reselect an existing baseline when level changes. Persists `SecurityCategorization`, information types/history and `ControlBaseline`/inheritance data. | Move to Categorization / baseline destination; never execute from setup completion or imported metadata receipt. Preserve reviewed categorization/history. |
| `SelectBaseline.tsx` | A separate baseline component remains in the steps directory but is **not imported by `IntakeWizard`**; live categorization includes its own baseline phase. | Removal candidate only for unused presentation after callsite regression; retain baseline APIs/services. |
| `PrivacyAnalysis` | `D/.../steps/PrivacyAnalysis.tsx:41-162`: local PTA and interconnection form; POST `/pta`, `/generate-approve-pia`, `/certify-no-interconnections`, `/interconnections`. `ME/Dashboard/DashboardCategorizationEndpoints.cs:415-567` calls privacy/interconnection services, persists PTA/PIA/interconnections and activity. PIA convenience endpoint both generates and calls review with `Approved`; it is not merely a draft generator. PTA `purpose` and interconnection `hostname` sent by UI are not forwarded by these shown handlers. Finish is locally gated by PTA/PIA and interconnection response flags. | Move to Privacy/interconnection destination. New setup must not silently certify no interconnections or generate-and-approve a PIA. Keep compatibility endpoints pending separately authorized review-policy remediation. |
| `CompletionSummary` | `D/.../steps/CompletionSummary.tsx:24-61`: on mount GET `/phase-readiness`; if ready, POST `/advance-rmf-step`; Go to system calls `onClose`. `ME/Dashboard/DashboardCategorizationEndpoints.cs:276-364` performs phase advance and writes `DeferredPrerequisite` for forced gates separately from the lifecycle call. | Replace completion **presentation** with `s-review`/`s-ready`. No automatic phase advance. Keep phase readiness/advance and their gates in RMF workflow. |
| `WizardStepper`, `WizardState`, `WizardStepData` | `D/components/wizard/IntakeWizard.tsx:1-152`, `D/hooks/useIntakeWizard.ts:23-146`: eight local completion booleans; Skip also marks a step completed; only registration data are actually returned through `onNext` by the live modal. State is reset on OPEN/CANCEL. | Reuse frame/presentation where appropriate; replace completion booleans with server-derived facts. Preserve dirty values across unmount/back. |

### 2.1 Cancel versus save: exact current sequence

1. Registration Next creates an active system. Revisiting registration renders
   the same POST-only component; it receives no existing system ID.
2. `PortfolioDashboard.tsx:219` gives **every** `onCancel` the callback
   `cancelWithCleanup(systemId)` and immediately refreshes the portfolio.
3. `useIntakeWizard.ts:157-173` awaits DELETE if there is an ID, catches any
   failure without surfacing it, then resets state.
4. `DashboardSystemsEndpoints.cs:273-316` DELETE defaults to `IsActive=false`;
   `permanent=true` is a separate destructive deletion. Default does not roll
   back prior component/grant/import work.
5. Completion's `onClose` is the same `onCancel` supplied by the modal, including
   `handleGoToSystem` (`IntakeWizard.tsx:46-55`,
   `CompletionSummary.tsx:59-61`). Thus the source wires the completed-system
   navigation through cleanup too. A failing network request can instead leave
   an active system; the hook comment saying orphaned drafts are excluded does
   not make that true.

These are source-confirmed control flows requiring regression reproduction
before code. Proposed actions are distinct: **save and exit**, **leave without
saving current edits**, **explicitly discard a saved draft**, and **open saved
system**. Only the explicit discard command may soft-delete an eligible draft.

## 3. General onboarding: retain domain behavior, consolidate orchestration

All relative endpoints in this section use `/api/onboarding`. The shared
`onboardingApi.ts:1-135` uses MSAL/auth interception and `{ok,data,errorCode,
message,suggestion}` envelopes. It is not interchangeable with the newer
workspace `{data}` envelope.

| Surface / endpoint inventory | Source-confirmed owner and persistence | Disposition / boundary |
|---|---|---|
| `OnboardingGate`, `OnboardingShell`, `/onboarding` | `D/features/onboarding/OnboardingGate.tsx:44-131`: CSP availability probe/impersonation short circuit; GET state; errors leave gate closed; requires completed `OrganizationContext` and `Roles`. Shell `:23-92` reads state, supports `stepNav=admin`, closes to home. | Retain bootstrap gate pending organization contract; never use a saved system draft to satisfy tenant activation. Preserve admin rerun/deep-link compatibility. |
| `OnboardingWizardModal`, navigator, `useOnboardingState` | Modal `:105-185,360-402` owns local current step, auto-start, refresh, skip and final navigation. Skip catches failure then advances; final seed callback catches skip/complete failures, refreshes, closes and navigates home. Hook `hooks/useOnboardingState.ts:1-46` independently exposes read/start/skip but modal/gate/shell do not consume it. Navigator constants are reused; its standalone view has no live modal render. | Consolidate read/error behavior and keep failures visible. No success navigation after failed completion. Retire redundant orchestration only after all destinations exist. |
| GET `/state`; POST `/start`, `/steps/{stepName}/skip`, `/complete` | `ME/Onboarding/OnboardingStateEndpoints.cs:20-124`; `O/OnboardingStateService.cs:17-76,79-126,161-205,225-247`. GET can create `TenantOnboardingState`; start calls bootstrap grant service; completion directly stamps status; step upsert stores `OnboardingStepCompletion`, last step and audit. | Preserve historical state; not a system workflow table. Never call start as part of system draft save. Mandatory-step semantics stay under existing policy. |
| `Step1OrganizationContext`; GET/PUT `/organization-context/` | Component `:44-116` hydrates and saves fields; endpoint `OrganizationContextEndpoints.cs:28-118` calls `IOrganizationContextService.UpsertAsync`, then best-effort step stamp. `OrganizationContext` and `WizardAuditEntry` retained. | Initial context/settings UI reuse only; this is not owning organization creation. Document cover-page consumer still exists. |
| `Step2RoleAssignments`; GET/POST `/persons/`, GET `/persons/directory`, POST `/persons/{id}/promote`; GET/POST `/role-assignments/`, DELETE `/{assignmentId}` | Step `:33-135`; `PersonEndpoints.cs:19-105`, `RoleAssignmentEndpoints.cs:22-157` → Person/directory and organization-role services. Role endpoint checks assignment matrix and stamps Roles only when ISSM, ISSO and Administrator are present. Persists `Person`, `OrganizationRoleAssignment`; last-admin/SoD rules remain. | Detailed Team/organization maintenance retained. No system setup grant by selecting a contact. Organization membership/enrollment is explicitly outside this amendment. |
| `Step3EmassImport`; POST `/imports/emass/upload`; GET `/{session}/preview`; POST `/{session}/commit`; GET `/{session}/log` | See §4; session/job/source provenance records. | Optional receipt in `s-sources`; explicit review outside setup. Keep bulk admin API compatibility. |
| `Step4SspPdfImport`; POST `/imports/ssp-pdf/upload`; GET `/batches/{batch}/summary`; GET `/{session}/extraction`; PUT `/{session}/corrections`; POST `/{session}/import` | See §4. | Same; never invoke import just because receipt was saved. |
| `Step5AzureSubscriptions`; GET `/azure/subscriptions`; GET/PUT `/registrations`; DELETE `/registrations/{id}` | Step `:20-80`, `AzureSubscriptionEndpoints.cs:18-111` → delegated ARM enumeration and registration service. `O/AzureSubscriptions/AzureSubscriptionRegistrationService.cs:44-148`: upsert selected rows, remove explicitly dropped **visible** rows, retain selected-invisible as Unavailable. Saves `AzureSubscriptionRegistration` and audit, not system `AzureProfile`. | Move org registration/consent to appropriate authorized settings; system connection uses assessment configuration. Selection does not validate collection, health or system boundary. |
| `Step6Templates`; GET `/templates/`, POST `/templates/upload`, GET/DELETE `/{id}`, GET `/{id}/download`, POST `/{id}/replace`, POST `/{id}/default`, DELETE `/{id}/default/clear` | Step `:20-83`; `OrganizationTemplateEndpoints.cs:20-250` → `OrganizationTemplateService`; validates DOCX/XLSX, hashes/stores originals through `IFileStorageProvider`, manages `OrganizationDocumentTemplate`, defaults and stale dependencies. | Destination document/admin management; retain originals, defaults and all routes. Do not conflate these templates with the separate SSP export-template store. |
| `Step7NarrativeSeeds`; GET/POST `/narrative-seeds/`, DELETE `/{id}` | Step `:11-75`; `NarrativeSeedEndpoints.cs:20-99` → `NarrativeSeedDocumentService.cs:62-140`. Saves bytes via storage and `NarrativeSeedDocument`, but sets `EvidenceArtifactId=Guid.Empty`, `IndexingStatus=Pending`, returns `IndexJobId=null`; deletion is soft and requires confirmation for indexed records. | Keep honest Pending/unwired status; not a proven evidence-repository/indexing pipeline. Move out of setup; retain binary and citation history. |
| `BackgroundJobProgress`, `JobStatusPanel`, `SkipStepModal`; GET `/jobs/{jobId}` | `ME/Onboarding/WizardJobsEndpoints.cs:20-55` → persisted `WizardJobStatus`; SignalR is progress only. BackgroundJobProgress has live Step3/4 callers; TenantWizard still calls JobStatusPanel/SkipStepModal. | Consolidate presentation/polling only. Do not remove components still used by tenant onboarding. No implemented general `GET /jobs?status=...` discovery route was found in this endpoint module despite the older progress contract example. |
| `ImportedDocumentsView`; GET `/imports`, GET `/imports/{id}/dependencies`, POST `/dependencies/{id}/rerun` | `D/features/admin/imported-documents/ImportedDocumentsView.tsx:1-15,268-273`; `ME/Onboarding/ImportedDocumentsEndpoints.cs:23-107` → artifact inventory/dependency services. | Retain inventory/destination, correct duplicate import clients before reusing them. No broad artifact deletion or stale-flag clearing on onboarding completion. |

## 4. Mission imports: actual job effects and compatibility hazards

### 4.1 eMASS registration import is not a complete package import

`O/Emass/EmassImportService.cs:43-140` buffers bytes, computes SHA-256,
writes `WizardStorageKeys.EmassImport(...)`, saves `EmassImportSession`,
enqueues `EmassParse`, then separately marks Parsing/ParseJobId. The parser
(`EmassImportParser.cs:75-178`) reads identifiers, names and control/POA&M
**counts** from XLSX/ZIP entries. Counts are not imported control records.

`CommitAsync:164-219` checks tenant + Parsed status, enqueues `EmassCommit`,
then separately sets Importing/CommitJobId and records audit. No supplied
request key or optimistic revision is present. Two requests reading Parsed
are not serialized by this code.

`EmassCommitJobHandler.cs:44-172,185-220`:

- Instructions are keyed by case-insensitive system identifier, last duplicate
  wins; missing decision defaults to Skip. Malformed entries are skipped.
- Existing systems are matched by acronym **or name**, without an explicit
  tenant predicate inside `ApplyDecisionAsync`; its `tenantId` argument is
  not used in that query or new-system construction.
- New rows contain only identity plus default MajorApplication/MissionSupport,
  hosting `"Imported"` and synthetic creator string. Merge leaves existing
  identity untouched. Overwrite updates Name/Acronym/ModifiedAt only.
- Each system is independently saved, then linked to source checksum through
  `WizardArtifactDependency`. A later per-system failure does not roll back
  earlier systems. Per-entry failure is logged; the job is marked Succeeded
  even when the session status becomes Failed. All-Skip can be Imported.
- No control implementation, POA&M or authorization decision is created by
  this handler. UI must render per-entry outcomes, not job-success-as-package-
  completion.

### 4.2 SSP PDF registration import

`SspPdfImportService.cs:46-132` writes one hashed original/session and queues
one `SspPdfExtract` job per file. Batch is a grouping ID, not one system.
`GetBatchSummaryAsync:133-142` returns a list. Corrections at `:154-178`
replace the JSON array without revision or session-state guard.
`CommitToSystemAsync:181-246` is **synchronous**, requires Extracted, overlays
nonblank corrections, creates a **new** `RegisteredSystem` with only
name/acronym/default classifications and `"Imported (SSP PDF)"`, sets
CreatedSystemId/Imported and saves, then separately writes dependency/audit.
Null/blank corrections do not clear extracted values in `ResolveFields`.
It does not attach reviewed content to an existing setup system.

Wire contract from `ME/Onboarding/SspPdfImportEndpoints.cs:100-201`:
upload → `202 {ok:true,data:{batchId,sessions:[...]}}`;
batch → `data:[...]`; extraction → flat `fields`;
corrections → `{corrections:[{fieldName,value}]}`;
import → `201 {ok:true,data:{sessionId,systemId}}`.

### 4.3 Worker, tenant, restart and provenance limitations

`O/Jobs/WizardJobRunner.cs:73-122` persists `WizardJobStatus.Payload`, then
places an envelope on an **in-process Channel**. `WizardJobHostedService.cs:
58-121` starts readers and dispatches by job type; the shown startup/dispatch
has neither persisted-queue replay nor a tenant-context Push/lease.
Durable status is not durable execution/recovery.

This matters beyond a missing predicate: `AtoCopilotContext.cs:36-94`
disables tenant filters when no ambient context exists;
`TenantStampingSaveChangesInterceptor.cs:70-80` returns without stamping;
`TenantScopedQueryGuardInterceptor.cs:95-105` permits no-HTTP background calls.
Consequently these safeguards do not establish worker isolation or ownership
for the shown import handler. Runtime exposure has not been exercised here.
The additive system path **must not enable commit** until explicit worker
tenant/actor revalidation, scoped target IDs and retry claims are tested.

`WizardArtifactDependencyService.cs:30-73` upserts by tenant/source/dependent,
replaces version tag and clears stale. Preserve source version history in
session/review receipts rather than treating this mutable link as complete
field provenance. `Cascade/ImportRerenderJobHandler.cs:32-51` and
`ExportRerenderJobHandler.cs:33-52` only clear stale flags/set LastReRunJobId.
They do not reimport or regenerate documents. No ready-state or task resolution
may claim source freshness merely because these jobs ran.

### 4.4 Every discovered mission-import client class remains accounted for

| Caller | Actual contract dependency / disposition |
|---|---|
| General `Step3EmassImport.tsx:36-133` | Polls `/jobs/{id}`, initializes healthy entries to Merge, sends instructions, fetches log on terminal job; local session/job IDs are lost on remount. Preserve explicit review; rehydrate from scoped session inventory. |
| General `Step4SspPdfImport.tsx:35-138` | Batch polling/extraction/corrections/import; `saveCorrections` catches failure and returns normally, so `importSystem` can continue with older server corrections. Stop commit on failed correction save; add regression before changing. |
| Admin `EmassImportWizard.tsx:26-58,120-136,205-240,423-469` | Separate axios client expects `{rows,total,parseStatus}`, sends bodyless commit, polls nonexistent `/{session}/commit/status`. Actual endpoint requires instructions and returns `commitJobId`; parsed systems are a different shape. Consolidate into typed canonical import UI, not a third client. |
| Admin `SspPdfImportWizard.tsx:137-167,412-436,524-551` | Expects singular sessionId, object batch status, sectioned fields/`correctedValue`, and final `status=Complete`; actual shapes above differ. Retain admin entry and IDs; replace its transport/state adapter. |
| MCP `compliance_import_emass` | **Different service**: `A/Compliance/Tools/EmassExportTools.cs:137-198` → `IEmassExportService.ImportAsync`; `A/Compliance/Services/EmassExportService.cs:173-210,533-631` imports an existing system's controls sheet. Dry-run defaults true; Overwrite changes status/narrative, Merge appends narrative, direct non-dry run saves `ControlImplementation`. No wizard session/provenance or review-revision receipt is created by this path. Preserve tool contract; do not route setup “receipt confirmed” to it or assert it has wizard review protections. |
| eMASS round-trip destination | Separate existing `/systems/{id}/emass/*` workflow, exercised by `D/../e2e/tests/22-emass-workflow.spec.ts:1-75`; not the bulk registration import. Preserve conflict/export workflow; no live eMASS submission is introduced. |

## 5. Canonical task store decision

**Verified:** `C/Services/TodoService.cs:27-106` is a computed projection of
RMF/domain facts, not a durable task store. It adds phase-specific items,
POA&M/findings/deviations, outstanding information and phase-debt reminders.
`TodoDtos.cs:1-58` contains Id/Label/Detail/Category/Prompt/Link/DeferredId,
not owner, status, revision or durable user intent.

`ME/Dashboard/DashboardRoadmapEndpoints.cs:190-204` is the sole discovered
production `GetTodoListAsync` caller. `D/components/cards/TodoPanel.tsx:23-74`
polls it and also reads separate profile todos; callers are `SystemDetail`
and `SystemLayout`. Its prompts open existing task/action handling, not a
new queue engine. Retain these two presentations over the same projection.

`DeferredPrerequisite` (`C/Models/Compliance/RmfModels.cs:1190-1250`) is
durable **force-advance gate debt**, with required phase names and gate text.
Dashboard advance creates it; resolver
`DashboardAssessmentsEndpoints.cs:731-819` returns 422 for a failed matching
gate, but catches gate-check exceptions and then permits resolution. There is
no general owner/destination/applicability model. A setup deferral must not
fabricate phase names or reuse this resolution path.

**Decision:** retain `TodoService` as the canonical **read** aggregation.
Persist only bounded, typed setup choices/deferral/ownership intent on the
owning `RegisteredSystem` (schema below); derive whether work is satisfied
from the existing authoritative domain records. Do not add Task/Workflow
tables or route ordinary setup debt into Kanban remediation tasks. A genuine
finding may still create its existing remediation/POA&M record through the
existing explicit action.

Additive `TodoItemDto` fields (old fields unchanged):

```text
kind: phase | preparation | finding | poam | profile | monitoring
state: open | deferred | blocked | reviewRequired | satisfied
owner: { personId?: uuid, role?: string, displayName?: string }
source: { type: string, id: string, revision?: string }
reasonCode?: string
contribution: string
requiredOperation?: string
canAct: boolean
updatedAt?: instant
```

`satisfied` items appear only with `includeSatisfied=true`; default existing
todo response remains outstanding work. Stable IDs include source IDs:
`setup:definition`, `setup:team`, `setup:hosting`, `source:{kind}:{sessionId}`,
`capability:{id}:applicability`, `capability:{id}:duties`,
`setup:documents`, `monitoring:connection`, `monitoring:scope`,
`monitoring:evaluation`. Merge semantically equivalent existing phase/profile
items instead of displaying duplicate “assign roles”/“complete profile” tasks.
Do not trust the current Prepare test of **any** legacy RmfRoleAssignment
(`TodoService.cs:112-129`) as proof that all effective required roles exist.

Owner means preparation accountability, not permission. A task with no assigned
person shows its required role or “Owner not assigned”. `canAct` is re-derived
from the target operation. A role revocation or source revision change can
reopen/block a task. There is **no new manual “mark complete” endpoint**.
Deferral reason and owner are saved through the setup draft update; completion
is checked against real records, including review/permission/scope requirements.

## 6. Hosting, assessment and monitoring: present truth and delivery gates

| Surface | Verified flow | Required treatment |
|---|---|---|
| Provider hosting association | `ME/Csp/ProviderMissionEndpoints.cs:11-47`: GET/POST `/api/dashboard/systems/{id}/provider-relationships`, POST `/{relationshipId}/previews`, POST `/review`, GET `/applicable-provider-capabilities`, POST `/provider-capability-adoptions` → `IProviderMissionService`. Uses exact persisted allocation/relationship context and existing idempotency key path. | Reuse eligible allocation read/association only in `s-hosting`; provider publishing, coverage reviews, adoption and customer duty confirmation remain separate operations. |
| System assessment connection | GET `/assessment-environment`, PUT same, DELETE same; GET `/assessment-readiness`. `DashboardAssessmentEnvironmentEndpoints.cs:19-97`: reader entry plus writer check for readiness; writer for configuration. `AssessmentEnvironmentService.cs:55-155` checks active org, deployment/cloud, registered subscription selection, categorization and ARM access probe. Configure saves owned `RegisteredSystem.AzureProfile` + activity; no collection occurs. | Use existing configuration/probe, add truthful monitoring projection. A successful probe is assessment access, not telemetry health or reviewed scope. |
| System assessment execution | `DashboardAssessmentsEndpoints.cs:288-340`: readiness recheck; selects **first** validated subscription; calls comprehensive assessment with `resourceGroup:null`, then stamps system ID. Comment explicitly assigns scope/integrity to #982/#983. | Do not start this endpoint from onboarding or label its result system-resource-scoped collection. Scope fix remains a concrete dependency. |
| ConMon page | `DashboardConMonEndpoints.cs:31-133` reads system plan/decisions/findings and joins enabled monitoring configs, alerts and remediation rules by AzureProfile **subscription IDs**. | Present existing plan/authorization facts, but do not derive per-system monitoring health or attribution from shared subscription matches. |
| ConMon domain | `A/Compliance/Services/ConMonService.cs:45-91` upserts `ConMonPlan` frequency/review date/distribution/string triggers; report reads system effectiveness/decisions. | Retain plans/reports/significant changes; this is not yet typed trigger configuration with per-resource scope/condition/evidence lineage. |
| Watch configuration | `ComplianceWatchService.cs:61-121` upserts `MonitoringConfiguration` by subscription/resource group, frequency/mode/enabled; seeds rules. `ComplianceWatchHostedService.cs:96-206` runs due/event checks. | Retain existing scheduler and rules; no second monitor engine. Do not call enabled = healthy. |
| Drift evaluation/attribution | `ComplianceWatchService.cs:266-343` loads subscription baselines, runs subscription assessment, compares per-resource findings. `:1127-1188` attaches system and creates significant-change follow-up via subscription resolver. `SystemSubscriptionResolver.cs:52-130` caches subscription → **first system ordered by Id**, not `(tenant,system,resource,reviewed scope)`. | Must not represent this as correctly separated systems sharing a subscription. New setup returns explicit unsupported-scope/unknown-health tasks until scoped paths are implemented and proven. |
| Recorded boundary | Existing `AuthorizationBoundaryDefinition`, `AuthorizationBoundary.IsInBoundary`, `BoundaryComponentAssignment.IsInScope` and exclusions remain canonical inputs. | An allocation, subscription attachment or component label alone is not a versioned reviewed monitoring baseline. |

**Chosen Phase 4 behavior:** `s-connect` can defer or inspect/configure an
already permitted Azure assessment attachment and perform the existing
read-only access check. It must display separate `configuration`, `access`,
`scopeReview`, `collection`, `evaluation` facts. `collection` and `evaluation`
remain Unknown/Unsupported unless backed by actual system-scoped evidence.
Do not add an enabled “validate collection” button backed only by readiness.
Explain the limitation and retain the task/handoff.

**Phase 5 scope contract, not implemented:** extend the existing monitoring
configuration/baseline path to carry `TenantId`, exact `RegisteredSystemId`,
reviewed boundary revision and explicit included/excluded normalized resource
IDs; preserve before/after evidence and evaluated rule revision. Keep resource
group/subscription as transport selectors, never system identity. Each rule
requires source, scope, condition, severity, owner, cadence and follow-up.
Evaluation records need last attempt, last successful collection/evaluation,
permission/telemetry errors and scope revision. A changed scope invalidates
health; accepted documentation changes stage separately from the approved
baseline. These are requirements for the existing monitor owner, not permission
to implement an unreviewed new trigger schema in this onboarding increment.

SaaS/on-premises/hybrid remain valid descriptive hosting choices. Only the
currently typed, allocated provider service can be associated. Unsupported
service/cloud collectors have disabled actions and named follow-up, not fake
Azure subscriptions or simulated successful telemetry.

## 7. Exact proposed additive system contract

### 7.1 Transport, routes and actions

Canonical base:
`/api/workspaces/organizations/{tenantId}/systems`.
Use existing workspace navigation/tenant resolver and the operation-authorized
endpoint metadata. **System IDs are opaque strings**, not UUID-validated
identifiers. `RegisteredSystem.Id` is a string with `[MaxLength(36)]`
(`C/Models/Compliance/RmfModels.cs:82-85`) and EF `HasMaxLength(36)`
(`C/Data/Context/AtoCopilotContext.cs:1250-1254`). Its default UUID generation
does not constrain existing values to UUID syntax. Current system routes use
unconstrained `{systemId}` and `string systemId`, e.g.
`ME/Dashboard/DashboardSystemsEndpoints.cs:212-219` and
`ME/Csp/ProviderMissionEndpoints.cs:11-19`; the organization's `{tenantId:guid}`
constraint does not apply to nested system identifiers. Retain opaque legacy
values exactly, URL-encode as one path segment, and never apply `Guid.Parse`,
`:guid` routing, UUID normalization or replacement IDs to them. New system
IDs may keep the current UUID-string generator. Receipt/person/allocation
keys whose entity types are actually Guid remain UUIDs.

This agrees with `tasks.md:97-98` and
`T/Ato.Copilot.Tests.Integration/Tenancy/TenantScopedEndpointHttpPipelineTests.cs:83-105`
(`GetSystems_WithOpaqueIdentifiers_PreservesTenantIsolation`). The correction
changes the identifier's **semantics**, not the mapped maximum length:
proposed TargetSystemId remains a nullable string of maximum length 36, not a
Guid/uniqueidentifier. Do not truncate or rewrite existing IDs during migration.
That is the **EF/SQL Server mapping**, not a new transport validator on
already-persisted keys: the CI1049 regression at `:86-87` actually constructs
`"sys-a-" + 32 hex digits` (38 characters). SQLite TEXT can retain that
out-of-model length. Preserve exact existing IDs on reads/resume and
source-target lookup; validate their existence/ownership, not UUID syntax or
a newly imposed DTO length rejection. Keep SQLite TargetSystemId as TEXT and
the SQL Server column aligned with the actual system-key storage. A migration
must compare deployed key type/width and observed lengths before creating a
narrower reference column, fail visibly on an incompatible schema, and retain
all IDs rather than truncating/backfilling replacements. No deployed data
length census was authorized or performed here; the 38-character fixture is
not evidence of a production SQL Server width change.
Owning organization is the selected authorized
workspace tenant; `RegisteredSystem` has TenantId, **no OrganizationId** today.
This contract does not invent a second organization ownership/membership model.

Extend the existing `IRmfLifecycleService`/`RmfLifecycleService` domain with
`CreateSetupDraftAsync`, `GetSetupAsync`, `SaveSetupDraftAsync`,
`ConfirmSetupAsync` and `DiscardSetupAsync` using the DTOs below. Controllers
are adapters; direct service callers receive the same tenant/current-operation
checks. Reuse canonical RegisteredSystem creation logic, but the setup creation
path must **not** call the legacy best-effort role snapshot as a hidden grant.
Existing `RegisterSystemAsync` and its MCP/REST contract remain compatible;
effective organization role fallback does not require newly copied grants.
Task projection remains `TodoService`, role projection remains the unified
role reader/access resolver, and no configurable transition engine is added.

| Proposed route | Request / result | Authority / side effects |
|---|---|---|
| POST `/setup-drafts` | `CreateSystemSetupDraft`; required `Idempotency-Key`; 201 `{data:SystemSetupView}`, Location to same record; repeat returns 200 | Existing `CanCreateSystem` for selected org; creates one canonical system draft, no grants/import/connection. |
| GET `/setup-drafts?cursor=&pageSize=25` | `{data:{items:SystemSetupSummary[],nextCursor}}`; max 100 | Current readable systems; saved drafts separate from outstanding work. |
| GET `/setup-drafts/requests/{requestKey}` | Original accessible system ID/revision plus current view; 404 inaccessible or unknown | Same tenant + creating actor; key possession is not authority. Reconcile uncertain creation before new key. |
| GET `/{systemId}/setup` | `SystemSetupView`, strong ETag `"setup-{revision}"` | Current `CanRead`; never creates state or executes optional work. |
| PUT `/{systemId}/setup` | `UpdateSystemSetupDraft`, `If-Match`, `Idempotency-Key`; 200 new view | `CanManageSystem`; atomically save choices/contact/deferral intent and permitted identity fields only. |
| POST `/{systemId}/setup/confirm` | `{reviewRevision,identityRevision,confirmed:true}` + same headers; 200 view | `CanManageSystem`; validates exact reviewed identity/objective/contact, source-receipt reconciliation and actual choices; stamps SetupCompletedAt only. |
| POST `/{systemId}/setup/discard` | `{reason}` + same headers; 200 view with discarded state | `CanManageSystem`; explicit soft discard only before setup completion and with no committed non-setup dependents or running mutation jobs. Otherwise 409 `DRAFT_HAS_DEPENDENCIES` and ordinary system-management handoff. No hard delete. |
| GET `/{systemId}/setup/monitoring` | `SystemMonitoringSetupView` below | `CanRead`, redacted operational details if caller lacks existing connection-read permission. No collection. |
| Existing `/api/dashboard/systems/{systemId}/todos` | Additive fields in §5; optional `includeSatisfied` | Remains canonical work projection with current read access. No new task engine/API. |

UI compatibility: `/systems/new` still enters adapted IntakeWizard; new saved
route `/systems/{systemId}/setup?step=s-hosting` rehydrates it through workspace
builders. `/systems/{id}` and existing Team/profile/capability/assessment/
document routes remain destinations. Invalid/unavailable steps fall back to
the earliest **actionable** step without losing server data. Refresh/back
reuses the same ID. `s-ready` never invokes cancel/DELETE.

```text
CreateSystemSetupDraft {
  name: string[1..200], acronym?: string[0..20],
  missionPurpose?: string[0..2000],
  objective?: "initialAto" | "continuePackage" | "maintainSystem",
  contact?: { personId: uuid, responsibility: "preparationContact" },
  sourceChoice?: "blank" | "sspPdf" | "emass" | "deferred",
  hostingChoice?: "allocatedService" | "organizationManaged" | "deferred",
  monitoringChoice?: "configureLater" | "reviewAzureConnection",
  lastScreen: one of the seven s-* IDs
}
UpdateSystemSetupDraft extends CreateSystemSetupDraft {
  expectedIdentityRevision: opaque string,
  deferredIntents: [{
    key: one of §5's bounded preparation/monitoring kinds,
    reason: string[1..1000], ownerPersonId?: uuid,
    ownerRole?: existing role name
  }]
}
SystemSetupView {
  systemId, tenantId, displayName, revision, identityRevision, savedAt,
  setupState: "draft" | "confirmed" | "discarded" | "legacy",
  completedAt?, draft: CreateSystemSetupDraft,
  unconfirmedFields: string[],
  effectiveTeam: [{role, personId?, displayName?, source, scope, assignmentId?}],
  sources: [{kind, sessionId, fileName, sha256, receiptState,
             analysisState, reviewState, appliedState, jobId?, errorCode?}],
  hosting: {choice, relationshipId?, assignmentId?, scopeRevision?,
            offeringId?, status, errorCode?},
  monitoring: SystemMonitoringSetupView,
  tasks: TodoItemDto[], permissions: existing operation-derived permissions,
  lastCommand?: {key, payloadHash, appliedRevision},
  links: authorized destination links
}
SystemMonitoringSetupView {
  configuration: "absent" | "present" | "unavailable",
  access: "notChecked" | "passed" | "denied" | "error",
  scopeReview: "unknown" | "missing" | "reviewed" | "stale" | "unsupported",
  collection: "unknown" | "notRun" | "succeeded" | "failed" | "unsupported",
  evaluation: "unknown" | "notRun" | "succeeded" | "failed" | "stale",
  checkedAt?, lastCollectedAt?, lastEvaluatedAt?, scopeRevision?,
  reasonCodes: string[], evidenceIds: string[], configurationUrl, monitoringUrl
}
```

The view is a projection, not client-authoritative state. Ignore/reject attempts
to submit status, permissions, checksum, receipt IDs belonging to another
system, actual role assignments, authorization standing or task completion.
The same saved mission purpose uses `RegisteredSystem.Description`; there is
no competing authoritative system description.

The mock requires a system name. Before a first save, a blank/invalid name or
unselected authorized organization keeps the screen open with field errors;
do not navigate and pretend to have saved. **Partial** means objective/contact/
hosting/monitoring and later documentation may remain unset; confirmation,
not draft save, requires objective and accountable contact.

Existing required system-type/criticality columns cannot encode unknown. For
new setup-only drafts, retain their existing storage-compatible enum defaults
but record these fields in `unconfirmedFields`; store hosting as
`"Undetermined"` rather than claiming Azure Government. All new projections,
document preview/generation and assessment admission must consult this marker,
display “Not recorded”, and block authoritative use until actual values are
confirmed in System definition. Do not expose these storage defaults as facts.
Legacy records have no marker and retain existing semantics. This requires
the reader compatibility deployment **before** enabling draft creation.

### 7.2 Persistence, revisions, concurrency and recovery

Add to existing `RegisteredSystem`, not a new onboarding aggregate:

| Proposed column | SQL Server / SQLite representation; invariant |
|---|---|
| `SetupDraftJson` | nullable `nvarchar(max)` / TEXT; typed schemaVersion=1 payload for objective, contact reference, choices, bounded deferred intents, unconfirmed fields, lastScreen; max 64 KiB. Identity is read from canonical columns, not duplicated as a second source of truth. |
| `SetupRevision` | non-null bigint / INTEGER default 0; EF concurrency token, increment on each setup mutation. |
| `SetupCompletedAt` | nullable datetimeoffset / TEXT UTC instant; null is not evidence that a legacy system is incomplete. |
| `SetupRequestKey` | nullable nvarchar(100) / TEXT; retain original create key for lifetime of record, including soft deletion. |
| `SetupRequestHash` | nullable char(64) / TEXT; server SHA-256 over versioned canonical creation payload + tenant + actor. |
| `SetupActorPersonId` | nullable uniqueidentifier / TEXT; validated existing same-tenant Person; provenance, not a grant. |
| `SetupLastCommandJson` | nullable nvarchar(max) / TEXT; `{key,hash,action,baseRevision,appliedRevision,resultState}`; max 8 KiB, saved atomically with mutation. |

Unique filtered index `(TenantId,SetupActorPersonId,SetupRequestKey)` where
key is not null; ordinary legacy systems are unaffected. No name-based
deduplication, cross-tenant reuse or copied capability operation is permitted.
`CapabilitySetupOperation` is capability/subscription-specific and subject to
abandoned-preparation cleanup
(`C/Models/Workspaces/WorkspaceModels.cs:127-151`,
`WorkspaceOperationsSchemaAdditions.cs:41-70`); it is **not** a safe container
for unrelated system drafts.

Create/save/confirm/discard and audit operate in one DB transaction using the
provider execution strategy. On duplicate create key, compare full intent hash
and return same ID only for matching authorized actor/payload; different intent
returns 409 `SETUP_REQUEST_CONFLICT`. No timeout retry gets a new ID/key.

**Replay is not persisted authority.** Before every creation-key lookup,
command replay, source reconciliation or apply-result replay, resolve the
current authenticated actor to their current active membership/Person in the
exact workspace and recheck current visibility and the operation's permission.
An old receipt's actor, saved response, contact selection or once-valid role
cannot grant access. Stored receipt fields describe the historical write only;
return its stable IDs/applied revision/outcome with a newly projected current
view. Recompute permissions, effective roles, `canAct`, eligible destinations,
task state, monitoring facts and accessible source metadata; never replay a
serialized historical authorization-bearing view. If current authority is
lost, return 403/opaque 404 as applicable, without performing the write or
leaking the old response. That denial does not mean the earlier write failed.
Resume by system ID remains available to any **currently** authorized actor;
the creation-key's actor binding is only lookup/replay scoping.

For later commands require a strong `If-Match`; missing → 428
`SETUP_REVISION_REQUIRED`; mismatch → 409 `STALE_SETUP`. Check last-command
key/hash **before** the stale-revision rejection: exact replay returns its
applied revision/current view without repeating mutation; reused key with a
different payload/action → 409 `SETUP_REQUEST_CONFLICT`. If another command
has already superseded it, the older expected revision returns `STALE_SETUP`
with current revision, not historical success and never a second mutation.
Only the original **creation** receipt has lifetime lookup. This is an
explicit bounded replay guarantee, not a claim of an unbounded command ledger.
IdentityRevision is a SHA-256 fingerprint of Name/Acronym/Description and
unconfirmed-field markers. Setup PUT also compares expectedIdentityRevision
and conditionally updates against the original canonical field values, not
SetupRevision alone. This detects an intervening legacy system edit even when
that writer knows nothing about the setup columns; zero affected rows is
`STALE_SETUP`. Command keys are bound to action and base revision in their
hash; a superseded command is never retried with a newly substituted revision.

A save failure retains local values. A lost response enters `uncertain`:
read original create-key receipt or setup view, compare lastCommand/revision,
then retry the **identical** authorized request. Do not discard, regenerate
keys, auto-merge concurrent edits or treat permission loss as definitive
noncommit. Changing organization starts a different scoped intent only after
the existing uncertain request is resolved.

Source files still selected only in browser memory are **not** server saved.
Before saved exit, show “File not uploaded; reselect it when you return”, or
offer the explicit source receipt action; do not silently upload, discard an
uncertain upload, or represent browser metadata as confirmed receipt.

### 7.3 Additive selected-system mission source routes

Under the same selected-system base, add:

| Route | Exact responsibility |
|---|---|
| GET `/{id}/source-imports` | Paginated existing session summaries filtered by explicit TargetSystemId and tenant; links to canonical analysis/review. |
| POST `/{id}/source-imports/{kind}` | kind=`emass` or `ssp-pdf`; multipart **one file**, Idempotency-Key; 202 `{data:{kind,sessionId,jobId,receiptState:"confirmed",analysisState,sourceRevision}}`. Uses existing storage/session/parser/job services. No commit. |
| GET `/{id}/source-imports/requests/{kind}/{key}` | Same-actor payload-bound receipt reconciliation; current authorization required. |
| POST `/{id}/source-imports/associations` | `{kind,sessionId,expectedSourceRevision,confirmed:true}`, Idempotency-Key; bind an accessible legacy unbound receipt to this exact system without applying extracted values. 200 same session summary; already-bound-to-another-target or changed intent → 409. Requires both the legacy receipt's current read authority and selected-system `CanManageSystem`. |
| GET `/{id}/source-imports/{kind}/{sessionId}` | Receipt, source hash, job/error, extracted proposals, supported/unmapped fields, current review revision. |
| POST `/{id}/source-imports/{kind}/{sessionId}/review-previews` | `{expectedSourceRevision}` → immutable target revision/hash + field-level before/proposed diffs, conflicts, supported writes, previewHash. No mutation of system facts. |
| POST `/{id}/source-imports/{kind}/{sessionId}/apply` | `{expectedSourceRevision,expectedSystemRevision,previewHash,decisions:[{field,decision:"keepCurrent"|"applyProposed"}]}`, Idempotency-Key; explicit apply only; exact reviewed target, never acronym/name matching/new system creation. |

Add nullable `TargetSystemId` (opaque string, maximum 36; SQL Server
nvarchar(36) / SQLite TEXT), `RequestKey` (100),
`RequestPayloadHash` (64), `RequestActorPersonId` (UUID),
`ReviewRevision` (bigint default 0), `ReviewSnapshotJson`,
`ApplyReceiptJson` to both existing import session entities. The receipt stores
source hash/revision, target ID/revision, before/after field snapshots, decision
hash, actor/time and applied result. Unique filtered index
`(TenantId,TargetSystemId,RequestActorPersonId,RequestKey)`. Legacy sessions
remain unbound; do not auto-associate by name/checksum. Association of an old
receipt requires explicit reviewed selection of the exact target and prevents
cross-tenant reuse. An old already-imported bulk session remains historical
provenance, not a replayable single-system proposal: the association command
rejects it with `SOURCE_ALREADY_APPLIED`; use its existing dependency links.

Hash upload intent from tenant, actor, target, kind, normalized filename and
content SHA-256 (computed server-side). Same bytes with different key are
distinct explicit receipts; same key with changed bytes/target is conflict.
Claim session before enqueue; persist job linkage and pending dispatch
atomically. Extend **existing** `WizardJobStatus` with claim/lease/attempt/
last-dispatch fields and recovery scan: nullable `ExecutionClaimId` UUID,
nullable `LeaseUntil` UTC instant, `Attempt` integer default 0, `Revision`
bigint default 0 concurrency token, and nullable `LastDispatchAt` UTC instant.
Use unique job ID and a nonunique `(Status,LeaseUntil)` index; claim by
revision-checked update, scan Queued or expired InProgress work on startup and
bounded intervals, and requeue the same persisted payload/job ID. Existing
result checkpoints/apply receipt determine whether to execute or return the
committed result after a crash. JobRunner needs a transactional enqueue path
sharing the session unit of work; merely wrapping its existing independently
saved EnqueueAsync call is insufficient. Reuse the same runner/Channel, no new
job engine. Workers push the persisted tenant context, explicitly query
tenant+target+session, revalidate active actor and permitted action, and claim
jobs with optimistic revision. Original bytes remain until authorized
retention/deletion; a known receipt can retry analysis without re-upload.
No network call is performed inside a long-lived DB transaction.

Apply must preserve approved profile/narrative/authorization records. For the
existing registration parsers, initially supported fields are **identity
Name/Acronym only**; all other extracted values/counts remain visible
unmapped proposals and tasks. Do not advertise full SSP/control/POA&M import
from these handlers. The separate controls-sheet parser can later participate
only through its existing review/provenance owners and human approval gates;
calling its direct overwrite is not an implementation of this apply contract.
An import review never approves an SSP section or narrative version.

For small identity apply, use one transaction for target/version, session
apply receipt, source-to-target session binding and audit. Repeated identical
apply returns the same historical write result plus a fresh currently
authorized projection; new edits/source/corrections invalidate preview and return 409
`STALE_IMPORT_REVIEW`. Async analysis status remains independent of receipt
acceptance and application. Bulk legacy handlers stay compatible but are not
used by the new setup flow; worker tenant/replay fixes must cover their callers
too before they are represented as safe resumable imports.

`expectedSystemRevision` is an opaque SHA-256 fingerprint of the current
canonical target identity fields and their setup unconfirmed markers, not
merely SetupRevision: legacy system PUT/MCP writers may change fields without
updating setup navigation. Recompute it inside the apply transaction and use
optimistic target concurrency/conditional update so a concurrent ordinary
edit cannot be overwritten after the check. `previewHash` binds that
fingerprint, source hash/review revision, field decisions and target ID.

For the new scoped path, original bytes and an applied receipt are immutable.
Once applied, corrections are rejected with `SOURCE_ALREADY_APPLIED`; an
explicit new receipt is required for another proposal version in this
increment. Do not replace `ApplyReceiptJson` and lose the former before/after
decision. Any admitted pre-apply correction increments
ReviewRevision and invalidates the previous preview. Audit captures each
correction/review/apply with source/target revision and actor.

**Opaque-ID provenance compatibility:** the legacy generic
`WizardArtifactDependency.DependentId` is Guid
(`C/Models/Onboarding/WizardArtifactDependency.cs:33-35`), and
`SspPdfImportSession.CreatedSystemId` is Guid?.
`EmassCommitJobHandler.cs:117-122` uses Guid.TryParse/Guid.Empty when making a
legacy link; `SspPdfImportService.cs:213-232` similarly converts its newly
generated ID. These are not suitable opaque-system-ID contracts. For the
new one-source/one-selected-system path, the session's **string TargetSystemId**
and immutable ApplyReceiptJson are the canonical dependency/provenance
binding. Do not coerce it into these legacy Guid fields or create Guid.Empty
dependency links. Keep old bulk-created UUID links readable unchanged;
inventory/task projections read the new bound session directly and expose a
typed `systemId:string`, not a fabricated UUID dependentId. This uses the
already-proposed session field rather than adding a parallel dependency store.

### 7.4 Error contract

New workspace routes use `{data:...}` success and
`{error:{code,message,fieldErrors?,currentRevision?,retryable}}` failure.
No raw exception, file content, identity token or CUI appears in telemetry.

| HTTP | Codes / behavior |
|---|---|
| 400 | `INVALID_SETUP`, `INVALID_SOURCE_KIND`, `UNSUPPORTED_IMPORT_FIELD`; retain submitted values. |
| 401 | Authentication required; preserve local unsaved draft, reauthorize on return. |
| 403 | `WORKSPACE_OPERATION_NOT_AUTHORIZED`; read-only/appropriate handoff, no retry under forged persona. |
| 404 | `SYSTEM_NOT_FOUND`, `SOURCE_NOT_FOUND`; opaque for inaccessible tenant/record. |
| 409 | `STALE_SETUP`, `SETUP_REQUEST_CONFLICT`, `STALE_IMPORT_REVIEW`, `DRAFT_HAS_DEPENDENCIES`, `SOURCE_REQUEST_CONFLICT`, `SOURCE_ALREADY_APPLIED`; explicit rereview/reconciliation. |
| 413 / 415 | Existing configured upload limits / unsupported format; do not silently change source kind. |
| 428 | `SETUP_REVISION_REQUIRED`. |
| 503 | `SETUP_SAVE_UNAVAILABLE`, `SOURCE_RECEIPT_UNCERTAIN`, `MONITORING_UNAVAILABLE`; never a success banner. Retain request key for possible committed outcome. |

Old onboarding envelopes, job statuses and MCP envelopes remain unchanged;
adapters normalize them for presentation, not by rewriting old wire contracts.

## 8. Permissions and non-HTTP callers

Source-confirmed policies:

- `SystemWorkspaceOperationAuthorization.cs:22-108`: current workspace
  create permission, per-system operation permissions, target-role whitelist;
  non-workspace compatibility differs.
- `SystemWorkspaceAccessService.cs:15-75` resolves membership, Person,
  system override/inherited/org/legacy assignments; `SystemWorkspaceAccessPolicy`
  `:111-160` separates management, authoring, review, evidence, assessments
  and exclusive AO authority. A creator/contact is not a role.
- `TenantResolutionMiddleware.cs:289-313` pushes ambient tenant and verifies
  scoped system visibility; `ComplianceAuthorizationMiddleware.cs:270-294`
  denies scoped REST writes without explicit workspace-authorized endpoint
  metadata. Several legacy categorization/privacy/general wizard writes above
  do not have that metadata. Their authenticated route group alone does **not**
  prove ordinary organization-workspace operability.
- `OnboardingAdministratorPolicy.cs:49-111` requires persisted org admin in a
  workspace, but legacy transport has a first-admin bootstrap window using
  tid/oid. Import handlers still extract raw tid/oid. New system source routes
  use workspace tenant/Person and selected-system authorization instead;
  never weaken the admin API to make a system picker work.

| Proposed operation | Required permission; never implied |
|---|---|
| First draft | Existing `CanCreateSystem` in active org; no creator enrollment or system role grant. |
| Read/resume/tasks/source metadata | Current `CanRead` for exact system; return only permitted source/config metadata. |
| Draft facts, contact, defer intent, confirm/discard | `CanManageSystem`; contact exists in exact tenant, selection not grant. |
| System role changes | Existing `AssignSystemRole` + target matrix + SoD; still separate explicit Team operation. |
| Source upload/analysis | `CanManageSystem` and deployment upload/handling limits; no publish/review permission inherited from receipt. |
| Source identity apply | `CanManageSystem`; approved downstream material is never overwritten. Narrative/evidence operations use their existing independent permissions if later supported. |
| Hosting associate | Existing mission relationship policy and exact eligible allocation revision. No raw-ID entitlement, adoption or coverage decision. |
| Capability adoption/duties | Existing responsibility access checks, including canonical ISSM/ISSO policy; setup contact/management alone insufficient. |
| Connection/readiness/collection | Preserve existing assessment writer policy; use explicit workspace operation authorization for new wrappers, and independent scope/collection admission. No health inference. |
| Document review/export/authorization | Existing action-specific workflow checks; SetupCompletedAt and objective never bypass them. |

**MCP/service compatibility inventory:** `RegisterSystemTool` →
`IRmfLifecycleService` (`RmfRegistrationTools.cs:17-105`) remains
`compliance_register_system`, wrapped by `ComplianceMcpTools.cs:1571`.
List/get/delete/advance/boundary/role tools in that same source, categorization/
baseline/privacy tools, and their existing services remain supported. No new
setup tool is necessary for this UI increment. If optional setup arguments
are later added, they must be additive and call the same domain service,
not a second state store; existing callers must not start inheriting discard
or completion behavior.

`watch_enable_monitoring`, disable/configure/status/rules/alerts/evidence/task
tools (`ComplianceWatchTools.cs`) call existing Watch services; ConMon plan,
report, change, expiration and reauthorization tools (`ConMonTools.cs`) call
`IConMonService`. Retain their scope limitations visibly until the canonical
scope fix covers both UI and MCP. `compliance_import_emass` is the separate
control import described above. No direct wizard import-service or TodoService
MCP caller was found in the production callsite search; do not invent one.
VS Code/M365/Web Chat continue through those shared MCP surfaces, not a new
onboarding transport.

## 9. Document-output dependencies: exact claim boundary

| Path inspected | Present behavior | Required before source→document completion claim |
|---|---|---|
| `SspService.cs:500-558,699-750` | Reads system identity, categorization, baseline, active legacy role rows, approved narrative versions, boundaries/inventory/components and SSP sections. Grounding violations are added as warnings; this method returns a document, not an unconditional exception. | Reuse identity/mission purpose from canonical setup record; consume reviewed field provenance; resolve effective roles rather than assuming browser team display is sufficient. Source receipts alone are not included reviewed content. |
| `SspExportService.cs:516-552` | DOCX/PDF call GenerateSsp for control count, then render via `DocumentTemplateService`. This code does not enforce the collected grounding warnings at that point. | Verify each actual output format and export-purpose/readiness enforcement; a successful Markdown preview is not proof Word/PDF honors the same reviewed source. |
| `DocumentTemplateService.cs:565-665` | Reads actual system, then first `OrganizationContext`, populates type/criticality/hosting, categorization/baseline and implementations. Its “RegisteredSystem has no TenantId” comment is stale; the model does. | Explicit tenant-specific organization selection in worker context and source-version binding; no use of storage defaults marked unconfirmed. Document metadata/provenance tests must cover two tenants. |
| `WizardArtifactDependencyService`, import/export rerender handlers (§4) | Mutable dependency tag plus stale clearing, not a rendered-byte comparison or reimport. | Do not resolve document-review task until a new reviewed artifact/result actually exists. Preserve old exports and human approval. |
| `EmassExportService.cs:173-210,533-631`, existing eMASS workflow | Controls-sheet import and export/round-trip are separate from registration receipts. | Package purpose and receiving-profile owners govern required identifiers, approved profile/narratives and readiness. Export != submission/acceptance/AO decision. |
| `EmassExportReadinessService.cs:15-88` | Current readiness blocks missing DITPR/eMASS IDs or information types, but only advises on absence of any approved SSP section and unscheduled POA&M items. | Do not require fabricated identifiers to create an initial draft, and do not reinterpret this readiness result as complete reviewed-package readiness. Purpose-specific receiving-profile gates remain owned by the export/readiness work. |

Minimum `s-ready` document handoff shows real system name, tenant, selected
source receipt(s)/hash(es), pending review, current approved document/version
if present and a **preparation preview** link. For initial and provider-backed
synthetic systems, later tests must inspect actual generated bytes/structured
OSCAL, not just DOM labels. The same identity/contact/scope/version must be
traceable; missing review/metadata stays an explicit gap. This document does
not claim that all approved-profile/export/ConMon audit dependencies are done.

## 10. Migration and compatibility contract

1. **Expand first:** add nullable draft/session fields, revisions/defaults and
   filtered unique indexes through the existing schema-additions mechanism;
   keep EF mappings/snapshot and SQLite/SQL Server scripts aligned. Follow
   `WorkspaceOperationsSchemaAdditions.cs:17-37,73-88` execution-strategy
   pattern; do not assume `EnsureCreated` upgrades existing databases.
2. Deploy readers that recognize setup metadata, unconfirmed required fields
   and bound sessions **before** enabling new draft creation. Keep legacy
   portfolio behavior unless caller opts into `includeSetupDrafts=true`;
   a new saved-draft list is explicit. Never use `IsActive=false` as “saved
   draft”: that remains deletion. New projection clients show an honest draft
   badge; legacy data receive no false completion badge.
3. Existing systems: SetupDraftJson null, revision 0, completion null → `legacy`,
   not incomplete/not authorized. No inference from CurrentRmfStep, any role,
   old local completedSteps, import status or old onboarding Completed.
4. Existing sessions: new target/key/review fields null; keep original IDs,
   hashes, batches/jobs/errors and source/dependent links. Quarantine ambiguous
   legacy tenant ownership for explicit review; do not match names across
   tenants or invent ownership during backfill.
5. Old OnboardingStepCompletions, skip records, TenantOnboardingStates, roles,
   grants, source files, templates/seeds, phase debt and exports remain intact.
   New task projection deduplicates equivalent work; it does not rewrite
   legacy force-advance history or silently mark prerequisites resolved.
6. Replay migration twice against populated SQLite and retry-enabled SQL
   Server; compare system/session/assignment/dependency/export IDs, counts,
   byte hashes, approval pointers and old/new reads. Explicit worker tenant
   stamping is required before replaying pending legacy import jobs.
7. Rollback disables new write entry points, preserves new columns/data and
   prevents old import workers from consuming new bound-session job payloads.
   Read-only old UI may return to compatible destinations; do not drop
   drafts/receipts or restore auto-discard/auto-approval semantics. Test old
   readers against expanded schema. Destructive schema downgrade is excluded.
8. Only after local review and migration tests retire duplicate optional
   modal orchestration/admin import state machines. Preserve `/onboarding`,
   `/systems/new`, saved-system routes, admin imported-document entry and
   exact old API/MCP envelopes through explicit compatibility adapters.

## 11. Test map and seven-screen acceptance

These are **planned additions/regressions**, not test results. Existing suite
files were inspected/located; no tests ran in this assignment. A new selector
below means “add this test”, not “already covered”.

| Mock screen | Production state / canonical data | Extend existing suites; mandatory new assertions |
|---|---|---|
| `s-details` | Adapted IntakeWizard new/existing setup; canonical system name/Description + objective/authorized tenant | `D/__tests__/hooks/useIntakeWizard.test.ts` (currently expects Skip to count completed at :83), `.../steps/SystemRegistration.test.tsx`, `pages/PortfolioDashboard.openWizard.test.tsx`, `pages/SystemsNewRoute.test.tsx`, `e2e/tests/03-systems.spec.ts`: same ID after Back/reload, three objectives, no default ATO, failed/uncertain save retained, no duplicate POST. |
| `s-team` | Existing effective-role projection plus preparation contact | `D/__tests__/components/roles/MissionOwnerWorkflows.test.tsx`; `T/Ato.Copilot.Tests.Unit/Tenancy/SystemWorkspaceAccessServiceTests.cs`; `T/...Integration/Tenancy/SystemWorkspaceAccessTests.cs`: source/scope, explicit grants only, missing roles as tasks, revoked person/contact, no member/owner self-elevation. |
| `s-sources` | Existing bound mission receipt, independent analysis/review/apply states | `T/...Unit/Onboarding/EmassImportServiceTests.cs`, `SspPdfImportServiceTests.cs`; `T/...Integration/Onboarding/EmassImportEndpointsTests.cs`, `SspPdfImportEndpointsTests.cs`, `EmassImportDependencyTests.cs`: confirmed receipt without commit, exact current wire shapes, uncertainty/reload, correction failure stops apply, same-key mismatch, stale preview and no duplicate system. Add component coverage for both existing duplicate admin entry points. |
| `s-hosting` | Existing provider relationships, organization-managed or explicit deferred | `D/__tests__/provider-relationships/MissionAssociationWizard.test.tsx` including hosting-only/no allocation/503/revision cases; `e2e/tests/mission-association-mock.spec.ts`: exact allocation, separate capability/duty action, organization-only usable, unsupported SaaS action honest, no automatic coverage. |
| `s-connect` | Assessment configuration + access check + independent scope/collection/evaluation | `D/__tests__/pages/AssessmentEnvironment.test.tsx`, `EnvironmentProfile.test.tsx`; `T/...Unit/Services/AssessmentEnvironmentServiceTests.cs`, `SystemSubscriptionResolverTests.cs`, `ConMonAlertPipelineTests.cs`; `T/...Integration/Compliance/AssessmentEnvironmentAdmissionTests.cs`, `AssessmentEnvironmentRelationalTests.cs`: no collector on save, revoked registrations, access != collection, unsupported scope, two systems/one subscription and excluded resources. |
| `s-review` | Fresh saved revision/facts/open tasks; explicit confirmation | Existing hook and permissions suites plus proposed `SystemSetupContractTests`: stale review 409; no import/grant/phase advance/PIA approval; failed confirm stays visible; task owner/status/destination/contribution; setup contact not role. |
| `s-ready` | Same system and TodoService queue, real document/monitoring handoffs | `D/__tests__/components/RemainingDomainPermissions.test.tsx:72-134`, routing `SystemCapabilityShell.test.tsx`, `SystemCapabilityApplicationRoutes.test.tsx`; `T/...Unit/Services/SspServiceSectionTests.cs`, `SspExportServiceTests.cs`, `EmassExportReadinessServiceTests.cs`, `Tools/EmassExportToolTests.cs`; `e2e/tests/14-documents.spec.ts`, `22-emass-workflow.spec.ts`: opening system never DELETEs, persisted follow-up across reload, generated bytes/provenance, no submission/ATO success claim. |

Further backend tests required:

- Add focused TodoService preparation-projection unit tests (no existing
  `TodoService`-named suite found) rather than claiming the current UI tests
  prove durable task semantics.
- Extend `OnboardingStateServiceTests`, `WizardArtifactDependencyServiceTests`,
  `WizardArtifactInventoryServiceTests`, `WizardEnvelopeContractTests` and
  `WizardReentrancyTests` for old state/envelopes and no false cascade success.
- Add job restart/lease/crash-after-commit tests to existing wizard-job suites
  or focused new test files in the same projects. Assert old and new job
  envelopes cannot cross tenant, job cannot apply after permission revocation,
  and partial failures retain truthful per-entry results.
- `EmassImportEndpointsTests.cs:47-96` replaces the administrator policy with
  `RequireAssertion(_ => true)`; `EmassImportDependencyTests.cs:27-115` uses
  InMemory without tenant middleware. These tests establish shape/provenance
  assertions, **not** production authorization/relational concurrency.
  Add negative cases through real auth + tenant-resolution + workspace-
  operation middleware in `TenantScopedEndpointHttpPipelineTests`,
  `SystemWorkspaceAccessTests`, `McpToolTenantScopeTests`.
- Extend `GetSystems_WithOpaqueIdentifiers_PreservesTenantIsolation` coverage
  to setup save/resume, selected-source association/apply, task links and
  provenance. Use non-UUID system keys within the existing 36-character
  mapping for cross-provider cases and preserve the existing 38-character
  SQLite fixture as a legacy-length compatibility case. Replay tests must revoke the actor or change effective permissions
  after the original success and verify denial/fresh redacted projection,
  never stale permissions or destinations from the stored receipt.
- Add SQLite and SQL Server schema/replay tests in the existing Data/Tenancy
  test projects for all new filtered indexes and concurrency tokens. SQL Server
  must use retry strategy; InMemory is not a substitute.

Suggested focused commands **after implementation approval**, from repo root:

```bash
dotnet test tests/Ato.Copilot.Tests.Unit --filter \
  'FullyQualifiedName~Onboarding|FullyQualifiedName~AssessmentEnvironmentServiceTests|FullyQualifiedName~SystemSubscriptionResolverTests|FullyQualifiedName~SystemWorkspaceAccessServiceTests|FullyQualifiedName~SspExportServiceTests'
dotnet test tests/Ato.Copilot.Tests.Integration --filter \
  'FullyQualifiedName~Onboarding|FullyQualifiedName~SystemWorkspaceAccessTests|FullyQualifiedName~TenantScopedEndpointHttpPipelineTests|FullyQualifiedName~AssessmentEnvironment'
cd src/Ato.Copilot.Dashboard
npm test -- src/__tests__/hooks/useIntakeWizard.test.ts \
  src/__tests__/components/wizard/SystemSetupJourney.test.tsx \
  src/__tests__/components/wizard/CompletionSafety.test.tsx \
  src/__tests__/features/onboarding/SystemSourceFlow.test.tsx \
  src/__tests__/pages/PortfolioDashboard.openWizard.test.tsx \
  src/__tests__/components/RemainingDomainPermissions.test.tsx \
  src/__tests__/provider-relationships/MissionAssociationWizard.test.tsx \
  src/__tests__/pages/AssessmentEnvironment.test.tsx
npx tsc --noEmit
```

Add a focused `e2e/tests/system-onboarding-consolidation.spec.ts` to the existing
Playwright project, with all seven screen IDs, keyboard/focus/error/recovery
and dirty navigation assertions. Compare each at **1440px**, **390px** and an
intermediate width using deterministic synthetic fixtures. Screenshots
supplement behavior and generated-document tests; fixture routes do not prove
production API permission. No new tooling dependency is required.

Manual local review must demonstrate: initial blank-source draft, reviewed
existing-source continuation, maintain objective without fabricated decision,
organization-managed and provider-backed hosting, deferred monitoring, loss/
recovery of create/upload responses, actor revocation, save/reload and
non-destructive ready handoff. Show actual persisted facts and remaining
document gaps. The user must be allowed to test these before any screen is
called accepted.

## 12. Phase 0 conclusion

The held system trace is closed at the **source/contract** level:
eight intake steps and their side effects, both cancellation paths, seven
general onboarding steps, both duplicate admin import clients, import
services/jobs/provenance, computed versus durable task state, selected hosting,
assessment/monitoring scope, document consumers and MCP alternatives are
identified above. The replacement direction is explicit and additive.

Implementation is gated on review of this contract, then failing reproductions
for the named source-confirmed failure paths. Known delivery dependencies are
specific: workspace-authorized wrappers, import worker tenant/replay claims,
typed receipt adapters, scope separation (#982/#983), real rerender/reimport
results, and reviewed output-format parity. None is represented as delivered
or healthy merely because a setup form can save.
