# Data Model: Deviation Management (Feature 035)

**Date**: 2026-03-17

---

## New Entities

### Deviation

The central entity for all compliance exceptions — false positives, risk acceptances, and waivers.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `Id` | `string` | PK, GUID, max 36 | Unique identifier |
| `RegisteredSystemId` | `string` | FK → RegisteredSystems, required | System this deviation belongs to |
| `DeviationType` | `string` | max 50, required | `FalsePositive`, `RiskAcceptance`, `Waiver` |
| `Status` | `string` | max 50, required | `Pending`, `Approved`, `Denied`, `Expired`, `Revoked` |
| `ControlId` | `string` | max 20, required | NIST 800-53 control ID (e.g., "AC-2") |
| `CatSeverity` | `int` (enum) | required | CatI (1), CatII (2), CatIII (3) |
| `Justification` | `string` | max 4000, required | Reason for deviation request |
| `CompensatingControls` | `string?` | max 2000 | Description of compensating controls |
| `EvidenceReferences` | `string` | TEXT, default `[]` | JSON array of ScanImportRecord IDs |
| `ExpirationDate` | `DateTime` | required | When deviation auto-expires |
| `ReviewCycle` | `string` | max 20, required | `90d`, `180d`, `Annual` |
| `FindingId` | `string?` | FK → ComplianceFindings, nullable | Linked finding |
| `PoamEntryId` | `string?` | FK → PoamEntries, nullable | Linked POA&M item |
| `AuthorizationDecisionId` | `string?` | FK → AuthorizationDecisions, nullable | Parent authorization context |
| `BoundaryDefinitionId` | `string?` | FK → AuthorizationBoundaryDefinitions, nullable | Boundary scope (waivers only) |
| `RequestedBy` | `string` | max 200, required | User ID of requestor |
| `RequestedAt` | `DateTime` | required, default UTC now | When request was submitted |
| `ReviewedBy` | `string?` | max 200 | User ID of reviewer (ISSM or AO) |
| `ReviewedAt` | `DateTime?` | nullable | When review was completed |
| `ReviewerRole` | `string?` | max 50 | `ISSM` or `AO` |
| `ReviewerComments` | `string?` | max 2000 | Reviewer's notes on decision |
| `ISSMRecommendation` | `string?` | max 20 | ISSM recommendation for CAT I deviations: `Approve` or `Deny` |
| `ISSMRecommendedBy` | `string?` | max 200 | ISSM user ID who recommended (CAT I two-step only) |
| `ISSMRecommendedAt` | `DateTime?` | nullable | When ISSM recommendation was recorded |
| `RevokedBy` | `string?` | max 200 | User ID who revoked |
| `RevokedAt` | `DateTime?` | nullable | When revoked |
| `RevocationReason` | `string?` | max 1000 | Reason for revocation |
| `CreatedAt` | `DateTime` | required, default UTC now | Record creation timestamp |
| `ModifiedAt` | `DateTime?` | nullable | Last modification timestamp |

### Navigation Properties

| Property | Type | Relationship |
|----------|------|--------------|
| `RegisteredSystem` | `RegisteredSystem?` | Many-to-one |
| `Finding` | `ComplianceFinding?` | Many-to-one (optional) |
| `PoamEntry` | `PoamItem?` | Many-to-one (optional) |
| `AuthorizationDecision` | `AuthorizationDecision?` | Many-to-one (optional) |
| `BoundaryDefinition` | `AuthorizationBoundaryDefinition?` | Many-to-one (optional) |

---

## Enums

### DeviationType (string stored)

| Value | Description |
|-------|-------------|
| `FalsePositive` | Scan result does not represent a real vulnerability |
| `RiskAcceptance` | Known risk accepted by authority with compensating controls |
| `Waiver` | Control determined not applicable for a scope/boundary |

### DeviationStatus (string stored)

| Value | Description |
|-------|-------------|
| `Pending` | Awaiting reviewer approval |
| `Approved` | Active deviation; linked entities transitioned |
| `Denied` | Rejected by reviewer; finding remains Open |
| `Expired` | Past expiration without renewal; entities reverted |
| `Revoked` | Manually withdrawn; entities reverted |

---

## Modified Entities

### ComplianceFinding

| Field | Change | Description |
|-------|--------|-------------|
| `DeviationId` | Added, `string?`, nullable FK → Deviations | Links finding to its active deviation |

### PoamItem

| Field | Change | Description |
|-------|--------|-------------|
| `DeviationId` | Added, `string?`, nullable FK → Deviations | Links POA&M to its active deviation |

---

## Indexes

| Name | Table | Columns | Notes |
|------|-------|---------|-------|
| `IX_Deviations_RegisteredSystemId` | Deviations | `RegisteredSystemId` | Filter by system |
| `IX_Deviations_Status` | Deviations | `Status` | Filter pending/approved |
| `IX_Deviations_FindingId` | Deviations | `FindingId` | Lookup by finding |
| `IX_Deviations_ExpirationDate` | Deviations | `ExpirationDate` | Background service expiration scan |
| `IX_ComplianceFindings_DeviationId` | ComplianceFindings | `DeviationId` | Reverse lookup |
| `IX_PoamEntries_DeviationId` | PoamEntries | `DeviationId` | Reverse lookup |

---

## Unique Constraints

| Constraint | Description |
|------------|-------------|
| No duplicate active deviations per finding | Application-enforced: before creating a Deviation, check that no existing Deviation with same `FindingId` and `Status IN (Pending, Approved)` exists. Return 409 Conflict if found. |

---

## Constants

| Name | Value | Description |
|------|-------|-------------|
| `MaxReviewCycleDays` | 365 | Maximum allowed review cycle / extension duration. Extensions beyond this require a new deviation request. |

---

## Migration Plan

### Step 1: Create Deviations table
- Add all columns, FKs, and indexes listed above.

### Step 2: Add FK columns to existing tables
- `ComplianceFindings.DeviationId` (nullable)
- `PoamEntries.DeviationId` (nullable)

### Step 3: Migrate RiskAcceptance data
- Seed Deviations from existing RiskAcceptances using the field mapping in research.md R1.
- Set `DeviationType = 'RiskAcceptance'` for all migrated records.
- Update `ComplianceFindings.DeviationId` and `PoamEntries.DeviationId` for migrated records.

### Step 4: Drop RiskAcceptances table
- Remove `RiskAcceptances` table and its FK constraints.
- Remove `RiskAcceptance` entity from `AtoCopilotContext.DbSet`.

---

## Entity Relationship Diagram

```
RegisteredSystem ──┐
                   │ 1:N
                   ▼
              Deviation ◄─── FindingId (0..1) ──── ComplianceFinding
                   │
                   ├──── PoamEntryId (0..1) ──── PoamItem
                   │
                   ├──── BoundaryDefinitionId (0..1) ──── AuthorizationBoundaryDefinition
                   │
                   └──── AuthorizationDecisionId (0..1) ──── AuthorizationDecision
```
