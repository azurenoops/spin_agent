# Research: Deviation Management (Feature 035)

**Date**: 2026-03-17
**Status**: Complete — all unknowns resolved

---

## R1: RiskAcceptance → Deviation Field Mapping

**Decision**: Migrate existing `RiskAcceptance` records into `Deviation` entity via EF migration. Drop `RiskAcceptances` table after data transfer.

**Field Mapping**:

| RiskAcceptance Field | Deviation Field | Notes |
|---------------------|----------------|-------|
| `Id` | `Id` | Direct copy (GUID string) |
| `AuthorizationDecisionId` | `AuthorizationDecisionId` | Retained as nullable FK |
| `FindingId` | `FindingId` | Nullable FK to ComplianceFinding |
| `ControlId` | `ControlId` | Direct copy (max 20) |
| `CatSeverity` | `CatSeverity` | Direct copy (enum) |
| `Justification` | `Justification` | Direct copy (max 4000) |
| `CompensatingControl` | `CompensatingControls` | Renamed to plural; kept as string (max 2000) |
| `ExpirationDate` | `ExpirationDate` | Direct copy |
| `AcceptedBy` | `RequestedBy` | Semantic rename — risk was "accepted", deviations are "requested" |
| `AcceptedAt` | `CreatedAt` | Same semantics |
| `IsActive` | `Status` | `true` → `Approved`, `false` + `RevokedAt != null` → `Revoked`, `false` + `ExpirationDate < now` → `Expired` |
| `RevokedAt` | `RevokedAt` | Direct copy |
| `RevokedBy` | `RevokedBy` | Direct copy |
| `RevocationReason` | `RevocationReason` | Direct copy |
| *(none)* | `DeviationType` | Set to `RiskAcceptance` for all migrated records |
| *(none)* | `ReviewedBy` | Set to `AcceptedBy` (AO who accepted = reviewer who approved) |
| *(none)* | `ReviewedAt` | Set to `AcceptedAt` |
| *(none)* | `ReviewCycle` | Default to `Annual` for migrated records |
| *(none)* | `EvidenceReferences` | Set to empty JSON array `[]` |
| *(none)* | `PoamEntryId` | Populate by joining on FindingId → PoamItem |
| *(none)* | `BoundaryDefinitionId` | Set to `null` (unscoped for migrated records) |
| *(none)* | `ISSMRecommendation` | Set to `null` (not applicable for migrated records) |
| *(none)* | `ISSMRecommendedBy` | Set to `null` (not applicable for migrated records) |
| *(none)* | `ISSMRecommendedAt` | Set to `null` (not applicable for migrated records) |

**Rationale**: Direct field correspondence exists for all core data. New workflow fields (reviewer, review cycle, evidence) get safe defaults. The `IsActive` boolean maps to a tri-state based on `RevokedAt` and `ExpirationDate`.

**Alternatives Considered**: Keeping both entities (rejected — dual source of truth for risk acceptance data; `compliance_accept_risk` tool would need conditional routing).

---

## R2: Evidence Reference Storage Pattern

**Decision**: Store evidence references as a JSON array of `ScanImportRecordId` strings in a `TEXT` column on the `Deviation` entity. No separate join table.

**Existing Pattern**: The codebase uses `ScanImportRecord` (parent) → `ScanImportFinding` (child) for all scan imports (CKL, XCCDF, Prisma). Each `ScanImportRecord` has:
- `Id`, `FileName`, `FileHash`, `ScanImportType` (CKL/XCCDF/PRISMA)
- `BenchmarkId`, `BenchmarkVersion`, `BenchmarkTitle`
- `TargetHostName`, `ScanTimestamp`
- Count aggregates (TotalEntries, OpenCount, PassCount, etc.)

**Storage Format**:
```json
["scan-import-id-1", "scan-import-id-2"]
```

**UI Hydration**: Dashboard fetches scan import details on-demand when rendering evidence in the detail drawer. Shows filename, scan type, date, and count summary per evidence reference.

**Rationale**: JSON array is lightweight, avoids adding a join table for a typically small set (1-3 evidence references per deviation). The existing `ScanImportRecord` entity already contains all the metadata needed for display.

**Alternatives Considered**: Separate `DeviationEvidence` join table (rejected — overkill for 1–3 references; direct string IDs plus join-free evidence metadata for scans/documents). Free-text evidence descriptions (rejected — scan import IDs enable structured display with file/benchmark details).

---

## R3: Auto-Expiration Mechanism

**Decision**: Implement a dedicated `DeviationExpirationService` as an `IHostedService` that runs daily alongside the existing `DigestSchedulerHostedService`. Use the same timer pattern.

**Current Behavior**:
- RiskAcceptance auto-expiration is **query-time** (lazy): `GetRiskRegisterAsync()` checks `ExpirationDate < UtcNow` and sets `IsActive = false` on read.
- No background service for proactive expiration — only alert escalation via `CheckExpirationAsync()` at graduated severity levels (Info@90d, Warning@60d, Urgent@30d, Critical@expired).
- Existing `DigestSchedulerHostedService` pattern: `BackgroundService` with daily timer at 08:00 UTC + immediate execution on startup.

**New Service Design**:
1. Run daily at 06:00 UTC (before digest at 08:00 UTC so digests include freshly expired items).
2. Query: `Deviations WHERE Status = Approved AND ExpirationDate < UtcNow`.
3. For each expired deviation:
   - Set `Status = Expired`.
   - Revert linked `Finding.Status` to `Open`.
   - Revert linked `PoamEntry.Status` to `Ongoing` (if it was `RiskAccepted`).
   - Log `DashboardActivity` audit record.
   - Fire notification to requestor and reviewer via `INotificationBroadcaster`.
4. Query: `Deviations WHERE Status = Approved AND ExpirationDate < UtcNow + 30d` → fire 30d/7d expiration warnings.

**Rationale**: Background service guarantees the spec's SC-003 ("100% of expired deviations revert within 24h"). Query-time expiration would only catch it when someone views the data, which is insufficient for compliance.

**Alternatives Considered**: Query-time lazy expiration (rejected — fails SC-003 if nobody queries the system on the day of expiration). Cron job (rejected — hosted service integrates cleanly with ASP.NET Core DI and lifecycle).

---

## R4: eMASS POA&M Export Enrichment

**Decision**: Add a new "Deviation Justification" column to the eMASS POA&M export and inject deviation data for risk-accepted items.

**Current Export**: `EmassExportService` generates XLSX via ClosedXML with 24 columns. POA&Ms with `Status == RiskAccepted` are marked `IsActive = false`. No justification text is included.

**Enrichment**:
1. Add column 25: **"Deviation Justification"** after "Last Updated By".
2. Add column 26: **"Deviation Type"** (FalsePositive/RiskAcceptance/Waiver).
3. Add column 27: **"Deviation Expiration"** (ISO date).
4. For each POA&M row, LEFT JOIN to `Deviation` via `PoamEntryId` or `FindingId` WHERE `Status = Approved`.
5. Populate new columns from matching deviation record; leave blank if no deviation.

**Rationale**: Non-breaking change — existing columns untouched; auditors get full deviation context inline with POA&M data. eMASS import tools typically ignore unrecognized trailing columns.

**Alternatives Considered**: Inject into `Comments` field (rejected — Comments may already have data; separate columns are cleaner for auditor review). Separate deviation-only export (rejected per clarification Q4 — user chose existing exports only).

---

## R5: OSCAL SSP Export Integration

**Decision**: Inject deviation data into the OSCAL SSP export as `risk` entries within the `back-matter` resources section and as annotations on affected `control-implementation` statements.

**Current Export**: `OscalSspExportService` generates OSCAL 1.1.2 JSON with: `metadata`, `import-profile`, `system-characteristics`, `system-implementation`, `control-implementation`, `back-matter`.

**Integration Points**:
1. **`control-implementation` annotations**: For controls with approved deviations, add a `props` entry:
   ```json
   {
     "name": "deviation-type",
     "value": "risk-acceptance",
     "ns": "https://ato-copilot.azurenoops.io/ns/oscal"
   }
   ```
2. **`back-matter` resources**: Add a resource per approved deviation with:
   - `uuid`: Deviation ID
   - `title`: "Risk Acceptance: {ControlId}" (or "False Positive" / "Waiver")
   - `description`: Deviation justification text
   - `props`: severity, expiration date, reviewer, compensating controls

**Rationale**: OSCAL 1.1.2 doesn't have a formal `risk` assembly in SSP (that's in the Assessment Results model). Using `props` and `back-matter` resources is the standard extension mechanism for custom data in OSCAL SSP.

**Alternatives Considered**: OSCAL Assessment Results `risk` assembly (rejected — that's a separate document type; the SSP export shouldn't contain assessment results). Omitting from OSCAL entirely (rejected per clarification Q4).
