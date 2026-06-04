# Feature Specification: Deployment Docs + Operations Runbook

**Feature Branch**: `spec/062-deployment-docs`
**Created**: 2026-06-03
**Status**: Draft
**GitHub Issue**: #130
**Related**: Spec 064 (Bicep/IaC) — tracked separately; this epic does not author Bicep files
beyond what spec 061 adds for the M365 bot.

## Background

ATO Copilot has a substantial `docs/deployment.md` (646 lines) that covers Docker, Azure
Container Apps, App Service, env vars, migrations, security, monitoring, and a brief runbook
section. The architecture is documented across `docs/architecture/` and per-persona quickstarts
exist in `docs/getting-started/`.

Despite this coverage, several critical operational gaps remain:

1. **No standalone operational runbook**: The runbook content embedded in `deployment.md` is
   a deployment guide addendum, not a structured incident response document. There are no
   documented response procedures for the top failure modes operators will encounter.
2. **No incident response procedures**: What does an on-call engineer do when the MCP server
   crashes? When the database is unreachable? When Azure AI is down? When auth breaks?
   When the M365 bot stops responding? None of this is documented.
3. **No smoke test script**: After every deployment, operators have no automated way to verify
   the system is accepting traffic and returning correct responses. Manual verification is ad hoc.
4. **No CI deploy job**: Deployment is entirely manual. There is no automated path from a green
   main build to a deployed artifact.
5. **No backup/restore procedure**: `docs/deployment.md` mentions database migration but does
   not document how to back up or restore the SQLite (dev) or SQL Server (prod) database.
6. **M365 bot deployment not documented**: The Teams bot (`extensions/m365/`) has no entry in
   any deployment guide. Spec 061 will produce `docs/deployment-m365.md`; this epic ensures
   it is linked and the gap is closed.
7. **VS Code extension `.vsix` not a CI artifact**: The VS Code extension is not packaged or
   distributed through CI; the artifact is not available for staged rollouts or manual
   installation.
8. **No Bicep/IaC**: Tracked by spec 064. This epic documents the gap and notes the dependency.

### Verified State of the Code (current `main`)

- `docs/deployment.md` — 646 lines, no TODO/stub markers; doc is written but has no standalone
  runbook
- `docs/architecture/` — auth, data-model, overview, RMF step map, security, tenant isolation
- `docs/getting-started/` — ao.md, isso.md, issm.md, sca.md, engineer.md
- `scripts/bootstrap.sh`, `scripts/seed-*.sh` — dev/seed scripts, NOT production deploy
- No `scripts/smoke-test.sh`
- No `docs/runbook.md`
- CI: no deploy job, no `.vsix` artifact job
- No Bicep files

## Clarifications

### Design Decisions

- **Q: Should `docs/runbook.md` be a standalone file or a section in `deployment.md`?**
  **A:** Standalone `docs/runbook.md`. The deployment guide explains *how* to deploy; the
  runbook explains *what to do when it breaks*. Operators in an incident should not have to
  scroll through deployment instructions to find remediation steps.

- **Q: Which failure modes should the initial runbook cover?**
  **A:** Top 5 by likelihood and impact:
  1. MCP server crashes / process exits unexpectedly
  2. Database unreachable (SQLite file locked, SQL Server connection refused)
  3. Azure AI service outage (OpenAI / Azure OpenAI 503)
  4. Authentication failure (AAD / MSAL errors, token signing key rotation)
  5. M365 bot unreachable (Bot Framework messaging endpoint returns 502/503)

- **Q: What should `smoke-test.sh` validate?**
  **A:** Minimum viable post-deploy check:
  1. `GET /health` → 200
  2. `GET /api/auth/me` → 401 (verifies auth middleware is loaded, not 500)
  3. One authenticated API call using a service-account token → 200
  Exit code 0 = all checks passed. Exit code 1 = at least one check failed. Each failure prints
  the endpoint, expected response, and actual response.

- **Q: Should the smoke test be idempotent (safe to run multiple times)?**
  **A:** Yes. The authenticated call must be a `GET` to a read-only endpoint. No mutations.

- **Q: Should CI produce a `.vsix` artifact on every PR or only on pushes to `main`?**
  **A:** On every push to `main` and on PRs that touch `extensions/vscode/**`. The artifact
  is retained for 30 days on PRs and 90 days on main builds.

- **Q: What is the backup strategy for SQLite?**
  **A:** SQLite: `sqlite3 ato-copilot.db .dump > backup.sql` — documented as a cron job or
  pre-deploy step. SQL Server: Azure SQL automated backups (point-in-time restore). Both
  procedures documented in `docs/runbook.md` under "Database Recovery".

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Operational runbook for top 5 failure modes (Priority: P1)

**As an** on-call engineer responding to a production incident
**I want** a structured runbook that tells me the symptoms, diagnosis steps, and remediation for
each common failure mode
**So that** I can resolve incidents quickly without tribal knowledge or guesswork.

**Why this priority**: P1 because the absence of an operational runbook means every incident
requires reverse-engineering the system under pressure. The content is deterministic — the
failure modes are known — and the cost of not having it is measured in MTTR (mean time to
resolve).

**Independent Test**: Present the runbook to an engineer unfamiliar with the codebase; have
them simulate the "Database unreachable" failure mode (point `DATABASE_URL` at a non-existent
host) and follow the runbook to diagnose and remediate. Record time to resolution. Delivers
standalone value because the runbook is useful regardless of whether the smoke test (US2) ships.

**Acceptance**
- `docs/runbook.md` exists and follows the structure defined in
  `contracts/runbook-template.md`.
- Covers all 5 failure modes: MCP crash, DB unreachable, Azure AI outage, auth failure,
  M365 bot unreachable.
- Each failure mode section includes: **Symptoms**, **Diagnosis** (commands to run),
  **Remediation** (steps to execute), **Escalation** (who to contact if unresolved).
- Includes a "Database Recovery" section with backup/restore procedures for SQLite and
  SQL Server.
- The runbook is linked from `docs/deployment.md` (add a "→ Operations Runbook" link at the
  top of the deployment guide).
- A technical reviewer who has never seen the document can execute the diagnosis steps for
  any failure mode without additional context.

### User Story 2 — Smoke test script validates deployed instance (Priority: P1)

**As an** operator who just deployed a new version of ATO Copilot
**I want** to run a single script that validates the deployment is healthy
**So that** I know immediately whether to proceed or roll back.

**Why this priority**: P1 because manual post-deploy checks are inconsistent and slow.
A scripted smoke test is the minimum viable automated validation layer and takes less than one
day to implement.

**Independent Test**: Deploy a known-good version; run `scripts/smoke-test.sh`; verify exit
code 0 and all check results printed as "PASS". Then misconfigure the server (break the DB
connection); run again; verify exit code 1 and at least one check printed as "FAIL" with the
actual response. Delivers standalone value immediately on any deployed instance.

**Acceptance**
- `scripts/smoke-test.sh` exists and is executable.
- Accepts `BASE_URL` as a required env var or positional argument.
- Accepts `SERVICE_ACCOUNT_TOKEN` as an optional env var for the authenticated check.
- Checks:
  1. `GET {BASE_URL}/health` → expects HTTP 200; body contains `"status":"ok"`.
  2. `GET {BASE_URL}/api/auth/me` → expects HTTP 401 (not 500 — auth middleware must load).
  3. If `SERVICE_ACCOUNT_TOKEN` is set: `GET {BASE_URL}/api/systems` with
     `Authorization: Bearer {token}` → expects HTTP 200.
- Each check prints: `[PASS]` or `[FAIL]` + endpoint + expected + actual HTTP status.
- Exit code 0 if all checks pass; exit code 1 if any check fails.
- Script uses only `curl` and `bash` (no Node.js, Python, or other runtimes required).
- Script is idempotent (read-only operations only, safe to run multiple times).
- The contract is documented in `contracts/smoke-test.md`.

### User Story 3 — CI produces `.vsix` artifact for VS Code extension (Priority: P2)

**As a** developer or QA engineer
**I want** every main branch build to produce a downloadable `.vsix` extension artifact
**So that** I can install specific builds for testing or staged rollout without building locally.

**Why this priority**: P2 because the VS Code extension currently has no CI distribution path.
Manual `.vsix` builds require cloning the repo and running build commands locally, which is a
friction barrier for QA and prevents staged rollouts.

**Independent Test**: Push a commit to `main` that touches `extensions/vscode/**`; navigate to
the GitHub Actions run; download the `vscode-extension` artifact; install it with
`code --install-extension ato-copilot-*.vsix`; verify the extension loads in VS Code.

**Acceptance**
- A GitHub Actions job `vscode-package` runs on push to `main` and on PRs touching
  `extensions/vscode/**`.
- Steps: checkout → `npm ci` in `extensions/vscode/` → `npx vsce package` → upload artifact.
- Artifact name: `vscode-extension-{sha}.vsix` (or similar unique name).
- Artifact retention: 90 days for main builds, 30 days for PR builds.
- The `.vsix` file installs cleanly in VS Code 1.85+ (no error on install).

### User Story 4 — M365 bot deployment steps in deployment guide (Priority: P2)

**As an** operator deploying the full ATO Copilot stack
**I want** the M365 bot deployment to be included in the main deployment guide
**So that** I don't have to find a separate document or discover the bot through the codebase.

**Why this priority**: P2 because this is a linking/cross-reference task that depends on
spec 061 (US4) producing `docs/deployment-m365.md`. Once that doc exists, this story
closes the discovery gap.

**Independent Test**: Navigate to `docs/deployment.md`; confirm a "Microsoft Teams Bot" section
or prominent link to `docs/deployment-m365.md` exists; confirm a new operator following
`deployment.md` top to bottom would not miss the M365 bot.

**Acceptance**
- `docs/deployment.md` includes a "Microsoft Teams Bot" section (or cross-reference) that:
  - Briefly describes what the M365 bot is and when to deploy it.
  - Links to `docs/deployment-m365.md` (produced by spec 061).
  - Notes required dependencies (Azure Bot Service registration, spec 061 US5).
- The M365 bot section appears in the table of contents of `deployment.md`.

### User Story 5 — Backup/restore procedure documented (Priority: P2)

**As an** operator managing an ATO Copilot production instance
**I want** a documented backup and restore procedure for both SQLite (dev) and SQL Server (prod)
**So that** I can recover data after a failure without improvising.

**Why this priority**: P2 because the current deployment guide covers database migration but not
backup or restore. Any production deployment without a backup procedure is a data-loss risk.

**Independent Test**: Follow the documented SQLite backup procedure on a dev instance; verify a
`.sql` dump is produced; stop the bot; delete the SQLite file; follow the restore procedure;
restart the bot; verify data is intact. Deliver standalone value because the procedure is
environment-independent.

**Acceptance**
- `docs/runbook.md` "Database Recovery" section includes:
  - **SQLite backup**: `sqlite3 ato-copilot.db .dump > backup-{date}.sql` — documented with
    recommended cron schedule (daily) and retention (7 backups).
  - **SQLite restore**: stop app → replace `.db` file or import `.sql` dump → restart app.
  - **SQL Server backup**: Azure SQL automated backup policy (how to verify it is enabled,
    how to initiate a point-in-time restore from the Azure Portal or CLI).
  - **SQL Server restore**: CLI command for point-in-time restore + validation steps.
- All commands are copy-pasteable.
- Backup procedures are linked from `docs/deployment.md`.

## Out of Scope

- Bicep/IaC files (tracked by spec 064)
- On-call rotation scheduling or PagerDuty/OpsGenie configuration
- Multi-region deployment
- Disaster recovery beyond single-region restore
- Automated rollback mechanism in CI (requires deploy job first)

## Dependencies

| Dependency | Type | Notes |
|---|---|---|
| Spec 061 (M365 bot) | Sibling | US4 of this epic depends on `docs/deployment-m365.md` from spec 061 |
| Spec 064 (Bicep/IaC) | Future | Deployment docs will be updated once Bicep modules exist |
| `SERVICE_ACCOUNT_TOKEN` | Infrastructure | Smoke test authenticated check requires a service account with read-only access |
| `vsce` CLI | Tool | Required for `.vsix` packaging in CI |
