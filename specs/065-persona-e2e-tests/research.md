# Research — 065: Persona-Driven E2E

## Auth Strategy Options

### Option A: Real Entra ID test users (recommended)
- Create test users in a non-prod Azure AD tenant
- Store passwords in GitHub Secrets
- Playwright logs in via `/login` using `page.fill` + `page.click`
- Auth state cached per run via `storageState`
- **Pro:** Tests the real auth flow  
- **Con:** Requires Azure AD tenant, slower CI login step

### Option B: Dev-mode auth bypass
- Add `PLAYWRIGHT_AUTH_BYPASS=1` env var that skips the Entra ID redirect
- Server returns a seeded user identity when bypass token present
- **Pro:** Fast, no Azure dependency  
- **Con:** Doesn't test real auth; bypass must be disabled in prod

**Decision:** Option A for CI. Option B available for local dev iteration.

## Page Object Model vs Direct Locators

Existing e2e tests use POM (`e2e/pages/`). Persona journey tests must use the same pattern
for consistency. New page objects needed: `assessments.page.ts`, `authorization.page.ts`.

## Flakiness Mitigation

Existing tests use `page.waitForURL` and `expect(locator).toBeVisible`. Journey tests
must add network-level guards:
- `page.waitForResponse(url => url.includes('/api/'), { status: 200 })` after navigation
- `page.waitForLoadState('networkidle')` before assertions on dynamic lists

## CI Infrastructure

Linux CI (GitHub Actions Ubuntu) requires `xvfb-run` for Chromium. This was identified
as a known pitfall in the VS Code extension test setup (spec 053). Same fix applies here:
```yaml
run: xvfb-run npx playwright test
```

## Test Run Duration

Based on existing tests, each spec file takes 5-15s. Two persona journey files with
~10 steps each: estimated 30-60s total. CI timeout: 5 minutes.
