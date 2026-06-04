# Data Model: Deployment Docs + Operations Runbook (Epic 062)

There are no database entities for this epic. This document covers the **script contracts**
for the two executable artifacts produced by this epic.

---

## 1. smoke-test.sh Contract

See `contracts/smoke-test.md` for the full contract.

### Inputs

| Input | Source | Required | Description |
|---|---|---|---|
| `BASE_URL` | Positional arg or env var | ✅ Yes | Base URL of the deployed ATO Copilot instance |
| `SERVICE_ACCOUNT_TOKEN` | Env var | No | Bearer token for the authenticated API check |

### Outputs

| Output | Type | Description |
|---|---|---|
| stdout | string | Per-check `[PASS]`/`[FAIL]` lines + summary |
| exit code | integer | `0` = all checks passed; `1` = one or more checks failed |

### Check Registry

| Check ID | Endpoint | Method | Expected HTTP | Auth |
|---|---|---|---|---|
| `health` | `/health` | GET | 200 | None |
| `auth-middleware` | `/api/auth/me` | GET | 401 | None |
| `authenticated-api` | `/api/systems` | GET | 200 | Bearer token |

---

## 2. Runbook Structure Contract

See `contracts/runbook-template.md` for the per-failure-mode template.

### Failure Modes Registry

| ID | Failure Mode | Priority | Section in runbook.md |
|---|---|---|---|
| FM-1 | MCP server crash / unexpected exit | High | § MCP Server Crash |
| FM-2 | Database unreachable | High | § Database Unreachable |
| FM-3 | Azure AI service outage | Medium | § Azure AI Service Outage |
| FM-4 | Authentication failure | High | § Authentication Failure |
| FM-5 | M365 bot unreachable | Medium | § M365 Bot Unreachable |

### Runbook Entry Fields

Each failure mode entry must include all of the following:

```
symptoms:         string[]   — Observable signs that this failure mode is active
diagnosis_steps:  Step[]     — Ordered list of commands/checks to confirm the diagnosis
remediation:      Step[]     — Ordered list of actions to resolve the failure
escalation:       Contact[]  — Who to contact if remediation steps fail
rollback:         string     — How to revert to the last known-good state
```

### Step Shape

```typescript
interface Step {
  description: string;    // Human-readable explanation
  command?: string;       // Shell command to run (if applicable)
  expected_output?: string; // What a successful result looks like
}
```

---

## 3. .vsix Artifact Naming Convention

| Build Type | Artifact Name Pattern | Retention |
|---|---|---|
| main branch | `vscode-extension-{short-sha}.vsix` | 90 days |
| PR build | `vscode-extension-pr{number}-{short-sha}.vsix` | 30 days |

The `.vsix` file is a standard VS Code Extension package produced by `vsce package`. It is not
signed for this epic (code signing is tracked as a future hardening task).

---

## 4. Database Backup Artifact Convention

| Database | Backup Format | Naming Pattern | Recommended Retention |
|---|---|---|---|
| SQLite (dev) | SQL dump (`.sql`) | `backup-{YYYY-MM-DD}.sql` | 7 most recent |
| SQL Server (prod) | Azure SQL automated (managed by Azure) | N/A — managed by Azure | 7–35 days (configurable in Azure) |
