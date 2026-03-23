# Feature Specification: Org-Level Control Inheritance

**Feature Branch**: `044-org-control-inheritance`  
**Created**: 2026-03-21  
**Status**: Draft  
**Input**: User description: "Org-level Control Inheritance — Move the default control inheritance designations (Inherited, Shared, Customer) from per-system to org-level, derived automatically from org-wide capabilities and their component mappings. When a capability like 'Certificate-Based Authentication' is implemented by Azure Key Vault and maps to controls SC-12, SC-13, those controls should be marked 'Inherited' at the org level by default. Systems inherit the org-level defaults when they select a baseline, and ISSMs can override individual controls at the system level (e.g., changing 'Inherited' to 'Shared' with a customer responsibility note). CRM generation uses effective inheritance (org defaults merged with system overrides). CSP profile application becomes optional since the org-level derivation replaces the manual 'apply profile' step. The existing per-system ControlInheritance entity gains an IsOrgDefault flag or the org defaults are stored in a new OrgInheritanceDefault table. --> Org defaults are stored in a dedicated `OrgInheritanceDefault` table, separate from the per-system `ControlInheritance` table. The per-system table gains a `DesignationSource` enum and nullable `OrgInheritanceDefaultId` FK. The Capabilities page becomes the single source of truth for what the CSP provides. The dashboard Control Inheritance page shows which designations came from org defaults vs system overrides, with a visual indicator. Audit trail tracks whether a change was org-derived, profile-applied, or manually overridden. This eliminates the repetitive step of applying CSP profiles to every new system and ensures consistency across the portfolio."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Automatic Org-Level Inheritance Derivation from Capabilities (Priority: P1)

A portfolio security architect manages org-wide capabilities on the Capabilities page (e.g., "Certificate-Based Authentication" implemented by Azure Key Vault, mapped to controls SC-12 and SC-13). When capabilities are linked to components and mapped to NIST controls, the system automatically derives org-level default inheritance designations. Controls where an org-wide capability provides full coverage are defaulted to "Inherited"; controls where the capability provides partial coverage are defaulted to "Shared." The Capabilities page becomes the single source of truth for what the CSP provides across the organization.

**Why this priority**: This is the foundational capability — every downstream feature (system-level inheritance, overrides, CRM generation) depends on org-level defaults existing. Without automatic derivation from capabilities, the feature has no data to propagate.

**Independent Test**: Create an org-wide capability "Certificate-Based Authentication" implemented by Azure Key Vault. Map it to controls SC-12 and SC-13 with role "Primary." Verify that org-level inheritance defaults are generated showing SC-12 and SC-13 as "Inherited" with provider derived from the capability's component.

**Acceptance Scenarios**:

1. **Given** an org-wide capability with ImplementationStatus = "Implemented" mapped to control AC-2 with role "Primary," **When** org-level defaults are derived, **Then** AC-2 appears as "Inherited" in the org defaults with the provider set to the capability's linked component name.
2. **Given** an org-wide capability mapped to control AC-2 with role "Shared," **When** org-level defaults are derived, **Then** AC-2 appears as "Shared" in the org defaults.
3. **Given** two org-wide capabilities both mapped to the same control AC-6 (one as "Primary," one as "Supporting"), **When** org-level defaults are derived, **Then** AC-6 is designated "Inherited" (the primary mapping takes precedence) and both providers are listed.
4. **Given** an org-wide capability with ImplementationStatus = "Planned" (not yet implemented), **When** org-level defaults are derived, **Then** controls mapped to that capability are not included in org defaults (only implemented capabilities generate defaults).
5. **Given** an org-wide capability mapped to control RA-5 with role "Supporting," **When** org-level defaults are derived, **Then** RA-5 appears as "Inherited" in the org defaults (Supporting maps to Inherited, same as Primary).
6. **Given** a capability mapped to controls SC-12 and SC-13 is deleted, **When** org-level defaults are re-derived, **Then** SC-12 and SC-13 no longer have org-level defaults (unless covered by another capability).

---

### User Story 2 — System Inherits Org Defaults on Baseline Selection (Priority: P1)

An ISSM creates a new system and selects a NIST baseline (e.g., Moderate). Upon baseline selection, the system automatically receives all applicable org-level inheritance defaults for the controls in that baseline. The ISSM immediately sees which controls are "Inherited," "Shared," or "Customer" based on what org-wide capabilities cover, without needing to manually apply a CSP profile or classify each control.

**Why this priority**: This is the core user-facing value — new systems get pre-populated inheritance designations automatically, eliminating the repetitive "apply CSP profile" step for every system.

**Independent Test**: Set up org-level defaults for 50 controls. Create a new system, select a Moderate baseline. Verify the system's Control Inheritance page shows designations matching org defaults for all 50 controls that are in the Moderate baseline, with the remaining controls shown as "Undesignated."

**Acceptance Scenarios**:

1. **Given** org-level defaults exist for 100 controls and a system selects a Moderate baseline containing 325 controls, **When** the baseline is applied, **Then** the 100 controls with org defaults receive those designations, and the remaining 225 controls are "Undesignated."
2. **Given** a newly created system with no prior designations, **When** the ISSM selects a baseline, **Then** the Control Inheritance page shows org-derived designations with a visual indicator (e.g., badge or icon) distinguishing them from manual designations.
3. **Given** a system that already has some manual designations, **When** the ISSM re-selects or changes the baseline, **Then** existing manual overrides are preserved and only controls without overrides receive org defaults.
4. **Given** org-level defaults do not exist for a particular control in the baseline, **When** the system is created, **Then** that control shows as "Undesignated" and is available for manual classification or CSP profile application.

---

### User Story 3 — ISSM Overrides Org Defaults at the System Level (Priority: P1)

An ISSM reviewing the Control Inheritance page for their system sees that control AC-2 is marked "Inherited" based on the org default (from an org-wide identity management capability). However, their specific system has additional customer-side access control procedures, so the ISSM overrides AC-2 to "Shared" and adds a customer responsibility description. The override is clearly indicated in the UI as a system-level change diverging from the org default.

**Why this priority**: Without the ability to override, org defaults become inflexible mandates. System-specific context always requires adjustments — this is a core workflow for every ISSM.

**Independent Test**: Navigate to a system where AC-2 is "Inherited" from org defaults. Change it to "Shared," add a customer responsibility note. Verify the designation updates, is visually marked as a system override, and persists on reload.

**Acceptance Scenarios**:

1. **Given** a control with an org-derived "Inherited" designation, **When** the ISSM changes it to "Shared" and adds a customer responsibility description, **Then** the control is saved as a system-level override with the new designation.
2. **Given** a control with a system-level override, **When** viewing the Control Inheritance page, **Then** the control row displays a visual indicator (e.g., "Override" badge or different icon) showing it diverges from the org default.
3. **Given** a control with a system-level override, **When** the ISSM clicks "Revert to Org Default," **Then** the override is removed and the control reverts to the current org-level designation.
4. **Given** a control with an org-derived "Shared" designation, **When** the ISSM overrides it to "Customer Responsibility," **Then** the provider field is cleared and the customer responsibility description is required.
5. **Given** multiple controls selected via checkboxes, **When** the ISSM uses bulk override, **Then** all selected controls are overridden to the chosen designation while preserving the ability to revert each individually.

---

### User Story 4 — Dashboard Shows Org Defaults vs. System Overrides (Priority: P2)

A compliance analyst opens the Control Inheritance page for a system and sees a clear visual distinction between controls that use org-level defaults and controls that have been overridden at the system level. The existing summary cards (Total Controls, Inherited, Shared, Customer Responsibility, Undesignated, Inheritance %) remain, and a new secondary summary bar is added below showing designation source: "85 Org Default / 12 System Override / 228 Undesignated." Filters let the analyst view only overrides, only org defaults, or all designations. Hovering over an org-default control shows the source capability and component that generated the default.

**Why this priority**: Transparency into where designations come from is essential for audit readiness and helps ISSMs make informed decisions about which defaults to accept or override.

**Independent Test**: On a system with a mix of org defaults and overrides, verify the summary bar counts are accurate. Filter to "Overrides Only" and confirm only override rows appear. Hover over an org-default control and verify the tooltip shows the source capability name and component.

**Acceptance Scenarios**:

1. **Given** a system with org-derived and overridden designations, **When** the Control Inheritance page loads, **Then** a summary bar displays counts for Org Default, System Override, and Undesignated.
2. **Given** the Control Inheritance page, **When** the analyst filters by "Org Defaults Only," **Then** only controls with org-derived designations are shown.
3. **Given** the Control Inheritance page, **When** the analyst filters by "System Overrides Only," **Then** only controls with system-level overrides are shown.
4. **Given** an org-default control, **When** the analyst hovers or clicks an info icon, **Then** a detail panel or tooltip shows the source capability name, linked component, and the mapping role that generated the default.
5. **Given** an overridden control, **When** the analyst hovers or clicks the override indicator, **Then** the original org-level default is shown alongside the current system-override value.

---

### User Story 5 — CRM Generation Uses Effective Inheritance (Priority: P2)

A security analyst generates a Customer Responsibility Matrix (CRM) for their system. The CRM reflects "effective inheritance" — the org-level default merged with any system-level overrides. If a control was overridden at the system level, the CRM uses the override; otherwise it uses the org default. The generated CRM document includes a column indicating whether each designation is org-derived or system-overridden for traceability.

**Why this priority**: The CRM is a key authorization artifact. It must reflect the actual effective state of each control, incorporating both default and override information for accurate compliance documentation.

**Independent Test**: With a system that has 80 org-default controls and 15 overrides, generate a CRM. Verify the CRM shows the override values for the 15 overridden controls and org-default values for the remaining 80 controls. Confirm the "source" column correctly identifies each.

**Acceptance Scenarios**:

1. **Given** a system with org defaults and system overrides, **When** the analyst generates a CRM, **Then** overridden controls reflect the system-level designation and non-overridden controls reflect the org-level default.
2. **Given** the generated CRM, **When** exported to CSV or Excel, **Then** a "Designation Source" column indicates "Org Default" or "System Override" for each control.
3. **Given** no org defaults and no overrides for a control, **When** the CRM is generated, **Then** that control appears as "Undesignated" with an empty source column.

---

### User Story 6 — Org Defaults Update When Capabilities Change (Priority: P2)

The portfolio security architect adds a new org-wide capability or modifies an existing capability's control mappings. Org-level defaults are automatically re-derived. Systems that rely on org defaults (without system-level overrides) see the updated designations. Systems with overrides are unaffected. An org-wide change notification or log entry records the propagation.

**Why this priority**: Capabilities evolve — new tools are adopted, mappings are refined. Org defaults must stay synchronized with the current capability portfolio to remain trustworthy.

**Independent Test**: Add a new org-wide capability mapped to controls PE-2 and PE-3. Verify org defaults now include PE-2 and PE-3 as "Inherited." Check that a system using org defaults for those controls sees the updated designations. Check that a system that had overridden PE-2 still shows the override.

**Acceptance Scenarios**:

1. **Given** a new org-wide capability mapped to PE-2 and PE-3, **When** org defaults are re-derived, **Then** PE-2 and PE-3 appear as new org-level defaults.
2. **Given** an existing org default for SC-12 and a system using that default (no override), **When** the underlying capability mapping for SC-12 is removed, **Then** the org default for SC-12 is removed and the system's SC-12 reverts to "Undesignated."
3. **Given** a system with a manual override on SC-12, **When** the org default for SC-12 changes, **Then** the system's override is preserved unchanged.
4. **Given** an org-default change affects 10 systems, **When** the change propagates, **Then** an audit entry is created for each affected system recording the change source as "OrgPropagation."

---

### User Story 7 — Audit Trail Tracks Change Sources (Priority: P3)

An auditor reviews the inheritance audit log for a system and can see the source of each designation change: "OrgDerived" (automatically applied from org defaults), "ProfileApply" (from a manually applied CSP profile), or "Manual" (ISSM override). The audit log includes the previous value, new value, who or what triggered the change, and a timestamp.

**Why this priority**: Audit traceability is a compliance requirement for federal systems. Assessors need to verify that inheritance designations have a documented provenance.

**Independent Test**: Perform three changes on a system: receive an org default (automatic), apply a CSP profile (manual step), and override a control (ISSM action). Review the audit log and confirm all three entries are present with the correct source type.

**Acceptance Scenarios**:

1. **Given** a system receives org defaults on baseline selection, **When** the audit log is viewed, **Then** entries show source = "OrgDerived" for each org-default designation with the capability name as context.
2. **Given** an ISSM overrides a control at the system level, **When** the audit log is viewed, **Then** the entry shows source = "Manual," the previous value (org default), the new value, and the ISSM's identity.
3. **Given** an ISSM applies a CSP profile to supplement org defaults, **When** the audit log is viewed, **Then** entries show source = "ProfileApply" for controls affected by the profile.
4. **Given** org defaults change due to a capability update, **When** the audit log for affected systems is viewed, **Then** entries show source = "OrgDerived" with the updated values and a reference to the capability change.

---

### User Story 8 — CSP Profile Application Becomes Optional (Priority: P3)

An ISSM onboarding a new system notices that org defaults have already pre-populated most inheritance designations. The "Apply CSP Profile" action remains available but is presented as optional — a supplementary step for filling gaps or overriding org defaults for specific systems. A message on the Control Inheritance page indicates how many controls are already designated from org defaults and how many remain undesignated.

**Why this priority**: The CSP profile workflow from Feature 043 must continue to work for organizations that haven't fully populated org-wide capabilities. Making it optional (rather than removing it) ensures backward compatibility.

**Independent Test**: Create a system where org defaults cover 200 of 325 controls. Verify the "Apply CSP Profile" option is available but not required. Apply a profile and confirm it fills in some of the remaining 125 controls without overwriting the 200 org-default controls (using the "Skip existing" conflict resolution option).

**Acceptance Scenarios**:

1. **Given** a system with org defaults covering most controls, **When** the Control Inheritance page loads, **Then** a banner shows "200 of 325 controls have org-level defaults. Apply a CSP profile to fill remaining gaps (optional)."
2. **Given** the ISSM clicks "Apply CSP Profile," **When** the conflict resolution dialog appears, **Then** "Skip existing" is the default selection (preserving org defaults).
3. **Given** the ISSM chooses "Overwrite all," **When** the profile is applied, **Then** profile values replace org defaults and the overwritten controls are marked as "ProfileApply" source in the audit log.
4. **Given** an organization with no org-wide capabilities configured, **When** a new system is created, **Then** all controls are "Undesignated" and the "Apply CSP Profile" workflow works exactly as before (backward compatible).

---

### Edge Cases

- What happens when a capability maps to a control with both "Primary" and "Shared" roles from different capabilities? The system uses a precedence rule: Primary/Supporting → Inherited; if all mappings are Shared → Shared; if no implemented capabilities cover the control → no org default.
- What happens when multiple capabilities both map to the same control with role "Primary"? The designation stays "Inherited" and all contributing capabilities/components are merged into the provider list (no arbitrary winner).
- What happens when all capabilities covering a control are deleted? The org default for that control is removed. Systems using the org default revert to "Undesignated" for that control; systems with overrides are unaffected.
- What happens when a capability's ImplementationStatus changes from "Implemented" to "Planned"? The capability is excluded from org-default derivation. Affected org defaults are removed (same as deletion behavior).
- What happens when a system's baseline changes from Moderate to High, adding new controls? New controls receive applicable org defaults; existing designations (both org defaults and overrides) are preserved for controls that remain in the baseline.
- What happens when a control is removed from the baseline? The corresponding inheritance designation (org default or override) is removed. If the baseline is later expanded to re-include that control, it receives applicable org defaults via normal propagation (no archive/restore mechanism).
- What happens in a multi-boundary environment? Org defaults are derived from org-wide capabilities (RegisteredSystemId = null). Boundary-scoped capabilities do not contribute to org defaults but may generate boundary-level defaults in a future extension.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST automatically derive org-level inheritance defaults from org-wide capabilities and their control mappings. A control mapped by an implemented capability with role "Primary" or "Supporting" defaults to "Inherited"; role "Shared" defaults to "Shared."
- **FR-002**: System MUST propagate applicable org-level defaults to a system's control inheritance when a baseline is selected, setting designations only for controls that do not already have a system-level override.
- **FR-003**: System MUST allow ISSMs to override any org-level default at the system level by changing the inheritance type and adding a customer responsibility description. When the inheritance type is set to "Customer Responsibility," the `customerResponsibility` field MUST be required (non-empty validation).
- **FR-004**: System MUST allow ISSMs to revert a system-level override back to the current org default with a single action.
- **FR-005**: System MUST visually distinguish org-derived designations from system-level overrides on the Control Inheritance page using badges, icons, or color indicators.
- **FR-006**: System MUST display summary counts on the Control Inheritance page showing Org Default, System Override, and Undesignated totals.
- **FR-007**: System MUST support filtering the Control Inheritance table by designation source (Org Default, System Override, Undesignated).
- **FR-008**: System MUST re-derive org-level defaults when org-wide capabilities are added, modified, or deleted, and propagate changes to systems that have not overridden the affected controls.
- **FR-009**: System MUST generate CRM exports reflecting effective inheritance (org default merged with system overrides) and include a "Designation Source" column in exports.
- **FR-010**: System MUST record an audit trail entry for every inheritance change, including the change source (OrgDerived, OrgPropagation, ProfileApply, Manual, BulkUpdate, CrmImport). Note: `InheritanceChangeSource` (audit event source) is distinct from `DesignationSource` (current state on ControlInheritance). Bulk override operations use change source `BulkUpdate` while setting DesignationSource to `Manual` on the affected records.
- **FR-011**: System MUST retain the "Apply CSP Profile" workflow as an optional supplementary action, with "Skip existing" as the default conflict resolution mode.
- **FR-012**: System MUST show the originating capability and component for any org-derived designation (via tooltip, detail panel, or info icon).
- **FR-013**: System MUST support bulk override of multiple controls at the system level, including the ability to revert multiple overrides to org defaults.
- **FR-014**: System MUST use a precedence rule when multiple capabilities map to the same control: Primary/Supporting role takes precedence over Shared; only capabilities with ImplementationStatus = "Implemented" are considered. When multiple capabilities map with the same role, all contributing capabilities and components are merged into the provider list.
- **FR-015**: System MUST reorganize the Control Inheritance page action buttons to reflect org-level inheritance primacy, demoting "Apply CSP Profile" and promoting org-default status visibility (see UX Layout below).

### Control Inheritance Page — Action Button Layout

With org-level inheritance as the primary path, the three action buttons currently shown in the page header ("Apply CSP Profile", "Import CRM", "Generate CRM") are reorganized:

**Primary actions (always visible in header):**
- **Generate CRM** — Primary CTA (blue/indigo, right-aligned). Uses effective inheritance (org defaults + system overrides). Unchanged behavior.
- **Import CRM** — Secondary action (outlined button). Unchanged behavior.

**Demoted actions (moved to "More Actions" dropdown or secondary row):**
- **Apply CSP Profile** — Moved into a "More Actions" (⋯) dropdown menu or rendered as a text-link below the primary buttons. When org defaults cover >0 controls, the button label changes to "Apply CSP Profile (optional)" and a tooltip explains: "Org defaults already cover N controls. Use this to fill remaining gaps from a CSP profile." When no org defaults exist (0 capabilities configured), the button remains in its original prominent position for backward compatibility.

**New actions added:**
- **Revert Selected to Org Defaults** — Appears in the bulk action toolbar when one or more overridden controls are selected. Reverts selected overrides back to current org defaults.
- **View Org Defaults** — Link or button that opens a read-only view of the current org-level defaults (all controls, not filtered to this system's baseline), so ISSMs can see the full org picture before overriding.

### Key Entities

- **OrgInheritanceDefault**: Represents the org-level default inheritance designation for a specific control. Derived from org-wide capabilities and their control mappings. Key attributes: ControlId, InheritanceType, Provider (derived from capability's component), SourceCapabilityId, DerivedAt timestamp.
- **ControlInheritance (extended)**: The existing per-system inheritance record gains awareness of whether it was org-derived or manually set. Key new attributes: IsOrgDefault flag (or DesignationSource enum), OrgInheritanceDefaultId (nullable FK linking back to the org default it was derived from).
- **InheritanceAuditEntry (extended)**: The existing audit entry gains new change source values: "OrgDerived" and "OrgPropagation" to track automatic derivation and system-level propagation events.
- **SecurityCapability**: Existing entity — serves as the source of truth for org-level capabilities. Capabilities with ImplementationStatus = "Implemented" and org-wide scope (RegisteredSystemId = null on their control mappings) drive org defaults.
- **CapabilityControlMapping**: Existing entity — the mapping between capability and NIST control (with Role = Primary/Shared) determines whether the org default is "Inherited" or "Shared."

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: New systems receive org-level inheritance defaults automatically upon baseline selection, reducing time-to-first-designation from minutes of manual CSP profile application to zero manual steps.
- **SC-002**: 100% of org-derived designations are traceable to a specific capability and component through the UI and audit log.
- **SC-003**: ISSMs can override any org-level default in under 30 seconds, with visual confirmation of the override and one-click revert capability.
- **SC-004**: CRM exports accurately reflect effective inheritance (org defaults merged with system overrides) for 100% of designated controls.
- **SC-005**: Org-level defaults update synchronously during capability save; affected systems reflect changes on next page load without additional user action.
- **SC-006**: Portfolio-wide consistency: all systems sharing the same baseline start with identical org-default designations, eliminating per-system variance caused by manual CSP profile application.
- **SC-007**: The "Apply CSP Profile" step is no longer required for new system onboarding in organizations with fully configured org-wide capabilities, reducing the system setup workflow by one major step.
- **SC-008**: Audit trail captures 100% of designation changes with correct source attribution (OrgDerived, Manual, ProfileApply, BulkUpdate, CrmImport).

## Assumptions

- Org-wide capabilities are identified by having CapabilityControlMapping records where RegisteredSystemId is null (the existing convention for org-wide vs. system-scoped mappings).
- The CapabilityMappingRole enum values "Primary" and "Supporting" map to "Inherited," while "Shared" maps to "Shared." Controls with no implemented-capability mapping receive no org default.
- The existing Capabilities page and component management workflows (from prior features) are functional and populated with data before this feature provides value.
- CSP inheritance profiles from Feature 043 continue to work unchanged; this feature adds an alternative (and preferred) path via org-wide capabilities but does not remove or deprecate the profile workflow.
- Org defaults are re-derived synchronously inline as part of the capability save/update/delete API call (not async background jobs or scheduled batches), ensuring real-time consistency with no additional infrastructure.
- Multi-tenancy / multi-organization scoping is not in scope — org defaults apply to the single organization context of the current deployment.

## Clarifications

### Session 2026-03-21

- Q: How should the "Supporting" role map to an inheritance designation? → A: "Supporting" maps to "Inherited" (same as Primary).
- Q: How should org-level defaults be re-derived when capabilities change? → A: Synchronous inline — derive as part of the capability save API call.
- Q: Should org-level defaults use a dedicated table or flagged rows in ControlInheritance? → A: Separate `OrgInheritanceDefault` table (clean separation, simpler re-derivation).
- Q: How should multiple "Primary" mappings to the same control be handled? → A: Merge providers — designation stays "Inherited" with all contributing capabilities/components listed.
- Q: What is the canonical term for controls where the customer bears full responsibility? → A: "Customer Responsibility" (FedRAMP CRM standard term).
- Q: Where do the Apply CSP Profile / Import CRM / Generate CRM buttons go with org-level inheritance? → A: Generate CRM and Import CRM stay as primary header actions; Apply CSP Profile is demoted to a "More Actions" dropdown (labeled "optional" when org defaults exist, prominent when no org defaults). New actions added: "Revert Selected to Org Defaults" (bulk toolbar) and "View Org Defaults" (read-only org-wide view). See FR-015 and UX Layout section.
