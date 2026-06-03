# Tasks: CI/CD Pipeline Hardening

**Feature**: 051-cicd-hardening
**Branch**: `051-cicd-hardening`
**Input artifacts**: [spec.md](./spec.md)

**Tests**: REQUIRED. Every new CI job must be verified to fail fast on a broken test.

---

## Phase 1: Integration Tests

- [ ] T001 Add `dotnet-integration-tests` job to `.github/workflows/ci.yml` after build job. Set `ASPNETCORE_ENVIRONMENT=Test` and SQLite `DefaultConnection` override. All 80+ files in `tests/Ato.Copilot.Tests.Integration/` must execute. Job must complete < 5 min. GitHub Issue: #136
- [ ] T002 Verify SQLite EF provider swap works by confirming `DbContextOptionsBuilder.UseSqlite` is used when `ASPNETCORE_ENVIRONMENT=Test`. Add `appsettings.Test.json` if not present.

## Phase 2: VS Code Extension Tests

- [ ] T003 Add `vscode-extension-test` job to ci.yml. Install xvfb. Run `xvfb-run node out/test/runTests.js` in `extensions/vscode/`. Compile extension before test step. All 16 test files must pass. GitHub Issue: #137

## Phase 3: M365 Extension Tests

- [ ] T004 Add `m365-extension-test` job to ci.yml. Steps: `npm ci && npm test` in `extensions/m365/`. All 15 mocha test suites must pass. GitHub Issue: #138

## Phase 4: Playwright E2E Smoke

- [ ] T005 Add `dashboard-e2e-smoke` job. Spin up `docker-compose.mcp.yml`. Wait for all health endpoints. Run `npx playwright test tests/0[1-4]` subset. Teardown after. GitHub Issue: #139

## Phase 5: ATO Compliance Gate Fix

- [ ] T006 Fix `mcp-server-url` in ATO Compliance Gate job to use docker-compose service URL (`http://localhost:5000/health` or equivalent). Verify gate passes clean. GitHub Issue: #140
