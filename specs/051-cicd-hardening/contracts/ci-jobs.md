# CI Jobs Contract

**Feature**: 051-cicd-hardening
**File**: `.github/workflows/ci.yml` (+ `.github/actions/ato-compliance-gate/action.yml`)

This document is the authoritative specification for every CI job — both
existing and new. Use it as the source of truth when implementing tasks.

---

## Job Inventory

| Job ID | Name | Status | Issue |
|---|---|---|---|
| `dotnet-build-test` | Build + Unit Tests | Existing (unchanged) | — |
| `vscode-extension-compile` | VS Code Extension Compile + Test | Existing (extended) | #137 |
| `ato-compliance-gate` | ATO Compliance Gate | Existing (updated) | #140 |
| `integration-tests` | Integration Tests | **New** | #136 |
| `m365-extension-test` | M365 Extension Test | **New** | #138 |
| `playwright-e2e` | Playwright E2E Smoke | **New** | #139 |

---

## 1. `dotnet-build-test` (Existing — No Change)

```yaml
name: Build + Unit Tests
runs-on: ubuntu-latest
needs: []
```

### Environment Variables

| Variable | Value | Purpose |
|---|---|---|
| *(none)* | — | — |

### Steps

| Step | Action/Command | Notes |
|---|---|---|
| Checkout | `actions/checkout@v4` | |
| Setup .NET | `actions/setup-dotnet@v4` (9.0.x) | |
| Restore | `dotnet restore Ato.Copilot.sln` | |
| Build | `dotnet build Ato.Copilot.sln -c Release --no-restore -nologo` | |
| Run unit tests | `dotnet test tests/Ato.Copilot.Tests.Unit/... --logger trx` | |
| Upload results | `actions/upload-artifact@v4` | `if: always()` |

### Artifacts Produced

| Name | Path | Condition |
|---|---|---|
| `unit-test-results` | `tests/**/TestResults/*.trx` | `if: always()` |

### Required Secrets/Vars

None.

---

## 2. `integration-tests` (New — #136)

```yaml
name: Integration Tests
runs-on: ubuntu-latest
needs: [dotnet-build-test]
```

### Environment Variables

| Variable | Value | Source | Purpose |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Testing` | Job `env:` block | Signals `MultiTenantWebApplicationFactory` to use InMemory/SQLite provider |

### Steps

| Step | Action/Command | Notes |
|---|---|---|
| Checkout | `actions/checkout@v4` | |
| Setup .NET | `actions/setup-dotnet@v4` (9.0.x) | |
| Restore | `dotnet restore Ato.Copilot.sln` | Packages cached from upstream job via restore cache |
| Build | `dotnet build Ato.Copilot.sln -c Release --no-restore -nologo` | Must build because artifacts are not shared between jobs |
| Run integration tests | `dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj -c Release --no-build -nologo --logger trx` | `--no-build` since build just ran |
| Upload results | `actions/upload-artifact@v4` | `if: always()` |

### Artifacts Produced

| Name | Path | Condition |
|---|---|---|
| `integration-test-results` | `tests/**/TestResults/*.trx` | `if: always()` |

### Required Secrets/Vars

None. The `Testcontainers.MsSql` tests are skipped automatically when Docker
is unavailable (via `Xunit.SkippableFact`).

### Notes

- Pre-existing test-order flakes (~146) are expected. Do NOT gate the
  feature on zero failures — document the baseline and monitor for regressions.
- The job does NOT require a running SQL Server, Docker daemon, or external
  service of any kind.

---

## 3. `vscode-extension-compile` (Extended — #137)

```yaml
name: VS Code Extension Compile + Test
runs-on: ubuntu-latest
needs: []
defaults:
  run:
    working-directory: extensions/vscode
```

### Environment Variables

| Variable | Value | Source | Purpose |
|---|---|---|---|
| `CI` | `true` | Step `env:` on `Run extension tests` step | Headless mode for `@vscode/test-electron` |

### Steps (full sequence including new steps)

| Step | Action/Command | New? | Notes |
|---|---|---|---|
| Checkout | `actions/checkout@v4` | No | |
| Setup Node | `actions/setup-node@v4` (Node 20, cache: npm) | No | `cache-dependency-path: extensions/vscode/package-lock.json` |
| Install dependencies | `npm ci` | No | |
| **Install xvfb** | `sudo apt-get update && sudo apt-get install -y xvfb` | **Yes** | Must run before `npm test` |
| Compile extension | `npm run compile` | No | `tsc -p ./` |
| **Run extension tests** | `xvfb-run --auto-servernum npm test` | **Yes** | `env: CI: 'true'`; `pretest` hook recompiles automatically |
| **Upload VS Code test results** | `actions/upload-artifact@v4` | **Yes** | `if: always()` |

### Artifacts Produced

| Name | Path | Condition |
|---|---|---|
| `vscode-test-results` | `extensions/vscode/test-results/**` (or any `.xml`/`.trx` emitted by the test runner) | `if: always()` |

### Required Secrets/Vars

None.

### Input/Output Contract

- **Input**: compiled TypeScript output in `extensions/vscode/dist/`
- **Output**: test pass/fail exit code; optional JUnit XML
- **Test runner**: `node ./dist/test/runTests.js` (`@vscode/test-electron`)
- **Test spec**: `test/suite/**/*.test.ts` (via `.mocharc.js`)

---

## 4. `m365-extension-test` (New — #138)

```yaml
name: M365 Extension Test
runs-on: ubuntu-latest
needs: []
defaults:
  run:
    working-directory: extensions/m365
```

### Environment Variables

None required.

### Steps

| Step | Action/Command | Notes |
|---|---|---|
| Checkout | `actions/checkout@v4` | |
| Setup Node | `actions/setup-node@v4` (Node 20, cache: npm) | `cache-dependency-path: extensions/m365/package-lock.json` |
| Install dependencies | `npm ci` | Installs mocha, ts-node, sinon, chai |
| Build | `npm run build` | `tsc` — compiles TypeScript to `dist/` |
| Run tests | `npm test` | `mocha` — uses `.mocharc.js` (`spec: test/**/*.test.ts`, `require: ts-node/register`) |

### Artifacts Produced

None (mocha outputs to stdout). Future enhancement: add Mocha JUnit reporter
and upload artifact.

### Required Secrets/Vars

None. All HTTP calls in the test suite are stubbed with `sinon`.

### Input/Output Contract

- **Input**: TypeScript source in `extensions/m365/src/`
- **Output**: mocha pass/fail exit code to stdout
- **Test spec**: `test/**/*.test.ts` (via `.mocharc.js`)
- **No external services required**

---

## 5. `playwright-e2e` (New — #139)

```yaml
name: Playwright E2E Smoke
runs-on: ubuntu-latest
needs: [dotnet-build-test]
defaults:
  run:
    working-directory: src/Ato.Copilot.Dashboard
```

### Environment Variables

| Variable | Value | Source | Purpose |
|---|---|---|---|
| `PLAYWRIGHT_BASE_URL` | `http://localhost:5173` | Job `env:` block | Overrides `playwright.config.ts` `BASE_URL` |
| `CI` | `true` | Job `env:` block | Activates `retries: 2`, `forbidOnly`, `screenshot: only-on-failure` |
| `VITE_API_URL` | `''` | Step `env:` on `Start Vite dev server` step | Prevents dashboard from failing on missing MCP URL |

### Steps

| Step | Action/Command | Notes |
|---|---|---|
| Checkout | `actions/checkout@v4` | |
| Setup Node | `actions/setup-node@v4` (Node 20, cache: npm) | `cache-dependency-path: src/Ato.Copilot.Dashboard/package-lock.json` |
| Install dependencies | `npm ci` | Includes `wait-on` (added to devDependencies in T504) |
| Install Playwright browsers | `npx playwright install --with-deps chromium` | Chromium only; `--with-deps` installs system libraries |
| Start Vite dev server | `npm run dev -- --port 5173 &` | Background process; `env: VITE_API_URL: ''` |
| Wait for server ready | `npx wait-on http://localhost:5173 --timeout 60000` | Fails the job if server doesn't start within 60s |
| Run smoke tests | `npx playwright test --grep @smoke` | Runs only `@smoke`-tagged tests |
| Upload Playwright report | `actions/upload-artifact@v4` | `if: failure()` only |

### Artifacts Produced

| Name | Path | Condition |
|---|---|---|
| `playwright-report` | `src/Ato.Copilot.Dashboard/playwright-report/` | `if: failure()` |

### Required Secrets/Vars

None. Smoke tests must not require a live MCP server (see research.md § 5.3).

### Input/Output Contract

- **Input**: Vite dev server on `http://localhost:5173`
- **Output**: Playwright HTML report (on failure); exit code
- **Test spec**: `src/Ato.Copilot.Dashboard/e2e/**/*.spec.ts` tagged `@smoke`
- **Config**: `src/Ato.Copilot.Dashboard/playwright.config.ts`
- **Global setup**: `src/Ato.Copilot.Dashboard/e2e/global-setup.ts`

---

## 6. `ato-compliance-gate` (Updated — #140)

```yaml
name: ATO Compliance Gate
if: github.event_name == 'pull_request'
runs-on: ubuntu-latest
needs: []
```

### `with:` Inputs (post-fix)

| Input | Before | After | Notes |
|---|---|---|---|
| `scan-paths` | `src,scripts,docs` | `src,scripts,docs` | Unchanged |
| `severity-threshold` | `'2'` | `'2'` | Unchanged |
| `mcp-server-url` | `${{ vars.ATO_MCP_SERVER_URL \|\| 'http://localhost:5000' }}` | `${{ vars.ATO_MCP_SERVER_URL \|\| '' }}` | Empty string → static-only mode |
| `fail-on-error` | `'true'` | **`'false'`** | Changed — avoids failing on MCP unreachability |
| `respect-risk-acceptances` | *(not set — uses action default `'true'`)* | **`'false'`** | Added — skips second MCP curl call |

### Outputs Consumed (from action)

| Output | Used by | Notes |
|---|---|---|
| `total-findings` | Step summary | Displayed in PR comments |
| `blocking-findings` | Gate status | `> 0` → `status=fail` |
| `accepted-findings` | Step summary | Informational |
| `scan-status` | Step summary | `pass` or `fail` |

### Required Secrets/Vars

| Name | Type | Required? | Notes |
|---|---|---|---|
| `vars.ATO_MCP_SERVER_URL` | Repository variable | Optional | When absent, gate runs in static-only mode |

### Behavior After Fix

| Scenario | Before fix | After fix |
|---|---|---|
| `vars.ATO_MCP_SERVER_URL` not set | Gate calls `localhost:5000`, curl fails, finds 0 findings, passes — **false assurance** | Gate skips curl, finds 0 findings (no IaC files or static-only), passes — **honest** |
| `vars.ATO_MCP_SERVER_URL` set to live URL | Gate calls live URL, works correctly | Unchanged — gate calls live URL, respects `fail-on-error: false` |
| Blocking findings found (static analysis) | Never reached (MCP unreachable) | Gate annotates PR with `::warning` / `::error`; sets `status=fail`; does NOT fail the job (no `exit 1`) because `fail-on-error: false` |
| `fail-on-error: true` caller (future) | action default was `false` — mismatched | Action default corrected to `true` — consistent with intent |

---

## 7. Complete Dependency Graph (YAML annotation)

```yaml
# In ci.yml — complete needs graph after this epic

jobs:
  dotnet-build-test:       # needs: []
  integration-tests:       # needs: [dotnet-build-test]
  vscode-extension-compile: # needs: []
  m365-extension-test:     # needs: []
  ato-compliance-gate:     # needs: [], if: pull_request
  playwright-e2e:          # needs: [dotnet-build-test]
```

**Critical path** (longest sequential chain):
```
dotnet-build-test (~4 min)
  └─► integration-tests (~3-4 min)  ← ~8 min total
  └─► playwright-e2e   (~3-4 min)  ← ~8 min total
```

All other jobs run in parallel from push/PR trigger. Total wall time
estimate: **8–12 minutes** for a complete PR run.
