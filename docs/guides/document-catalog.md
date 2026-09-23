# Document Production Catalog

> Complete reference of all documents Security Posture Intelligence Navigator can produce, including format options, owning personas, and generation tools.

---

## Document Summary

| Document | Abbreviation | Standard | Who Produces | When Produced | Output Formats |
|----------|-------------|----------|-------------|---------------|----------------|
| System Security Plan | SSP | NIST SP 800-18, DoDI 8510.01 | ISSO / ISSM | Implement phase | Markdown, PDF, DOCX |
| Security Assessment Report | SAR | NIST SP 800-53A | SCA | Assess phase | Markdown, PDF, DOCX |
| Risk Assessment Report | RAR | DoDI 8510.01 | SCA / ISSM | Assess phase | Markdown, PDF, DOCX |
| Plan of Action & Milestones | POA&M | OMB A-130, DoDI 8510.01 | ISSM | Assess → Monitor | Markdown, PDF, Excel |
| Customer Responsibility Matrix | CRM | FedRAMP, DoDI 8510.01 | ISSM | Select phase | Markdown, PDF, DOCX |
| Authorization Decision Letter | ATO Letter | DoDI 8510.01 | AO (via tool) | Authorize phase | Markdown, PDF, DOCX |
| Continuous Monitoring Report | ConMon | NIST SP 800-137 | ISSM | Monitor (periodic) | Markdown, PDF, DOCX |
| Authorization Package | — | DoDI 8510.01 | ISSM | Authorize phase | ZIP bundle |
| eMASS Export | — | DISA eMASS format | ISSM | Any phase | Excel (.xlsx) |
| OSCAL Export | — | NIST OSCAL v1.0.6 | ISSM | Any phase | JSON |

---

## System Security Plan (SSP)

**Tool**: `compliance_generate_ssp`

The SSP is the primary authorization document containing system description, categorization, control baseline, and all control implementation narratives.

### SSP Sections

| Section | Content |
|---------|---------|
| 1. System Information | Name, type, mission criticality, hosting, RMF phase, boundary |
| 2. Security Categorization | FIPS 199 notation, C/I/A impacts, DoD IL, info types |
| 3. Control Baseline | Level, overlay, total controls, tailoring, inheritance coverage |
| 4. Control Implementations | Per-family controls with narrative text, status, inheritance, STIG mappings |

### Generation Options

```
"Generate the SSP for system {id}"                    → Markdown
"Generate the SSP as PDF"                             → PDF via QuestPDF
"Generate SSP using our DISA template"                → DOCX with custom template
"Generate SSP sections: system_information,categorization" → Partial generation
```

---

## Security Assessment Report (SAR)

**Tool**: `compliance_generate_sar`

The SAR documents the SCA's assessment of control effectiveness, including finding severity and overall risk posture.

### SAR Sections

| Section | Content |
|---------|---------|
| 1. Executive Summary | System ID, assessor, overall score, total findings |
| 2. CAT Severity Breakdown | CAT I/II/III counts with risk categorization |
| 3. Control Family Results | Per-family pass/fail with CAT mapping |
| 4. Risk Summary | Assessment-to-authorization risk posture |
| 5. Detailed Findings | Per-control determination, method, evidence, notes |

---

## Risk Assessment Report (RAR)

**Tool**: `compliance_generate_rar`

The RAR provides the risk analysis that informs the AO's authorization decision.

### RAR Sections

| Section | Content |
|---------|---------|
| 1. Executive Summary | Aggregate risk level, recommendation |
| 2. Per-Family Risk | Risk score by NIST control family |
| 3. Threat/Vulnerability Analysis | Finding counts by severity category |
| 4. Residual Risk | Accepted risks, compensating controls |

---

## Plan of Action & Milestones (POA&M)

**Tool**: `compliance_create_poam`, `compliance_list_poam`

The POA&M tracks identified weaknesses and planned remediation activities.

### DoD-Required Fields

| Field | Description |
|-------|-------------|
| POA&M ID | Auto-generated identifier |
| Weakness | Description of the finding |
| Security Control | NIST 800-53 control ID |
| CAT Severity | CAT I, CAT II, or CAT III |
| Point of Contact | Responsible individual |
| Resources Required | Budget, personnel, tools needed |
| Scheduled Completion | Target remediation date |
| Milestones | Target dates for intermediate steps |
| Status | Ongoing, Completed, Delayed, Risk Accepted |

---

## Customer Responsibility Matrix (CRM)

**Tool**: `compliance_generate_crm`

The CRM documents which controls are inherited, shared, or customer-responsible, grouped by NIST control family.

### Inheritance Categories

| Type | Meaning |
|------|---------|
| **Inherited** | Fully satisfied by the Cloud Service Provider |
| **Shared** | Partially CSP, partially customer |
| **Customer** | Entirely the customer's responsibility |

---

## Authorization Decision Letter

**Generated from**: `compliance_issue_authorization`

The ATO Letter is automatically generated when the AO issues an authorization decision. It includes the decision type, residual risk level, justification, expiration date, and any terms and conditions.

---

## Continuous Monitoring Report (ConMon)

**Tool**: `compliance_generate_conmon_report`

Periodic reports generated per the ConMon plan (monthly, quarterly, or annual).

### Report Contents

- Compliance score and delta from previous period
- New and resolved findings
- POA&M status summary
- Watch alert summary
- Significant changes reported
- Recommendations

---

## Authorization Package

**Tool**: `compliance_bundle_authorization_package`

Bundles all required documents into a single deliverable for the AO:

| Included Document | Required |
|------------------|----------|
| SSP | Yes |
| SAR | Yes |
| RAR | Yes |
| POA&M | Yes |
| CRM | Yes |
| ATO Letter | After decision |

---

## Export Formats

### eMASS Export

**Tool**: `compliance_export_emass`

Generates an Excel spreadsheet in DISA eMASS-compatible format for import into the Enterprise Mission Assurance Support Service.

!!! info "Air-Gapped Note"
    `compliance_export_emass` generates the file locally. In air-gapped environments, manual transfer to eMASS via removable media is required.

### OSCAL Export

**Tool**: `compliance_export_oscal`

Generates NIST OSCAL v1.0.6 JSON for interoperability with other GRC tools. Works fully offline.

---

## Template System

Security Posture Intelligence Navigator supports two template modes:

### Default Format

Built-in compliant format covering all DoDI 8510.01 required sections. Not locked to a specific DISA template revision.

### Custom Organizational Templates

Organizations can upload DOCX templates with merge field placeholders:

| Merge Field | Data Source |
|-------------|------------|
| `{{system_name}}` | Registered system name |
| `{{system_acronym}}` | System acronym |
| `{{system_type}}` | System type classification |
| `{{categorization}}` | FIPS 199 overall level |
| `{{impact_level}}` | DoD Impact Level |
| `{{fips_notation}}` | Full FIPS 199 notation |
| `{{control_implementations}}` | All control narratives by family |
| `{{baseline_summary}}` | Baseline level, count, tailoring |
| `{{findings_summary}}` | Finding counts by CAT severity |
| `{{poam_items}}` | POA&M items with status |
| `{{risk_acceptances}}` | Active risk acceptances |

**Template Workflow**:

```
1. Upload:   compliance_upload_template(name="DISA SSP v3", document_type="ssp", ...)
2. List:     compliance_list_templates(document_type="ssp")
3. Generate: compliance_generate_ssp(system_id="...", format="docx", template="<id>")
```

See the [Administrator Guide](../personas/administrator.md) for template management details.

---

## See Also

- [RMF Phase Reference](../rmf-phases/index.md) — When each document is produced
- [Persona Overview](../personas/index.md) — Who produces each document
- [Administrator Guide](../personas/administrator.md) — Template management
