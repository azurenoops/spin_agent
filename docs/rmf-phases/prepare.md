# RMF Phase 0: Prepare

> Establish the context and priority for managing security and privacy risk. Register the system, define the authorization boundary, and assign RMF roles.

---

## Phase Overview

| Attribute | Value |
|-----------|-------|
| **Phase Number** | 0 |
| **NIST Reference** | SP 800-37 Rev. 2, §3.1 |
| **Lead Persona** | ISSM |
| **Supporting Personas** | ISSO, Engineer |
| **Key Outcome** | System registered with boundary and roles assigned |

---

## Persona Responsibilities

### ISSM (Lead)

**Tasks in this phase** (per NIST SP 800-37 Rev 2, Tasks P-14 through P-18):

1. Register the system → Tool: `compliance_register_system`
2. Identify system components (People, Places, Things) → Dashboard: **Components** page
3. Define the authorization boundary around identified assets → Tool: `compliance_define_boundary`
4. Exclude shared/inherited resources → Tool: `compliance_exclude_from_boundary`
5. Assign RMF roles → Tool: `compliance_assign_rmf_role`
6. Verify role assignments → Tool: `compliance_list_rmf_roles`
7. Advance to Categorize → Tool: `compliance_advance_rmf_step`

**Natural Language Queries**:

> **"Register a new system called 'ACME Portal' as a Major Application with mission-critical designation in Azure Government"** → `compliance_register_system` — creates system entity with RMF step = Prepare

> **"Define the authorization boundary for system {id} — add the production VMs, SQL database, and Key Vault"** → `compliance_define_boundary` — adds Azure resource IDs to the boundary

> **"Assign Jane Smith as ISSM and Bob Jones as ISSO for system {id}"** → `compliance_assign_rmf_role` — assigns named personnel to RMF roles

> **"Show me all registered systems"** → `compliance_list_systems` — lists all systems with RMF phase and status

> **"What roles are assigned to system {id}?"** → `compliance_list_rmf_roles` — shows all role assignments

> **"Advance system {id} to the Categorize phase"** → `compliance_advance_rmf_step` — transitions to next phase (gate-checked)

### ISSO (Support)

- Assist with boundary definition by identifying Azure resources
- Verify role assignments are accurate
- Create Privacy Threshold Analysis → Tool: `compliance_create_pta`
- Generate Privacy Impact Assessment (if PTA indicates PII) → Tool: `compliance_generate_pia`
- Register system interconnections → Tool: `compliance_add_interconnection`

### Engineer (Support)

- Provide Azure resource inventory for boundary definition
- Confirm system type and hosting environment details
- Register interconnections discovered during architecture review → Tool: `compliance_add_interconnection`

---

## Privacy & Interconnection Activities

### Privacy Threshold Analysis

Before leaving the Prepare phase, determine whether the system collects or processes PII:

```
Tool: compliance_create_pta
Parameters:
  system_id: "<system-guid>"
  collects_pii: true
  pii_categories: ["Name", "SSN", "Email"]
```

If the PTA indicates PII is collected, a Privacy Impact Assessment is required:

```
Tool: compliance_generate_pia
Parameters:
  system_id: "<system-guid>"
```

The PIA is submitted to the ISSM for review via `compliance_review_pia`.

### Interconnection Documentation

Register all system-to-system connections crossing the authorization boundary:

```
Tool: compliance_add_interconnection
Parameters:
  system_id: "<system-guid>"
  remote_system_name: "HR Payroll System"
  direction: "Outbound"
  protocol: "HTTPS"
  data_types: ["Employee Records"]
```

The ISSM generates formal ISA/MOU documents using `compliance_generate_isa` and registers agreements using `compliance_register_agreement`. If the system has no external connections, the ISSM can certify using `compliance_certify_no_interconnections`.

---

## Documents Produced

| Document | Owner | Format | Gate Dependency |
|----------|-------|--------|----------------|
| Privacy Threshold Analysis (PTA) | ISSO | Record | Informational |
| Privacy Impact Assessment (PIA) | ISSO / ISSM | Record | Informational (if PII) |
| Interconnection Agreements (ISA/MOU) | ISSM | Record | Informational |

---

## Phase Gates

| Gate | Condition | Checked By |
|------|-----------|-----------|
| Roles assigned | At least one RMF role assigned to the system | `compliance_advance_rmf_step` |
| Boundary defined | At least one resource in the authorization boundary | `compliance_advance_rmf_step` |
| Privacy readiness | PTA completed; PIA submitted if PII is collected | `compliance_check_privacy_compliance` |
| Interconnections documented | All connections registered or no-interconnections certified | `compliance_validate_agreements` |

---

## Transition to Next Phase

| Trigger | From Phase | To Phase | Handoff |
|---------|-----------|----------|---------|
| `compliance_advance_rmf_step` with gate pass | Prepare | Categorize | System ID with boundary and roles ready for categorization |

---

## See Also

- [Next Phase: Categorize](categorize.md)
- [ISSM Guide](../guides/issm-guide.md) — Full ISSM workflow documentation
- [Persona Overview](../personas/index.md) — Role definitions
