# Quickstart — 065: Persona-Driven E2E

## Prerequisites
```bash
# Install Playwright browsers (one-time)
cd src/Ato.Copilot.Dashboard
npx playwright install chromium
```

## Running Tests Locally (Auth Bypass)
```bash
cd src/Ato.Copilot.Dashboard
PLAYWRIGHT_AUTH_BYPASS=1 PLAYWRIGHT_BASE_URL=http://localhost:5173 npx playwright test --grep @journey
```

## Running Tests Locally (Real Auth)
```bash
cd src/Ato.Copilot.Dashboard
E2E_ISSO_PASSWORD=<password> E2E_SCA_PASSWORD=<password> \
PLAYWRIGHT_BASE_URL=http://localhost:5173 npx playwright test --grep @journey
```

## Running Specific Persona
```bash
npx playwright test --grep @persona:isso
npx playwright test --grep @persona:sca
```

## View Report
```bash
npx playwright show-report
```

## Seed E2E Users (local dev)
```bash
bash scripts/seed-e2e-users.sh
# Creates isso-test@ato.local and sca-test@ato.local in the dev SQLite DB
```
