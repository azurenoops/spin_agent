# Phase 1 — Data Model: First-Class Login Experience

**Feature**: 051-login
**Plan**: [plan.md](./plan.md)
**Research**: [research.md](./research.md)
**Spec**: [spec.md](./spec.md)
**Date**: 2026-05-28

This feature adds **one** new entity (`LoginAuditEvent`) and ships its DDL
via an idempotent `EnsureSchemaAdditions` module per R5. It makes **zero**
schema changes to existing tables. The "remember tenant" cookie
(`RememberedTenantCookie` in [spec.md § Key Entities](./spec.md)) is a
device-only artifact with no server-side mirror; it is not represented as a
database entity.

## 1. New entity: `LoginAuditEvent`

### 1.1 Purpose

Append-only audit-trail row recording a single authentication-related
event across any of the four surfaces (Dashboard, VS Code, M365 Teams,
Web Chat). Drives FR-032 (audit schema), FR-033 (privacy-preserving
failure logging), FR-034 / FR-035 (throttle audit), and FR-036a (13-month
hot + cold archive retention).

### 1.2 Fields

| Field | C# Type | DB Type (SQL Server) | DB Type (SQLite) | Required | Constraints |
|---|---|---|---|---|---|
| `Id` | `Guid` | `uniqueidentifier` | `TEXT (GUID)` | yes | `[Key]`; primary key. |
| `EventType` | `LoginAuditEventType` (enum) | `nvarchar(32)` | `TEXT` | yes | Persisted as string via `HasConversion<string>()`. 9 values (see § 1.3). |
| `Oid` | `string` | `nvarchar(254)` | `TEXT` | no | Entra `oid` claim, when present. `[MaxLength(254)]`. Null for pre-Entra events (e.g., `NetworkFailure` before Entra responded). |
| `Tid` | `string` | `nvarchar(254)` | `TEXT` | no | Entra `tid` claim (the user's home directory). `[MaxLength(254)]`. Null when no Entra response. Captured for forensic context only; does NOT determine tenant ownership (Q2). |
| `EffectiveTenantId` | `Guid` | `uniqueidentifier` | `TEXT (GUID)` | **yes (non-null)** | The system tenant (`SYSTEM_TENANT_ID`) for pre-session and `NoTenantAssignment` rows per Q2. Otherwise the resolved tenant. Logical FK to `Tenants.Id` with `Cascade` on tenant offboarding. |
| `CorrelationId` | `string` | `nvarchar(64)` | `TEXT` | yes | Correlates events across surfaces (e.g., the same OAuthPrompt flow in Teams and the callback to the dashboard). Sourced from the existing `CorrelationIdMiddleware`. |
| `SourceIp` | `string` | `nvarchar(45)` | `TEXT` | yes | IPv4 or IPv6 address. `[MaxLength(45)]` covers IPv6 max. Single client may have many rows per minute under throttle attack — this column is the most-indexed value. |
| `UserAgent` | `string` | `nvarchar(512)` | `TEXT` | yes | Truncated browser / extension / bot UA string. `[MaxLength(512)]`. |
| `Surface` | `LoginSurface` (enum) | `nvarchar(16)` | `TEXT` | yes | Persisted as string. 4 values: `Dashboard`, `VSCode`, `M365`, `Chat`. |
| `OccurredAt` | `DateTimeOffset` | `datetimeoffset` | `TEXT (ISO-8601)` | yes | Server-side UTC timestamp at write time. Default `DateTimeOffset.UtcNow`. |
| `ErrorClass` | `LoginErrorClass?` (enum) | `nvarchar(32)` | `TEXT` | no | Set only when `EventType = LoginFailure`. 10 values (see § 1.4). Persisted as string. |
| `MetadataJson` | `string?` | `nvarchar(2000)` | `TEXT` | no | Structured payload per event type. Shape rules per § 1.5. Null when no structured metadata. `[MaxLength(2000)]`. |

**Total fields**: 12.

### 1.3 `LoginAuditEventType` enum

```csharp
namespace Ato.Copilot.Core.Models.Auth;

public enum LoginAuditEventType
{
    LoginSuccess = 0,
    LoginFailure = 1,
    SignOut = 2,
    IdleSignOut = 3,
    ImpersonationStart = 4,
    ImpersonationEnd = 5,
    TenantSwitch = 6,
    SimulatedLogin = 7,
    SimulationBlocked = 8,
}
```

Persisted as **string** via `HasConversion<string>()` so raw-SQL audit
queries are readable without a value lookup. Matches the existing Feature
050 `CapabilityHistoryEventType` serialization convention.

### 1.4 `LoginErrorClass` enum

```csharp
namespace Ato.Copilot.Core.Models.Auth;

public enum LoginErrorClass
{
    // CAC failures
    NoCardInserted = 0,
    CertExpired = 1,
    CertNotYetValid = 2,
    CertRevoked = 3,
    ClockSkew = 4,
    // Entra failures
    NoTenantAssignment = 5,
    AccountDisabled = 6,
    MfaFailure = 7,
    ConditionalAccessBlock = 8,
    NetworkFailure = 9,
}
```

Set only when `EventType = LoginFailure`. Null otherwise.

### 1.5 `MetadataJson` shape per event type

| Event type | Shape | Example |
|---|---|---|
| `LoginSuccess` | `null` *or* `{ "authMethod": "Cac" \| "Entra" \| "Simulation" }` | `{ "authMethod": "Cac" }` |
| `LoginFailure` | `{ "errorClass": "<enum>" }` *(redundant with column for raw-SQL convenience)* | `{ "errorClass": "CertExpired" }` |
| `SignOut` | `null` | `null` |
| `IdleSignOut` | `{ "sessionStartedAt": "<iso8601>", "idleSeconds": <int> }` per FR-032 acceptance scenario 5 | `{ "sessionStartedAt": "2026-05-28T14:00:00Z", "idleSeconds": 1865 }` |
| `ImpersonationStart` | `{ "impersonatedTenantId": "<guid>", "expectedEndAt": "<iso8601>" }` | `{ "impersonatedTenantId": "...", "expectedEndAt": "2026-05-28T15:30:00Z" }` |
| `ImpersonationEnd` | `{ "impersonatedTenantId": "<guid>", "reason": "manual" \| "expired" \| "idle_timeout" \| "sign_out" }` | `{ "impersonatedTenantId": "...", "reason": "manual" }` |
| `TenantSwitch` | `{ "fromTenantId": "<guid>", "toTenantId": "<guid>" }` | `{ "fromTenantId": "...", "toTenantId": "..." }` |
| `SimulatedLogin` | `{ "identityId": "<string>" }` (the configured identity-list key per US7) | `{ "identityId": "dev-cspadmin" }` |
| `SimulationBlocked` | `{ "attemptedIdentityId": "<string>?", "environment": "<string>" }` (severity flag handled by Serilog scope, not metadata) | `{ "attemptedIdentityId": "dev-cspadmin", "environment": "Staging" }` |

**Serialization rule**: `JsonSerializer.Serialize` with camelCase + ignore-
null (the same `s_json` options used by Feature 050's
`CapabilityHistoryService`). Null metadata → null column (NEVER the literal
string `"null"`).

### 1.6 Validation rules

- `CorrelationId` MUST be non-empty (`Required`) and ≤ 64 chars.
- `SourceIp` MUST be non-empty and parseable as `IPAddress`.
- `UserAgent` MUST be non-empty (`null` is a server bug — at minimum the
  middleware MUST log `unknown` if the header is genuinely absent).
- `EffectiveTenantId` MUST be non-null. Pre-session callers MUST pass
  `SYSTEM_TENANT_ID` explicitly.
- `Oid` and `Tid` MUST follow Entra's UUID / GUID-like shape when present;
  the persistence layer does NOT validate this (Entra is the source of
  truth) but the appending service rejects strings > 254 chars.
- `MetadataJson` MUST be either `null` or a JSON document parsable by
  `JsonDocument.Parse`. Invalid JSON is a server bug — the writer enforces
  shape, never the caller.
- `OccurredAt` MUST be UTC. Enforced by writing `DateTimeOffset.UtcNow`
  only; never accepting a client-supplied timestamp.

### 1.7 Immutability

There is **no `UpdateAsync` or `DeleteAsync`** on `ILoginAuditService`. The
interface exposes only `AppendAsync`, `ListAsync`, and
`ListSystemTenantAsync` (R9 SOC-analyst read path). A unit test
enumerates the interface methods and asserts the set is exactly
`{ AppendAsync, ListAsync, ListSystemTenantAsync }`.

Direct-SQL `UPDATE` / `DELETE` on the table is an out-of-band ops
operation; the application layer never emits one.

### 1.8 Retention (FR-036a, Q3)

- **Hot retention**: 13 months in the `LoginAuditEvents` table.
- **Cold archive**: rows older than 13 months migrate to immutable
  append-blob storage (Azure Storage in prod, local filesystem in dev)
  via `LoginAuditArchiveService` (`IHostedService`) running daily at
  `02:00 UTC`.
- **Tenant offboarding** (Feature 048 cascade): removes hot rows. The
  cold archive is NOT cascaded — auditors retain pre-offboarding
  forensic capability per AU-9 (3).

**EF Core FK declaration** (in `OnModelCreating`):

```csharp
modelBuilder.Entity<LoginAuditEvent>()
    .HasOne<Tenant>()
    .WithMany()
    .HasForeignKey(e => e.EffectiveTenantId)
    .OnDelete(DeleteBehavior.Cascade);
```

### 1.9 Indexes

| Index | Columns | Rationale |
|---|---|---|
| `IX_LoginAuditEvents_Tenant_Occurred` | `(EffectiveTenantId, OccurredAt DESC)` | Primary read pattern — list a tenant's events in reverse chronological order, served by SOC tooling and by the per-tenant admin diagnostics view. Leading on `EffectiveTenantId` matches the tenant query filter applied by `[TenantScoped]`. |
| `IX_LoginAuditEvents_Occurred` | `(OccurredAt)` | Secondary read pattern — the `LoginAuditArchiveService` daily job scans `WHERE OccurredAt < UtcNow - 13 months`. Ungrouped by tenant to keep the migration cheap. |
| `IX_LoginAuditEvents_Oid` | `(Oid, OccurredAt DESC)` filtered `WHERE Oid IS NOT NULL` (SQL Server) / unfiltered (SQLite) | Forensic read pattern — "show me everything for user X". Filtered index on SQL Server keeps the index size proportional to authenticated events only. |

Three indexes ship in the schema-additions module.

### 1.10 Storage estimates

- Row size: ~ 600 bytes typical (5 × GUID/string IDs at ~64 B each, two
  enum strings at 32 B, `UserAgent` ~ 200 B, `SourceIp` ~ 16 B, timestamps
  + metadata + overhead).
- Per-tenant per-day: typical ≤ 100 rows / day ≈ 60 KB.
- Per-tenant 13-month hot horizon: ≤ 24 MB typical, ≤ 2.4 GB peak (worst
  case 10K rows/day for 13 months).
- 1,000 tenants: ≤ 24 GB hot baseline, ≤ 2.4 TB peak — comfortably within
  SQL Server S3 budgets at the peak end with the leading composite
  index.

### 1.11 Entity file location

```text
src/Ato.Copilot.Core/Models/Auth/
├── LoginAuditEvent.cs        # NEW
├── LoginAuditEventType.cs    # NEW
├── LoginErrorClass.cs        # NEW
└── LoginSurface.cs           # NEW
```

The entity is in the `Auth` namespace alongside `AuthEnums.cs` and
`AuthModels.cs`.

### 1.12 Reference C# definition (illustrative; tasks.md will pin)

```csharp
using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Auth;

/// <summary>
/// Append-only audit-trail row for a single authentication-related event
/// across the four ATO Copilot surfaces. Feature 051 FR-032 (schema),
/// FR-033 (privacy-preserving failure logging), FR-034 / FR-035 (throttle
/// audit), FR-036a (13-month hot + indefinite cold archive retention).
/// </summary>
/// <remarks>
/// Rows are tenant-scoped via <see cref="EffectiveTenantId"/>; pre-session
/// and unmapped events use <c>SYSTEM_TENANT_ID</c> per clarification Q2
/// (2026-05-28) so the automatic tenant query filter applies uniformly.
/// </remarks>
[TenantScoped]
public class LoginAuditEvent
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public LoginAuditEventType EventType { get; set; }

    [MaxLength(254)]
    public string? Oid { get; set; }

    [MaxLength(254)]
    public string? Tid { get; set; }

    /// <summary>
    /// Tenant that owns this audit row. <c>SYSTEM_TENANT_ID</c> for
    /// pre-session and <see cref="LoginErrorClass.NoTenantAssignment"/>
    /// rows per clarification Q2. Cascade-deleted on tenant offboarding.
    /// </summary>
    public Guid EffectiveTenantId { get; set; }

    [Required, MaxLength(64)]
    public string CorrelationId { get; set; } = string.Empty;

    [Required, MaxLength(45)]
    public string SourceIp { get; set; } = string.Empty;

    [Required, MaxLength(512)]
    public string UserAgent { get; set; } = string.Empty;

    [Required]
    public LoginSurface Surface { get; set; }

    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;

    public LoginErrorClass? ErrorClass { get; set; }

    [MaxLength(2000)]
    public string? MetadataJson { get; set; }
}
```

## 2. EF Core wiring (`AtoCopilotContext` additions)

```csharp
public DbSet<LoginAuditEvent> LoginAuditEvents => Set<LoginAuditEvent>();

// In OnModelCreating:
modelBuilder.Entity<LoginAuditEvent>(entity =>
{
    entity.HasKey(e => e.Id);

    entity.Property(e => e.EventType)
        .HasConversion<string>()
        .HasMaxLength(32)
        .IsRequired();
    entity.Property(e => e.ErrorClass)
        .HasConversion<string>()
        .HasMaxLength(32);
    entity.Property(e => e.Surface)
        .HasConversion<string>()
        .HasMaxLength(16)
        .IsRequired();

    entity.Property(e => e.Oid).HasMaxLength(254);
    entity.Property(e => e.Tid).HasMaxLength(254);
    entity.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
    entity.Property(e => e.SourceIp).HasMaxLength(45).IsRequired();
    entity.Property(e => e.UserAgent).HasMaxLength(512).IsRequired();
    entity.Property(e => e.MetadataJson).HasMaxLength(2000);

    entity.HasIndex(e => new { e.EffectiveTenantId, e.OccurredAt })
        .HasDatabaseName("IX_LoginAuditEvents_Tenant_Occurred")
        .IsDescending(false, true);

    entity.HasIndex(e => e.OccurredAt)
        .HasDatabaseName("IX_LoginAuditEvents_Occurred");

    entity.HasIndex(e => new { e.Oid, e.OccurredAt })
        .HasDatabaseName("IX_LoginAuditEvents_Oid")
        .IsDescending(false, true)
        .HasFilter("[Oid] IS NOT NULL");  // SQL Server only; SQLite ignores the filter

    entity.HasOne<Tenant>()
        .WithMany()
        .HasForeignKey(e => e.EffectiveTenantId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

## 3. EnsureSchemaAdditions module sketch

```csharp
namespace Ato.Copilot.Core.Data.Migrations.EnsureSchemaAdditions;

public static class LoginAuditEventsSchemaAdditions
{
    public static async Task ApplyAsync(AtoCopilotContext db, ILogger logger, CancellationToken ct = default)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        var isSqlServer = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
        var isSqlite = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (isSqlServer) await db.Database.ExecuteSqlRawAsync(SqlServerScript, ct);
            else if (isSqlite) await db.Database.ExecuteSqlRawAsync(SqliteScript, ct);
            else { /* warn + skip */ return; }
            logger.LogInformation("Verified Feature 051 LoginAuditEvents schema on {Provider}", provider);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LoginAuditEventsSchemaAdditions encountered an error — non-fatal if table already exists");
        }
    }

    private const string SqlServerScript = """
        IF OBJECT_ID(N'dbo.LoginAuditEvents', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.LoginAuditEvents (
                Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_LoginAuditEvents PRIMARY KEY,
                EventType NVARCHAR(32) NOT NULL,
                Oid NVARCHAR(254) NULL,
                Tid NVARCHAR(254) NULL,
                EffectiveTenantId UNIQUEIDENTIFIER NOT NULL,
                CorrelationId NVARCHAR(64) NOT NULL,
                SourceIp NVARCHAR(45) NOT NULL,
                UserAgent NVARCHAR(512) NOT NULL,
                Surface NVARCHAR(16) NOT NULL,
                OccurredAt DATETIMEOFFSET NOT NULL CONSTRAINT DF_LoginAuditEvents_OccurredAt DEFAULT(SYSUTCDATETIME()),
                ErrorClass NVARCHAR(32) NULL,
                MetadataJson NVARCHAR(2000) NULL,
                CONSTRAINT FK_LoginAuditEvents_Tenants_EffectiveTenantId
                    FOREIGN KEY (EffectiveTenantId) REFERENCES dbo.Tenants(Id) ON DELETE CASCADE
            );
        END;

        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LoginAuditEvents_Tenant_Occurred')
            CREATE INDEX IX_LoginAuditEvents_Tenant_Occurred
                ON dbo.LoginAuditEvents (EffectiveTenantId ASC, OccurredAt DESC);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LoginAuditEvents_Occurred')
            CREATE INDEX IX_LoginAuditEvents_Occurred ON dbo.LoginAuditEvents (OccurredAt);
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LoginAuditEvents_Oid')
            CREATE INDEX IX_LoginAuditEvents_Oid
                ON dbo.LoginAuditEvents (Oid ASC, OccurredAt DESC)
                WHERE Oid IS NOT NULL;
        """;

    private const string SqliteScript = """
        CREATE TABLE IF NOT EXISTS "LoginAuditEvents" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_LoginAuditEvents" PRIMARY KEY,
            "EventType" TEXT NOT NULL,
            "Oid" TEXT NULL,
            "Tid" TEXT NULL,
            "EffectiveTenantId" TEXT NOT NULL,
            "CorrelationId" TEXT NOT NULL,
            "SourceIp" TEXT NOT NULL,
            "UserAgent" TEXT NOT NULL,
            "Surface" TEXT NOT NULL,
            "OccurredAt" TEXT NOT NULL DEFAULT (datetime('now')),
            "ErrorClass" TEXT NULL,
            "MetadataJson" TEXT NULL,
            CONSTRAINT "FK_LoginAuditEvents_Tenants_EffectiveTenantId"
                FOREIGN KEY ("EffectiveTenantId") REFERENCES "Tenants" ("Id") ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS "IX_LoginAuditEvents_Tenant_Occurred"
            ON "LoginAuditEvents" ("EffectiveTenantId" ASC, "OccurredAt" DESC);
        CREATE INDEX IF NOT EXISTS "IX_LoginAuditEvents_Occurred"
            ON "LoginAuditEvents" ("OccurredAt");
        CREATE INDEX IF NOT EXISTS "IX_LoginAuditEvents_Oid"
            ON "LoginAuditEvents" ("Oid" ASC, "OccurredAt" DESC);
        """;
}
```

## 4. Configuration entities (in-memory only — not persisted)

These are options-binding shapes consumed by the runtime but not stored in
the database. See [contracts/internal-services.md § 3](./contracts/internal-services.md)
for the strongly-typed bindings.

- `AuthOptions` — `DefaultMethod`, `IdleTimeoutMinutes`,
  `RememberTenantCookieDays`, `Cloud`, `VSCode`, `Throttle`, `TeamsSso`,
  `Archive`, `Msal`, `Cookie`. Validated at startup. (Note: the simulation
  gate lives on Feature 027's `CacAuthOptions.SimulationMode` flag, not on
  `AuthOptions`, per analysis C10.)
- `AuthThrottleOptions` — `Development` / `Production` blocks each
  containing `PerIpPerMinute` + `PerIdentityPerMinute`.
- `SimulatedIdentityDescriptor` (under `CacAuthOptions:SimulatedIdentities`)
  — `IdentityId` (string key for `POST /api/auth/simulate`), `DisplayName`,
  `Oid`, `Tid`, `TenantId`, `Roles` (string[]), `Persona`. Extends Feature
  027 from a single identity to a list per US7.

### 4.1 `LoginAttemptCounter` (distributed-cache only)

**Not a database entity.** Documented here only so implementers reading
`data-model.md` do not overlook it (analysis C15). Full contract lives at
[contracts/internal-services.md § 2](./contracts/internal-services.md) and
the key-design in [research.md § R7](./research.md).

Two `IDistributedCache` keys per throttle decision:

- `login-throttle:ip:{ip}:{minute-bucket}` — integer count, TTL 60 s.
- `login-throttle:identity:{oid|tid|anonymous}:{minute-bucket}` — integer
  count, TTL 60 s.

Backed by Redis in production (Azure Cache for Redis) and the local
`ato-copilot-redis` service in dev docker-compose; `IDistributedMemoryCache` in dev / CI. No persistence, no migration, no
entity class.

## 5. State transitions

This feature does not introduce a new state machine of its own. It
**records** transitions of the existing session lifecycle:

```text
                ┌─────────────────────────────────────────┐
                │ Existing session lifecycle:             │
                │                                         │
                │   Unauthenticated                       │
                │       │                                 │
                │       ▼  user clicks Sign in            │
                │   AuthInFlight                          │
                │       │                                 │
                │       ▼  MSAL completes                 │
                │   Authenticated                         │
                │       │                                 │
                │       ▼  user clicks Sign out           │
                │   SignedOut                             │
                │                                         │
                │   Authenticated                         │
                │       │                                 │
                │       ▼  idle timer fires               │
                │   SignedOut (reason=idle_timeout)       │
                │                                         │
                │   Authenticated (CSP-Admin)             │
                │       │                                 │
                │       ▼  POST /api/tenants/{id}/impersonate
                │   Impersonating                         │
                │       │                                 │
                │       ▼  user clicks Exit OR auto-expiry│
                │   Authenticated                         │
                └─────────────────────────────────────────┘
                              │
                              ▼ every transition writes
                ┌─────────────────────────────────────────┐
                │ LoginAuditEvent (new in Feature 051)    │
                │   LoginSuccess   on successful login    │
                │   LoginFailure   on auth failure        │
                │   SignOut        on explicit sign out   │
                │   IdleSignOut    on idle-timer fire     │
                │   ImpersonationStart / End              │
                │   TenantSwitch   on /select-tenant      │
                │   SimulatedLogin on dev sim path        │
                │   SimulationBlocked on env-violation    │
                └─────────────────────────────────────────┘
```

## 6. SOC-analyst read path (R9)

`ILoginAuditService.ListSystemTenantAsync(...)` is exposed for SOC
analysts to query pre-session and unmapped events. It requires the
`Auth.SocAnalyst` claim mapped in Feature 003's `RoleClaimMappings`.
Without that claim the endpoint returns `403 FORBIDDEN_NOT_SOC_ANALYST`.

Implementation skips the tenant query filter via `.IgnoreQueryFilters()`
scoped to `EffectiveTenantId == SYSTEM_TENANT_ID` only — any attempt to
list other tenants' rows through this method MUST surface as an
`InvalidOperationException` (defense-in-depth against a future caller
extending the method's signature).

## 7. Test-coverage map

| Concern | Test file (planned) |
|---|---|
| `[TenantScoped]` query filter applies on read | `LoginAuditServiceTests.cs` |
| `AppendAsync` does NOT call `SaveChangesAsync` (R7 / Feature-050 SRP parity) | `LoginAuditServiceTests.cs` |
| Pre-session row uses `SYSTEM_TENANT_ID` | `LoginAuditServiceTests.cs` |
| Tenant cascade on offboarding | `LoginAuditServiceTests.cs` |
| Interface surface is exactly `{ AppendAsync, ListAsync, ListSystemTenantAsync }` | `LoginAuditServiceTests.cs` (reflection-based) |
| `MetadataJson` null vs. object | `LoginAuditServiceTests.cs` |
| Composite index leads with `EffectiveTenantId` | data-model self-test in `AtoCopilotContextTests.cs` (existing pattern) |
| SQLite schema additions emit the same DDL shape as SQL Server | `LoginAuditEventsSchemaAdditionsTests.cs` |

## 8. Cross-reference matrix

| FR | Entity field / index / behavior | Section |
|---|---|---|
| FR-032 (audit schema) | Entity § 1.2, indexes § 1.9 | § 1 |
| FR-033 (privacy in failures) | `Oid` nullable; `MetadataJson` shape table § 1.5 forbids cert thumbprint | § 1.2, § 1.5 |
| FR-034 / FR-035 (throttle) | Audit row + `MetadataJson.environment` field set by throttle handler | § 1.5 + [contracts/internal-services.md § 2](./contracts/internal-services.md) |
| FR-036a (retention) | `LoginAuditArchiveService` daily job; secondary index on `OccurredAt` | § 1.8, § 1.9 |
| Q1 (Teams SSO mode) | N/A at data layer — config-only | [contracts/m365-bot.md](./contracts/m365-bot.md) |
| Q2 (system tenant ownership) | `EffectiveTenantId` non-null + § 6 SOC read path | § 1.2, § 6 |
| Q3 (retention horizon) | § 1.8 13-month hot + indefinite cold | § 1.8 |
| Q4 (MSAL silent refresh) | N/A at data layer — SPA-only | [contracts/frontend-types.md](./contracts/frontend-types.md) |
| Q5 (login race) | N/A at data layer — SPA-only | [contracts/frontend-types.md](./contracts/frontend-types.md) |
