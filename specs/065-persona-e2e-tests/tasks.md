# Tasks — 065: Persona-Driven E2E

## Phase 1 — Auth Foundation (P1)
_Issue #133 | Priority: P1 | 1 sprint_

- [ ] T001: `e2e/fixtures/auth.ts` — ISSO + SCA user fixtures with `storageState`
  - File: `src/Ato.Copilot.Dashboard/e2e/fixtures/auth.ts`
  - Seeded test users: `isso-test@ato.local`, `sca-test@ato.local`
  - Stores auth state to `e2e/.auth/isso.json`, `e2e/.auth/sca.json`
- [ ] T002: `e2e/global-setup.ts` — extend to run auth setup before tests
  - File: `src/Ato.Copilot.Dashboard/e2e/global-setup.ts`
- [ ] T003: Seed script — creates ISSO + SCA test users in development DB
  - File: `scripts/seed-e2e-users.sh`

## Phase 2 — Page Objects (P1)
_Issue #133 | Priority: P1_

- [ ] T004: `e2e/pages/assessments.page.ts` — Assessments tab POM
  - File: `src/Ato.Copilot.Dashboard/e2e/pages/assessments.page.ts`
  - Methods: `goto()`, `waitForFindings()`, `openFinding(index)`, `findingCount()`
- [ ] T005: `e2e/pages/authorization.page.ts` — Authorize tab POM (US1 from 055)
  - File: `src/Ato.Copilot.Dashboard/e2e/pages/authorization.page.ts`
  - Methods: `goto()`, `getStatus()`, `isAccessDenied()`

## Phase 3 — Journey Tests (P1)
_Issue #133 | Priority: P1_

- [ ] T006: `e2e/tests/20-isso-journey.spec.ts` — ISSO full journey
  - File: `src/Ato.Copilot.Dashboard/e2e/tests/20-isso-journey.spec.ts`
  - Tags: `@persona:isso`, `@journey`
  - Steps per US1 acceptance criteria
- [ ] T007: `e2e/tests/21-sca-journey.spec.ts` — SCA full journey
  - File: `src/Ato.Copilot.Dashboard/e2e/tests/21-sca-journey.spec.ts`
  - Tags: `@persona:sca`, `@journey`
  - Steps per US2 acceptance criteria

## Phase 4 — CI Wiring (P1)
_Issue #133 | Priority: P1_

- [ ] T008: `.github/workflows/ci.yml` — add `playwright-e2e` job
  - Needs: `dotnet-build`, `frontend-build`
  - Runs: `xvfb-run npx playwright test --reporter=html`
  - Uploads: `playwright-report/` as artifact on failure
  - Secrets: `E2E_ISSO_PASSWORD`, `E2E_SCA_PASSWORD`

## Phase 5 — Hardening (P2)
_Issue #133 | Priority: P2_

- [ ] T009: Network intercept assertions — verify no 4xx/5xx during ISSO journey
- [ ] T010: Console error assertions — verify no `console.error` during journeys
- [ ] T011: Retry flakiness reduction — add `waitForNetworkIdle` guards
