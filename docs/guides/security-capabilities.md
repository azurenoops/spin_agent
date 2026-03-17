# Security Capabilities Library

> Feature 030: Visual Compliance Dashboard & Risk Solutions Library

The Security Capabilities Library provides a centralized catalog of your organization's security solutions, with automatic NIST 800-53 control mapping, narrative generation, and propagation management.

---

## Overview

A **Security Capability** represents a technology, process, or service that implements one or more NIST 800-53 controls. Examples include:

- **Multi-Factor Authentication** (Entra ID) → AC-2, AC-7, IA-2, IA-5, IA-8
- **Encryption at Rest** (Key Vault) → SC-12, SC-13, SC-28
- **Log Analytics** (Azure Monitor) → AU-2, AU-3, AU-6, AU-12

---

## Creating a Capability

Navigate to `/capabilities` and click **+ Create Capability**.

### Required Fields

| Field | Description |
|-------|-------------|
| **Name** | Unique display name (e.g., "Multi-Factor Authentication") |
| **Provider** | Technology provider (e.g., "Microsoft Entra ID") |
| **Category** | NIST 800-53 control family (e.g., "IA — Identification and Authentication") |
| **Description** | What this capability does and how it works |
| **Implementation Status** | Planned, InProgress, Implemented, or Deprecated |
| **Owner** | Team or role responsible |

---

## Control Mapping

After creating a capability, expand it and use the **Mapping Panel** to link controls.

### Mapping Roles

Each mapping has a role that describes the capability's relationship to the control:

| Role | Description |
|------|-------------|
| **Primary** | This capability is the primary implementation for the control |
| **Supporting** | This capability supports another primary implementation |
| **Shared** | This capability is shared across multiple controls |

!!! warning "Duplicate Primary"
    If a control already has a Primary mapping from another capability, you'll see a warning. Only one capability should be Primary per control per system.

---

## Narrative Auto-Generation

When you map a capability to controls, narratives are automatically generated and written to the corresponding `ControlImplementation` records.

### How It Works

1. The **NarrativeTemplateService** generates a narrative using the capability's name, provider, and description
2. Each NIST family has context-specific wording (e.g., AC family includes access control terms, IA family includes authentication terms)
3. The generated narrative is stored in `ControlImplementation.Narrative`
4. `SecurityCapabilityId` is set on the `ControlImplementation` to track the source

### Example Generated Narrative

> *"Access control is enforced through Multi-Factor Authentication provided by Microsoft Entra ID. This capability ensures that all user access to the system requires multi-factor verification, supporting the requirements of AC-2 by implementing automated account management procedures."*

---

## Narrative Propagation

When you **update** a capability (change name, provider, or description), narratives are automatically regenerated for all mapped controls.

### Manual Override Protection

If you manually edit a generated narrative in the SSP authoring workflow, the `IsManuallyCustomized` flag is set to `true`. When the capability is updated:

- **Non-customized narratives** → Regenerated automatically
- **Customized narratives** → Skipped (preserved as-is)

The update response shows how many narratives were updated vs. skipped:

```
Updated 4 narratives, skipped 1 manually customized
```

---

## Deleting a Capability

When you delete a capability:

1. All `ControlImplementation.SecurityCapabilityId` references are set to `null`
2. Existing narratives are **preserved** (not deleted)
3. A `DashboardActivity` entry is created to track the deletion

---

## Browsing & Filtering

The Capabilities Library page supports:

- **Search** — Filter by capability name
- **Category Filter** — Filter by NIST control family
- **Status Filter** — Filter by implementation status
- **Pagination** — Cursor-based pagination for large catalogs

---

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/dashboard/capabilities` | List capabilities with filters |
| POST | `/api/dashboard/capabilities` | Create a new capability |
| PUT | `/api/dashboard/capabilities/{id}` | Update capability (triggers propagation) |
| DELETE | `/api/dashboard/capabilities/{id}` | Delete capability |
| GET | `/api/dashboard/capabilities/{id}/mappings` | List control mappings |
| POST | `/api/dashboard/capabilities/{id}/mappings` | Create new mappings |

---

## Boundary-Scoped Mappings (Feature 033)

Capability-to-control mappings can be scoped to specific authorization boundaries:

- A **boundary badge** on each mapping indicates its boundary scope (or "All Boundaries" for organization-wide mappings)
- When adding new mappings, a boundary selector allows targeting a specific boundary
- **Composite narratives**: When a control has mappings across multiple boundaries, the narrative auto-generates with organization-wide sections first, followed by per-boundary sections alphabetically
- Manually customized narratives are not overwritten during propagation (logged as `CompositeNarrativeSkipped` audit event)
