# Phase 1: Quickstart — Local Verification of CI/CD Pipeline Hardening

**Branch**: `053-cicd-hardening`
**Date**: 2026-06-03
**Audience**: an engineer who has the repo cloned and wants to **prove all five new CI job definitions work correctly on their workstation** before opening the PR.

This recipe does not require a GitHub Actions account, a GitHub runner, or any cloud resources. All five jobs are standard shell commands that can be reproduced locally.

---

## Prerequisites

```bash
# Versions pinned by global.json + package.json. Verify:
dotnet --version          # 9.0.x
node --version            # 20.x or 22.x
docker --version          # 24.x or newer (Compose v2 built-in)
```

On Linux/WSL, also verify xvfb is available:

```bash
which xvfb-run            # should resolve; if not: sudo apt-get install -y xvfb
```

If you don't have the above, run `scripts/bootstrap.sh` (macOS/Linux) or `scripts/bootstrap.ps1` (Windows).

---

## 0. Make sure the branch is current

```bash
cd /c/Users/zeus_bot/ato-copilot
git checkout 053-cicd-hardening
git pull --ff-only
```

---

## 1. FR-001 — `dotnet-integration-tests` job (T001, T002)

**Goal**: all 80+ integration test files execute against SQLite in under 5 minutes.

```bash
# Set the environment variables the CI job will set:
export ASPNETCORE_ENVIRONMENT=Test

# Run the integration test project:
dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  --configuration Release \
  --logger "console;verbosity=normal" \
  --no-build
```

> If `--no-build` fails with "no output," build first: `dotnet build Ato.Copilot.sln -c Release`

**Expected output**:

```
Passed! - Failed: 0, Passed: N, Skipped: 0, Total: N
```

where N ≥ 80 (the current count of integration test cases across the 80+ files).

**Timing**: run `time dotnet test ...` — elapsed time must be under 5 min.

**Troubleshooting**:
- If tests fail with `Cannot open database` — confirm `ASPNETCORE_ENVIRONMENT=Test` is exported and that `appsettings.Test.json` exists (T002 creates it if absent).
- If `appsettings.Test.json` is missing, create it at `tests/Ato.Copilot.Tests.Integration/appsettings.Test.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=/tmp/ato-copilot-test.db"
  }
}
```

---

## 2. FR-002 — `vscode-extension-test` job (T003)

**Goal**: all 16 VS Code extension test files pass headlessly via xvfb-run in under 3 minutes.

```bash
cd extensions/vscode

# Install dependencies:
npm ci

# Compile the extension:
npm run compile

# Run tests headlessly (mirrors the CI job step):
xvfb-run node out/test/runTests.js
```

**Expected output**:

```
  16 passing
  0 failing
```

**Timing**: run `time xvfb-run node out/test/runTests.js` — elapsed time must be under 3 min.

**Troubleshooting**:
- If `xvfb-run` is missing: `sudo apt-get install -y xvfb` (Linux/WSL) or use WSLg on Windows.
- If `out/test/runTests.js` is missing: the `npm run compile` step is required first.
- If VS Code binary download hangs: `@vscode/test-electron` caches the VS Code binary in `~/.vscode-test/`. On first run, allow up to 2 min for the download.

---

## 3. FR-003 — `m365-extension-test` job (T004)

**Goal**: all 15 Mocha test suites in `extensions/m365/` pass via `npm test`.

```bash
cd extensions/m365

# Install dependencies:
npm ci

# Run the full Mocha suite:
npm test
```

**Expected output**:

```
  15 passing
  0 failing
```

**Troubleshooting**:
- If `npm test` exits with `No test files found`: verify `extensions/m365/package.json` has a `"test"` script and a Mocha spec pattern pointing to the `test/` directory.
- If a suite fails: read the Mocha output; the failure is in the extension source, not the CI YAML.

---

## 4. FR-004 — `dashboard-e2e-smoke` job (T005)

**Goal**: Playwright smoke tests 01–04 pass against the live docker-compose stack in under 8 minutes.

### Step 1: Start the stack

```bash
cd /c/Users/zeus_bot/ato-copilot
docker compose -f docker-compose.mcp.yml up -d --build
```

### Step 2: Wait for health endpoints

```bash
# Poll the MCP server health endpoint (mirrors the CI wait loop):
for i in $(seq 1 30); do
  if wget -q --spider http://localhost:5000/health 2>/dev/null; then
    echo "MCP server is ready"; break
  fi
  echo "Waiting for MCP server... ($i/30)"
  sleep 5
done
```

Adjust the port if `docker-compose.mcp.yml` maps the MCP service to a different host port. Check with:

```bash
docker compose -f docker-compose.mcp.yml ps
```

### Step 3: Run the smoke tests

```bash
cd src/Ato.Copilot.Dashboard

# Install Playwright browsers if not already cached:
npx playwright install --with-deps chromium

# Run the 01-04 smoke subset:
npx playwright test tests/0[1-4] --config e2e/playwright.config.ts
```

**Expected output**:

```
  4 passed (Chromium)
```

**Timing**: the full step 1–3 sequence must complete in under 8 minutes.

### Step 4: Tear down

```bash
cd /c/Users/zeus_bot/ato-copilot
docker compose -f docker-compose.mcp.yml down -v
```

`-v` removes all volumes — required to get a clean state for the next run.

**Troubleshooting**:
- If the health endpoint never responds: check `docker compose logs ato-copilot-mcp` for startup errors.
- If Playwright reports `browserType.launch: Executable doesn't exist`: run `npx playwright install chromium`.
- If tests `01`–`04` error on API calls: the MCP server may not have completed its startup sweep; increase the wait-loop iterations.

---

## 5. FR-005 — ATO Compliance Gate fix (T006)

**Goal**: the ATO Compliance Gate job step reaches the MCP server via the correct docker-compose health endpoint URL.

This step cannot be fully verified locally without running the GitHub Actions workflow, but you can confirm the URL is reachable while the stack is up:

```bash
# With docker-compose.mcp.yml running (from step 4 above, before teardown):
curl -f http://localhost:5000/health
```

**Expected**: HTTP 200 response from the MCP health endpoint.

Verify the YAML change in `ci.yml`:

```bash
grep -A3 'mcp-server-url' .github/workflows/ci.yml
```

**Expected**: the value should be `http://localhost:5000/health` (or the port confirmed above from `docker compose ps`), not a static or placeholder URL.

---

## 6. Full CI YAML diff review

Before pushing, review the complete diff:

```bash
git diff .github/workflows/ci.yml
```

Check that:

1. Five job definitions are present: `dotnet-integration-tests`, `vscode-extension-test`, `m365-extension-test`, `dashboard-e2e-smoke`, and the modified `ato-compliance-gate`.
2. All new jobs have `runs-on: ubuntu-latest`.
3. All new jobs are independent (no `needs:` pointing to each other unless the E2E job must wait for the stack — which is handled internally within the job, not via cross-job `needs:`).
4. No existing job is modified except `ato-compliance-gate` (the `mcp-server-url` input only).
5. Job-level `timeout-minutes` values match the SLOs: integration tests ≤ 5, VS Code tests ≤ 3, E2E smoke ≤ 8.

---

## Tear-down

```bash
docker compose -f docker-compose.mcp.yml down -v
```

Removes containers, networks, and volumes created by the E2E verification step.
