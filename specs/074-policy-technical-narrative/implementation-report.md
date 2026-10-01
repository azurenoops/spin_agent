# Requirement coverage implementation report

## Status

### Rebase onto main (October 1)

Rebased onto `74273c46` after preserving all local narrative and responsibility
work in a checkpoint commit. The only textual conflict was in package
validation: upstream's approved system-design checks are retained unchanged,
with requirement-coverage checks running alongside them.

The first combined regression run exposed six schema-validation failures in
the newly merged system-design export tests. Their fixture has a baseline and
implementation but no retained catalog; the source-qualified exporter therefore
correctly omits an unverified profile URI and control identifiers. The fixture
now has an explicit synthetic source and matching `ac-1` catalog control.
The previous fabricated source fallback was not restored, and schema/design
assertions remain unchanged. This is test-context alignment, not a production
design change.

Post-rebase validation passed: solution build (10 warnings, zero errors in the
final incremental run), 243 focused unit tests, 18 focused integration tests,
Dashboard strict TypeScript checking, 46 focused Dashboard tests and six
desktop/mobile browser cases. The full solution test suite was not rerun.
Concurrent responsibility-draft edits continued after the checkpoint; these
later edits remain outside the rebase follow-up commit.

### Hierarchical table correction (October 1)

The flat-row renderer did not match the approved mock. The Dashboard now groups
the current page using the existing catalog-backed parent identifiers. Parent
rows precede their enhancements; child rows are indented with a connecting guide
and retain independent Policy/Technical states and detail actions. No identifier
parsing, synthetic requirements or server/data changes were introduced.

Search/filter/page boundaries retain a navigation-only parent context when the
parent is not a matching record on that page. Context rows do not affect counts
or fabricate statement states. The summary says "enhancements shown" because it
counts only this page, not the entire selected baseline.

Validation: three new tests failed before production changes; all 28 focused
workspace/API/coverage tests then passed. Strict Dashboard TypeScript checking
and production build passed. Six Playwright cases passed at desktop/mobile
widths, including author/viewer flows and measured indentation/guide styling.
The build still reports Browserslist, SignalR annotation, dynamic import and
chunk-size notices; this is not a warning-free-build claim. No C# was changed
for this presentation correction; previous solution results remain recorded
below, not represented as a fresh full-suite run.

Live port 5197 was checked: AC-11 precedes indented AC-11(1); searching only
AC-11(1) shows parent context and "Showing 1 of 1 records"; its parent link opens
AC-11's requirements while preserving the search and organization/system URL.
User manual acceptance remains pending.

Manual check: open Narratives on port 5197, select All controls, search AC-11,
and inspect the parent/child layout. Open each row, then search AC-11(1) alone
and use its parent-context link. Repeat as a view-only user and an authorized
author; navigation must not mutate content or grant editing permission.
Rollback of this correction requires restoring only the grouping/rendering and
hierarchy CSS changes, then rebuilding the Dashboard; no database restore or API
container replacement is needed.

### Large-source retrieval fix (October 1)

Root cause was measured inside the live API container: the retained
10,441,580-character catalog took 202 ms to retrieve with sequential access and
158 ms to parse. Buffered SqlClient async retrieval instead took 124,462 ms
before a 15-second cancellation request completed. The issue was transport/
materialization, not the source content, catalog selection or parsing.

`CatalogSourceReader` now resolves scoped metadata through EF, then streams the
large JSON column with sequential access. It preserves the active transaction,
tenant/baseline predicates, cancellation, exact source bytes and downstream hash
checks. Reference-source replacement between the metadata and body reads is
reported as a concurrency conflict. Other providers retain their existing path.

The reader is used by narrative list/detail/coverage, automatic association,
source capture, baseline operations, document exports, retained-context checks
and readiness source hashing. Catalog-definition list/detail queries now select
only metadata rather than accidentally fetching the complete source document.
No caching requirement, timeout increase, truncated source, or integrity-check
bypass was introduced.

The local API runs `ato-copilot-mcp:streamed-catalog-20261001-e98474e9`.
Authenticated measurements through port 5197 returned HTTP 200:

- Narrative list: 216 ms.
- AC-11 detail: 174 ms.
- AC-11 requirement coverage: 190 ms.

The real UI displays `ac-11_smt.a`, `ac-11_smt.b`, the selected AC-11(1) link,
and the enhancement's `ac-11.1_smt` with its parent link. There is no catalog
selector or reconciliation-rationale input. Existing FedRAMP-R5 / 5.2.0 source
identity and control/version/baseline content fingerprints remain unchanged.

The SQL Server performance regression streams two synthetic sources of over
10.5 million characters each in under five seconds, checks exact contents and
scope, and leaves no tracked binding entities. Six SQL performance/isolation/
transaction cases and 147 focused governance/export regressions passed. Full
solution revalidation is recorded separately when complete.

Complete solution revalidation for the streaming change: build passed with zero
diagnostics in that incremental run; 7,966 unit tests passed. Integration
reported 1,771 passes, 20 existing skips and one provider-package analysis
failure (`ReadyForReview` expected, `NeedsAttention` observed before catalog
retrieval). The unchanged test passed when run alone. This does not establish a
clean complete-suite result; no assertion was weakened or failure suppressed.
Concurrent responsibility-review changes in this worktree were preserved.

### Automatic association correction (October 1)

The user removed the interactive catalog-selection requirement. The local API
now runs `ato-copilot-mcp:auto-catalog-20261001-e98474e9`, with system-owned
association at startup and baseline/source lifecycle hooks. The narrative
catalog/rationale form is removed. Existing bindings remain pinned; ordinary
GETs remain read-only. Baseline reselection retains its row and source history.

Focused validation passed 57 unit, 24 integration and 22 frontend tests plus
strict TypeScript. The complete build passed; the complete integration suite
passed 1,768 tests (20 existing skips). The unit suite reported 7,965 passes and
one notification-delivery test failure: it expected DELIVERY_FAILED but observed
DELIVERY_TIMEOUT. That test is outside this change and was not modified.

**Historical blocker, resolved by the retrieval fix above.** The live system already had an attributed
FedRAMP-R5 / 5.2.0 binding created before this deployment; it was preserved.
The retained JSON occupies 20,883,160 SQL bytes. Sequential request logs show
narrative workspace/detail reads returning 200 after approximately 140 seconds,
and the UI remains loading during verification. Large-source retrieval is a
hypothesis, not an established root cause; SQL retrieval and parsing must be
timed separately. No restart, binding replacement or fabricated source was used
to mask the problem. Investigation paused at the repository debugging limit;
the request for further direction could not be answered because the user was
unavailable. Do not mark the live experience or feature accepted.

### Catalog administration repair

The local preview now runs `ato-copilot-mcp:catalog-admin-20260930-e98474e9`.
Control Catalog always exposes the reference-source workflow, including when
framework definitions already exist. Platform administrators in the provider
workspace can load/refresh one source or backfill missing sources; ordinary
organization users and support contexts cannot mutate the global catalog.
Existing full-import endpoints use the same server gate.

Source-only capture stores source JSON/version/URI/time separately and does not
replace flattened definitions, system baselines, narratives or retained bindings.
The narrative warning links to catalog management instead of ending at disabled
choices. Missing-source backfill is an explicit admin action, not a page-load
side effect or automatic approval.

Validation for this repair: full solution build passed with no diagnostics in
that incremental run; 7,959 unit tests and 1,763 integration tests passed, with
20 existing skips. Thirty-three focused Dashboard tests, strict type checking,
production build and six browser cases passed. Live source status returned 200;
the current non-platform-administrator account received the expected 403
`CATALOG_ADMIN_REQUIRED` on a mutation. The UI shows read-only admin guidance for
that account. No privileges were granted or identity switched for testing.

A fresh verified SQL backup was taken before deployment. The pre-update API is
retained as `ato-copilot-mcp-before-catalog-admin-e98474e9`. Narrative and baseline
fingerprints still match the original values. Source loading and subsequent
system binding remain actions for the authorized administrator and reviewer.

### Approved local preview deployment

On September 30, the user explicitly approved a database backup and updating the
local API behind port 5197. A COPY_ONLY SQL Server backup passed RESTORE VERIFYONLY,
and the new API image became healthy. Pre/post fingerprints for 339 control
implementations, narrative versions and the baseline matched exactly.

Live verification exposed and corrected a first-upgrade ordering defect:
tenant-column setup attempted the new coverage tables before their schema module
created them. The regression now removes these tables from an older database
before invoking actual startup. The schema module runs before the tenant-column
pass. All 12 targeted startup/narrative HTTP tests passed after the correction.

The active local API image is
`ato-copilot-mcp:requirement-coverage-20260930-e98474e9-r2`.
It is healthy with no container restarts, and the authenticated AC-11 coverage
request through port 5197 returns HTTP 200. The corrected startup verified RLS on
146 tenant-scoped tables without the earlier missing-table errors. Control,
version and baseline fingerprints still match the pre-deployment values.

The pre-deployment container remains stopped as
`ato-copilot-mcp-before-coverage-e98474e9`; the first feature image is also retained.
The verified 15 MB SQL backup is in the SQL container at
`/var/opt/mssql/backup/requirement-coverage-e98474e9/AtoCopilot.bak`.
No database restore was performed.

Existing catalog choices report **Source import needed**. The live UI therefore
shows **Catalog source needs reconciliation**, not invented requirements or
relationships. Catalog import/binding decisions remain unapproved and were not
performed. Importing the applicable authoritative source and reviewing its
system binding are still required before populated requirement navigation can
be accepted manually.

Implemented locally on `feat/requirement-coverage-enhancement-nav`, based on
`origin/main` at `a4d43d7ca64bbdbb57eeb2a6671ec7eb2e777399`.
Changes are uncommitted and have not been pushed.

**This is not a declaration of feature acceptance or release readiness.**
Real-user manual acceptance and approval of the previewed GitHub writes remain
outstanding. No eMASS submission, acceptance, operational control effectiveness,
or authorization decision is claimed.

## Changes

- Preserved the existing Control Narratives table, drawer, tabs, statement
  switch, search/filter/page state and workspace navigation.
- Added catalog-derived parent/enhancement labels and navigation, including
  selected controls without narrative records. Source identifiers and display
  labels resolve through explicit catalog data rather than identifier parsing.
- Retained unflattened imported catalog sources and per-baseline source bindings.
  Missing source identity, structured requirements and parameter values remain
  explicit gaps; legacy prose is not automatically mapped.
- Added independent Policy/Technical requirement responses, explicit evidence
  hash associations, version checks and separate coverage review.
- Added rationale-required enhancement proposals. Selection acceptance and
  narrative draft creation/reconciliation are transactional; existing approved
  text and untouched narrative halves survive. Returned proposals retain their
  review notes. Human Technical drafts do not retain old AI-generated flags.
- Extended approved SSP, custom/built-in DOCX, PDF, OSCAL and eMASS preparation
  projections. Parent responses and separate enhancement identifiers survive
  exact-output tests. OSCAL uses catalog identifiers, not invented Policy/
  Technical statement IDs.
- Added purpose-specific requirement readiness checks and source freshness.
  Required gaps block new preparation readiness; retained archives stay retained.

## Architecture and guidance compliance

Guidance references: [AGENTS.md, Verification Protocol](../../AGENTS.md),
[constitution, Documentation as Source of Truth / TDD / Quality Gates](../../.specify/memory/constitution.md),
and [tenant-isolation architecture, Three-Layer Defense in Depth](../../docs/architecture/tenant-isolation.md#three-layer-defense-in-depth).
Repository identity, documentation-first, failing-test-first, required build/test/
type-check execution and scoped automated security checks: **PASS**.
Zero-warning/lint gate: **NOT ESTABLISHED**, for the documented existing
warnings and absent lint setup. GitHub traceability and real-user manual
acceptance: **BLOCKED pending explicit user action**.

- **Repository/guidance:** identity and working changes were checked; AGENTS.md,
  the constitution, applicable specs/contracts and relevant documentation were
  read. The branch base was corrected with explicit user approval before coding.
- **Documentation first:** verified gaps, design decisions and dependency-ordered
  tasks were recorded before production changes.
- **TDD:** source parsing, transactional selection, mappings, permissions,
  schema/provider behavior, UI behavior and export fidelity have retained
  failing-test-first evidence.
- **Reuse:** existing workspace permissions, narrative snapshots, source
  projection, schema-addition and document pipelines are reused. No new agent,
  MCP tool envelope, role switch or automatic enhancement selection was added.
- **Data design:** immutable per-baseline catalog copies avoid an additional
  global revision subsystem. Working/reviewed mapping snapshots remain distinct
  and participate in narrative history. Pending enhancement drafts are stored
  outside active implementation records.
- **Security:** reads/writes check system and tenant ownership and server
  permissions; separate-person review is enforced. SQL Server RLS tests cover
  both new tables. Concurrency and partial-failure tests verify persistent state,
  not only tracked in-memory entities.
- **Provider reliability:** SQL Server mutations use the configured execution
  strategy around the whole transaction. A one-time transient insert fault
  verifies replay does not duplicate proposals.
- **External writes:** exact issue bodies and parent links were previewed, but
  approval was unavailable. None were sent. GitHub issue discipline remains an
  explicit open release gate; local implementation continued at the user's
  subsequent direction.

## Validation results

Full required commands before the deployment-ordering correction above:

```bash
dotnet build Ato.Copilot.sln
dotnet test Ato.Copilot.sln
```

- Build: passed, **0 errors, 35 warnings** in that incremental run. A zero-warning
  solution build is not claimed. No compiler warnings were reported for newly
  introduced production files.
- Unit suite: **7,958 passed, 0 failed**.
- Integration suite: **1,758 passed, 0 failed, 20 skipped**.
- Earlier runs exposed genuine feature issues in SQL Server DDL/transactions and
  source-alias handling; those were corrected and revalidated. An unchanged
  scan-import test also exposed its existing completion-versus-file-cleanup
  ordering race on earlier runs; it passed on the final run and was not modified
  or suppressed.

Dashboard:

```bash
npx --no-install tsc --noEmit -p tsconfig.json
npm run build
npx --no-install playwright test --config playwright.requirement-coverage.config.ts
```

- Strict TypeScript and production build: passed.
- Focused API/component tests: **36 passed**.
- Isolated desktop/mobile browser cases: **6 passed**, including viewer/author
  behavior, parent/enhancement navigation, preserved URL scope and no horizontal
  overflow.
- New Dashboard API/panel: **100% measured executable-line coverage**, with
  **92.59% combined branch coverage** in the focused report.
- Combined focused unit/HTTP execution: parser **62/62**, requirement service
  **398/398**, document projection **158/158** executable lines covered.
  These are scoped measurements, not a claim of 100% repository-wide coverage.
- SQL Server startup upgrade/rerun, new-table RLS and retry/fault-injection
  scenarios passed in disposable test containers.
- `git diff --check`: passed.

Limitations of the checks:

- `npm run lint -- --no-fix` cannot run: the existing script invokes ESLint,
  but ESLint is not in the installed locked dependencies and no ESLint
  configuration is tracked. No unrelated lint tooling was introduced.
- The Dashboard build reports Browserslist age, SignalR annotation,
  existing mixed-import and chunk-size warnings. No warning suppression or
  unrelated dependency upgrade was used.
- Synthetic browser fixtures do not replace authenticated real-user acceptance.
  Real eMASS import/submission interoperability was not exercised.

## Local manual acceptance

Follow the [AC-11 / AC-11(1) walkthrough](quickstart.md#requirement-coverage-local-acceptance)
using an isolated database and separate reader, author and reviewer identities.
It covers catalog reconciliation, requirement mapping, supporting evidence,
pending/accepted/returned enhancements, conflicts, preserved approved content
and inspection of actual generated documents.

The walkthrough includes checked local startup entry points and a dedicated
Dashboard/API port example. Do not replace shared services or disable
authentication. Manual results have not yet been supplied.

## Rollback

1. Stop only the isolated feature instance and retain a backup of its database.
2. Return to a reviewed prior application build in a separate clean checkout;
   do not discard unrelated working changes.
3. Keep additive columns, source bindings, proposal records and historical
   snapshots. Do not drop data or remove audit history to roll back code.
4. Verify older-writer/export compatibility before using a downgraded instance
   for preparation. Reversing an accepted baseline change requires new
   authorized review work, not deleting its history.

The [contract](contracts/requirement-coverage.md) and [task ledger](tasks.md#requirement-coverage-continuation)
retain the behavior and remaining acceptance gates.
