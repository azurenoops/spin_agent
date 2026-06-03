# Spec 065 — Persona-Driven E2E: ISSO + SCA Full Journey
**Epic:** Persona-Driven E2E — ISSO + SCA Full Journey  
**GitHub Issue:** #133  
**Wave:** 1 — Medium Priority  
**Status:** Draft

---

## Background

The dashboard has Playwright infrastructure (`playwright.config.ts`, `global-setup.ts`,
15+ page objects, and 10+ spec files covering navigation, inheritance, portfolio, components,
boundaries, narratives, deviations, and POA&M). However:

1. **CI never runs Playwright tests** — no `playwright` step exists in `.github/workflows/ci.yml`
2. **No ISSO journey test** — existing tests are feature-centric, not persona-centric
3. **No SCA journey test** — assessment workflow is untested end-to-end
4. **Auth bypass** — `global-setup.ts` only checks dashboard reachability; no real login flow
5. **No page objects** for: Assessments, Evidence, Remediation, Authorization

The platform claims to serve ISSO and SCA personas across the full RMF lifecycle.
There is no test that proves a single user, as their role, can complete a representative
workflow without encountering a broken route, missing API call, or permission error.

---

## Verified Code State

### Playwright Infrastructure
- `src/Ato.Copilot.Dashboard/playwright.config.ts`: configured for Chromium, `e2e/` dir, 30s timeout
- `e2e/global-setup.ts`: health-check only, no auth setup
- `e2e/pages/`: portfolio, systems, system-detail, components, boundaries, narratives, deviations, poam, remediation, evidence, capabilities, control-inheritance page objects
- `e2e/tests/`: 10+ spec files (01-navigation → 10-poam), but all are feature-level, not persona journeys
- `05-components.spec.ts:43`: only one persona reference (`rmfRole: 'ISSO'`) — a role label in test data, not a persona-gated auth test

### Missing
- No `assessments.page.ts` page object
- No persona-gated login helper
- No CI job running any Playwright tests
- No ISSO or SCA journey spec files

### Auth Config
- Login is at `/login` → `/login/callback` (Azure Entra ID OIDC)
- `RoleSwitcher` was removed in Feature 051 — role comes from `GET /api/auth/me`
- For E2E testing, a seeded test tenant with ISSO and SCA users is needed

---

## Clarifications

**Q: Should tests use real authentication?**  
A: For CI, use a pre-seeded test user with known credentials stored in GitHub Secrets.
For local dev, allow bypass via `PLAYWRIGHT_AUTH_BYPASS=1` env var (not for prod CI).

**Q: What is the ISSO full journey?**  
A: Register system → Categorize → Set baseline → Assign roles → View narratives →
Review POA&M → View evidence → Trigger assessment → Review findings.

**Q: What is the SCA full journey?**  
A: Log in as SCA → View assigned system → Access Assessments tab → Review findings →
Accept/reject risk → View remediation plan.

---

## User Stories

### US1 (P1): ISSO Full Journey E2E Test
**As an ISSO**, I can complete the full pre-assessment RMF workflow without errors.

**Steps:**
1. Login as ISSO test user
2. Navigate to Portfolio → see system list
3. Open system → System Detail page
4. Navigate to each tab: Narratives, POA&M, Evidence, Boundaries, Components
5. Each tab loads without HTTP errors
6. Attempt to view Assessments tab — loads findings list (or empty state)

**Acceptance Criteria:**
- All 6 tabs load with HTTP 200 responses (verified via Playwright network intercept)
- No `console.error` triggered during navigation
- Test completes in under 60s
- Test is tagged `@persona:isso`

**Edge Cases:**
- System with no evidence → Evidence tab shows empty-state UI, not an error
- System with no findings → Assessment tab shows empty-state
- ISSO cannot access AO-only authorize tab (verify 403 or route redirect)

---

### US2 (P1): SCA Full Journey E2E Test
**As an SCA**, I can access the assessment workflow and review findings.

**Steps:**
1. Login as SCA test user
2. Navigate to assigned system → Assessments tab
3. View finding detail → finding card loads
4. Navigate to Remediation tab → see remediation plans

**Acceptance Criteria:**
- Assessments tab loads finding list
- Finding detail opens without error
- Remediation tab loads
- Test tagged `@persona:sca`

**Edge Cases:**
- SCA cannot see AO authorize view
- SCA sees read-only Narratives (no edit)

---

### US3 (P1): CI Playwright Job
**The CI pipeline runs all Playwright tests on every PR.**

**Acceptance Criteria:**
- `.github/workflows/ci.yml` gains a `playwright-e2e` job
- Job uses `xvfb-run` (required on Linux CI for Chromium headless)
- Job requires `dotnet-build` and `frontend-build` to pass first
- Test artifacts (report, screenshots, videos) uploaded on failure

---

### US4 (P2): Auth Setup Helper
**A reusable Playwright fixture handles test user login and stores auth state.**

**Acceptance Criteria:**
- `e2e/fixtures/auth.ts` provides `issoUser` and `scaUser` fixtures
- Auth state stored in `e2e/.auth/isso.json` and `e2e/.auth/sca.json`
- Auth state reused across tests (login once per run, not per test)
- `PLAYWRIGHT_AUTH_BYPASS=1` env var allows bypassing real login in local dev

---
