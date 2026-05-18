# Phase 1 Data Model: Tenant- & Organization-Scoped Data Isolation

**Feature**: `048-tenant-isolation` | **Date**: 2026-05-07 | **Spec**: [spec.md](spec.md)

This document specifies the new entities (`Tenant`, `Organization`), the attribute infrastructure (`[TenantScoped]`, `[GlobalReference]`), the request-scoped runtime contract (`ITenantContext`), and the catalog of which existing entities are retrofitted.

---

## 1. New entities

### 1.1 `Tenant`

Authoritative root of an authorization boundary. Lives under namespace `Ato.Copilot.Core.Models.Tenancy`.

| Field | Type | Constraints | Purpose |
|-------|------|-------------|---------|
| `Id` | `Guid` | PK | Surrogate key referenced by all `TenantId` FKs. |
| `EntraTenantId` | `Guid?` | Unique when not null; indexed | Mapped to Entra `tid` claim for SSO. Null permitted for lab / air-gapped tenants. |
| `DisplayName` | `string(200)` | Required | Human-readable name shown in UI. |
| `LegalEntityName` | `string(300)` | Nullable until wizard completes | Captured during onboarding; used on official documents. |
| `DoDComponent` | `string(120)` | Nullable | DoD service / agency (Army, Navy, Air Force, USCG, ISA, etc.). |
| `PrimaryPocName` | `string(200)` | Nullable until wizard completes | Per Spec FR-001. |
| `PrimaryPocEmail` | `string(254)` | Nullable until wizard completes; RFC-5321 syntax | |
| `PrimaryPocPhone` | `string(40)` | Nullable | |
| `HqAddressLine1` | `string(200)` | Nullable | |
| `HqAddressLine2` | `string(200)` | Nullable | |
| `HqCity` | `string(120)` | Nullable | |
| `HqStateOrProvince` | `string(120)` | Nullable | |
| `HqPostalCode` | `string(20)` | Nullable | |
| `HqCountry` | `string(80)` | Nullable | ISO 3166-1 alpha-2 or alpha-3 expected. |
| `DefaultClassificationLevel` | `enum ClassificationLevel { Unclassified=0, CUI=1, Secret=2 }` | Default `Unclassified` | Drives default markings on generated documents. |
| `AuthorizingOfficialName` | `string(200)` | Nullable | |
| `AuthorizingOfficialEmail` | `string(254)` | Nullable | |
| `TimeZone` | `string(64)` | Default `"UTC"` | IANA timezone, e.g., `America/New_York`. |
| `Status` | `enum TenantStatus { Active=0, Suspended=1, Disabled=2 }` | Default `Active` | See FR-057–FR-059. |
| `OnboardingState` | `enum OnboardingState { Pending=0, InWizard=1, Active=2 }` | Default `Pending` | Drives wizard routing. |
| `CreatedAt` | `DateTimeOffset` | Required | UTC timestamp. |
| `CreatedBy` | `string(200)` | Required | OID of the actor who pre-provisioned (or `"system"` for self-onboard). |
| `UpdatedAt` | `DateTimeOffset?` | | |
| `UpdatedBy` | `string(200)?` | | |
| `RowVersion` | `byte[]` | Concurrency token (SQL Server `rowversion`) | Optimistic concurrency on status flips and updates. |

**Marked**: `[GlobalReference]` (the `Tenants` table itself cannot be tenant-scoped — chicken-and-egg).

**Indexes**:
- `IX_Tenants_EntraTenantId` (unique filtered where `EntraTenantId IS NOT NULL`)
- `IX_Tenants_Status` (used by tenant-list filters)

**Validation rules** (FluentValidation in `TenantProvisioningService`):
- `DisplayName` 1–200 chars, no leading/trailing whitespace.
- `EntraTenantId` may be null (single-tenant default) but if present must not duplicate any existing row.
- Wizard cannot set `OnboardingState=Active` until all required wizard fields are non-null.

**State transitions**:

```text
Pending → InWizard          (first user routed into wizard)
InWizard → Active           (wizard submit; required fields populated)
Active ↔ Suspended ↔ Disabled  (CSP-Admin via PATCH /api/tenants/{id}/status)
```

`Pending → Disabled` is permitted (for CSP-side cancellation before onboarding completes). `Disabled` is reversible via the same PATCH endpoint.

---

### 1.2 `Organization`

Sub-grouping inside a tenant (e.g., division, mission program, command). Lives under `Ato.Copilot.Core.Models.Tenancy`.

| Field | Type | Constraints | Purpose |
|-------|------|-------------|---------|
| `Id` | `Guid` | PK | |
| `TenantId` | `Guid` | FK → `Tenants.Id`, indexed, NOT NULL | Owning tenant. |
| `Name` | `string(200)` | Required, unique within tenant | Human-readable. |
| `Description` | `string(2000)` | Nullable | |
| `ParentOrganizationId` | `Guid?` | FK → `Organizations.Id` (self-ref), indexed | Allows shallow hierarchy (e.g., service → command). Future-proofing; may be null. |
| `CreatedAt` | `DateTimeOffset` | Required | |
| `CreatedBy` | `string(200)` | Required | |
| `RowVersion` | `byte[]` | Concurrency token | |

**Marked**: `[TenantScoped]`.

**Indexes**:
- `IX_Organizations_TenantId_Name` (composite, unique within tenant)
- `IX_Organizations_TenantId_ParentOrganizationId`

**Validation rules**:
- `Name` 1–200 chars; uniqueness scoped to `(TenantId, Name)`.
- `ParentOrganizationId` (when set) MUST resolve to an `Organization` in the same `TenantId` — enforced by `TenantStampingSaveChangesInterceptor`.

---

### 1.3 The `system tenant`

A single seeded row with `Id = '00000000-0000-0000-0000-000000000000'` and `DisplayName = 'Ato.Copilot.System'`. All `[GlobalReference]` rows have `TenantId` set to this value. The RLS predicate short-circuits `TenantId = system-tenant-id` so reads succeed for any session.

---

## 2. Attribute infrastructure

Lives under `Ato.Copilot.Core.Models.Tenancy.Attributes`.

### 2.1 `TenantScopedAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TenantScopedAttribute : Attribute
{
    /// <summary>Optional natural key column to suggest as the secondary key on the composite (TenantId, …) index.</summary>
    public string? CompositeIndexHint { get; init; }
}
```

Marks an entity as participating in:
- `HasQueryFilter(e => e.TenantId == ctx.EffectiveTenantId || (ctx.IsCspAdmin && ctx.ImpersonatedTenantId == null))`
- `TenantStampingSaveChangesInterceptor` (stamps `TenantId` on Added entities, validates FK consistency).
- SQL Server RLS policy installation.

The entity MUST expose a `Guid TenantId { get; set; }` property; a startup self-check fails if the property is missing.

### 2.2 `GlobalReferenceAttribute`

```csharp
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GlobalReferenceAttribute : Attribute { }
```

Marks an entity as reference data living in the system tenant. The entity:
- MAY have a `TenantId` field (if so, it is always the system tenant id).
- Is excluded from `HasQueryFilter` registration.
- Is excluded from RLS policy installation (no SQL filter applied; RLS predicate's system-tenant short-circuit is what makes them readable cross-tenant).

### 2.3 Startup self-check

`AtoCopilotContext.OnModelCreating` enumerates all entity types and asserts each has exactly one of:
- `[TenantScoped]` and a `Guid TenantId { get; set; }` property, OR
- `[GlobalReference]`, OR
- An entry in `TenantScopingExceptions.Allowed` (tracked exemptions).

Failure throws at startup with a clear message naming the offending type. This enforces FR-003.

---

## 3. Runtime contract: `ITenantContext`

Lives under `Ato.Copilot.Core.Interfaces.Tenancy`.

```csharp
public interface ITenantContext
{
    /// <summary>The user's home tenant resolved from Entra `tid` claim or single-tenant default.</summary>
    Guid TenantId { get; }

    /// <summary>Optional sub-organization scope. Null = tenant-level.</summary>
    Guid? OrganizationId { get; }

    /// <summary>True when the principal carries the CSP.Admin role.</summary>
    bool IsCspAdmin { get; }

    /// <summary>The tenant the CSP-Admin is currently impersonating, if any.</summary>
    Guid? ImpersonatedTenantId { get; }

    /// <summary>Computed: ImpersonatedTenantId ?? TenantId. The value used for query-filter and stamping.</summary>
    Guid EffectiveTenantId { get; }

    /// <summary>Tenant lifecycle status — fed by 30-second IMemoryCache lookup.</summary>
    TenantStatus Status { get; }
}
```

**Lifetime**: `AddScoped<ITenantContext, TenantContext>()`. Populated by `TenantResolutionMiddleware`. Reads through `IHttpContextAccessor`-equivalent that the middleware wires up.

For background services and `Channels` consumers that have no HTTP context, an `ITenantContextAccessor` (analogous to `IHttpContextAccessor`) is provided:

```csharp
public interface ITenantContextAccessor
{
    ITenantContext? Current { get; }
    IDisposable Push(ITenantContext context);   // sets AsyncLocal scope; disposing pops
}
```

---

## 4. Cross-tenant references — interceptor rules

`TenantStampingSaveChangesInterceptor` (registered in `DbContextOptionsBuilder`) implements:

1. **Stamp on insert.** For every entry in `EntityState.Added` with `[TenantScoped]`, set `TenantId = ctx.EffectiveTenantId` if it is `Guid.Empty`. If it is a non-empty value that differs from `EffectiveTenantId` AND the actor is not CSP-Admin, **throw `TenantConsistencyException`**.
2. **Forbid tenant change on update.** For every `EntityState.Modified` entry, if `TenantId` is in the modified-properties set, throw.
3. **Validate FK consistency.** For every `[TenantScoped]` entry being added or modified, walk its navigation properties; for each loaded reference, if the referenced entity is `[TenantScoped]` and its `TenantId` differs from this entry's `TenantId`, throw.
4. **Soft-validate `[GlobalReference]` cross-references.** A `[TenantScoped]` entity MAY reference a `[GlobalReference]` row regardless of `TenantId` (e.g., `ControlImplementation → NistControl`).

Exceptions thrown by the interceptor surface to API callers as `409 CROSS_TENANT_REFERENCE_REJECTED` via the standard error envelope.

---

## 5. Retrofit catalog (DbSets gaining `TenantId`)

The 60+ entities to retrofit are catalogued by domain. **Bold** entries also gain `OrganizationId Guid?`.

### 5.1 RMF system & boundary

- **`RegisteredSystem`**, **`AuthorizationBoundary`**, **`AuthorizationBoundaryDefinition`**, `BoundaryComponentAssignment`, `SecurityCategorization`, `RmfRoleAssignment`, `SystemCapabilityLink`

### 5.2 Controls & inheritance

- `ControlBaseline`, `ControlTailoring`, **`ControlInheritance`**, `InheritanceAuditEntry`, **`OrgInheritanceDefault`**, `ControlImplementation`, `ControlEffectiveness`, `AssessmentRecord`, `AuthorizationDecision`, `RiskAcceptance`

### 5.3 Findings, scans, evidence

- `ComplianceAssessment`, `ComplianceFinding`, `ComplianceEvidence`, **`EvidenceArtifact`**, `EvidenceVersion`, `ScanImportRecord`, `ScanImportFinding`, `ComplianceDocument`

### 5.4 POA&Ms, deviations, remediation

- `PoamItem`, `PoamMilestone`, `Deviation`, `RemediationPlan`, `RemediationBoard`, `RemediationTask`, `TaskComment`, `TaskHistoryEntry`, `AutoRemediationRule`

### 5.5 Watch / alerts

- `ComplianceAlert`, `AlertIdCounter`, `AlertNotification`, `NotificationPreferences`, `MonitoringConfiguration`, `ComplianceBaseline`, `AlertRule`, `SuppressionRule`, `EscalationPath`, `ComplianceSnapshot`, `SignificantChange`

### 5.6 SAP / SAR / SSP / privacy

- `SecurityAssessmentPlan`, `SapControlEntry`, `SapTeamMember`, `SspSection`, `ContingencyPlanReference`, `NarrativeVersion`, `NarrativeReview`, `PrivacyThresholdAnalysis`, `PrivacyImpactAssessment`, `SystemInterconnection`, `InterconnectionAgreement`

### 5.7 Components, capabilities, profiles

- **`SystemComponent`**, `ComponentSystemAssignment`, `SecurityCapability`†, `CapabilityControlMapping`, `ComponentCapabilityLink`, `SystemProfileSection`, `InventoryItem`

  † `SecurityCapability` is `[GlobalReference]` when seeded from a CSP catalog and `[TenantScoped]` when authored by a tenant. Modeled by an `IsGlobalReference` discriminator and dual handling in the service layer.

### 5.8 Continuous monitoring

- `ConMonPlan`, `ConMonReport`

### 5.9 Roadmap & packages

- `ImplementationRoadmap`, `AuthorizationPackage`, `SecurityAssessmentReport`, `SarSection`, `DeferredPrerequisite`

### 5.10 Dashboard

- `ComplianceTrendSnapshot`, `DashboardActivity`

### 5.11 Auth artifacts

- `CacSession`, `JitRequestEntity`, `CertificateRoleMapping` (already mostly user-scoped; `TenantId` added for full isolation)

### 5.12 Performance / cache

- `CachedResponse` (cache rows must be partitioned by tenant to avoid cross-tenant cache hits — important security boundary)

### 5.13 Audit

- `AuditLogEntry` — adds `ActorTenantId`, `EffectiveTenantId`, `ImpersonatedTenantId` (all nullable Guid). The row's primary `TenantId` is the `EffectiveTenantId` value; existing rows backfill to default tenant.

### 5.14 Already scoped (no change required to `TenantId` column itself; only OnModelCreating registration)

`TenantOnboardingState`, `OnboardingStepCompletion`, `OrganizationContext`, `Person`, `OrganizationRoleAssignment`, `SystemRoleAssignment`, `EmassImportSession`, `SspPdfImportSession`, `AzureSubscriptionRegistration`, `OrganizationDocumentTemplate`, `NarrativeSeedDocument`, `WizardArtifactDependency`, `WizardJobStatus`, `WizardAuditEntry`. (These continue to carry `TenantId`; their `[TenantScoped]` attribute is added in this feature.)

### 5.15 `[GlobalReference]` (no `TenantId` filter; system-tenant short-circuit applies)

`NistControl`, `ComplianceFramework`, `FrameworkControl`, `InformationType`, `Tenants`, `Organizations`† (see note below).

† `Organizations` is conceptually `[TenantScoped]`, but its rows are visible to CSP-Admin during impersonation routing; the standard `[TenantScoped]` query filter handles this correctly.

---

## 6. Audit log entity changes

`AuditLogEntry` gains:

| Field | Type | Notes |
|-------|------|-------|
| `ActorTenantId` | `Guid?` | The acting user's home tenant (from `ITenantContext.TenantId`). |
| `EffectiveTenantId` | `Guid?` | Tenant against which the action executed (`ImpersonatedTenantId ?? TenantId`). |
| `ImpersonatedTenantId` | `Guid?` | Set only during a CSP-Admin impersonation session. |

`TenantId` (existing) retains its meaning: the tenant that owns the audit row. For impersonation, this equals `EffectiveTenantId`.

**Index**: `IX_AuditLogs_TenantId_Timestamp` and `IX_AuditLogs_ActorTenantId_Timestamp` (composite, descending on timestamp) to support `GET /api/audit` filters.

---

## 7. `MultiTenantMigrationReport` (DTO)

Returned by `POST /api/admin/migrate-to-multitenant` and the CLI.

```csharp
public sealed record MultiTenantMigrationReport(
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    Guid DefaultTenantId,
    IReadOnlyList<MigrationTableReport> Tables,
    bool RlsInstalled,
    string? Error);

public sealed record MigrationTableReport(
    string TableName,
    long TotalRows,
    long RowsAssignedByOverride,
    long RowsAssignedToDefault,
    long RowsAlreadyAssigned);
```

---

## 8. Relationships diagram

```text
                 ┌────────────────────────────┐
                 │  Tenants                   │ [GlobalReference]
                 │  (PK Id)                   │
                 └─────────────┬──────────────┘
                               │1
              ┌────────────────┼─────────────────┐
              │                │                 │
              ▼N               ▼N                ▼N
     Organizations      OrganizationContext   AuditLogEntry
     [TenantScoped]     [TenantScoped]        [TenantScoped]
              │1
              ▼N
     RegisteredSystems, ComplianceFindings, EvidenceArtifacts, …
     (60+ retrofitted [TenantScoped] entities)
                               
     System tenant (Id=000…000)──hosts──▶ [GlobalReference] rows
                                          (NistControl, FrameworkControl, …)
```

---

## 9. Schema-add idempotency contract

Every modification to existing tables MUST be expressible as an additive, idempotent SQL fragment in `EnsureSchemaAdditionsAsync`:

```sql
IF COL_LENGTH('dbo.RegisteredSystems', 'TenantId') IS NULL
BEGIN
    ALTER TABLE dbo.RegisteredSystems ADD TenantId UNIQUEIDENTIFIER NULL;
END
-- backfill is the responsibility of MultiTenantMigrationService, not this script
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name='IX_RegisteredSystems_TenantId_Name' AND object_id=OBJECT_ID('dbo.RegisteredSystems'))
BEGIN
    CREATE INDEX IX_RegisteredSystems_TenantId_Name
      ON dbo.RegisteredSystems(TenantId, Name);
END
```

After backfill (and only after) a separate idempotent step issues `ALTER COLUMN TenantId UNIQUEIDENTIFIER NOT NULL`.

---

## 10. Validation rule summary

| Rule | Where enforced |
|------|---------------|
| Every `[TenantScoped]` entity has `Guid TenantId` | Startup self-check (`OnModelCreating`) |
| `TenantId` non-empty on save | `TenantStampingSaveChangesInterceptor` (stamping) |
| `TenantId` on insert equals `ITenantContext.EffectiveTenantId` (unless CSP-Admin) | Same |
| `TenantId` not modified on update | Same |
| Cross-tenant FK references rejected unless target is `[GlobalReference]` | Same |
| `Organizations.Name` unique within tenant | EF unique index |
| `Tenants.EntraTenantId` unique when not null | Filtered unique index |
| `OnboardingState=Active` requires required wizard fields | `TenantOnboardingService` (pre-save validator) |
| `Status` transitions audited | `TenantsEndpoints.PatchStatus` handler |
