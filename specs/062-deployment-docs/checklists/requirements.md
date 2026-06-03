# Requirements Checklist: Deployment Docs + Operations Runbook (Epic 062)

Use this checklist during PR review and acceptance testing. All P1 items must be checked before
the epic is marked Done.

---

## P1 Requirements

### US1 — Operational Runbook

- [ ] `docs/runbook.md` exists
- [ ] Runbook follows the structure in `contracts/runbook-template.md`
- [ ] **FM-1 MCP Server Crash**: Symptoms, Diagnosis, Remediation, Escalation, Rollback all present
- [ ] **FM-2 Database Unreachable**: all 5 sections present; diagnosis includes connection test command
- [ ] **FM-3 Azure AI Service Outage**: all 5 sections present; includes Azure Service Health check link
- [ ] **FM-4 Authentication Failure**: all 5 sections present; includes AAD signing key rotation procedure
- [ ] **FM-5 M365 Bot Unreachable**: all 5 sections present; includes Bot Framework messaging endpoint verification
- [ ] **Database Recovery** section present with SQLite backup + restore + SQL Server PITR
- [ ] All commands in the runbook are copy-pasteable (no redacted commands)
- [ ] `docs/deployment.md` links to `docs/runbook.md` near the top (or in a "See Also" section)
- [ ] Technical review: unfamiliar engineer can follow FM-2 diagnosis on a dev instance without additional help

### US5 — Backup/Restore Procedures (merged into T062-01)

- [ ] SQLite backup command: `.backup` command (hot backup) documented
- [ ] SQLite backup retention guidance: 7 most recent backups recommended
- [ ] SQLite restore procedure: stop app → restore file → restart app
- [ ] SQL Server: `az sql db show ... --query "earliestRestoreDate"` documented for verification
- [ ] SQL Server: `az sql db restore` point-in-time restore command documented
- [ ] SQL Server restore validation steps documented
- [ ] All commands tested on a real instance (SQLite) or verified against Azure docs (SQL Server)

### US2 — Smoke Test Script

- [ ] `scripts/smoke-test.sh` exists and is executable (`chmod +x`)
- [ ] Script uses only `bash` and `curl` (no Node.js, Python, jq required)
- [ ] Accepts `BASE_URL` as positional arg or env var
- [ ] Accepts `SERVICE_ACCOUNT_TOKEN` as optional env var
- [ ] Check 1: `GET /health` → expects HTTP 200
- [ ] Check 2: `GET /api/auth/me` → expects HTTP 401
- [ ] Check 3 (if token provided): `GET /api/systems` with auth → expects HTTP 200
- [ ] Each check prints `[PASS]` or `[FAIL]` + endpoint + expected + actual HTTP status
- [ ] Prints summary: "N passed, N failed"
- [ ] Exit code 0 if all checks pass
- [ ] Exit code 1 if any check fails
- [ ] Script is idempotent (read-only; safe to run multiple times)
- [ ] `contracts/smoke-test.md` is the documented specification for the script

---

## P2 Requirements

### US3 — .vsix CI Artifact

- [ ] `vscode-package` CI job exists in `.github/workflows/`
- [ ] Job triggers on push to `main`
- [ ] Job triggers on PRs touching `extensions/vscode/**`
- [ ] Steps: checkout → `npm ci` → `vsce package` → `upload-artifact`
- [ ] Artifact name includes commit SHA
- [ ] Retention: 90 days on main, 30 days on PR
- [ ] The produced `.vsix` installs in VS Code 1.85+ without errors

### US4 — M365 Bot in Deployment Guide

- [ ] `docs/deployment.md` table of contents includes "Microsoft Teams Bot" entry
- [ ] Body section briefly describes the M365 bot and when to deploy it
- [ ] Link to `docs/deployment-m365.md` is present and resolves correctly
- [ ] Required dependencies listed (Azure Bot Service, AAD app registration)
- [ ] A new operator following `deployment.md` top-to-bottom encounters the M365 bot section

---

## Non-Functional Requirements

- [ ] `scripts/smoke-test.sh` has no hardcoded URLs or tokens
- [ ] `docs/runbook.md` does not embed secrets or internal hostnames — uses `<placeholder>` syntax
- [ ] All documentation is reviewed for accuracy against the current `main` branch codebase
- [ ] `docs/runbook.md` is linked from the project `README.md` (or `docs/README.md`) under
      "Operations"
