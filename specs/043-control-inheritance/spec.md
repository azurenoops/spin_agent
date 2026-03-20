# Feature Specification: Control Inheritance & Customer Responsibility Matrix

**Feature Branch**: `043-control-inheritance`  
**Created**: 2026-03-20  
**Status**: Implemented  
**Input**: User description: "Build a dashboard UI and REST API layer for managing control inheritance designations, CSP responsibility profiles, and CRM generation. Expose the existing backend service layer to the dashboard, add pre-built CSP inheritance profiles for Azure Government, CRM import, and cross-system inheritance."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — View & Manage Control Inheritance Designations (Priority: P1)

An Authorizing Official (AO) or security engineer opens the "Control Inheritance" page in the dashboard to see a summary of how many controls in the active baseline are Inherited, Shared, Customer-responsible, or still Undesignated. They filter the table by control family (e.g., AC — Access Control), review each control's current inheritance status, and update individual designations by selecting a type from a dropdown and specifying the provider.

**Why this priority**: This is the core capability — without the ability to view and set inheritance designations through the dashboard, every downstream feature (CRM generation, CSP profiles, import) has no interface to build on.

**Independent Test**: Navigate to the Control Inheritance page for a system with an active baseline. Verify the summary bar displays correct counts. Set a control's inheritance type to "Inherited" with provider "Azure Government FedRAMP High." Confirm the change persists on page reload and the summary counts update.

**Acceptance Scenarios**:

1. **Given** a system with an active baseline and no inheritance designations, **When** the user opens the Control Inheritance page, **Then** all controls appear as "Undesignated" and the summary bar shows 0 Inherited, 0 Shared, 0 Customer, and the full count as Undesignated.
2. **Given** a system with an active baseline, **When** the user selects a control and sets its inheritance type to "Shared" with provider "Azure Government FedRAMP High" and enters a customer responsibility description, **Then** the designation is saved, the table row updates, and the summary bar recalculates counts.
3. **Given** the Control Inheritance page, **When** the user filters by family "AC", **Then** only Access Control family controls are displayed.
4. **Given** no active baseline for the selected system, **When** the user navigates to Control Inheritance, **Then** a message "Select a baseline first" is displayed and no table is shown.

---

### User Story 2 — Bulk-Update Inheritance Designations (Priority: P1)

A security engineer needs to designate dozens of controls at once — for example, marking all physical security (PE) controls as "Inherited" from a cloud provider. They select multiple controls using checkboxes, choose an inheritance type and provider from a dropdown, and apply the change in one action.

**Why this priority**: Manually updating 300+ controls one at a time is impractical. Bulk operations are essential for the page to deliver real productivity value.

**Independent Test**: Select 10 controls using checkboxes. Use the bulk-update toolbar to set them all to "Inherited" with provider "Azure Government FedRAMP High." Confirm all 10 rows update and the summary bar reflects the change.

**Acceptance Scenarios**:

1. **Given** a displayed list of controls, **When** the user selects multiple controls via checkboxes, **Then** a bulk-action toolbar appears with inheritance type and provider dropdowns.
2. **Given** 15 controls selected, **When** the user chooses "Inherited" and provider "AWS GovCloud FedRAMP High" and clicks Apply, **Then** all 15 controls are updated and the summary bar recalculates.
3. **Given** the user selects controls that already have designations, **When** bulk-updating, **Then** existing designations are overwritten with the new values.

---

### User Story 3 — Generate and Export Customer Responsibility Matrix (Priority: P2)

An AO or compliance analyst needs a formal CRM document showing, for each control family, which controls are Inherited, Shared, or Customer-responsible, along with customer responsibility descriptions for Shared controls. They click "Generate CRM," review the family-by-family breakdown on screen, and export it as CSV or Excel for inclusion in authorization packages.

**Why this priority**: CRM generation is the primary deliverable that justifies tracking inheritance data. It's a required artifact for many authorization packages.

**Independent Test**: With inheritance designations set for at least 20 controls across multiple families, click "Generate CRM." Verify a family-grouped table appears with correct counts and percentages. Export as CSV and verify the file opens correctly with all data present.

**Acceptance Scenarios**:

1. **Given** a system with inheritance designations set for all controls, **When** the user clicks "Generate CRM," **Then** a CRM view is displayed grouped by control family showing control ID, inheritance type, provider, and customer responsibility text.
2. **Given** the CRM view is displayed, **When** the user clicks "Export CSV," **Then** a format selector offers Custom, FedRAMP, or eMASS layout; the downloaded CSV uses the selected format's column structure.
3. **Given** the CRM view is displayed, **When** the user clicks "Export Excel," **Then** a format selector offers Custom, FedRAMP, or eMASS layout; the downloaded Excel uses the selected format's column structure with headers and column widths.
5. **Given** the user selects FedRAMP format, **When** the export completes, **Then** the file structure matches the FedRAMP CRM template layout.
6. **Given** the user selects eMASS format, **When** the export completes, **Then** the file structure is compatible with eMASS CRM import.
4. **Given** a system with zero inheritance designations, **When** the user clicks "Generate CRM," **Then** the CRM shows all controls as "Undesignated" with an informational banner suggesting they set designations first.

---

### User Story 4 — Apply a Pre-Built CSP Inheritance Profile (Priority: P2)

A security engineer is onboarding a new system hosted on Azure Government. Instead of manually classifying 325 controls, they click "Apply CSP Profile," select "Azure Government (FedRAMP High)," preview a summary of how many controls will be set to Inherited/Shared/Customer, and confirm the application. All designations are applied in bulk with pre-populated customer responsibility descriptions for Shared controls.

**Why this priority**: Pre-built profiles eliminate the single largest manual effort in control inheritance — classifying hundreds of controls based on a CSP's published CRM. This directly enables the system to be useful "out of the box."

**Independent Test**: On a system with no existing designations, apply the Azure Government FedRAMP High profile. Verify that all applicable controls receive designations, the summary bar updates, and Shared controls have customer responsibility text pre-filled.

**Acceptance Scenarios**:

1. **Given** a system with an active baseline and no designations, **When** the user clicks "Apply CSP Profile" and selects "Azure Government (FedRAMP High)," **Then** a preview dialog shows the count of controls that will be set to Inherited, Shared, and Customer.
2. **Given** the preview dialog, **When** the user confirms, **Then** all controls are designated per the profile, the table updates, and the summary bar shows the new counts.
3. **Given** a system with some existing designations, **When** the user applies a CSP profile, **Then** a conflict resolution dialog offers "Skip existing" or "Overwrite all" options.
4. **Given** the user chooses "Skip existing," **When** the profile is applied, **Then** only Undesignated controls receive new designations while existing ones are preserved.
5. **Given** the user chooses "Overwrite all," **When** the profile is applied, **Then** all controls are re-designated per the profile regardless of prior values.

---

### User Story 5 — Import a CRM Spreadsheet (Priority: P3)

A security engineer has a CRM spreadsheet from a CSP or another system. They upload the file (CSV or Excel), map columns to the expected fields (Control ID, Responsibility Type, Provider, Customer Responsibility Description), preview the mapped data, and apply it to set inheritance designations in bulk.

**Why this priority**: Importing external CRM data supports organizations that already have CRM artifacts and need to digitize them, or that use CSP-published CRMs not yet available as built-in profiles.

**Independent Test**: Upload a CSV file with 50 control rows. Map columns in the preview dialog. Confirm designations are applied and match the uploaded data.

**Acceptance Scenarios**:

1. **Given** the Control Inheritance page, **When** the user clicks "Import CRM" and uploads a CSV file, **Then** a column-mapping dialog appears showing detected columns and expected fields.
2. **Given** the column-mapping dialog, **When** the user maps columns and clicks Preview, **Then** a preview table shows how the import will be applied, with row count and designation breakdown.
3. **Given** the preview includes controls that already have designations, **When** the user confirms with "Skip existing," **Then** only new designations are applied.
4. **Given** the preview includes controls that already have designations, **When** the user confirms with "Overwrite," **Then** all designations from the import replace existing values.
5. **Given** the uploaded file has unrecognizable control IDs, **When** previewing, **Then** those rows are flagged as "Not found in baseline" and excluded from import.

---

### User Story 6 — Cross-Portfolio Inheritance (Priority: P4)

A portfolio manager with multiple systems wants System B to inherit controls from System A, which has an existing ATO. They navigate to System B's Control Inheritance page, choose "Inherit from System," select System A, and the system auto-pulls applicable narrative text. If System A later loses its ATO, an impact analysis shows which controls in System B (and other dependent systems) are affected.

**Why this priority**: Cross-system inheritance is a valuable capability for organizations managing portfolios of systems but requires schema changes and dependency tracking, making it a future-phase feature.

**Independent Test**: On System B's inheritance page, link controls to System A as provider. Verify narrative text is pulled from System A. Run impact analysis showing System B depends on System A.

**Acceptance Scenarios**:

1. **Given** System B's Control Inheritance page, **When** the user selects "Inherit from System" for a control and chooses System A, **Then** the providing system is recorded and narrative text from System A's control implementation is shown.
2. **Given** System A is the provider for controls in Systems B and C, **When** an administrator runs impact analysis on System A, **Then** a report shows all controls in Systems B and C that depend on System A's controls.

---

### User Story 7 — Categorization & Baseline Management Page (Priority: P1)

A security engineer or AO navigates to the "Categorization" page (route: `/baseline`) to review the current system categorization and baseline. The page shows the baseline level (Low/Moderate/High), overlay status, total control count, family breakdown, and tailoring history. If no baseline has been configured, the page shows a clear call-to-action to select one. The page also provides a "Recategorize" dialog allowing users to change the system's FIPS 199 categorization (Confidentiality, Integrity, Availability levels and information types). When the categorization level changes, the system automatically reselects the appropriate baseline, preserves and reapplies existing inheritance designations to matching controls in the new baseline, and auto-updates narrative statuses.

**Why this priority**: Multiple dashboard pages (Inheritance, Narratives, Gap Analysis, Documents) depend on a baseline being configured, but there is no dedicated place for users to view baseline details or initiate baseline selection from existing navigation. Combining categorization with baseline management provides a single entry point for the foundational system classification workflow.

**Independent Test**: Navigate to the Categorization page for a system without a baseline. Verify the no-baseline state with "Select Baseline" CTA. Select a baseline. Verify summary cards show correct counts, family breakdown table lists all families with distribution bars. Open the Recategorize dialog, change the impact level — verify the baseline auto-reselects and a cascade banner reports the updated baseline, control count, and reapplied inheritances.

**Acceptance Scenarios**:

1. **Given** a system with no baseline, **When** the user opens the Categorization page, **Then** a no-baseline state is shown with a "Select Baseline" button.
2. **Given** a system with an active baseline, **When** the user opens the Categorization page, **Then** the page shows the baseline level badge, overlay badge, summary cards (Total, Inherited, Shared, Customer, Undesignated), metadata panel, and family breakdown table.
3. **Given** a system with an active baseline and tailoring history, **When** the user opens the Categorization page, **Then** a tailoring history table shows each tailored control with action, rationale, overlay status, by, and date.
4. **Given** the Categorization page, **When** the user clicks "Re-select Baseline" and confirms, **Then** the baseline is recomputed, existing inheritance designations are preserved and reapplied to matching controls in the new baseline, and the page refreshes with updated counts.
5. **Given** the family breakdown table, **When** the user types in the filter input, **Then** only matching families are displayed.
6. **Given** a system with a Moderate baseline, **When** the user opens the Recategorize dialog and changes the overall impact to High and saves, **Then** the system categorization is updated, the baseline is automatically reselected to High, a cascade banner shows the new baseline level, control count, and number of reapplied inheritance designations.
7. **Given** the Recategorize dialog for a non-provisional information type, **When** the user adjusts C/I/A levels, **Then** an adjustment justification is auto-populated and sent with the request.

---

### User Story 8 — Narrative Auto-Status from Inheritance Designations (Priority: P1)

When inheritance designations are applied to controls — whether via manual edit, bulk update, CSP profile application, or CRM import — the system automatically updates the corresponding narrative implementation statuses. Controls designated as "Inherited" have their narrative status set to "Implemented" (the provider handles the control entirely). Controls designated as "Shared" have their narrative status set to "Partially Implemented" (the customer has residual responsibilities). This eliminates the need for users to manually update narrative statuses after setting inheritance designations.

**Why this priority**: Inheritance designations and narrative statuses are tightly coupled — an Inherited control is by definition Implemented by the provider. Automating this status update prevents data inconsistency and saves significant manual effort across 300+ controls.

**Independent Test**: On a system with a baseline and narrative records, set 5 controls to "Inherited" and 3 to "Shared" via the Control Inheritance page. Verify that the corresponding narrative statuses are automatically updated to Implemented and Partially Implemented respectively. Check that a blue banner reports the number of auto-updated narratives.

**Acceptance Scenarios**:

1. **Given** a system with a baseline and narratives, **When** the user sets a control's inheritance to "Inherited" via the Control Inheritance page, **Then** the corresponding narrative's implementation status is automatically set to "Implemented" and the response includes the auto-updated count.
2. **Given** a system with a baseline and narratives, **When** the user sets a control's inheritance to "Shared," **Then** the corresponding narrative's implementation status is automatically set to "Partially Implemented."
3. **Given** the Control Inheritance page, **When** inheritance designations are applied (manual, bulk, or profile) and narratives are auto-updated, **Then** a dismissable blue banner reports the count of auto-updated narratives.
4. **Given** a CSP profile is applied to a system, **When** the profile designates controls as Inherited or Shared, **Then** narrative statuses are auto-updated for all affected controls and the count is included in the apply response.
5. **Given** a CRM import is applied, **When** designations include Inherited or Shared controls, **Then** narrative statuses are auto-updated and the import result dialog shows the auto-updated count.

---

### User Story 9 — Categorization-to-Baseline Cascade (Priority: P1)

When a user changes the system categorization (e.g., from Moderate to High), the system detects that the baseline level has changed and automatically reselects the appropriate baseline. During reselection, existing inheritance designations are snapshotted, the old baseline is replaced with the new one, and inheritance designations are reapplied to controls that exist in both the old and new baselines. Narrative statuses are also auto-updated for the reapplied inheritance designations. The user sees a cascade banner reporting what happened.

**Why this priority**: Without automatic cascade, changing categorization would silently leave the old baseline in place, creating a mismatch between the system's stated impact level and its actual control baseline. Users would need to manually reselect the baseline and re-do all inheritance work.

**Independent Test**: On a system with a Moderate baseline and 50 inheritance designations, recategorize to High. Verify the baseline changes to High, inheritance designations are reapplied to matching controls, and the cascade banner reports the new level, control count, and reapplied inheritance count.

**Acceptance Scenarios**:

1. **Given** a system with a Moderate baseline and inheritance designations, **When** the user changes the categorization to High, **Then** the baseline is automatically reselected to High.
2. **Given** the baseline is reselected during categorization change, **When** the old baseline had inheritance designations, **Then** designations for controls that exist in both old and new baselines are automatically reapplied.
3. **Given** the cascade completes, **When** the Categorization page refreshes, **Then** a blue dismissable banner shows the new baseline level, total controls, and number of reapplied inheritances.
4. **Given** the categorization changes but the overall impact level stays the same, **When** the save completes, **Then** no baseline reselection occurs and no cascade banner is shown.
5. **Given** inheritance designations are reapplied during cascade, **When** Inherited or Shared controls are among them, **Then** narrative statuses are auto-updated (Inherited→Implemented, Shared→PartiallyImplemented).

---

### Edge Cases

- What happens when the user applies a CSP profile for a baseline that has fewer controls than the profile covers? Only matching controls are designated; unmatched profile entries are silently skipped.
- What happens when a CRM import file contains duplicate control IDs? The last row for each control ID in the file wins; duplicates are noted in the import summary.
- What happens when two users simultaneously update the same control's inheritance? The last write wins with standard optimistic concurrency; the second user sees updated data on their next refresh.
- What happens when the user reselects a baseline after inheritance designations exist? Designations are snapshotted before the old baseline is deleted, then reapplied to matching controls in the new baseline. Designations for controls that no longer exist in the new baseline are lost.
- What happens when a CRM export is requested for a system with partial designations? The export includes all controls with their current status, including "Undesignated" entries.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST expose a REST endpoint to list all inheritance designations for a system, filterable by control family and inheritance type.
- **FR-002**: System MUST expose a REST endpoint to set inheritance designations for one or more controls in a single request.
- **FR-003**: System MUST expose a REST endpoint to generate and return a Customer Responsibility Matrix for a system.
- **FR-004**: *(Deferred — baseline tailoring is out of scope for this feature; see `TailorBaselineAsync` for future exposure)* System MUST expose a REST endpoint to add or remove controls from a system's baseline with rationale.
- **FR-005**: System MUST expose a REST endpoint (`GET /systems/{systemId}/baseline`) to retrieve baseline details including level, overlay, control counts (total, inherited, shared, customer, tailored in/out), family breakdown with control counts, tailoring history, and control IDs.
- **FR-006**: Dashboard MUST display a "Control Inheritance" page under the Compliance Posture navigation section.
- **FR-006a**: Dashboard MUST display a "Categorization" page (route: `baseline`) as the first item under the Compliance Posture navigation section, combining categorization management with baseline details (level, summary cards, family breakdown, tailoring history) when a baseline exists, or a "Select Baseline" call-to-action when no baseline is configured. The page MUST include a RecategorizeDialog for changing system categorization.
- **FR-006b**: The Control Inheritance page MUST appear before the Narratives page in the Compliance Posture navigation group.
- **FR-007**: The Control Inheritance page MUST show a summary bar with total controls, inherited count, shared count, customer count, undesignated count (derived as TotalControls − Inherited − Shared − Customer), and inheritance percentage.
- **FR-008**: The Control Inheritance page MUST display a table of controls with columns: Control ID, Family, Inheritance Type, Provider, Customer Responsibility, Set By, and Set At.
- **FR-009**: The table MUST support filtering by control family and inheritance type.
- **FR-010**: Users MUST be able to update a single control's inheritance type, provider, and customer responsibility text inline.
- **FR-011**: Users MUST be able to select multiple controls and bulk-update their inheritance type and provider.
- **FR-012**: The provider field MUST offer a dropdown of known providers (e.g., Azure Government FedRAMP High, AWS GovCloud FedRAMP High) in addition to free-text entry.
- **FR-013**: The page MUST display a "Select a baseline first" message when the active system has no baseline.
- **FR-014**: Users MUST be able to generate a CRM view grouped by control family showing inheritance type, provider, and customer responsibility for each control.
- **FR-015**: Users MUST be able to export the CRM as CSV or Excel, with a format selector offering three layout options: Custom (app-native columns), FedRAMP CRM template, and eMASS-compatible format. The selected format determines column structure and grouping in the exported file.
- **FR-017**: System MUST include a built-in CSP inheritance profile for Azure Government FedRAMP High covering all applicable NIST 800-53 Rev 5 controls, loaded from a JSON config file at startup.
- **FR-029**: System MUST support admin-extensible CSP profiles — administrators MUST be able to add or modify CSP inheritance profiles by editing JSON config files without code changes. New profiles become available after application restart.
- **FR-018**: Users MUST be able to apply a CSP inheritance profile with a preview of designation counts before confirmation.
- **FR-019**: When applying a CSP profile to a system with existing designations, the system MUST offer conflict resolution options: Skip existing or Overwrite all.
- **FR-020**: Users MUST be able to import a CRM from a CSV or Excel file.
- **FR-021**: The CRM import MUST support column mapping for Control ID, Responsibility Type, Provider, and Customer Responsibility Description.
- **FR-022**: The CRM import MUST preview mapped data before applying.
- **FR-023**: The CRM import MUST flag unrecognizable control IDs and exclude them from import.
- **FR-024**: System MUST support linking a control's inheritance to another registered system as the provider (cross-portfolio, future phase).
- **FR-025**: System MUST support impact analysis showing which controls in dependent systems are affected when a providing system's status changes (future phase).
- **FR-026**: Inheritance designation write operations (set, bulk-update, profile apply, CRM import) MUST be restricted to users with AO or Security Engineer roles; all system members MUST have read access to view designations and generated CRMs.
- **FR-028**: Every inheritance designation change (manual edit, bulk-update, profile apply, CRM import) MUST create an immutable audit entry recording the actor, previous value, new value, timestamp, and change source. The full audit history MUST be viewable per control.
- **FR-030**: When inheritance designations are applied (via manual edit, bulk update, CSP profile, or CRM import), the system MUST automatically update the corresponding narrative implementation statuses: Inherited controls → Implemented, Shared controls → Partially Implemented. The write response MUST include the count of auto-updated narratives.
- **FR-031**: When the system categorization changes and the resulting baseline level differs from the current baseline, the system MUST automatically reselect the baseline, preserve and reapply existing inheritance designations to matching controls in the new baseline, and auto-update narrative statuses for reapplied Inherited/Shared controls.
- **FR-032**: The categorization save response MUST include cascade information when a baseline reselection occurs: new baseline level, total control count, and number of reapplied inheritance designations.
- **FR-033**: The `SelectBaselineAsync` operation MUST snapshot existing inheritance designations before deleting the old baseline, then reapply them to matching controls in the new baseline after creation.
- **FR-034**: The `RegenerateNarrativeWithAiAsync` operation MUST fall back to deterministic `GenerateEnrichedNarrative` when AI capabilities are disabled, rather than failing silently.

### Key Entities

- **ControlInheritance**: A per-control designation recording the inheritance type (Inherited, Shared, Customer), the providing entity, customer responsibility text for shared controls, and current-state audit fields (SetBy, SetAt). Controls with no ControlInheritance record are implicitly Undesignated; no explicit "Undesignated" rows are stored. Full change history is tracked separately in InheritanceAuditEntry.
- **InheritanceAuditEntry**: An immutable audit log entry created on every change to a ControlInheritance record, capturing actor (user ID), previous value (inheritance type, provider, customer responsibility), new value, timestamp, and change source (manual, bulk-update, profile-apply, CRM-import). Entries are append-only and never modified or deleted.
- **ControlBaseline**: The aggregate baseline for a system, tracking counts of inherited, shared, and customer controls, with collections of inheritance designations and tailoring records.
- **ControlTailoring**: A record of controls added to or removed from a baseline, including the rationale and whether an overlay is required.
- **CSP Inheritance Profile**: Reference data mapping each control in a standard baseline to an inheritance type and (for shared controls) a customer responsibility description, based on a CSP's published CRM.
- **Customer Responsibility Matrix (CRM)**: A generated artifact aggregating all inheritance designations for a system, grouped by control family, with inheritance percentages and customer responsibility details.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can view all control inheritance designations for a 325-control baseline in under 2 seconds from page load.
- **SC-002**: Users can bulk-update 50 controls' inheritance designations in a single action completing in under 3 seconds.
- **SC-003**: Users can generate a complete Customer Responsibility Matrix in under 3 seconds for a High baseline.
- **SC-004**: Applying a pre-built CSP profile designates all applicable controls with zero manual data entry per control.
- **SC-005**: A user applying a CSP profile to a clean baseline can complete all inheritance designations in under 5 minutes with zero per-control manual data entry.
- **SC-006**: CRM export produces a file that accurately reflects 100% of the designations shown in the dashboard.
- **SC-007**: CRM import correctly maps and applies at least 95% of rows from a well-formatted spreadsheet without manual correction.
- **SC-008**: The Control Inheritance page requires no baseline schema changes for core functionality (Phases 1-2).

## Assumptions

- The existing `IBaselineService` methods (`SetInheritanceAsync`, `GenerateCrmAsync`, `TailorBaselineAsync`, `GetBaselineAsync`) are stable, tested, and ready to be called from new REST endpoints without modification.
- The `ControlInheritance` model's `InheritanceType` enum already includes values for Inherited, Shared, and Customer.
- Dashboard navigation and page routing follow the existing patterns established by other compliance pages (e.g., Gap Analysis).
- CSP inheritance profiles (including Azure Government FedRAMP High) are maintained as JSON config files that ship as seed data. Administrators can add new profiles or modify existing ones by editing config files; no code changes are required.
- CSP profile data accuracy is based on Microsoft's publicly available Azure Government CRM documentation at the time of implementation.
- Excel export will use a standard library already available or easily added to the project.
- Cross-portfolio inheritance (Phase 4) will require a database migration to add a nullable foreign key column to the `ControlInheritance` table.
- Performance targets assume indexed queries on `ControlInheritance.ControlBaselineId`.

## Clarifications

### Session 2026-03-20

- Q: What authorization model governs inheritance designation writes? → A: Role-gated writes — AO and Security Engineer roles can modify designations; all system members can view.
- Q: How is "Undesignated" represented in the data model? → A: Implicit — absence of a ControlInheritance record means Undesignated. UndesignatedCount is derived as TotalControls − InheritedCount − SharedCount − CustomerCount. No explicit "Undesignated" rows are stored.
- Q: What format should CRM exports use? → A: Configurable — allow the user to choose between Custom (app-native), FedRAMP CRM template, or eMASS-compatible format at export time.
- Q: What level of audit trail is required for inheritance designation changes? → A: Full audit history — every change records actor, previous value, new value, and timestamp. The complete change log is retained, not just the last modification.
- Q: How extensible should CSP inheritance profiles be? → A: Built-in profiles ship as seed data with admin-extensible JSON config files — administrators can add or modify CSP profiles without code changes.

## Phasing

- **Phase 1**: REST API endpoints + dashboard page for viewing and managing inheritance designations, bulk update, and CRM generation/export. This phase delivers a usable end-to-end workflow.
- **Phase 2**: Embedded CSP inheritance profiles (Azure Government FedRAMP High) with apply-and-preview workflow and conflict resolution.
- **Phase 3**: CRM import from CSV/Excel with column mapping, preview, and conflict resolution.
- **Phase 4** *(future)*: Cross-portfolio inheritance with system-to-system provider linking, narrative auto-pull, and impact analysis. Requires schema migration.
- **Phase 5** *(implemented)*: Post-implementation enhancements — narrative auto-status from inheritance designations, categorization-to-baseline cascade with inheritance preservation, combined Categorization & Baseline page with RecategorizeDialog, nav reorder (Control Inheritance before Narratives), terminology alignment (resources→components), regenerate narrative fallback, and UI layout fixes.
