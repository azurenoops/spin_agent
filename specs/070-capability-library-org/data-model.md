# Data Model — 070: Capability Library (Org Scope)

---

## 1. New Entity: `CapabilitySubscription`

### 1.1 C# Model

```csharp
// File: src/Ato.Copilot.Core/Models/Tenancy/CapabilitySubscription.cs

using Ato.Copilot.Core.Models.Tenancy.Attributes;

namespace Ato.Copilot.Core.Models.Tenancy;

/// <summary>
/// Feature 225 (Epic #225): records an org's decision to subscribe a Published CSP
/// capability to one of their registered systems. Tenant-scoped to prevent cross-tenant
/// reads; the FK to <see cref="CspInheritedCapability"/> crosses the TenantScoped /
/// GlobalReference boundary intentionally (FR-013).
/// </summary>
[TenantScoped]
public class CapabilitySubscription
{
    /// <summary>Primary key (server-generated GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Tenant that owns this subscription row. Set by server; used by EF query filter.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// The registered system ID this subscription is scoped to. String FK matching
    /// <see cref="RegisteredSystem.SystemId"/> (string PK pattern used throughout the model).
    /// </summary>
    [MaxLength(256)]
    public string SystemId { get; set; } = string.Empty;

    /// <summary>GlobalReference FK to the subscribed capability (cross-boundary FK).</summary>
    public Guid CspInheritedCapabilityId { get; set; }

    /// <summary>UTC timestamp when the subscription was created.</summary>
    public DateTimeOffset SubscribedAt { get; set; }

    /// <summary>OID of the ISSO/ISSM who created the subscription.</summary>
    [MaxLength(256)]
    public string SubscribedBy { get; set; } = string.Empty;

    /// <summary>Lifecycle status — Active (0) or Cancelled (1 = soft-deleted).</summary>
    public CapabilitySubscriptionStatus Status { get; set; } = CapabilitySubscriptionStatus.Active;

    /// <summary>UTC timestamp when the subscription was cancelled (null when Active).</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>OID of the ISSO/ISSM who cancelled the subscription (null when Active).</summary>
    [MaxLength(256)]
    public string? CancelledBy { get; set; }

    // ── Navigation ───────────────────────────────────────────────────────────

    /// <summary>Navigation to the subscribed capability (GlobalReference — no tenant filter).</summary>
    public CspInheritedCapability Capability { get; set; } = null!;
}
```

### 1.2 Status Enum

```csharp
// File: src/Ato.Copilot.Core/Models/Tenancy/CapabilitySubscriptionStatus.cs

namespace Ato.Copilot.Core.Models.Tenancy;

public enum CapabilitySubscriptionStatus
{
    Active    = 0,
    Cancelled = 1,
}
```

---

## 2. EF Core Configuration

Add to `AtoCopilotContext.OnModelCreating` (or a dedicated `IEntityTypeConfiguration<CapabilitySubscription>`):

```csharp
// In OnModelCreating (AtoCopilotContext.cs):

modelBuilder.Entity<CapabilitySubscription>(b =>
{
    b.HasKey(cs => cs.Id);

    // Tenant-isolation index — drives the tenant query filter scan
    b.HasIndex(cs => new { cs.TenantId, cs.SystemId })
     .HasDatabaseName("IX_CapabilitySubscriptions_TenantId_SystemId");

    // Lookup index for idempotency check on subscribe + list-per-capability
    b.HasIndex(cs => new { cs.TenantId, cs.CspInheritedCapabilityId, cs.SystemId })
     .HasDatabaseName("IX_CapabilitySubscriptions_TenantId_CapabilityId_SystemId");

    // Cross-boundary FK: TenantScoped → GlobalReference.
    // Restrict delete so archiving a capability does not cascade-cancel subscriptions
    // (operator must explicitly unsubscribe first — gives a clean audit trail).
    b.HasOne(cs => cs.Capability)
     .WithMany()
     .HasForeignKey(cs => cs.CspInheritedCapabilityId)
     .OnDelete(DeleteBehavior.Restrict);

    // Tenant query filter — mirrors pattern used by other TenantScoped entities.
    b.HasQueryFilter(cs =>
        TenantFilterDisabled ||
        TenantFilterCspAdminAll ||
        cs.TenantId == TenantFilterEffectiveId);
});
```

### 2.1 DbSet Registration (AtoCopilotContext.cs)

```csharp
// ─── Capability Library (Feature 225 / Epic #225) ────────────────────────────

/// <summary>Feature 225: org subscriptions of Published CSP capabilities to registered systems.</summary>
public DbSet<CapabilitySubscription> CapabilitySubscriptions => Set<CapabilitySubscription>();
```

---

## 3. Migration SQL

The EF-generated migration will produce SQL equivalent to the following.

### SQLite (dev)

```sql
CREATE TABLE "CapabilitySubscriptions" (
    "Id"                        TEXT NOT NULL CONSTRAINT "PK_CapabilitySubscriptions" PRIMARY KEY,
    "TenantId"                  TEXT NOT NULL,
    "SystemId"                  TEXT NOT NULL,
    "CspInheritedCapabilityId"  TEXT NOT NULL,
    "SubscribedAt"              TEXT NOT NULL,
    "SubscribedBy"              TEXT NOT NULL,
    "Status"                    INTEGER NOT NULL DEFAULT 0,
    "CancelledAt"               TEXT,
    "CancelledBy"               TEXT,
    CONSTRAINT "FK_CapabilitySubscriptions_CspInheritedCapabilities_CspInheritedCapabilityId"
        FOREIGN KEY ("CspInheritedCapabilityId")
        REFERENCES "CspInheritedCapabilities" ("Id")
        ON DELETE RESTRICT
);

CREATE INDEX "IX_CapabilitySubscriptions_TenantId_SystemId"
    ON "CapabilitySubscriptions" ("TenantId", "SystemId");

CREATE INDEX "IX_CapabilitySubscriptions_TenantId_CapabilityId_SystemId"
    ON "CapabilitySubscriptions" ("TenantId", "CspInheritedCapabilityId", "SystemId");
```

### SQL Server (prod)

```sql
CREATE TABLE [dbo].[CapabilitySubscriptions] (
    [Id]                        UNIQUEIDENTIFIER NOT NULL
        CONSTRAINT [PK_CapabilitySubscriptions] PRIMARY KEY,
    [TenantId]                  UNIQUEIDENTIFIER NOT NULL,
    [SystemId]                  NVARCHAR(256)    NOT NULL,
    [CspInheritedCapabilityId]  UNIQUEIDENTIFIER NOT NULL,
    [SubscribedAt]              DATETIMEOFFSET   NOT NULL,
    [SubscribedBy]              NVARCHAR(256)    NOT NULL,
    [Status]                    INT              NOT NULL DEFAULT 0,
    [CancelledAt]               DATETIMEOFFSET   NULL,
    [CancelledBy]               NVARCHAR(256)    NULL,
    CONSTRAINT [FK_CapabilitySubscriptions_CspInheritedCapabilities]
        FOREIGN KEY ([CspInheritedCapabilityId])
        REFERENCES [CspInheritedCapabilities] ([Id])
        ON DELETE NO ACTION
);

CREATE NONCLUSTERED INDEX [IX_CapabilitySubscriptions_TenantId_SystemId]
    ON [dbo].[CapabilitySubscriptions] ([TenantId], [SystemId]);

CREATE NONCLUSTERED INDEX [IX_CapabilitySubscriptions_TenantId_CapabilityId_SystemId]
    ON [dbo].[CapabilitySubscriptions] ([TenantId], [CspInheritedCapabilityId], [SystemId]);
```

---

## 4. Indexes Rationale

| Index | Queries Served | Cardinality |
|-------|---------------|-------------|
| `IX_CapabilitySubscriptions_TenantId_SystemId` | `GET /api/systems/{id}/capability-subscriptions` (list all subs for a system), EF tenant query filter | Medium |
| `IX_CapabilitySubscriptions_TenantId_CapabilityId_SystemId` | `POST` idempotency check (is there already an Active row?), `DELETE` by capabilityId | Low-Medium |

No unique constraint on the second index: the tuple `(TenantId, CapabilityId, SystemId)` can have
one Active and one or more Cancelled rows over time (subscribe → cancel → re-subscribe sequence).
The idempotency check is implemented in application code (`WHERE Status = Active LIMIT 1`).

---

## 5. Row-Level Security (RLS) Decision

**Decision: use EF Core `HasQueryFilter` only (no SQL-level RLS policy for this table).**

Rationale: All other `TenantScoped` entities in ATO Copilot use EF's `HasQueryFilter` pattern
(confirmed by `AtoCopilotContext` and `RlsFilterPredicateTests`). Introducing a SQL-level
RLS policy on only this new table would create an inconsistency in the isolation model.

The `[TenantScoped]` attribute on `CapabilitySubscription` causes the existing
`ApplyTenantQueryFilters` scan in `OnModelCreating` to pick it up automatically.

See `specs/057-rls-isolation-044-050/` for the authoritative RLS design decision.

---

## 6. No Schema Changes to Existing CSP Tables

This epic does **not** modify:
- `CspInheritedCapabilities` — no new columns; the entity is `GlobalReference` and read-only
  from the org subscription perspective.
- `CspInheritedComponents` — no changes.
- Any existing migration.

The cross-boundary FK (`CapabilitySubscription.CspInheritedCapabilityId → CspInheritedCapabilities.Id`)
is a documented design choice (see `research.md R2`) and is the only cross-boundary FK introduced
by this epic.
