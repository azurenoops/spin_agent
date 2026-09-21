# Database Providers

This page documents the three database providers supported by ATO Copilot,
how to configure them, and what each one is designed for.

---

## Supported Providers

| Provider | Environment | Status |
|----------|-------------|--------|
| **SQL Server** (`SqlServer`) | Production / default | Fully supported |
| **SQLite** (`Sqlite`) | Development / test | Fully supported |
| **PostgreSQL** (`Postgres`) | Feature 041 | **Incomplete — do not enable in production** |

---

## Configuration

Set the active provider with the `Database:Provider` key in `appsettings.json`
or as an environment variable.

**Valid values:** `SqlServer`, `Sqlite`, `Postgres`

```json
{
  "Database": {
    "Provider": "SqlServer",
    "CommandTimeoutSeconds": 30,
    "MaxRetryCount": 3,
    "MaxRetryDelay": 30,
    "MigrationTimeoutSeconds": 300,
    "EnableSensitiveDataLogging": false
  }
}
```

Environment variable equivalent (Docker / Azure Container Apps):

```bash
Database__Provider=SqlServer
Database__CommandTimeoutSeconds=30
```

---

## Connection String Format

### SQL Server (production)

```
Server=<host>,1433;Database=AtoCopilot;User Id=<user>;Password=<pwd>;Encrypt=True;TrustServerCertificate=False;
```

For Managed Identity (Azure):

```
Server=<host>,1433;Database=AtoCopilot;Authentication=Active Directory Managed Identity;Encrypt=True;
```

Set via `ConnectionStrings__DefaultConnection` (environment) or
`ConnectionStrings:DefaultConnection` (appsettings).

### SQLite (development / test)

```
Data Source=./data/atoCopilot.db
```

Set via `ConnectionStrings__DefaultConnection`. The database file is created
automatically by `EnsureCreatedAsync` on first startup.

### PostgreSQL (Feature 041 — not for production)

```
Host=<host>;Port=5432;Database=atoCopilot;Username=<user>;Password=<pwd>;
```

The DDL branch for PostgreSQL exists in `EnsureSchemaAdditions` classes but is
**incomplete**. Do not set `Database:Provider=Postgres` in any environment until
Feature 041 is fully implemented and validated.

---

## EF Core Migrations

ATO Copilot uses two initialization strategies depending on the provider:

| Provider | Strategy |
|----------|----------|
| SQL Server | `EnsureCreatedAsync` + `EnsureSchemaAdditionsAsync` on every startup |
| SQLite | `MigrateAsync` (standard EF Core migrations) + `EnsureSchemaAdditionsAsync` |

To apply EF Core migrations manually (SQLite / dev):

```bash
dotnet ef database update --project src/Ato.Copilot.Core
```

To generate a new migration:

```bash
dotnet ef migrations add <MigrationName> --project src/Ato.Copilot.Core \
    --startup-project src/Ato.Copilot.Mcp
```

---

## EnsureSchemaAdditions Behavior

After the base schema is initialized, the startup sequence runs a series of
additive DDL passes via the `EnsureSchemaAdditions` classes
(`src/Ato.Copilot.Core/Data/Migrations/EnsureSchemaAdditions/`).

### Narrative Provenance Additions

The Policy/Technical narrative schema pass also adds
`OscalDecompositionFragments.DerivationBasis` (default `Unknown`) and nullable
`NarrativeVersions.SnapshotJson` on SQLite and SQL Server. Existing confidence
scores are not used to infer origin, and old version rows remain without snapshots.
The pass checks for existing columns and is safe to repeat without replacing data.

Back up an existing database before upgrading. Verify both columns after startup
and retain the backup for application-version rollback. SQLite upgrade and repeat
execution are covered by automated tests; the SQL Server DDL has structural tests
but still requires execution against a representative SQL Server database.

### Fail-fast contract (Issue #868)

Every `ApplyAsync` method propagates DDL failures **unconditionally on all
providers** — SQL Server, SQLite, and any future provider. If any schema
addition fails, startup halts immediately with an `InvalidOperationException`
and logs the failure at `LogLevel.Error` including the provider name. This
prevents the process from continuing on a structurally incomplete schema.

> **Why this applies to SQLite too:** all SQLite DDL scripts in the
> `EnsureSchemaAdditions` classes are written to be idempotent by design —
> `CREATE TABLE IF NOT EXISTS`, PRAGMA-guarded `ALTER TABLE`, and
> `CREATE INDEX IF NOT EXISTS`. An exception that reaches the catch block
> therefore cannot be a benign "already applied" race; it is a genuine schema
> failure. Swallowing it would boot the service on a broken database, turning
> a fixable startup abort into silent data-integrity risk downstream.
>
> **Invariant:** every DDL script added to `EnsureSchemaAdditions` classes
> **must** be idempotent (guard every statement with `IF NOT EXISTS` or its
> SQLite equivalent). Non-idempotent scripts will hard-abort startup on the
> second run — this is intentional and by design.

If startup aborts with `"Database schema initialization failed in
<ClassName> for provider '<provider>'"`, the database file or server has a
schema that does not match the expected state. Resolution steps:

1. **Development (SQLite):** delete the database file and let `EnsureCreatedAsync`
   recreate it, then rerun the app.
2. **Production (SQL Server):** examine the specific exception message in the
   `LogLevel.Error` entry to identify which table, column, or index failed and
   apply the corrective DDL manually under a change-control window.

Classes in this directory:

- `TenantsAndOrganizationsSchemaAdditions` — Tenants and Organizations tables (Feature 048)
- `TenantIdColumnAdditions` — TenantId columns on all `[TenantScoped]` entities (Feature 048)
- `AuditLogTenantAttributionAdditions` — AuditLogs tenant-attribution columns (Feature 048)
- `GlobalBaselineSchemaAdditions` — GlobalBaselines table (Feature 048)
- `CapabilityHistoryEventsSchemaAdditions` — CapabilityHistoryEvents table (Feature 050)
- `LoginAuditEventsSchemaAdditions` — LoginAuditEvents table (Feature 051)
- `RlsPolicyInstaller` — SQL Server Row-Level Security policy (Feature 048, SQL Server only)

---

## Provider Caveats

### SQLite

- **Dev and test only.** SQLite is not thread-safe for concurrent writes and
  lacks Row-Level Security support.
- Tenant isolation is enforced by EF query filters and the `SaveChanges`
  interceptor only — the database engine does **not** enforce isolation.
- A startup warning is emitted on every run: `"Tenant isolation: SQLite provider
  detected — Row-Level Security NOT installed. Using EF query filters only.
  NOT FOR PRODUCTION."`

### PostgreSQL

- The `Postgres` branch in `EnsureSchemaAdditions` classes either skips silently
  or is not yet written. Feature 041 is the tracking issue.
- Do not set `Database:Provider=Postgres` until Feature 041 is complete and
  all schema addition classes have been updated and tested.
