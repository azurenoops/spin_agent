# Feature Specification: Component-Centric Boundary Model

**Feature Branch**: `040-component-centric-boundary`
**Created**: 2025-03-19
**Status**: Draft
**Input**: User description: "Component-Centric Boundary Model — Refactor the authorization boundary architecture so that SystemComponents become the single source of truth for all assets (People, Places, Things)."

## Clarifications

### Session 2026-03-19

- Q: What is the migration rollback strategy if the data migration (Story 5) fails partway through? → A: Wrap entire migration in a single DB transaction; roll back all changes on any failure.
- Q: How should concurrent boundary-component assignment conflicts be handled? → A: Pessimistic lock — the UI locks the boundary-component row while one user is editing and displays a message to other users that someone is currently updating the boundary.
- Q: Should Person and Place components have automated discovery paths? → A: Entra ID discovery for Person components at the org-wide Component Library only (not system-level), behind an optional organization setting. Places remain manual-entry only.
- Q: What should happen when Azure discovery API calls fail or return partial results? → A: Show partial results with a warning banner indicating which resource groups or subscriptions failed; allow retry of failed portions.
- Q: What is the acceptable maximum duration for the data migration on a system with 1,000 existing boundary resource rows? → A: Under 60 seconds.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Azure Discovery in Component Library (Priority: P1)

As an ISSO, I navigate to the org-wide Component Library page and click "Discover from Azure." The system prompts me for an Azure subscription. I see a paginated, filterable list of discovered Azure resources grouped by resource group. I select multiple resources and click "Import Selected." The system creates one org-wide "Thing" component per selected resource, populating its Azure resource fields (resource ID, resource type, resource group, and location). Duplicate resources (already imported) are flagged and skipped.

**Why this priority**: This is the foundational data-entry path. Without Azure-discovered components in the library, no downstream boundary assignment or migration is possible. It also directly supports NIST SP 800-37 Task P-16 (Asset Identification), which must complete before boundary definition.

**Independent Test**: Can be fully tested by performing a discovery scan, importing three resources, verifying the new "Thing" components appear in the library with correct Azure fields, and confirming a re-scan shows them as already imported.

**Acceptance Scenarios**:

1. **Given** an ISSO is on the Component Library page with valid Azure credentials, **When** they click "Discover from Azure" and select a subscription, **Then** the system displays Azure resources grouped by resource group with name, type, resource group, and location columns.
2. **Given** discovered resources are displayed, **When** the ISSO selects five resources and clicks "Import Selected," **Then** five new org-wide "Thing" components are created, each with Azure resource fields populated.
3. **Given** three of those five resources were already imported in a prior scan, **When** the ISSO runs discovery again, **Then** those three resources display an "Already imported" badge and are excluded from bulk import by default.
4. **Given** the ISSO filters discovery by resource group or resource type, **When** results refresh, **Then** only matching resources appear.
5. **Given** the subscription contains more than 100 resources, **When** discovery loads, **Then** results are paginated and the ISSO can page through them without performance degradation.

---

### User Story 2 — System-Level Azure Discovery in Component Inventory (Priority: P1)

As an ISSM or ISSO, I navigate to the system-level Components page and click "Discover from Azure." The system scans the Azure subscription associated with this system and displays discovered resources. I select resources and click "Import Selected." The system creates system-scoped "Thing" components directly within this system, populating Azure resource fields. This supports the common scenario where Azure resources are already deployed before the ATO process begins — the user needs to quickly inventory existing infrastructure as system-level components without first creating them at the org level and then assigning them.

**Why this priority**: Many organizations deploy Azure infrastructure before initiating the ATO process. The system-level discovery path is the fastest way to populate the component inventory for a specific system, which is required before boundary definition (NIST P-16 before P-17). Without this, users must either manually create each component or go through the org-level library and then assign — adding unnecessary friction for the most common workflow.

**Independent Test**: Can be fully tested by navigating to a system's Components page, running Azure discovery, importing four resources, and verifying four system-scoped "Thing" components appear in the inventory with correct Azure fields and the correct RegisteredSystemId.

**Acceptance Scenarios**:

1. **Given** an ISSM is on the system-level Components page and the system has an Azure subscription configured, **When** they click "Discover from Azure," **Then** the system displays discoverable Azure resources grouped by resource group.
2. **Given** discovered resources are displayed, **When** the ISSM selects three resources and clicks "Import Selected," **Then** three new system-scoped "Thing" components are created with RegisteredSystemId set to the current system and Azure resource fields populated.
3. **Given** two of those resources already exist as components (org-wide or system-scoped) for this system, **When** discovery runs, **Then** those resources display an "Already imported" badge and are excluded from bulk import by default.
4. **Given** the same Azure resource was previously imported at the org level, **When** the ISSM views system-level discovery, **Then** the resource shows "Exists in org library" and offers an option to assign the existing org component to this system instead of creating a duplicate.
5. **Given** the system does not have an Azure subscription configured, **When** the ISSM opens the Components page, **Then** the "Discover from Azure" button is disabled with a tooltip explaining that an Azure subscription must be linked to this system first.

---

### User Story 3 — Boundary Component Assignment with Include/Exclude (Priority: P1)

As an ISSM, I open the Boundary Management page for a system and select a boundary definition. I see a "Components" tab showing all components assigned to this boundary with their include/exclude status. I can assign components from the org-wide library (or system-specific components), toggle each between "In Scope" and "Excluded," provide an exclusion rationale when excluding, and specify an inheritance provider when applicable. The same component can appear in multiple boundaries with different scope statuses.

**Why this priority**: This is the core architectural change — moving scope tracking from the resource record to the boundary-component relationship. Without this, components cannot be scoped per-boundary and the model remains resource-centric.

**Independent Test**: Can be fully tested by assigning one component to two boundaries — "In Scope" in Boundary A, "Excluded" (with rationale) in Boundary B — and verifying each boundary shows the correct status independently.

**Acceptance Scenarios**:

1. **Given** a boundary definition exists and org-wide components are in the library, **When** the ISSM opens the boundary's Components tab and clicks "Assign Component," **Then** a picker shows available components not yet assigned to this boundary.
2. **Given** a component is assigned to a boundary as "In Scope," **When** the ISSM toggles it to "Excluded," **Then** the system requires an exclusion rationale before saving.
3. **Given** a component is assigned to Boundary A as "In Scope," **When** the ISSM assigns the same component to Boundary B as "Excluded" with rationale "Managed by external CSP," **Then** both assignments coexist: Boundary A shows "In Scope," Boundary B shows "Excluded."
4. **Given** a component has an inheritance provider set, **When** displaying the component in the boundary view, **Then** the inheritance provider is shown alongside the component's scope status.
5. **Given** a component is removed from a boundary, **When** the removal completes, **Then** the component itself remains in the org-wide library; only the boundary assignment is deleted.

---

### User Story 4 — Simplified Boundary Management Page (Priority: P2)

As an ISSM, when I open the Boundary Management page, I see boundary definitions and their assigned components — there is no separate "Resources" tab for raw Azure resource management. The resource tab is replaced by a unified component assignment view with include/exclude toggles. Existing resource-only data has been migrated to components.

**Why this priority**: This is the UX simplification that follows from the data model change. It depends on User Stories 1 and 2 being complete so that all assets are components and boundary scoping works through the relationship entity.

**Independent Test**: Can be fully tested by opening the Boundary Management page for a system that previously had raw resources, verifying those resources now appear as components, and confirming the old resource-management tab is no longer present.

**Acceptance Scenarios**:

1. **Given** a system with a boundary that previously had raw Azure resource entries, **When** the ISSM opens the Boundary Management page, **Then** the old "Resources" tab is not visible; only the "Components" tab is shown.
2. **Given** the Components tab is displayed for a boundary, **When** the ISSM views the list, **Then** each component shows its name, type (Person/Place/Thing), scope status (In Scope / Excluded), exclusion rationale (if excluded), and inheritance provider (if set).
3. **Given** the ISSM wants to add an Azure-discovered asset to a boundary, **When** they click "Assign Component," **Then** they pick from the component library (which contains Azure-discovered components) rather than entering raw Azure resource details.

---

### User Story 5 — Data Migration from AuthorizationBoundary to SystemComponent (Priority: P2)

As a system administrator, when the application is upgraded, existing rows in the AuthorizationBoundary table are automatically migrated. Each resource row becomes a new org-wide "Thing" SystemComponent with Azure fields populated. The boundary-component relationship preserves the original scope status (in-scope or excluded), exclusion rationale, and inheritance provider. The original AuthorizationBoundary table is retained but marked deprecated; no new records are written to it.

**Why this priority**: Migration must run before the simplified UI (Story 3) can hide the old resource tab, but it does not block discovery (Story 1) or the new assignment model (Story 2).

**Independent Test**: Can be fully tested by seeding the database with five AuthorizationBoundary rows (three in-scope, two excluded with rationale), running the migration, and verifying five new SystemComponent records exist with correct Azure fields and five corresponding boundary-component assignments with correct scope status.

**Acceptance Scenarios**:

1. **Given** the AuthorizationBoundary table contains resource rows with Azure details, **When** the migration runs, **Then** a new "Thing" SystemComponent is created for each unique resource ID with AzureResourceId, AzureResourceType, AzureResourceGroup, and AzureLocation populated.
2. **Given** the same Azure resource ID appears in multiple AuthorizationBoundary rows (across boundaries), **When** the migration runs, **Then** only one SystemComponent is created for that resource, and one boundary-component assignment per original row is created.
3. **Given** an AuthorizationBoundary row has IsInBoundary = false and an ExclusionRationale, **When** the migration runs, **Then** the corresponding boundary-component assignment has scope = Excluded with the original rationale preserved.
4. **Given** the migration completes, **When** the system starts normally, **Then** no new writes occur to the AuthorizationBoundary table; it is kept read-only for backward compatibility.

---

### User Story 6 — Component-Level Assessment Findings (Priority: P1)

As an ISSM, when I run a compliance assessment against a system, findings are automatically linked to the components whose Azure resources they affect. On the Assessment detail view, I can filter and group findings by component — seeing which specific component (e.g., "SQL Database" or "App Service") has which gaps, with per-component risk summaries showing open finding count, highest severity, and overdue remediation count. On the Remediation page, remediation tasks and POA&M items also display their associated component, so the team knows exactly which asset to fix. This enables targeted remediation: I know exactly which asset has which compliance issues, rather than sifting through a flat list of findings by control family alone.

**Why this priority**: Assessment-to-component linkage is the core value of having Azure resource IDs on components. Without it, the ISSM gains no new insight from importing Azure resources — findings remain disconnected from the asset inventory. This is critical for RMF Assess (Phase 4) and Monitor (Phase 6) workflows where the SCA and ISSM need per-asset risk visibility.

**Independent Test**: Can be fully tested by importing an Azure resource as a component, running an assessment that produces findings for that resource's ARM ID, and verifying the Assessment detail view shows per-component risk summaries with the correct finding count and severity, and that the Remediation page displays the associated component name on linked remediation tasks.

**Acceptance Scenarios**:

1. **Given** a system has Azure-backed "Thing" components and a compliance assessment is run, **When** findings are generated with Azure resource IDs, **Then** each finding is automatically linked to the matching component (by matching `ComplianceFinding.ResourceId` to `SystemComponent.AzureResourceId`).
2. **Given** a component has linked findings, **When** the ISSM views the Assessment detail view, **Then** a per-component risk summary shows: open finding count, highest severity (Critical/High/Medium/Low), and overdue remediation count.
3. **Given** a component has linked findings, **When** the ISSM views the Remediation page, **Then** remediation tasks and POA&M items display their associated component name so the team knows which asset to fix.
4. **Given** a component has no findings (e.g., a "Person" or "Place" component without an Azure resource), **When** the ISSM views the Assessment detail view, **Then** that component does not appear in the per-component breakdown.
5. **Given** the ISSM opens the Assessment detail view, **When** they filter by a specific component, **Then** only findings linked to that component's Azure resource ID are displayed.
6. **Given** a finding's Azure resource ID does not match any component, **When** the assessment results are displayed, **Then** those findings appear under an "Unlinked Resources" section with a prompt to import the resource as a component.
7. **Given** the ISSM imports an unlinked resource as a component after assessment, **When** the findings view refreshes, **Then** the previously unlinked findings are now linked to the newly created component.

---

### User Story 7 — NIST P-16 to P-17 Workflow Alignment (Priority: P3)

As an ISSM following the NIST SP 800-37 Rev 2 Prepare step, the system guides me to complete asset identification (Task P-16) before authorization boundary definition (Task P-17). The Component Library is positioned as the P-16 step — populate your asset inventory first. Boundary definitions and component assignment are the P-17 step — define boundaries and assign your inventoried assets.

**Why this priority**: This is a workflow/UX guidance improvement that enhances compliance posture but does not block the core data model changes.

**Independent Test**: Can be fully tested by navigating a fresh system setup and verifying the system prompts or guides the user to the Component Library before allowing boundary definition.

**Acceptance Scenarios**:

1. **Given** a newly registered system with no components, **When** the ISSM navigates to Boundary Management, **Then** the system displays a guidance message directing them to populate the Component Library first (P-16 before P-17).
2. **Given** the ISSM has populated components in the library, **When** they navigate to Boundary Management, **Then** the guidance message no longer appears and they can proceed to define boundaries and assign components.

---

### User Story 8 — Documentation Alignment (Priority: P2)

As a documentation consumer (ISSM, ISSO, SCA, or Engineer), the user-facing guides and getting-started pages accurately reflect the implemented component-centric workflow. Documentation was pre-updated ahead of implementation to establish the correct NIST P-16 → P-17 task order (components before boundaries), clarify that per-component risk summaries appear on Assessment and Remediation pages (not the Components page), and add the "Identify System Components" step to the ISSM workflow. After implementation, the documentation MUST be validated against the actual UI and API behavior to ensure accuracy.

**Why this priority**: Incorrect or out-of-sync documentation undermines user trust and causes workflow errors. Since docs were updated before code, they must be verified post-implementation.

**Independent Test**: Can be validated by walking through each updated guide step-by-step in the running application and confirming every described action, navigation path, and expected result matches the actual behavior.

**Pre-Updated Documentation Files**:

- `docs/guides/issm-guide.md` — Added Step 2 (Identify System Components) before boundary definition; updated workflow overview; renumbered Steps 3–6; added NIST P-16/P-17 info callout and component-assessment linkage tip
- `docs/getting-started/issm.md` — Added Step 2 (Identify System Components) with Discover from Azure guidance; renumbered boundary to Step 3, roles to Step 4; added NIST task order info callout
- `docs/guides/component-inventory.md` — Added Assessment & Remediation Linkage section explaining that per-component risk summaries appear on Assessment detail view and Remediation page, not the Components page
- `docs/guides/compliance-dashboard.md` — Added Risk Visibility note under Component Inventory section clarifying where risk summaries are displayed

**Acceptance Scenarios**:

1. **Given** the ISSM guide describes a "Step 2: Identify System Components" action, **When** a user follows the guide in the running application, **Then** the Components page exists at the described path and supports the documented actions (add component, Discover from Azure).
2. **Given** the getting-started ISSM page describes discovering Azure resources as Step 2, **When** a new ISSM follows the quick-start flow, **Then** the system supports the described Discover from Azure workflow before boundary definition.
3. **Given** the component-inventory guide states risk summaries appear on Assessment and Remediation pages, **When** a user navigates to the Assessment detail view after running an assessment, **Then** per-component risk summaries are visible as documented.
4. **Given** the compliance-dashboard guide states "Per-component risk summaries are displayed on the Assessment detail view and Remediation page," **When** a user navigates to the Remediation page, **Then** remediation tasks show the associated component name as documented.
5. **Given** all four documentation files were updated pre-implementation, **When** implementation is complete, **Then** a documentation review pass confirms no stale references to old workflow order or incorrect page locations for risk summaries.

---

### User Story 9 — Entra ID Discovery for Person Components (Priority: P3)

As an ISSO managing the org-wide Component Library, I can optionally click "Discover from Entra ID" to import users and groups as "Person" components. This feature is gated behind an organization-level setting (disabled by default) since some organizations do not permit Entra ID lookups. When enabled, the system queries Microsoft Graph API for directory users/groups and displays them for selection and import. This is available only on the org-wide Component Library page, not at the system level.

**Why this priority**: This is a convenience feature for organizations that permit Entra ID integration. It accelerates Person component population but is not required for the core component-centric boundary model. Manual entry remains the fallback for organizations that do not enable this setting.

**Independent Test**: Can be fully tested by enabling the org setting, clicking "Discover from Entra ID" on the Component Library page, importing two users, and verifying two org-wide "Person" components are created. When the setting is disabled, the button should not appear.

**Acceptance Scenarios**:

1. **Given** the organization-level Entra ID discovery setting is enabled, **When** an ISSO navigates to the org-wide Component Library, **Then** a "Discover from Entra ID" button is visible alongside "Discover from Azure."
2. **Given** the setting is disabled (default), **When** the ISSO navigates to the Component Library, **Then** the "Discover from Entra ID" button is not displayed.
3. **Given** the ISSO clicks "Discover from Entra ID," **When** results load, **Then** the system displays Entra ID users and groups with name, email/UPN, and type (User/Group).
4. **Given** discovered users are displayed, **When** the ISSO selects three users and clicks "Import Selected," **Then** three org-wide "Person" components are created with names matching the Entra ID display names.
5. **Given** a user was already imported as a Person component, **When** Entra ID discovery runs again, **Then** that user displays an "Already imported" badge and is excluded from bulk import by default.

---

### Edge Cases

- What happens when an Azure resource is deleted in Azure after being imported as a component? The component remains as-is (stale); a future re-discovery flags it with a "Not found in Azure" indicator.
- What happens when a component assigned to a boundary is deleted from the org-wide library? The boundary assignment is cascade-deleted; the boundary view reflects the removal immediately.
- What happens when the migration encounters an AuthorizationBoundary row with a resource ID that already matches an existing SystemComponent? The migration links the existing component rather than creating a duplicate.
- What happens when the ISSO lacks Azure credentials? The "Discover from Azure" button is disabled with a tooltip explaining that Azure credentials are required.
- What happens when Azure discovery API calls fail, time out, or return partial results? The system displays whatever results were successfully retrieved alongside a warning banner identifying which resource groups or subscriptions failed. A "Retry Failed" button allows the user to re-attempt only the failed portions without re-scanning successful groups.
- What happens when an ISSM sets a component to "Excluded" without providing a rationale? The save is blocked; a rationale is mandatory for excluded components.
- What happens when a compliance assessment produces findings for an Azure resource that is not yet a component? The findings are stored normally with their ResourceId; they appear under "Unlinked Resources" in the assessment view until the resource is imported as a component.
- What happens when a component's AzureResourceId is changed after findings were linked? Existing finding links (ComponentId references) remain intact. A "Re-link Findings" action is available on the component's detail panel in the Components page, which re-runs the ResourceId → AzureResourceId matching logic for all findings in the same system. This also triggers automatically on the next assessment run.
- What happens when a STIG/SCAP import or Prisma Cloud import introduces findings with resource IDs? The same ComponentId resolution logic applies — matching ResourceId to AzureResourceId to auto-link findings to components.
- What happens if the data migration fails partway through? The entire migration is wrapped in a single DB transaction; any failure rolls back all changes, leaving the database in its pre-migration state. The migration can then be re-run after the issue is resolved.
- What happens when two users attempt to edit boundary-component assignments simultaneously? The first user acquires a lock on the boundary; the second user sees a message (e.g., "This boundary is currently being updated by [user]") and cannot save changes until the lock is released.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: SystemComponent MUST support optional Azure resource fields: AzureResourceId, AzureResourceType, AzureResourceGroup, and AzureLocation.
- **FR-002**: The Component Library page MUST provide a "Discover from Azure" action that scans a selected Azure subscription and displays discovered resources.
- **FR-002a**: If Azure discovery partially fails (e.g., timeout or throttling on specific resource groups), the system MUST display successfully retrieved results with a warning banner identifying the failed resource groups/subscriptions and provide a "Retry Failed" action to re-attempt only the failed portions.
- **FR-003**: Azure discovery results MUST be filterable by resource group, resource type, and text search on resource name.
- **FR-004**: Azure discovery MUST flag resources that already exist as SystemComponents (by matching AzureResourceId) and skip them during bulk import.
- **FR-005**: Bulk import MUST create one org-wide "Thing" SystemComponent per selected Azure resource with all four Azure fields populated.
- **FR-005a**: The org-wide Component Library page MUST optionally support a "Discover from Entra ID" action (for Person components) that imports users/groups from Microsoft Entra ID as org-wide "Person" components. This feature MUST be gated behind an organization-level setting (disabled by default) since some organizations will not permit Entra ID lookups. Place components remain manual-entry only.
- **FR-006**: The system MUST support a boundary-component relationship entity that tracks: scope status (In Scope or Excluded), exclusion rationale (required when Excluded), and inheritance provider (optional).
- **FR-007**: The same SystemComponent MUST be assignable to multiple boundaries with independent scope status per boundary.
- **FR-008**: The Boundary Management page MUST present a unified component assignment view instead of separate resource and component tabs.
- **FR-009**: The component assignment view MUST allow the ISSM to assign components, toggle include/exclude status, enter exclusion rationale, and set inheritance provider.
- **FR-010**: A data migration MUST convert existing AuthorizationBoundary resource rows into SystemComponent records and corresponding boundary-component assignments, preserving scope status, rationale, and inheritance provider.
- **FR-011**: The migration MUST deduplicate by Azure resource ID — one SystemComponent per unique resource, with multiple boundary-component assignments if the resource existed in multiple boundaries.
- **FR-012**: The AuthorizationBoundary table MUST be retained for backward compatibility but MUST NOT receive new inserts from application code after migration.
- **FR-013**: The system MUST display a P-16 guidance message on the Boundary Management page when no components exist in the library, directing the user to populate assets before defining boundaries.
- **FR-014**: Removing a component from a boundary MUST delete only the boundary-component assignment, not the component itself.
- **FR-015**: Toggling a component to "Excluded" MUST require a non-empty exclusion rationale before persisting.
- **FR-016**: The system-level Components page MUST provide a "Discover from Azure" action that scans the Azure subscription linked to that system and creates system-scoped "Thing" components (with RegisteredSystemId set) for selected resources.
- **FR-016a**: When a user begins editing boundary-component assignments, the system MUST acquire a pessimistic lock on that boundary. Other users attempting to edit the same boundary MUST see a notification message identifying the current editor (e.g., "This boundary is currently being updated by [user]"). The lock MUST be released when the editing user saves or navigates away.
- **FR-017**: System-level Azure discovery MUST detect when a discovered resource already exists as an org-wide component and offer to assign the existing org component to the system instead of creating a duplicate.
- **FR-018**: System-level Azure discovery MUST detect when a discovered resource already exists as a system-scoped component for this system and flag it as "Already imported."
- **FR-019**: ComplianceFinding MUST support an optional ComponentId field that links a finding to the SystemComponent whose AzureResourceId matches the finding's ResourceId.
- **FR-020**: When a compliance assessment is run or findings are imported, the system MUST automatically resolve ComponentId by matching each finding's ResourceId against SystemComponent.AzureResourceId within the same system.
- **FR-021**: The Assessment detail view MUST display per-component risk summaries (open finding count, highest severity, overdue remediation count) and support filtering and grouping findings by component.
- **FR-022**: The Remediation page MUST display the associated component name on remediation tasks and POA&M items that are linked to findings with a resolved ComponentId.
- **FR-023**: Findings whose ResourceId does not match any SystemComponent MUST be displayed under an "Unlinked Resources" section with an option to import the resource as a component.
- **FR-024**: When a new component is created with an AzureResourceId that matches existing unlinked findings, the system MUST retroactively link those findings to the component.
- **FR-025**: All pre-updated documentation files (ISSM guide, getting-started ISSM, component-inventory guide, compliance-dashboard guide) MUST be validated against the implemented UI and API after feature completion, and any discrepancies MUST be corrected before the feature is marked as done.
- **FR-026**: When Azure discovery is re-run against a subscription, resources that were previously imported as components but no longer exist in Azure MUST be flagged with a "Not found in Azure" indicator on the discovery results. The component itself is not deleted or modified.
- **FR-027**: The component detail panel MUST provide a "Re-link Findings" action that re-runs the ComponentId resolution logic (matching ComplianceFinding.ResourceId to SystemComponent.AzureResourceId) for all findings in the same system. This action is useful after a component's AzureResourceId is changed.

### Key Entities

- **SystemComponent**: The single source of truth for all assets (People, Places, Things). Extended with optional Azure resource fields (AzureResourceId, AzureResourceType, AzureResourceGroup, AzureLocation) to represent discovered Azure resources. Org-wide when RegisteredSystemId is null; system-specific otherwise.
- **BoundaryComponentAssignment** (new): Join entity linking a SystemComponent to an AuthorizationBoundaryDefinition within a RegisteredSystem. Tracks per-boundary scope: InScope or Excluded, exclusion rationale, and inheritance provider. Replaces the scope-tracking role of the AuthorizationBoundary entity.
- **AuthorizationBoundaryDefinition**: The boundary container (e.g., "Production," "Dev/Test"). Unchanged in structure; its resource navigation shifts from AuthorizationBoundary rows to BoundaryComponentAssignment records.
- **AuthorizationBoundary** (deprecated): Legacy resource-tracking entity. Retained read-only for backward compatibility. Not modified; no new rows written after migration.
- **ComplianceFinding** (extended): Existing finding entity that records assessment/scan results per NIST control. Extended with an optional ComponentId FK that links findings to the SystemComponent whose AzureResourceId matches the finding's ResourceId. This enables per-component risk visibility across assessments, STIG imports, Prisma Cloud imports, and ACAS/Nessus scans.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An ISSO can discover and import 50 Azure resources as components in under 3 minutes through the Component Library page.
- **SC-001a**: An ISSM can discover and import 20 Azure resources as system-scoped components in under 2 minutes through the system-level Components page.
- **SC-002**: An ISSM can assign a component to a boundary and set its scope status (In Scope or Excluded) in under 30 seconds.
- **SC-003**: 100% of existing AuthorizationBoundary resource records are migrated to SystemComponent records with correct boundary-component assignments after upgrade.
- **SC-003a**: The data migration completes in under 60 seconds for a database with 1,000 existing AuthorizationBoundary resource rows.
- **SC-004**: The same component can appear in two different boundaries with opposite scope statuses (In Scope in one, Excluded in the other), and each boundary view correctly reflects its own status.
- **SC-005**: The Boundary Management page no longer exposes raw Azure resource entry; all boundary assets are managed exclusively through component assignment.
- **SC-006**: A system with no components in the library displays a P-16 to P-17 guidance prompt on the Boundary Management page, directing the user to populate assets before defining boundaries.
- **SC-007**: After running an assessment on a system with 10 Azure-backed components, at least 90% of findings with matching Azure resource IDs are automatically linked to the correct component.
- **SC-008**: The Assessment detail view displays per-component risk summaries and supports filtering findings by component within 5 seconds of page load.
- **SC-009**: The Remediation page displays the associated component name on remediation tasks linked to findings with a resolved ComponentId.
- **SC-010**: All four pre-updated documentation files accurately describe the implemented workflow, navigation paths, and feature behavior with zero stale references to old workflow order or incorrect risk summary locations.

## Assumptions

- The existing AzureResourceDiscoveryService (which uses Azure Resource Graph) will be reused for the Component Library discovery feature, extending it to return data suitable for component creation rather than boundary resource creation.
- The Component Library page already supports org-wide CRUD for components; the Azure discovery flow is an additional creation path alongside manual entry.
- The ComponentSystemAssignment join entity already exists for linking org-wide components to systems with boundary scope. The new BoundaryComponentAssignment entity replaces or extends this to carry include/exclude semantics at the boundary level.
- Azure credentials are available through the existing Azure Identity integration; no new authentication flows are needed.
- Entra ID discovery for Person components requires Microsoft Graph API read permissions (User.Read.All or similar). Organizations that do not grant these permissions will have the Entra ID discovery feature disabled via the organization setting.
- Performance targets assume a standard web application on commodity hardware; no specialized infrastructure is required.
- Data migration runs once at upgrade time, is idempotent (safe to re-run), and executes within a single database transaction that rolls back entirely on any failure.
