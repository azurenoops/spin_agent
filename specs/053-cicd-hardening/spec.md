# Spec: CI/CD Pipeline Hardening

**Feature**: 053-cicd-hardening
**Branch**: `053-cicd-hardening`
**Status**: Ready for implementation
**Priority**: P0 — Critical

## Problem Statement

The CI workflow has only 3 jobs (Build + Unit Tests, VS Code Compile, ATO Compliance Gate). Integration tests, VS Code mocha tests, M365 mocha tests, and Playwright E2E smoke tests never execute on any PR.

## Goal

Every PR executes 5 job groups. No PR merges with a silent test gap.

## Functional Requirements

- FR-001: `dotnet-integration-tests` job runs `tests/Ato.Copilot.Tests.Integration/` with SQLite provider
- FR-002: `vscode-extension-test` job runs 16 test files headlessly via `xvfb-run`
- FR-003: `m365-extension-test` job runs `npm test` in `extensions/m365/`
- FR-004: `dashboard-e2e-smoke` Playwright job runs tests 01-04 against live docker-compose stack
- FR-005: ATO Compliance Gate `mcp-server-url` fixed to use docker-compose health endpoint

## Constraints

- SQLite only for integration tests (no SQL Server in CI)
- Reuse existing `docker-compose.mcp.yml`
- All 5 jobs run in parallel — do not disable existing unit test job
- Integration test job < 5 min, VS Code test job < 3 min, E2E smoke < 8 min

## GitHub Issues

- Epic: #119
- Tasks: #136 (integration tests), #137 (VS Code tests), #138 (M365 tests), #139 (Playwright E2E), #140 (compliance gate fix)

## Files

- `.github/workflows/ci.yml` — primary target
- `tests/Ato.Copilot.Tests.Integration/` — 80+ test files
- `extensions/vscode/test/runTests.ts`
- `extensions/m365/package.json`
- `src/Ato.Copilot.Dashboard/e2e/playwright.config.ts`
- `docker-compose.mcp.yml`
