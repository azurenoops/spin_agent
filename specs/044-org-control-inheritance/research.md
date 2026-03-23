# Research: Org-Level Control Inheritance

**Feature**: 044-org-control-inheritance  
**Date**: 2026-03-21

## 1. Org-Default Storage Strategy

**Decision**: Separate `OrgInheritanceDefault` table  
**Rationale**: Clean separation from per-system `ControlInheritances` table. Re-derivation is a simple delete-all + reinsert on one bounded table. No risk of polluting per-system queries with sentinel rows. The existing `ControlInheritance` entity uses `ControlBaselineId` as a composite scope key (tied to a system's baseline) — org defaults have no baseline, so they don't fit the existing schema.  
**Alternatives considered**:
- Flagged rows in `ControlInheritances` with `IsOrgDefault = true` — rejected: requires nullable `ControlBaselineId`, breaks existing indexes and queries that assume baseline-scoped data.

## 2. Derivation Trigger Strategy

**Decision**: Synchronous inline derivation during capability save API call  
**Rationale**: The derivation query (aggregate org-wide capability mappings → inheritance defaults) operates on ~416 controls and ~11 capabilities — well within single-digit millisecond range. No background job infrastructure needed. EF Core transaction ensures atomicity with the capability save.  
**Alternatives considered**:
- Async background job (e.g., Hangfire) — rejected: over-engineered for data volume; adds infrastructure dependency
- Manual admin trigger button — rejected: defeats automation goal; risks stale defaults

## 3. Derivation Hook Points

**Decision**: Hook into `CapabilityService.CreateMappingsAsync`, `CapabilityService.UpdateCapabilityAsync`, and `CapabilityService.DeleteCapabilityAsync`  
**Rationale**: These are the three mutation paths for capability control mappings. Each already uses the `AtoCopilotContext` and runs within a transaction. Calling `IOrgInheritanceService.ReDeriveAsync()` at the end of each method ensures defaults stay synchronized.  
**Implementation detail**: The new `IOrgInheritanceService` is injected into `CapabilityService` via constructor DI.

## 4. Baseline Propagation Hook Point

**Decision**: Insert org-default propagation in `BaselineService.SelectBaselineAsync` between inheritance snapshot reapplication (line ~119) and narrative auto-population (line ~144)  
**Rationale**: After existing inheritance snapshots are restored (preserving manual overrides from re-selection), org defaults fill in gaps for controls that have no snapshot — this is exactly the "new system selects baseline" flow. Controls with existing snapshot designations (overrides) are skipped.  
**Implementation detail**: Call `IOrgInheritanceService.PropagateToSystemAsync(systemId, baselineId, newControlIds, cancellationToken)`.

## 5. Role-to-Designation Mapping

**Decision**: Primary/Supporting → "Inherited"; Shared → "Shared"  
**Rationale**: Confirmed in spec clarification session. The `CapabilityMappingRole` enum already has `Primary`, `Supporting`, `Shared` values (defined in `DashboardEnums.cs`). Supporting capabilities still deliver full CSP coverage; the distinction is about relative importance, not customer responsibility.  
**Precedence rule**: When multiple capabilities map to the same control — Primary/Supporting takes precedence over Shared. Multiple same-role mappings merge providers.

## 6. DesignationSource Tracking

**Decision**: Add `DesignationSource` enum to existing `ControlInheritance` entity and extend `InheritanceChangeSource` enum  
**Rationale**: The existing `ControlInheritance` entity needs to know whether it was org-derived or manually set (for UI badges and filtering). The existing `InheritanceChangeSource` enum needs `OrgDerived` and `OrgPropagation` values for audit entries.  
**New enum values**:
- `InheritanceChangeSource.OrgDerived` — set during org-default propagation to a system
- `InheritanceChangeSource.OrgPropagation` — set when org defaults change and cascade to systems

**New property on `ControlInheritance`**:
- `DesignationSource` (enum: `Manual`, `OrgDerived`, `ProfileApply`, `CrmImport`) — tracks how this designation was set
- `OrgInheritanceDefaultId` (string, nullable FK) — links to the org default it was derived from

## 7. Frontend UX Patterns

**Decision**: Extend existing ControlInheritance.tsx with source badges, filters, and button reorganization  
**Rationale**: The existing page already has `InheritanceSummaryBar`, `BulkUpdateToolbar`, and filter dropdowns. Adding a "Source" filter and badges follows the established pattern. The "More Actions" dropdown for Apply CSP Profile follows the existing `CspProfileDialog` component pattern.  
**Existing API pattern**: Axios client at `src/Ato.Copilot.Dashboard/src/api/inheritance.ts` — new org-default endpoints follow the same pattern with `apiClient.get`/`apiClient.put`.

## 8. Org-Default Endpoint Routing

**Decision**: Add org-level endpoints under `/api/dashboard/inheritance/org-defaults` (not organization-scoped)  
**Rationale**: The current deployment is single-org. No `organizationId` parameter needed. Endpoints sit alongside existing inheritance endpoints in `DashboardEndpoints.cs` under the `/api/dashboard` group. System-specific endpoints remain at `/systems/{systemId}/inheritance` — the list endpoint gains a `source` query parameter for filtering.  
**New endpoints**:
- `GET /api/dashboard/inheritance/org-defaults` — list all org-level defaults
- `POST /api/dashboard/inheritance/org-defaults/derive` — manually trigger re-derivation (admin action)
- `GET /api/dashboard/systems/{systemId}/inheritance` — extended with `source` filter param

## 9. Migration Strategy

**Decision**: Single migration file following existing naming convention  
**Rationale**: Naming pattern: `YYYYMMDDHHMMSS_Feature044_OrgLevelInheritance.cs`. Creates `OrgInheritanceDefaults` table, adds `DesignationSource` and `OrgInheritanceDefaultId` columns to `ControlInheritances`, extends `InheritanceAuditEntries` indexes.  
**Table**: `OrgInheritanceDefaults` with columns: Id, ControlId, InheritanceType, Provider, SourceCapabilityId, DerivedAt. Indexes on ControlId (unique) and SourceCapabilityId.

## 10. CRM Export Enhancement

**Decision**: Add "Designation Source" column to CRM exports  
**Rationale**: Existing `CrmExportService` already supports multiple layout formats (custom, FedRAMP, eMASS). The new column is appended to all layouts. Value is "Org Default" or "System Override" based on `DesignationSource` property. The export query JOIN now includes the `OrgInheritanceDefault` reference for traceability.
