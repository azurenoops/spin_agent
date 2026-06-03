# Requirements Checklist — 065: Persona-Driven E2E

## SDK Artifacts
- [x] spec.md
- [x] tasks.md
- [x] data-model.md
- [x] research.md
- [x] plan.md
- [x] quickstart.md
- [x] checklists/requirements.md
- [x] contracts/playwright-conventions.md

## Test Artifacts Required
- [ ] `e2e/fixtures/auth.ts` — ISSO + SCA user fixtures
- [ ] `e2e/pages/assessments.page.ts`
- [ ] `e2e/pages/authorization.page.ts`
- [ ] `e2e/tests/20-isso-journey.spec.ts`
- [ ] `e2e/tests/21-sca-journey.spec.ts`
- [ ] `scripts/seed-e2e-users.sh`

## CI Requirements
- [ ] `playwright-e2e` job in `ci.yml`
- [ ] `xvfb-run` wrapper on Linux
- [ ] Playwright artifacts uploaded on failure
- [ ] GitHub Secrets: `E2E_ISSO_PASSWORD`, `E2E_SCA_PASSWORD`

## Quality Gates
- [ ] ISSO journey: 0 console errors, 0 API 4xx/5xx
- [ ] SCA journey: 0 console errors, 0 API 4xx/5xx
- [ ] Tests complete in < 90s on CI
- [ ] No `.only` or `.skip` in merged test files

## Out of Scope
- AO, ISSM, Engineer persona journeys (follow-on wave)
- Mobile/responsive E2E
- Performance load testing (spec 063)
- OSCAL export E2E
