# RMF Phase 5: Authorize

> Provide accountability by requiring a senior official to determine if the security and privacy risk is acceptable.

---

## Phase Overview

| Attribute | Value |
|-----------|-------|
| **Phase Number** | 5 |
| **NIST Reference** | SP 800-37 Rev. 2, §3.6 |
| **Lead Persona** | AO |
| **Supporting Personas** | ISSM (package preparation), SCA (SAR delivery), ISSO (support) |
| **Key Outcome** | Authorization decision issued (ATO, ATOwC, IATT, or DATO) |

---

## Persona Responsibilities

### AO (Lead — Decision)

**Tasks in this phase**:

1. Review authorization package → Tool: `compliance_bundle_authorization_package`
2. Review risk register → Tool: `compliance_show_risk_register`
3. Issue authorization decision → Tool: `compliance_issue_authorization`
4. Accept risk on findings → Tool: `compliance_accept_risk`

**Natural Language Queries**:

> **"Show the authorization package summary for system {id}"** → `compliance_bundle_authorization_package` — reviews bundled package (SSP + SAR + RAR + POA&M + CRM)

> **"What's the compliance score and finding breakdown for system {id}?"** → score and CAT I/II/III counts

> **"Issue an ATO for system {id} expiring January 15, 2028 with Low residual risk — all CAT I findings remediated, 2 CAT III findings accepted"** → `compliance_issue_authorization` — records decision, transitions system to Monitor

> **"Issue an ATO with conditions for system {id} — MFA enforcement must be completed within 90 days"** → `compliance_issue_authorization` — ATOwC with tracked conditions

> **"Accept risk on finding {finding-id} for control CM-6 (CAT III) — configuration deviation documented"** → `compliance_accept_risk` — risk acceptance with compensating control and expiration

> **"Deny authorization for system {id} — 3 unmitigated CAT I findings"** → `compliance_issue_authorization` — DATO, system enters read-only mode

> **"Show the risk register for system {id}"** → `compliance_show_risk_register` — active/expired risk acceptances

> **"What risks have I accepted that are expiring soon?"** → `compliance_show_risk_register` — filtered by expiration

### Authorization Decision Types

| Type | Description | Expiration | System State After |
|------|-------------|------------|-------------------|
| **ATO** | Authority to Operate — full authorization | Required (typically 3 years) | Monitor phase |
| **ATOwC** | ATO with Conditions — with stipulations | Required | Monitor phase (conditions tracked) |
| **IATT** | Interim Authority to Test — limited scope | Required (typically 6 months) | Monitor phase (limited) |
| **DATO** | Denial of Authorization — cannot operate | None | Read-only mode, advancement blocked |

### Key Authorization Behaviors

- **Supersedes prior decisions**: Any existing active authorization is automatically deactivated
- **Compliance score captured**: Score at decision time is recorded permanently
- **RMF advancement**: System moves to Monitor phase on ATO/ATOwC/IATT
- **Open findings recorded**: All open findings at decision time are captured in the record
- **DATO effects**: System enters read-only mode, generates persistent alert, blocks RMF advancement

### Risk Acceptance Lifecycle

1. AO accepts risk with justification + compensating control + expiration date
2. Risk acceptance is active → finding severity is documented but accepted
3. Expiration date arrives → acceptance auto-expires, finding reverts to active
4. Linked POA&M items revert from `RiskAccepted` to `Ongoing`
5. Alert sent to both AO and ISSM

### ISSM (Package Preparation)

**Tasks in this phase**:

1. Review SSP section completeness → Tool: `compliance_ssp_completeness`
2. Export OSCAL SSP → Tool: `compliance_export_oscal_ssp`
3. Validate OSCAL SSP → Tool: `compliance_validate_oscal_ssp`
4. Verify privacy compliance → Tool: `compliance_check_privacy_compliance`
5. Validate interconnection agreements → Tool: `compliance_validate_agreements`
6. Bundle authorization package → Tool: `compliance_bundle_authorization_package`
7. Review risk register → Tool: `compliance_show_risk_register`

**Natural Language Queries**:

> **"Bundle the authorization package for system {id} including evidence"** → `compliance_bundle_authorization_package` — bundles SSP + SAR + RAR + POA&M + CRM + ATO Letter

> **"Export the OSCAL SSP for the authorization package"** → `compliance_export_oscal_ssp` — generates NIST OSCAL-compliant SSP document

> **"Validate the OSCAL SSP before submitting the package"** → `compliance_validate_oscal_ssp` — schema validation

> **"Is the SSP complete for system {id}?"** → `compliance_ssp_completeness` — all 13 sections must be Approved

> **"Check privacy compliance readiness"** → `compliance_check_privacy_compliance` — PTA/PIA status

> **"Are all interconnection agreements valid?"** → `compliance_validate_agreements` — ISA/MOU status check

> **"What documents are ready for the authorization package?"** → document readiness check

### SCA (SAR Delivery)

- Deliver final SAR to ISSM for inclusion in authorization package
- Available for questions from the AO during risk review

### ISSO (Support)

- Provide additional evidence or clarification on findings as requested

---

## Authorization Package Contents

| Document | Source | Required |
|----------|--------|----------|
| System Security Plan (SSP) | `compliance_generate_ssp` | Yes |
| OSCAL SSP Export | `compliance_export_oscal_ssp` | Recommended |
| Security Assessment Plan (SAP) | `compliance_generate_sap` (finalized) | Yes |
| Security Assessment Report (SAR) | `compliance_generate_sar` | Yes |
| Risk Assessment Report (RAR) | `compliance_generate_rar` | Yes |
| Plan of Action & Milestones (POA&M) | `compliance_list_poam` | Yes |
| Customer Responsibility Matrix (CRM) | `compliance_generate_crm` | Yes |
| Privacy Impact Assessment (PIA) | `compliance_generate_pia` (if PII) | Conditional |
| Interconnection Agreements (ISA/MOU) | `compliance_generate_isa` | Conditional |
| ATO Letter | Generated from authorization decision | After AO decision |

---

## Documents Produced

| Document | Owner | Format | Gate Dependency |
|----------|-------|--------|----------------|
| Authorization Decision Letter | AO | Generated | Authorize → Monitor |
| Risk Acceptance Memorandum | AO | Generated | Informational |
| Terms & Conditions (ATOwC) | AO | Generated | Informational |
| Authorization Package (bundled) | ISSM | ZIP | Informational |

---

## Phase Gates

| Gate | Condition | Checked By |
|------|-----------|-----------|
| Authorization issued | An authorization decision has been recorded | `compliance_advance_rmf_step` |
| SSP complete | All 13 SSP sections Approved | `compliance_ssp_completeness` |
| SAP finalized | SAP locked before assessment | `compliance_get_sap` |
| Privacy compliant | PTA complete; PIA approved (if applicable) | `compliance_check_privacy_compliance` |
| Interconnections valid | All ISA/MOUs active or no-interconnections certified | `compliance_validate_agreements` |
| OSCAL validated | OSCAL SSP passes schema validation (recommended) | `compliance_validate_oscal_ssp` |

---

## Transition to Next Phase

| Trigger | From Phase | To Phase | Handoff |
|---------|-----------|----------|---------|
| `compliance_advance_rmf_step` after ATO/ATOwC/IATT | Authorize | Monitor | Authorization active, ConMon plan needed |
| DATO issued | Authorize | — (blocked) | System in read-only mode, remediation required |

---

## See Also

- [Previous Phase: Assess](assess.md)
- [Next Phase: Monitor](monitor.md)
- [AO Guide](../guides/ao-quick-reference.md) — Full AO workflow documentation
- [ISSM Guide](../guides/issm-guide.md) — Package preparation workflows
- [POA&M Management Guide](../guides/poam-management.md) — POA&M exports for authorization packages

### POA&M Exports for Authorization (Feature 039)

During authorization, export POA&M data in eMASS Excel format for inclusion in the authorization package. Use `compliance_export_poam` with format `emass_excel` or the Export button on the POA&M dashboard.
