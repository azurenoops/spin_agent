# Requirements Checklist: eMASS Workflow Sync & OSCAL 1.1.2 Upgrade

**Feature**: 071-emass-workflow-sync | **Issue**: #59

Use this checklist during implementation, review, and acceptance testing. Mark each item `[x]` when verified.

---

## Functional Requirements

### OSCAL 1.1.2 Upgrade (US1 — P0)

- [ ] **FR-001-a** `BuildOscalPoam()` emits `"oscal-version": "1.1.2"` in metadata
- [ ] **FR-001-b** `BuildOscalPoam()` uses `related-findings` (not `related-observations`) in `poam-items`
- [ ] **FR-001-c** `BuildOscalPoam()` includes `import-ssp` reference at root level
- [ ] **FR-002-a** `BuildOscalAssessmentResults()` emits `"oscal-version": "1.1.2"` in metadata
- [ ] **FR-002-b** `BuildOscalAssessmentResults()` includes `reviewed-controls.control-selections` array
- [ ] **FR-002-c** `BuildOscalAssessmentResults()` uses `target.status.state` object format (not string)
- [ ] **FR-002-d** `BuildOscalAssessmentResults()` includes `import-ap` reference at root level
- [ ] **FR-003** Both upgraded builders pass OSCAL 1.1.2 schema validation via `OscalSchemaValidationService`
- [ ] All Feature 041 tests pass after upgrade (regression guard)

### System Identifier Completeness (US5)

- [ ] **FR-007-a** eMASS Excel exports include `DitprId` in the correct eMASS Excel column
- [ ] **FR-007-b** eMASS Excel exports include `EmassId` in the correct eMASS Excel column
- [ ] **FR-007-c** OSCAL POA&M export includes `metadata.system-id` with DitprId entry
- [ ] **FR-007-d** OSCAL POA&M export includes `metadata.system-id` with EmassId entry
- [ ] **FR-007-e** OSCAL Assessment Results export includes `metadata.system-id` with both identifiers

### Export Readiness Check (US2)

- [ ] **FR-004-a** Readiness check validates `DitprId` presence (Blocking)
- [ ] **FR-004-b** Readiness check validates `EmassId` presence (Blocking)
- [ ] **FR-004-c** Readiness check validates CIA categorization populated (Blocking)
- [ ] **FR-004-d** Readiness check validates at least one Approved SSP section (Advisory)
- [ ] **FR-004-e** Readiness check validates all POA&M items have scheduled completion dates (Advisory)
- [ ] **FR-005** Response classifies each gap as `Blocking` or `Advisory`
- [ ] **FR-006** `DitprId` and `EmassId` gaps are always `Blocking` (never Advisory)

### Round-Trip Sync (US3)

- [ ] **FR-008** `EmassRoundTripSyncService` uses `EmassImportParser` for parsing (no parser modifications)
- [ ] **FR-009-a** Each changed field produces exactly one `EmassConflict` record
- [ ] **FR-009-b** Conflict records include `FieldName`, `SpinValue`, `EmassValue`, `ConflictStatus = Unresolved`
- [ ] **FR-009-c** Conflict records include `DetectedAt` timestamp and `SyncBatchId`
- [ ] **FR-010** SPIN entity tables are NOT modified during a sync run
- [ ] **FR-011-a** Resolving with `AcceptEmass` updates SPIN data with `EmassValue`
- [ ] **FR-011-b** Resolving with `KeepSpin` does NOT modify SPIN data
- [ ] **FR-011-c** Resolving with `Deferred` does NOT modify SPIN data
- [ ] **FR-012** Second sync with unresolved prior conflicts returns 409 (or warning requiring acknowledgment)
- [ ] **FR-013** Identical fields (after normalization) produce zero conflict records

### Workflow Status (US4)

- [ ] **FR-014-a** Status response includes `LastExportedAt` (nullable)
- [ ] **FR-014-b** Status response includes `LastSyncedAt` (nullable)
- [ ] **FR-014-c** Status response includes `UnresolvedConflictCount`
- [ ] **FR-014-d** Status response includes `ExportSummary` with per-category counts
- [ ] **FR-014-e** Status response includes `ReadinessStatus` summary (IsReady + gap counts)
- [ ] **FR-015** Status endpoint accessible to ISSO, ISSM, AO roles
- [ ] **FR-016-a** System never exported → `LastExportedAt = null`
- [ ] **FR-016-b** System never exported → `OverallStatus = NeverExported`

### MCP Tools

- [ ] **FR-017-a** `emass_get_workflow_status` accepts `system_id` parameter
- [ ] **FR-017-b** `emass_get_workflow_status` returns markdown-formatted status table
- [ ] **FR-017-c** `emass_get_workflow_status` works from dashboard chat, Teams, and VS Code
- [ ] **FR-018-a** `emass_check_export_readiness` accepts `system_id` parameter
- [ ] **FR-018-b** `emass_check_export_readiness` returns formatted readiness card with gaps
- [ ] **FR-018-c** `emass_check_export_readiness` highlights Blocking gaps in bold

### Access Control

- [ ] **FR-019-a** Status endpoint: ISSO ✓, ISSM ✓, AO ✓, other roles → 403
- [ ] **FR-019-b** Readiness endpoint: ISSO ✓, ISSM ✓, AO ✓, other roles → 403
- [ ] **FR-019-c** Sync upload: ISSO ✓, ISSM ✓, AO → 403, other roles → 403
- [ ] **FR-019-d** Conflict list: ISSO ✓, ISSM ✓, AO ✓, other roles → 403
- [ ] **FR-020** Conflict resolution (AcceptEmass/KeepSpin): ISSO ✓, ISSM ✓, AO → 403

---

## Non-Functional Requirements

- [ ] **NFR-001** Sync diff for 400 controls + 150 POA&M items completes < 30 seconds
- [ ] **NFR-002** Status endpoint responds < 500 ms
- [ ] **NFR-003** All new endpoints use `{ data, meta, errors }` response envelope
- [ ] **NFR-004** Resolved conflict records retained for 90 days
- [ ] **NFR-005** Feature 041 test suite passes without regressions

---

## Gherkin Acceptance Scenarios

### Scenario 1: OSCAL version in POA&M export

```gherkin
Given a system with active POA&M items
When I export the OSCAL POA&M via the MCP tool or API
Then the JSON contains "oscal-version": "1.1.2" in metadata
And the poam-items array uses "related-findings" not "related-observations"
And the output passes OSCAL 1.1.2 schema validation
```

### Scenario 2: Blocking readiness gap for missing DitprId

```gherkin
Given a system with DitprId not set
When the ISSO calls emass_check_export_readiness
Then IsReady is false
And the gaps array contains an entry for DitprId with severity Blocking
And the entry includes a fixUrl pointing to the system settings page
```

### Scenario 3: Round-trip sync creates conflicts without modifying SPIN

```gherkin
Given a system with SystemName "Alpha" in SPIN
And an eMASS Excel file where SystemName is "Alpha Production"
When the ISSO uploads the Excel to POST /api/systems/{id}/emass/sync
Then an EmassConflict record is created with FieldName="SystemInfo.SystemName"
  SpinValue="Alpha", EmassValue="Alpha Production", ConflictStatus="Unresolved"
And the RegisteredSystem.SystemName in the database remains "Alpha"
```

### Scenario 4: Resolving a conflict with AcceptEmass updates SPIN

```gherkin
Given an unresolved EmassConflict for SystemInfo.SystemName
  with SpinValue="Alpha" and EmassValue="Alpha Production"
When the ISSO calls PUT /api/systems/{id}/emass/conflicts/{conflictId}
  with resolution="AcceptEmass"
Then ConflictStatus is updated to "AcceptEmass"
And RegisteredSystem.SystemName is updated to "Alpha Production"
And ResolvedAt and ResolvedBy are populated
```

### Scenario 5: KeepSpin resolution leaves SPIN unchanged

```gherkin
Given the same unresolved conflict as Scenario 4
When the ISSO calls PUT with resolution="KeepSpin"
Then ConflictStatus is updated to "KeepSpin"
And RegisteredSystem.SystemName remains "Alpha"
```

### Scenario 6: Workflow status for a never-exported system

```gherkin
Given a system that has never had an eMASS export
When GET /api/systems/{id}/emass/status is called
Then OverallStatus is "NeverExported"
And LastExportedAt is null
And UnresolvedConflictCount is 0
```

### Scenario 7: MCP tool returns formatted status table

```gherkin
Given a system with export history and 2 unresolved conflicts
When the user asks "What is my eMASS status?" in the dashboard chat
Then the agent calls emass_get_workflow_status
And the response includes a markdown table with Controls, PoamItems, Artifacts rows
And shows "Unresolved Conflicts: 2 ⚠️"
```

---

## Definition of Done

- [ ] All `tasks.md` tasks completed and marked `[x]`
- [ ] All Functional Requirements above checked `[x]`
- [ ] All NFRs verified (performance tests run)
- [ ] All 7 Gherkin scenarios pass (unit or integration tests)
- [ ] `dotnet test` passes with no regressions (Feature 041 + Feature 071)
- [ ] PR reviewed and approved
- [ ] Issue #59 linked; all acceptance criteria in issue verified
- [ ] `docs/guides/emass-workflow.md` created and renders in MkDocs
- [ ] Agent tool catalog updated with 2 new tool entries

---

## DO NOT / Anti-Patterns

- **DO NOT** modify `EmassImportParser.cs` for Feature 071 — create adapters
- **DO NOT** modify `EmassImportService.cs` (onboarding path) — round-trip sync is a separate service
- **DO NOT** auto-merge SPIN data during sync — all merges are explicit ISSO choices
- **DO NOT** break Feature 041 package generation — OSCAL upgrade must be backward-compatible for package pipeline
- **DO NOT** store binary Excel content in `EmassConflict` records — store field values as strings only
- **DO NOT** create a `EmassExportLog` table in this feature — derive last-exported timestamps from `AuthorizationPackage` records (future increment if needed)
- **DO NOT** expose the conflict resolution endpoint to AO role — AO is read-only
