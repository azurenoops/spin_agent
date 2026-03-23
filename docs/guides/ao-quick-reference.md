# Authorizing Official (AO) Quick Reference

> Feature 015: Persona-Driven RMF Workflows — US8

This guide covers the Authorizing Official's tools for issuing authorization decisions, accepting risk, and reviewing the risk register.

!!! tip "New to ATO Copilot?"
    If this is your first time using ATO Copilot as an AO, start with the [AO Getting Started](../getting-started/ao.md) page for prerequisites, first-time setup, and your first 3 commands.

---

## Prerequisites

- ATO Copilot MCP server access
- `Compliance.AuthorizingOfficial` role assigned to the system
- Assessment completed (SAR generated, POA&M items created)

---

## Portfolio View

As an AO, you typically manage a portfolio of systems. Use the multi-system dashboard for portfolio-level visibility:

**Natural Language Queries:**

> **"Show the multi-system compliance dashboard"** — Portfolio view of all authorized systems

> **"Show all systems with ATOs expiring in the next 90 days"** — Expiration alerts across portfolio

> **"Which of my authorized systems have CAT I findings?"** — Risk-based portfolio filtering

> **"Show risk acceptances expiring in the next 90 days across all systems"** — Upcoming risk acceptance expirations

> **"Show authorization decisions I have issued this year"** — Decision history

| Tool | Purpose |
|------|---------|
| `compliance_multi_system_dashboard` | All systems in one view with scores and status |
| `compliance_track_ato_expiration` | Graduated expiration alerts (90d/60d/30d/expired) |
| `compliance_show_risk_register` | Review accepted risks approaching expiration |
| `compliance_reauthorization_workflow` | Check triggers and initiate reauthorization |

---

## Authorization Decision Types

| Type | Description | Expiration Required |
|------|-------------|---------------------|
| **ATO** | Authority to Operate — full authorization | Yes |
| **ATOwC** | ATO with Conditions — authorization with stipulations | Yes |
| **IATT** | Interim Authority to Test — limited testing authorization | Yes |
| **DATO** | Denial of Authorization to Operate | No |

---

## Issue an Authorization Decision

Tool: `compliance_issue_authorization`

### Basic ATO

```json
{
  "system_id": "<system-guid>",
  "decision_type": "ATO",
  "expiration_date": "2028-01-15",
  "residual_risk_level": "Low",
  "residual_risk_justification": "All CAT I findings remediated, 2 CAT III accepted"
}
```

### ATO with Conditions

```json
{
  "system_id": "<system-guid>",
  "decision_type": "AtoWithConditions",
  "expiration_date": "2026-06-30",
  "residual_risk_level": "Medium",
  "residual_risk_justification": "CAT II findings under remediation per POA&M",
  "terms_and_conditions": "MFA enforcement must be completed within 90 days. Quarterly POA&M reviews required."
}
```

### ATO with Inline Risk Acceptances

Accept risk on specific findings as part of the authorization decision:

```json
{
  "system_id": "<system-guid>",
  "decision_type": "ATO",
  "expiration_date": "2028-01-15",
  "residual_risk_level": "Low",
  "risk_acceptances": "[{\"finding_id\":\"<finding-guid>\",\"control_id\":\"CM-6\",\"cat_severity\":\"CatIII\",\"justification\":\"Configuration deviation documented and approved\",\"compensating_control\":\"Continuous monitoring alerts configured\",\"expiration_date\":\"2026-01-15\"}]"
}
```

### Deny Authorization (DATO)

```json
{
  "system_id": "<system-guid>",
  "decision_type": "DATO",
  "residual_risk_level": "Critical",
  "residual_risk_justification": "3 unmitigated CAT I findings with no remediation plan"
}
```

### Key Behaviors

- **Supersedes prior decisions**: Any existing active authorization is deactivated
- **Compliance score**: Calculated automatically from control effectiveness records at decision time
- **RMF advancement**: System moves to the **Monitor** phase after authorization
- **Findings captured**: Open findings at decision time are recorded in the decision record

---

## Accept Risk on a Finding

Tool: `compliance_accept_risk`

Accept risk on a specific finding after an authorization decision has been issued:

```json
{
  "system_id": "<system-guid>",
  "finding_id": "<finding-guid>",
  "control_id": "AC-2",
  "cat_severity": "CatII",
  "justification": "Network segmentation provides equivalent protection",
  "compensating_control": "Azure NSG rules restrict lateral movement",
  "expiration_date": "2025-12-31"
}
```

### Requirements

- An **active** authorization decision must exist for the system
- The finding must exist in the database
- The expiration date determines when the risk acceptance auto-expires

---

## View Risk Register

Tool: `compliance_show_risk_register`

### Active Acceptances Only (default)

```json
{
  "system_id": "<system-guid>"
}
```

### All Acceptances (including expired and revoked)

```json
{
  "system_id": "<system-guid>",
  "status_filter": "all"
}
```

### Filter Options

| Filter | Shows |
|--------|-------|
| `active` | Currently active risk acceptances (default) |
| `expired` | Past expiration date — automatically marked |
| `revoked` | Manually revoked acceptances |
| `all` | All acceptances regardless of status |

**Note:** Past-due risk acceptances are automatically expired when querying the register.

---

## AO Workflow Summary

```
1. Review SAR and RAR prepared by ISSM/SCA
2. Review POA&M items and remediation timelines
3. Review authorization package (bundle)
4. Make authorization decision:
   a. ATO — if risk is acceptable
   b. ATOwC — if conditional approval is appropriate
   c. IATT — for limited testing authorization
   d. DATO — if risk is unacceptable
5. Accept risk on specific findings as needed
6. Monitor risk register for expiring acceptances
```

---

## RBAC Summary

| Action | Required Role |
|--------|---------------|
| Issue authorization decision | `Compliance.AuthorizingOfficial` (exclusive) |
| Accept risk | `Compliance.AuthorizingOfficial` (exclusive) |
| View risk register | All compliance roles |
| View POA&M items | All compliance roles |
| View RAR/SAR | All compliance roles |

---

## Error Scenarios

| Error | Cause | Resolution |
|-------|-------|------------|
| `System not found` | Invalid system_id | Verify system is registered |
| `No active authorization` | Risk acceptance without prior ATO | Issue authorization decision first |
| `Invalid decision_type` | Unrecognized type string | Use: `ATO`, `AtoWithConditions`, `IATT`, `DATO` |
| `Invalid residual_risk_level` | Unrecognized level | Use: `Low`, `Medium`, `High`, `Critical` |
| `Finding not found` | Invalid finding_id for risk acceptance | Verify finding exists |

---

## See Also

- [AO Getting Started](../getting-started/ao.md) — First-time setup and first 3 commands
- [Persona Overview](../personas/index.md) — All personas, RACI matrix, and role definitions
- [RMF Phase Reference](../rmf-phases/index.md) — Phase-by-phase workflow details
- [Quick Reference Card](../reference/quick-reference-cards.md) — Printable AO cheat sheet

---

## POA&M for Authorization Decisions (Feature 039)

As an Authorizing Official, POA&M data informs your authorization decisions:

- **Trend Reports**: Use `compliance_poam_trend` to review open POA&M trends before making ATO decisions
- **Risk Posture**: Use `compliance_poam_metrics` to see total open, overdue, and severity breakdown
- **eMASS Export**: Request POA&M exports in eMASS format for inclusion in authorization packages
- **Dashboard**: Navigate to the POA&M Trends tab for visual analytics on closure rates and aging

---

## Authorization Package Review (Feature 041)

As the AO, you review and authorize the submission of authorization packages to eMASS:

- **Readiness check**: `compliance_validate_package` — verify all artifacts pass pre-submission validation
- **Generate package**: `compliance_generate_package` — bundle all artifacts into a ZIP for eMASS upload
- **Review SAR findings**: `compliance_package_status` — inspect artifacts, validation results, and SAR content
- **Package history**: `compliance_list_packages` — view previous packages and their validation status
- **Download**: Download completed packages from the Documents page for eMASS submission

> "Validate package readiness for [system name]"

> "Generate authorization package for [system name]"

---

## Capabilities Coverage KPI (Feature 045)

The Security Capabilities Hub provides a **Coverage %** KPI that tracks how many baseline controls are mapped to at least one security capability. This metric feeds into your Portfolio Risk Profile:

- **Coverage %**: Percentage of NIST controls with at least one capability mapping
- **Gap Controls**: Controls not yet mapped to any capability (higher risk for authorization decisions)
- **Provider Cards**: Per-CSP breakdown of controlled vs. total controls

Navigate to the [Capabilities Hub](/capabilities) dashboard to review coverage and identify gaps before authorization decisions.
