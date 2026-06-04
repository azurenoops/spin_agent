# Research: Deployment Docs + Operations Runbook (Epic 062)

## 1. Runbook Best Practices

**SRE Workbook (Google)** recommendations for operational runbooks:
- Runbooks are not tutorials. They are decision trees and command sequences for engineers
  under pressure. Every line should be actionable.
- Include "expected output" for every diagnostic command so the engineer can confirm they're
  in the right failure mode before executing remediation.
- Runbooks rot unless they are tested. Schedule a quarterly "fire drill" where an engineer
  follows the runbook on a staging environment.
- Keep runbooks version-controlled alongside code (in `docs/`), not in a wiki that diverges.

**Structure adopted for this epic** (per `contracts/runbook-template.md`):
1. Symptoms — what you observe
2. Diagnosis — commands to confirm the failure mode
3. Remediation — commands to fix it
4. Escalation — who to call if remediation fails
5. Rollback — how to revert to the prior state

---

## 2. Smoke Test Implementation Options

**Option A — Pure bash + curl** (selected):
- Zero runtime dependency beyond bash and curl (available on every CI runner and production host).
- Simple to read and modify.
- Sufficient for the checks required (HTTP status code validation).
- Limitation: cannot easily validate response body structure beyond simple `grep`.

**Option B — Node.js script** (rejected for primary):
- Richer response validation (JSON schema, content checks).
- Requires Node.js on the deployment host.
- Adds a runtime dependency for a script that should work anywhere.

**Option C — k6 / Artillery load test** (out of scope):
- Load testing, not smoke testing.
- Overkill for post-deploy health verification.

**Decision**: Option A for US2. If deeper response validation is needed in the future, a
Node.js companion (`scripts/smoke-test.js`) can be added without replacing the bash script.

---

## 3. GitHub Actions .vsix Packaging

**`@vscode/vsce`** is the official VS Code Extension packaging tool (formerly `vsce`):

```bash
npm install -g @vscode/vsce
vsce package --out my-extension-1.0.0.vsix
```

Requirements:
- `package.json` must have `publisher`, `engines.vscode`, `main`, and `contributes` fields.
- `README.md` and `CHANGELOG.md` should exist (warnings if missing but not blocking).
- Icon referenced in `package.json` must exist.

**Artifact retention** in GitHub Actions:
- `actions/upload-artifact@v4` supports `retention-days` parameter.
- Max retention: 90 days (GitHub free/team plans).
- Use a conditional expression for different retention on main vs PR builds.

---

## 4. SQLite Backup Options

| Method | Command | Notes |
|---|---|---|
| SQL dump | `sqlite3 db.sqlite .dump > backup.sql` | Human-readable; can restore to any SQLite version |
| Binary copy | `cp db.sqlite db.sqlite.bak` | Fast; must ensure WAL is checkpointed first |
| `.backup` command | `sqlite3 db.sqlite ".backup backup.sqlite"` | Hot backup; safe while app is running |

**Recommended**: `.backup` command (hot backup, safe while running) for daily automated backups.
SQL dump for pre-migration backups (human-readable, portable).

Pre-migration backup command:
```bash
DATE=$(date +%Y-%m-%d)
sqlite3 ato-copilot.db ".backup backup-${DATE}.sqlite"
# Also produce a SQL dump for portability
sqlite3 ato-copilot.db .dump > backup-${DATE}.sql
```

---

## 5. Azure SQL Server Backup

Azure SQL Database provides:
- **Automated backups**: full weekly, differential daily, transaction log every 5–12 minutes.
- **Point-in-time restore (PITR)**: restore to any point within the retention period (7–35 days,
  configurable).
- **Long-term retention (LTR)**: optional weekly/monthly/yearly backups for compliance.

**Verify automated backup is enabled**:
```bash
az sql db show \
  --resource-group <rg> \
  --server <server> \
  --name <db> \
  --query "earliestRestoreDate"
```
A non-null `earliestRestoreDate` confirms automated backups are active.

**Point-in-time restore**:
```bash
az sql db restore \
  --resource-group <rg> \
  --server <server> \
  --name <db>-restored \
  --source-database <db> \
  --time "2026-06-01T12:00:00Z"
```

---

## 6. Failure Mode Analysis

### FM-1: MCP Server Crash

**Likely causes**:
- Uncaught exception in the MCP server process
- Out-of-memory (OOM) kill
- Azure Container Apps scaling event (graceful restart, not crash)
- Dependency service unavailable at startup

**Key signals**: Container Apps restart count > 0; application logs showing unhandled exception;
health probe failures.

### FM-2: Database Unreachable

**Likely causes**:
- SQL Server firewall rule change
- Connection string rotation (secret update not propagated)
- SQL Server maintenance window / DTU exhaustion
- SQLite file permissions (dev only)

**Key signals**: API returning 500 with "connection refused" or "login failed"; health endpoint
may still return 200 if health check does not test DB.

### FM-3: Azure AI Service Outage

**Likely causes**:
- Azure OpenAI regional outage
- API key rotation / quota exceeded
- Model version deprecated

**Key signals**: Assessment requests timing out or returning 503; AI features degraded but
non-AI features (OSCAL management, control tracking) continue working.

### FM-4: Authentication Failure

**Likely causes**:
- AAD signing key rotation (automatic, happens periodically)
- MSAL library bug or version mismatch
- Misconfigured tenant ID or client ID in env vars
- Token expiry edge case (clock skew)

**Key signals**: All authenticated API calls return 401; `/api/auth/me` returns 500 (not 401).

### FM-5: M365 Bot Unreachable

**Likely causes**:
- Container Apps ingress misconfigured (HTTP not HTTPS — Bot Framework rejects HTTP)
- Azure Bot Service messaging endpoint not updated after redeployment
- `BOT_ID`/`BOT_PASSWORD` secret rotation not propagated
- Bot process crashed (same as FM-1 but for the M365 container)

**Key signals**: Teams shows "Sorry, I ran into an issue" or no response; Bot Framework test
console returns 502/503.
