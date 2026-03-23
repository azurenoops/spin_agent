# Research: Control Inheritance & Customer Responsibility Matrix

**Feature**: 043-control-inheritance  
**Date**: 2026-03-20

## R1: Existing Backend Service Layer Readiness

**Question**: Are `IBaselineService` methods stable and sufficient for dashboard exposure?

**Decision**: The existing service methods are fully stable and ready.

**Rationale**: 
- `SetInheritanceAsync(systemId, InheritanceInput[], setBy)` — upserts inheritance records, recalculates baseline counts, returns `InheritanceResult`. Supports multiple controls in a single call (bulk-ready).
- `GenerateCrmAsync(systemId)` — returns `CrmResult` with family-grouped entries, counts, and percentages. Already computes `UndesignatedControls` = Total − Inherited − Shared − Customer.
- `GetBaselineAsync(systemId, includeDetails?, familyFilter?)` — returns baseline with optional eager-loaded `Inheritances` and `Tailorings` collections. Supports family filter.
- `TailorBaselineAsync(systemId, TailoringInput[], tailoredBy)` — endpoint already exists but not needed for Phase 1 inheritance core.

**Alternatives considered**: 
- Creating a separate `IControlInheritanceService` — rejected because the logic is already in `IBaselineService` and splitting would create unnecessary abstraction.
- Direct EF queries in endpoints — rejected per constitution (services handle business logic).

---

## R2: Audit Entry Pattern

**Question**: How should `InheritanceAuditEntry` be implemented to match existing audit patterns?

**Decision**: New `InheritanceAuditEntry` EF entity modeled after `DashboardActivity` and `AuditLogEntry` patterns, stored as append-only rows in a dedicated table.

**Rationale**:
- `AuditLogEntry` in the codebase stores action, userId, outcome, details (JSON), and timestamp — good pattern for the structure.
- `DashboardActivity` stores per-system events with actor/summary — good pattern for the activity feed integration.
- `InheritanceAuditEntry` needs: ControlInheritanceId FK, ControlId, Actor, PreviousInheritanceType, NewInheritanceType, PreviousProvider, NewProvider, PreviousCustomerResponsibility, NewCustomerResponsibility, Timestamp, ChangeSource (enum: Manual, BulkUpdate, ProfileApply, CrmImport).
- Audit entry creation should happen in `BaselineService.SetInheritanceAsync()` since it's the single path for all updates.

**Alternatives considered**:
- Reusing `AuditLogEntry` with JSON details — rejected because structured fields enable better querying and reporting.
- Database triggers — rejected because EF Core doesn't support them portably across SQLite/SQL Server.

---

## R3: CSP Profile Data Format and Loading

**Question**: What format and loading mechanism should pre-built CSP profiles use?

**Decision**: JSON config files in `src/seed-data/csp-profiles/`, loaded at startup into an in-memory list by a `CspProfileService`. Administrators extend by adding new JSON files.

**Rationale**:
- JSON is the standard config format used throughout the project (settings, OSCAL, etc.).
- Files are embedded as content items in the `.csproj` and copied to build output, or loaded from a configurable directory path.
- Profile schema: `{ profileId, name, provider, baselineLevel, description, controls: [{ controlId, inheritanceType, customerResponsibility? }] }`
- Azure Government FedRAMP High profile covers ~325 NIST 800-53 Rev 5 controls — data sourced from Microsoft's published Azure Government CRM documentation.
- Loading is a singleton service that reads all JSON files from the profiles directory at startup and exposes them as an in-memory collection.

**Alternatives considered**:
- Database-stored profiles — rejected because admin-extensibility via JSON files is simpler and matches the spec's "no code changes required" requirement.
- Embedded resources — rejected because administrators can't modify embedded resources without rebuilding.

---

## R4: CRM Export Format Strategy

**Question**: How should multi-format CRM export (Custom, FedRAMP, eMASS) be implemented?

**Decision**: New `CrmExportService` with format-specific generators using ClosedXML for Excel and `StringBuilder` for CSV. Format selection via query parameter.

**Rationale**:
- ClosedXML 0.104.2 is already a project dependency, used by `EmassExportService` and `PoamService.ExportEmassExcelAsync()`.
- The existing POA&M export endpoint pattern (`GET /systems/{systemId}/poam/export?format=csv|emass_excel|oscal_json`) is the model to follow — returns `Results.File(bytes, contentType, fileName)`.
- Three CRM formats: 
  - **Custom**: ControlId, Family, InheritanceType, Provider, CustomerResponsibility (all columns from CrmEntry)
  - **FedRAMP**: Matches FedRAMP CRM template layout — Control Number, Control Name, Responsible Role (Inherited/Shared/Provided By Customer), Implementation Status, Customer Responsibility Description
  - **eMASS**: Compatible with eMASS CRM import — Control Identifier, Responsibility Type (maps from InheritanceType), Common Control Provider, Customer Responsibility
- CSV export uses `System.Text.StringBuilder` with proper RFC 4180 quoting (fields containing commas or quotes get double-quoted).

**Alternatives considered**:
- CsvHelper NuGet package — rejected to avoid adding a new dependency; the CSV format is simple enough for manual generation.
- Separate endpoints per format — rejected; single endpoint with format parameter matches the established POA&M export pattern.

---

## R5: CRM Import Strategy (Phase 3)

**Question**: How should CSV/Excel CRM import with column mapping work?

**Decision**: Backend endpoint accepts multipart file upload, returns parsed columns for frontend mapping dialog, then a second endpoint applies the mapped import.

**Rationale**:
- Two-step process: (1) `POST /systems/{systemId}/inheritance/import/preview` — parse file, return detected columns and sample rows; (2) `POST /systems/{systemId}/inheritance/import/apply` — accept column mapping + conflict resolution option, apply designations.
- CSV parsing uses `StreamReader` with comma splitting (handling quoted fields). Excel parsing uses ClosedXML (same as `EmassExportService.ImportAsync()`).
- Column mapping in frontend: user selects which source column maps to each target field (ControlId, InheritanceType, Provider, CustomerResponsibility).
- Conflict resolution: Skip existing (default) or Overwrite all — same pattern as eMASS import.
- Unrecognized control IDs flagged in preview response with "Not found in baseline" status.

**Alternatives considered**:
- Auto-detect columns by header name — implemented as a best-effort default with manual override in the mapping dialog.
- Server-side-only mapping — rejected because the spec requires user review before applying.

---

## R6: Dashboard Page Pattern

**Question**: What frontend patterns should the Control Inheritance page follow?

**Decision**: Follow the POA&M Management page pattern — it's the most similar feature (table with filters, bulk actions, summary bar, export).

**Rationale**:
- `PoamManagement.tsx` uses: summary metrics at top, filterable table below, bulk-action toolbar on selection, and export button.
- API client pattern: create `api/inheritance.ts` with typed Axios calls matching `api/poam.ts`.
- Types: create `types/inheritance.ts` with interfaces for API responses.
- Navigation: add route under `SystemLayout` in the "Compliance Posture" group (after Narratives, before Legal & Regulatory).
- Page uses `useSystemContext()` for system ID and `usePolling()` for data refresh.

**Alternatives considered**:
- Custom standalone page layout — rejected; existing `SystemLayout` with sidebar navigation is the established pattern.
- SWR/React Query for data fetching — rejected; existing codebase uses `usePolling()` + manual state management consistently.

---

## R7: Role-Based Access Control

**Question**: How should FR-026 role-gated writes be enforced?

**Decision**: The dashboard currently uses a simplified "dashboard-user" actor for all operations. Role enforcement will be implemented at the endpoint level by checking the authenticated user's role from the JWT/auth context. Read endpoints remain open to all authenticated system members.

**Rationale**:
- Existing endpoints already extract auth info from `context.User`. The pattern in `AuditLoggingMiddleware.cs` shows `context.User?.Identity?.Name` and `context.User?.FindFirst("pim_role")?.Value`.
- Write endpoints (`PUT /inheritance`, `POST /inheritance/apply-profile`, `POST /inheritance/import/apply`) will validate the user role before proceeding. Note: bulk updates use the same `PUT /inheritance` endpoint with `changeSource: "BulkUpdate"`.
- For Phase 1 implementation, the existing "dashboard-user" actor convention continues with a TODO for proper role enforcement when the auth system is fully integrated.

**Alternatives considered**:
- Policy-based authorization middleware — would be ideal but is not yet established in the codebase; can be added as a cross-cutting concern later.

---

## R8: Cross-Portfolio Inheritance (Phase 4 — Future)

**Question**: What schema changes are needed for cross-system inheritance?

**Decision**: Deferred to Phase 4. Requires adding a nullable `ProvidingSystemId` FK column to `ControlInheritance` + a dependency tracking table.

**Rationale**:
- Phase 4 is explicitly marked as "future" in the spec.
- Current `ControlInheritance.Provider` is a string field (CSP name). Cross-system would add a `ProvidingSystemId?` FK to `RegisteredSystems`.
- Impact analysis requires a new `InheritanceDependency` denormalized view or query joining consuming system → providing system.
- No Phase 1-3 work should preclude this.

**Alternatives considered**: None evaluated — deferred per spec phasing.
