# Feature Specification: CI/CD Hardening

**Feature Branch**: `051-cicd-hardening`
**Created**: 2026-06-03
**Status**: Draft
**Epic Issues**: #136 (Integration Tests), #137 (VS Code Mocha), #138 (M365 Mocha),
#139 (Playwright E2E), #140 (ATO Compliance Gate Fix)

## Background

The current CI pipeline (`.github/workflows/ci.yml`) runs three jobs:

1. **`dotnet-build-test`** — builds the .NET solution and runs the unit-test project
   (`tests/Ato.Copilot.Tests.Unit`). Integration tests in
   `tests/Ato.Copilot.Tests.Integration` are **never executed**.
2. **`vscode-extension-compile`** — compiles the VS Code extension via
   `npm run compile`. Tests in `extensions/vscode/test/suite/` are
   **never executed**.
3. **`ato-compliance-gate`** — calls the composite action
   `.github/actions/ato-compliance-gate` which POSTs to
   `http://localhost:5000` (or `vars.ATO_MCP_SERVER_URL`). **The MCP server
   never starts in CI**, so every `curl` call silently falls back to
   `{"findings": []}`, the gate trivially passes, and the
   `fail-on-error: 'true'` flag in `ci.yml` is effectively a no-op.

Two additional extension packages are never exercised in CI at all:

- **`extensions/m365/`** — a standalone Teams Declarative Agent. Its
  `mocha` test suite (`npm test`) is never invoked.
- **`src/Ato.Copilot.Dashboard/`** — a Vite/React SPA that ships a
  Playwright E2E suite under `e2e/`. The suite is never run.

The combined effect is that:

- Feature regressions in the integration layer (EF Core + SQLite,
  multi-tenant wiring, endpoint contracts) are **invisible until local
  dev or production**.
- TypeScript regressions in the VS Code and M365 extensions survive `tsc`
  but **runtime logic failures are undetected**.
- The ATO compliance gate provides **false assurance**: it marks PRs green
  even when the MCP server is unreachable, defeating its entire purpose.
- E2E regressions in the dashboard are entirely **invisible in CI**.

This epic closes all four gaps with five targeted issues.

### Verified CI State (current `main`)

| Job | What runs | What is missing |
|---|---|---|
| `dotnet-build-test` | Build + unit tests | Integration tests (`Tests.Integration`) |
| `vscode-extension-compile` | `tsc` compile only | `npm test` (mocha via `node ./dist/test/runTests.js`) |
| `ato-compliance-gate` | curl to `localhost:5000` (always 404) | Live or stubbed MCP server |
| *(absent)* | — | M365 mocha suite |
| *(absent)* | — | Playwright E2E smoke |

### Key Infrastructure Facts

- **Integration test project** (`tests/Ato.Copilot.Tests.Integration`):
  uses `Microsoft.AspNetCore.Mvc.Testing` + `Microsoft.EntityFrameworkCore.InMemory`
  for most tests; `Testcontainers.MsSql` for Row-Level Security tests
  (gated with `Skip.IfNot` on Docker availability).
- **VS Code extension test script**: `"test": "node ./dist/test/runTests.js"`.
  Requires `pretest: npm run compile` already defined.
- **M365 extension test script**: `"test": "mocha"`. Uses `.mocharc.js`
  pointing at `test/**/*.test.ts` with `ts-node/register`.
- **Playwright config** (`src/Ato.Copilot.Dashboard/playwright.config.ts`):
  `testDir: './e2e'`, `baseURL: process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:5173'`,
  `projects: [{ name: 'chromium' }]`, `workers: 1`, `retries: 2` (in CI),
  `globalSetup: './e2e/global-setup.ts'`.
- **Docker Compose** (`docker-compose.mcp.yml`): services `sqlserver` (port 1433),
  `ato-copilot` / `ato-copilot-mcp` (port 3001), `ato-chat` (port 5001),
  `ato-dashboard` (port 5173→8080). MCP server healthcheck: `curl -f http://localhost:3001/health`.
- **ATO Compliance Gate action** (`action.yml`): composite action,
  `fail-on-error` defaults to `'false'` in the action itself, but
  `ci.yml` overrides to `'true'`. The gate POSTs to
  `${{ inputs.mcp-server-url }}/api/tools/compliance_scan_iac` — if
  the MCP server is unreachable, `curl` returns `{"findings": []}` and
  the gate trivially passes.

## Clarifications

### Session 2026-06-03

- **Q: Should integration tests use SQLite (in-process) or spin up SQL Server
  via Testcontainers?**
  **A:** In-process SQLite for the standard integration suite (already supported
  by `Microsoft.EntityFrameworkCore.InMemory` + the existing
  `MultiTenantWebApplicationFactory`). The Testcontainers-MsSql RLS tests
  remain gated with `Skip.IfNot` since Docker-in-Docker adds significant
  complexity and run time. A follow-up issue can enable Docker-in-Docker
  for RLS coverage.

- **Q: Should the VS Code extension tests run in a headless Electron (full
  `@vscode/test-electron` path) or a plain Node environment?**
  **A:** Full `@vscode/test-electron` path using `xvfb-run`. The extension
  test runner (`dist/test/runTests.js`) bootstraps the VS Code extension host;
  skipping that would produce misleading coverage over mocked VS Code APIs.

- **Q: For the ATO Compliance Gate fix — which approach: (a) mock/stub mode,
  (b) `fail-on-error: false` analysis-only, or (c) spin up the MCP container?**
  **A:** See the full trade-off analysis in [research.md § 3](./research.md).
  **Selected approach: Option (b) + partial (a).** The workflow will set
  `fail-on-error: 'false'` and `respect-risk-acceptances: 'false'` so the
  gate runs static IaC analysis (file discovery + severity bucketing) without
  hitting the MCP server. This is honest: it produces real scan annotations
  on PRs without silently swallowing MCP errors. Option (c) is tracked as
  a future enhancement in [research.md § 3.3](./research.md).

- **Q: Should Playwright run on every PR or only on `main`?**
  **A:** Every PR, but smoke-tagged tests only (`--grep @smoke`). Full E2E
  suite is a follow-on task. The smoke subset should run in under 3 minutes
  on `ubuntu-latest`.

- **Q: M365 mocha — does it require a running server?**
  **A:** No. The M365 test suite uses `sinon` stubs for HTTP calls. The mocha
  runner executes in plain Node with `ts-node/register` — no server needed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Integration tests run on every PR (#136) (Priority: P1)

**As a** backend engineer
**I want** the integration-test suite to run automatically on every PR
**So that** regressions in endpoint contracts, EF Core wiring, and
multi-tenant isolation are caught before merge.

**Why this priority**: P1 because the integration suite is the only
automated check of the HTTP API contract. Unit tests mock the data layer;
integration tests exercise the real EF Core + SQLite path end-to-end.
Without them, breaking changes to endpoint shapes or middleware ordering
go undetected until staging.

**Independent Test**: Add the `integration-tests` job to `ci.yml`, push a
PR, verify the job appears in the status checks, and confirm it passes
with the existing suite (currently 510+ tests, ~146 pre-existing failures
that are test-order flakes unrelated to this issue).

**Acceptance**

- A new `integration-tests` CI job runs `tests/Ato.Copilot.Tests.Integration`
  on every PR and push to `main`.
- The job uses `dotnet test` with `--logger trx` and uploads a `.trx` artifact.
- The job sets `ASPNETCORE_ENVIRONMENT=Testing` and does NOT require a
  running SQL Server or Docker daemon (InMemory/SQLite path only).
- Tests guarded by `Skip.IfNot(DockerAvailable)` are silently skipped
  (existing behaviour via `Xunit.SkippableFact`).
- The job does NOT replace the existing `dotnet-build-test` job; it depends
  on it (needs: `dotnet-build-test`).
- A `.trx` artifact named `integration-test-results` is uploaded even if
  the job fails (`if: always()`).

### User Story 2 — VS Code extension tests run on every PR (#137) (Priority: P1)

**As a** VS Code extension engineer
**I want** the Mocha test suite to execute in CI
**So that** runtime logic regressions (e.g., broken MCP client, malformed
chat participant handler) are caught — not just TypeScript compile errors.

**Acceptance**

- The existing `vscode-extension-compile` job is **extended** (not replaced)
  with a second step that executes `npm test` after `npm run compile`.
- The step runs under `xvfb-run` so the VS Code extension host can render
  a headless GUI.
- The step uses the env var `CI=true` which the VS Code test runner already
  reads to suppress interactive prompts.
- Test results are uploaded as a `vscode-test-results` artifact.
- A job failure on test failure is the correct outcome (no `continue-on-error`).

### User Story 3 — M365 extension tests run on every PR (#138) (Priority: P1)

**As a** M365 extension engineer
**I want** the Mocha test suite for the Teams Declarative Agent to run in CI
**So that** handler and adapter regressions are caught before merge.

**Acceptance**

- A new `m365-extension-test` CI job runs in `extensions/m365/`.
- Steps: checkout → `setup-node@v4` (Node 20, cache: npm) → `npm ci` →
  `npm run build` (compile TypeScript) → `npm test` (mocha).
- The job runs on `ubuntu-latest` and does NOT require a server.
- Job failure on test failure is the correct outcome.
- No artifact upload is required (mocha outputs to console; future enhancement).

### User Story 4 — Playwright E2E smoke runs on every PR (#139) (Priority: P2)

**As a** dashboard engineer
**I want** a Playwright smoke suite to run on every PR
**So that** critical UI flows (login, navigation, compliance scan initiation)
are exercised before merge.

**Why this priority**: P2 because it requires the dashboard dev server to
be built and started, which adds ~2-3 minutes of job overhead. The smoke
subset keeps it bounded.

**Acceptance**

- A new `playwright-e2e` CI job runs after the `dotnet-build-test` job.
- The job: installs Playwright browsers (`npx playwright install --with-deps chromium`),
  starts the Vite dev server in the background, waits for it to be ready
  (via `wait-on` or health poll), then runs
  `npx playwright test --grep @smoke`.
- `PLAYWRIGHT_BASE_URL` is set to `http://localhost:5173`.
- The dashboard MCP server URL is pointed at a mock or the job sets
  `VITE_API_URL=http://localhost:3001` and the job gracefully handles
  MCP being unavailable (smoke tests stub API calls).
- HTML report + screenshots are uploaded as `playwright-report` artifact
  on failure (`if: failure()`).
- The job runs `ubuntu-latest` with `retries: 2` (already in the Playwright
  config for CI).

### User Story 5 — ATO Compliance Gate produces honest results (#140) (Priority: P1)

**As a** security reviewer
**I want** the ATO Compliance Gate job to either perform real IaC analysis
or clearly report that the MCP server was unavailable
**So that** a green compliance gate actually means something.

**Why this priority**: P1 because the current state actively produces
false assurance: the gate marks PRs green even when it cannot reach
the MCP server, which is its primary purpose.

**Acceptance**

- The `ci.yml` `ato-compliance-gate` job is updated so that:
  - `mcp-server-url` is still configurable via `vars.ATO_MCP_SERVER_URL`
    but the default is removed from the workflow (the action default of
    `localhost:5000` is superseded).
  - `fail-on-error` is changed from `'true'` to `'false'` so that
    MCP-server unreachability does not cause a false-positive CI failure.
  - `respect-risk-acceptances` is set to `'false'` so the action
    performs static-only analysis and does not attempt the second
    `curl` call to `/api/tools/compliance_check_risk_acceptance`.
- The action's `action.yml` default for `fail-on-error` is corrected from
  `'false'` to `'true'` so that future callers who do supply a live MCP URL
  get fail-fast behaviour by default.
- A `SCAN_NOTES` step summary annotation is added to clearly state whether
  the scan ran in "static-only" or "live MCP" mode.
- The existing `scan-paths: src,scripts,docs` and
  `severity-threshold: '2'` are retained.

### Edge Cases

- **Integration tests: InMemory vs. SQLite provider divergence.** Some
  integration tests rely on SQLite-specific behaviour (transaction semantics,
  `PRAGMA` settings). The CI job must set the `ATO_DATABASE__PROVIDER=InMemory`
  or `Sqlite` environment variable consistent with how
  `MultiTenantWebApplicationFactory` selects the provider. If the factory
  already reads `ASPNETCORE_ENVIRONMENT=Testing` to select InMemory,
  no additional env var is needed.
- **VS Code tests: xvfb unavailable.** If `xvfb-run` is not available on
  the runner, the VS Code extension host cannot start. The job must
  `apt-get install -y xvfb` before invoking `npm test`, or use the
  `Xvfb` GitHub Action.
- **VS Code tests: compiled output stale.** `pretest: npm run compile` in
  `package.json` already ensures recompilation before the test run, so no
  separate compile step is needed before `npm test`.
- **M365 mocha: missing `ts-node` in PATH.** `ts-node/register` requires
  `ts-node` installed as a dev dependency (confirmed in `package.json`).
  `npm ci` is sufficient; no global install needed.
- **Playwright: Vite dev server startup race.** If `npx playwright test`
  starts before the dev server is ready, all tests fail with
  `ECONNREFUSED`. The CI step must use `wait-on http://localhost:5173`
  (or equivalent) with a 60-second timeout before executing Playwright.
- **Playwright: dashboard requires MCP server.** The Vite dev server may
  attempt API calls on load. Smoke tests must either (a) stub the API via
  `page.route()` or (b) accept that the dashboard renders a "disconnected"
  state and only test navigation/layout. The first smoke tests should be
  written for (b) to unblock CI without requiring a live MCP server.
- **Compliance Gate: `vars.ATO_MCP_SERVER_URL` not set.** When the
  repository variable is absent, the workflow expression
  `${{ vars.ATO_MCP_SERVER_URL || '' }}` evaluates to an empty string.
  The action must handle an empty `mcp-server-url` by skipping the MCP
  curl calls entirely (risk-acceptance check already guarded by
  `respect-risk-acceptances`; the scan step should short-circuit on
  empty URL).
- **Compliance Gate: jq not installed on runner.** The action uses `jq`
  for JSON parsing. `ubuntu-latest` runners include `jq` pre-installed,
  but this should be documented as a runner prerequisite.
- **Concurrency cancellation mid-job.** The existing `concurrency` block
  (`cancel-in-progress: true`) may cancel a running integration or Playwright
  job when a new push arrives. Artifacts from cancelled runs are not uploaded.
  This is acceptable — the latest push's run is the one that matters.
- **Integration tests: test-order flakes.** The integration suite has
  ~143–146 pre-existing test-order flakes (verified in Feature 050 T049).
  These must NOT be counted as new failures introduced by this epic. The
  job should record and upload the `.trx` but must not inflate the reported
  new-failure count.

## Functional Requirements

- **FR-001** — A new `integration-tests` job MUST be added to
  `.github/workflows/ci.yml`, gated on `needs: dotnet-build-test`, running
  `dotnet test tests/Ato.Copilot.Tests.Integration` with `--logger trx`
  and uploading the `.trx` artifact.

- **FR-002** — The existing `vscode-extension-compile` job MUST be extended
  to execute `npm test` (the VS Code extension Mocha runner) after the
  compile step, under `xvfb-run`.

- **FR-003** — A new `m365-extension-test` job MUST be added that runs
  `npm run build && npm test` in `extensions/m365/`.

- **FR-004** — A new `playwright-e2e` job MUST be added that installs
  Playwright + Chromium, starts the Vite dev server, waits for readiness,
  and runs `npx playwright test --grep @smoke`.

- **FR-005** — The `ato-compliance-gate` job in `ci.yml` MUST be updated
  to pass `fail-on-error: 'false'` and `respect-risk-acceptances: 'false'`
  so the gate performs static IaC analysis without requiring a live MCP server.

- **FR-006** — The `action.yml` default for `fail-on-error` MUST be
  corrected from `'false'` to `'true'` so future callers with a live MCP
  URL get fail-fast behaviour.

- **FR-007** — Every new CI job MUST upload its test artifacts under a
  distinct artifact name (no collisions with existing `unit-test-results`).

- **FR-008** — No existing CI job MUST be removed or have its current
  passing behaviour broken by this epic.

- **FR-009** — The `playwright-e2e` job MUST set `PLAYWRIGHT_BASE_URL`
  and MUST upload an HTML report on failure.

- **FR-010** — The integration-tests job MUST set `ASPNETCORE_ENVIRONMENT=Testing`
  to ensure the test factory selects the InMemory/SQLite provider.

## Out of Scope

- Running Testcontainers SQL Server RLS tests in CI (Docker-in-Docker — tracked separately).
- Publishing a `.vsix` extension package from CI.
- Deploying the dashboard or MCP server as part of CI.
- Option (c) of the compliance gate fix (spinning up the full MCP container in CI).
- Adding new E2E tests — this epic only wires up the runner for existing/new smoke tests.
- Code coverage reporting or enforcement thresholds.
- Windows or macOS runner variants (all new jobs use `ubuntu-latest`).
