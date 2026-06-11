# Research: eMASS Workflow Sync & OSCAL 1.1.2 Upgrade

**Feature**: 071-emass-workflow-sync | **Date**: 2026-06-11

## 1. OSCAL 1.0.6 → 1.1.2 Breaking Changes

### Source

NIST OSCAL GitHub changelog: https://github.com/usnistgov/OSCAL/releases  
OSCAL 1.1.0 release notes identify the following breaking changes relevant to this feature:

### Plan of Action & Milestones (POA&M)

| Change | 1.0.6 | 1.1.2 | Impact |
|--------|-------|-------|--------|
| Rename field | `related-observations` | `related-findings` | **Breaking** — eMASS import will reject documents using the old field name |
| New required field | *(absent)* | `import-ssp` at root level | **Required** for eMASS linkage; omitting causes validation warning |
| Version string | `"1.0.6"` | `"1.1.2"` | Must match across all artifacts in a package |

### Assessment Results

| Change | 1.0.6 | 1.1.2 | Impact |
|--------|-------|-------|--------|
| `target.status` structure | `"status": "pass"` (string) | `"status": { "state": "satisfied" }` (object) | **Breaking** — field restructured; must use new object format |
| `reviewed-controls` | *(absent at results level)* | `reviewed-controls.control-selections` required | **Required** for eMASS compliance assessment import |
| New root field | *(absent)* | `import-ap` (Assessment Plan reference) | Required to link AR → SAP |
| Version string | `"1.0.6"` | `"1.1.2"` | Same as above |

### Relevant NIST URLs (air-gapped: use bundled schemas from Feature 041 T002)
- OSCAL POA&M schema: `src/Ato.Copilot.Core/Resources/oscal-schemas/oscal_poam_schema.json`
- OSCAL Assessment Results schema: `src/Ato.Copilot.Core/Resources/oscal-schemas/oscal_assessment-results_schema.json`

---

## 2. eMASS Import Requirements for System Identifiers

### DitprId and EmassId

eMASS uses two identifier fields to match an imported package to an existing system record:

| Field | eMASS Column | Required? | Notes |
|-------|-------------|-----------|-------|
| `DitprId` | `DITPR System ID` | **Mandatory** for DoD systems | Links to the DoD IT Portfolio Repository |
| `EmassId` | `eMASS System ID` | **Mandatory** for systems already registered in eMASS | Auto-assigned by eMASS on system creation |

If either field is missing, eMASS either rejects the import or creates a duplicate system record, both of which require manual eMASS administrator intervention to clean up.

**OSCAL representation**:
```json
"system-id": [
  { "identifier-type": "https://ies.apps.mil/jira/DoD-DITPR", "id": "<DitprId>" },
  { "identifier-type": "https://dodea.emass.mil", "id": "<EmassId>" }
]
```
This structure appears in the SSP, POA&M, and Assessment Results `metadata` block.

---

## 3. eMASS Excel Import Template Structure

The existing `EmassImportParser` parses eMASS Excel exports. Key worksheets for round-trip sync:

| Worksheet | Key Fields for Diff |
|-----------|---------------------|
| `System Information` | SystemName, Description, SystemType, SecurityCategorization (CIA), DitprId, EmassId |
| `Controls` | ControlId, ImplementationStatus, Narrative, TestMethod |
| `POA&M` | PoamId, WeaknessName, ScheduledCompletionDate, MitigationDescription, Status |

**Source**: `EmassImportParser.cs` — review existing column mappings before implementing diff logic in `EmassRoundTripSyncService`.

---

## 4. Conflict Detection Strategy

### Field-Level Diff Approach

The sync service will compare each parsed eMASS field against the corresponding SPIN DB field. Comparison rules:

| Field Type | Comparison Method |
|------------|-------------------|
| String | Case-insensitive trim comparison |
| Date | UTC date equality (ignore time component for scheduled dates) |
| Enum/Status | Normalized string comparison |
| Nullable | `null` ≠ empty string — both treated as "not set" for comparison |

### What Creates a Conflict

- Field present in eMASS import AND different from current SPIN value → `EmassConflict` record
- Field present in eMASS import but missing in SPIN → conflict of type addition
- Field present in SPIN but absent from eMASS export → **no conflict** (SPIN data is authoritative for fields not in eMASS export)

### What Does NOT Create a Conflict

- Field values are identical after normalization
- Fields that SPIN owns exclusively (not tracked in eMASS Excel)

---

## 5. Round-Trip Sync — Prior Art

### Initial Onboarding (EmassImportService)

`EmassImportService.cs` handles the *initial* onboarding import — it writes SPIN entities directly from eMASS data with no conflict detection. This is appropriate for first-time import when SPIN has no existing data.

Feature 071 adds a *subsequent* sync path that MUST NOT overwrite existing SPIN data.

**Do not modify** `EmassImportService` or `EmassImportParser` for Feature 071. Create `EmassRoundTripSyncService` as a separate service that reuses the parser.

---

## 6. Workflow Status — Export History Source

`EmassWorkflowStatusService` needs to know when each data category was last exported. Potential sources:

| Option | Pros | Cons |
|--------|------|------|
| Query `AuthorizationPackage.CompletedAt` (Feature 041) | Already exists | Only tracks full packages, not individual category exports |
| Add `EmassExportLog` table | Granular per-category tracking | Requires new entity |
| Use `AuthorizationPackage.PackageArtifact` records | Artifact-level timestamps | Still tied to full packages |

**Decision**: For MVP (Feature 071), derive `LastExportedAt` from the most recent `AuthorizationPackage` with `Status = Completed` for the system. Per-category pending counts are derived from comparing entity `UpdatedAt` timestamps against the package's `CompletedAt`. A dedicated `EmassExportLog` table can be added in a future increment if per-category granularity is needed.

---

## 7. Performance Considerations

### Sync Diff Performance

For a Moderate system (400 controls, 150 POA&M items):
- ~550 entities to compare
- Each comparison is an in-memory field-level diff (no additional DB queries per entity)
- Batch insert of conflict records using EF Core `AddRangeAsync` + single `SaveChangesAsync`
- Expected: well under NFR-001's 30-second target

**Risk**: N+1 query if sync service fetches entities one-by-one. Mitigation: Eager-load all system entities before beginning diff.

### Status Endpoint Performance

Status endpoint aggregates:
1. Count query on `EmassConflict` table (indexed on `RegisteredSystemId, ConflictStatus`)
2. Max timestamp query on `AuthorizationPackage` (indexed on `RegisteredSystemId`)
3. Readiness check (in-memory field presence checks only)

Expected response time: < 100 ms on indexed queries. NFR-002 target is 500 ms.

---

## 8. RBAC Model

| Operation | Allowed Roles | Rationale |
|-----------|---------------|-----------|
| View workflow status | ISSO, ISSM, AO | Read-only — all eMASS stakeholders need visibility |
| Run readiness check | ISSO, ISSM, AO | Read-only |
| Upload sync file | ISSO, ISSM | Only primary ATO workspace users can initiate sync |
| Resolve conflict (KeepSpin/AcceptEmass) | ISSO, ISSM | Data changes require the accountable role |
| Resolve conflict (Deferred) | ISSO, ISSM, AO | Deferral is informational only |

**Source**: Consistent with existing RBAC in `EmassExportTools.cs` and Feature 041 endpoints.
