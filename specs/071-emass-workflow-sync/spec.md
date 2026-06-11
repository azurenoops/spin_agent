# Feature Specification: eMASS Workflow Sync & OSCAL 1.1.2 Upgrade

**Feature**: 071-emass-workflow-sync
**Branch**: `071-emass-workflow-sync`
**Created**: 2026-06-11
**Status**: Draft
**GitHub Issue**: [#59](https://github.com/azurenoops/spin_agent/issues/59)
**Extends**: `specs/041-emass-package` (Feature 041 — eMASS Authorization Package Export)
**Wave**: 9

## Problem Statement

ISSOs who work across both SPIN and eMASS face three compounding pain points:

1. **OSCAL version mismatch** — Feature 041's POA&M and Assessment Results exports emit OSCAL 1.0.6 while the SSP emits 1.1.2. eMASS import validation rejects packages with mixed OSCAL versions, making *every current package submission fail* until this is fixed.
2. **No round-trip sync** — After onboarding via `EmassImportService`, there is no way to re-import a newer eMASS Excel export without destructively overwriting SPIN data. ISSOs who update data in eMASS cannot safely bring those updates into SPIN.
3. **Blind export status** — There is no single view showing what SPIN data has already been exported to eMASS, when, and what is still pending. ISSOs must manually track export state outside the tool.

Two additional gaps block eMASS package acceptance:
- **Missing system identifiers** — `DitprId` and `EmassId` are required fields for eMASS package import but are currently optional/unpopulated in exports.
- **No pre-export readiness check** — No mechanism validates that all required eMASS fields are present before the ISSO attempts a submission that will be rejected.

## Goal

Extend Feature 041 so that SPIN can serve as the ISSO's **primary ATO workspace** while eMASS remains the system of record. Specifically:

1. Upgrade all OSCAL exports (POA&M, Assessment Results) to 1.1.2 so packages are accepted by eMASS.
2. Enable safe round-trip sync: import newer eMASS data, diff against SPIN state, surface conflicts for ISSO review — never silently overwrite.
3. Provide a single `EmassWorkflowStatus` view showing exported vs. pending data.
4. Gate exports with an `EmassExportReadiness` check validating all eMASS-required fields.

## Scope

### In Scope

- **OSCAL 1.0.6 → 1.1.2 upgrade** in `EmassExportService` (`BuildOscalPoam`, `BuildOscalAssessmentResults`)
- **`EmassExportReadiness` service** — validates `DitprId`, `EmassId`, SSP completeness, POA&M completeness before export
- **`EmassRoundTripSync` service** — imports a newer eMASS Excel, diffs against SPIN state, persists conflict records for ISSO review; never auto-merges
- **`EmassWorkflowStatus` DTO + endpoint** — `GET /api/systems/{id}/emass/status` returning export history, pending items, and last sync timestamp
- **MCP tools**: `emass_get_workflow_status`, `emass_check_export_readiness`
- **`EmassConflict` entity** — persists field-level conflicts awaiting ISSO resolution
- **Dashboard route** — `/systems/{id}/emass/status` for the ISSO workflow status view

### Out of Scope

- Full authorization package ZIP assembly (Feature 041)
- SAR generation (Feature 041)
- OSCAL SAP export (Feature 041)
- Evidence repository integration (Feature 041)
- Automated conflict resolution / merge (conflicts are surfaced only — ISSO decides)
- eMASS API live integration (all sync is file-based Excel import/export)
- Changes to the initial onboarding wizard (`Step3EmassImport.tsx`, `EmassImportWizard.tsx`)

## User Stories

### US1 — OSCAL Version Consistency (Priority: P0 — Blocking)

As an ISSO submitting a package to eMASS, I need all OSCAL artifacts to use OSCAL 1.1.2 so that eMASS does not reject the import with a version conflict error.

**Why P0**: Every eMASS package generated today will be rejected. This is a blocking defect, not a feature addition.

**Acceptance Scenarios**:
1. **Given** the system generates an OSCAL POA&M export, **When** the JSON is inspected, **Then** `metadata.oscal-version` equals `"1.1.2"` and the `poam-items` array uses the 1.1.2 schema (field `related-findings` not `related-observations`).
2. **Given** the system generates an OSCAL Assessment Results export, **When** the JSON is inspected, **Then** `metadata.oscal-version` equals `"1.1.2"`, `results[].reviewed-controls` is present, and `target.status.state` uses valid 1.1.2 values.
3. **Given** a full authorization package (Feature 041), **When** each OSCAL artifact is extracted and validated, **Then** all four artifacts (SSP, POA&M, AR, SAP) carry `"oscal-version": "1.1.2"`.

---

### US2 — Export Readiness Check (Priority: P1)

As an ISSO, before exporting to eMASS I want SPIN to validate that all required eMASS fields are populated so that I can fix gaps before attempting a submission that eMASS will reject.

**Why P1**: eMASS returns cryptic rejection codes. Pre-flight validation saves hours of back-and-forth.

**Acceptance Scenarios**:
1. **Given** a system missing `DitprId` and `EmassId`, **When** the ISSO runs the readiness check, **Then** the response lists both fields as blocking gaps with links to where they can be populated.
2. **Given** a system with all required fields complete, **When** the readiness check runs, **Then** it returns `"ready": true` with a summary of what will be exported.
3. **Given** the ISSO asks the agent "Is System X ready for eMASS export?", **When** the `emass_check_export_readiness` MCP tool is called, **Then** the response is a formatted readiness card listing gaps (if any) with actionable fix steps.

---

### US3 — Round-Trip Sync (Priority: P1)

As an ISSO, I want to import a newer eMASS Excel export into SPIN so that I can see what changed in eMASS since my last import — without SPIN overwriting data I have already entered.

**Why P1**: Without round-trip sync, the ISSO must manually reconcile two systems, defeating the purpose of SPIN as the primary workspace.

**Acceptance Scenarios**:
1. **Given** an eMASS Excel file newer than the last SPIN import, **When** the ISSO uploads it via the sync endpoint, **Then** SPIN computes a field-level diff and stores `EmassConflict` records for every changed field, with `SpinValue` and `EmassValue` visible to the ISSO.
2. **Given** existing `EmassConflict` records, **When** the ISSO reviews them, **Then** they can resolve each conflict by choosing "Keep SPIN", "Accept eMASS", or "Defer" — SPIN data is only updated when the ISSO explicitly chooses "Accept eMASS".
3. **Given** fields that are identical in both SPIN and the new eMASS export, **When** the sync runs, **Then** no conflict is created for those fields — only changed fields surface.
4. **Given** the sync runs while unresolved conflicts exist from a prior sync, **When** the ISSO attempts a second sync, **Then** SPIN warns the ISSO that unresolved conflicts exist and requires acknowledgment before proceeding.

---

### US4 — Workflow Status View (Priority: P1)

As an ISSO, I want a single dashboard view showing what SPIN data has been exported to eMASS, when it was exported, and what is still pending, so that I can track my eMASS submission progress without checking two systems.

**Why P1**: ISSOs currently have no visibility into SPIN→eMASS export state. This directly addresses the issue's "SPIN as primary workspace" requirement.

**Acceptance Scenarios**:
1. **Given** a system with prior exports, **When** the ISSO opens the eMASS Status page, **Then** they see: last export timestamp, which data categories were exported (controls, POA&Ms, artifacts), how many items are pending export, and any unresolved sync conflicts.
2. **Given** the ISSO asks the agent "What's my eMASS export status for System X?", **When** `emass_get_workflow_status` is called, **Then** the response includes a formatted table showing exported vs. pending items per category.
3. **Given** a system that has never been exported, **When** the status page loads, **Then** it shows `"Never exported"` with a call-to-action to run the readiness check.

---

### US5 — System Identifier Completeness (Priority: P1)

As an ISSO, I need `DitprId` and `EmassId` to be populated and included in all eMASS exports so that eMASS can match the imported data to the correct system record.

**Why P1**: Without these identifiers, eMASS cannot associate the imported artifacts with any system — the import silently fails or creates a duplicate record.

**Acceptance Scenarios**:
1. **Given** `DitprId` and `EmassId` are set on a system, **When** any eMASS Excel export is generated, **Then** both fields appear in the correct eMASS columns per the eMASS import template.
2. **Given** an OSCAL POA&M or Assessment Results export, **When** the JSON is inspected, **Then** the `metadata.system-id` array includes entries for both `DitprId` and `EmassId` under their respective schemes.
3. **Given** a readiness check where `DitprId` is empty, **When** results are returned, **Then** `DitprId` appears as a **blocking** gap (not a warning).

---

## Edge Cases

- What if the ISSO uploads an eMASS Excel from a *different system*? → Validate the system identifier in the file against `EmassId`; reject with a clear error if they do not match.
- What if a synced field has been deleted in SPIN but exists in eMASS? → Surface as a conflict of type `Deletion` — the ISSO must explicitly decide.
- What if the ISSO resolves conflicts and then uploads yet another newer eMASS file? → Create a new sync batch; prior resolved conflicts are archived, not overwritten.
- What if `DitprId` / `EmassId` are present in the onboarding import but were later cleared? → The readiness check catches this; the status page shows a "System identifiers missing" warning.
- What if OSCAL schema validation fails after the 1.1.2 upgrade? → The upgraded builder must pass the bundled OSCAL 1.1.2 schema validation introduced in Feature 041 T024.

## Requirements

### Functional Requirements

**OSCAL Upgrade (US1)**
- **FR-001**: `BuildOscalPoam()` in `EmassExportService` MUST emit `"oscal-version": "1.1.2"` and use `related-findings` (not `related-observations`) per the 1.1.2 schema change.
- **FR-002**: `BuildOscalAssessmentResults()` in `EmassExportService` MUST emit `"oscal-version": "1.1.2"`, include `reviewed-controls` with `control-selections`, and use valid `target.status.state` values per the 1.1.2 schema.
- **FR-003**: Both upgraded builders MUST pass validation against the OSCAL 1.1.2 schemas bundled in Feature 041 (`OscalSchemaValidationService`).

**Export Readiness (US2 + US5)**
- **FR-004**: `EmassExportReadinessService` MUST validate: `DitprId` present, `EmassId` present, SSP has at least one Approved section, all POA&M items have scheduled completion dates, system categorization (Confidentiality/Integrity/Availability) is populated.
- **FR-005**: Readiness response MUST classify each gap as `Blocking` (export must not proceed) or `Advisory` (export may proceed with warning).
- **FR-006**: `DitprId` and `EmassId` MUST be treated as `Blocking` gaps.
- **FR-007**: All eMASS Excel exports MUST include `DitprId` and `EmassId` in the appropriate columns; OSCAL exports MUST include them in `metadata.system-id`.

**Round-Trip Sync (US3)**
- **FR-008**: `EmassRoundTripSyncService` MUST parse an uploaded eMASS Excel file using the existing `EmassImportParser` and diff it against current SPIN state at the field level.
- **FR-009**: Each differing field MUST produce an `EmassConflict` record with: `FieldName`, `SpinValue`, `EmassValue`, `ConflictStatus` (Unresolved/KeepSpin/AcceptEmass/Deferred), `DetectedAt`, `ResolvedAt`, `ResolvedBy`.
- **FR-010**: SPIN data MUST NOT be modified during a sync run — only `EmassConflict` records are written.
- **FR-011**: Conflict resolution endpoint MUST accept a resolution choice and update SPIN data **only** when `AcceptEmass` is chosen.
- **FR-012**: A second sync MUST warn if unresolved conflicts from the prior batch exist; the ISSO must acknowledge before proceeding.
- **FR-013**: Unchanged fields MUST NOT produce conflict records.

**Workflow Status (US4)**
- **FR-014**: `GET /api/systems/{id}/emass/status` MUST return `EmassWorkflowStatus` including: `LastExportedAt`, `LastSyncedAt`, `UnresolvedConflictCount`, per-category export summary (`Controls`, `PoamItems`, `Artifacts`), and `ReadinessStatus` (summary only — not full gaps list).
- **FR-015**: The endpoint MUST be accessible to users with ISSO, ISSM, or AO roles.
- **FR-016**: If the system has never been exported, `LastExportedAt` MUST be `null` and `OverallStatus` MUST be `"NeverExported"`.

**MCP Tools**
- **FR-017**: `emass_get_workflow_status` MCP tool MUST accept `system_id` and return the `EmassWorkflowStatus` DTO formatted as a markdown table.
- **FR-018**: `emass_check_export_readiness` MCP tool MUST accept `system_id` and return a readiness card listing gaps with fix instructions; both tools require ISSO/ISSM/AO role.

**Access Control**
- **FR-019**: All new endpoints and tools MUST enforce ISSO, ISSM, or AO role authorization.
- **FR-020**: Conflict resolution (Accept eMASS value) MUST be restricted to ISSO and ISSM roles (not AO read-only).

### Non-Functional Requirements

- **NFR-001**: Round-trip sync diff for a system with 400 controls and 150 POA&M items MUST complete in under 30 seconds.
- **NFR-002**: `EmassWorkflowStatus` endpoint MUST respond in under 500 ms (reads only — no generation).
- **NFR-003**: All new endpoints MUST follow the existing API envelope pattern (`{ data, meta, errors }`).
- **NFR-004**: Conflict records MUST be retained for 90 days after resolution.
- **NFR-005**: OSCAL upgrade MUST NOT break existing Feature 041 package generation — all Feature 041 tests must continue to pass.

## Success Criteria

- **SC-001**: 100% of OSCAL exports (SSP, POA&M, AR, SAP) carry `"oscal-version": "1.1.2"` — zero version mismatch in generated packages.
- **SC-002**: `EmassExportReadiness` correctly identifies all blocking gaps for 5 representative system configurations (no false negatives on `DitprId`/`EmassId`).
- **SC-003**: Round-trip sync surfaces field-level conflicts without modifying SPIN data until the ISSO explicitly accepts.
- **SC-004**: `EmassWorkflowStatus` endpoint returns accurate per-category export counts and conflict count in < 500 ms.
- **SC-005**: All new MCP tools are callable from dashboard chat, Teams, and VS Code with formatted responses.
