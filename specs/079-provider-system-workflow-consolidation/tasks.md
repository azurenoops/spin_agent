# Tasks: Feature 079

**Inputs**: [spec](spec.md), [plan](plan.md), [screen contract](contracts/screen-route-migration.md)  
**Status**: Application implementation started 2026-09-26; full contract acceptance remains open.  
**Format**: ID / story / dependency / exact affected paths and acceptance.

Tests are mandatory, despite the older template's optional-test wording.

### September 30 SSP reference fidelity

- [x] SSP01 — Inspect supplied DOCX layout and document source mappings/limits.
- [x] SSP02 — Add failing tests for template section order and explicit gaps.
- [x] SSP03 — Implement SSP masthead, cover, front matter and numbered sections;
  preserve complete generated values, provenance and other document types.
- [ ] SSP04 — Verify unit/type/build and desktop/mobile rendering; provide local
  manual acceptance steps. User acceptance and DOCX export fidelity stay open.

SSP verification checkpoint: 23 focused Dashboard tests, `tsc --noEmit`, production
Dashboard build and two Chromium scenarios at 1440/390px passed. Final screenshots
were visually inspected; contents navigation, complete generated values and
mobile overflow checks passed. The first browser run used `localhost`, which
returned an older deployed bundle; the corrected source check uses
`http://127.0.0.1:5173`. No deployed container was changed. Solution build completed
with 144 warnings and zero errors, so the zero-warning gate is not met. Full .NET
tests were interrupted after a CKL 5000-entry performance failure (12.642 seconds
versus 10 seconds), a package-analyzer cancellation assertion failure, and SQL
integration timeouts; no overall test success is claimed.
User local acceptance, full-suite/coverage gates and export fidelity remain open.
No external writes, commits or pushes were performed.
Each behavioral task is red-green-refactor: commit or retain failing-test evidence
before production changes, use AAA, then refactor. Do not mark a task complete
from source inspection alone.

Path prefixes below are repository-relative:

- `Core` = `src/Ato.Copilot.Core`
- `Agents` = `src/Ato.Copilot.Agents`
- `Mcp` = `src/Ato.Copilot.Mcp`
- `UI` = `src/Ato.Copilot.Dashboard`
- `Unit` = `tests/Ato.Copilot.Tests.Unit`
- `Integration` = `tests/Ato.Copilot.Tests.Integration`

## September 29: approved package-readiness experience

### September 30 workspace-header cleanup

- [x] H001 — Move verified context and authorized admin links into AccountMenu;
  remove the standalone blue workspace bar and preserve support/sign-out behavior.
- [x] H002 — Reuse authorized workspace choices in a cancellable dialog;
  preserve drafts/URL on cancel and prevent cross-system carryover on switch.
- [x] H003 — Add scoped narrative-library header icon adjacent to Chat.
- [x] H004 — Verify keyboard/mobile, cancel/draft preservation, scoped navigation,
  request cancellation and initial-login compatibility; deploy local Dashboard.
  TypeScript and 34 focused tests passed; 14 header/theme browser cases passed
  on both source/deployed UI. Two stale audited-support setup scenarios were not
  claimed passing; no support controls were weakened. Dashboard-only
  `workspace-header-20260930` deployed healthy, with backend/data services retained.

### September 30 personal Settings correction

- [x] S001 — Trace Settings controls, consumers, account notification API,
  workspace administration and current accessibility; document dispositions.
- [x] S002 — Implement three expandable personal sections, compact verified
  identity header and authorized workspace administration links.
- [x] S003 — Reuse account notification form with explicit save/error/retry,
  cancellation, no silent local fallback and no reset coupling.
- [x] S004 — Wire retained personal display controls, omit unsupported controls
  and scope reset without changing operational records/preferences.
- [x] S005 — Run focused tests/typecheck and desktop/mobile keyboard verification;
  deploy matching local UI and document limitations/acceptance steps.
  TypeScript, 18 settings/admin tests, 148 display-consumer tests and two browser
  cases per local port passed. Dashboard-only image `personal-settings-20260930`
  deployed healthy; live preferences GET confirmed without mutation. Full-suite
  failures remain documented in `docs/guides/compliance-dashboard.md`.

### Shared Azure environment and CSP allocation follow-through

- [x] I001 — Correct allocation-first contracts and trace independent provider
  scope, subscription, optional linkage and downstream authorization paths.
- [x] I002 — Test and implement independent provider consumption, optional
  many-to-many links, lifecycle impact review and truthful migration warnings.
- [x] I003 — Replace the two UI sections and implement provider selection plus
  unified multi-subscription attachment without losing deployment drafts.
- [x] I004 — Verify independent/mixed configurations, permissions, migration,
  unlink/detach retention, browser keyboard/mobile behavior and consumer guards.
  Automated evidence: 128 focused Dashboard tests, 237 final backend unit tests,
  10 environment HTTP tests and 12 desktop/mobile browser cases passed. Optional
  links are atomic and response-verified; removal is blocked by active adoptions,
  and removed scopes cannot supply current document/evidence provenance.
  Live Azure and populated SQL Server migration/RLS were not exercised.
- [x] I005 — Build, deploy locally, document actual results/migration/limitations
  and supply the manual acceptance walkthrough without unapproved GitHub writes.
  Deployment hold resolved September 30: final API and Dashboard rebuilt serially
  and deployed as `independent-scopes-final-20260930`; both healthy, exact
  authenticated provider-scope picker request returns200 rather than404.
  Existing provider relationships remain visible without subscriptions.
  SQL/Redis/Chat and domain records retained. Prior interrupted full integration
  and external verification gaps remain recorded, not presented as green.
  See `docs/dev/provider-demo-refresh.md` for actual results/manual steps.

- [x] E001 — Read current instructions, design originals and existing allocation,
  subscription registration, assessment and boundary/monitoring contracts.
- [x] E002 — Define additive organization allocation/environment/scope DTOs and
  schema with explicit ownership, source provenance and authority.
- [x] E003 — Implement provider allocation recording, scoped listing, usage,
  withdrawal/replacement impact review and version/idempotency tests.
- [x] E004 — Implement common environment apply/update/access/discovery with
  org-owned/provider sources, atomic hosting/environment/scope links and history.
- [x] E005 — Wire assessments and monitoring to canonical scope/entitlement,
  preserving run permissions and preventing revoked/broadened collection.
- [x] E006 — Implement Provider hosting/Connected environments and three-step
  wizard, reuse organization registration and preserve documentation drafts.
- [ ] E007 — Verify ownership models, three subscriptions, multi-system sharing,
  denied/partial/discovery/concurrency cases and downstream documentation.
  Automated allocation/scope and preserved-documentation cases pass. End-to-end
  canonical selected scope -> approved document/export output, live Azure sources
  and populated allocation SQL RLS remain unverified; do not mark that outcome
  complete from a saved attachment.
- [x] E008 — Run type checks/builds/tests/browser checks, local walkthrough and
  additive migration notes; distinguish verified Azure calls from mocks.
  Results and remaining full-suite failures are recorded in
  `docs/dev/provider-demo-refresh.md`; this is not a clean full-regression gate.

### Internal acceptance closure (authorized follow-through)

- [x] A001 — Repair the four stale response-constructor fixtures and establish
  a serialized whole-solution build/test baseline.
- [x] A002 — Create a reproducible synthetic system through canonical authoring,
  review and finalization operations; retain actual exporters/schema validators.
- [x] A003 — Inspect a real generated package, schema results, manifest hashes
  and source values without mocked generation/validation success.
- [x] A004 — Address documented inventory/readiness resolution gaps without
  inventing cloud hardware metadata or silently passing unevaluated requirements.
- [x] A005 — Prove source change, correct role handoff, stale result, revalidation,
  subsequent export and preserved historical artifact behavior.
- [x] A006 — Run builds/tests, document reproduction/local acceptance, and keep
  external eMASS receipt/acceptance as a gated unperformed check.
  Final executed suites: 7,758 unit tests passed, zero failed; 1,740 integration
  tests passed, zero failed, 20 existing Nessus scenarios skipped. Serialized
  whole-solution build passes; existing warning backlog is retained and reported.
  External receiver acceptance remains unavailable/unperformed, not waived.

#### A006 integration isolation follow-through (September 29)

- [x] Final full-run engineer simulation follow-up: the retained release log
  reports one remaining HTTP 500 in the Platform Engineer's synthetic
  `compliance_assess` invocation. Like the ISSO fixture, it still resolves the
  real cloud assessment engine for `test-sub`; inspect response diagnostics,
  replace only that external dependency, and require real tool execution plus
  the exact simulated identity/roles. Re-run both simulation classes. SQL-backed
  tests skipped by the parent run were not exercised.
  Focused red diagnostics (session `files/acceptance-remediation-results/engineer-simulation-red.log`)
  show the HTTP 500 is a duplicate-key `ArgumentException` inside EF InMemory
  `AssessmentPersistenceService.SaveAssessmentAsync`, reached through the real
  `RunRetainedAssessmentAsync` / `compliance_assess` path. This authentication
  fixture must not depend on live cloud assessment execution/persistence; this
  change does not establish that the engine's separate persistence path is fixed.
  Engineer fixture now supplies a strict synthetic `IAtoComplianceEngine`,
  verifies its exact invocation and successful result content, and preserves all
  simulated identity/role and actual middleware assertions. Both simulation
  classes passed **6/6, zero skipped**, recorded in
  session `files/acceptance-remediation-results/simulation-personas-final.trx`. No production edits.

- [x] Repair the authorization fixture's missing real `IInterconnectionService`
  registration exposed by the expanded package validator.
- [x] Read every portfolio page in tenant HTTP assertions: the collection retains
  earlier systems, while the endpoint intentionally defaults to 50 records.
  Preserve tenant exclusions and CSP impersonation assertions.
- [x] Trace capability setup/evidence targeting, retained package analysis and
  simulated ISSO tool execution before changing their failing expectations.
  Verified evidence mismatch: canonical local setup writes `SystemCapabilityLinks`,
  but upload accepts only `CapabilityControlMappings`. Accept either retained
  association within the current tenant/system; do not invent a control mapping.
  The simulated-ISSO authentication fixture invokes the real Azure assessment
  engine for the synthetic `test-sub` identifier. Substitute only that external
  execution dependency and assert the real protected tool invokes it; retain
  CAC middleware, identity assertions, tenant binding and authorization.
- [x] Re-run the host groups affected by the recorded 100-second timeout wave.
  The retained log shows relational connection cancellation and request-body
  cancellation; SQL Server use is not established by those generic EF frames.
  Do not increase timeouts or disable production authorization.
  No timeout recurred in the 155-test targeted run; this does not prove the
  whole-suite resource-contention cause. The factory/provider was not changed.
- [x] Run only the assigned integration groups after the exclusive compilation
  lane is granted; retain exact failures and distinguish environment-dependent
  results from verified regressions. Local manual acceptance remains available.
  First targeted pass: 154/155 integration tests and 27/27 evidence-service unit
  tests passed. All eight timeout-wave classes reached assertions without another
  timeout. The remaining provider-to-mission workflow incorrectly promotes a
  working-profile preview; preserve that rejection and test final generation
  from approved sources, including the existing evidence/provenance assertions.
  The first approved-path rerun reached a real missing-output failure: the final
  generated JSON has no `back-matter` approved provider summary. Source trace:
  `SspExportService.CreatePreviewAsync` adds responsibility/evidence pins, but
  `GenerateOscalJsonAsync`'s non-preview branch calls only `ExportAsync`.
  Normal enqueue also does not capture `SourceTenantId` / `RequestedPersonId`,
  which retained evidence worker validation requires. Do not remove the summary
  assertions or promote working previews to hide that release blocker.
  Changes/tests/logs are local; no deployment, external submission or authorization
  decision occurred.

  Targeted results (session `files/acceptance-remediation-results/integration-isolation-targeted.trx`):
  Authorization 13/13; tenant HTTP pipeline 26/26; workspace operations 20/20;
  CSP package lifecycle 8/8; simulated ISSO 3/3; workspace membership 26/26;
  assessment-plan workspace 10/10; categorization 11/11; control narratives 7/7;
  legacy policy mutation 3/3; policy workspace 11/11; provider decisions 14/14;
  provider monitoring 1/1; provider-mission workflow 1/2.
  Evidence artifacts: 27/27 (`evidence-artifact-targeted.trx`).
  Approved-export residual blocker reproduced separately:
  `provider-mission-approved-export.trx` (0/1), with the explicit missing-summary
  assertion, not a timeout. Logs with the same basenames retain build/test output.

  **Authorized handoff repair and final focused verification**:
  normal enqueue now retains the resolved tenant/requesting Person (never
  converts provider/support privilege into ordinary authority). The worker binds
  that ordinary scope, rechecks current system access, and the approved JSON
  generator uses canonical responsibility/evidence enrichment with validated
  summary hashes and retained source manifests. The HTTP workflow proves final
  approved bytes, exact approved summary content, responsibility revisions, caller
  identities, no private source inclusion and no invented AO decision. A separate
  HTTP test retains the working-preview promotion rejection. Unit tests retain
  evidence revocation checks and add current-system-access revocation coverage.

  Final result: **166/166 integration tests** in
  session `files/acceptance-remediation-results/integration-isolation-final.trx`; previous group counts
  above are unchanged except provider-mission is now **3/3**, plus **2/2**
  `SystemOperationalStatusHttpTests` and **8/8** `SspExportEndpointTests`.
  **90/90 unit tests** in session `files/acceptance-remediation-results/approved-export-unit.trx`
  cover SSP exports, provider evidence documents and evidence artifacts.
  No timeout recurred; the original broad-run timeout/empty-candidate
  cause remains unproven, with no factory changes or timeout increases.
  These targeted results do not substitute for the parent's whole-suite gate.

### Follow-up full-suite triage

Repair confirmed test drift rather than restoring unsafe historical behavior:
STIG unavailable results remain failed with scanner observations retained;
assessment upserts preserve linked historical findings; scan status includes
queued/processing/cancelled values; evidence-query fixtures must supply both
policy and Defender source responses. Minimal endpoint test hosts must register
the current workspace/tenant dependencies without bypassing authorization.
Serialized compilation and build-free full test execution remain required.
The classifier's existing 1,000-record/one-second requirement remains unchanged.
A full-suite-only 1.249-second failure under parallel load is addressed by
running its collection nonparallel; do not loosen the threshold or suppress it.

A002/A003/A005 native acceptance: two real InitialSubmission ZIPs, four real
schema validators, 152 distinct evidence artifacts, native inventory workbook,
SAR DOCX and verified readiness metadata. Both archives contain 160 entries.
Source mutation/review produces a new archive and leaves predecessor bytes
unchanged. No AO decision is created. Full solution compilation is green;
the full no-build test run remains separately tracked under A006.

Existing story ownership: US1 (#1042/#1043, related #1039/#1041/#764) and US4
(#1046), under #1038. External issue checklist append is preview/approval-gated.
The approved mock's data is illustrative, not a fixture to seed into the demo.

- [x] R001 — Inspect instructions, current purpose, validation/catalog/worker,
  retained contexts, manual eMASS observations, recorded decisions and phase history.
- [ ] R002 — Approve/synchronize the exact GitHub checklist append, preserving
  existing parents and acceptance criteria; do not close issues automatically.
  Preview offered September 29; user unavailable. GitHub remains unchanged.
  Existing verified issue linkage is retained; local implementation continues.
- [x] R003 — Write failing server tests for check outcomes, current/stale/failed
  history, purpose preservation and no-AO InitialSubmission.
- [x] R004 — Extend existing validators and add immutable scoped readiness runs,
  source references/fingerprints, readable findings, ownership and action permissions.
- [x] R005 — Guard enqueue/worker against changed evaluated records and invalid
  generated artifacts; retain exact run/source references and historical outputs.
- [x] R006 — Replace the readiness body with the approved task-oriented layout,
  five package milestones, RMF phase/history and expandable supporting records.
- [x] R007 — Wire purpose confirmation, direct-link check drawer, safe source and
  return navigation, history, stale refresh and generation context preservation.
- [x] R008 — Prove source changes affect actual previews/exports and validation,
  including draft/approved separation, conditional privacy, POA&M and responsibility semantics.
- [x] R009 — Test tenant/system/action denial, request cancellation, concurrent
  updates, real counts, unavailable sources and iterative historical workflows.
- [x] R010 — Run solution build/test, Dashboard typecheck/unit/browser suites,
  distinguish baseline failures, deploy locally and provide manual acceptance URL/steps.

R003–R010 verification: 59 targeted integration/source-preview tests, 306
frontend/routing/adapter tests and Dashboard type checking pass. Tests inspect
actual working-profile/approved output differences and emitted ZIP bytes/metadata.
Successful worker exporters are synthetic; a complete production-generated
package passing all real schemas has not been demonstrated. Real-schema negative
tests prove invalid emitted bytes cannot complete a package.
Full solution build retains four baseline `PendingImpacts` constructor errors;
the attempted solution test also encountered concurrent MVC manifest locks.
Six browser acceptance scenarios passed on each local port, and real
Legacy/InitialSubmission run persistence, correct source actions and disabled
generation for the incomplete demo were verified. Local manual acceptance
steps are documented in `docs/dev/provider-demo-refresh.md`. R010 records
execution/delivery, not a passing whole-solution baseline or user acceptance;
the baseline failures and unapproved R002 issue append remain open.

## Planning and first failing slice

| ID | Story/dependency | Task |
|---|---|---|
| T001 | All / planning | Verify baseline SHA and design copy hashes; create this spec set and `docs/design/index.md`; publish no GitHub changes. |
| T002 | All / T001 | Review `github-issue-preview.md`; obtain approval before creating/linking issues. Preserve existing parents; link all US owners. |
| T003 | US2 / T001 | Add deterministic synthetic fixture and production-host failing slice test at `Integration/ProviderMissionDocumentWorkflowTests.cs` (proposed); browser test `UI/e2e/provider-mission-document-workflow.spec.ts` (proposed). Use existing test-host/identity helpers, not a new testing framework. |
| T004 | All / T003 | For each PR, record code/dependency/migration/test/retirement decision in `research.md` and screen row. Characterize current behavior; update contracts before production edits. |

## Phase 1: foundations needed by the slice

| ID | Story/dependency | Task |
|---|---|---|
| T101 | US1 / T003 | Add failing purpose/initial-package tests around `Agents/Compliance/Services/PackageValidationService.cs` and `AuthorizationPackageService.cs`; then add purpose/snapshot persistence in their existing Core models/schema modules. Test SQLite/SQL Server and legacy requests. |
| T102 | US1 / T101 | Add actual generated OSCAL assertions in `Unit` for `Agents/Compliance/Services/OscalSspExportService.cs`; resolve reviewed decisions/adoptions through existing Core provider services. Assert stable party references and absent/conflicting metadata failures. |
| T103 | US1 / T102 | Add approved scalar/structured-child/draft-isolation tests for `SystemProfileService.cs`, `SspService.cs`, export generators and source projections; coordinate #969/#970, do not duplicate fixes. |
| T104 | US1 / T103 | Add readiness parity/fault tests for `EmassExportReadinessService.cs`, `PackageValidationService.cs`, `UI/src/pages/Documents.tsx`, `EmassStatus.tsx`, `SystemDetail.tsx`, and `LegalRegulatory.tsx`. Compose existing validators into one server result; errors cannot appear empty/passed. |
| T105 | US1 / T102 | Replace test-local export handler confidence with production-host tests using `Mcp/Endpoints/Dashboard/DashboardExportsEndpoints.cs`, `Mcp/Program.cs`, worker and download. Preserve scoped roles/tenants, evidence permissions, atomic failures and revoked access. |
| T106 | US2 / T003,T104 | Adapt `UI/src/components/layout/PageLayout.tsx`, `SystemLayout.tsx`, existing workspace UI primitives only where slice screens require it. Add interaction and 1440/390 visual comparisons; meet mocks, not old-layout parity. |

## Phase 2: complete provider-to-document slice

| ID | Story/dependency | Task |
|---|---|---|
| T201 | US2 / T003 | Exercise real host registration/worker through `Mcp/Endpoints/Csp/CspPackageImportEndpoints.cs`, `Mcp/Services/CspPackageWorker.cs`, `Core/Services/PackageImports/CspPackageProcessor.cs` and `CspPackageService.*`. Preserve partial/excluded source semantics, retries and stale review. Repair only reproduced failures. |
| T202 | US2 / T201 | Test real publication via `Core/Services/PackageImports/CspPackageService.Publication.cs` and `Unit/ProviderAuthorizations/ProviderImpactPublicationTests*.cs`; preserve atomic immutable release/context creation and exact approval. |
| T203 | US2 / T202 | Test allocation/association/adoption via `Core/Services/ProviderAuthorizations/ProviderHostingService.cs`, `ProviderMissionService*.cs`, `Mcp/Endpoints/Csp/ProviderHostingEndpoints.cs`, `ProviderMissionEndpoints.cs`; retain subscription handoff and role separation. |
| T204 | US2 / T203 | Trace organization placement/subscription/adoption callers before consolidating duplicate writes; update `data-model.md` with record counts/collision plan. Do not remove distinct models merely because all link systems. |
| T205 | US2 / T203 | Add evidence-sharing/lineage tests and smallest extension to `Core/Models/Compliance/EvidenceArtifactModels.cs`, provider evidence services and `Mcp/Endpoints/Dashboard/DashboardEvidenceEndpoints.cs`. Require approved distribution contract first. |
| T206 | US2 / T102-T106,T205 | Implement slice mock screens in `UI/src/features/provider-authorizations/`, `package-imports/`, `provider-relationships/`, `workspace-operations/system-capabilities/`, `pages/CapabilityResponsibilityReview.tsx`, `EvidenceRepository.tsx`, `Documents.tsx`. Use retained APIs, not mock in-memory engines. |
| T207 | US2 / T206 | Complete preview via the existing export/generation path; parse actual OSCAL/DOCX/package and inspect PDF. Prove pinned versions, source metadata, duties and evidence. Pass T003 host/browser tests and offer local user walkthrough. |

## Phase 3: provider mock screens

| ID | Story/dependency | Task |
|---|---|---|
| T301 | US3 / T207 | Replace provider offering/source/scope UI in `UI/src/features/provider-authorizations/AuthorizationsPage.tsx` and related views; adapt `/onboarding/csp` intake. Implement all assigned screen rows, mock fidelity and deep-link tests. |
| T302 | US3 / T301 | Complete evidence/finding UI with `Core/Services/ProviderAuthorizations/ProviderFindingService.cs` and `Mcp/Endpoints/Csp/ProviderFindingEndpoints.cs`; preserve explicit access/review/closure and history. |
| T303 | US3 / T302 | Consolidate provider capability/release/mission/admin/history screens in `UI/src/features/workspace-operations/`; use current workspace/auth services. Test provider relationship visibility limits. |
| T304 | US3 / T303 | Remove superseded provider editors/state/styles/unused routes after imports, APIs, jobs/MCP and retained-history checks. Record any bounded compatibility follow-up, not indefinite dual editing. |

## Phase 4: Systems mock screens

| ID | Story/dependency | Task |
|---|---|---|
| T401 | US4 / T207 | Replace Systems navigation/composition in `UI/src/components/layout/SystemLayout.tsx` and `ApplicationRoutes.tsx`; reuse `features/workspaces/SystemAliasRedirect.tsx`. Test every old route and workspace context. |
| T402 | US4 / T401 | Implement mock definition and controls/evidence pages using `UI/src/pages/SystemProfile.tsx`, `BoundaryManagement.tsx`, `ControlInheritance.tsx`, `EvidenceRepository.tsx`, `LegalRegulatory.tsx`, and selected-system capability features. Preserve underlying records and authorizations. |
| T403 | US4 / T402 | Implement mock assessment/risk/package/eMASS pages using existing `pages/Assessments.tsx`, `Remediation.tsx`, `PoamManagement.tsx`, `DeviationsPage.tsx`, `Documents.tsx`, `EmassStatus.tsx`. Add full production-host eMASS conflict/isolation tests. |
| T404 | US4 / T403 | Implement Overview/team/history mock composition, retain authorized decision operations and historical views, run all 30 page visual/interaction checks; retire replaced UI and duplicate calculations. |

## Phase 5: scoped changes and monitoring

| ID | Story/dependency | Task |
|---|---|---|
| T501 | US5 / T207,T404 | Add production route/permission tests for `UI/src/api/conmon.ts` against `Mcp/Endpoints/Dashboard/DashboardConMonEndpoints.cs` reads and `DashboardAuthorizationEndpoints.cs` writes. All four writes already exist; reuse and verify them, not duplicate handlers. |
| T502 | US5 / T501 | Implement scope/dependency attribution and collection-health records/projections in existing ConMon/watch services; test two systems per subscription, boundary revisions, moves/deletes, unknown events and failed/stale collection. |
| T503 | US5 / T502 | Extend `Agents/Compliance/Services/ConMonService.cs` and existing watch/job infrastructure for typed rule conditions, cadence/owner, evaluation history and deduplication. Coordinate #676/#754; no new parallel scheduler. |
| T504 | US5 / T503 | Extend `Core/Services/ProviderAuthorizations/ProviderImpactService.*`, responsibility impact dispatcher and narrative proposals to document/evidence/duty deltas. Preserve separate provider/mission dispositions and AO authority. |
| T505 | US5 / T504 | Implement mock monitoring/rules/changes/impact/report screens; test real host events, replay/disabled rules and old-baseline reads. Review generated document changes manually. |

## Phase 6 and final retirement

| ID | Story/dependency | Task |
|---|---|---|
| T601 | US6 / T207,T505 | Define concrete SaaS/manual-scope cases and approved compatibility contract; extend `Core/Models/ProviderAuthorizations/ProviderAuthorizationModels.cs` and existing hosting/mission validation, with preserved Azure behavior. |
| T602 | US6 / T601 | Implement the existing mock's service/management choices and manual relationship state; no fictitious Azure IDs or unsupported operational connectors. |
| T603 | All / each replacement | Execute `contracts/rollout-migration.md`: count reconciliation, safe upgrade/restart, redirects, canonical write cutover, legacy usage checks, retained history and retirement. |
| T604 | All / implemented phases | Run broader regression/build/typecheck at integration milestones and before merge. Record exact results/limits; offer user local tests; close issues only after their own acceptance is met. |

## Automated and manual matrix

| Phase | Deterministic automated evidence | Manual evidence |
|---|---|---|
| 1 | Purpose rules, actual generated fields, approved/draft isolation, missing/unknown/error readiness, production export authorization/download | Preview distinctive fixture values; inspect OSCAL/DOCX/PDF/ZIP and failed-check messages |
| 2 | Full host/worker/publication/association/adoption/responsibility/evidence path, same-subscription/cross-tenant denial, retries/stale approvals | Follow both workspaces with separate roles, inspect persisted records and actual artifacts |
| 3 | All provider screen primary actions, source failures, private evidence, reviewed closure, old links | Match mock visuals at 1440/390, keyboard/modals, onboarding deferral |
| 4 | All 30 pages, required actions and aliases, readiness parity, file-based eMASS conflict path, role/tenant isolation | Full system walkthrough and recorded decision kept separate from initial submission |
| 5 | Condition boundaries, disabled rules, replay/dedup, scope moves, collection failure vs no change, staged impact/history | Inject synthetic changes, review affected documents, verify no automatic authorization |
| 6 | Azure compatibility, manual SaaS eligibility, scope separation, repeat migration and retained history | Verify management duties and unsupported connector labels |

All phases cover restricted/unavailable evidence, revoked membership,
superseded/expired/withdrawn sources, concurrency and interrupted publication or
export. Unit tests remain deterministic; live Azure/Entra/eMASS checks are
separate, explicitly authorized manual work.

## Commands for implementation validation

From the repository root, start with relevant existing suites (not run here):

```bash
dotnet test tests/Ato.Copilot.Tests.Unit/Ato.Copilot.Tests.Unit.csproj \
  --filter 'FullyQualifiedName~ProviderHostingServiceTests|FullyQualifiedName~ProviderMissionServiceTests|FullyQualifiedName~ProviderImpactPublicationTests'
dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  --filter 'FullyQualifiedName~CapabilityResponsibilityTests|FullyQualifiedName~SspExportEndpointTests|FullyQualifiedName~EmassWorkflowEndpointsTests'
```

When touched, from the Dashboard directory run `npx tsc --noEmit`, `npm test`,
and `npm run test:e2e -- <changed-spec>` with an explicit spec path added by the
owning task. Do not pass the placeholder literally.

At integration milestones and before merge:

```bash
dotnet build Ato.Copilot.sln
dotnet test Ato.Copilot.sln
```

Expected: all required tests pass, no new warnings, required coverage, and
matching UI/actual artifact outputs. Record existing failures separately rather
than masking them. Builds/test execution do not replace user acceptance.

## Implementation checkpoint and release gates

- Shared provider shell now follows the five task destinations. Overview,
  offering-scoped mission relationships, retained change reviews and
  administration entry points use actual APIs/context, not mock data.
- Provider offering list/create/overview/source inventory/import/scope and
  finding/evidence/POA&M operations have changed presentation and tests.
  Canonical source review/publication remains in use; candidate deep links
  resolve to the retained review workflow. Issuer type can be explicitly
  recorded as person/organization or left unknown.
- Systems has eight navigation groups and all 30 destinations, with preserved
  legacy routes and concrete task bodies. Scope/health, executable rules,
  attributed changes, impact review, retained document preview and scoped
  activity history now have real endpoints rather than unavailable placeholders.
  Record-based definition, controls/evidence, assessment/risk and team screens
  reuse the existing authorized mutations. Overview, Documents and eMASS use
  the same purpose-specific validation component.
- InitialSubmission is explicit in the package API and now selectable in
  validation/generation; omitted purpose retains Legacy behavior. History
  displays recorded purpose. AuthorizedBaselineArchive and ChangeSubmission
  require explicit retained baseline/decision/hash selection. Archives retain
  original baseline bytes; change bundles add an explicitly retained, reviewed
  SSP delta, not an implicitly regenerated full assessment package.
- Provider-to-OSCAL provenance resolves reviewed source metadata rather than
  fabricating provider-name authorization types/current dates. Subscriptions
  now retain an explicit current adoption selection with concurrency fencing;
  successor selection preserves historical snapshots, and unsubscribe/reactivation
  clears the selection rather than reviving old authorization context.
- The SSP preview route displays actual generated fields, complete OSCAL,
  diagnostics and hashes. Explicit retention freezes the returned bytes and
  profile/provider/narrative/evidence source manifest; export consumes that
  snapshot rather than regenerating it. The UI requires review of the retained
  response and distinguishes current working sources from approved pins.
- All six profile sections capture immutable approved scalar/structured-row
  snapshots; document builders consume those and approved narrative versions.
  Provider source title/type/issuer/date, current selected adoption, confirmed
  duties and approved shared summaries reach actual generated output.
- Evidence sharing is explicit, versioned and limited to named eligible
  mission systems. Private attachments stay private. Grant/hash checks also
  protect export/worker/download; revocation does not delete historical bytes.
- Azure and manual service scopes are discriminated within the existing scope/
  allocation/adoption model. SaaS does not require Azure identifiers. Generic
  upstream-provider references coexist with unchanged legacy Microsoft records;
  neither service choice nor a rule claims a live connector.
- Provider-owned source monitoring reuses the evaluator/scheduler and impact
  ledger; mission rules use separate system scope and dispositions. Mixed
  provider/local boundaries do not suppress healthy local observations.
- SAP title/lead/scope/approach update the existing draft with concurrency
  checks. External AO decision recording separately captures source evidence,
  authority/date and retained baseline; no export or rule issues a decision.
- Export/package dialogs reuse the native modal shell instead of separate
  focus/backdrop implementations. Archive retry clears hidden selections and
  requires renewed source selection/validation.

### First complete HTTP slice

`ProviderMissionWorkflowHttpTests` passed twice consecutively through the
production host, real local source worker, review/publication, allocation,
Mission Owner association/adoption, separate ISSM confirmation, summary sharing,
retained preview, unchanged OSCAL schema validation, worker completion and
authenticated download. It asserts exact approved-summary/base64 bytes, customer
duty provenance, idempotent replay, foreign/unassigned denial, no fabricated AO
decision, and downloaded-byte/raw SHA-256 equality. One recorded artifact was
14,720 bytes, digest
`1A99B46B7CC95C20DED1F3DBDBA0A5A9774DD2DCDF7F89123CA4C2A788D71926`.
This is synthetic local integration, not live Azure or eMASS acceptance.

Final refinement also performs categorization through the real HTTP UI
contract (assigned ISSM POST 200, not a service-call prerequisite). That run
passed the unchanged assertions with a 14,720-byte artifact, digest
`636450F6468359E791A0FAB77A07858BB5B8CA267FCF6ED554DD289B1EF6C1C4`.
Per-run identities change generated hashes; each run compares the exact retained
bytes and digest within that run.

### Verification evidence and remaining environment gate

- [x] Internal-release fixture alignment: the sequential acceptance log reports
  endpoint parameter inference failures in `NotificationHubIsolationTests`
  (`AssessmentResultsWorkspaceService` and `ITenantContext`) and
  `ApiMismatchRouteTests` (`RemediationWorkspaceService`). Repair only their
  minimalist test-host registrations using scoped production services and
  explicit test identity/tenant semantics; preserve authorization assertions.
  Verified both groups with sequential `dotnet test --no-restore -m:1 --filter
  FullyQualifiedName~<class>` runs: `NotificationHubIsolationTests` **98 passed,
  0 failed, 0 skipped**; `ApiMismatchRouteTests` **49 passed, 0 failed, 0 skipped**.
  Scoped real workspace services and strict unused dependency mocks restore
  host construction without changing assertions or production endpoints.
  Integration compilation reported existing warnings outside these fixtures.
  These same commands, targeting their respective unit/integration projects,
  are available for local manual reruns; user acceptance remains pending.
  No full-suite claim.

- Manual exchange slice: additive tenant-scoped history, completed same-system
  package/hash/version validation, authenticated recorder provenance, explicit
  outcomes and append-only corrections are implemented. SQLite unique indexes
  fence concurrent history writes and request replay. Tests cover production
  HTTP mapping, scoped assignments, tenant/export mismatch and no AO mutation.
  Targeted exchange/workflow regressions passed: 34 unit, 10 integration and
  5 Dashboard interaction tests. Verification-only follow-up expanded the
  exchange suite to **63 passing tests**, with **100% service line coverage
  (106/106)** and **98.52% branch coverage (67/68)**. Additional cases cover
  invalid provenance, retained export purpose/identity, restricted histories,
  changed-actor replay, conflicting concurrent keys, and storage failures
  propagated without leaving an insert pending. The remaining branch is the
  nullable `CompletedAt` comparison after the query requires it to be non-null;
  no production refactor or coverage suppression was introduced. Measurement
  used isolated build output to avoid concurrent collector instrumentation.
  SQLite additive schema/repeatability and concurrent replay
  were exercised; SQL Server DDL has not been executed in this slice.
  Local user acceptance remains pending; no live eMASS receipt is asserted.
- Workbook rationale follow-up: the existing conflict request/response now
  carries optional `rationale`, with legacy `notes` compatibility. Existing
  conflict Notes and append-only audit metadata retain current/prior reasons,
  actors and original values; no new table or reconciliation engine was added.
  Dashboard single/bulk acceptance requires rationale. Live-field comparison,
  existing-column concurrency checks and relational transactions prevent stale
  overwrites. Verification passed: **116 unit tests**, **14 HTTP integration
  tests**, **12 Dashboard tests**, and Dashboard type-check. Coverage exercised
  **all 74 modified executable service/exception lines** (not a claim of 100%
  coverage for untouched legacy sync code). Manual workbook and audit acceptance
  steps are in `quickstart.md`; external receipt history remains separate.

- Solution builds pass. Incremental builds reported zero warnings; the later
  full reference rebuild also reported existing obsolete/nullability warnings.
- Dashboard `npx tsc --noEmit` and `npm run build` pass. The CSS syntax warning
  was fixed without changing parsing behavior; dependency annotation, mixed
  import, Browserslist age and bundle-size warnings remain.
- Provider shared-shell/relationship tests: 26 passed; new provider-workspace
  files reached 100% line/function coverage, 94.11% branch coverage.
- Targeted provider migration tests and Systems tests passed in their reported
  runs; see the implementation plan for the latest integration record.
- Chromium: two provider desktop/mobile navigation checks and 20 Systems/
  responsibility checks passed at 1440/390px using synthetic intercepted APIs.
  These do not prove live backend authorization or complete visual fidelity.
- Follow-up: generated preview/purpose/navigation tests passed (59 tests for
  preview/navigation and 34 for purpose/progress/API contracts). The updated
  Systems Chromium flow passed at 1440/390px through real SPA preview rendering
  with synthetic HTTP fixtures and explicit InitialSubmission requests.
- Backend follow-up: 377 targeted unit and 157 targeted integration tests passed,
  covering explicit current-adoption selection, v1-to-v2 output/history,
  reactivation/concurrency, production preview authorization and wrong-system
  download rejection. A read-only follow-up review confirmed the historical-pin
  and missing-UI-purpose defects resolved.
- Full Dashboard regression reached **2,216 tests / 223 files passed** after
  fixture and async-control corrections. Native-modal and retained-context
  follow-ups also pass their targeted suites.
- Full .NET unit regression: **7,510 passed, 0 failed**.
- Full integration regression reached **1,508 passed, 1 failed, 20 skipped**.
  The single failure was a test inventory coercing valid string system IDs to
  GUIDs; it now preserves native IDs, with a regression and passing affected
  class. The final focused HTTP run passed **41 tests**, including the complete
  vertical slice, preview and tenant isolation paths. Earlier tenant fixture
  leakage was reproduced and corrected without
  relaxing assertions or production authentication.
- The subsequent all-integration rerun encountered SQL Server timeouts during
  database creation; a separate read-only `docker ps` also stopped responding.
  The run was stopped after preserving diagnostics. No shared Docker restart,
  broad container deletion or timeout suppression was attempted. Full SQL Server
  execution is an **environment-blocked release gate**, not a passing check.
- Final compiled-UI verification passed **12 desktop/mobile browser workflows**
  plus **4 manual-service/AO/SAP browser workflows** at 1440/390px. Earlier
  responsibility tests added 18 passing browser scenarios. These use synthetic
  HTTP responses; production-host tests separately verify persistence and access.
- User visual/manual acceptance and live receiving-eMASS validation remain
  unperformed. Do not claim production deployment, real receipt/import
  acceptance, or cATO from these checks.

No issue closure, GitHub write, commit, push, production mutation, live cloud
validation, or user manual acceptance has occurred.
