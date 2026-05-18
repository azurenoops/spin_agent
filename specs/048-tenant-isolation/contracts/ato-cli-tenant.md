# Contract: `ato-cli tenant` Subcommand

**Feature**: `048-tenant-isolation` | **Date**: 2026-05-07
**Spec references**: FR-073, FR-074, FR-075, FR-076.

`ato-cli` is the offline / air-gapped companion to the
`POST /api/admin/migrate-to-multitenant` endpoint. It is published as a
.NET dotnet-tool from the new `Ato.Copilot.Cli` project. The CLI talks
directly to the database via a connection string; it does not require the
ASP.NET host or HTTP stack to be running.

## Install

```bash
dotnet tool install --global Ato.Copilot.Cli
```

Or use the self-contained single-file binary (`ato-cli-linux-x64`) shipped
in the Ato.Copilot release for fully offline / air-gapped operators.

## Top-level usage

```text
ato-cli tenant <subcommand> [options]

Subcommands:
  default     Show or set the singleton default tenant id used by SingleTenant deployments.
  assign      Assign rows to tenants based on a CSV mapping.
  migrate     Run the full migration: backfill TenantId columns + install RLS policies.
  status      Show the current per-table tenant-coverage report.

Global options:
  --connection-string <cs>    Direct DB connection string. May also be set via
                              ATO_DB__CONNECTION_STRING environment variable.
  --json                      Emit output as JSON (default: human-readable).
  --dry-run                   Compute changes without applying them. Implied by `status`.
  --verbose                   Increase log verbosity to Debug.
```

## `ato-cli tenant default`

```text
ato-cli tenant default [--id <guid>]
```

| Behavior | Description |
|----------|-------------|
| With `--id` | Sets `DeploymentOptions.DefaultTenantId` in the DB-backed configuration table. Idempotent. |
| Without `--id` | Prints the currently configured default tenant id. |

Exit codes: `0` success, `2` no default configured (read mode), `3` invalid GUID.

## `ato-cli tenant assign`

```text
ato-cli tenant assign --csv <path> [--default-tenant-id <guid>]
```

The CSV file has the columns:

```csv
TableName,RowIdPrefix,TenantId
RegisteredSystems,,11111111-1111-1111-1111-111111111111
ComplianceFindings,FOO-,22222222-2222-2222-2222-222222222222
```

- A blank `RowIdPrefix` matches every row whose `TenantId` is currently NULL.
- Rows not matched by any CSV row are assigned to `--default-tenant-id`, if
  supplied; otherwise they remain NULL and the command exits non-zero.

Exit codes: `0` success, `4` unmatched rows without a default, `5` CSV parse error.

## `ato-cli tenant migrate`

```text
ato-cli tenant migrate \
  --connection-string <cs> \
  --default-tenant-id <guid> \
  [--csv <path>] \
  [--install-rls true|false] \
  [--report-out report.json]
```

Performs the full backfill in a single transaction (per `MultiTenantMigrationService`):

1. Reads the schema; verifies every retrofitted table has the additive
   `TenantId` column already present (refuses to run if Phase A has not been
   deployed — see [research.md §8](../research.md)).
2. Applies CSV overrides (if provided).
3. Backfills remaining NULL `TenantId` rows to `--default-tenant-id`.
4. Issues `ALTER COLUMN … NOT NULL` on each retrofitted table.
5. If `--install-rls true` (default) and provider is SQL Server, installs RLS
   policies. (For SQLite, this step is a no-op with a warning.)
6. Emits an audit row (`Action = Tenant.Migrate.Cli`).
7. Writes a JSON `MigrationReport` to `--report-out` (default: `stdout`).

Exit codes: `0` success, `4` unmatched rows without default, `6` schema not
ready (run Phase A first), `7` RLS install failure (transaction rolled back).

## `ato-cli tenant status`

```text
ato-cli tenant status [--connection-string <cs>]
```

Prints one row per retrofitted table:

```text
TableName              TotalRows  RowsMissingTenant  AssignedToDefault  AssignedByOverride
RegisteredSystems          1024                  0                  0                   0
ComplianceFindings        12482                  0                  0                   0
EvidenceArtifacts          3201                  0                  0                   0
…
```

Always `--dry-run`. Exit code is `0` even when rows are missing tenant
assignments (status is informational).

## Audit emission contract

Every CLI invocation that mutates state MUST connect to the audit table and
write a row with:

| Field | Value |
|-------|-------|
| `Action` | `Tenant.Migrate.Cli` (for `migrate`) or `Tenant.Default.Cli` (for `default --id`) |
| `Resource` | The CSV path or `--default-tenant-id` value |
| `ActorOid` | Resolved from the OS user; `cli:<username>@<hostname>` |
| `ActorTenantId` | NULL (CLI runs outside any tenant scope) |
| `EffectiveTenantId` | NULL |
| `Outcome` | `Success` or `Failure` |
| `CorrelationId` | New GUID; printed to stdout |

## Exit-code summary

| Code | Meaning |
|------|---------|
| 0 | Success |
| 2 | Read-mode default-tenant absent |
| 3 | Invalid GUID argument |
| 4 | Unmatched rows without default |
| 5 | CSV parse error |
| 6 | Schema not ready (Phase A pending) |
| 7 | RLS install failure (transaction rolled back) |
| 130 | Cancelled (Ctrl+C) |
