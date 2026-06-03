# Plan — 065: Persona-Driven E2E

## Phase 1 — Auth Foundation (Sprint 1)
T001–T003: Auth fixtures + seed users. Dependency: dev environment with seeded system.

## Phase 2 — Page Objects (Sprint 1)
T004–T005: Assessments and Authorization POMs. These are blockers for Phase 3.

## Phase 3 — Journey Tests (Sprint 1)
T006–T007: Write the two persona journey tests using Phase 1 + 2 artifacts.

## Phase 4 — CI Wiring (Sprint 2)
T008: Add CI job. Requires GitHub Secrets to be set in repo settings.

## Phase 5 — Hardening (Sprint 2)
T009–T011: Network/console assertions, flakiness reduction.

## Dependencies
- Spec 055 (Authorize page) must ship before T005 (authorization.page.ts POM) is testable
- Spec 053 CI hardening for the `xvfb-run` pattern
- Azure AD test tenant with test users provisioned

## Risks
| Risk | Mitigation |
|------|------------|
| Auth test users not provisioned in time | Start with Option B bypass, retrofit Option A |
| Playwright flakiness on CI | Add `waitForNetworkIdle`, set retries: 2 |
| Authorize page (spec 055) not shipped | Stub with `isAccessDenied()` assertion |
