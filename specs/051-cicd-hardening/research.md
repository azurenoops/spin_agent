# Research: CI/CD Hardening

**Feature**: 051-cicd-hardening
**Date**: 2026-06-03

---

## 1. Integration Tests (#136)

### 1.1 Current State

`tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj`
references:

- `Microsoft.AspNetCore.Mvc.Testing` 9.0.0 — provides `WebApplicationFactory<T>`
- `Microsoft.EntityFrameworkCore.InMemory` 9.0.0 — in-process provider
- `Testcontainers.MsSql` 3.10.0 — for Row-Level Security tests
- `Xunit.SkippableFact` 1.5.23 — gates Testcontainer tests on Docker availability
- `xunit` 2.9.3 / `xunit.runner.visualstudio` 2.8.2
- `FluentAssertions` 7.0.0, `Moq` 4.20.72

The project references `Ato.Copilot.Mcp`, `Ato.Copilot.Core`, and four
other projects — it compiles as part of `Ato.Copilot.sln`.

### 1.2 Why It's Currently Excluded

`ci.yml` runs only:
```
dotnet test tests/Ato.Copilot.Tests.Unit/Ato.Copilot.Tests.Unit.csproj
```

The integration `.csproj` path is never referenced. There is no `--filter`
or exclusion comment; it is simply omitted.

### 1.3 Run Time Estimate

Feature 050 T049 reported: integration suite → 510 passed / 146 failed
(pre-existing test-order flakes). Rough run time on local hardware:
~45–90 seconds for 510 tests. On GitHub-hosted `ubuntu-latest` (slower
cold-start): estimated 2–4 minutes including restore + build.

The `Testcontainers.MsSql` tests are gated with `Skip.IfNot(DockerAvailable)`.
On GitHub-hosted runners without Docker-in-Docker configured,
`DockerAvailable` is expected to return `false`, so those tests will be
skipped automatically.

### 1.4 Decision

Add the integration tests as a separate job (`needs: dotnet-build-test`)
to avoid re-compiling the solution. Pass `--no-build` to the test command
since the build artifact is already available from the upstream job.

> **Note**: GitHub Actions does not automatically share build artifacts
> between jobs. To use `--no-build`, the `integration-tests` job must
> either re-run `dotnet build` or use `actions/upload-artifact` +
> `actions/download-artifact` to pass the compiled output. The simpler
> approach (re-run build with `--no-restore` since packages are cached via
> `dotnet restore`) is recommended for the initial implementation.

---

## 2. VS Code Extension Tests (#137)

### 2.1 Current State

`extensions/vscode/package.json`:
- `"test": "node ./dist/test/runTests.js"` — runs `@vscode/test-electron`
- `"pretest": "npm run compile"` — recompiles before test
- `@vscode/test-electron` `^2.3.8` in devDependencies

`.mocharc.js`: `spec: "test/suite/**/*.test.ts"`, `require: ["ts-node/register"]`

### 2.2 The xvfb Requirement

`@vscode/test-electron` downloads a headless VS Code binary and runs it as
an Electron process. Electron requires a display server. On Linux CI runners,
this means `xvfb` must be running.

Two approaches:
1. **`xvfb-run --auto-servernum npm test`** — wraps the command in a
   virtual framebuffer. Simple and reliable.
2. **`Xvfb` GitHub Action** (`GabrielBB/xvfb-action@v1`) — a community
   action that starts/stops Xvfb around a step. Adds an external dependency.

**Selected**: Option 1 (`xvfb-run`). No external action dependency, works
on `ubuntu-latest` after `apt-get install -y xvfb`.

### 2.3 Test Script

`npm test` → `node ./dist/test/runTests.js`. The script is already compiled
by `pretest: npm run compile`, so no additional compile step is needed before
`npm test`.

---

## 3. ATO Compliance Gate Fix (#140)

### 3.1 Root Cause

`ci.yml` line 90:
```yaml
mcp-server-url: ${{ vars.ATO_MCP_SERVER_URL || 'http://localhost:5000' }}
```

The `localhost:5000` fallback is always reached because `vars.ATO_MCP_SERVER_URL`
is unset in the default repository setup. The MCP server never runs in CI.
The `action.yml` compliance scan step includes:
```bash
RESPONSE=$(curl -s -X POST "${{ inputs.mcp-server-url }}/api/tools/compliance_scan_iac" \
  --connect-timeout 5 --max-time 30 2>/dev/null || echo '{"findings": []}')
```

The `|| echo '{"findings": []}'` fallback means curl failure = zero findings =
trivially passes. `fail-on-error: 'true'` in `ci.yml` only triggers when
`blocking-findings > 0`, which never happens.

### 3.2 Option Analysis

#### Option (a): Mock/Stub Mode
Introduce a `mock-mode: 'true'` input to `action.yml` that runs a bundled
set of test findings (static YAML or JSON) instead of calling the MCP server.

**Pros**: Predictable; tests the gate logic itself.
**Cons**: Mocked findings may diverge from real findings; developers may
disable it thinking it's "fine" since it's just a mock. The gate becomes
a test of the mock, not the IaC.

#### Option (b): Static-Only Analysis (Selected)
Pass `fail-on-error: 'false'` and `respect-risk-acceptances: 'false'`.
The scan step still discovers IaC files and buckets them by severity
(the `cat_severity` field from the MCP response). With MCP unreachable,
each file gets `{"findings": []}` → zero findings → gate passes silently.

**Enhancement**: When `mcp-server-url` is empty, skip the curl call
entirely and emit an informational notice rather than making a failing
HTTP call. This makes the "no MCP configured" state explicit.

**Pros**: Honest — the gate summary says "static-only mode"; real
annotations still appear when a live MCP URL is configured; zero false
positives; zero silent failures.
**Cons**: No risk-acceptance checking in the default CI configuration.

#### Option (c): Spin Up MCP Container
Add a `services:` block to the `ato-compliance-gate` job that pulls the
`ato-copilot-mcp` Docker image (from a registry or built from source).

**Pros**: Real end-to-end compliance checking on every PR.
**Cons**: Requires a published Docker image (not currently in CI), SQL
Server dependency, `SA_PASSWORD` secret, 2–5 minutes of container startup,
significant CI complexity. This is the correct long-term direction but
out of scope for this hardening epic.

### 3.3 `action.yml` Default Bug

`action.yml` line 24: `default: 'false'` for `fail-on-error`. This means
any caller who does NOT override `fail-on-error` gets silent-pass behavior
even if they provide a live MCP URL. The correct default is `'true'`
(fail on blocking findings). `ci.yml` already overrides to `'true'`, but
the action default should match the intended behavior for new callers.

---

## 4. M365 Extension Tests (#138)

### 4.1 Current State

`extensions/m365/package.json`:
- `"test": "mocha"` — runs mocha directly
- `"build": "tsc"` — TypeScript compilation

`.mocharc.js`: `spec: "test/**/*.test.ts"`, `require: ["ts-node/register"]`

No server required: mocha uses `sinon` stubs for HTTP calls (confirmed by
devDependencies: `sinon`, `@types/sinon`).

### 4.2 Why Not Combined With VS Code Job

The M365 extension is a separate npm workspace (`extensions/m365/`) with
its own `package-lock.json` and `node_modules`. Combining it with the VS
Code job would require either monorepo tooling (not used here) or running
`npm ci` twice in one job. A separate job is cleaner and runs in parallel.

---

## 5. Playwright E2E Smoke (#139)

### 5.1 Config Details

`src/Ato.Copilot.Dashboard/playwright.config.ts`:
- `testDir: './e2e'`
- `baseURL: process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:5173'`
- `workers: 1` (sequential — important for CI stability)
- `retries: process.env.CI ? 2 : 0` (already CI-aware)
- `forbidOnly: !!process.env.CI` (blocks `.only` in CI)
- `projects: [{ name: 'chromium' }]` (Chromium only)
- `globalSetup: './e2e/global-setup.ts'` (auth or env setup)
- `timeout: 30_000 ms`, `expect.timeout: 10_000 ms`

### 5.2 Vite Dev Server Strategy

The dashboard builds and serves via `npm run dev` (Vite dev server on
port 5173). In CI:
1. Start Vite in background: `npm run dev -- --port 5173 &`
2. Wait for readiness: `npx wait-on http://localhost:5173 --timeout 60000`
3. Run Playwright: `npx playwright test --grep @smoke`

Alternative: use `playwright.config.ts`'s `webServer` option to auto-start.
However, this would require modifying the config to conditionally start the
server in CI — more invasive. The background-start approach is simpler and
matches the existing config shape.

### 5.3 MCP Server Dependency

The dashboard makes API calls to the MCP server. In CI (no MCP server):
- Smoke tests should use `page.route()` to intercept and stub API calls, OR
- Test only navigation and static layout (does not require API responses), OR
- Accept that the dashboard shows an "API unavailable" state and assert on that.

The initial smoke test should be option 2 (navigation-only) to unblock CI.

### 5.4 `wait-on` Dependency

`wait-on` is a widely-used package for waiting on URLs/ports/files.
Available as `npm install --save-dev wait-on`. Alternatively, a simple
`bash` loop with `curl` could replace it without adding a dependency.
The `wait-on` approach is more idiomatic in the Playwright ecosystem.

---

## 6. Summary of Selected Approaches

| Issue | Approach | Rationale |
|---|---|---|
| #136 Integration | Separate `integration-tests` job, `needs: dotnet-build-test`, re-build, InMemory/SQLite | Simplest; no Docker-in-Docker required |
| #137 VS Code | Extend existing job, `xvfb-run npm test` | Same job = same Node cache |
| #138 M365 | New independent job | Parallel; separate workspace |
| #139 Playwright | New job, Vite background start + `wait-on`, smoke-only | Bounded run time; unblocks CI without requiring MCP |
| #140 Gate fix | `fail-on-error: false` + `respect-risk-acceptances: false` + empty URL → skip curl | Honest static mode; no false assurance |
