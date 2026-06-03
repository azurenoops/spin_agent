# Phase 1: CI Job Contracts — CI/CD Pipeline Hardening

**Branch**: `053-cicd-hardening`
**Date**: 2026-06-03

This document defines the interface contract for each new or modified CI job introduced by Feature 053. All jobs run on `ubuntu-latest` and are defined in `.github/workflows/ci.yml`.

---

## Job Inventory

| Job ID | Type | FR | GitHub Issue | Status |
|---|---|---|---|---|
| `dotnet-integration-tests` | NEW | FR-001 | #136 | Defined below |
| `vscode-extension-test` | NEW | FR-002 | #137 | Defined below |
| `m365-extension-test` | NEW | FR-003 | #138 | Defined below |
| `dashboard-e2e-smoke` | NEW | FR-004 | #139 | Defined below |
| `ato-compliance-gate` | MODIFY | FR-005 | #140 | Defined below |

All five job groups run in **parallel** — no cross-job `needs:` dependencies between them. Each job passes or fails independently.

---

## Job 1: `dotnet-integration-tests`

**Functional Requirement**: FR-001
**GitHub Issue**: #136

### Trigger

Runs on every `push` and `pull_request` event targeting `main` (same triggers as the existing build job).

### Runner

`ubuntu-latest`

### Inputs

| Input | Source | Value |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Job `env:` block | `Test` |
| `ConnectionStrings__DefaultConnection` | Job `env:` block or `appsettings.Test.json` | `Data Source=/tmp/ato-copilot-test.db` |
| .NET SDK version | `global.json` | 9.0.x (resolved by `actions/setup-dotnet`) |

### Steps

1. `actions/checkout@v4` — check out the branch.
2. `actions/setup-dotnet` with version from `global.json`.
3. `dotnet restore Ato.Copilot.sln`
4. `dotnet build Ato.Copilot.sln --configuration Release --no-restore`
5. `dotnet test tests/Ato.Copilot.Tests.Integration/ --configuration Release --no-build --logger "github;verbosity=normal"`

### Outputs

None (the job produces no artifact; pass/fail is the only output).

### Pass Criteria

- `dotnet test` exits with code 0.
- All test cases in `tests/Ato.Copilot.Tests.Integration/` execute (≥ 80 test cases across ≥ 80 files).
- Job completes within `timeout-minutes: 5`.

### Fail Criteria

- `dotnet test` exits with a non-zero code (one or more test cases failed or errored).
- Job exceeds `timeout-minutes: 5` (GitHub Actions cancels the job and marks it failed).
- `dotnet build` fails (build error in the integration test project or its dependencies).

### Side Effects

- Creates an ephemeral SQLite database file at `/tmp/ato-copilot-test.db` on the runner.
- The runner workspace is discarded at job end; no artifacts persist.

---

## Job 2: `vscode-extension-test`

**Functional Requirement**: FR-002
**GitHub Issue**: #137

### Trigger

Same as Job 1.

### Runner

`ubuntu-latest`

### Inputs

| Input | Source | Value |
|---|---|---|
| Node.js version | `extensions/vscode/package.json` `engines.node` | 20.x (resolved by `actions/setup-node`) |
| xvfb | System package | Installed via `sudo apt-get install -y xvfb` |
| VS Code binary | `@vscode/test-electron` | Downloaded to `~/.vscode-test/` at runtime |

### Steps

1. `actions/checkout@v4`
2. `actions/setup-node` with Node.js version from `extensions/vscode/package.json`.
3. `sudo apt-get install -y xvfb` — install the headless display server.
4. `cd extensions/vscode && npm ci` — install extension dependencies.
5. `cd extensions/vscode && npm run compile` — compile TypeScript to `out/`.
6. `cd extensions/vscode && xvfb-run node out/test/runTests.js` — run 16 test files headlessly.

### Outputs

None.

### Pass Criteria

- `xvfb-run node out/test/runTests.js` exits with code 0.
- All 16 test files in `extensions/vscode/src/test/` execute.
- Job completes within `timeout-minutes: 3`.

### Fail Criteria

- `xvfb-run node out/test/runTests.js` exits with a non-zero code (one or more Mocha tests failed).
- `npm run compile` fails (TypeScript compilation error).
- Job exceeds `timeout-minutes: 3`.

### Side Effects

- VS Code binary is downloaded to `~/.vscode-test/` on the runner (cached by `@vscode/test-electron`). The runner workspace is discarded; no artifacts persist.

---

## Job 3: `m365-extension-test`

**Functional Requirement**: FR-003
**GitHub Issue**: #138

### Trigger

Same as Job 1.

### Runner

`ubuntu-latest`

### Inputs

| Input | Source | Value |
|---|---|---|
| Node.js version | `extensions/m365/package.json` `engines.node` | 20.x (resolved by `actions/setup-node`) |

### Steps

1. `actions/checkout@v4`
2. `actions/setup-node` with Node.js version from `extensions/m365/package.json`.
3. `cd extensions/m365 && npm ci` — install M365 extension dependencies.
4. `cd extensions/m365 && npm test` — run all 15 Mocha suites.

### Outputs

None.

### Pass Criteria

- `npm test` exits with code 0.
- All 15 Mocha test suites in `extensions/m365/` execute and pass.
- Job completes within `timeout-minutes: 10` (no explicit SLO in spec; 10 min is a conservative ceiling).

### Fail Criteria

- `npm test` exits with a non-zero code (one or more Mocha tests failed).
- `npm ci` fails (dependency resolution error).

### Side Effects

None beyond the runner workspace, which is discarded at job end.

---

## Job 4: `dashboard-e2e-smoke`

**Functional Requirement**: FR-004
**GitHub Issue**: #139

### Trigger

Same as Job 1.

### Runner

`ubuntu-latest`

### Inputs

| Input | Source | Value |
|---|---|---|
| Docker Compose file | Repository | `docker-compose.mcp.yml` |
| Playwright config | Repository | `src/Ato.Copilot.Dashboard/e2e/playwright.config.ts` |
| Node.js version | `src/Ato.Copilot.Dashboard/package.json` `engines.node` | 20.x (resolved by `actions/setup-node`) |
| Test subset | CLI argument | `tests/0[1-4]*` (tests 01–04 only) |

### Steps

1. `actions/checkout@v4`
2. `actions/setup-node` with Node.js version from Dashboard `package.json`.
3. `docker compose -f docker-compose.mcp.yml up -d --build` — start the full stack in the background.
4. Health-check loop — poll `http://localhost:<MCP_PORT>/health` until HTTP 200 or timeout 120 s.
5. `cd src/Ato.Copilot.Dashboard && npm ci` — install Dashboard dependencies (includes Playwright).
6. `npx playwright install --with-deps chromium` — install Chromium browser for Playwright.
7. `npx playwright test tests/0[1-4] --config e2e/playwright.config.ts` — run smoke tests 01–04.
8. `docker compose -f docker-compose.mcp.yml down -v` — tear down stack and remove volumes (always runs, even on failure).

### Outputs

- Playwright HTML report artifact (uploaded via `actions/upload-artifact` on failure for debugging).

### Pass Criteria

- Playwright exits with code 0.
- Tests 01, 02, 03, and 04 all pass in the Chromium browser.
- Job completes within `timeout-minutes: 8`.

### Fail Criteria

- Playwright exits with a non-zero code (one or more smoke tests failed or timed out).
- Health-check loop times out (docker-compose stack did not reach healthy state within 120 s).
- Job exceeds `timeout-minutes: 8`.

### Side Effects

- Docker-compose creates a network, containers, and volumes on the runner.
- Step 8 (`docker compose down -v`) always runs (via `if: always()`) to ensure cleanup even on failure.
- No artifacts persist after teardown.

---

## Job 5: `ato-compliance-gate` (MODIFIED)

**Functional Requirement**: FR-005
**GitHub Issue**: #140

### Change Description

The existing `ato-compliance-gate` job already exists in `ci.yml`. Feature 053 changes **one field only**: the `mcp-server-url` input passed to the compliance gate action step.

**Before** (incorrect value — static URL that does not resolve in CI):
```yaml
mcp-server-url: <old value>
```

**After** (correct value — docker-compose health endpoint on the runner's localhost):
```yaml
mcp-server-url: http://localhost:5000/health
```

> Adjust the port to match the actual host-port mapping declared in `docker-compose.mcp.yml` for the MCP service. Verify with `docker compose -f docker-compose.mcp.yml ps` while the stack is running.

### Inputs (after fix)

| Input | Source | Value |
|---|---|---|
| `mcp-server-url` | YAML literal | `http://localhost:5000/health` |
| All other inputs | Unchanged | (existing values preserved) |

### Outputs

Unchanged from the current job definition.

### Pass Criteria

- The compliance gate action successfully reaches the MCP server at `mcp-server-url`.
- All compliance checks pass.
- Job exits with code 0.

### Fail Criteria

- The compliance gate action cannot reach `mcp-server-url` (connection refused, timeout, or non-2xx health response).
- One or more compliance checks fail.

### Side Effects

The compliance gate job must run **after** the docker-compose stack is healthy. If the gate runs in parallel with `dashboard-e2e-smoke` (which also starts docker-compose), a shared compose stack may conflict. **Resolution**: the `ato-compliance-gate` job adds `needs: [dashboard-e2e-smoke]` **only if** it requires the same docker-compose stack — otherwise it starts its own stack independently. This detail is resolved in T006 by reading the current job definition and the compose file port mapping.

---

## Cross-Job Summary

| Property | `dotnet-integration-tests` | `vscode-extension-test` | `m365-extension-test` | `dashboard-e2e-smoke` | `ato-compliance-gate` |
|---|---|---|---|---|---|
| Runner | ubuntu-latest | ubuntu-latest | ubuntu-latest | ubuntu-latest | ubuntu-latest |
| Timeout | 5 min | 3 min | 10 min | 8 min | (existing) |
| DB dependency | SQLite (ephemeral) | None | None | docker-compose | docker-compose |
| Network calls | None | None | None | MCP + Dashboard | MCP health endpoint |
| Artifacts | None | None | None | Playwright report (on fail) | None |
| Parallel | Yes | Yes | Yes | Yes | Possibly `needs: e2e` |
