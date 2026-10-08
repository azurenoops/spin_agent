# Guided setup and saved work

> Implementation branch: `078-onboarding-consolidation`.
> Supported onboarding implementation is available locally for review. This
> guide does not claim deployment, user acceptance, live directory/cloud
> connectivity or eMASS import acceptance.

## Final local verification (September 30, 2026)

The implementation follows all 22 onboarding mock states, using canonical
records/services and the existing workspace boundaries. Provider, organization,
tenant activation and system setup remain separate workflows.

| Check | Actual result |
|---|---|
| Solution build | Passed; repository warnings remain |
| Full backend unit suite | 7,254 passed, no failures or skips |
| Affected onboarding/import/document integration suite | 84 passed, no failures or skips, including both fixtures corrected after the broad run |
| Required SQL Server retained-data/repeated-upgrade tests | 3 passed, no skips; provider, organization/tenant and system/source data |
| Coordinated real-SPA browser scenarios | 29 passed at desktop/mobile and selected intermediate widths; synthetic APIs |
| Dashboard strict types and production build | Passed; disclosed build warnings remain |
| Full Dashboard suite | 2,077 passed; six unchanged ApplicationShell tests fail because their axios mock omits `isAxiosError` |
| Full integration run | 1,480 passed, two failed, 40 skipped; both failures were corrected and pass in the final 84-test affected rerun. The full 11-minute lane was not rerun afterward |
| Lint | Could not run: the existing script references ESLint but the project does not install it |

The broad-run fixture corrections preserve the assertions and tighten their
setup: a system and organization context now share the same explicit tenant,
and resumed tenant onboarding uses a real local Person/membership/Administrator.
Production tenant filters and permissions were not relaxed. Broad RLS/Nessus
fixture skips remain unverified; the three new SQL Server tests were separately
run with `ATO_REQUIRE_DOCKER_TESTS=1`.

All seven findings from the bounded correctness review were fixed and rechecked.
The shared setup frame/home/readers have measured 100% scoped coverage;
organization/tenant scope is below the plan's full modified-path target
(99.86% lines, 90.19% branches). Whole-feature coverage and local user acceptance
remain release gates, not claimed successes.

Supported mission imports retain small XLSX/PDF originals and explicitly review
supported identity fields. They are not a complete automatic SSP/eMASS package
import. Tests inspect actual generated DOCX identity, complete purpose text,
technical fields and every reviewed source hash. Monitoring configuration,
permission, scope, collection and evaluation remain distinct; unsupported or
unavailable collection is not represented as healthy or as cATO.

No commit, push, issue write, release deployment or live business-data mutation
was performed. Existing unrelated working-tree changes were preserved.

Guided setup prepares a usable workspace and identifies the work still needed
for a documented system. Completing setup does not approve documents, publish
provider releases, accept customer duties or make an authorization decision.

## Approved provider first-login implementation (October 8, 2026)

This section documents the approved **next implementation** and does not replace
the verified September 30 results below. The first-login changes are not yet
claimed implemented, tested, accepted, deployed, or release-ready by this guide.

The current UI prototype at
`src/Ato.Copilot.Dashboard/src/features/csp-onboarding/prototype` is onboarding
only. Reviewers should exercise its entry/status scenarios, six setup stages,
save/resume and failure/receipt recovery through the final onboarding confirmation.
The prototype intentionally stops there. It does not demonstrate My Offerings,
offering overview, readiness, evidence, assessment, decision, or other provider
workspace screens. Production may route an authorized user to those existing
destinations only when the user activates the explicit **Open provider
workspace** link on the completion screen. The completion screen retains the
onboarding summary; it must not auto-redirect, preload, or embed the provider
workspace. Destination behavior is reviewed in its owning workflow.

### Approved operating assumptions

- A platform operator authorizes the initial provider setup administrator.
  Nobody receives authority because they logged in first.
- The implementation includes durable hashed, expiring, single-use invitation
  tokens and invitation create/read/accept/revoke APIs. No invitation delivery
  channel is claimed; an API-created token is not proof of email or notification.
- Provider screens display **SCA**, while the canonical persisted role remains
  **Assessor**.
- An offering has one active primary service portfolio at a time. Prior
  memberships remain historical records.
- A provider AO may record a previously issued external decision with exact
  provenance and assigned scope. SPIN Agent does not issue the provider decision.
- Follow-up reviewers and due dates are optional unless a later approved policy
  requires them for a named work type. Acceptance criteria and an accountable
  principal or explicit Unassigned state are still required.

### Manual acceptance to perform after implementation

1. As a platform operator, grant setup authority to a named provider identity;
   verify a different first-login identity cannot start registration.
2. Complete and resume the six-stage setup, including a deferred offering or
   source. Confirm the durable work item retains acceptance criteria and may have
   no reviewer or due date.
3. Create an invitation through the API, confirm storage does not retain the raw
   token, accept it once as the intended identity, and verify replay, mismatch,
   expiry, and revocation fail. Do not report that any message was delivered.
4. Verify `SCA` presentation resolves to canonical `Assessor` permissions and
   does not add AO or administrator rights.
5. Race two primary-portfolio assignments and verify only one is active while
   both histories remain readable.
6. Declare existing authorization facts and receive/analyze a source. Confirm
   proposed facts remain unverified until review and that only an assigned
   provider AO can record the already-issued external decision.
7. Complete onboarding in every prototype scenario at desktop/mobile and by
   keyboard. Confirm each mock branch stops at completion/status, retains the
   onboarding summary, and does not navigate until the user activates **Open
   provider workspace**. Test the authorized destination separately through the
   production router and confirm no workspace is embedded in onboarding.

Acceptance must record backend persistence/API results, Dashboard/browser
results, local manual review, deployment, and any future delivery-channel test
separately. Setup completion still does not establish document review,
assessment, authorization, customer coverage, monitoring health, cATO readiness,
eMASS submission, or an AO decision.

## Enter and resume

In an ordinary authorized workspace, choose **Guided setup** in the workspace
header. This is an explicit action; established users are not forced to repeat
bootstrap. The available paths depend on the current server permissions:

- **Provider workspace:** provider details, access, optional first offering,
  optional sources, review and provider handoff.
- **Organization:** create or reuse the exact organization, review contact and
  administrator separately, and resume any unfinished enrollment.
- **Mission system:** draft identity and objective, effective team, optional
  sources, hosting, monitoring, review and system work queue.

**View saved setup** opens named server records. Continue uses the existing
draft or operation rather than creating another record. Lists are paginated.
An unavailable read displays an error and retry, not a successful empty list.
An authorization failure clears retained private records from this view.

Canonical entry routes, within the existing workspace URL:

| Entry | Relative route |
|---|---|
| Choose a setup path | `/setup` |
| Resume saved work | `/setup/resume` |
| Provider setup | `/onboarding/csp` |
| Organization setup | `/organizations/new` |
| New system | `/systems/new` |
| Existing system setup | `/systems/{systemId}/setup` |

Ordinary organization routes use
`/workspaces/organizations/{tenantId}`. Provider routes use `/workspaces/csp`.
Do not substitute provider/support access for ordinary organization membership.
Identifiers in saved links are opaque identifiers, not display names.

## Saved does not mean applied

**Save & finish later** must wait for a server-confirmed save. Failed saves
retain entered fields and show unsaved status. Moving between steps does not
silently grant roles, apply an import, publish a release or approve content.

Source receipt and analysis are independent:

- Selected files are not yet uploaded.
- A confirmed receipt may continue analysis after leaving setup.
- An uncertain response must reconcile the same request and file manifest.
- A browser restart does not retain browser File objects; reselect the same
  files if reconciliation determines they still need to be sent.
- Unreadable/excluded content remains visible for review, not counted as
  complete coverage.

The deployment's approved data-handling policy is independent of an offering's
impact label. Missing handling authority must be visible and cannot be elevated
by a setup form.

## Local review without business-data writes

The existing Playwright workspace fixture supplies deterministic synthetic
identities and API responses to the real SPA. It does not contact Azure, Entra
or eMASS and does not establish production authorization or persistence.

Use an existing isolated Dashboard preview, or start one from the repository:

```bash
cd src/Ato.Copilot.Dashboard
npm run dev -- --host 127.0.0.1 --port 5179 --strictPort
```

The implementation session started this preview already; do not launch a second
process on the same port if it is still running. Do not restart shared backend,
database or Docker services for these UI checks.

In another terminal, run the entry/resume browser scenarios:

```bash
cd src/Ato.Copilot.Dashboard
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5179 \
  npm run test:e2e -- e2e/tests/onboarding-entry.spec.ts \
  e2e/tests/onboarding-provider-consolidation.spec.ts \
  e2e/tests/onboarding-organization-consolidation.spec.ts \
  e2e/tests/system-onboarding-consolidation.spec.ts \
  --project=chromium --reporter=list --output=test-results/onboarding-final
```

To review the same synthetic flows interactively:

```bash
cd src/Ato.Copilot.Dashboard
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5179 \
  npm run test:e2e:ui -- e2e/tests/onboarding-entry.spec.ts
```

Select a test and inspect each action in the Playwright UI. The synthetic
`org-a`, `system-a` and provider names exist only in fixture-intercepted tests;
do not expect those IDs to exist in an ordinary browser session.

### Entry/resume fixture walkthrough

| Role / fixture | Route | Actions and expected outcome |
|---|---|---|
| Provider-only synthetic actor | `/workspaces/csp/setup` | Provider and organization cards are available; no customer-system creation card. Open saved setup, continue the named provider record, reload and use Back. All requests in this entry test are reads. |
| Organization actor with server `canCreateSystem=true` | `/workspaces/organizations/org-a/setup` | System setup appears only after the permission response. No provider API is queried. The link retains organization scope. |
| Organization actor with unavailable saved-record service | `/workspaces/organizations/org-a/setup/resume` | Error and retry are visible; there is no “No saved setup records” success state and no guessed create permission. |
| Mobile at 390px | Same fixture routes | Cards and guidance stack without document overflow; the numbered sidebar is hidden as in the supplied mock. Footer actions remain reachable. |

Desktop screenshots are captured at 1440px. Start and resume images are emitted
into the corresponding Playwright test-output directory. They are verification
evidence, not production data or proof of backend authorization.

## Current verification checkpoint

- The shared frame, entry/resume views and canonical-reader tests pass.
- The 31 shared UI tests pass with 100% lines, statements, branches and functions
  for SetupFrame, SetupHome and SetupHomeRoute, including authorization loss and
  transient-read recovery. This is scoped coverage, not whole-feature coverage.
- Four entry/resume Chromium scenarios pass, covering desktop/mobile layout,
  scoped navigation, server create permission and unavailable data.
- The required agent-context generator repair has a failing-first regression;
  its focused baseline passed 87 tests and strict Dashboard type checking.
- Domain persistence, migration, receipt/import, review-output and full journey
  validation are separate gates being completed by the implementation.

For current detailed results and remaining gates, see
[implementation tasks](../../specs/078-role-aware-workspaces/tasks.md) and the
[domain contracts](../../specs/078-role-aware-workspaces/contracts/onboarding-consolidation.md).

Do not close the linked issues from screenshots or form-save success alone.
Record **implemented**, **automated checks passed**, **locally tested** and
**user accepted** separately, and keep unperformed live integration checks
explicit.

## Organization and tenant validation

This section records the organization/tenant implementation checkpoint on
September 30, 2026. It distinguishes real local HTTP/SQLite tests from synthetic
browser evidence. **Real Entra/guest sign-in, Graph consent, SQL Server execution,
deployment readiness, and user acceptance have not been verified by these runs.**

### Current access versus the requested enrollment

The source contract is
[organization and tenant consolidation](../../specs/078-role-aware-workspaces/contracts/onboarding-organization-consolidation.md).
The current implementation preserves these boundaries:

| Condition | What the user should see | Permitted action |
|---|---|---|
| Exact bound local Person, same directory/object membership active, scoped Administrator active | Organization access and that requested enrollment are complete, even if an earlier response was lost. | Read/refresh reconciles live facts. No additional grant is needed. |
| A valid current Administrator exists, but an old attempt is unbound | Current administrator availability is separate from historical incomplete work. | Explicitly reuse the existing administrator without creating a new operation, Person, membership, or role. |
| A valid current Administrator exists, but the retained request identifies another person/identity | Current access is available; the different requested enrollment remains pending/conflicting. | Use the supported membership/role-management workflow. Initial administrator enrollment cannot replace the existing Administrator. |
| The requested Person/membership saved but the Administrator stage failed | Persisted Person and membership remain complete; role enrollment remains incomplete. | Explicit retry of the same operation runs only missing stages. Refresh alone never grants. |
| Administrator role exists but its matching membership was revoked | Role history remains, but ordinary access is incomplete. | Explicit authorized repair, not success inferred from the role label or contact email. |
| The current actor is only a provider administrator | Provider handoff, with no inferred customer-system access. | An ordinary customer workspace link is available only if that actor independently has a valid membership/local Person. |

Source locations:

- [Live operation projection](../../src/Ato.Copilot.Core/Services/Workspaces/WorkspaceOperationsService.Provisioning.cs)
  derives Person/membership/Administrator completion from stored identities and
  current rows, not old stage flags.
- [Live summary and draft service](../../src/Ato.Copilot.Core/Services/Workspaces/WorkspaceOperationsService.OrganizationOnboarding.cs)
  separates current administrator records from the exact requested operation.
  Administrator pages default to 25, cap at 100, and use database paging;
  availability and requested-identity checks are not limited to the first page.
- [Provisioning endpoint](../../src/Ato.Copilot.Mcp/Endpoints/Workspaces/WorkspaceOperationsEndpoints.cs)
  binds intent, then calls existing membership and initial-admin services for
  missing stages. New clients send the operation revision; stale submissions
  conflict rather than changing a bound identity.
- [Membership service](../../src/Ato.Copilot.Mcp/Services/Tenancy/OrganizationMembershipService.cs)
  reuses the exact existing grant/admin, rejects different-person reassociation,
  and protects the last accessible Administrator. No second identity engine was
  introduced. Directory search/manual entry never proves real guest sign-in.

### User-run local walkthrough

Use the already-running isolated Dashboard preview from the earlier section.
Do not restart shared containers or change real memberships to exercise this
guide. The browser fixtures intercept API calls and use synthetic identity data.

1. Open the organization browser test in Playwright UI. At **Organization
   details**, enter the name and contact, then **Save & finish later**. Confirm
   that no organization/grant occurs before confirmation. Reopen the same draft,
   reload, and verify both fields survive.
2. At **Administrator access**, distinguish the primary contact from the
   explicitly selected admin. Open **Directory lookup is unavailable**;
   inspect manual-unverified, existing-authorized, and defer choices. Return
   without losing details. Selecting a directory result does not grant access.
3. Review the exact creation/reuse and enrollment outcome. Defer enrollment and
   confirm once. The ready screen must still show pending membership/admin,
   not a fully authorized system. Reload: the same organization and operation
   must remain, with no second creation or grant.
4. Run the **organization repair** scenario: inspect a named current admin
   beside the incomplete different-identity request. Retry is disabled for that
   conflicting request; refresh is read-only. Run the retained **partial failure**
   scenario to prove same-identity retry preserves completed Person/membership.
5. Run **uncertain draft save**: the server fixture retains the write while
   dropping the response. **Check saved draft** reads the same draft ID before
   retry. Run **forbidden live-access summary**: no guessed success, enrollment,
   or customer workspace handoff is allowed.
6. Run the tenant activation scenario. Applied legal entity/address/CUI/AO/POC/
   subgroup fields hydrate from the server. Edit, navigate away/back, save, and
   reload. Saved draft changes remain distinct from applied fields and do not
   activate the tenant. Desktop numbered navigation and mobile Back/footer
   remain usable. All six domain step commands and final activation are also
   exercised in component and real HTTP tests.

From the Dashboard directory, run the complete organization browser set:

```bash
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5179 \
  npx playwright test \
  e2e/tests/onboarding-organization-consolidation.spec.ts \
  e2e/tests/organization-setup-flow.spec.ts \
  --project=chromium --reporter=list \
  --output=test-results/organization-onboarding-review
```

For interactive review, use the same command with `--ui`. These tests run the
real SPA against `installWorkspaceFixture` plus canonical domain DTO responses;
they do not assert that live authentication/cloud integration works.

### All six mock screenshot pointers

The browser command writes the following images inside each named test's output
subdirectory. Organization states are captured at **1440, 900 and 390 pixels**;
the retained legacy journeys and tenant save/reload run at 1440 and 390.

| Required mock state | Screenshot | Test/output group |
|---|---|---|
| `o-details` | `o-details.png` | `organization mock states and explicit server draft survive reload` |
| `o-admin` | `o-admin.png` | Same group |
| `directory-offline` | `directory-offline.png` | Same group |
| `o-review` | `o-review.png` | Same group |
| `o-ready` | `o-ready.png` | Same group |
| `o-repair` | `o-repair.png` | `organization repair shows current access without granting the different request` |

Additional evidence: `draft-uncertain.png`, `forbidden-access.png`,
`tenant-hydrated-dirty.png`, and `tenant-restored-draft.png`.
The implementation-session captures were preserved outside the shared
Playwright output directory, which other test runs may clear:

```text
/Users/johnspinella/.copilot/session-state/
  0d9f1c62-49c5-4dbe-ad1c-a6051df63df2/files/
    organization-browser-validation/
```

For example, the desktop detail/review/ready images are in
`tests-onboarding-organizat-c1d57-ft-survive-reload-at-1440px-chromium/`;
desktop repair is in
`tests-onboarding-organizat-2adc2-different-request-at-1440px-chromium/`.
Mobile detail/review/ready is in
`tests-onboarding-organizat-06313-aft-survive-reload-at-390px-chromium/`;
mobile repair is in
`tests-onboarding-organizat-84ed8--different-request-at-390px-chromium/`.
These files are local review evidence, not committed product assets.

### Measured automated results and coverage

Latest domain frontend run: **178 tests passed across nine files**. The scoped
V8 coverage measurement includes the four organization editing/transport files
listed below plus every runtime file beneath `TenantWizard`:

| Metric | Measured result |
|---|---:|
| Statements | 94.49% |
| Lines | 94.49% |
| Branches | 84.76% |
| Functions | 82.14% |

This is **aggregate coverage for this explicit scope**, not whole-dashboard,
backend, every modified file, or per-file 80% coverage. For transparency, the
organization-only subset measured 89.55% lines, 83.80% branches and 67.94%
functions; individual AddOrganizationPage branches measured 75.13%.
All tenant step files measured 100% lines/functions; remaining uncovered paths
include some cancellation, error, selection and callback combinations.
Coverage expansion also caught and fixed a review bug where equal fields with
different JSON property ordering incorrectly disabled activation.

Reproduce the measurement without replacing a shared coverage report:

```bash
npm test -- \
  src/__tests__/components/csp-dashboard/OrgsTable.test.tsx \
  src/__tests__/workspaces/TenantOnboardingDraft.test.tsx \
  src/__tests__/workspaces/TenantOnboardingGuard.test.tsx \
  src/__tests__/api/tenantOnboarding.test.ts \
  src/__tests__/workspaces/OrganizationSetupFlow.test.tsx \
  src/features/workspace-operations/OrganizationPages.test.tsx \
  src/__tests__/workspaces/WorkspaceOperations.test.tsx \
  src/__tests__/workspaces/EntraUserPicker.test.tsx \
  src/__tests__/api/organizationSetup.test.ts \
  --coverage \
  --coverage.include='src/features/workspace-operations/{AddOrganizationPage,OrganizationProvisioningPage,OrganizationSetupPresentation,organizationOnboardingApi}.{ts,tsx}' \
  --coverage.include='src/features/onboarding/TenantWizard/**/*.{ts,tsx}' \
  --coverage.reporter=text --coverage.reporter=json-summary \
  --coverage.reportsDirectory=coverage/organization-onboarding \
  --reporter=dot
npx tsc -b --pretty false
```

The measured JSON report is preserved in the same implementation session under
`files/organization-coverage/coverage-summary.json`. The latest browser run passed
**16/16 scenarios**, including positive same-identity enrollment and partial
retry; this is not narrowed to an unavailable banner or a successful form save.

During the granted serialized .NET slot, **25 organization/tenant HTTP tests**,
**11 schema/private-query-guard tests**, and **55 retained workspace
service/contract tests** passed. The combined onboarding selection passed
**46/46** tests with zero skipped. Backend percentage coverage was **not measured**.
Existing repository build warnings were reported, not suppressed.

For a local backend rerun from the repository root, coordinate exclusive use of
the test host with other developers/agents, then run:

```bash
dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  --no-restore \
  --filter 'FullyQualifiedName~OrganizationCreationFlowTests|FullyQualifiedName~TenantOnboardingWizardTests'
dotnet test tests/Ato.Copilot.Tests.Unit/Ato.Copilot.Tests.Unit.csproj \
  --no-restore \
  --filter 'FullyQualifiedName~WorkspaceOperationsSchemaTests|FullyQualifiedName~TenantScopedQueryGuardGlobalReferenceTests|FullyQualifiedName~WorkspaceOperationsServiceTests|FullyQualifiedName~WorkspaceOperationsContractTests'
```

These tests cover local HTTP authorization, SQLite persistence, schema upgrade,
compound identity, immutable replay and existing enrollment behavior. They do
not certify SQL Server transactions or real guest token claims.

Before user acceptance, inspect the screenshots and interactively repeat the
scenarios above. Record defects and acceptance separately. Historical grants,
operations and subgroup IDs remain intact; no production records were deleted
when the duplicate legacy editor and unreferenced UI adapters were retired.

#### Delayed-hydration correction and recheck

A later combined browser run exposed a real lost-step race at 900px:
workspace authentication/navigation context refreshed while the user was on
Review, causing a second read of the same saved draft. Its delayed response
replaced the current review step and edits with the earlier saved Details state.
The hydration effect incorrectly depended on the navigation callback's identity.

A deterministic failing component test now changes the workspace navigation
context after editing/review and releases the stale response. The fix makes
hydration depend on the scalar draft ID; a local draft's URL transition preserves
its editing ownership even after the pending-write flag clears. Actual draft-ID
changes cancel the old read, clear the old record, and reject mismatched response
IDs. Save/confirm/reconcile responses are likewise restricted to the draft that
started them. This is a state-ownership fix, not a timeout increase or test retry.

Recheck results: **180 scoped component/transport tests pass**, strict types
pass, and **all 16 browser scenarios pass**, including 1440/900/390 organization
journeys and tenant save/reload. Screenshot evidence in the preserved session
directory above was refreshed. The same explicit coverage scope now measures
**93.87% statements/lines, 84.54% branches, and 82.14% functions**. These newer
numbers supersede the earlier checkpoint; organization-only function coverage
remains 67.94%, not a per-file 80% assertion.

For manual reproduction, keep a saved organization draft open, edit its name,
advance to Review, and allow ordinary session/workspace refresh activity.
The review step and edits must remain. Then navigate deliberately to a different
saved draft: only that draft's response may hydrate the form. No .NET, live
identity, SQL Server, or deployment validation was performed for this UI fix.

#### Explicit stale-version reconciliation

A 409 draft conflict now retains the local form **and its original revision**.
**Check saved draft** presents the saved server version without making the
stale form writable against a newer revision. The user must choose
**Use saved server version**, which replaces fields, step and revision together.
The page warns to copy any retained edits before that explicit replacement.
A lost-response save can reconcile automatically only when its retained request
identity, expected revision and canonical values match the saved response.
Different server data requires explicit reconciliation even if the original
network failure was ambiguous.

The deterministic stale-tab regression failed before this correction and now
passes; **104 focused organization tests and strict types pass**, and
**17 synthetic browser scenarios pass**, including the new conflict scenario
and all previous widths/recovery paths. Its additional screenshot is
`explicit-conflict-reconciliation.png` under the preserved domain output.
The earlier percentage coverage is a dated measurement before this correction,
not a fresh percentage claim. The separate reused-operation revision regression
is queued for the exclusive .NET slot; no backend fix is claimed from UI tests.

#### Expanded coverage checkpoint

The subsequent coverage-driven pass adds real interaction/error cases for
existing-organization search and explicit admin reuse, directory-selected intent
and clearing, unsupported/mismatched/confirmed draft hydration, deliberate fresh
draft navigation, stale versus exact-request reconciliation, keyless recovery,
failed enrollment start/reload, saving unbound admin edits without grants,
customer navigation only with explicit permission, all tenant field callbacks,
and provider/support/non-admin denial. No production code was changed during
this coverage pass.

Latest measured results: **209 tests pass across nine files**, strict types pass,
and the same explicit instrumentation scope measures:

| Metric | Latest result |
|---|---:|
| Statements / lines | 99.86% |
| Branches | 90.19% |
| Functions | 96.52% |

This **does not meet a 100% target** and is not reported as complete coverage.
The two uncovered statements are generic non-string fallback returns in draft
normalization/comparison; other uncovered branches include defensive cancellation,
repeated-action, optional/legacy-value and malformed-response combinations.
OrganizationProvisioningPage, its shared presentation helpers, the canonical
organization draft API, tenant API/guard and all tenant step files have 100%
statement/line coverage; AddOrganizationPage is 99.75% and TenantWizard index is
99.25%. Branch percentages remain below 100% and are retained in the JSON report.

The current measurement overwrote only the private domain report at
`files/organization-coverage/coverage-summary.json` and
`files/organization-coverage/coverage-final.json` in the implementation session.
It did not clear screenshot directories. The latest **17-scenario browser**
evidence remains in `files/organization-browser-validation/`; its separate
output is not the shared parent `test-results/onboarding-integration`.
Reused-operation revision validation remains pending the serialized .NET slot;
no unrun relational/SQL Server coverage is inferred from the frontend metrics.

The parent has now captured the reused-operation revision test failing before
the fix (confirmation left revision 0). Its correction updates the operation
revision/timestamp and records actor-attributed audit whenever confirmed
unbound intent actually changes. Same-intent replay must not increment again;
stale enrollment must return `STALE_REVISION` before creating a Person or grant.
The strengthened replay/audit/no-grant test is ready for the parent's combined
GREEN run. That run is not yet represented as passing here.

## Provider validation

This provider checkpoint separates **wired local HTTP/SQLite tests** from
**synthetic API fixtures through the real Dashboard**. It does not establish
production handling approval, Entra identity validity, Azure connectivity,
SQL Server runtime acceptance, eMASS submission, or user acceptance.
The detailed source and implementation record is
[provider consolidation](../../specs/078-role-aware-workspaces/contracts/onboarding-provider-consolidation.md).

### The seven provider screen states

The provider journey has six numbered stages and seven screen states.
Receipt recovery is a source-stage state, not another required setup stage.

| Screen | User action and truthful outcome |
|---|---|
| `p-details` — Identify your provider | Enter provider/operator/contact fields. Read the configured handling limit separately. Save partial fields without activating the profile. |
| `p-access` — Confirm provider access | Review the actual authorized caller. Record a contact-only security reviewer or defer. This does not grant roles; provider directory discovery is visibly unavailable in this adapter. |
| `p-offering` — Add your first service offering | Create/select an exact offering or explicitly defer. AWS GovCloud, Microsoft 365 DoD and manual SaaS are descriptive records, not fictional Azure scope. |
| `p-sources` — Add source material | Declare permitted content and register its exact request before uploading. Unknown boundary uses an unassociated receipt, never a placeholder boundary. Sources may instead be explicitly deferred. |
| `p-uncertain` — Check the package receipt | Reconcile the saved request. Browser File objects do not survive restart; reselect the exact files only if bytes must be retried. A network failure is not proof of non-receipt. |
| `p-review` — Review provider setup | Confirm committed profile facts and retained optional work. Acknowledge any unresolved source requests rather than relabeling them “not added.” |
| `p-ready` — Your provider workspace is ready | Open the provider review queue. Profile readiness, source processing, human review and publication remain independent. |

At 390px the numbered sidebar is hidden as in the mock, while the guided header,
Save & finish later and footer actions remain reachable. At 1440px the field
panels and guidance column are visible side by side. These are **seven provider
states**, not forty distinct provider screens. The latest isolated run produced
46 PNG captures across viewport/state/footer and retained-review scenarios;
that capture count is not a count of unique screens.

### Handling policy is operator configuration, not an offering claim

The implementation binds `ProviderHandlingOptions` from
`Deployment:DataHandling`. The ordinary-provider read
`GET /api/csp/onboarding/handling-policy` projects these facts read-only; the
wizard cannot change operator policy.

| Configuration field | Meaning |
|---|---|
| `PolicyId`, `Version` | Operator-controlled policy identity/version; version is bounded to 100 characters. |
| `ApprovalReference`, `ValidUntil` | Required approval-reference/validity inputs. The app does not verify that a supplied reference is a genuine authorization decision. |
| `EnvironmentLabel` | Display description, not proof of approved deployment or connectivity. |
| `AllowedClassifications`, `AllowedMarkings` | Explicit allowed declarations; classifications are Unclassified, CUI or Secret. No permission is inferred from a filename or service name. |
| `SyntheticOnly` | Requires the user's synthetic-data declaration when true. This is not a content-classification detector. |
| `UploadsEnabled`, `AnalysisEnabled` | Separate operator gates. Enabling them alone does not make missing policy identity, approval or validity become Known. |

Missing, invalid or expired required policy facts project Unknown/Expired and
disable new registered upload/analysis. A stored legacy classification floor
and a declared IL6 offering cannot elevate this authority. Partial provider
drafts and explicit source deferral remain available.

For a new installation awaiting an approved policy, a deliberately non-enabling
configuration shape is:

```json
{
  "Deployment": {
    "DataHandling": {
      "PolicyId": null,
      "Version": null,
      "EnvironmentLabel": "Handling policy not configured",
      "ApprovalReference": null,
      "ValidUntil": null,
      "AllowedClassifications": [],
      "AllowedMarkings": [],
      "SyntheticOnly": true,
      "UploadsEnabled": false,
      "AnalysisEnabled": false
    }
  }
}
```

This example is **not authorization to upload**. The environment owner must
supply genuinely approved settings through the deployment configuration process;
no production configuration or container was changed by the validation runs.
Automated positive tests use an isolated synthetic policy, source bytes and
test-only approval reference.

The policy itself remains configuration. A retained registered receipt records
its intent identity, policy version and declared content alongside existing
source hashes/manifests. Original source bytes remain in canonical file storage,
not the browser draft. Processing rechecks current policy before hydration and
checkpoint; revocation leaves retained sources in NeedsAttention rather than
publishing generated output. Existing pre-upgrade flows without a setup draft
or configured policy retain their legacy compatibility; that exception is not
a claim that those deployments have verified handling approval.

### Continued portal intake and source review

After setup, use **Authorizations → Import authorization package**, or the
offering's existing package action. FileFirstImport and exact-context
OfferingIntake share the policy-enforced uploader:

1. Read the trusted policy and obtain explicit content declarations.
2. Register an `ActivePortal` intent with the exact name, ordered file manifest,
   optional offering hint/exact context and a UUID key no longer than 100
   characters.
3. Retain source bytes through the existing receipt service.
4. Verify or reconcile the exact receipt before showing success.

ActivePortal intents require an Active provider but do **not** require or mutate
a setup draft. Setup-associated requests remain bounded separately; the
26th portal intake is not permanently blocked by 25 historical receipts.
Old receipts, request identities and review history remain retained.
The authorized pending-request list is paged. An `uploadIntentId` in the current
URL restores the exact request after a lost response; knowing that ID grants no
access.

An offering hint does not establish an association. Associate an unassociated
receipt later using the exact valid boundary and current revisions. New setup
receipts cannot publish without that explicit association. Canonical source
review, exact approval and publication still run in their existing services;
setup does not create a competing receipt/release engine.

Manual/non-Azure SaaS offerings persist their declared environment/service
metadata with an **empty Azure technical environment list**, no invented
subscription/resource IDs, and no automatic boundary/hosting-scope rows.
Their descriptive availability does not imply implemented cloud discovery or
technical allocation. An offering can be deferred and setup still completed.

### Reproduce the synthetic browser evidence locally

Use the existing responsive preview; do not restart shared backend/database/
container services. Always specify a domain output directory because Playwright
otherwise clears its default output and may remove another runner's captures.

```bash
cd src/Ato.Copilot.Dashboard
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5179 \
  npx playwright test \
  e2e/tests/onboarding-provider-consolidation.spec.ts \
  e2e/tests/package-imports.spec.ts \
  e2e/tests/provider-portal-intake.spec.ts \
  --project=chromium --reporter=list \
  --output=test-results/provider-activeportal-integration
```

The latest run passed **15 scenarios**. It covers all seven provider states at
1440/390, explicit partial save/reload, uncertain receipt recovery without
duplicate bytes, failed saves, forbidden reads, unknown handling, continued
portal intake after 25 historical sources, and retained source review/exact
approval/publication replay/protected-reference behavior.

The following files were verified present when this section was written:

- [Provider details, 1440px](../../src/Ato.Copilot.Dashboard/test-results/provider-activeportal-integration/tests-onboarding-provider--c6553-responsive-layout-at-1440px-chromium/p-details-1440.png)
- [Recovered active-portal source, 390px](../../src/Ato.Copilot.Dashboard/test-results/provider-activeportal-integration/tests-provider-portal-inta-a7828-historical-sources-at-390px-chromium/active-portal-recovered-390.png)

These local ignored test-output files are not published documentation assets and
may need regeneration after test-output cleanup. The scenario sources, not PNG
existence alone, define the repeatable evidence. Earlier capture directories were
erased by default-output cleanup; do not rely on their historical paths.

### Backend results and remaining acceptance

| Validation scope | Latest verified result |
|---|---|
| Provider HTTP/SQLite (`FullyQualifiedName~ProviderSetup`) | **23/23 passed**, including actual stored non-Azure fields, unchanged setup state for portal intake, 26 retained manifests/receipts and portal operation with no setup draft. |
| Canonical package services plus provider relational/schema/replay/compatibility | **68/68 passed**, including concurrent first drafts, exact replay, transaction rollback, old non-null DraftId migration with preserved rows, handling revoke before/during analysis and publication guards. |
| Related provider/package/legacy-entry frontend tests | **401 tests in 41 files passed**; dashboard `tsc --noEmit` passed. |
| Synthetic real-SPA Chromium | **15 scenarios passed**, as above. Not live backend authorization/cloud proof. |

The 26th-intake regression was checked by temporarily restoring the exact former
provider-wide lifetime guard during the exclusive test slot: both test variants
failed 422 versus required 201. The guard was removed for the final passing run.
The initial correction build also exposed a misplaced migration block, which
was fixed before final schema/HTTP GREEN. No assertion weakening, test auth
bypass or warning suppression was used.

Final integration compilation emitted zero warning lines. The shared unit
rebuild emitted 89 warnings outside the new provider files; its EF1002 warnings
referenced workspace schema code, not the provider schema. This is not a
warning-free whole-solution claim.

**Still required:** the user's local manual review, SQL Server runtime
acceptance, and completion of the requested 100% modified-path coverage target.
The earlier scoped V8 measurement was 81.68% lines overall and included
unchanged guard code; it is dated, not the final coverage of the expanded portal
implementation. Do not infer 100% coverage from passing scenario counts.

For manual review, use synthetic sources only: save an incomplete provider,
reopen it, defer offering/sources, inspect the actual current administrator,
upload without a boundary, interrupt/recover the same request, and continue
from the active portal. Inspect retained source names/hashes and unresolved
work, then review association, approval and publication separately. Confirm that
Unknown handling blocks bytes without preventing draft work, and that no
offering label changes the displayed handling policy. Record manual acceptance
separately from these automated results.
