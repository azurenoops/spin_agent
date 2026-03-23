# Security Capabilities Hub

> Feature 045: Unified Security Capabilities Hub

The Capabilities Hub is a centralized command center for managing the full security capability pipeline: **Components → Capabilities → Control Inheritance**. It provides CSP profile import, CRM file import, coverage analytics, and component linking — all from a single page.

---

## Three-Layer Model

The Capabilities Hub implements a 3-layer architecture:

1. **Components** — Technology products and services (e.g., "Microsoft Entra ID", "Zscaler ZIA")
2. **Capabilities** — Security functions mapped to NIST 800-53 control families (e.g., "Entra ID / Access Control")
3. **Control Mappings** — Links from capabilities to specific controls with inheritance types (Inherited, Shared)

---

## Getting Started

When the Capabilities page is empty, a **Guided Empty State** offers three paths:

| Path | Description |
|------|-------------|
| **Create Manually** | Define a single capability with name, provider, category |
| **Import CSP Profile** | Bulk-import from Azure FedRAMP High or another seed profile |
| **Import CRM File** | Upload a CSV/Excel CRM to auto-create capabilities |

---

## CSP Profile Import

1. Click **Import CSP Profile** in the toolbar
2. Select a profile from the dropdown (e.g., "Azure Government FedRAMP High")
3. Choose conflict resolution: **Skip** existing or **Overwrite**
4. Click **Preview** to see a dryRun summary (components/capabilities to create vs reuse)
5. Click **Confirm Import** to execute

The import pipeline:

- Creates one **Component** per CSP service (e.g., "Microsoft Entra ID")
- Creates one **Capability** per service ("{service} / {family}" naming)
- Creates **ControlMappings** with inheritance types from the profile
- Auto-generates **narratives** for each mapped control
- Derives **org-level defaults** for downstream system inheritance

---

## CRM File Import

1. Click **Import CRM** in the toolbar
2. Drag & drop or browse for a CSV or Excel file
3. Map detected columns to target fields (Control ID, Inheritance Type, Provider, Customer Responsibility)
4. Review sample data and set conflict resolution
5. Click **Preview Import** to see a dryRun summary
6. Click **Confirm Import** to execute

The import groups rows by **Provider + NIST Family** to create capabilities automatically.

---

## Coverage Dashboard

Four KPI cards at the top of the Capabilities page show:

| Card | Description |
|------|-------------|
| **Total Capabilities** | Number of security capabilities defined |
| **Mapped Controls** | Controls with at least one capability mapping |
| **Gap Controls** | Controls in the highest baseline with no mapping |
| **Coverage %** | Mapped / total controls in highest baseline |

The Coverage % also appears on the **Portfolio Risk Profile** page as an 8th KPI card.

---

## Component Linking

Each capability card shows **component badges** for linked components. Click **Link Components** to:

1. Search and filter the org-level component library
2. Toggle checkboxes to link/unlink components
3. Save changes

On the **Component Inventory** page, Thing-type components without capabilities show a **+ Capability** quick action that navigates to the Capabilities page with the create form pre-filled.

---

## Control Inheritance Integration

The **Control Inheritance** page now shows:

- A **cross-link banner**: "Designations derived from Security Capabilities. [Manage Capabilities →]"
- **Component context tooltips** on org-level defaults: hover over Source Capability to see which capabilities back each designation
- CSP Profile and CRM Import buttons have been **removed** from the inheritance page (use the Capabilities Hub instead)

---

## Control Mapping

Expand a capability card to see the **Mapping Panel** for linking controls.

### Mapping Roles

| Role | Description |
|------|-------------|
| **Primary** | This capability is the primary implementation |
| **Supporting** | Supports another primary implementation |
| **Shared** | Shared across multiple controls |

---

## Narrative Auto-Generation

When you map a capability to controls, narratives are automatically generated using the capability's name, provider, and description. Each NIST family has context-specific wording.

### Manual Override Protection

Manually edited narratives have `IsManuallyCustomized = true` and are preserved during capability updates.

---

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/dashboard/capabilities` | List capabilities with filters |
| POST | `/api/dashboard/capabilities` | Create a new capability |
| PUT | `/api/dashboard/capabilities/{id}` | Update capability |
| DELETE | `/api/dashboard/capabilities/{id}` | Delete capability |
| POST | `/api/dashboard/capabilities/import/csp-profile` | Import CSP profile |
| POST | `/api/dashboard/capabilities/import/crm` | Import CRM file |
| GET | `/api/dashboard/capabilities/coverage` | Coverage analytics |
| GET | `/api/dashboard/capabilities/csp-profiles` | List available CSP profiles |
| POST | `/api/dashboard/components/{id}/capabilities` | Link capabilities to component |
| DELETE | `/api/dashboard/components/{id}/capabilities/{capId}` | Unlink capability |
