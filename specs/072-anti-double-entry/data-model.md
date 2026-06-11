# Data Model — 072: Anti-Double-Entry (SPIN/eMASS Sync Status)

---

## 1. New Entity: `EmassFieldSnapshot`

### 1.1 C# Model

```csharp
// File: src/Ato.Copilot.Core/Models/Onboarding/EmassFieldSnapshot.cs

using System.ComponentModel.DataAnnotations;
using Ato.Copilot.Core.Models.Tenancy.Attributes;
using Ato.Copilot.Core.Models.Onboarding;

namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Feature 072 (Issue #60): Records the last-known eMASS value for a specific field on a
/// registered system, enabling divergence detection and inline field-origin warnings.
/// One row per (TenantId, SystemId, FieldName) — upserted on each eMASS import commit.
/// </summary>
[TenantScoped]
public class EmassFieldSnapshot
{
    /// <summary>Primary key (server-generated GUID).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Tenant that owns this snapshot. Populated by TenantStampingSaveChangesInterceptor.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// FK to RegisteredSystem.Id (string PK). MaxLength matches RegisteredSystem.Id pattern.
    /// Not a navigation property — string PK boundary per codebase convention.
    /// </summary>
    [Required]
    [MaxLength(36)]
    public string SystemId { get; set; } = string.Empty;

    /// <summary>
    /// Canonical field name. One of: Name | Acronym | DitprId | OverallLevel | BaselineType | SystemType.
    /// Used as the lookup key for divergence detection and banner rendering.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Raw string value from the eMASS import column at the time of import.
    /// Stored as string regardless of SPIN field type (e.g., "Moderate", "AC-2", "12345").
    /// Null if the eMASS column was absent or empty during import.
    /// </summary>
    [MaxLength(1000)]
    public string? EmassValue { get; set; }

    /// <summary>UTC timestamp when this field value was imported from eMASS.</summary>
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>FK → EmassImportSession.Id — which import session produced this snapshot.</summary>
    public Guid ImportSessionId { get; set; }

    /// <summary>
    /// True when the current SPIN field value differs from EmassValue.
    /// Set by EmassFieldSyncService.CheckDivergenceAsync() after each relevant entity save.
    /// </summary>
    public bool IsDiverged { get; set; } = false;

    /// <summary>UTC timestamp when divergence was first detected for this field. Null if never diverged.</summary>
    public DateTimeOffset? DivergenceDetectedAt { get; set; }

    /// <summary>UTC timestamp when the field was last reconciled (brought back in sync). Null if never reconciled.</summary>
    public DateTimeOffset? ReconciledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ── Navigation ───────────────────────────────────────────────────────────

    /// <summary>Navigation to the import session that created this snapshot.</summary>
    public EmassImportSession ImportSession { get; set; } = null!;
}
```

### 1.2 Tracked Field Names (Constants)

```csharp
// File: src/Ato.Copilot.Core/Models/Onboarding/EmassTrackedField.cs

namespace Ato.Copilot.Core.Models.Onboarding;

/// <summary>
/// Canonical field name constants for EmassFieldSnapshot.FieldName.
/// These must match the keys used in EmassFieldSyncService and dashboard API client.
/// </summary>
public static class EmassTrackedField
{
    public const string Name          = "Name";
    public const string Acronym       = "Acronym";
    public const string DitprId       = "DitprId";
    public const string OverallLevel  = "OverallLevel";
    public const string BaselineType  = "BaselineType";
    public const string SystemType    = "SystemType";

    public static readonly IReadOnlyList<string> All = [Name, Acronym, DitprId, OverallLevel, BaselineType, SystemType];
}
```

---

## 2. Modified Entity: `RegisteredSystem`

Add `DitprId` column (Feature 072):

```csharp
// In: src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs
// Class: RegisteredSystem — add after existing Acronym property:

/// <summary>
/// DoD IT Portfolio Repository identifier (eMASS system_identifier field).
/// Pre-populated from eMASS import by EmassCommitJobHandler (Feature 072, FR-005).
/// </summary>
[MaxLength(50)]
public string? DitprId { get; set; }
```

---

## 3. Optional: `UserPreference` Entity (if not already present)

If no user-preference storage exists in the codebase (search for `UserPreference`
before creating — it may already exist), create a minimal key-value store:

```csharp
// File: src/Ato.Copilot.Core/Models/Tenancy/UserPreference.cs

using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Simple key-value preference store scoped to a user within a tenant.
/// Used by Feature 072 to persist banner-dismiss preferences server-side.
/// Key pattern: "emass-banner-dismissed:{systemId}" for eMASS banner dismissals.
/// </summary>
[TenantScoped]
public class UserPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    /// <summary>OID of the user this preference belongs to.</summary>
    [MaxLength(256)]
    public string UserOid { get; set; } = string.Empty;

    /// <summary>Preference key (namespaced, e.g., "emass-banner-dismissed:SYS-001").</summary>
    [MaxLength(256)]
    public string Key { get; set; } = string.Empty;

    /// <summary>Preference value as string (e.g., "true", "false", JSON).</summary>
    [MaxLength(2000)]
    public string? Value { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
```

> **Note:** If `UserPreference` already exists in the codebase, reuse it. Feature 072 only
> needs to store a single `true/false` value keyed by `"emass-banner-dismissed:{systemId}"`.

---

## 4. EF Core Configuration

Add to `AtoCopilotContext.OnModelCreating`:

```csharp
// In AtoCopilotContext.cs — OnModelCreating:

modelBuilder.Entity<EmassFieldSnapshot>(b =>
{
    b.HasKey(s => s.Id);

    // Primary lookup: get all snapshots for a system; also satisfies field-name lookup
    b.HasIndex(s => new { s.TenantId, s.SystemId, s.FieldName })
     .HasDatabaseName("IX_EmassFieldSnapshots_TenantId_SystemId_FieldName");

    // Divergence-filter index: find diverged snapshots per system quickly
    b.HasIndex(s => new { s.TenantId, s.SystemId, s.IsDiverged })
     .HasDatabaseName("IX_EmassFieldSnapshots_TenantId_SystemId_IsDiverged");

    // FK to import session — restrict delete so import session can't be hard-deleted
    // while snapshots reference it
    b.HasOne(s => s.ImportSession)
     .WithMany()
     .HasForeignKey(s => s.ImportSessionId)
     .OnDelete(DeleteBehavior.Restrict);

    // Tenant query filter — mirrors pattern used by other TenantScoped entities
    b.HasQueryFilter(s =>
        TenantFilterDisabled ||
        TenantFilterCspAdminAll ||
        s.TenantId == TenantFilterEffectiveId);
});
```

### 4.1 DbSet Registration

```csharp
// In AtoCopilotContext.cs — after EmassImportSessions:

// ─── eMASS Field Sync (Feature 072 / Issue #60) ───────────────────────────────

/// <summary>Feature 072: per-field eMASS import snapshots for sync status and divergence detection.</summary>
public DbSet<EmassFieldSnapshot> EmassFieldSnapshots => Set<EmassFieldSnapshot>();
```

---

## 5. Migration SQL

The EF-generated migration produces SQL equivalent to:

### SQLite (dev)

```sql
ALTER TABLE "RegisteredSystems" ADD COLUMN "DitprId" TEXT;

CREATE TABLE "EmassFieldSnapshots" (
    "Id"                   TEXT NOT NULL CONSTRAINT "PK_EmassFieldSnapshots" PRIMARY KEY,
    "TenantId"             TEXT NOT NULL,
    "SystemId"             TEXT NOT NULL,
    "FieldName"            TEXT NOT NULL,
    "EmassValue"           TEXT,
    "ImportedAt"           TEXT NOT NULL,
    "ImportSessionId"      TEXT NOT NULL,
    "IsDiverged"           INTEGER NOT NULL DEFAULT 0,
    "DivergenceDetectedAt" TEXT,
    "ReconciledAt"         TEXT,
    "CreatedAt"            TEXT NOT NULL,
    "UpdatedAt"            TEXT NOT NULL,
    CONSTRAINT "FK_EmassFieldSnapshots_EmassImportSessions_ImportSessionId"
        FOREIGN KEY ("ImportSessionId")
        REFERENCES "EmassImportSessions" ("Id")
        ON DELETE RESTRICT
);

CREATE INDEX "IX_EmassFieldSnapshots_TenantId_SystemId_FieldName"
    ON "EmassFieldSnapshots" ("TenantId", "SystemId", "FieldName");

CREATE INDEX "IX_EmassFieldSnapshots_TenantId_SystemId_IsDiverged"
    ON "EmassFieldSnapshots" ("TenantId", "SystemId", "IsDiverged");
```

### SQL Server (prod)

```sql
ALTER TABLE [dbo].[RegisteredSystems]
    ADD [DitprId] NVARCHAR(50) NULL;

CREATE TABLE [dbo].[EmassFieldSnapshots] (
    [Id]                   UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT [PK_EmassFieldSnapshots] PRIMARY KEY,
    [TenantId]             UNIQUEIDENTIFIER NOT NULL,
    [SystemId]             NVARCHAR(36)     NOT NULL,
    [FieldName]            NVARCHAR(100)    NOT NULL,
    [EmassValue]           NVARCHAR(1000)   NULL,
    [ImportedAt]           DATETIMEOFFSET   NOT NULL,
    [ImportSessionId]      UNIQUEIDENTIFIER NOT NULL,
    [IsDiverged]           BIT              NOT NULL DEFAULT 0,
    [DivergenceDetectedAt] DATETIMEOFFSET   NULL,
    [ReconciledAt]         DATETIMEOFFSET   NULL,
    [CreatedAt]            DATETIMEOFFSET   NOT NULL,
    [UpdatedAt]            DATETIMEOFFSET   NOT NULL,
    CONSTRAINT [FK_EmassFieldSnapshots_EmassImportSessions]
        FOREIGN KEY ([ImportSessionId])
        REFERENCES [dbo].[EmassImportSessions] ([Id])
        ON DELETE NO ACTION
);

CREATE NONCLUSTERED INDEX [IX_EmassFieldSnapshots_TenantId_SystemId_FieldName]
    ON [dbo].[EmassFieldSnapshots] ([TenantId], [SystemId], [FieldName]);

CREATE NONCLUSTERED INDEX [IX_EmassFieldSnapshots_TenantId_SystemId_IsDiverged]
    ON [dbo].[EmassFieldSnapshots] ([TenantId], [SystemId], [IsDiverged]);
```

---

## 6. Indexes Rationale

| Index | Queries Served | Cardinality |
|-------|---------------|-------------|
| `IX_EmassFieldSnapshots_TenantId_SystemId_FieldName` | `GET /emass-sync-status` (load all fields for system), divergence check (load specific field), upsert lookup | Low (≤ 6 rows per system) |
| `IX_EmassFieldSnapshots_TenantId_SystemId_IsDiverged` | Divergence count for badge status (`WHERE IsDiverged = 1`) | Low |

The table is expected to have at most 6 rows per system (one per tracked field), so index
selectivity is high and all lookups will be fast (< 5 ms on any reasonable hardware).

---

## 7. No Schema Changes to Existing eMASS Tables

This feature does **not** modify:
- `EmassImportSessions` — no new columns; FK reference is read-only from snapshot side
- `EmassImportParser` / `EmassParsedSystem` — parsing logic unchanged
- `SecurityCategorization` — no new columns (divergence tracked via `EmassFieldSnapshot`)
- `ControlBaseline` — no new columns

The only schema changes are:
1. New table `EmassFieldSnapshots`
2. New column `RegisteredSystems.DitprId`
