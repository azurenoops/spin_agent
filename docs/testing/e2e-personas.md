# E2E Persona Test Guide

Persona-driven end-to-end tests for ATO Copilot. Covers realistic ISSO and SCA user journeys through the RMF lifecycle.

## Persona Specs

| Spec | Persona | RMF Steps | Seed |
|------|---------|-----------|------|
| `isso-journey.spec.ts` | ISSO | Categorize → Select → Implement → Assess → Authorize | `isso-seed.json` |
| `sca-journey.spec.ts` | SCA | Browse → Import Scan → Review Findings → POA&M | `sca-seed.json` |

## Running Locally

```bash
# From src/Ato.Copilot.Dashboard/
npx playwright test e2e/tests/personas/

# Run a single persona
npx playwright test e2e/tests/personas/isso-journey.spec.ts

# With UI mode
npx playwright test e2e/tests/personas/ --ui
```

## Environment Setup

Set `PLAYWRIGHT_BASE_URL` to point at a running dashboard instance:

```bash
export PLAYWRIGHT_BASE_URL=http://localhost:5173
```

For seeded data, use the fixture JSON files in `e2e/fixtures/` to pre-populate the API via the admin seed endpoint before running:

```bash
curl -X POST http://localhost:3001/api/v1/test/seed \
  -H 'Content-Type: application/json' \
  -d @e2e/fixtures/isso-seed.json
```

## CI Integration

Persona specs are included in the `playwright-smoke` CI job via `testMatch` glob `**/personas/*.spec.ts`.
They run after the main E2E suite and gate the `deploy-stage` job.

## Notes

- Tests use `test.skip()` gracefully when no systems exist in the target environment
- Page Objects are in `e2e/pages/` — extend them as UI evolves
- Fixtures in `e2e/fixtures/*.json` document the expected seed shape but do not auto-seed; implement the seed endpoint in `TestDataEndpoints.cs` if full data-driven testing is needed
