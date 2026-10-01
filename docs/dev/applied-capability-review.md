# Applied capability review: local acceptance walkthrough

## Current deployment after the main rebase

The session branch now descends from `origin/main` at `69b7e407` (merged #1062).
The panel uses the upstream `ResponsibilityDraftService`, `ResponsibilityFirstPass`
and canonical control/scope draft APIs. The earlier session-specific draft
store/endpoints were retired; no database tables or saved records were dropped.
The implementation was preserved in a local commit and rebased, not pushed.

Current images:

- `ato-copilot-mcp:main69b7e407-reviewf7368843-20261001`
- `ato-copilot-dashboard:main69b7e407-reviewf7368843-20261001`

The original demo MCP/dashboard containers were upgraded while preserving their
active environment, named volumes and ports (3002/5173). A copy-only,
checksum-verified database backup was taken in the original SQL volume before
upgrade. SQL Server still contains the active **SPIN Demo System** record.

The review UI on **5197** now proxies to that same original demo backend on
**3002**, so it uses the existing demo database instead of the earlier fresh
preview database. Normal sign-in and organization/system permissions still apply;
an unauthenticated/unscoped system-list request returns 403 rather than bypassing
authorization. The isolated development backend/volume on 3197 is retained, not
merged or deleted. To explicitly test it instead:

```bash
CAPABILITY_REVIEW_MCP_BASE_URL=http://mcp:3001 \
  docker compose -f docker-compose.capability-review.yml up --no-build -d
```

Post-rebase verification: backend compilation, actual dashboard type-check and
production build passed; **124 backend unit tests**, **88 integration tests**, **79 focused dashboard tests**
and **18 delivered-image browser scenarios** passed. Earlier test totals below
describe the pre-rebase implementation, not a new full-suite run on merged main.

Canonical statuses are Proposed, ComparisonRequired and Accepted. Save is not
acceptance; confirmation requires saved-state review, source/duty acknowledgements
and notes and uses upstream enforcement. Saved counts are read from canonical
authorized control/scope contexts, not inferred from component association.

### Current local walkthrough

1. Sign in on 5197 and select the original demo organization and SPIN Demo System.
   Workspace authorization is required; a raw unscoped API call is not a data browser.
2. Open Audit collection, inspect Overview and recorded component locations, then
   select AU-11, AU-2 or AU-6 in Responsibilities.
3. The canonical first pass starts with system records. Explicitly choose a recorded
   provider scope when applicable; inspect source versions, duties, exclusions,
   missing information and conflicts. Correct the populated fields.
4. Save the draft. Refresh and compare suggestions using the canonical comparison
   actions; corrections are not silently replaced. Saving does not accept inheritance.
5. Choose Review saved responsibility, verify applicability/coverage and customer
   duties, add review notes and explicitly confirm. Confirm the matrix and separately
   reviewed narrative/document outputs. No action here submits eMASS or grants an ATO.

The prior implementation notes and validation below are retained as a history of
the original session, not as evidence of a full-suite run on the rebased code.

## Prior implementation notes

The Applied Capabilities list and right-side dialog remain in place. The dialog
now has Overview, Where it applies, and Responsibilities sections. Keyboard
Left/Right, Home and End navigate the sections. Scope gaps count contributing
components with no InScope or SystemWide location; excluded locations do not
establish coverage. Responsibility confirmation counts and saved draft review
counts are separate.

Selecting a control opens its prepared responsibility first pass in the dialog.
Users can correct and save the system draft without accepting inheritance.
Unsaved corrections survive control/section changes, suggestion refresh and
placement-dialog navigation within the identity-keyed page. Browser reload
restores saved content, not unsaved edits. Switching identities/workspaces
clears in-memory edits.

The existing full capability review supports an explicit handoff of a reviewed,
saved provider draft into its existing responsibility confirmation form. This
handoff does **not** confirm anything. Source/coverage checks and notes remain
required. Organization-only allocations still use the authorized Control
Inheritance workflow; there is no competing inheritance approval API.

## Source and permission boundaries

- A current, exact provider adoption/scope pin enables the published
  control-specific Provider/Shared/Customer split. Legacy subscriptions without
  that pin use system records and explicitly request scope adoption/review.
- The release contract contains splits, not necessarily detailed customer duty
  text or exclusions. Missing information is flagged, not synthesized as fact.
- Approved policy/technical narratives are preferred to current working content.
  Working content, missing duties and unverified applicability are flagged.
- Component locations, system descriptions, scope revisions, narrative versions
  and evidence content hashes participate in preparation provenance/invalidation.
  Evidence metadata is not proof that its content has been verified.
- Optional configured AI supplies a separately labelled explanation. It cannot
  write the allocation or authoritative duty fields. A failed AI request remains
  an error; absent AI configuration is explicitly reported with the supported
  source-grounded fields.
- Generated explanations and immutable saved revisions are tenant/system/control
  scoped. ISSM/ISSO assignments are rechecked server-side for draft saves and
  existing responsibility confirmation. Provider-authored sources remain read-only.
- Draft review never changes control satisfaction, approved narratives, accepted
  inheritance, authorization decisions or submission state.

## Start locally

The isolated development Compose definition uses separately named services,
versioned image tags and a dedicated SQLite data volume by default:

```bash
docker compose -f docker-compose.capability-review.yml up --no-build -d
curl --fail http://localhost:3197/health
curl --fail http://localhost:5197/
```

This is a development simulation configuration, not a production deployment.
It does not copy existing SPIN data or synthetic mock content. For an existing
system, use an authorized test database copy by configuring
`CAPABILITY_REVIEW_DATABASE_PROVIDER` and `CAPABILITY_REVIEW_CONNECTIONSTRING`
through your local environment; do not commit credentials. Import or create only
your real authorized source records. Take a backup before enabling a new build
against an existing database. Startup additions retain existing records and add
the two draft preparation/history tables.

The session's updated local image tags are already built. Use `--no-build` to
test those verified images while container package-source TLS remains unavailable.
After that connectivity issue is resolved, `up --build -d` runs the normal clean
Dockerfile restore/build path. Do not disable TLS checks or substitute unverified
provider content to make a build or review appear successful.

If another application owns 5197, do not stop it without its owner's permission:

```bash
CAPABILITY_REVIEW_PORT=5198 \
  docker compose -f docker-compose.capability-review.yml up --no-build -d
```

For frontend-only inspection:

```bash
cd src/Ato.Copilot.Dashboard
VITE_API_PROXY_TARGET=http://localhost:3197 \
  npm run dev -- --host 127.0.0.1 --port 5197 --strictPort
```

Use 5198 instead if 5197 is occupied. A served frontend alone is not an
end-to-end working system; the matching backend must be reachable.

### Environment recovery and delivery

Initially port 5197 was owned by two Vite development previews from another
worktree, so early source validation used 5198 without stopping them. After the
user directed autonomous completion of the requested port-5197 delivery, only
those two identified listeners were terminated. Their worktree files and the
original Docker stack were not changed. The final updated isolated app is served
at `http://127.0.0.1:5197`, with its matching API at
`http://127.0.0.1:3197/health`; both returned HTTP 200.

Initial Docker image builds were attempted but Docker Desktop returned
"Docker Desktop is unable to start." Its sequential startup logs reported
"no space left on device" writing its VM/startup log. The host data volume was
at 100% while the separate repository volume had free space. No shared Docker
images, caches, volumes, logs or unrelated files were pruned, and no restart
was used to conceal this failure. After the user explicitly requested Docker
startup, host storage had recovered to 107 GiB free. The stale Docker backend
could not stop through the desktop CLI, so only its identified stalled processes
were terminated and Docker was relaunched. All five existing containers recovered
healthy with their original ports and volumes.

Updated images were subsequently built under these unique local tags:

- `ato-copilot-mcp:applied-review-145446f0-20261001`
- `ato-copilot-dashboard:applied-review-145446f0-20261001`

Container npm/NuGet HTTPS restores failed with SSL EOF/connection-reset errors.
TLS verification was not disabled. The MCP image used the existing Dockerfile's
`nuget-packages` named context and cached `.nupkg` feed. The dashboard used current
`npm run build` output, after its real TypeScript check, supplied as the existing
Dockerfile's `build` named context (`app/dist` layout) to the unchanged nginx
runtime. This is local verified-artifact packaging; the normal clean-container
restore path still needs its network issue resolved.

The isolated Compose app was started with `--no-build` and is healthy at
`http://localhost:5197` (API health: `http://localhost:3197/health`), using its
dedicated development SQLite volume. The page and login/bootstrap API returned
HTTP 200. It is not the main stack's data and contains no copied provider mock
statements. All 18 applied-capability/system-capability browser scenarios passed
against the final Docker images on 5197, including provider and organization
workflows, read-only actors, stale writes, protected evidence, keyboard access,
WCAG contrast, light/dark themes and desktop/mobile clipping checks.

## Manual walkthrough: Audit collection

Use an authorized local test system and its real **Audit collection** capability.
If AU-11, AU-2 or AU-6 is not mapped, obtain the correct source mapping or baseline
through the existing authorized workflow; do not add fabricated provider claims.

1. Open Applied Capabilities and the Audit collection review dialog. Verify
   provider/source and description against the recorded source. Inspect the
   expandable version/evidence/limitations section. Check that scope gap and
   saved-draft counts match actual records.
2. Go to Where it applies. Confirm each component's name and recorded location.
   Use Assign location/Change to select an existing authorized boundary. Check
   excluded/unassigned components remain gaps. Verify no locations are assigned
   implicitly and no deployment or monitoring coverage is asserted.
3. Open Responsibilities and AU-11. Compare the original split/source references,
   customer records and missing-information flags. When provider scope is pinned,
   verify the explicit split is copied unchanged. Without a current pin, verify
   the first pass does not invent a provider or inherited designation.
4. Correct the customer draft and notes. Switch to AU-2, then AU-6, then back to
   AU-11. Your correction must remain. Refresh suggestions: inspect the new
   suggestions without replacement of edited fields. If context changed, compare
   it and explicitly keep your edits with the refreshed context before saving.
5. Save AU-11's draft. Confirm the displayed saved version/author/time. Mark it
   reviewed for preparation and save again. Verify draft review counts change
   only after the successful request. Confirm the responsibility matrix and
   approved narratives have **not** changed yet.
6. Repeat review/correction for AU-2 and AU-6 using their own source context.
   Inspect evidence and narratives through the selected control link; content
   from another control must not be substituted.
7. For a provider draft, open its full capability review and use the reviewed,
   saved draft in the existing confirmation form. Independently verify actual
   provider coverage and customer duties, supply review notes, then confirm
   through the existing lifecycle. Customer duties exceeding that contract's
   2000-character limit must be summarized by the reviewer, never silently cut.
8. Verify the accepted allocation/customer duty/provider in the responsibility
   matrix. Preview the SSP and applicable eMASS preparation output. Existing
   approved narratives and implementation status remain preserved. Generating an
   export is not submission to eMASS or an authorization decision.
9. Repeat as a read-only user and in another tenant/system: draft mutation and
   placement actions must not bypass server permissions. Use two review sessions
   to save the same version: one may succeed; the other must show a conflict.
   Refresh/compare rather than overwriting the other reviewer.
10. Use browser request blocking/offline mode for save and AI preparation.
    Failed requests must display errors, retain corrections and never claim saved
    or reviewed. Change source/scope in a separate authorized session and confirm
    prior draft assumptions become stale without loss of saved history.
11. Test keyboard-only access, focus return/Escape and 320/390px widths. The
    right-side panel must not clip horizontally.

## Checks

```bash
dotnet build Ato.Copilot.sln
dotnet test Ato.Copilot.sln
cd src/Ato.Copilot.Dashboard
npx tsc -b --pretty false
npm test -- --run \
  src/__tests__/workspaces/SystemCapabilityList.test.tsx \
  src/__tests__/workspaces/ResponsibilityDraftEditor.test.tsx \
  src/__tests__/workspaces/SystemCapabilityResponsibility.test.tsx \
  src/__tests__/workspaces/SystemCapabilityDetail.test.tsx
PLAYWRIGHT_BASE_URL=http://localhost:5197 \
  npx playwright test e2e/tests/applied-capabilities.spec.ts
```

The TypeScript package has no `typecheck` script; its actual build/type-check
command is `tsc -b`. Only the dashboard TS project was changed.
The build's existing warning messages were compared against an isolated
`145446f0` baseline and matched. The production Vite build retains its existing
large-chunk warning; it was not suppressed or "fixed" as unrelated work.
The initial full-suite attempt encountered SQLite "database or disk is full";
disk-dependent tests passed when task-owned scratch files were placed on the
available repository volume. Full-suite results must be reported separately
from targeted successes and SQL Server-dependent skips.

### Verified session results

- Required final solution rebuild completed successfully: **0 errors, 144
  existing warnings**, matching the clean `145446f0` baseline warning messages.
  The final incremental confirmation build passed with **0 warnings and 0 errors**.
  No zero-warning clean baseline or new-warning suppression is claimed.
- Complete final solution tests passed with task scratch on the available
  repository filesystem, sequential projects and isolated xUnit collections:
  **7,899 unit tests passed; 1,816 integration tests passed; 20 integration tests
  skipped**. Subsequent targeted closure/coverage tests: **56 passed**, including
  the additional permission-revocation and known-narrative checks.
- Real SQL Server additive draft schema preservation, server workspace roles,
  simultaneous draft saves and canonical matrix/SSP/eMASS output checks passed
  in a separate required-Docker run: 7 tests passed with no skips.
- Final complete dashboard suite: **2,969 tests passed across 309 files** with
  `npm test -- --maxWorkers=1`. The exact source-readiness wait, source-qualified
  operation fixture and saved-provenance validation were corrected rather than
  weakening assertions. `npx tsc -b --pretty false` and the production build passed.
- All **18 Chromium capability scenarios passed against the final nginx/MCP
  images on 5197**. This includes panel and system-workflow WCAG checks, keyboard
  access, selected controls, preserved corrections, provider/org placement and
  recovery, stale removal, read-only actors, protected evidence, and light/dark
  desktop/mobile layouts. The panel also passed 320px no-clipping checks.
- The final touched-UI coverage run measured **97.57% executable lines and 81.7%
  branches**. All four new UI modules measured **100% executable-line coverage**.
  Existing unchanged paths in the touched detail/list modules account for the
  remaining full-file lines; full-file coverage is not misrepresented as 100%.
  The two new backend preparation/persistence implementations measured **100%
  executable-line coverage (87/87 and 93/93 lines)**. The coverage UI run passed
  all 80 selected tests. Branch coverage is reported separately, not represented
  as 100%.
- Earlier broad runs exposed loading-race assertions, outdated task headings,
  source-incompatible fixtures and measured contrast failures in this workflow.
  Their correction is included in the final passing runs. No test thresholds
  were increased, failures swallowed, accessibility rules disabled or assertions
  skipped to obtain those results.
- Full .NET attempts also exposed process-wide allocation-budget interference
  from concurrent test collections and a collector/binary instrumentation
  collision. Rebuild uninstrumented binaries before the final complete run and
  do not run a .NET coverage collector concurrently with a build or another
  suite reading the same outputs. Use the supported xUnit collection isolation
  setting for a trustworthy allocation-budget measurement:

  ```bash
  dotnet test Ato.Copilot.sln --no-build -m:1 \
    -- xUnit.ParallelizeTestCollections=false
  ```

  This preserves all tests and their original performance budgets. Dedicated
  simultaneous-request tests still create and verify their concurrent requests.

The requested port is now active and the complete manual walkthrough is available
before user acceptance. Human review of real source records remains required;
automated fixture scenarios do not stand in for that review.

## Rollback

1. Stop only this preview project with
   `docker compose -f docker-compose.capability-review.yml down` (no `-v`).
   Do not stop the main stack or another worktree's servers.
2. Redeploy the previous approved image/revision. Draft storage additions are
   additive; keep both draft tables, original sources, confirmations and narrative
   history. Older code does not consume these draft rows.
3. Do not delete draft history or rewind the shared database. Any accepted
   responsibility changes require the existing audited correction workflow;
   reverting the UI is not a data rollback.
4. Local acceptance is pending the user's walkthrough. No commit, push or GitHub
   issue write was performed for this implementation.
