# RMF Phase 2: Select

> Select, tailor, and document the controls necessary to protect the system based on the categorization results.

---

## Phase Overview

| Attribute | Value |
|-----------|-------|
| **Phase Number** | 2 |
| **NIST Reference** | SP 800-37 Rev. 2, §3.3 |
| **Lead Persona** | ISSM |
| **Supporting Personas** | ISSO, SCA (review), Engineer (consulted) |
| **Key Outcome** | NIST 800-53 baseline selected, tailored, inheritance declared, CRM generated |

---

## Persona Responsibilities

### ISSM (Lead)

**Tasks in this phase**:

1. Select baseline → Tool: `compliance_select_baseline`
2. Tailor controls → Tool: `compliance_tailor_baseline`
3. Declare inheritance → Tool: `compliance_set_inheritance`
4. Review baseline → Tool: `compliance_get_baseline`
5. Generate CRM → Tool: `compliance_generate_crm`
6. View STIG mappings → Tool: `compliance_show_stig_mapping`
7. Advance to Implement → Tool: `compliance_advance_rmf_step`

**Natural Language Queries**:

> **"Select the control baseline for system {id} with CNSSI 1253 overlay"** → `compliance_select_baseline` — selects Low/Moderate/High baseline with optional CNSSI 1253 overlay

> **"Remove control PE-5 from the baseline — not applicable, system is 100% cloud-hosted"** → `compliance_tailor_baseline` — removes control with documented rationale

> **"Set AC-1 as inherited from Azure Government FedRAMP High"** → `compliance_set_inheritance` — marks control as fully inherited

> **"Set AC-2 as shared with Azure Government — customer configures access policies"** → `compliance_set_inheritance` — marks control as shared with CSP

> **"Generate the Customer Responsibility Matrix for system {id}"** → `compliance_generate_crm` — generates CRM grouped by control family

> **"Show me the STIG mappings for AC-2"** → `compliance_show_stig_mapping` — shows DISA STIG IDs mapped to NIST controls

> **"How many controls are in the baseline for system {id}?"** → `compliance_get_baseline` — baseline summary with control count

### SCA (Review)

- Review baseline selection for completeness
- Verify tailoring rationale is documented

### Engineer (Consulted)

- Provide technical input on control applicability
- Confirm cloud service responsibilities

---

## Baseline Control Counts

| Baseline | Typical Count | DoD IL |
|----------|--------------|--------|
| Low | ~152 controls | IL2 |
| Moderate | ~329 controls | IL4 |
| High | ~400 controls | IL5/IL6 |

---

## Inheritance Types

| Type | Meaning | SSP Narrative |
|------|---------|---------------|
| **Inherited** | Fully satisfied by CSP (e.g., physical security) | Auto-populated standard statement |
| **Shared** | Partially CSP, partially customer | Requires customer responsibility documentation |
| **Customer** | Entirely the customer's responsibility | Requires full human-authored narrative |

### Org-Level Inheritance Defaults

Organizations can define org-wide inheritance defaults derived from the **Security Capabilities Library**. When org defaults are active:

- Controls mapped to org-wide capabilities are automatically designated as Inherited or Shared with tracked `OrgDerived` source
- New systems inheriting a baseline receive org defaults via **cascade propagation**
- ISSMs can override org defaults per-system; overrides are tracked separately
- The **Revert to Org Defaults** action restores org-derived designations for overridden controls

Org defaults reduce repetitive work across systems sharing the same CSP and accelerate the Select phase. See the [Control Inheritance Guide](../guides/control-inheritance.md#org-level-inheritance-defaults) for detailed usage.

---

## Documents Produced

| Document | Owner | Format | Gate Dependency |
|----------|-------|--------|----------------|
| Customer Responsibility Matrix (CRM) | ISSM | Markdown | Informational (not gate-blocking) |

---

## Phase Gates

| Gate | Condition | Checked By |
|------|-----------|-----------|
| Baseline exists | A security control baseline has been selected | `compliance_advance_rmf_step` |

---

## Transition to Next Phase

| Trigger | From Phase | To Phase | Handoff |
|---------|-----------|----------|---------|
| `compliance_advance_rmf_step` with gate pass | Select | Implement | Baseline and inheritance ready for narrative authoring |

---

## See Also

- [Previous Phase: Categorize](categorize.md)
- [Next Phase: Implement](implement.md)
- [ISSM Guide](../guides/issm-guide.md) — Full ISSM workflow documentation
- [NIST Controls Reference](../reference/nist-controls.md) — Control details
- [STIG Coverage Reference](../reference/stig-coverage.md) — DISA STIG mappings
