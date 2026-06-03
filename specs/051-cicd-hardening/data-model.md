# Data Model: CI/CD Hardening

**Feature**: 051-cicd-hardening
**Note**: This epic introduces **no new database entities**. It operates
entirely at the CI infrastructure layer: GitHub Actions workflow YAML,
composite action YAML, and a single new E2E test file. The "data model"
here describes the CI infrastructure contracts — secrets, environment
variables, artifacts, and runner configuration.

---

## 1. GitHub Actions Secrets

No *new* secrets are required by this epic. The compliance gate already
reads `vars.ATO_MCP_SERVER_URL` (a repository *variable*, not a secret).

| Name | Type | Required by | Description |
|---|---|---|---|
| `vars.ATO_MCP_SERVER_URL` | Repository variable (not secret) | `ato-compliance-gate` job | Optional. When set, the compliance scan POSTs to this URL. When absent, gate runs in static-only mode. |

> **No new `secrets.*` references are introduced.** All new jobs use only
> standard GitHub-provided context (`github.*`, `runner.*`, `env.*`).

---

## 2. Environment Variables Per Job

### 2.1 `integration-tests` job

| Variable | Value | Source | Purpose |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Testing` | Job `env:` block | Signals `MultiTenantWebApplicationFactory` to use InMemory/SQLite provider, not SQL Server. |
| `DOTNET_NOLOGO` | `1` | Implicit via `--nologo` flag | Suppresses .NET SDK banner. |

### 2.2 `vscode-extension-compile` job (extended)

| Variable | Value | Source | Purpose |
|---|---|---|---|
| `CI` | `true` | Step `env:` on the `npm test` step | The `@vscode/test-electron` runner reads `CI=true` to suppress interactive prompts and use headless mode. |

### 2.3 `m365-extension-test` job (new)

| Variable | Value | Source | Purpose |
|---|---|---|---|
| *(none beyond defaults)* | — | — | Mocha + ts-node require no extra env vars in CI. |

### 2.4 `playwright-e2e` job (new)

| Variable | Value | Source | Purpose |
|---|---|---|---|
| `PLAYWRIGHT_BASE_URL` | `http://localhost:5173` | Job `env:` block | Overrides Playwright config `BASE_URL`. |
| `CI` | `true` | Job `env:` block | Activates Playwright CI mode: `retries: 2`, `forbidOnly`, screenshot on failure. |
| `VITE_API_URL` | `''` (empty string) | Step `env:` on the `npm run dev` step | Prevents the Vite dev server from failing on missing MCP server URL. |

### 2.5 `ato-compliance-gate` job (updated)

| Variable | Value | Source | Purpose |
|---|---|---|---|
| `ATO_MCP_SERVER_URL` | `${{ vars.ATO_MCP_SERVER_URL \|\| '' }}` | `with:` input `mcp-server-url` | Empty string → static-only mode; non-empty → live MCP mode. |

---

## 3. Artifacts

Each CI job produces named artifacts. Names must be unique across the
entire workflow to prevent collisions.

| Artifact name | Produced by job | Content | Upload condition | Retention |
|---|---|---|---|---|
| `unit-test-results` | `dotnet-build-test` | `.trx` files from `tests/Ato.Copilot.Tests.Unit/` | `if: always()` | 7 days (GitHub default) |
| `integration-test-results` | `integration-tests` | `.trx` files from `tests/Ato.Copilot.Tests.Integration/` | `if: always()` | 7 days |
| `vscode-test-results` | `vscode-extension-compile` | Mocha XML/text output from `extensions/vscode/` | `if: always()` | 7 days |
| `playwright-report` | `playwright-e2e` | Playwright HTML report + screenshots + video from `src/Ato.Copilot.Dashboard/playwright-report/` | `if: failure()` | 7 days |

> **Note**: M365 mocha outputs to stdout only. No artifact upload is
> required for the initial implementation; Mocha JUnit reporter can be
> added in a follow-up.

---

## 4. Runner Requirements

All new and updated jobs use `ubuntu-latest`.

### 4.1 Pre-installed Tools on `ubuntu-latest`

The following tools are available on GitHub-hosted `ubuntu-latest` runners
and do not require explicit installation steps:

| Tool | Version | Used by |
|---|---|---|
| `curl` | system | `ato-compliance-gate` action |
| `jq` | system | `ato-compliance-gate` action |
| `bash` | system | all jobs |
| Node.js | via `setup-node@v4` | VS Code, M365, Playwright jobs |
| .NET | via `setup-dotnet@v4` | `dotnet-build-test`, `integration-tests` |

### 4.2 Tools That Must Be Installed in CI

| Tool | Installed by | Used by | Installation step |
|---|---|---|---|
| `xvfb` | `apt-get install -y xvfb` | `vscode-extension-compile` | Before `npm test` |
| `chromium` + Playwright system deps | `npx playwright install --with-deps chromium` | `playwright-e2e` | After `npm ci` |
| `wait-on` | `npm ci` (after adding to `devDependencies`) | `playwright-e2e` | Via `package.json` |

---

## 5. Workflow Dependency Graph

```
[push / pull_request]
        │
        ├─► dotnet-build-test       (no deps)
        │       └─► integration-tests    (needs: dotnet-build-test)
        │       └─► playwright-e2e       (needs: dotnet-build-test)
        │
        ├─► vscode-extension-compile  (no deps — extended in-place)
        │
        ├─► m365-extension-test       (no deps)
        │
        └─► ato-compliance-gate       (no deps, if: pull_request only)
```

**Key properties**:

- `integration-tests` does not run until `dotnet-build-test` succeeds
  (avoids redundant work when the solution fails to build).
- `playwright-e2e` similarly gates on `dotnet-build-test` (PR quality bar).
- `m365-extension-test` and `ato-compliance-gate` are fully independent
  and start immediately on trigger.
- The top-level `concurrency` block (`cancel-in-progress: true`) applies
  to all jobs; a new push cancels in-flight runs of all jobs in the group.

---

## 6. `action.yml` Input/Output Contract (Updated)

File: `.github/actions/ato-compliance-gate/action.yml`

### Inputs (post-fix)

| Input | Default (before fix) | Default (after fix) | Notes |
|---|---|---|---|
| `scan-paths` | `.` | `.` | Unchanged |
| `severity-threshold` | `'2'` | `'2'` | Unchanged |
| `respect-risk-acceptances` | `'true'` | `'true'` | Callers should pass `'false'` when no MCP server is configured |
| `mcp-server-url` | `'http://localhost:5000'` | `'http://localhost:5000'` | Unchanged default; callers pass empty string for static-only |
| `fail-on-error` | `'false'` | **`'true'`** | **CORRECTED** — fail-fast is the right default for callers who have a live MCP |

### Outputs (unchanged)

| Output | Description |
|---|---|
| `total-findings` | Total compliance findings |
| `blocking-findings` | Findings at or above severity threshold |
| `accepted-findings` | Findings covered by risk acceptances |
| `scan-status` | `pass` or `fail` |

### New Internal Output (added)

| Output | Values | Description |
|---|---|---|
| `scan-mode` | `static-only` \| `live-mcp` | Set by the `scan` step based on whether `mcp-server-url` is empty |

---

## 7. No Database Changes

This epic introduces zero EF Core migrations, zero new `DbSet<T>` registrations,
and zero changes to any entity class. The existing `AtoCopilotContext` and
all entity models are untouched.
