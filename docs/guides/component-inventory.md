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
