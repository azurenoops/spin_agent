# Data Model: Control Inheritance & Customer Responsibility Matrix

**Feature**: 043-control-inheritance  
**Date**: 2026-03-20

## Entity Relationship Diagram

```
RegisteredSystem (1) ──── (0..1) ControlBaseline
                                     │
                         ┌───────────┼───────────┐
                         │           │           │
                     (0..*) ControlInheritance  (0..*) ControlTailoring
                         │
                     (0..*) InheritanceAuditEntry

CspInheritanceProfile (reference data — JSON files, not DB-persisted)
     │
     └── (0..*) CspProfileControl (inline entries in JSON)
```

## Existing Entities (No Changes)

### ControlBaseline
**Location**: `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string | PK, MaxLength(36) | GUID |
| RegisteredSystemId | string | FK, Required, MaxLength(36) | FK → RegisteredSystem |
| BaselineLevel | string | Required, MaxLength(20) | "Low", "Moderate", "High" |
| OverlayApplied | string? | MaxLength(100) | e.g., "CNSSI 1253" |
| TotalControls | int | | Count of controls in baseline |
| InheritedControls | int | | Count with InheritanceType.Inherited |
| SharedControls | int | | Count with InheritanceType.Shared |
| CustomerControls | int | | Count with InheritanceType.Customer |
| TailoredOutControls | int | | Controls removed from baseline |
| TailoredInControls | int | | Controls added to baseline |
| ControlIds | List\<string\> | JSON-converted | All control IDs in baseline |
| CreatedAt | DateTime | | UTC creation time |
| CreatedBy | string | Required, MaxLength(200) | Who created the baseline |
| ModifiedAt | DateTime? | | Last modification |
| Inheritances | ICollection\<ControlInheritance\> | Nav property | Child inheritance records |
| Tailorings | ICollection\<ControlTailoring\> | Nav property | Child tailoring records |

### ControlInheritance (Existing — No Changes)
**Location**: `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string | PK, MaxLength(36) | GUID |
| ControlBaselineId | string | FK, Required, MaxLength(36) | FK → ControlBaseline |
| ControlId | string | Required, MaxLength(20) | NIST control ID (e.g., "AC-2") |
| InheritanceType | InheritanceType | Required, Enum | Inherited / Shared / Customer |
| Provider | string? | MaxLength(200) | CSP name (e.g., "Azure Government (FedRAMP High)") |
| CustomerResponsibility | string? | MaxLength(2000) | Description for Shared controls |
| SetBy | string | Required, MaxLength(200) | Actor who set the designation |
| SetAt | DateTime | Default: UtcNow | When the designation was set |
| ControlBaseline | ControlBaseline | Nav property | Parent baseline |

**Implicit state**: Controls without a `ControlInheritance` record are "Undesignated". UndesignatedCount = TotalControls − InheritedControls − SharedControls − CustomerControls.

### InheritanceType Enum (Existing — No Changes)

```csharp
public enum InheritanceType
{
    Inherited,   // Fully inherited from CSP/provider
    Shared,      // Shared responsibility between provider and customer
    Customer     // Fully customer-responsible
}
```

## New Entities

### InheritanceAuditEntry (NEW)
**Location**: `src/Ato.Copilot.Core/Models/Compliance/RmfModels.cs`  
**Table**: `InheritanceAuditEntries`

Immutable, append-only audit log for every change to a ControlInheritance record.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | string | PK, MaxLength(36) | GUID |
| ControlInheritanceId | string | Required, MaxLength(36), Indexed | FK → ControlInheritance (logical, no cascade) |
| ControlId | string | Required, MaxLength(20) | Denormalized for query convenience |
| ControlBaselineId | string | Required, MaxLength(36), Indexed | Denormalized for query convenience |
| Actor | string | Required, MaxLength(200) | User or service that made the change |
| PreviousInheritanceType | string? | MaxLength(20) | null if first designation ("Created") |
| NewInheritanceType | string | Required, MaxLength(20) | "Inherited", "Shared", "Customer" |
| PreviousProvider | string? | MaxLength(200) | Previous provider value |
| NewProvider | string? | MaxLength(200) | New provider value |
| PreviousCustomerResponsibility | string? | MaxLength(2000) | Previous description |
| NewCustomerResponsibility | string? | MaxLength(2000) | New description |
| ChangeSource | InheritanceChangeSource | Required, Enum | How the change was made |
| Timestamp | DateTime | Required, Indexed | UTC timestamp of the change |

**Indexes**:
- `IX_InheritanceAuditEntries_ControlInheritanceId` — for per-record audit trail
- `IX_InheritanceAuditEntries_ControlBaselineId_Timestamp` — for system-wide audit queries
- `IX_InheritanceAuditEntries_Timestamp` — for chronological queries

**Constraints**:
- Rows are insert-only — no UPDATE or DELETE operations
- No cascade delete from ControlInheritance (audit entries survive re-designation)

### InheritanceChangeSource Enum (NEW)

```csharp
public enum InheritanceChangeSource
{
    Manual,       // Single control edit via UI
    BulkUpdate,   // Multi-select bulk update
    ProfileApply, // CSP profile application
    CrmImport     // CRM spreadsheet import
}
```

## Reference Data (Not DB-Persisted)

### CspInheritanceProfile (JSON Config)
**Location**: `src/seed-data/csp-profiles/*.json`

Loaded at startup by `CspProfileService` into an in-memory collection. Administrators add new profiles by dropping JSON files into the directory.

```json
{
  "profileId": "azure-gov-fedramp-high",
  "name": "Azure Government (FedRAMP High)",
  "provider": "Azure Government (FedRAMP High)",
  "baselineLevel": "High",
  "description": "Pre-built inheritance profile based on Microsoft Azure Government FedRAMP High CRM.",
  "version": "2026-03",
  "controls": [
    {
      "controlId": "AC-1",
      "inheritanceType": "Shared",
      "customerResponsibility": "Customer must develop, document, and disseminate organization-specific access control policy and procedures. Azure provides platform-level access control infrastructure."
    },
    {
      "controlId": "AC-2",
      "inheritanceType": "Shared",
      "customerResponsibility": "Customer manages application-level user accounts and access provisioning. Azure manages platform identity infrastructure and Entra ID service."
    },
    {
      "controlId": "PE-1",
      "inheritanceType": "Inherited",
      "customerResponsibility": null
    }
  ]
}
```

**In-memory model**: `CspInheritanceProfile` class with strongly-typed properties (not an EF entity).

## Computed/Derived Data

### CRM (Customer Responsibility Matrix)
Generated by `BaselineService.GenerateCrmAsync()` — already implemented. Returns:

- `CrmResult.FamilyGroups[]` — grouped by control family
- Each `CrmEntry` — controlId, inheritanceType (including "Undesignated" for controls without records), provider, customerResponsibility
- Summary counts for inherited, shared, customer, undesignated

### Summary Bar Metrics
Derived from `ControlBaseline` counts (already maintained by `BaselineService.SetInheritanceAsync()`):
- **Total**: `baseline.TotalControls`
- **Inherited**: `baseline.InheritedControls`
- **Shared**: `baseline.SharedControls`
- **Customer**: `baseline.CustomerControls`
- **Undesignated**: `TotalControls - InheritedControls - SharedControls - CustomerControls`
- **Inheritance %**: `(InheritedControls + SharedControls) / TotalControls * 100`

## Validation Rules

| Entity | Field | Rule |
|--------|-------|------|
| ControlInheritance | InheritanceType | Must be valid enum value (Inherited, Shared, Customer) |
| ControlInheritance | Provider | Required when InheritanceType is Inherited or Shared |
| ControlInheritance | CustomerResponsibility | Recommended when InheritanceType is Shared; MaxLength 2000 |
| ControlInheritance | ControlId | Must exist in baseline's ControlIds list |
| InheritanceAuditEntry | All fields | Immutable after creation — no updates allowed |
| CRM Import | ControlId | Must match a control in the active baseline; unfound IDs flagged as errors |
| CRM Import | InheritanceType | Must map to valid enum (case-insensitive, supports "System-Specific" → Customer) |
| CSP Profile | controlId | Only controls matching the active baseline are applied; others silently skipped |

## State Transitions

```
Control (no ControlInheritance record)
    │
    ├── Set Inheritance → Inherited (Provider required)
    ├── Set Inheritance → Shared (Provider + CustomerResponsibility)
    └── Set Inheritance → Customer
    
Any designation can transition to any other designation:
    Inherited ↔ Shared ↔ Customer
    
Each transition creates an InheritanceAuditEntry recording previous → new values.

Deleting a baseline cascades deletion of ControlInheritance records.
InheritanceAuditEntry records are NOT cascade-deleted (orphaned but preserved).
```

## EF Core Configuration

```csharp
// In AtoCopilotContext.OnModelCreating()
modelBuilder.Entity<InheritanceAuditEntry>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).HasMaxLength(36);
    entity.Property(e => e.ControlInheritanceId).HasMaxLength(36);
    entity.Property(e => e.ControlId).HasMaxLength(20);
    entity.Property(e => e.ControlBaselineId).HasMaxLength(36);
    entity.Property(e => e.Actor).HasMaxLength(200);
    entity.Property(e => e.PreviousInheritanceType).HasMaxLength(20);
    entity.Property(e => e.NewInheritanceType).HasMaxLength(20);
    entity.Property(e => e.PreviousProvider).HasMaxLength(200);
    entity.Property(e => e.NewProvider).HasMaxLength(200);
    entity.Property(e => e.PreviousCustomerResponsibility).HasMaxLength(2000);
    entity.Property(e => e.NewCustomerResponsibility).HasMaxLength(2000);
    entity.Property(e => e.ChangeSource).HasConversion<string>().HasMaxLength(20);

    entity.HasIndex(e => e.ControlInheritanceId);
    entity.HasIndex(e => new { e.ControlBaselineId, e.Timestamp });
    entity.HasIndex(e => e.Timestamp);
});
```
