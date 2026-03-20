# Feature Specification: Registered System Intake Wizard

**Feature Branch**: `042-system-intake-wizard`  
**Created**: 2025-03-20  
**Status**: Draft  
**Input**: User description: "Create a feature spec for a Registered System Intake Wizard. The wizard should take the place of the 'add system' button on the systems page. The wizard should allow the user to register a system, add capabilities, define system components, add auth boundaries, assign RMF roles, verify role assignments, then set categorization. The wizard should be intuitive and walks the user through the intake. Make sure you do performance, and documentation updates."

## Clarifications

### Session 2026-03-20

- Q: Should the intake wizard be a full-page routed view or a modal overlay? → A: Modal overlay on the Systems page (stays on `/systems`).
- Q: What happens to the system if the user cancels or abandons the wizard after Step 1? → A: Keep the system with a visible "Setup Incomplete" indicator in the portfolio.
- Q: Should Step 2 allow creating new capabilities inline or only linking existing ones? → A: Only link existing capabilities (search & select from library).
- Q: How should users be identified when assigning RMF roles in Step 5? → A: Select from existing org-wide Person components (SystemComponent where type = Person).
- Q: How should SP 800-60 information types be presented in Step 7? → A: Searchable list grouped by SP 800-60 Vol II category, with auto-suggested C/I/A levels from selected types.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register a New System via Guided Wizard (Priority: P1)

An ISSM or system owner clicks the "+ Add System" button on the Systems (Portfolio) page and is presented with a multi-step wizard instead of the current single-dialog form. The wizard opens in Step 1: System Registration, where the user enters the system name, acronym, system type, mission criticality, hosting environment, and description. A progress indicator at the top shows the user where they are in the overall intake process. After completing this step, the user advances to the next step.

**Why this priority**: System registration is the foundational first action — no other wizard step can proceed without a registered system. This replaces the existing "Add System" dialog and must work independently.

**Independent Test**: Can be fully tested by clicking "+ Add System," filling in system details, and confirming the system appears in the portfolio list. Delivers the core system creation value.

**Acceptance Scenarios**:

1. **Given** the user is on the Systems page, **When** they click "+ Add System," **Then** a multi-step wizard opens at Step 1 with fields for system name, acronym, system type, mission criticality, hosting environment, and description.
2. **Given** the user is on Step 1, **When** they fill in the required fields and click "Next," **Then** the system is persisted and the wizard advances to Step 2.
3. **Given** the user is on Step 1, **When** they leave required fields empty and click "Next," **Then** inline validation errors appear and the wizard does not advance.
4. **Given** the wizard is open, **When** the user clicks "Cancel" at any step, **Then** any unsaved step data is discarded (the already-saved system registration remains if Step 1 was completed) and the user returns to the Systems page.

---

### User Story 2 - Add Security Capabilities (Priority: P2)

After registering the system, the wizard advances to Step 2: Security Capabilities. The user can search and select from existing security capabilities in the organization's library. Selected capabilities are linked to the system, giving the user a clear picture of what security controls the system will leverage. New capabilities are not created inline — the user manages the capability library separately.

**Why this priority**: Capabilities provide the security foundation for the system and inform control mapping downstream. This is important for completeness but the system can exist without capabilities initially.

**Independent Test**: Can be tested by advancing past Step 1, searching for and selecting capabilities, then confirming the selected capabilities are associated with the system.

**Acceptance Scenarios**:

1. **Given** the user is on Step 2, **When** the step loads, **Then** a searchable list of available security capabilities is displayed.
2. **Given** the user is on Step 2, **When** they select one or more capabilities and click "Next," **Then** the selections are saved and the wizard advances to Step 3.
3. **Given** the user is on Step 2, **When** they click "Skip" without selecting any capabilities, **Then** the wizard advances to Step 3 with no capabilities linked (can be added later).

---

### User Story 3 - Define System Components (Priority: P2)

In Step 3: System Components, the user adds components categorized as Person, Place, or Thing. For each component the user provides a name, type, sub-type, description, and owner. The wizard allows adding multiple components in a list view with inline "Add" and "Remove" actions.

**Why this priority**: Components form the system inventory required for the SSP and boundary definition. Co-priority with capabilities since both feed downstream steps.

**Independent Test**: Can be tested by adding components of various types (Person, Place, Thing) and confirming they appear in the system's component inventory.

**Acceptance Scenarios**:

1. **Given** the user is on Step 3, **When** the step loads, **Then** a component entry form and list of already-added components is displayed.
2. **Given** the user is on Step 3, **When** they add a component with name, type, and required fields and click "Add," **Then** the component appears in the list.
3. **Given** the user is on Step 3, **When** they click "Remove" on an added component, **Then** the component is removed from the list.
4. **Given** the user is on Step 3, **When** they click "Next" with at least one component added, **Then** components are persisted and the wizard advances to Step 4.
5. **Given** the user is on Step 3, **When** they click "Skip" without adding components, **Then** the wizard advances to Step 4 (components can be added later).

---

### User Story 4 - Add Authorization Boundaries (Priority: P2)

In Step 4: Authorization Boundaries, the user defines one or more authorization boundaries for the system. For each boundary the user provides a name, type (Physical, Logical, or Hybrid), and description. Components added in Step 3 can be assigned to boundaries in this step.

**Why this priority**: Authorization boundaries are a gate condition for advancing from Prepare to Categorize in the RMF lifecycle. Without boundaries, the system cannot progress through RMF.

**Independent Test**: Can be tested by creating a boundary, optionally assigning components to it, and confirming boundaries appear in the system's Boundary Management page.

**Acceptance Scenarios**:

1. **Given** the user is on Step 4, **When** the step loads, **Then** a boundary creation form is displayed with fields for name, type, and description.
2. **Given** the user is on Step 4, **When** they create a boundary and optionally assign previously-added components, **Then** the boundary and assignments are saved.
3. **Given** the user is on Step 4, **When** they click "Next" with at least one boundary defined, **Then** the wizard advances to Step 5.
4. **Given** the user is on Step 4, **When** they click "Skip," **Then** the wizard advances to Step 5 with no boundaries defined (can be added later).

---

### User Story 5 - Assign RMF Roles (Priority: P3)

In Step 5: RMF Roles, the user assigns personnel to standard RMF roles (Authorizing Official, ISSM, ISSO, SCA, System Owner). The user selects a role and then picks a person from the existing org-wide Person components (SystemComponent entries of type Person). This ensures role assignments reference real personnel already tracked in the system inventory.

**Why this priority**: RMF role assignments are a gate condition for progressing from Prepare to Categorize. This step is important but can be done later if personnel are not yet identified.

**Independent Test**: Can be tested by assigning at least one role and confirming the assignment appears on the system's role management view.

**Acceptance Scenarios**:

1. **Given** the user is on Step 5, **When** the step loads, **Then** the standard RMF roles are listed with current assignment status (assigned or unassigned).
2. **Given** the user is on Step 5, **When** they select a role and pick a Person component from the searchable list, **Then** the role assignment is saved immediately.
3. **Given** the user is on Step 5, **When** they assign multiple roles and click "Next," **Then** the wizard advances to Step 6.
4. **Given** the user is on Step 5, **When** they click "Skip" without assigning roles, **Then** the wizard advances to Step 6 (roles can be assigned later).

---

### User Story 6 - Verify Role Assignments (Priority: P3)

In Step 6: Verify Roles, the wizard presents a read-only summary of all role assignments made in Step 5. The user reviews the assignments for accuracy. They can go back to Step 5 to make changes or confirm and proceed.

**Why this priority**: Verification ensures data quality for a critical RMF gate condition. Paired with role assignment as a confirmation step.

**Independent Test**: Can be tested by completing Step 5, reviewing the summary in Step 6, confirming all assignments are accurately displayed, and proceeding.

**Acceptance Scenarios**:

1. **Given** the user is on Step 6, **When** the step loads, **Then** a summary table shows all assigned roles with person name, role, and assignment date.
2. **Given** the user reviews the summary, **When** they notice an error and click "Back," **Then** they return to Step 5 to edit role assignments.
3. **Given** the user verifies all assignments are correct, **When** they click "Next," **Then** the wizard advances to Step 7.

---

### User Story 7 - Set Security Categorization (Priority: P3)

In Step 7: Security Categorization, the user sets the FIPS 199 security categorization for the system. The user selects information types from a searchable list grouped by SP 800-60 Volume II categories (e.g., "Services Delivery," "Government Resource Management"). Each selected information type auto-populates recommended Confidentiality, Integrity, and Availability impact levels as suggestions. The user can override any suggested level. The overall categorization is computed as the high-water mark of the three impact levels across all selected information types.

**Why this priority**: Categorization is required to advance from Categorize to Select in the RMF lifecycle. It is the final wizard step and caps off the intake process.

**Independent Test**: Can be tested by selecting impact levels, adding information types, and confirming the categorization is saved and reflected on the system detail page.

**Acceptance Scenarios**:

1. **Given** the user is on Step 7, **When** the step loads, **Then** a searchable information type selector is displayed, grouped by SP 800-60 Volume II categories, along with Confidentiality, Integrity, and Availability impact level fields.
2. **Given** the user selects an information type, **When** it is added, **Then** the recommended C/I/A levels from SP 800-60 are auto-populated as suggestions and the user can override any level.
3. **Given** the user selects impact levels and adds at least one information type, **When** they click "Finish," **Then** the categorization is saved and the overall FIPS 199 category is computed as the high-water mark across all selected types.
4. **Given** the user clicks "Finish," **When** the save succeeds, **Then** a completion summary is displayed and the user is navigated to the newly created system's detail page.
5. **Given** the user is on Step 7, **When** they click "Skip & Finish," **Then** the wizard completes without categorization (can be done later) and navigates to the system detail page.

---

### User Story 8 - Wizard Navigation and Progress Tracking (Priority: P1)

Throughout the intake wizard, a progress indicator (stepper) is displayed showing all steps, the current step, and completed steps. The user can navigate backward to any previously completed step to review or edit data. Steps beyond the current step are not navigable until reached sequentially. The wizard preserves all entered data when navigating between steps.

**Why this priority**: Navigation and progress tracking are essential for the wizard to be usable. Without them the multi-step flow would be disorienting.

**Independent Test**: Can be tested by navigating forward and backward through steps, verifying data persists across navigation, and confirming the progress indicator updates correctly.

**Acceptance Scenarios**:

1. **Given** the wizard is open, **When** the user views the progress indicator, **Then** completed steps show a checkmark, the current step is highlighted, and future steps are grayed out.
2. **Given** the user is on Step 4, **When** they click on Step 2 in the progress indicator, **Then** the wizard navigates to Step 2 with all previously entered data intact.
3. **Given** the user is on Step 3, **When** they click on Step 6 in the progress indicator, **Then** nothing happens (forward-skipping is not allowed via the stepper).

---

### User Story 9 - Performance Optimization (Priority: P2)

The wizard loads quickly and each step transition is responsive. Data entry forms within each step are performant even when displaying large lists of capabilities, components, or information types. The wizard minimizes network calls by batching saves at step transitions rather than on every keystroke.

**Why this priority**: A slow or unresponsive wizard undermines the guided experience and leads to user frustration, especially for organizations managing many systems.

**Independent Test**: Can be tested by measuring wizard load time, step transition times, and form responsiveness under standard data volumes.

**Acceptance Scenarios**:

1. **Given** the user clicks "+ Add System," **When** the wizard opens, **Then** Step 1 is fully interactive within 2 seconds.
2. **Given** the user clicks "Next" on any step, **When** data is saved, **Then** the next step loads within 1 second under normal conditions.
3. **Given** a step displays a searchable list (capabilities, information types), **When** the user types a search query, **Then** results filter within 300 milliseconds.

---

### User Story 10 - Documentation Updates (Priority: P3)

End-user documentation is updated to reflect the new intake wizard workflow. The documentation covers each wizard step, explains what data is needed, and provides guidance on when steps can be skipped. Persona-specific getting-started guides are updated for ISSM, ISSO, and System Owner roles.

**Why this priority**: Documentation ensures users can self-serve and reduces support burden, but does not block the feature from shipping.

**Independent Test**: Can be tested by reviewing the updated documentation pages for completeness, accuracy, and alignment with the wizard UI.

**Acceptance Scenarios**:

1. **Given** the documentation site, **When** a user searches for "system intake" or "register system," **Then** they find a guide explaining the wizard workflow step by step.
2. **Given** the ISSM getting-started guide, **When** reviewed post-update, **Then** it references the intake wizard as the primary method for registering new systems.

---

### Edge Cases

- What happens when the user's session expires mid-wizard? The wizard preserves completed steps (already saved to the server) and prompts re-authentication. Unsaved data on the current step is lost.
- What happens if the user closes the browser tab during the wizard? Completed steps (saved server-side) remain intact. The system appears in the portfolio with a "Setup Incomplete" badge indicating not all wizard steps were finished. The user can click the system to resume configuration from individual management pages ("Resume Setup" via wizard re-entry is deferred to a future enhancement).
- What happens if a duplicate system name is entered? The system displays a validation error on Step 1 indicating the name is already in use.
- How does the wizard handle network failures during a "Next" transition? An error notification is displayed, the user remains on the current step, and they can retry.
- What happens if a capability or information type is deleted by another user while the wizard is open? The wizard refreshes list data on each step load. If a previously selected item no longer exists, a warning is shown and the user can select an alternative.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST replace the existing "+ Add System" dialog on the Systems page with a multi-step intake wizard.
- **FR-002**: The wizard MUST include the following steps in order: (1) System Registration, (2) Security Capabilities, (3) System Components, (4) Authorization Boundaries, (5) Assign RMF Roles, (6) Verify Role Assignments, (7) Set Categorization.
- **FR-003**: System MUST display a progress indicator showing all steps, the current step, and completed steps throughout the wizard.
- **FR-004**: System MUST allow backward navigation to any previously completed step while preserving entered data.
- **FR-005**: System MUST NOT allow forward-skipping to unreached steps via the progress indicator.
- **FR-006**: System MUST persist data at each step transition (on "Next" or "Skip") so that completed steps survive navigation, session loss, or wizard abandonment.
- **FR-007**: Steps 2 through 7 MUST offer a "Skip" option allowing the user to defer that step's configuration to a later time.
- **FR-008**: Step 1 (System Registration) MUST collect: system name (required), acronym (optional), system type (required), mission criticality (required), hosting environment (required), and description (optional).
- **FR-009**: Step 1 MUST validate that the system name is unique before allowing progression.
- **FR-010**: Step 2 (Security Capabilities) MUST present a searchable, selectable list of existing security capabilities and allow linking selected capabilities to the system. The wizard MUST NOT provide inline capability creation; capabilities are managed in the Security Capabilities Library.
- **FR-011**: Step 3 (System Components) MUST allow creating components of type Person, Place, or Thing with name, type, sub-type, description, and owner fields. When the component type is Person, the form MUST additionally collect personName (required) and email (optional) fields.
- **FR-012**: Step 4 (Authorization Boundaries) MUST allow creating boundaries of type Physical, Logical, or Hybrid and optionally assigning components from Step 3.
- **FR-013**: Step 5 (Assign RMF Roles) MUST present the standard RMF roles (Authorizing Official, ISSM, ISSO, SCA, System Owner) and allow assigning each role by selecting from a searchable list of org-wide Person components (SystemComponent entries where componentType = Person). Free-text entry is not permitted; all assignees must be existing Person components.
- **FR-014**: Step 6 (Verify Role Assignments) MUST display a read-only summary of all role assignments and allow the user to navigate back to Step 5 for corrections.
- **FR-015**: Step 7 (Set Categorization) MUST present SP 800-60 information types in a searchable list grouped by Volume II categories. When an information type is selected, its recommended C/I/A impact levels MUST auto-populate the Confidentiality, Integrity, and Availability fields as editable suggestions. The user MUST be able to override any suggested level.
- **FR-016**: Step 7 MUST automatically compute the overall FIPS 199 category as the high-water mark of the three impact levels.
- **FR-017**: Upon completing the final step, the wizard MUST display a completion summary and navigate the user to the newly registered system's detail page.
- **FR-018**: System MUST display a "Cancel" button on every step that discards unsaved current-step data and returns the user to the Systems page.
- **FR-019**: Step 2 MUST support searching and filtering capabilities by name, category, and implementation status.
- **FR-020**: Step 3 MUST allow the user to generate a component description using the existing AI description generation feature. The description field MUST include a "Generate" button that invokes the existing `compliance_generate_description` MCP tool and populates the result into the description textarea, with a loading spinner during generation and an error toast if the call fails.
- **FR-021**: End-user documentation MUST be updated to describe the intake wizard workflow including all seven steps.
- **FR-022**: Persona-specific getting-started guides (ISSM, ISSO, and Engineer/System Owner) MUST reference the intake wizard as the primary method for system registration. The System Owner persona is documented in `docs/getting-started/engineer.md`.
- **FR-023**: The wizard MUST be responsive and function correctly on screen widths from 1024px and above.
- **FR-024**: Systems where the wizard was not fully completed MUST display a "Setup Incomplete" badge on the portfolio page. The badge MUST be removed once all required setup steps (roles assigned, boundary defined, categorization set) are completed, whether through the wizard or individual management pages.

### Non-Functional Requirements

- **NFR-001**: The wizard's initial load (Step 1 visible and interactive) MUST complete within 2 seconds on a 25 Mbps symmetric connection (FCC broadband minimum).
- **NFR-002**: Each step transition (save + load next step) MUST complete within 1 second under normal load.
- **NFR-003**: Searchable lists within the wizard MUST filter results within 300 milliseconds of user input.
- **NFR-004**: The wizard MUST batch save operations at step boundaries rather than on every form field change to minimize network overhead.

### Key Entities

- **RegisteredSystem**: The core entity created in Step 1. Represents an information system being tracked for RMF compliance. Key attributes: name, acronym, systemType, missionCriticality, hostingEnvironment, description, currentRmfStep.
- **SecurityCapability**: A security tool or control mapped in Step 2. Key attributes: name, provider, category, implementationStatus. Linked to the system via CapabilityControlMappings.
- **SystemComponent**: An inventory item (Person, Place, or Thing) created in Step 3. Key attributes: name, componentType, subType, description, owner, personName, email, rmfRoleName.
- **BoundaryDefinition**: An authorization boundary created in Step 4. Key attributes: name, boundaryType (Physical/Logical/Hybrid), description. Components are assigned via BoundaryComponentAssignment.
- **RmfRoleAssignment**: A personnel-to-role mapping created in Step 5. Key attributes: role, userId, userDisplayName, assignedAt, assignedBy.
- **SecurityCategorization**: The FIPS 199 categorization set in Step 7. Key attributes: confidentialityImpact, integrityImpact, availabilityImpact, overallImpact, informationTypes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can register a new system and complete all seven wizard steps in under 10 minutes for a typical system (5–10 components, 3–5 capabilities, 2 boundaries, 5 role assignments).
- **SC-002**: 90% of users successfully complete the wizard on their first attempt without needing external help or documentation.
- **SC-003**: The number of systems registered with incomplete initial configuration (missing roles, boundaries, or categorization) decreases by 50% compared to the current single-dialog approach.
- **SC-004**: User satisfaction with the system registration process improves, measured by a reduction in support requests related to system setup by 40%.
- **SC-005**: Wizard step transitions are perceived as instant by users (under 1 second) in 95% of interactions.
- **SC-006**: The intake wizard loads and is interactive within 2 seconds of clicking "+ Add System" in 99% of page loads.
- **SC-007**: All seven wizard steps are documented in end-user guides, and documentation search returns relevant results for "register system," "intake wizard," and "add system" queries.

## Assumptions

- The existing backend endpoints for system registration, capabilities, components, boundaries, roles, and categorization are sufficient. The wizard orchestrates existing operations in a guided UI flow.
- The wizard is implemented as a modal overlay on the Systems page within the existing dashboard application, staying on the `/systems` route rather than navigating to a separate page.
- Users are authenticated before accessing the Systems page; no additional authentication is needed for the wizard.
- The "Skip" functionality on steps 2–7 means the wizard simply advances without calling the save operation for that step.
- Data validation rules align with existing form validations (e.g., system name max 200 characters, role names from the existing RMF role list).
- The AI-powered description generation feature (already available for components and systems) will be reused inside the wizard without modification.
- Performance targets (2-second load, 1-second transitions) assume a 25 Mbps symmetric connection (FCC broadband minimum) and are measured on the client-side.
- "Resume Setup" (re-opening the wizard for an incomplete system) is deferred to a future enhancement. Users can resume configuration from individual management pages on the system detail view.
