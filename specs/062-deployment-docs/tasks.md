# Tasks: Deployment Docs + Operations Runbook (Epic 062)

All tasks map to GitHub Issue #130. Priority levels: P1 = required for epic completion,
P2 = scheduled for this epic but can slip to a follow-up.

---

## P1 Tasks

### T062-01 — Create docs/runbook.md
**Story**: US1, US5  
**File**: `docs/runbook.md` (new)  
**Effort**: M (1–2 days)

Steps:
1. Create `docs/runbook.md` using the template from `contracts/runbook-template.md`.
2. Write the following sections:
   - **Overview**: purpose of the runbook, scope, escalation chain placeholder.
   - **Failure Mode 1 — MCP Server Crash**: symptoms, diagnosis, remediation, escalation.
   - **Failure Mode 2 — Database Unreachable**: symptoms, diagnosis, remediation, escalation.
   - **Failure Mode 3 — Azure AI Service Outage**: symptoms, diagnosis, remediation, escalation.
   - **Failure Mode 4 — Authentication Failure**: symptoms, diagnosis, remediation, escalation.
   - **Failure Mode 5 — M365 Bot Unreachable**: symptoms, diagnosis, remediation, escalation.
   - **Database Recovery**: SQLite backup/restore + SQL Server backup/restore procedures.
   - **Common Diagnostic Commands**: log queries, health checks, connection tests.
3. Add a link to `docs/runbook.md` near the top of `docs/deployment.md`.
4. Technical review: an engineer unfamiliar with the project follows the diagnosis steps for
   "Database Unreachable" and can complete them without additional help.

**Done when**: `docs/runbook.md` exists, covers all 5 failure modes + database recovery,
and is linked from `docs/deployment.md`.

---

### T062-02 — Create scripts/smoke-test.sh
**Story**: US2  
**File**: `scripts/smoke-test.sh` (new)  
**Effort**: S (half-day)

Steps:
1. Create `scripts/smoke-test.sh`:
   ```bash
   #!/usr/bin/env bash
   set -euo pipefail

   BASE_URL="${1:-${BASE_URL:?'BASE_URL required'}}"
   SERVICE_ACCOUNT_TOKEN="${SERVICE_ACCOUNT_TOKEN:-}"
   PASS=0; FAIL=0

   check() {
     local label="$1" url="$2" expected="$3"
     local actual
     actual=$(curl -s -o /dev/null -w "%{http_code}" "$url")
     if [ "$actual" = "$expected" ]; then
       echo "[PASS] $label → HTTP $actual"
       ((PASS++))
     else
       echo "[FAIL] $label → expected HTTP $expected, got HTTP $actual"
       ((FAIL++))
     fi
   }

   check "Health"             "$BASE_URL/health"       "200"
   check "Auth middleware"    "$BASE_URL/api/auth/me"  "401"

   if [ -n "$SERVICE_ACCOUNT_TOKEN" ]; then
     actual=$(curl -s -o /dev/null -w "%{http_code}" \
       -H "Authorization: Bearer $SERVICE_ACCOUNT_TOKEN" \
       "$BASE_URL/api/systems")
     if [ "$actual" = "200" ]; then
       echo "[PASS] Authenticated API → HTTP $actual"
       ((PASS++))
     else
       echo "[FAIL] Authenticated API → expected HTTP 200, got HTTP $actual"
       ((FAIL++))
     fi
   fi

   echo ""
   echo "Results: $PASS passed, $FAIL failed"
   [ "$FAIL" -eq 0 ] || exit 1
   ```
2. Make executable: `chmod +x scripts/smoke-test.sh`.
3. Test locally against a running dev instance.
4. Document in `contracts/smoke-test.md`.

**Done when**: script exits 0 on a healthy instance, exits 1 on a broken instance, uses only
`curl` and `bash`.

---

## P2 Tasks

### T062-03 — CI job: produce .vsix artifact
**Story**: US3  
**File**: `.github/workflows/ci.yml` (or new `.github/workflows/vscode-package.yml`)  
**Effort**: S (half-day)

Steps:
1. Add job `vscode-package`:
   ```yaml
   vscode-package:
     runs-on: ubuntu-latest
     steps:
       - uses: actions/checkout@v4
       - uses: actions/setup-node@v4
         with: { node-version: '20' }
       - run: npm ci
         working-directory: extensions/vscode
       - run: npm install -g @vscode/vsce
       - run: vsce package --out ato-copilot-${{ github.sha }}.vsix
         working-directory: extensions/vscode
       - uses: actions/upload-artifact@v4
         with:
           name: vscode-extension-${{ github.sha }}
           path: extensions/vscode/ato-copilot-*.vsix
           retention-days: ${{ github.ref == 'refs/heads/main' && 90 || 30 }}
   ```
2. Add path filter: trigger on `extensions/vscode/**` changes and on `push` to `main`.
3. Verify the produced `.vsix` installs in VS Code 1.85+.

**Done when**: a main branch build produces a downloadable `.vsix` artifact in GitHub Actions.

---

### T062-04 — Add M365 bot section to docs/deployment.md
**Story**: US4  
**Depends on**: Spec 061 T061-06 (docs/deployment-m365.md)  
**File**: `docs/deployment.md`  
**Effort**: XS (1 hour)

Steps:
1. Add a "Microsoft Teams Bot" section to the table of contents in `docs/deployment.md`.
2. Add a body section:
   ```markdown
   ## Microsoft Teams Bot

   The ATO Copilot Teams bot (`extensions/m365/`) requires a separate deployment process,
   including Azure Bot Service registration and Azure Container Apps hosting.

   → See [M365 Bot Deployment Guide](./deployment-m365.md) for full instructions.

   **Dependencies**:
   - Azure Bot Service registration (see deployment-m365.md § Bot Service)
   - Azure Container Apps environment (shared with or separate from the main API)
   - AAD app registration for SSO (see deployment-m365.md § Azure Bot Service)
   ```
3. Verify the link resolves correctly in GitHub Markdown rendering.

**Done when**: `docs/deployment.md` includes the M365 section and link; a new operator
following the guide top-to-bottom encounters the M365 bot.

---

### T062-05 — Document backup/restore in runbook
**Story**: US5  
**File**: `docs/runbook.md` (part of T062-01)  
**Effort**: S (2–3 hours) — included in T062-01 scope, tracked separately for acceptance

Steps:
1. In `docs/runbook.md` "Database Recovery" section, write:
   - SQLite backup command + recommended cron entry.
   - SQLite restore procedure (stop app → restore → restart).
   - SQL Server: verify Azure SQL automated backup is enabled (Portal: Database → Backups).
   - SQL Server: point-in-time restore CLI command.
   - Validation: how to verify data integrity after restore.
2. Add cross-reference link from `docs/deployment.md` "Database" section.

**Done when**: both SQLite and SQL Server procedures are documented with copy-pasteable commands.

---

## Completion Criteria

P1 tasks (T062-01, T062-02) must be complete for epic 062 to close.
P2 tasks (T062-03 through T062-05) are required for full production readiness but may ship in a
fast-follow.
