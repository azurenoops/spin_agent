# Quickstart: CI/CD Hardening

**Feature**: 051-cicd-hardening

This guide shows how to validate each new CI job locally before pushing.

---

## Prerequisites

```bash
# Required tools
node --version   # >= 20
dotnet --version # >= 9.0
act --version    # GitHub Actions local runner (https://github.com/nektos/act)
# Optional: Docker (for act and Testcontainers)
```

Install `act` if not present:
```bash
curl -s https://raw.githubusercontent.com/nektos/act/master/install.sh | sudo bash
```

---

## 1. Validate Integration Tests Locally (#136)

```bash
cd /tmp/ato-copilot

# Build the solution first
dotnet build Ato.Copilot.sln -c Release -nologo

# Run integration tests (mirrors what CI does)
ASPNETCORE_ENVIRONMENT=Testing \
dotnet test tests/Ato.Copilot.Tests.Integration/Ato.Copilot.Tests.Integration.csproj \
  -c Release --no-build -nologo --logger trx

# Expected: ~510 pass, ~146 skip/fail (pre-existing flakes)
# Check: no new failures beyond the known baseline
```

**Dry-run the CI job**:
```bash
act -j integration-tests --dry-run
```

---

## 2. Validate VS Code Extension Tests Locally (#137)

```bash
cd /tmp/ato-copilot/extensions/vscode

# Install dependencies
npm ci

# Compile
npm run compile

# Run tests (Linux: requires xvfb; skip on macOS/Windows)
if command -v xvfb-run &>/dev/null; then
  xvfb-run --auto-servernum CI=true npm test
else
  CI=true npm test  # macOS/Windows: Electron finds a display automatically
fi
```

**Dry-run the CI job**:
```bash
cd /tmp/ato-copilot
act -j vscode-extension-compile --dry-run
```

---

## 3. Validate M365 Extension Tests Locally (#138)

```bash
cd /tmp/ato-copilot/extensions/m365

# Install dependencies
npm ci

# Build (TypeScript compilation)
npm run build

# Run mocha tests
npm test

# Expected: mocha outputs test results to stdout; exit 0 on success
```

**Dry-run the CI job**:
```bash
cd /tmp/ato-copilot
act -j m365-extension-test --dry-run
```

---

## 4. Validate ATO Compliance Gate Fix Locally (#140)

```bash
cd /tmp/ato-copilot

# Simulate the updated workflow inputs manually:
# - mcp-server-url: '' (empty = static-only mode)
# - fail-on-error: false
# - respect-risk-acceptances: false

# Trigger via act (pull_request event required — gate runs only on PRs)
act pull_request -j ato-compliance-gate \
  --var ATO_MCP_SERVER_URL='' \
  --dry-run

# Manual check: inspect action.yml scan step
# When mcp-server-url is empty:
# - No curl call to /api/tools/compliance_scan_iac
# - scan-mode=static-only output
# - Step summary says "running in static-only mode"

# To test with a live MCP server (optional):
# 1. Start the MCP server: docker compose -f docker-compose.mcp.yml up ato-copilot
# 2. act pull_request -j ato-compliance-gate --var ATO_MCP_SERVER_URL=http://localhost:3001
```

---

## 5. Validate Playwright E2E Smoke Locally (#139)

```bash
cd /tmp/ato-copilot/src/Ato.Copilot.Dashboard

# Install dependencies (including wait-on)
npm ci

# Install Playwright browsers
npx playwright install --with-deps chromium

# Start Vite dev server in background
npm run dev -- --port 5173 &
DEV_PID=$!

# Wait for server ready
npx wait-on http://localhost:5173 --timeout 60000

# Run smoke tests
PLAYWRIGHT_BASE_URL=http://localhost:5173 CI=true \
npx playwright test --grep @smoke

# View HTML report
npx playwright show-report

# Clean up
kill $DEV_PID
```

**Dry-run the CI job**:
```bash
cd /tmp/ato-copilot
act -j playwright-e2e --dry-run
```

---

## 6. Validate Full Workflow Locally

```bash
cd /tmp/ato-copilot

# Dry-run all jobs to check YAML syntax and dependency graph
act push --dry-run

# Run all jobs (requires Docker for act)
# Note: ato-compliance-gate only runs on pull_request events
act push -j dotnet-build-test
act push -j integration-tests
act push -j vscode-extension-compile
act push -j m365-extension-test
act push -j playwright-e2e
act pull_request -j ato-compliance-gate
```

---

## 7. Verifying CI on a Real PR

After implementing all tasks:

1. Create a feature branch: `git checkout -b 051-cicd-hardening`
2. Make a small change (e.g., add a comment to `ci.yml`).
3. Push and open a draft PR.
4. Watch GitHub Actions — all 6 jobs should appear and pass:
   - `dotnet-build-test` ✅
   - `integration-tests` ✅
   - `vscode-extension-compile` ✅
   - `m365-extension-test` ✅
   - `ato-compliance-gate` ✅ (with "static-only mode" in step summary)
   - `playwright-e2e` ✅

5. Verify artifacts are uploaded:
   - `integration-test-results` — `.trx` files present
   - `vscode-test-results` — test output present
   - `playwright-report` — only if tests fail (by design)

6. Verify the compliance gate step summary shows:
   ```
   ⚠️ MCP server URL not configured — running in static-only mode.
   Risk acceptances were not checked.
   ```
