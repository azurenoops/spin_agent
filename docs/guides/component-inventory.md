# Component Inventory

> Feature 030: Visual Compliance Dashboard & Risk Solutions Library

The Component Inventory tracks all elements of your system using the **People, Places, and Things** model required for SSP Appendix A generation.

---

## Overview

Every information system consists of three types of components:

| Type | Description | Examples |
|------|-------------|----------|
| **Person** | Security roles and personnel | ISSM, ISSO, SCA, System Admin |
| **Place** | Locations where system components reside | Azure Gov East, Azure Gov West, Data Center |
| **Thing** | Technical assets and tools | Entra ID, Defender, Key Vault, Sentinel |

---

## Adding Components

Navigate to `/systems/{systemId}/components` and click **+ Add Component**.

### Required Fields

| Field | Description |
|-------|-------------|
| **Name** | Component name (e.g., "Microsoft Entra ID") |
| **Type** | Person, Place, or Thing |

### Optional Fields

| Field | Description |
|-------|-------------|
| **Sub-Type** | Classification (e.g., "Cloud Service", "Security Personnel") |
| **Description** | What this component does |
| **Owner** | Responsible team or role |
| **Status** | Active, Planned, or Decommissioned |
| **Linked Capabilities** | Security capabilities this component supports |

---

## Capability Linking

Components can be linked to Security Capabilities to show which components implement which security solutions. This creates a traceable chain:

```
Component (Thing: Entra ID)
  → Capability (Multi-Factor Authentication)
    → Controls (IA-2, IA-5, AC-7)
      → Narratives (auto-generated)
```

---

## Inventory Sections

The inventory page organizes components into three collapsible sections:

1. **People** — Security personnel with roles and responsibilities
2. **Places** — Physical and cloud locations
3. **Things** — Technical assets, tools, and services

Each section shows a count badge and lists components with:
- Name and status badge
- Sub-type and owner
- Linked capability tags

---

## Deletion Flagging

When you delete an **Active** component that has linked capabilities:

1. The component is removed from the inventory
2. Each linked capability receives a `DashboardActivity` entry flagging it for review
3. A confirmation dialog shows which capabilities will be flagged

This ensures that removing a component triggers a review of whether the corresponding security capabilities are still adequately implemented.

!!! note "Decommissioned Components"
    Deleting a component with **Decommissioned** status does not flag capabilities, since the component was already retired from service.

---

## Filtering & Search

- **Type Filter** — Show only People, Places, or Things
- **Status Filter** — Show Active, Planned, or Decommissioned
- **Search** — Filter by component name or description

---

## Assessment & Remediation Linkage

Azure-backed "Thing" components are automatically linked to compliance findings by matching `SystemComponent.AzureResourceId` to `ComplianceFinding.ResourceId`. Per-component risk summaries — including open finding count, highest severity, and overdue remediation count — appear on the **Assessment detail view** and **Remediation page**, not on the Components page.

The Components page remains focused on asset inventory management (CRUD, capability linking, boundary assignment). To view risk posture per component, navigate to:

- **Assessment detail view** — filter and group findings by component, with per-component risk summaries
- **Remediation page** — remediation tasks and POA&M items display their associated component name

---

## SSP Appendix A

The component inventory feeds into SSP Appendix A generation via the `DocumentGenerationService`. When generating an SSP document, the Appendix A section is populated with:

- Component name and type
- Description and owner
- Linked security capabilities
- Current operational status

---

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/dashboard/systems/{systemId}/components` | List components with filters |
| POST | `/api/dashboard/systems/{systemId}/components` | Create a new component |
| PUT | `/api/dashboard/components/{id}` | Update a component |
| DELETE | `/api/dashboard/components/{id}` | Delete a component (flags capabilities) |

---

## Boundary-Scoped Components (Feature 033)

Components can be assigned to specific authorization boundaries:

- When creating a component, an optional **Boundary** selector appears if the system has multiple boundary definitions
- Components with no boundary assignment are treated as organization-wide (applicable to all boundaries)
- The component list groups entries by boundary when multiple boundaries exist
- Deleting a boundary reassigns its components to the Primary boundary

---

## Org-Wide Component Library (Feature 036)

Components can be created at the **organization level** (not tied to a specific system) and then assigned to one or more registered systems.

### Policies for this system

Open **Controls & evidence → Policies** inside an authorized system workspace.
This uses the existing organization Policy component library, not another library.
The **Organization policy library** link opens the library with its Policy filter.

When no references exist, the page shows **No policies linked yet**, one **Add
policy** button, and no empty table, search, or pagination. Populated systems have
search, source-status/source-change filters, accurate totals, and paging.

**Add policy** is a two-step drawer:

1. **Choose policy** searches authorized organization records by name/topic and
   shows source status and actual revision metadata. Already assigned sources are
   identified. **View source** reads the authorized source rather than treating
   an Active status as applicability approval.
2. **Explain & add** keeps the target system and selected source visible. Enter
   a system-specific rationale (1-500 characters), review its actual character
   count and save preview, then choose **Add system reference**.

**Common references** are suggestions, not automatic requirements. Find the
suggestion in the library first. If missing, an authorized library author can
explicitly create it using **Create a library policy** (name up to 200 characters,
topic up to 100, description up to 2000). That save creates only the shared source
and returns to selection; it does not assign it. Canceling the later system flow
does not delete an explicitly created library record.

**Retained details** show rationale, source snapshot, current source/change
indication, actual related controls, and audit history. Editing rationale does
not replace the source snapshot. A stale source or assignment requires refresh
and renewed selection/review, not an automatic overwrite.

Sources do not currently have native numbered policy versions. The page uses
actual created/updated/capture metadata and retained source fingerprints rather
than inventing version numbers. Older assignments without snapshots are
explicitly labeled unretained. Policy presence through a linked capability is
distinct from a direct assignment and is not silently removed by unlinking.

**Unlink from system** previews the scoped impact and removes only that system
reference. The shared source and other system assignments remain; history is
auditable. Available actions come from server authorization, not the role label.

Applicability review is not implemented by the current assignment contract.
Source status and review information are displayed separately. No link, Active
source, or saved rationale proves control implementation, approves a narrative,
or grants an ATO.

#### SSP and narrative connections

Current SSP/OSCAL code can export Policy records as generic component inventory.
The narrative enrichment template handles Things, People, and Places, but does
not currently include Policy components as supporting-reference context.
The new retained snapshot/rationale is not yet mapped automatically into SSP
supporting citations or all eMASS outputs. Inspect generated documents rather
than infer submission readiness from a saved assignment.

The separate legal-authority section-mapping issue remains tracked by
[azurenoops/spin_agent#970](https://github.com/azurenoops/spin_agent/issues/970).
Policy narrative-context gaps are tracked by
[azurenoops/spin_agent#972](https://github.com/azurenoops/spin_agent/issues/972).
The page makes no unsupported FedRAMP section-number claim.

### Creating Org-Wide Components

Navigate to `/components` to view the org-wide component library. Click **+ Add Component** to create a component with:

- **Name**, **Type**, **Status** — same as system-scoped components
- **Linked Capabilities** — security capabilities this component implements

### Assigning to Systems

From the component library or a system's component page, assign an org-wide component to a system:

1. Click the **Assign to System** action on a component
2. Select a target system and optional authorization boundary
3. The component now appears in that system's component inventory

A component can be assigned to **multiple systems** — useful for shared infrastructure like Entra ID or Defender.

### Impact Preview

When editing or deleting a component that is linked to capabilities with control mappings, a **cascade impact preview** appears showing:

- Number of systems affected
- Number of control narratives that will be regenerated
- Number of manually customized narratives that will be **skipped** (preserved)

### Cascade Narrative Regeneration

When you change a component's **name**, **description**, or **owner**, all linked control narratives are automatically regenerated:

1. The system traverses: Component → Linked Capabilities → Control Mappings → Control Implementations
2. For each affected narrative, a **NarrativeVersion** snapshot is created (preserving the previous text)
3. The narrative is regenerated using deterministic templates with updated component context
4. Manually customized narratives (`IsManuallyCustomized = true`) are **never overwritten**

The same cascade triggers when a component is **assigned to** or **removed from** a system.

### API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/dashboard/components` | List all org-wide components |
| POST | `/api/dashboard/components` | Create an org-wide component |
| PUT | `/api/dashboard/components/{id}` | Update (triggers cascade) |
| DELETE | `/api/dashboard/components/{id}` | Delete a component |
| POST | `/api/dashboard/components/{id}/assign` | Assign to a system |
| DELETE | `/api/dashboard/components/{id}/assignments/{assignmentId}` | Remove assignment |
| GET | `/api/dashboard/components/{id}/impact-preview` | Preview cascade impact |
