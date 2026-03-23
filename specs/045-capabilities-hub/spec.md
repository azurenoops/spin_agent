# Feature Specification: Unified Security Capabilities Hub

**Feature Branch**: `045-capabilities-hub`  
**Created**: 2026-03-22  
**Status**: Draft  
**Input**: User description: "Unified Security Capabilities Hub — Elevate the Security Capabilities page to be the single source of truth for defining where control inheritance comes from."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — CSP Profile Import via Capabilities Page (Priority: P1)

A compliance officer needs to onboard a cloud service provider's security posture into the organization. They navigate to the Security Capabilities page, click "Import CSP Profile," select the Azure Government FedRAMP High profile, and see a preview showing how many components, capabilities, control mappings, and affected systems will be created. After confirming, the system creates the full pipeline — components (e.g., Entra ID, Key Vault), capabilities grouped by provider and category, component-to-capability links, control mappings, org inheritance designations, and enriched SSP narratives — in one operation.

**Why this priority**: This is the highest-value integration. CSP profile import is the most common onboarding path and currently creates raw ControlInheritance records without reusable capabilities or component traceability. Fixing this single flow eliminates the largest gap in the 3-layer model.

**Independent Test**: Can be fully tested by importing a CSP profile on the Capabilities page and verifying that components, capabilities, mappings, inheritance designations, and narratives all exist with correct linkages.

**Acceptance Scenarios**:

1. **Given** a user is on the Capabilities page with no existing capabilities, **When** they click "Import CSP Profile" and select a profile, **Then** a preview dialog displays the count of components, capabilities, control mappings, and systems that will be affected.
2. **Given** a user confirms the import preview, **When** the import completes, **Then** a component (type: Thing) is created for each unique CSP service (e.g., "Microsoft Entra ID"), capabilities are created grouped by provider+category, component-to-capability links are established, and control mappings are created for each control in the profile.
3. **Given** a CSP profile is imported, **When** a component or capability with the same name and provider already exists, **Then** the system adds new mappings to the existing records instead of creating duplicates.
4. **Given** a CSP profile import completes, **When** the system processes the results, **Then** org inheritance derivation runs automatically, inheritance cascades to all active systems, and enriched SSP narratives are auto-generated with component context.

---

### User Story 2 — Capabilities Page Coverage Dashboard (Priority: P2)

A security manager opens the Capabilities page and immediately sees summary cards at the top: Total Capabilities, Mapped Controls, Gap Controls (unmapped), and Coverage %. Each capability card shows linked component names as badges alongside control mappings and the count of systems using it. A contextual header explains the 3-layer model (Components → Capabilities → Inheritance).

**Why this priority**: Visibility into coverage gaps is the primary reason users visit this page. Without a dashboard showing what's covered and what's missing, users cannot make informed decisions about where to direct effort.

**Independent Test**: Can be verified by creating several capabilities with varying control mappings and confirming the summary cards display accurate counts and percentages.

**Acceptance Scenarios**:

1. **Given** the Capabilities page loads, **When** capabilities and mappings exist, **Then** summary cards display Total Capabilities count, Mapped Controls count, Gap Controls count, and Coverage % (mapped / total baseline controls).
2. **Given** a capability has linked components, **When** the capability card renders, **Then** component names appear as badges on the card alongside control mappings and system count.
3. **Given** the Capabilities page loads, **When** rendered, **Then** a contextual header explains the relationship between Components, Capabilities, and Control Inheritance.

---

### User Story 3 — CRM Import via Capabilities Page (Priority: P3)

A compliance analyst has a CRM spreadsheet (CSV or Excel) from another organization or a vendor. They navigate to the Capabilities page, click "Import CRM," upload the file, and map columns. The system groups rows by provider to create capabilities, creates a component (Thing) per unique provider, links components to capabilities, creates control mappings, and triggers org inheritance derivation. A preview dialog shows what will be created before applying.

**Why this priority**: CRM import is the second most common import path. Routing it through capabilities ensures consistent traceability, but it depends on the same infrastructure as CSP import (Story 1).

**Independent Test**: Can be verified by uploading a CRM CSV with multiple providers and confirming that components, capabilities, mappings, and inheritance records are correctly created and linked.

**Acceptance Scenarios**:

1. **Given** a user clicks "Import CRM" on the Capabilities page, **When** they upload a CSV/Excel file and map columns, **Then** the system parses the file and displays a preview of components, capabilities, and control mappings that will be created.
2. **Given** CRM rows contain multiple providers, **When** the import processes, **Then** one component (type: Thing) is created per unique provider and one capability is created per provider + NIST family grouping (e.g., "Azure / Access Control", "Azure / Audit and Accountability").
3. **Given** CRM import completes, **When** the system processes results, **Then** component-to-capability links are created, control mappings are established, and org inheritance derivation runs automatically.
4. **Given** a CRM contains a provider that matches an existing component, **When** the import runs, **Then** the existing component is reused and new mappings are added to it.

---

### User Story 4 — Link Components to Capabilities (Priority: P4)

A security engineer wants to associate existing components (People/Places/Things) with a capability to establish traceability. On the Capabilities page, they expand a capability card and click "Link Components," which opens a modal showing available components. They select one or more components and save, creating ComponentCapabilityLink records. The capability card immediately updates to show the linked component badges.

**Why this priority**: Manual component linking is essential for organizations that don't use CSP or CRM import but still need the Component → Capability traceability chain. It completes the manual workflow path.

**Independent Test**: Can be verified by creating a component and a capability independently, then linking them via the modal and confirming the link appears on the capability card.

**Acceptance Scenarios**:

1. **Given** a capability exists with no linked components, **When** the user clicks "Link Components" on the capability card, **Then** a modal displays available components (filterable by type and name).
2. **Given** the component picker modal is open, **When** the user selects components and saves, **Then** ComponentCapabilityLink records are created and the capability card updates to show component badges.
3. **Given** a component is already linked to a capability, **When** the user opens the component picker, **Then** that component appears as pre-selected and cannot be duplicated.

---

### User Story 5 — Control Inheritance Page Simplification (Priority: P5)

A compliance officer visits the Control Inheritance page and sees a cross-link banner at the top explaining that designations are derived from Security Capabilities, with a link to manage capabilities. The CSP Profile and CRM Import buttons have been removed — all import actions now live exclusively on the Capabilities page. Org default tooltips in the inheritance table now show which components back the source capability.

**Why this priority**: Simplifying the Inheritance page prevents users from bypassing the capabilities pipeline. It depends on the import flows being available on the Capabilities page first (Stories 1 and 3).

**Independent Test**: Can be verified by navigating to the Control Inheritance page and confirming the banner, dropdown relocation, and component context tooltips render correctly.

**Acceptance Scenarios**:

1. **Given** the Control Inheritance page loads, **When** rendered, **Then** a cross-link banner displays: "Designations derived from Security Capabilities. [Manage Capabilities →]" with a working link to the Capabilities page.
2. **Given** the Control Inheritance page loads, **When** the user looks for CSP Profile and CRM Import actions, **Then** those buttons are not present — the cross-link banner directs users to the Capabilities page as the sole import path.
3. **Given** a control has an org default designation backed by a capability, **When** the user hovers over the org default indicator, **Then** a tooltip shows which components back the source capability.

---

### User Story 6 — Component Page Capability Coverage (Priority: P6)

A system administrator views a component on the Components page and can see how many capabilities and controls that component supports. For Thing-type components, a "Create Capability from Component" quick action pre-fills a new capability form with the component's name and provider.

**Why this priority**: This provides reverse traceability (Component → Capability) and a convenient shortcut, but only adds value once the capability pipeline is fully operational.

**Independent Test**: Can be verified by viewing a component that has linked capabilities and confirming coverage counts display, then using the quick action to create a pre-filled capability.

**Acceptance Scenarios**:

1. **Given** a component has linked capabilities via ComponentCapabilityLink records, **When** the component detail view renders, **Then** it displays the count of linked capabilities and the count of controls those capabilities map to.
2. **Given** a Thing-type component exists, **When** the user clicks "Create Capability from Component," **Then** the capability creation form opens with the name and provider pre-filled from the component.

---

### User Story 7 — Guided Empty State (Priority: P7)

A new user opens the Capabilities page for the first time with no capabilities defined. Instead of an empty list, they see a guided empty state with three action cards: "Create Manually" (opens the capability form), "Import CSP Profile" (opens the CSP import flow), and "Import CRM" (opens the CRM import flow). Each card has a brief description of when to use it.

**Why this priority**: Improves first-time user experience but is cosmetic — the underlying functionality must exist first.

**Independent Test**: Can be verified by loading the Capabilities page with zero capabilities and confirming the three action cards are displayed with working links.

**Acceptance Scenarios**:

1. **Given** the Capabilities page loads with zero capabilities, **When** rendered, **Then** a guided empty state displays three action cards: "Create Manually," "Import CSP Profile," and "Import CRM."
2. **Given** the guided empty state is displayed, **When** the user clicks any action card, **Then** the corresponding action (form, CSP import dialog, CRM import dialog) opens.
3. **Given** at least one capability exists, **When** the Capabilities page loads, **Then** the empty state is not shown and the normal list view is displayed.

---

### Edge Cases

- What happens when a CSP profile import is run twice with the same profile? The system deduplicates components and capabilities by name+provider and only adds new control mappings, avoiding duplicate records.
- What happens when a CRM file contains control IDs that don't exist in the system's NIST baseline? Those rows are flagged in the preview as "unmatched" and skipped during import.
- What happens when a capability is deleted after being created by CSP import? All associated control mappings, inheritance designations, and auto-generated narratives are cascade-cleaned, consistent with existing delete behavior.
- What happens when a CSP profile import would create a Primary mapping for a control that already has a Primary mapping from another capability? The system warns in the preview and assigns the new mapping as Supporting to avoid violating the one-Primary-per-control constraint.
- What happens when the CRM file has rows with empty provider fields? Those rows are grouped under a default "Unspecified Provider" capability and no component is created for them.
- What happens when an import is interrupted mid-operation (e.g., network failure)? The import operation is transactional — either all records are created or none are, preventing partial/inconsistent state.
- What happens when a user has no registered systems and views Coverage %? The denominator falls back to the CSP profile's declared baseline level (e.g., High = 325 controls). If no CSP profiles have been imported either, Coverage % displays "N/A — import a CSP profile to establish a baseline."

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide "Import CSP Profile" action on the Capabilities page that creates the full pipeline: components (Things), capabilities, component-capability links, control mappings, org inheritance designations, and enriched narratives in a single transactional operation.
- **FR-002**: System MUST display a preview dialog before any import showing counts of components, capabilities, control mappings, and affected systems that will be created.
- **FR-003**: System MUST deduplicate during import — if a component or capability with the same name and provider already exists, new mappings are added to the existing record rather than creating duplicates.
- **FR-004**: System MUST automatically trigger org inheritance derivation and cascade propagation after CSP profile import completes.
- **FR-005**: System MUST auto-generate enriched SSP narratives with component context for all control mappings created during import.
- **FR-006**: System MUST provide "Import CRM" action on the Capabilities page that parses CSV/Excel files, groups rows by provider + NIST family to create capabilities (e.g., "Azure / Access Control"), creates components, establishes links and control mappings, and triggers org inheritance derivation.
- **FR-007**: System MUST create one component (type: Thing) per unique non-empty provider and one capability per unique provider + NIST family combination found in a CRM import. Rows with empty provider fields are grouped under a default "Unspecified Provider" capability with no component created.
- **FR-008**: System MUST display coverage summary cards on the Capabilities page showing Total Capabilities, Mapped Controls, Gap Controls, and Coverage %.
- **FR-009**: System MUST show linked component names as badges on each capability card alongside control mappings and system count.
- **FR-010**: System MUST display a contextual page header on the Capabilities page explaining the 3-layer model (Components → Capabilities → Inheritance).
- **FR-011**: System MUST display a guided empty state when no capabilities exist, with three action cards: Create Manually, Import CSP Profile, Import CRM.
- **FR-012**: System MUST provide a "Link Components" action per capability card that opens a component picker modal allowing users to create ComponentCapabilityLink records.
- **FR-013**: System MUST display a cross-link banner on the Control Inheritance page: "Designations derived from Security Capabilities. [Manage Capabilities →]."
- **FR-014**: System MUST remove CSP Profile and CRM Import buttons from the Control Inheritance page entirely — the Capabilities page is the sole import path. The cross-link banner (FR-013) directs users there.
- **FR-015**: System MUST show component context in org default tooltips on the Control Inheritance page, displaying which components back the source capability.
- **FR-016**: System MUST show capability coverage per component on the Components page (count of linked capabilities and mapped controls).
- **FR-017**: System MUST provide a "Create Capability from Component" quick action on Thing-type components that pre-fills the capability form with the component's name and provider.
- **FR-018**: System MUST provide a `GET /capabilities/coverage` endpoint returning org-wide coverage (against the highest active baseline) with total capabilities, mapped controls, unmapped controls, coverage %, per-family breakdown, and optional per-system breakdown. When no systems are registered, the denominator falls back to the imported CSP profile's declared baseline level (e.g., High = 325 controls). When no CSP profiles have been imported, coverage displays "N/A."
- **FR-019**: System MUST handle Primary mapping conflicts during import by assigning conflicting mappings as Supporting and including a warning in the preview.
- **FR-020**: System MUST process import operations transactionally — all records created or none — to prevent partial/inconsistent state.
- **FR-021**: System MUST display a "Capability Coverage %" KPI card on the Portfolio Risk Profile dashboard (`/`) showing org-wide coverage against the highest active baseline, alongside existing KPIs (Total Systems, Avg Compliance %, etc.).

### Key Entities

- **SystemComponent (Component)**: A person, place, or thing that participates in the security posture. Components of type "Thing" represent infrastructure and services (e.g., Entra ID, Key Vault). Created during imports to represent CSP services or CRM providers.
- **SecurityCapability (Capability)**: An organization-level reusable security measure (e.g., "Multi-Factor Authentication") with a provider, category (NIST family), description, and implementation status. The central hub entity connecting components to control mappings.
- **ComponentCapabilityLink**: A junction record linking a component to a capability, establishing which components provide or support a given security measure.
- **CapabilityControlMapping**: Links a capability to a specific NIST control with a role (Primary/Supporting/Shared) and optional system scope. Org-wide mappings (no system scope) apply to all systems.
- **ControlInheritance**: Per-system NIST control designation (Inherited/Shared/Customer) now derived from the capabilities pipeline rather than imported directly from CSP profiles or CRM files.
- **ControlImplementation (Narrative)**: Auto-generated SSP narrative text for a control in a system, enriched with component and capability context.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Importing a CSP profile from the Capabilities page creates the complete Component → Capability → Control Mapping → Org Inheritance → Narrative pipeline in one operation, with 100% of resulting records traceable end-to-end.
- **SC-002**: The Coverage summary on the Capabilities page accurately reflects the percentage of baseline controls covered by at least one capability, updated within 5 seconds of any import or manual change.
- **SC-003**: Duplicate imports of the same CSP profile or CRM file produce zero duplicate components or capabilities — only new mappings are added to existing records.
- **SC-004**: Users can complete a full CSP profile import (preview, confirm, apply) in under 2 minutes. *(Manual verification)*
- **SC-005**: Users can complete a full CRM import (upload, map columns, preview, confirm, apply) in under 3 minutes. *(Manual verification)*
- **SC-006**: The guided empty state provides clear, self-explanatory action paths (Create Manually, Import CSP Profile, Import CRM) that enable first-time users to initiate an import without external guidance. *(Qualitative — verified via manual walkthrough)*
- **SC-007**: After Feature 045 ships, the Control Inheritance page has no direct import actions — all CSP profile and CRM imports happen exclusively through the Capabilities page.
- **SC-008**: Component coverage counts on the Components page are accurate and update automatically when capability links change.

## Assumptions

- The CSP profile JSON schema (e.g., `azure-gov-fedramp-high.json`) will be extended from the current flat `controls[]` array to a `services[]` array that groups controls by CSP service (e.g., "Microsoft Entra ID", "Azure Key Vault", "Azure Monitor"). Each service entry includes `name`, `category`, `description`, and its own `controls[]`. This is our own custom schema (not DISA or any external standard) and is backward-compatible — the `CspProfileService` loader will be updated to parse either format. The extended format provides service-level granularity needed to auto-create components and group capabilities during import.
- CRM files follow a tabular format with at minimum: Control ID, Inheritance Type, and Provider columns. Additional columns are optional.
- The existing `NarrativeTemplateService` and AI narrative generation pipeline are sufficient for generating enriched narratives with component context — no new narrative engine is needed.
- The `GET /capabilities/coverage` endpoint computes org-wide coverage against the highest active baseline (e.g., High = 325 controls) as the primary number, and also returns per-system breakdowns against each system's own baseline. When no systems are registered, the denominator falls back to the imported CSP profile's declared baseline level (e.g., High = 325). When neither systems nor CSP profiles exist, coverage returns null and the UI displays "N/A." The org-wide number appears on both the Portfolio Risk Profile dashboard and the Capabilities page; the per-system breakdown is available on the Capabilities page.
- Organization-level inheritance derivation (Feature 044) already handles the cascade from org defaults to individual systems and will be triggered after imports without modification to its core logic.
- The component picker modal can reuse the existing component list/search patterns already present in the application.

## Performance Requirements

### Performance Targets

- **PERF-001**: CSP profile import (full pipeline: components + capabilities + mappings + org inheritance derivation + narrative generation) for a High baseline (~160 controls, ~10 services) MUST complete in **< 30 seconds**.
- **PERF-002**: CRM import (full pipeline) for a 325-row High baseline CRM file MUST complete in **< 30 seconds**.
- **PERF-003**: `GET /capabilities/coverage` endpoint MUST return in **< 2 seconds** for up to 50 capabilities and 325 control mappings.
- **PERF-004**: Capabilities page initial load (summary cards + capability list with component badges) MUST render in **< 2 seconds**.
- **PERF-005**: Portfolio Risk Profile KPI card (Capability Coverage %) MUST not add more than **200ms** to the existing page load time.
- **PERF-006**: Import preview dialog (dry-run computation) MUST display within **< 5 seconds** of profile/file selection.

### Performance Tests

Integration tests following the existing project pattern (xUnit + `Stopwatch` assertions):

- **PT-001**: `CspProfileImport_HighBaseline_CompletesWithinPerformanceBudget` — Import the Azure Gov FedRAMP High profile through the full pipeline and assert total elapsed time < 30 seconds.
- **PT-002**: `CrmImport_325Rows_CompletesWithinPerformanceBudget` — Import a 325-row CRM CSV through the full pipeline and assert total elapsed time < 30 seconds.
- **PT-003**: `CoverageEndpoint_50Capabilities_ReturnsWithinBudget` — Seed 50 capabilities with 325 control mappings, call `GET /capabilities/coverage`, and assert response time < 2 seconds.
- **PT-004**: `CspProfileImport_DuplicateRun_NoPerformanceDegradation` — Run the same CSP profile import twice and assert the second run (deduplication path) completes within the same 30-second budget.

## Documentation Updates

The following documentation files MUST be updated after implementation to reflect the Unified Capabilities Hub changes:

| Document | Updates Required |
|----------|-----------------|
| `docs/guides/security-capabilities.md` | Rewrite as the primary capabilities guide: CSP profile import flow, CRM import flow, coverage dashboard, component badges, 3-layer model explanation, guided empty state, link-components workflow |
| `docs/guides/control-inheritance.md` | Remove CSP Profile and CRM Import button references; add cross-link banner documentation; update org default tooltip to include component context |
| `docs/architecture/data-model.md` | Update CSP profile JSON schema (services[] extension); document import pipeline data flow (CSP/CRM → Components → Capabilities → Mappings → Inheritance → Narratives) |
| `docs/architecture/overview.md` | Add Capabilities Hub architecture section showing the 3-layer model; update system diagram with import pipeline |
| `docs/architecture/agent-tool-catalog.md` | Add `GET /capabilities/coverage` endpoint; update CSP profile import endpoint to reflect new pipeline; add import-via-capabilities endpoints |
| `docs/api/mcp-server.md` | Add coverage endpoint; update CSP profile and CRM import endpoint docs to reflect Capabilities page routing; remove old Control Inheritance import endpoints |
| `docs/reference/tool-inventory.md` | Add coverage endpoint row; update CSP/CRM import rows to reference Capabilities page |
| `docs/reference/glossary.md` | Add terms: Capabilities Hub, 3-Layer Model, Coverage %, Gap Controls; update Security Capability definition |
| `docs/guides/issm-guide.md` | Update CSP profile import instructions to reference Capabilities page instead of Control Inheritance page |
| `docs/guides/ao-quick-reference.md` | Add Capability Coverage % KPI to Portfolio Risk Profile section (if referenced) |
| `CHANGELOG.md` | Add version entry for Feature 045 |
| `docs/release-notes/` | Create release notes file for Feature 045 |

## Clarifications

### Session 2026-03-22

- Q: How should CSP profiles map controls to specific CSP services for component/capability creation? → A: Extend our custom CSP profile JSON schema with a `services[]` array grouping controls by CSP service name (Option B). The schema is our own format (not DISA-specific), users cannot edit profiles via the UI (seed data loaded at startup), and CRM import remains the user-facing customization path.
- Q: When grouping CRM import rows into capabilities, what should be the grouping key? → A: Group by Provider + NIST Family (e.g., "Azure / Access Control"). This produces ~20 capabilities per provider (one per NIST family) and mirrors the CSP profile services structure.
- Q: Should the Coverage API compute against the org-wide NIST catalog or each system's active baseline? → A: Both (Option C). Org-wide coverage against the highest active baseline appears on the Portfolio Risk Profile dashboard as a KPI card and on the Capabilities page. Per-system breakdowns against each system's own baseline are also available on the Capabilities page.
- Q: What should happen to existing ControlInheritance records created by the old CSP import path? → A: Not applicable — no production data exists yet. The old import path on the Control Inheritance page will be replaced by the new Capabilities pipeline; no migration needed.
- Q: Should the old CSP Profile and CRM Import buttons on the Control Inheritance page be kept or removed? → A: Remove entirely (Option A). The Capabilities page is the only import path. No legacy users to support.
- Q: How should Coverage % work when a user has no registered systems (first-time setup)? → A: Use the CSP profile's declared baseline level as the fallback denominator (Option B). If a user imports a CSP profile before registering any systems, the profile's baseline (e.g., High = 325 controls) becomes the denominator for Coverage %. If no CSP profiles have been imported either, Coverage % displays "N/A."
