MCP E2E & API Test Suite

Files produced in this branch:
- tests/e2e/ (Playwright-based E2E tests)
- tests/api/ (Jest+Axios API tests)
- tests/e2e/fixtures/roles.json (role fixtures for AO, ISSM, ISSO, SCA, Engineer)
- scripts/seed-test.sh (helper to run scripts/seed-progress.sql against running SQL container)

Environment variables used:
- MCP_BASE_URL: base URL for MCP API/website (default: http://localhost:5100)
- MSSQL_SA_PASSWORD: SA password for running scripts/seed-test.sh

Run instructions (local):
1) Start the backend and DB using docker-compose.mcp.yml (see repo root).
   docker compose -f docker-compose.mcp.yml up -d
2) Seed the DB (optional):
   MSSQL_SA_PASSWORD=YourStrong!Pass scripts/seed-test.sh
3) Run API tests:
   cd tests/api
   npm ci
   npm test
4) Run E2E tests:
   cd tests/e2e
   npm ci
   npx playwright install --with-deps
   npm test

CI snippet (GitHub Actions):

name: E2E & API Tests
on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    services:
      mssql:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          SA_PASSWORD: ${{ secrets.MSSQL_SA_PASSWORD }}
          ACCEPT_EULA: "Y"
        ports:
          - 14331:1433
        options: >-
          --health-cmd="/opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P '${{ secrets.MSSQL_SA_PASSWORD }}' -Q 'select 1'" \
          --health-interval=10s --health-timeout=5s --health-retries=10
    steps:
      - uses: actions/checkout@v4
      - name: Start docker-compose services
        run: docker compose -f docker-compose.mcp.yml up -d
      - name: Seed database
        env:
          MSSQL_SA_PASSWORD: ${{ secrets.MSSQL_SA_PASSWORD }}
        run: scripts/seed-test.sh
      - name: Run API tests
        run: |
          cd tests/api
          npm ci
          npm test
      - name: Run E2E tests
        run: |
          cd tests/e2e
          npm ci
          npx playwright install --with-deps
          npm test

Notes:
- The Playwright E2E tests are intentionally API-driven to make them deterministic and fast. They exercise registration, role assignment, baseline selection, SAP generation (positive/negative), assessment import, report generation, AO authorization, and ConMon alerting via direct HTTP calls to the MCP API.
- Role fixtures assert allowed/forbidden paths; the tests as written do not attempt to check the server-side RBAC enforcement automatically — integrate with the simulated roles in src/Ato.Copilot.Mcp/appsettings.Development.json (SimulatedRoles) for test parity.

