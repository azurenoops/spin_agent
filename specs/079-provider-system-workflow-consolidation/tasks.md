# Tasks: Feature 079

**Inputs**: [spec](spec.md), [plan](plan.md), [screen contract](contracts/screen-route-migration.md)  
**Status**: Application implementation started 2026-09-26; full contract acceptance remains open.  
**Format**: ID / story / dependency / exact affected paths and acceptance.

Tests are mandatory, despite the older template's optional-test wording.
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
