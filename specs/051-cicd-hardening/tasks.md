# Tasks: CI/CD Hardening

**Feature**: 051-cicd-hardening
**Branch**: `051-cicd-hardening`
**Input artifacts**: [plan.md](./plan.md), [spec.md](./spec.md),
[research.md](./research.md), [data-model.md](./data-model.md),
[contracts/ci-jobs.md](./contracts/ci-jobs.md), [quickstart.md](./quickstart.md)

**Issues**: #136 (Integration Tests), #137 (VS Code Mocha), #138 (M365 Mocha),
#139 (Playwright E2E), #140 (ATO Compliance Gate Fix)

**Tests**: REQUIRED per Constitution § VI (TDD). Every workflow change must
be validated with a local `act` dry-run or equivalent before committing.
CI-config tasks are the "tests" in this context — each must be verified green
before the next phase begins.

**Organization**: Tasks are grouped by phase and user story. Issues #136–#138
(integration + extension tests) are P1 and can proceed in parallel after
Phase 1. Issue #140 (compliance gate fix) is P1 and is independent of all
other issues. Issue #139 (Playwright) is P2 and depends on knowing the
dashboard E2E baseline.

## Format: `[ID] [P?] [#Issue] Description with file path`

- **[P]**: Different files; no dependency on incomplete tasks; safe to run in parallel.
- **[#NNN]**: GitHub issue reference.

## Path Conventions

- **CI Workflow**: `.github/workflows/ci.yml`
- **CI Action**: `.github/actions/ato-compliance-gate/action.yml`
- **Backend tests**: `tests/Ato.Copilot.Tests.Integration/`
- **VS Code extension**: `extensions/vscode/`
- **M365 extension**: `extensions/m365/`
- **Dashboard**: `src/Ato.Copilot.Dashboard/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Housekeeping and validation of current state before any changes.

- [ ] T001 Create GitHub issues per Constitution § DevOps Issue Discipline —
  parent Epic 051 issue + 5 sub-issues (#136–#140) with parent linkage.
  **DEFERRED — requires explicit user approval before external GitHub writes.**

- [ ] T002 [P] Verify existing CI passes on current `main`: run
  `.github/workflows/ci.yml` locally via `act` or confirm the last
  green run in Actions. Document the baseline failure count for
  `tests/Ato.Copilot.Tests.Integration` (expected ~146 pre-existing
  test-order flakes). No files modified.

- [ ] T003 [P] Audit the `extensions/vscode/test/suite/` directory to confirm
  test files exist and that `node ./dist/test/runTests.js` can execute
  in a headless environment. Document any blockers in a comment on #137.
  No files modified.

---

## Phase 2: Integration Tests in CI (#136) — P1

**Goal**: `dotnet test tests/Ato.Copilot.Tests.Integration` runs on every PR,
uploads `.trx` artifacts, and gates on the `dotnet-build-test` job.

**Independent Test**: After T101, push a test PR and verify the
`integration-tests` job appears in the status checks and produces a
`integration-test-results` artifact.

### Tests for Phase 2 (write first; must fail before implementation)

- [ ] T101 [P] [#136] Dry-run validate the new `integration-tests` job in
  `.github/workflows/ci.yml` using `act -j integration-tests --dry-run`.
  Confirm the job YAML is syntactically valid and the `needs` array
  references `dotnet-build-test` correctly. This is the "failing test"
  for this task — a parse error or `act` failure means the job is not
  yet wired correctly.

### Implementation for Phase 2

- [ ] T102 [#136] Add the `integration-tests` job to
  `.github/workflows/ci.yml` after the `dotnet-build-test` job definition.
  Job spec:
  - `name: Integration Tests`
  - `runs-on: ubuntu-latest`
  - `needs: [dotnet-build-test]`
  - `env: ASPNETCORE_ENVIRONMENT: Testing`
  - Steps: checkout → setup-dotnet (9.0.x) → restore → build
    (`dotnet build Ato.Copilot.sln -c Release --no-restore -nologo`) →
    run integration tests
    (`dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj -c Release --no-build -nologo --logger trx`) →
    upload artifact (`integration-test-results`, path `tests/**/TestResults/*.trx`,
    `if: always()`).
  Per FR-001 / spec.md US1 acceptance.

- [ ] T103 [#136] Confirm the `MultiTenantWebApplicationFactory` (or equivalent
  in `tests/Ato.Copilot.Tests.Integration/`) already selects the InMemory
  or SQLite provider when `ASPNETCORE_ENVIRONMENT=Testing`. If it does not,
  update the factory in
  `tests/Ato.Copilot.Tests.Integration/Infrastructure/MultiTenantWebApplicationFactory.cs`
  (or equivalent) to read `ASPNETCORE_ENVIRONMENT` and skip SQL Server
  configuration when `Testing`. Per FR-010.

**Checkpoint**: Phase 2 complete — `integration-tests` appears in PR status
checks. Pre-existing flakes are visible but do not block the feature.

---

## Phase 3: VS Code Extension Mocha Tests in CI (#137) — P1

**Goal**: Extend the existing `vscode-extension-compile` job to execute
`npm test` (the `@vscode/test-electron` Mocha runner) under `xvfb-run`.

**Independent Test**: After T201, push a PR and verify the
`vscode-extension-compile` job succeeds with both compile and test steps,
and a `vscode-test-results` artifact is uploaded.

### Tests for Phase 3 (write first)

- [ ] T201 [P] [#137] Dry-run validate the updated `vscode-extension-compile`
  job (with the new `npm test` step) using `act -j vscode-extension-compile
  --dry-run`. Confirm the `xvfb-run` invocation and `CI=true` env var are
  present.

### Implementation for Phase 3

- [ ] T202 [#137] Extend the `vscode-extension-compile` job in
  `.github/workflows/ci.yml`:
  - Add a step before `npm run compile`: `Install xvfb` →
    `sudo apt-get update && sudo apt-get install -y xvfb`.
  - After the `Compile extension` step, add: `Run extension tests` →
    `xvfb-run --auto-servernum npm test` with `env: CI: 'true'`.
  - Add an `Upload VS Code test results` step (artifact name
    `vscode-test-results`, path `extensions/vscode/test-results/` or any
    `.trx`/`.xml` the runner emits, `if: always()`).
  Per FR-002 / spec.md US2 acceptance.

**Checkpoint**: Phase 3 complete — VS Code Mocha tests execute in CI on
every PR.

---

## Phase 4: M365 Extension Mocha Tests in CI (#138) — P1

**Goal**: Add a new `m365-extension-test` CI job that builds and tests the
M365 Teams Declarative Agent extension.

**Independent Test**: After T301, push a PR and verify the new
`m365-extension-test` job appears in status checks and passes.

### Tests for Phase 4 (write first)

- [ ] T301 [P] [#138] Dry-run validate the new `m365-extension-test` job
  using `act -j m365-extension-test --dry-run`.

### Implementation for Phase 4

- [ ] T302 [P] [#138] Add a new `m365-extension-test` job to
  `.github/workflows/ci.yml`:
  - `name: M365 Extension Test`
  - `runs-on: ubuntu-latest`
  - `needs: []` (no dependency — independent of dotnet jobs)
  - `defaults.run.working-directory: extensions/m365`
  - Steps: checkout → `setup-node@v4` (Node 20, cache: npm,
    `cache-dependency-path: extensions/m365/package-lock.json`) →
    `npm ci` → `npm run build` → `npm test`.
  Per FR-003 / spec.md US3 acceptance.

**Checkpoint**: Phase 4 complete — M365 Mocha tests run on every PR.
Phases 2, 3, and 4 are independent and may be developed in parallel [P].

---

## Phase 5: ATO Compliance Gate Fix (#140) — P1

**Goal**: Update the compliance gate so it runs honest static IaC analysis
without silently swallowing MCP-unreachability errors.

**Independent Test**: After T401 + T402, push a PR with a Terraform file
containing a known CAT II finding. Verify the gate runs, emits a
`::warning` annotation for the finding, sets `scan-status=pass` (since
`fail-on-error: false`), and the step summary clearly shows "static-only
mode".

### Tests for Phase 5 (write first)

- [ ] T401 [P] [#140] Dry-run validate the updated `ato-compliance-gate` job
  in `.github/workflows/ci.yml` using `act -j ato-compliance-gate --dry-run`.
  Confirm `fail-on-error: 'false'` and `respect-risk-acceptances: 'false'`
  are present in the `with:` block.

### Implementation for Phase 5

- [ ] T402 [#140] Update the `ato-compliance-gate` job in
  `.github/workflows/ci.yml`:
  - Change `fail-on-error: 'true'` → `fail-on-error: 'false'`.
  - Add `respect-risk-acceptances: 'false'`.
  - Change the `mcp-server-url` expression to
    `${{ vars.ATO_MCP_SERVER_URL || '' }}` (empty string when the
    repository variable is not set).
  Per FR-005 / spec.md US5 acceptance.

- [ ] T403 [#140] Update `.github/actions/ato-compliance-gate/action.yml`:
  - Correct the `fail-on-error` default from `'false'` → `'true'`.
  - Add handling in the `scan` step: when `inputs.mcp-server-url` is
    empty, skip the MCP `curl` calls entirely and set a step output
    `scan-mode=static-only`.
  - Add a step summary annotation: when `scan-mode=static-only`, append
    `> ⚠️ MCP server URL not configured — running in static-only mode.
    Risk acceptances were not checked.` to `$GITHUB_STEP_SUMMARY`.
  Per FR-005, FR-006 / spec.md US5 acceptance.

- [ ] T404 [P] [#140] Verify the `Evaluate gate` step in `action.yml` still
  correctly emits `status=pass` / `status=fail` based on `blocking-findings`
  when MCP is unavailable (static mode). Write a local bash unit test that
  sets `BLOCKING=1` and asserts `exit 1` is NOT reached when
  `fail-on-error=false`. No workflow file change required.

**Checkpoint**: Phase 5 complete — compliance gate runs on every PR and
produces meaningful annotations without false assurance.

---

## Phase 6: Playwright E2E Smoke (#139) — P2

**Goal**: Add a `playwright-e2e` CI job that installs Chromium, starts the
Vite dev server, and runs smoke-tagged E2E tests.

**Prerequisite**: At least one `@smoke`-tagged test must exist in
`src/Ato.Copilot.Dashboard/e2e/` before this job is wired up. If none
exist, T501 creates a skeleton smoke test.

**Independent Test**: After T502, push a PR and verify the `playwright-e2e`
job runs and uploads a `playwright-report` artifact on failure.

### Tests for Phase 6 (write first)

- [ ] T501 [P] [#139] Create (or verify existence of) at least one smoke test
  in `src/Ato.Copilot.Dashboard/e2e/smoke.spec.ts` tagged `@smoke`.
  Minimum viable test: navigate to `http://localhost:5173`, assert the page
  title matches `/ATO Copilot/i`. This test must pass when the Vite dev
  server is running locally (`PLAYWRIGHT_BASE_URL=http://localhost:5173`).

- [ ] T502 [P] [#139] Dry-run validate the new `playwright-e2e` job using
  `act -j playwright-e2e --dry-run`.

### Implementation for Phase 6

- [ ] T503 [#139] Add a new `playwright-e2e` job to
  `.github/workflows/ci.yml`:
  - `name: Playwright E2E Smoke`
  - `runs-on: ubuntu-latest`
  - `needs: [dotnet-build-test]`
  - `defaults.run.working-directory: src/Ato.Copilot.Dashboard`
  - Steps:
    1. Checkout.
    2. `setup-node@v4` (Node 20, cache: npm, `cache-dependency-path:
       src/Ato.Copilot.Dashboard/package-lock.json`).
    3. `npm ci`.
    4. `npx playwright install --with-deps chromium`.
    5. Start Vite dev server in background:
       `npm run dev -- --port 5173 &` with `env: VITE_API_URL: ''` (or
       a stub base URL).
    6. Wait for readiness:
       `npx wait-on http://localhost:5173 --timeout 60000`.
    7. Run smoke tests:
       `npx playwright test --grep @smoke` with
       `env: PLAYWRIGHT_BASE_URL: http://localhost:5173, CI: 'true'`.
    8. Upload `playwright-report` artifact
       (`src/Ato.Copilot.Dashboard/playwright-report/`,
       `if: failure()`).
  Per FR-004, FR-009 / spec.md US4 acceptance.

- [ ] T504 [#139] Add `wait-on` to `src/Ato.Copilot.Dashboard/package.json`
  dev dependencies if not already present:
  `npm install --save-dev wait-on`. Commit `package.json` +
  `package-lock.json`.

**Checkpoint**: Phase 6 complete — Playwright smoke tests run on every PR;
HTML reports uploaded on failure.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, documentation, and PR prep.

- [ ] T601 [P] Run the full CI suite locally (or trigger on a draft PR) and
  verify all five new/updated jobs pass without introducing new failures.
  Record job run times; flag any job exceeding 10 minutes for optimization.

- [ ] T602 [P] Verify `act` dry-run for the complete `.github/workflows/ci.yml`
  with all jobs enabled. Confirm no YAML syntax errors, no duplicate job
  IDs, and correct `needs` dependency graph.

- [ ] T603 [P] Confirm the `concurrency` block (`cancel-in-progress: true`)
  correctly applies to all new jobs. No file change needed if the existing
  top-level block covers them.

- [ ] T604 [P] Update the repository README or `.github/CONTRIBUTING.md` to
  document the new CI jobs and their purposes, including the compliance gate
  "static-only mode" notice. File: `.github/CONTRIBUTING.md` (create if
  absent) or `README.md` (update CI section).

- [ ] T605 Constitution Check matrix in [plan.md](./plan.md) — add a
  Post-Implementation Re-Check section with verdicts for each row after
  all phases complete.

- [ ] T606 Commit and open PR — **NOT executed** per Constitution non-negotiable
  rule #9 ("Never push without permission"). Run `git add` + `git commit` and
  surface the PR description for review before any push.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies. T001 deferred (requires approval).
- **Phase 2 (#136)**: Independent. May start after Phase 1.
- **Phase 3 (#137)**: Independent [P] with Phase 2 — touches different job.
- **Phase 4 (#138)**: Independent [P] with Phases 2 + 3.
- **Phase 5 (#140)**: Independent [P] with Phases 2–4 — touches different job.
- **Phase 6 (#139)**: P2; may start after Phase 1. Requires T501 (smoke test exists).
- **Phase 7 (Polish)**: Depends on all prior phases completing.

### Parallel Opportunities

**Maximum parallel set (after Phase 1)**:

```text
T101 / T102 / T103  — integration-tests job         [P]
T201 / T202         — vscode-extension-compile job   [P]
T301 / T302         — m365-extension-test job        [P]
T401 / T402 / T403  — ato-compliance-gate fix        [P]
T501 / T502 / T503  — playwright-e2e job             [P, P2]
```

All five issue tracks write to different sections of `ci.yml` and different
action/extension directories. A single engineer can merge them sequentially;
a team of five can work fully in parallel.

---

## Task Summary

| Phase | Issue | Task count | Description |
|---|---|---|---|
| 1 | — | 3 (T001–T003) | Setup & baseline audit |
| 2 | #136 | 3 (T101–T103) | Integration tests |
| 3 | #137 | 2 (T201–T202) | VS Code Mocha |
| 4 | #138 | 2 (T301–T302) | M365 Mocha |
| 5 | #140 | 4 (T401–T404) | Compliance gate fix |
| 6 | #139 | 4 (T501–T504) | Playwright E2E smoke |
| 7 | — | 6 (T601–T606) | Polish + PR |
| **Total** | | **24** | |

**Suggested MVP scope**: Phases 1–5 (T001–T404, 14 tasks). Delivers the three
P1 issues (#136, #137, #138, #140). Phase 6 (Playwright) can ship in a
follow-up PR.
