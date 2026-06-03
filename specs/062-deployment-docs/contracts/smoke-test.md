# Smoke Test Contract — 062

## Purpose
Post-deploy validation script that verifies a freshly deployed ATO Copilot instance is
healthy before traffic is routed to it. Designed to run from CI/CD pipeline or manually
from an operator terminal.

## Script: `scripts/smoke-test.sh`

### Inputs (environment variables)

| Variable | Required | Description |
|----------|----------|-------------|
| `SMOKE_BASE_URL` | Yes | Base URL of deployed instance (e.g., `https://ato-copilot.azurecontainerapps.io`) |
| `SMOKE_BEARER_TOKEN` | Yes | Short-lived bearer token for authenticated calls |
| `SMOKE_TIMEOUT` | No | Per-request timeout in seconds (default: 10) |
| `SMOKE_VERBOSE` | No | Set to `1` for verbose curl output |

### Test Cases

| # | Test | Endpoint | Method | Expected |
|---|------|----------|--------|----------|
| 1 | Health check | `/health` | GET | HTTP 200, body `{"status":"healthy"}` |
| 2 | Auth identity | `/api/auth/me` | GET (bearer) | HTTP 200, body contains `"tenantId"` |
| 3 | Systems list | `/api/dashboard/systems` | GET (bearer) | HTTP 200, body contains `"items"` |
| 4 | NIST controls | `/api/dashboard/nist-controls?family=AC&pageSize=1` | GET (bearer) | HTTP 200, `"total" > 0` |
| 5 | Deployment mode | `/api/deployment/mode` | GET | HTTP 200 |

### Exit Codes

| Code | Meaning |
|------|---------|
| 0 | All tests passed |
| 1 | One or more tests failed |
| 2 | Invalid configuration (SMOKE_BASE_URL missing) |

### Output Format
```
[SMOKE] ATO Copilot smoke test — <SMOKE_BASE_URL>
[PASS]  Health check (/health) → 200
[PASS]  Auth identity (/api/auth/me) → 200
[FAIL]  Systems list → expected 200, got 503
[SMOKE] Result: 1 FAILED (4 passed, 1 failed)
```

### Usage in CI
```yaml
- name: Smoke test
  run: bash scripts/smoke-test.sh
  env:
    SMOKE_BASE_URL: ${{ steps.deploy.outputs.url }}
    SMOKE_BEARER_TOKEN: ${{ secrets.CI_SMOKE_TOKEN }}
```

### Limitations
- Does not test write paths (POST/PUT/DELETE) to avoid polluting production data
- Does not validate OSCAL export or file uploads
- Bearer token must have at least read-only access to the deployed tenant
