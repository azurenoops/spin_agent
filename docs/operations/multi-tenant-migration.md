# Multi-Tenant Migration Runbook

> **Spec**: [`specs/048-tenant-isolation/spec.md`](../../specs/048-tenant-isolation/spec.md) · **Architecture**: [`docs/architecture/tenant-isolation.md`](../architecture/tenant-isolation.md)

## Audience

Operations engineers migrating an existing **SingleTenant** ATO Copilot
deployment to **MultiTenant** mode. This is a one-way operation: rolling back
requires a database restore.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Pre-Flight Checklist](#pre-flight-checklist)
3. [Migration Steps](#migration-steps)
4. [Rollback](#rollback)
5. [Troubleshooting](#troubleshooting)
6. [FAQ](#faq)

---

## Prerequisites

- SQL Server 2019+ (production). SQLite cannot enable Row-Level Security and is
  not supported as a migration source.
- A user account with `db_owner` on the target database (the migration installs
  RLS policies and `SECURITY POLICY` objects).
- A user account with `CSP.Admin` role for any UI/HTTP path; the CLI tool
  authenticates against the database directly and bypasses Entra.
- A current backup of the target database (see §3.1).
- The current build of `ato-cli` deployed on the migration host.

## Pre-Flight Checklist

Run all of these before §3.

- [ ] Application is in maintenance mode (no inbound traffic).
- [ ] Recent backup exists and has been **restore-tested** to a scratch DB.
- [ ] `appsettings.Production.json` has both `Deployment:Mode` (currently
      `SingleTenant`) and the connection string available for editing.
- [ ] At least one row exists in the target `Tenants` table corresponding to
      the migrated default. If none exists, create it via
      `POST /api/admin/seed-default-tenant` or run
      `ato-cli tenant create --display-name "<your CSP>"` and capture the
      resulting GUID for `--default-tenant-id`.
- [ ] If you have multiple legal entities sharing one DB, prepare a CSV
      mapping file (see §3.2). Otherwise the default tenant id covers
      everything.

## Migration Steps

### 3.1 Backup the Database

```pwsh
sqlcmd -S <server> -d master -Q "BACKUP DATABASE [AtoCopilot] TO DISK='X:\backups\AtoCopilot-pre-mt.bak' WITH COMPRESSION, INIT"
```

Verify the backup is readable:

```pwsh
sqlcmd -S <server> -d master -Q "RESTORE VERIFYONLY FROM DISK='X:\backups\AtoCopilot-pre-mt.bak'"
```

### 3.2 Dry Run / Preview

The migration tool exposes a **preview** mode that lists the per-table row
counts and the chosen tenant assignment without writing anything.

**Via HTTP** (CSP-Admin only):

```http
GET /api/admin/migrate-to-multitenant/preview
Authorization: Bearer <csp-admin-token>
```

**Via CLI** (air-gapped):

```bash
ato-cli tenant migrate \
  --connection-string "Server=...;Database=AtoCopilot;..." \
  --default-tenant-id 11111111-1111-1111-1111-111111111111 \
  --csv ./tenant-overrides.csv \
  --install-rls=false \
  --report-out ./preview.json
```

> The CLI does not have a separate `--dry-run` flag; running with
> `--install-rls=false` and inspecting the report before the second run is the
> recommended dry-run pattern. The HTTP `/preview` endpoint is the no-write
> equivalent.

`tenant-overrides.csv` columns (no header required; header line ignored if
present):

```csv
TableName,RowIdPrefix,TenantId
RegisteredSystems,prod-,22222222-2222-2222-2222-222222222222
Persons,contractor-,33333333-3333-3333-3333-333333333333
```

The `RowIdPrefix` is matched against the row's primary key as a string prefix.
Rows that match no override fall back to `--default-tenant-id`.

### 3.3 Execute

**Via HTTP**:

```http
POST /api/admin/migrate-to-multitenant
Authorization: Bearer <csp-admin-token>
Content-Type: application/json

{
  "defaultTenantId": "11111111-1111-1111-1111-111111111111",
  "overrides": [
    { "tableName": "RegisteredSystems", "rowIdPrefix": "prod-", "tenantId": "22222222-2222-2222-2222-222222222222" }
  ],
  "installRls": true
}
```

**Via CLI**:

```bash
ato-cli tenant migrate \
  --connection-string "Server=...;Database=AtoCopilot;..." \
  --default-tenant-id 11111111-1111-1111-1111-111111111111 \
  --csv ./tenant-overrides.csv \
  --install-rls=true \
  --report-out ./migration-report.json
```

The output `migration-report.json` is the canonical record of what was
written. Archive it next to the backup file. Both the HTTP endpoint and the
CLI emit an `AuditLogEntry` with the action verb and a correlation id.

### 3.4 Verify Tenant Isolation

After the run, before reopening traffic:

1. Pick two tenant ids from `Tenants`. Run a control query as each:

   ```sql
   EXEC sp_set_session_context 'TenantId', '<tenant-A-guid>';
   SELECT COUNT(*) FROM RegisteredSystems;
   EXEC sp_set_session_context 'TenantId', '<tenant-B-guid>';
   SELECT COUNT(*) FROM RegisteredSystems;
   ```

   The two counts MUST match the report's per-tenant rollup. A non-CSP-Admin
   session that omits `sp_set_session_context` MUST return 0 rows from any
   `[TenantScoped]` table.

2. Run the integration smoke test pack against the migrated DB:

   ```bash
   dotnet test tests/Ato.Copilot.Tests.Integration --filter "FullyQualifiedName~Tenancy"
   ```

   All Tenancy-marked tests MUST pass against the production DB connection
   string before §3.5.

### 3.5 Switch Mode

Edit `appsettings.Production.json`:

```json
{
  "Deployment": { "Mode": "MultiTenant" }
}
```

Restart the application. On first request, `TenantResolutionMiddleware`
validates that every authenticated principal carries a `tid` claim; legacy
clients that previously relied on the implicit single-tenant binding now
receive `400 MISSING_TENANT_CLAIM`.

If `MultiTenant` is on but no `CspProfile` row is `Active`, the CSP-Admin who
signs in next is routed through the singleton onboarding wizard
(`/onboarding/csp`) per Feature 048 §Phase 13. All other endpoints return
`503 CSP_ONBOARDING_INCOMPLETE` until the wizard completes.

### 3.6 Enable Row-Level Security (SQL Server)

Step 3.3 with `installRls=true` already does this. To verify:

```sql
SELECT name, is_enabled, is_not_for_replication
FROM sys.security_policies
WHERE name LIKE 'sp_TenantIsolation_%';
```

Each `[TenantScoped]` table should appear in the results with `is_enabled = 1`.

## Rollback

There is no in-place rollback. The supported procedure is:

1. Take the application offline.
2. Restore the database from the backup taken in §3.1.
3. Revert `Deployment:Mode` back to `SingleTenant` in
   `appsettings.Production.json`.
4. Restart.
5. Restore from the audit-log archive: any rows added between the backup and
   rollback are lost.

This is intentional. Multi-tenant rows reference cross-tenant primary keys
(impersonation cookies, audit log, global baselines) that cannot be unwound
with a forward-only script.

## Troubleshooting

| Symptom                                      | Likely cause                          | Action |
|----------------------------------------------|---------------------------------------|--------|
| `MIGRATION_FAILED: tenant table is empty`    | No `Tenants` row before run           | Pre-flight §3 — create one first |
| `400 INVALID_REQUEST: defaultTenantId is required` | Missing body field              | Add `defaultTenantId` |
| `403 FORBIDDEN_NOT_CSP_ADMIN`                | Caller lacks `CSP.Admin` role         | Use a CSP-Admin token or the CLI |
| Report shows `error: "RLS install failed: ..."` | DB user not `db_owner`             | Re-run with `db_owner` credentials |
| Post-migration: any tenant sees 0 rows in dashboard | `sp_set_session_context` not set | Confirm app restart picked up `Deployment:Mode = MultiTenant` |
| `503 CSP_ONBOARDING_INCOMPLETE` after restart | Expected — CSP-Admin must finish wizard | Sign in as `CSP.Admin` and complete `/onboarding/csp` |

## FAQ

**Q: Can I run the migration while the application is online?**
A: No. The interceptor stamps `TenantId` on writes, but the schema change to
non-null `TenantId` columns and the RLS policy install both require an
exclusive table lock window. Take the app offline.

**Q: What happens to existing `AuditLogEntry` rows?**
A: They are `[GlobalReference]`, so they are not touched by the backfill.
They remain queryable from any tenant that has audit-read permission.

**Q: I see the migration ran twice; is that bad?**
A: No. The migration service is idempotent: it skips tables where
`TenantId` is already non-null on every row, and `CREATE SECURITY POLICY` is
wrapped in `IF NOT EXISTS`.

**Q: Can I split one tenant into two later?**
A: Yes, via `POST /api/admin/tenants/split` — but that is a separate runbook
(not part of Feature 048).

---

## Release-Validation Runbook (Feature 048, T153)

> **Purpose**: This section is the canonical checklist a release engineer runs
> on a **clean dev machine** to declare Feature 048 (Tenant Isolation) ready to
> ship. T153 was deliberately deferred from the agent loop because a clean-machine
> run cannot be performed inside an automated coding session — there is no
> guarantee the agent host has no leftover state from prior tests, no stray SQL
> Server containers, no cached `bin/`/`obj/`/`node_modules/`. This runbook
> closes that gap: every line below is meant to be executed by a human (or a
> CI release pipeline) on a workstation that has just run `git clean -xdf`.

### Audience

A release engineer or QA owner cutting a `048-tenant-isolation` build for
staging or production. Not the day-to-day developer loop.

### Clean-machine prerequisite

Before starting, run **all** of the following from the repo root:

```bash
# Remove every untracked + ignored artifact (bin, obj, node_modules, dist,
# site/, .venv, etc.) — but keep .git intact.
git clean -xdfn          # Dry run — review the file list first.
git clean -xdf           # Then commit to it.

# Confirm the toolchain matches global.json + package.json engines.
dotnet --version         # → 9.0.x   (must match global.json)
node --version           # → v20.x   (M365 + dashboard require Node 20 LTS)
docker --version         # any 24+   (SQL Server scenario)

# No stray containers from prior runs.
docker ps -a --filter "name=ato-" --format '{{.Names}}'
# If any rows print: docker rm -f <name>
```

If any tool version is wrong, **stop**. Fix the host and start over. Do not
continue on a partially-correct host — the migration covers RLS and SQL Server
behaviors that silently no-op on the wrong server version.

### Sequence (executes [`specs/048-tenant-isolation/quickstart.md`](../../specs/048-tenant-isolation/quickstart.md) §§ 1–7)

Execute the seven sections **in order**. Do not skip ahead. Each section's
"Expected" block is the gate — if any expectation fails, file a release-blocker
issue and stop.

| # | Quickstart section | What it proves | Pass criteria |
|---|--------------------|----------------|---------------|
| 1 | §1 Single-tenant smoke (US3) | Existing `SingleTenant` installs upgrade to the 048 binary unchanged. | Dashboard shows no tenant picker; log line `Tenant isolation: SingleTenant mode active`; system + default tenants present. |
| 2 | §2 Multi-tenant boot (FR-070/071/073) | `ato-cli tenant migrate` is idempotent + transactional and turns a `SingleTenant` DB into a `MultiTenant` one without data loss. | `ato-cli tenant status` reports `RowsMissingTenant: 0` after migration; report JSON shows `rlsInstalled: true`; app starts cleanly in `MultiTenant` mode. |
| 3 | §3 Two-tenant isolation (US1) | EF query filters + (on SQL Server) RLS enforce per-tenant scoping for all `DbSet`s. | Tenant A token never returns Tenant B rows from any `/api/*` endpoint, including `/api/dashboard/*` and the inheritance grid. |
| 4 | §4 CSP-Admin impersonation (US2) | `ImpersonatedTenantId` flows through `ITenantContext` and is observable in audit + UI banner. | Impersonation banner renders; audit log has `actor_tenant_id` ≠ `tenant_id` row; `/api/csp/dashboard/*` endpoints accessible only with `CSP.Admin`. |
| 5 | §5 SQL Server RLS bypass test (US5) | RLS rejects a raw connection that omits `sp_set_session_context`. | `SELECT COUNT(*) FROM RegisteredSystems` from a session without `tenant_id` returns `0`; the policy is visible in `sys.security_policies`. |
| 6 | §6 Air-gapped migration (FR-075) | Migration runs without outbound network. | `ato-cli tenant migrate` completes on a host with `iptables -A OUTPUT -j DROP` (or equivalent); no NuGet/Entra calls in the trace. |
| 7 | §7 Build + tests | Full repo build + tests are green on the clean host. | `dotnet build Ato.Copilot.sln` 0 errors / 0 new warnings; `dotnet test Ato.Copilot.sln` 100% pass; dashboard `npm run lint && npm run build` green; `extensions/vscode` + `extensions/m365` `npm run compile` / `npm run build` green (Constitution § Local Type-Checking Parity). |

### What to record

Save each of the following to a release artifact bundle (e.g.
`release-validation-048-<date>.zip`):

1. The output of every "Expected" block (terminal log, copy-paste).
2. `migration-report.json` from §2.4.
3. A screenshot of the dashboard from §3 showing **only** the active tenant's systems.
4. A screenshot of the impersonation banner from §4.
5. The audit-log query result from §4 (CSV).
6. The `sys.security_policies` row from §5 (CSV).
7. `dotnet test --logger "trx"` results from §7.

### Drift recording

If any expectation diverges from the quickstart, **document the drift in this
runbook before the fix lands** (Constitution § Document Before Fix):

1. Append a short subsection under [Troubleshooting](#troubleshooting) with
   the symptom, root cause, and the corrective action taken.
2. Update the relevant quickstart `Expected` block in the same PR so the next
   release engineer sees the new reality.
3. If the drift is a regression in the 048 code itself (not the runbook),
   open a release-blocker GitHub issue linking the failing step.

### Sign-off

Release validation is complete when **all seven** quickstart sections produced
their expected output on a single clean-machine run, the artifact bundle is
attached to the release ticket, and any drift has been recorded per §Drift
recording. The release engineer signs off by tagging the commit
`v0.48.0-rc1` (or the next planned tag) and noting "T153 validated on
`<date>`" in the release notes.

