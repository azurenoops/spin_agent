# Feature Specification: Org-Wide Risk Solutions & Context-Aware Narrative Generation

**Feature Branch**: `036-risk-solutions`
**Created**: 2026-03-17
**Status**: Draft
**Input**: Build "write once, apply everywhere" model — enrich SSP narrative generation with linked components, boundary context, and responsible roles. Refactor components from system-scoped to org-wide library with system assignments.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Context-Aware Narrative Generation (Priority: P1)

As an ISSO, when I map a capability (e.g., "Multi-Factor Authentication") to a control (e.g., IA-2), the system auto-generates a narrative that references the actual components delivering that capability, the boundary they operate within, and the responsible personnel — not just a generic template sentence.

**Why this priority**: This is the core value proposition. Generic narratives like "The organization implements MFA using Microsoft Entra ID" don't pass 3PAO scrutiny. Assessors want to see specific tools, infrastructure, boundary context, and who's responsible. Without this, every auto-generated narrative needs manual rewrites — defeating the purpose of automation.

**Independent Test**: Create a capability with linked components (a Thing, a Person, a Place), map it to a control, and verify the generated narrative text includes all component names, types, boundary name, and the responsible role/person.

**Acceptance Scenarios**:

1. **Given** a capability "Multi-Factor Authentication" with provider "Microsoft Entra ID" linked to components (Thing: "Azure Conditional Access", Person: "ISSO — John Smith", Place: "Azure Gov Cloud East") within a boundary "Production", **When** I create a control mapping to IA-2, **Then** the generated narrative includes all component names, the boundary name "Production", and the responsible person "John Smith" in a coherent SSP-appropriate paragraph.

2. **Given** a capability with no linked components, **When** I create a control mapping, **Then** the generated narrative falls back to the current template (capability name + provider + description + family context) without errors.

3. **Given** a capability mapped to a control that already has a manually customized narrative, **When** I add more components to the capability, **Then** the manually customized narrative is preserved (not overwritten) and flagged as "custom — auto-update skipped".

4. **Given** a control with multiple capabilities mapped (e.g., MFA as Primary, Account Lockout as Supporting), **When** narratives are generated, **Then** the composite narrative includes component context for each capability, organized by role (Primary first, then Supporting).

---

### User Story 2 — Org-Wide Component Library (Priority: P1)

As a security engineer, I define my organization's People, Places, and Things once in a central library. I then assign those components to specific systems and boundaries. When I update a component (e.g., rename the ISSO from "John Smith" to "Jane Doe"), every system's narratives that reference that component are automatically updated.

**Why this priority**: Equal priority with Story 1 because context-aware narratives require org-wide components to exist. The current model requires duplicating the same component per system, making cascade updates impossible. This is the structural foundation for the "write once, apply everywhere" pattern.

**Independent Test**: Create an org-wide component "ISSO — John Smith", assign it to two systems, map it to a capability, then rename it to "Jane Doe" and verify both systems' narratives update.

**Acceptance Scenarios**:

1. **Given** a component "Microsoft Entra ID" (Thing) defined in the org-wide library, **When** I assign it to systems "Eagle Eye" and "Falcon", **Then** it appears in both systems' component inventories without creating duplicate records.

2. **Given** an org-wide component assigned to 3 systems and linked to a capability that maps to 12 controls, **When** I update the component name from "Duo MFA" to "Okta MFA", **Then** all narratives across all 3 systems that reference this component are regenerated with the new name, and the system reports "36 narratives updated across 3 systems".
   > **Note**: This scenario validates cascade behavior implemented in User Story 4 (Phase 6). It is listed here for completeness but is independently testable only after US4 is complete.

3. **Given** an org-wide component assigned to a system within a specific boundary, **When** I view the system's component inventory, **Then** the component shows its boundary assignment and linked capabilities.

4. **Given** a component that is assigned to zero systems, **When** I view the org-wide library, **Then** it shows as "unassigned" with zero system/narrative impact.

---

### User Story 3 — Cascade Updates on Capability Changes (Priority: P2)

As an ISSO, when I change a capability's provider (e.g., swap "Duo" for "Okta"), all narratives across all systems that use that capability are automatically regenerated with the new provider and component details. I see a preview of the impact before confirming.

**Why this priority**: This is an signature feature. The data model already supports it partially (capability updates trigger narrative regen), but the generated text doesn't include component/boundary context. Depends on Stories 1 and 2 being complete.

**Independent Test**: Edit a capability's provider, confirm the impact preview, and verify all affected narratives across multiple systems are regenerated with updated text.

**Acceptance Scenarios**:

1. **Given** a capability "MFA" with provider "Duo" mapped to 81 controls across 3 systems, **When** I edit the provider to "Okta", **Then** the system shows an impact preview: "This change will regenerate 243 narratives across 3 systems (Eagle Eye: 81, Falcon: 81, Hawk: 81). 12 manually customized narratives will be skipped."

2. **Given** the impact preview is displayed, **When** I confirm the change, **Then** all non-custom narratives are regenerated and a completion summary shows the actual counts.

3. **Given** I cancel the change after seeing the impact preview, **Then** no narratives are modified.

---

### User Story 4 — Cascade Updates on Component Changes (Priority: P2)

As a security engineer, when I rename a component, reassign it to a different boundary, or change its owner, all narratives that reference that component through linked capabilities are automatically regenerated.

**Why this priority**: Complements Story 3 for the infrastructure side. When the ISSO changes, or a tool moves to a different boundary, the SSP must reflect that. Depends on Story 2 (org-wide components).

**Independent Test**: Rename a component, verify all narratives referencing it through capability links are regenerated with the new name.

**Acceptance Scenarios**:

1. **Given** a component "ISSO — John Smith" linked to capability "MFA" which maps to 20 controls across 2 systems, **When** I rename the component to "ISSO — Jane Doe", **Then** all 40 narratives are regenerated with "Jane Doe" replacing "John Smith".

2. **Given** a component "Azure Firewall" assigned to boundary "Production" linked to capability "Network Protection", **When** I reassign the component to boundary "Dev/Test", **Then** narratives referencing this component are regenerated with the new boundary context.

3. **Given** a component linked to multiple capabilities, **When** the component is updated, **Then** narratives for all linked capabilities across all systems are regenerated (not just one capability).

---

### User Story 5 — Capability Coverage View Per System (Priority: P3)

As an ISSO viewing a specific system (e.g., Eagle Eye), I see a "Capability Coverage" view that shows all capabilities assigned to this system, which components deliver each capability, which controls each capability covers, and the narrative generation status for each control.

**Why this priority**: This provides the unified view connecting capabilities, components, controls, and narratives per system. It is a reporting/visibility feature that depends on Stories 1 and 2 being in place.

**Independent Test**: Navigate to a system's Capability Coverage page and verify it displays capabilities with their linked components, mapped controls, and narrative status (auto-populated, custom, empty).

**Acceptance Scenarios**:

1. **Given** a system "Eagle Eye" with 5 capabilities mapped to 120 controls, **When** I open the Capability Coverage view, **Then** I see each capability with its linked components, the count of mapped controls, and a progress indicator (e.g., "81/81 narratives populated").

2. **Given** a capability with 3 linked components (1 Person, 1 Place, 1 Thing), **When** I expand the capability in the coverage view, **Then** I see all 3 components with their type, status, boundary assignment, and owner.

3. **Given** a control mapped to multiple capabilities with different roles (Primary vs Supporting), **When** I view it in the coverage view, **Then** the Primary capability is highlighted and listed first.

---

### Edge Cases

- What happens when a component is deleted that is still linked to capabilities? The system unlinks it and regenerates affected narratives without that component's context. A warning is shown with the count of affected narratives.
- What happens when a boundary is deleted and components assigned to it are reassigned to the Primary boundary? Component-boundary reassignment triggers narrative regeneration for all linked capabilities.
- What happens when an org-wide component is assigned to a system that already has a system-specific component with the same name? The system warns about the duplicate and offers to merge or keep both.
- What happens when a capability has components linked from different boundaries? The composite narrative includes per-boundary sections: "Within the Production boundary: [components]. Within the Dev/Test boundary: [components]."
- How are existing system-scoped components migrated to the org-wide library? A one-time migration deduplicates components by name+type, creates org-wide records, and replaces system-scoped records with assignments. Components with identical names but different descriptions are logged as warnings but migrated automatically.

## Clarifications

### Session 2026-03-17

- Q: When cascade narrative regeneration fails partway through (e.g., 150 of 243 narratives updated before a database timeout), what should happen? → A: Transactional per system — each system's narratives are committed independently. Failed systems are retried.
- Q: How is the boundary scope determined when an org-wide component is assigned to a system for narrative context? → A: Boundary set explicitly per assignment — user picks the boundary when assigning a component to a system.
- Q: Should the migration from system-scoped to org-wide components run automatically at startup or be admin-triggered? → A: Automatic at startup — runs during database initialization. There is minimal real data to migrate so no manual review needed.
- Q: When cascade regeneration updates an auto-populated narrative, should it create a new NarrativeVersion or overwrite in-place? → A: Create a new NarrativeVersion — increment version counter, reset approval to Draft, preserve audit history.
- Q: Where should the org-wide component library live in the UI navigation? → A: Top header nav alongside Portfolio and Capabilities at `/components`. Remove Components from the system details left nav menu.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The narrative generation engine MUST query linked components (via capability-to-component links) and embed component names, types, and owners into generated narrative text.
- **FR-002**: The narrative generation engine MUST include the boundary name and type for boundary-scoped capability mappings (e.g., "within the Production boundary").
- **FR-003**: The narrative generation engine MUST include the responsible role/person from the capability owner or the linked Person-type component.
- **FR-004**: Components (People, Places, Things) MUST be definable as org-wide resources without a required system association.
- **FR-005**: The system MUST support assigning org-wide components to one or more systems. Each assignment MUST include an explicit boundary selection — the user picks which boundary the component operates within for that system.
- **FR-006**: When a capability's name, provider, or description is updated, the system MUST regenerate all non-custom narratives across all affected systems, using current component and boundary context.
- **FR-007**: When a component's name, description, or owner changes, the system MUST regenerate all non-custom narratives that reference that component through linked capabilities.
- **FR-008**: When a component's boundary assignment changes, the system MUST regenerate affected narratives with the updated boundary context.
- **FR-009**: Before saving a capability or component change that triggers narrative regeneration, the system MUST display an impact preview showing the count of affected narratives and affected systems.
- **FR-010**: The system MUST provide a Capability Coverage view within the system context showing capabilities, their linked components, mapped controls, and narrative status.
- **FR-011**: Manually customized narratives (where `IsManuallyCustomized` is true) MUST be preserved during cascade regeneration and flagged as "custom — auto-update skipped".
- **FR-012**: The system MUST automatically migrate existing system-scoped components to org-wide records with system assignments at startup during database initialization, deduplicating by name and type.
- **FR-013**: Narrative generation for controls with multiple capability mappings MUST produce a composite narrative organized by mapping role (Primary first, then Supporting, then Shared) with component context per capability.
- **FR-014**: Authorization boundaries MUST remain system-scoped — each system defines its own boundaries independently.
- **FR-015**: Cascade narrative regeneration MUST be transactional per system — each system's batch of narrative updates commits independently. If one system's batch fails, other systems' completed batches are preserved. Failed systems MUST be retried once immediately. If the retry also fails, the system MUST be reported as failed in the cascade result summary with the error reason. No further automatic retries are attempted.
- **FR-016**: Cascade narrative regeneration MUST create a new NarrativeVersion record for each updated narrative, incrementing the version counter and resetting approval status to Draft. The change reason MUST indicate the trigger (e.g., "Auto-regenerated: capability provider changed from Duo to Okta").
- **FR-017**: The org-wide component library MUST be accessible via a top-level header nav link at `/components`, alongside Portfolio and Capabilities. The "Components" link MUST be removed from the system details left navigation menu.
- **FR-018**: NarrativeTemplateService MUST support an AI-assisted generation mode. When AI is enabled (via `AzureAiOptions.Enabled`), the service MUST use `IChatClient` to generate contextually rich, SSP-appropriate narrative text using a system prompt that includes capability metadata, component context, boundary information, and control family guidance. When AI is disabled or unavailable, the service MUST fall back to the existing deterministic template. AI-generated narratives MUST set `AiSuggested = true` on the `ControlImplementation` record.

### Key Entities

- **SecurityCapability** (existing, org-wide): A reusable security capability (Risk Solution) like "Multi-Factor Authentication" with a provider, description, and owner. Maps to controls. Links to components. Defined once, applied across systems.
- **SystemComponent** (refactored to org-wide): A Person, Place, or Thing in the organization's security stack. Defined once at the org level. Assigned to systems via a new assignment join entity. Examples: "ISSO — John Smith" (Person), "Azure Gov East" (Place), "Microsoft Entra ID" (Thing).
- **ComponentSystemAssignment** (new): Links an org-wide component to a specific system with an optional boundary scope. Enables one component to serve multiple systems.
- **CapabilityControlMapping** (existing): Maps a capability to a NIST control with a role (Primary/Supporting/Shared), optional system scope, and optional boundary scope. Triggers narrative generation.
- **ComponentCapabilityLink** (existing): Many-to-many link between components and capabilities. Used by narrative generation to discover which tools/people/places deliver a capability.
- **ControlImplementation** (existing): The per-system per-control narrative record. Updated by cascade regeneration when capabilities or components change.
- **AuthorizationBoundaryDefinition** (existing, system-scoped): Named security perimeter per system. Components are assigned to boundaries through their system assignment.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Updating a single capability's provider regenerates all affected narratives across all systems within 10 seconds, without manual intervention per system.
- **SC-002**: Generated narratives include specific component names, boundary names, and responsible personnel — not generic placeholders — for 100% of auto-populated narratives where components are linked.
- **SC-003**: Renaming a component updates all referencing narratives across all systems in a single operation, with the user performing exactly one edit action.
- **SC-004**: Users can define a component once and assign it to multiple systems, reducing duplicate component records by at least 80% compared to the current per-system model.
- **SC-005**: The impact preview accurately reports the number of affected narratives and systems before any cascade change is applied.
- **SC-006**: 3PAO-ready narrative quality — generated narratives answer who (responsible person), what (capability/tool), where (boundary), and how (implementation description) for each control.
- **SC-007**: When AI-assisted narrative generation is enabled, AI-generated narratives MUST be coherent, contextually accurate, and require no more than minor edits in 90% of cases as assessed during acceptance testing.

## Assumptions

- The existing SecurityCapability model is already org-wide and does not need structural changes — only the narrative generation logic enrichment.
- The existing CapabilityControlMapping join table correctly supports system-scoped and org-wide mappings via the optional RegisteredSystemId FK.
- The existing ComponentCapabilityLink M2M join table will continue to serve as the link between components and capabilities.
- The `IsAutoPopulated` and `IsManuallyCustomized` flags together determine cascade eligibility: regenerate when `IsAutoPopulated == true && IsManuallyCustomized == false`. The `AiSuggested` flag indicates whether AI was used for the most recent generation.
- Authorization boundaries remain system-scoped because each system's security perimeter is architecturally distinct.
- The one-time migration from system-scoped to org-wide components can run during a maintenance window and is a non-destructive transformation (no data loss, only restructuring).
- Performance of cascade narrative regeneration is acceptable for up to 500 narratives per batch (largest expected scenario: 1 capability x 5 systems x 100 controls).

## Dependencies

- Feature 033 (Boundary-Scoped Model) — boundary definitions and boundary-scoped capability mappings are prerequisite and already implemented.
- Feature 035 (Deviation Management) — deviation-aware narratives must not be broken by cascade regeneration.
- Feature 024 (Narrative Governance) — versioned narratives and approval workflows must be respected during cascade updates (regeneration creates new versions, resets approval to Draft).
