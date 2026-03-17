# Feature Specification: Boundary-Scoped Model

**Feature Branch**: `033-boundary-scoped-model`  
**Created**: 2026-03-15  
**Status**: Draft  
**Input**: User description: "Restructure the data model to make authorization boundaries the primary organizing container for capabilities and components. Components and capability-to-control mappings should be tied to specific authorization boundaries rather than just systems. Support systems with multiple authorization boundaries. Update gap analysis to be boundary-scoped. Update dashboard UI and chat flows to use boundaries as the organizing unit."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Define an Authorization Boundary (Priority: P1)

An ISSO or ISSM navigates to a system's detail page and creates one or more named authorization boundaries. Each boundary represents a distinct security perimeter — for example, "Eagle Eye Production" and "Eagle Eye Dev/Test." The boundary includes a description, a boundary type (physical, logical, or hybrid), and a list of in-scope resources. The system's existing `AuthorizationBoundary` resource records are migrated to the system's default boundary automatically.

**Why this priority**: The boundary is the foundational container. Nothing else in this feature works without a named, manageable boundary entity.

**Independent Test**: Navigate to System Detail → click "Manage Boundaries" → create a new boundary with name, type, and description → see it listed alongside any existing default boundary → verify the system's pre-existing boundary resources appear under the default boundary.

**Acceptance Scenarios**:

1. **Given** a registered system with no named boundaries, **When** the feature is deployed and migrations run, **Then** a default boundary named "[System Name] — Primary" is created and all existing `AuthorizationBoundary` resource records are reassigned to it.
2. **Given** a system with one default boundary, **When** the user creates a second boundary, **Then** both boundaries appear in a list with their resource counts.
3. **Given** a boundary, **When** the user adds or removes resources, **Then** the resource list updates and the boundary summary (resource count, resource types) reflects the change.
4. **Given** a boundary with linked components and capability mappings, **When** the user attempts to delete it, **Then** the system warns about downstream impacts and requires confirmation.

---

### User Story 2 — Assign Components to a Boundary (Priority: P1)

When adding or editing a system component (Person, Place, or Thing), the user selects which authorization boundary the component belongs to. The Component Inventory page groups components by boundary first, then by type (People/Places/Things) within each boundary. Existing components are assigned to the system's default boundary during migration.

**Why this priority**: Components represent the assets, personnel, and locations inside a security perimeter. Tying them to a boundary gives the SSP Appendix A and §11 accurate, boundary-specific inventories.

**Independent Test**: Navigate to Component Inventory → see components grouped under boundary headings → add a new component and select a boundary → verify it appears under the correct boundary section.

**Acceptance Scenarios**:

1. **Given** a system with two boundaries, **When** the user opens the Add Component form, **Then** a boundary selector appears listing both boundaries.
2. **Given** an existing component assigned to the default boundary, **When** the user edits it and selects a different boundary, **Then** the component moves to the new boundary section.
3. **Given** components spread across two boundaries, **When** the user views the Component Inventory page, **Then** components are grouped by boundary (collapsible sections), with People/Places/Things subsections within each.
4. **Given** a component linked to capabilities, **When** the component is reassigned to a different boundary, **Then** the capability linkages are preserved and gap analysis for both boundaries updates.

---

### User Story 3 — Scope Capability Mappings to a Boundary (Priority: P1)

When mapping a security capability to controls, the user can scope the mapping to a specific authorization boundary rather than the entire system. Org-wide mappings (no system or boundary specified) continue to apply to all boundaries. Boundary-scoped mappings override org-wide mappings when both exist for the same control. The Capability Library MappingPanel shows which boundary (or "All Systems") each mapping applies to.

**Why this priority**: The same capability may protect different controls in different parts of a system. Boundary-scoped mappings ensure narratives and gap analysis reflect the actual security posture of each perimeter.

**Independent Test**: Open a capability's MappingPanel → add a control mapping with boundary "Eagle Eye Production" → verify the mapping appears with a boundary label → run gap analysis for that boundary and confirm the control is counted as covered.

**Acceptance Scenarios**:

1. **Given** a capability with an org-wide mapping for AC-2, **When** the user views gap analysis for any boundary, **Then** AC-2 shows as covered in all boundaries.
2. **Given** a capability with a boundary-specific mapping for IA-2 on "Production," **When** the user views gap analysis for "Dev/Test," **Then** IA-2 is NOT covered by that mapping (only by any org-wide mapping if one exists).
3. **Given** an org-wide mapping and a boundary-specific mapping for the same control, **When** the system resolves coverage, **Then** the boundary-specific mapping takes precedence for narrative generation.
4. **Given** a mapping scoped to a boundary, **When** the user views the MappingPanel, **Then** a boundary badge shows the boundary name (or "All Systems" for org-wide).

---

### User Story 4 — Boundary-Scoped Gap Analysis (Priority: P2)

The Gap Analysis page adds a boundary selector. When a boundary is selected, coverage ratios, the coverage matrix, and unmapped control lists reflect only the mappings applicable to that boundary (boundary-specific + org-wide). A summary view shows all boundaries side-by-side with their overall coverage percentage for quick comparison.

**Why this priority**: Without boundary-scoped gap analysis, systems with multiple boundaries see a blended coverage number that doesn't reflect the real posture of each perimeter.

**Independent Test**: Navigate to Gap Analysis → select "Production" boundary from the selector → verify coverage % changes from the "All" view → switch to "Dev/Test" → verify different coverage numbers → view the boundary comparison summary.

**Acceptance Scenarios**:

1. **Given** a system with two boundaries and different capability mappings, **When** the user selects each boundary in the gap analysis view, **Then** the coverage matrix shows different numbers for each.
2. **Given** the "All Boundaries" view is selected, **When** the user views gap analysis, **Then** coverage reflects the union of all boundary-specific and org-wide mappings (current behavior).
3. **Given** a boundary with zero capability mappings (only org-wide apply), **When** the user views its gap analysis, **Then** coverage reflects only org-wide mappings.
4. **Given** the boundary comparison summary, **When** displayed, **Then** each boundary shows total controls, covered count, gap count, and coverage % in a compact table.

---

### User Story 5 — Boundary-Aware Narrative Propagation (Priority: P2)

When a capability is updated, the "change once, update everywhere" narrative propagation respects boundary scoping. Narratives linked to a boundary-scoped mapping update only for that boundary's control implementations. The update response indicates how many narratives were updated per boundary.

**Why this priority**: Without boundary awareness, updating a capability could incorrectly regenerate narratives for boundaries where the capability doesn't apply, or miss boundaries with specific scoping.

**Independent Test**: Update a capability's description that has boundary-scoped mappings → verify only narratives for controls within the mapped boundaries are regenerated → verify narratives in other boundaries are untouched.

**Acceptance Scenarios**:

1. **Given** a capability mapped to IA-2 only in "Production," **When** the capability description is updated, **Then** the IA-2 narrative is regenerated only for the Production boundary, not Dev/Test.
2. **Given** a capability with an org-wide mapping, **When** updated, **Then** narratives across all boundaries are regenerated (current behavior preserved).
3. **Given** a capability with both org-wide and boundary-specific mappings, **When** updated, **Then** the response shows per-boundary update counts.
4. **Given** a customized narrative in a boundary where the capability changed, **When** propagation runs, **Then** the customized narrative is preserved and an audit event is logged.
5. **Given** a control (e.g., IA-2) mapped to different capabilities in two boundaries (e.g., Entra ID MFA in Production, Duo MFA in Dev/Test), **When** composite narrative generation runs, **Then** the resulting narrative contains per-boundary sections: "Within the Production boundary, Entra ID MFA provides multi-factor authentication for… Within the Dev/Test boundary, Duo MFA provides multi-factor authentication for…"

---

### User Story 6 — Boundary Management in Chat Channels (Priority: P2)

Users can manage boundaries through the VS Code extension and Teams bot via natural language. The compliance agent supports queries like "list boundaries for Eagle Eye," "what's the gap analysis for Eagle Eye Production?," and "add Entra ID MFA to the Production boundary for AC-2."

**Why this priority**: Chat-based workflows are a core experience for engineers and ISSOs who don't always use the dashboard. Boundary-awareness in chat keeps the mental model consistent.

**Independent Test**: In VS Code, type `@ato /compliance list boundaries for Eagle Eye` → receive a list of boundaries with resource counts → type `@ato /compliance what's the coverage for Eagle Eye Production?` → receive boundary-scoped gap analysis.

**Acceptance Scenarios**:

1. **Given** a system with two boundaries, **When** the user asks "list boundaries for Eagle Eye," **Then** the agent returns both boundary names with resource and component counts.
2. **Given** a boundary-scoped query, **When** the user asks "gap analysis for Eagle Eye Production," **Then** the agent returns coverage specific to that boundary.
3. **Given** a capability, **When** the user says "map Entra ID MFA to AC-2 for Eagle Eye Production boundary," **Then** the mapping is created with the correct boundary scope.

---

### User Story 7 — SSP §11 Auto-Generation from Boundary (Priority: P3)

The SSP auto-generation feature (Feature 022) uses boundary-organized data to produce the authorization boundary section (§11). The generated content includes the boundary name, type, description, in-scope resources, components within the boundary, and a resource listing.

**Why this priority**: This is the downstream consumer of the boundary model. It depends on all prior stories being complete and Feature 022 infrastructure being in place.

**Independent Test**: Trigger SSP §11 generation for a system with two boundaries → verify the output contains separate subsections for each boundary with their respective resources and components.

**Acceptance Scenarios**:

1. **Given** a system with two named boundaries, **When** SSP §11 is generated, **Then** the output contains a subsection for each boundary with its name, type, description, and resource table.
2. **Given** a boundary with linked components, **When** §11 is generated, **Then** components appear in the boundary's inventory table grouped by type.
3. **Given** a system with one default boundary (migration state), **When** §11 is generated, **Then** the output renders a single boundary section (backward compatible with Feature 022).

---

### User Story 8 — Azure Resource Discovery & Auto-Suggest Components (Priority: P3)

An ISSM or engineer managing a system can query Azure Resource Graph to discover resources in the system's subscription. The system auto-suggests creating authorization boundaries based on resource groups or subscriptions, and within each boundary, auto-suggests creating SystemComponent records (type: Thing) for selected resources, pre-populated with the resource name, type, and Azure resource ID. This eliminates manual data entry for cloud-hosted systems and ensures the boundary structure and resource inventory stay aligned with the live Azure environment.

**Why this priority**: This is an accelerator that builds on the completed boundary model. It requires Azure credentials already configured in the environment and is additive — systems without Azure subscriptions continue to work manually.

**Independent Test**: Navigate to a system's boundary management page → click "Discover Azure Resources" → authenticate via existing managed identity → system shows resource groups as suggested boundaries → accept 2 resource group boundaries → see a searchable list of resources within each → select resources → system prompts to create Thing components → confirm → new boundaries and SystemComponent records appear.

**Acceptance Scenarios**:

1. **Given** a boundary with a configured Azure subscription, **When** the user clicks "Discover Azure Resources," **Then** the system queries Azure Resource Graph and displays a searchable, filterable list of resources (VMs, databases, storage accounts, networks, etc.).
2. **Given** discovered Azure resources, **When** the user selects one or more resources, **Then** the system prompts to create SystemComponent records (type: Thing) pre-populated with the resource name, Azure resource type, and resource ID.
3. **Given** a boundary that already contains a resource matching a discovered Azure resource (by resource ID), **When** discovery results are displayed, **Then** the already-added resource is visually marked and excluded from the auto-suggest prompt.
4. **Given** Azure credentials are unavailable or expired, **When** the user clicks "Discover Azure Resources," **Then** the system displays a clear error message explaining the credential requirement and linking to configuration documentation.
5. **Given** a system with a configured Azure subscription containing multiple resource groups, **When** the user initiates discovery, **Then** the system suggests creating an authorization boundary for each resource group (or one per subscription), pre-populated with the resource group name, type "logical," and its contained resources. The user can accept, rename, merge, or skip each suggested boundary.
6. **Given** a suggested boundary name that matches an existing boundary in the system, **When** displayed, **Then** the suggestion is marked as "already exists" and the user can choose to add newly discovered resources to the existing boundary instead.

---

### Edge Cases

- What happens when a system has no boundaries defined? → The system retains a "default" boundary created during migration. All operations work against the default boundary, preserving current behavior.
- What happens when the last resource is removed from a boundary? → The boundary remains as an empty container. The user may delete it if no components or mappings reference it.
- What happens when a boundary is deleted that has components and mappings? → The system auto-reassigns all orphaned components and mappings to the system's Primary boundary. A confirmation dialog shows the count of items being moved. The Primary boundary itself cannot be deleted.
- How does the system handle org-wide mappings when a new boundary is added to a system? → Org-wide mappings automatically apply to the new boundary. No user action needed.
- What happens to the existing `RegisteredSystemId` FK on `CapabilityControlMapping`? → It is retained for backward compatibility. The new `AuthorizationBoundaryDefinitionId` FK is nullable — null means the mapping uses the legacy system-wide scope (equivalent to applying to all boundaries in that system).
- What happens when a component has linked capabilities and is moved between boundaries? → The capability linkages persist. Gap analysis for both the source and destination boundaries updates automatically.
- How does the system handle a system with 10+ boundaries? → The dashboard UI supports scrollable/searchable boundary selectors. Performance targets assume up to 20 boundaries per system.
- Can a boundary have a different control baseline than its parent system? → No. All boundaries inherit the system's baseline. Gap analysis uses the same control set for every boundary; only the capability mappings differ.
- What happens when different boundaries map different capabilities to the same control? → The system generates a composite narrative that references each boundary and its capability (e.g., "In the Production boundary, Entra ID MFA provides… In the Dev/Test boundary, Duo MFA provides…"). If the narrative has been manually customized, it is preserved and an audit event is logged.
- What happens when Azure credentials are unavailable or expired during resource discovery? → The system displays a clear error message explaining the credential requirement and links to configuration documentation. No partial data is loaded.
- What happens when a discovered Azure resource already exists as a boundary resource? → The resource is visually marked as "already added" in the discovery list and excluded from the auto-suggest component creation prompt.
- What happens when the Azure subscription contains thousands of resources? → The discovery UI provides type-based filtering and text search. Results are paginated. The user selects only relevant resources for the boundary.
- What happens when a suggested boundary name from a resource group matches an existing boundary? → The suggestion is marked "already exists." The user can add newly discovered resources to the existing boundary or skip it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support named authorization boundaries within a registered system, each with a unique name, boundary type (physical, logical, hybrid), and description.
- **FR-002**: System MUST create a default boundary named "[System Name] — Primary" during data migration for every existing system, reassigning all existing `AuthorizationBoundary` resource records to it.
- **FR-003**: System MUST allow users to create, edit, and delete authorization boundaries for a system. Deleting a non-Primary boundary auto-reassigns its orphaned components and mappings to the Primary boundary after displaying a confirmation summary. The Primary boundary cannot be deleted.
- **FR-004**: System MUST allow system components (People, Places, Things) to be assigned to a specific authorization boundary.
- **FR-005**: System MUST group components by boundary first, then by type, in the Component Inventory UI.
- **FR-006**: System MUST assign existing components to the system's default boundary during migration.
- **FR-007**: System MUST allow capability-to-control mappings to be scoped to a specific authorization boundary, with org-wide mappings (no boundary specified) applying to all boundaries.
- **FR-008**: System MUST display the boundary scope (boundary name or "All Systems") on each mapping in the MappingPanel.
- **FR-009**: System MUST resolve coverage by combining boundary-specific mappings with org-wide mappings, with boundary-specific taking precedence for narrative generation. When multiple boundaries map different capabilities to the same control, the system MUST generate a composite narrative referencing each boundary and its respective capability.
- **FR-010**: System MUST provide a boundary selector on the Gap Analysis page, filtering coverage ratios to the selected boundary's applicable mappings.
- **FR-011**: System MUST provide a boundary comparison summary showing all boundaries side-by-side with their resource count, component count, and coverage percentage.
- **FR-012**: System MUST scope narrative propagation to respect boundary-specific mappings when a capability is updated. (Distinct from FR-009: FR-009 governs initial coverage resolution and composite narrative generation; FR-012 governs re-generation triggered by capability updates.)
- **FR-013**: System MUST report per-boundary narrative update counts in the capability update response.
- **FR-014**: System MUST expose boundary management operations (list, create, query) through MCP tools accessible from VS Code and Teams chat channels.
- **FR-015**: System MUST generate SSP §11 content organized by authorization boundary, with each boundary listing its resources and components.
- **FR-016**: System MUST preserve backward compatibility — single-boundary systems behave identically to the current system-scoped model with no user action required.
- **FR-017**: System MUST retain the existing `RegisteredSystemId` FK on `CapabilityControlMapping` for backward compatibility, adding an optional `AuthorizationBoundaryDefinitionId` FK.
- **FR-018**: System MUST log boundary-related changes (create, delete, component reassignment, mapping scope changes) as `AuditLogEntry` records in the existing `AuditLogs` table, capturing the action type, affected entity IDs, and the acting user.
- **FR-019**: System MUST display a boundary summary section on the System Detail page showing up to 20 boundaries in a scrollable card grid, each card displaying the boundary name, resource count, component count, and coverage percentage, with a link to manage that boundary.
- **FR-020**: System MUST query Azure Resource Graph using configured managed identity credentials to discover resources in the system's Azure subscription, returning a searchable, filterable list of resources grouped by type.
- **FR-021**: System MUST auto-suggest creating SystemComponent records (type: Thing) for selected Azure resources, pre-populating the component name, sub-type (Azure resource type), and storing the Azure resource ID. Resources already present in the boundary (matched by resource ID) MUST be excluded from the suggestion.
- **FR-022**: System MUST auto-suggest creating authorization boundaries from Azure resource groups (or subscriptions), pre-populating the boundary name from the resource group name, setting type to "logical," and associating the resource group's resources. Users can accept, rename, merge (add newly discovered resources to an existing boundary without removing its current resources), or skip each suggested boundary.

### Key Entities

- **AuthorizationBoundaryDefinition**: A named security perimeter within a registered system. Key attributes: name, boundary type (physical/logical/hybrid), description, registered system reference. One system can have many boundaries.
- **AuthorizationBoundary** (existing): Resource-level records (VMs, databases, networks) now linked to a specific boundary definition rather than directly to the system.
- **SystemComponent** (existing, modified): Gains a reference to the boundary definition it belongs to.
- **CapabilityControlMapping** (existing, modified): Gains an optional reference to a boundary definition for boundary-scoped mappings.
- **ControlImplementation** (existing, unchanged): Narratives remain one per control per system. When a boundary-scoped mapping exists, the narrative text includes boundary context (e.g., "Within the Production boundary...") but the row itself is not duplicated per boundary.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can create and manage multiple authorization boundaries per system within a single session, completing boundary setup in under 5 minutes.
- **SC-002**: Component Inventory correctly groups all components by their assigned boundary, with zero orphaned components after migration.
- **SC-003**: Gap analysis returns different coverage percentages for boundaries with different capability mappings, verified across at least 2 boundaries on a test system.
- **SC-004**: Narrative propagation updates only narratives within the boundary scope of the changed mapping, with zero cross-boundary narrative corruption.
- **SC-005**: Chat-based boundary queries (list, gap analysis, mapping) return the same data as the dashboard UI for the same boundary.
- **SC-006**: Data migration completes without data loss — every existing resource record, component, and mapping is assigned to a default boundary for its system.
- **SC-007**: Systems with a single default boundary exhibit identical behavior to the pre-feature system-scoped model, with no regressions in gap analysis, narrative generation, or UI workflows.

## Clarifications

### Session 2026-03-15

- Q: Do all boundaries within a system share the system's single control baseline, or can each boundary have its own? → A: All boundaries share the system's single control baseline.
- Q: When a boundary is deleted, should orphaned components and mappings be reassigned to a user-selected boundary or auto-moved to Primary? → A: Auto-reassign to the Primary boundary with a confirmation summary.
- Q: Should ControlImplementation narratives be scoped per-boundary or remain one per control per system? → A: One narrative per control per system. The boundary determines which capability sources the narrative, but the narrative itself remains at the system level.
- Q: When multiple boundaries map different capabilities to the same control, how should the system-level narrative handle the conflict? → A: Composite narrative — auto-generated text references each boundary and its respective capability.
- Q: Should the System Detail page show a boundary-level summary alongside system metrics, or only through a dedicated subpage? → A: Add a compact boundary summary section on System Detail showing each boundary's name, resource count, component count, and coverage %.

## Assumptions

- All authorization boundaries within a system share the system's control baseline (Low/Moderate/High). Per-boundary baseline overrides are not supported.
- Most organizations will continue to use a single boundary per system. Multi-boundary support is for edge cases like split production/development environments or systems with distinct physical and logical perimeters.
- The upper bound of boundaries per system is 20. UI and query performance targets are based on this assumption.
- Feature 022 (SSP Full OSCAL) infrastructure will be available before US7 (SSP §11 generation) is implemented.
- Existing org-wide capability mappings will continue to work across all boundaries without re-mapping.
- Azure Resource Graph API access requires pre-configured managed identity credentials (`ATO_AZUREAD__TENANTID`, `ATO_GATEWAY__AZURE__SUBSCRIPTIONID`). US8 does not provision or manage these credentials.

## Scope Boundaries

- **In scope**: Boundary CRUD, component-to-boundary assignment, mapping-to-boundary scoping, boundary-scoped gap analysis, narrative propagation, chat integration, SSP §11 generation, Azure resource discovery with auto-suggest boundary and component creation (from resource groups/subscriptions).
- **Out of scope**: Boundary diagrams or visual topology editors. Multi-system shared boundaries (a boundary belongs to exactly one system). Non-Azure cloud providers (AWS, GCP).
