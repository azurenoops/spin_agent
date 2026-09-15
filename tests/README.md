# External Playwright + HTTP tests — ATO Copilot (public routes only)

This folder contains Playwright tests and a simple Node HTTP test script for deployed ATO Copilot public routes.

Prereqs
- Node 18+ (for `fetch` in raw tests) or install node-fetch
- Playwright: `npm i -D @playwright/test` at repo root (or use global npx)
- Optional: set environment variable BASE_URL to target host (defaults to the artifact host)

Playwright
- Install: npm ci (ensure devDeps include Playwright) and `npx playwright install`
- Run tests (single file): 
  - BASE_URL="https://your-host" npx playwright test tests/ato-copilot.spec.ts
- To run all tests in debug/headful mode:
  - BASE_URL="https://your-host" npx playwright test tests/ato-copilot.spec.ts --headed --timeout=300000

Notes:
- Auth-protected routes are explicitly skipped and marked SKIPPED(REQUIRES_AUTH).
- Tests only target public routes and are safe to run against production/staging without credentials.

Node HTTP raw tests
- Run: node tests/http-raw-tests.js [BASE_URL]
- Example:
  - node tests/http-raw-tests.js https://ca-ato-copilot-dashboard-v2.blackwater-9393aa1a.centralus.azurecontainerapps.io
- Exit code is 0 on success, non-zero on failures. Useful for CI smoke steps.

License / Attribution
- Test code is authored for the ATO Copilot repo per artifact `playwright-tests-ato-copilot.md`.
