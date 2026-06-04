# Quickstart: Deployment Docs + Operations Runbook (Epic 062)

This guide tells you where to find the operational documentation produced by epic 062 and
how to use the smoke test script.

---

## Smoke Test

### Basic Usage (unauthenticated checks only)

```bash
BASE_URL=https://your-ato-copilot.azurecontainerapps.io \
  bash scripts/smoke-test.sh
```

Expected output (healthy instance):

```
[PASS] Health → HTTP 200
[PASS] Auth middleware → HTTP 401

Results: 2 passed, 0 failed
```

### Full Usage (with authenticated check)

```bash
BASE_URL=https://your-ato-copilot.azurecontainerapps.io \
SERVICE_ACCOUNT_TOKEN=eyJ... \
  bash scripts/smoke-test.sh
```

Expected output (healthy instance):

```
[PASS] Health → HTTP 200
[PASS] Auth middleware → HTTP 401
[PASS] Authenticated API → HTTP 200

Results: 3 passed, 0 failed
```

### Failed Check Example

```
[PASS] Health → HTTP 200
[FAIL] Auth middleware → expected HTTP 401, got HTTP 500
[FAIL] Authenticated API → expected HTTP 200, got HTTP 500

Results: 1 passed, 2 failed
```

Exit code: `1` — roll back or investigate.

---

## Runbook

The operational runbook is at `docs/runbook.md`. It covers:

| Failure Mode | Section |
|---|---|
| MCP server crashes | § MCP Server Crash |
| Database unreachable | § Database Unreachable |
| Azure AI service down | § Azure AI Service Outage |
| Auth broken (all 401 → 500) | § Authentication Failure |
| Teams bot not responding | § M365 Bot Unreachable |
| Database backup / restore | § Database Recovery |

### Quick Reference: Runbook Entry Points

| Symptom | Go to |
|---|---|
| All API calls return 500 | § MCP Server Crash |
| API calls return 500 with "connection refused" in logs | § Database Unreachable |
| Assessment requests time out, basic CRUD works | § Azure AI Service Outage |
| All authenticated calls return 401 or 500 | § Authentication Failure |
| Teams bot shows "Sorry, I ran into an issue" | § M365 Bot Unreachable |

---

## VS Code Extension (.vsix) — Post US3

After epic 062 US3 ships, every build of `main` produces a `.vsix` artifact:

1. Navigate to the GitHub Actions run for the commit you want.
2. Click the `vscode-extension-{sha}` artifact under "Artifacts".
3. Download and extract the `.vsix` file.
4. Install in VS Code:
   ```bash
   code --install-extension ato-copilot-*.vsix
   ```

---

## M365 Bot Deployment — Post US4

See `docs/deployment-m365.md` for the full M365 bot deployment guide (produced by spec 061).
A summary link is also in `docs/deployment.md` under "Microsoft Teams Bot".

---

## Database Backup — Quick Reference

### SQLite (dev / staging)

```bash
# Hot backup (safe while app is running)
DATE=$(date +%Y-%m-%d)
sqlite3 /path/to/ato-copilot.db ".backup backup-${DATE}.sqlite"

# Human-readable SQL dump (for migration / portability)
sqlite3 /path/to/ato-copilot.db .dump > backup-${DATE}.sql
```

### SQL Server (production)

```bash
# Verify automated backup is enabled
az sql db show \
  --resource-group <rg> --server <server> --name <db> \
  --query "earliestRestoreDate"

# Point-in-time restore (creates a new database)
az sql db restore \
  --resource-group <rg> --server <server> \
  --name <db>-restored \
  --source-database <db> \
  --time "2026-06-01T12:00:00Z"
```

See `docs/runbook.md` § Database Recovery for the full procedure including validation steps.
