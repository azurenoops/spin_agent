# Plan: CI/CD Hardening

**Feature**: 051-cicd-hardening
**Date**: 2026-06-03

---

## 1. Objective

Harden the CI pipeline by closing four gaps:

1. Integration tests never run → add `integration-tests` job (#136).
2. VS Code extension tests never run → extend `vscode-extension-compile` (#137).
3. M365 extension tests never run → add `m365-extension-test` job (#138).
4. Playwright E2E suite never runs → add `playwright-e2e` job (#139).
5. ATO Compliance Gate produces false assurance → fix `ci.yml` + `action.yml` (#140).

---

## 2. Delivery Phases

### Phase 1 — Setup (1 day)
Baseline audit, GitHub issues, dry-run validation environment.
No production files changed.

### Phase 2 — Integration Tests (0.5 day)
One new job in `ci.yml`. Independent. Highest-value P1 change.
Expected CI run time: 3–5 minutes.

### Phase 3 — VS Code Mocha (0.5 day)
Two new steps in existing `vscode-extension-compile` job.
Requires `xvfb` install step.

### Phase 4 — M365 Mocha (0.5 day)
One new job in `ci.yml`. Fully independent and parallel with Phase 3.
Expected CI run time: < 1 minute.

### Phase 5 — ATO Compliance Gate Fix (0.5 day)
`ci.yml` + `action.yml` changes. Independent of all other phases.
Correctness fix with no new infrastructure.

### Phase 6 — Playwright E2E Smoke (1 day)
One new job + smoke test skeleton + `wait-on` dependency.
Highest complexity. P2 — may slip to follow-up PR.

### Phase 7 — Polish & PR (0.5 day)
Full suite validation, documentation, PR preview.

**Total estimate**: 3.5 days solo; ~1.5 days with parallel execution of
Phases 2–5.

---

## 3. Constitution Check

| Principle | Status | Notes |
|---|---|---|
| § I — Single Responsibility | PASS | Each job does one thing |
| § II — Testability | PASS | Every job change validated with `act` dry-run |
| § III — Spec-Driven | PASS | All changes traced to spec.md user stories |
| § IV — Minimal Surface | PASS | No new secrets, no new DB entities |
| § V — Backwards Compatibility | PASS | Existing jobs untouched (only vscode job extended in-place) |
| § VI — TDD | PASS (adapted) | CI changes validated with dry-runs before implementation |
| § DevOps: GitHub Issue Discipline | DEFERRED | T001 deferred pending user approval |

---

## 4. Risk Register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| xvfb not available on `ubuntu-latest` | Low | High | Explicit `apt-get install -y xvfb` step |
| Integration test pre-existing flakes counted as new failures | Medium | Medium | Document 146 baseline flakes; do not gate on zero failures |
| Playwright smoke test fails due to missing API server | High | Low | Smoke tests must not require live MCP; use navigation-only assertions initially |
| `wait-on` npm package not found | Low | Medium | Alternative: bash `until curl` loop |
| Compliance gate `static-only` mode misunderstood as "full scan" | Medium | Medium | Explicit step summary annotation: "running in static-only mode" |
| CI wall time exceeds 15 minutes total | Low | Medium | All new jobs run in parallel; integration tests gated on build; estimated total 8–12 min |

---

## 5. Files Modified

| File | Change type | Issue |
|---|---|---|
| `.github/workflows/ci.yml` | Modified | #136, #137, #138, #139, #140 |
| `.github/actions/ato-compliance-gate/action.yml` | Modified | #140 |
| `src/Ato.Copilot.Dashboard/package.json` | Modified (add `wait-on`) | #139 |
| `src/Ato.Copilot.Dashboard/package-lock.json` | Modified (auto) | #139 |
| `src/Ato.Copilot.Dashboard/e2e/smoke.spec.ts` | Created | #139 |
| `tests/Ato.Copilot.Tests.Integration/Infrastructure/*.cs` | Possibly modified | #136 |

---

## 6. Success Criteria

After this epic merges, a new PR must trigger:

- [ ] `dotnet-build-test` — green (unit tests pass)
- [ ] `integration-tests` — green (integration tests pass or skip gracefully)
- [ ] `vscode-extension-compile` — green (compile + mocha pass)
- [ ] `m365-extension-test` — green (build + mocha pass)
- [ ] `ato-compliance-gate` — green (static-only scan; step summary shows mode)
- [ ] `playwright-e2e` — green (smoke tests pass)

The compliance gate must **never** emit a green status from silently
swallowed MCP errors. The step summary must explicitly state whether the
scan ran in static-only or live-MCP mode.
