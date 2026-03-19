# Security Control Assessor (SCA) Guide

> Feature 015 — Phase 9: Assessment Artifacts & CAT Severity

This guide walks Security Control Assessors through the assessment workflow using ATO Copilot's MCP tools.

!!! tip "New to ATO Copilot?"
    If this is your first time using ATO Copilot as an SCA, start with the [SCA Getting Started](../getting-started/sca.md) page for prerequisites, first-time setup, and your first 3 commands.

---

## Overview

As an SCA, you record per-control effectiveness determinations, map findings to DoD CAT severity levels, take immutable assessment snapshots, verify evidence chain of custody, and generate Security Assessment Reports (SARs).

!!! warning "Read-Only Role"
    As SCA you have **read-only** access. You cannot modify narratives, fix findings, or issue authorization decisions. If you attempt a write operation, ATO Copilot will return an RBAC denial with explanation.

### RBAC Constraints

| Tool | Access | Notes |
|------|--------|-------|
| `compliance_assess_control` | ✅ Allowed | SCA's primary function |
| `compliance_take_snapshot` | ✅ Allowed | Creates immutable records |
| `compliance_verify_evidence` | ✅ Allowed | Evidence integrity verification |
| `compliance_check_evidence_completeness` | ✅ Allowed | Coverage reporting |
| `compliance_compare_snapshots` | ✅ Allowed | Trend analysis |
| `compliance_generate_sar` | ✅ Allowed | SAR generation |
| `compliance_generate_rar` | ✅ Allowed | RAR generation |
| `compliance_import_nessus` | ✅ Allowed | ACAS/Nessus vulnerability scan import |
| `compliance_list_nessus_imports` | ✅ Allowed | Nessus import history |
| `compliance_write_narrative` | ❌ Denied | SCA cannot modify SSP |
| `compliance_remediate` | ❌ Denied | SCA cannot fix findings |
| `compliance_issue_authorization` | ❌ Denied | Only AO can authorize |
| `watch_dismiss_alert` | ❌ Denied | Cannot dismiss alerts |

### Prerequisite Workflow

Before assessment, the system should have completed:

1. **Registration** — `compliance_register_system`
2. **Categorization** — `compliance_categorize_system` (FIPS 199)
3. **Baseline Selection** — `compliance_select_baseline` (NIST 800-53)
4. **SSP Authoring** — `compliance_write_narrative` / `compliance_generate_ssp`

---

## 1. Assess Controls

Use `compliance_assess_control` to record your determination for each control.

### Satisfied Determination

```
assessment_id: "assess-123"
control_id: "AC-2"
determination: "Satisfied"
method: "Test"
notes: "Account management procedures verified against STIG checklist."
```

### OtherThanSatisfied with CAT Severity

When a control is not satisfied, you **must** specify a CAT severity level:

| CAT Level | Impact | Examples |
|-----------|--------|----------|
| **CAT I** | Critical — direct risk to availability, integrity, or confidentiality | Missing encryption, no access control enforcement |
| **CAT II** | Significant — potential for system compromise | Weak password policies, incomplete logging |
| **CAT III** | Low — administrative or documentation gaps | Missing audit review procedures, incomplete labels |

```
assessment_id: "assess-123"
control_id: "AC-3"
determination: "OtherThanSatisfied"
method: "Test"
cat_severity: "CatII"
notes: "Mandatory access control checks missing for privileged operations."
```

### Assessment Methods

| Method | When to Use |
|--------|-------------|
| `Test` | Execute procedures, observe behavior, validate configurations |
| `Interview` | Question personnel about policies, procedures, practices |
| `Examine` | Review documentation, logs, configuration artifacts |

### Linking Evidence

Attach evidence IDs to your assessment:

```
evidence_ids: "ev-001,ev-002,ev-003"
```

---

## 2. Take Assessment Snapshots

After completing a batch of assessments, create an immutable snapshot:

```
system_id: "sys-456"
assessment_id: "assess-123"
```

This creates a **tamper-proof record** with:
- SHA-256 integrity hash over all determinations, findings, and evidence
- Timestamp of capture
- Compliance score at that point in time
- Immutable flag (cannot be updated or deleted)

**Best Practice:** Take snapshots at key milestones — after each assessment cycle, before/after remediation, and before authorization decisions.

---

## 3. Verify Evidence Integrity

Use `compliance_verify_evidence` to verify that evidence content hasn't been tampered with:

```
evidence_id: "ev-001"
```

The tool recomputes the SHA-256 hash and compares it with the stored hash:
- **verified** — Hash matches, evidence is authentic
- **tampered** — Hash differs, evidence content was modified after collection

---

## 4. Check Evidence Completeness

Before generating a SAR, verify all controls have supporting evidence:

```
system_id: "sys-456"
assessment_id: "assess-123"
family_filter: "AC"  (optional — filter to one family)
```

The report shows:
- Overall completeness percentage
- Per-control status: `verified`, `unverified`, or `missing`
- Controls lacking any evidence

!!! tip "Dashboard Evidence Repository"
    The **Evidence** page in the Compliance Dashboard provides a complementary visual interface for reviewing evidence. Navigate to a system's Evidence page to browse all uploaded and automated evidence artifacts, verify SHA-256 hashes, filter by control family or category, and review version history — without using MCP commands.

---

## 5. Compare Assessment Cycles

After remediation and reassessment, compare snapshots to track progress:

```
snapshot_id_a: "snap-001"  (before remediation)
snapshot_id_b: "snap-002"  (after remediation)
```

The comparison shows:
- Score delta (improvement or regression)
- Controls newly satisfied
- Controls newly other-than-satisfied
- New and resolved findings
- Evidence changes

---

## 6. Generate Security Assessment Report (SAR)

Generate the SAR for authorization decision-makers:

```
system_id: "sys-456"
assessment_id: "assess-123"
format: "markdown"
```

The SAR includes:
1. **Executive Summary** — System, assessor, overall score, finding counts
2. **CAT Severity Breakdown** — CAT I/II/III counts with risk categorization
3. **Control Family Results** — Per-family pass/fail with CAT mapping
4. **Risk Summary** — Assessment-to-authorization risk posture
5. **Detailed Findings** — Per-control determination details

---

## Typical SCA Workflow

```
1. compliance_assess_control  ← Assess each control (batch)
2. compliance_take_snapshot   ← Snapshot current state
3. compliance_verify_evidence ← Spot-check evidence integrity
4. compliance_check_evidence_completeness ← Verify coverage
5. compliance_generate_sar    ← Generate SAR
6. (Remediation occurs)
7. compliance_assess_control  ← Re-assess remediated controls
8. compliance_take_snapshot   ← Snapshot after remediation
9. compliance_compare_snapshots ← Show improvement
10. compliance_generate_sar   ← Updated SAR for AO decision
```

---

## RBAC Requirements

| Tool | Required Role |
|------|--------------|
| `compliance_assess_control` | `Compliance.Auditor` (SCA) |
| `compliance_take_snapshot` | `Compliance.Auditor` |
| `compliance_verify_evidence` | `Compliance.Auditor` |
| `compliance_check_evidence_completeness` | Any compliance role |
| `compliance_compare_snapshots` | Any compliance role |
| `compliance_generate_sar` | `Compliance.Auditor` |

---

## Evidence Integrity

All evidence collected by ATO Copilot is integrity-protected:

- **Collection**: Each evidence item receives a SHA-256 hash at collection time
- **Verification**: `compliance_verify_evidence` recomputes the hash and compares it to the stored value
- **Tamper detection**: If the hash differs, the evidence is flagged as `tampered`
- **Immutable snapshots**: Assessment snapshots are immutable — they cannot be updated or deleted after creation

---

## Air-Gapped Environment Notes

!!! info "Assess Phase — Disconnected Environments"
    All SCA assessment tools work fully offline:
    
    - `compliance_assess_control`, `compliance_take_snapshot`, `compliance_verify_evidence`, `compliance_compare_snapshots`, `compliance_generate_sar`, `compliance_generate_rar` — all operate on locally stored assessment data.
    - **Evidence collection** (`compliance_collect_evidence`) requires network access to Azure resources. In air-gapped environments, evidence must be imported from prior scans or manual artifact uploads.

---

## SCAP/STIG Import for Assessment

> Feature 017: SCAP/STIG Viewer Import

As an SCA, you can leverage imported CKL and XCCDF data to inform your control assessments. Imported scan results automatically create compliance findings with STIG-to-NIST mappings, providing evidence for your assessment determinations.

### Using Imported Scan Data

After the ISSM imports CKL/XCCDF files, findings are automatically created with:
- **CAT severity** mapped from STIG rules
- **NIST control mapping** via CCI cross-references
- **Control effectiveness** auto-determined based on aggregate finding status

### Reviewing Import Results

```
Tool: compliance_list_imports
Parameters:
  system_id: "<system-guid>"
```

For per-finding detail:

```
Tool: compliance_get_import_summary
Parameters:
  import_id: "<import-record-id>"
```

### Assessment Workflow with STIG Imports

```
1. Review imported findings via compliance_list_imports
2. compliance_assess_control  ← Use imported STIG data as evidence
3. compliance_take_snapshot   ← Capture state including imported findings
4. compliance_generate_sar    ← SAR includes STIG-based severity data
```

---

## Assess Controls Using Prisma Cloud Data

Prisma Cloud scan imports automatically create `ControlEffectiveness` records for in-baseline NIST controls, providing automated assessment evidence alongside traditional STIG data.

### How Prisma Findings Auto-Populate ControlEffectiveness

When a Prisma import is processed, each import creates or updates effectiveness records for affected NIST 800-53 controls:

- **Open Prisma alerts** → `OtherThanSatisfied` determination with CAT severity
- **All Prisma alerts resolved** for a control → `Satisfied` determination
- Evidence is automatically linked from the import's `CloudScanResult` evidence record

### Using Trend Data for Remediation Validation

After remediation work, use `compliance_prisma_trend` to validate improvements:

```
Tool: compliance_prisma_trend
Parameters:
  system_id: "<system-guid>"
  group_by: "nist_control"
```

The `remediationRate` shows the percentage of previously-open findings now resolved. Use `resolvedFindings` vs `newFindings` to track net compliance improvement.

### Combined STIG + Prisma Evidence Review

For each control, review both STIG and Prisma evidence together:

```
1. compliance_get_baseline (system_id) ← List all in-scope controls
2. compliance_list_imports (system_id) ← Review both CKL/XCCDF and Prisma imports
3. compliance_assess_control           ← Evaluate control using combined evidence
4. compliance_generate_sar             ← SAR aggregates STIG + Prisma effectiveness
```

Controls assessed by both STIG and Prisma sources provide stronger evidence for AO review.

### Exporting Assessment State

Export the current assessment state as a CKL checklist for external review:

```
Tool: compliance_export_ckl
Parameters:
  system_id: "<system-guid>"
  benchmark_id: "Windows_Server_2022_STIG"
```

The exported CKL file is compatible with DISA STIG Viewer and eMASS.

---

## SAP Generation & Finalization

> Feature 018: SAP Generation

SCAs generate and finalize the Security Assessment Plan before beginning control assessments.

### Generating a SAP

```
Tool: compliance_generate_sap
Parameters:
  system_id: "<system-guid>"
```

The generated SAP includes assessment scope, methodology, schedule, and team composition derived from system metadata and baseline controls.

### Reviewing and Updating

```
Tool: compliance_get_sap
Parameters:
  sap_id: "<sap-guid>"
```

If changes are needed:

```
Tool: compliance_update_sap
Parameters:
  sap_id: "<sap-guid>"
  updates: { "methodology": "updated text..." }
```

### Finalizing the SAP

```
Tool: compliance_finalize_sap
Parameters:
  sap_id: "<sap-guid>"
```

Finalization locks the SAP — no further edits are allowed. This is required before assessment can begin.

### SCA Assessment Workflow with SAP

```
1. compliance_generate_sap        ← Create SAP from system metadata
2. compliance_update_sap           ← Refine scope/methodology
3. compliance_finalize_sap         ← Lock SAP for assessment
4. compliance_assess_control       ← Begin control assessments
5. compliance_generate_sar         ← SAR references finalized SAP
```

---

## Privacy Compliance Check

> Feature 021: Privacy & Interconnections

SCAs can verify that privacy artifacts (PTA, PIA) are complete and consistent as part of the authorization package review.

### Checking Privacy Compliance

```
Tool: compliance_check_privacy_compliance
Parameters:
  system_id: "<system-guid>"
```

Returns a checklist of privacy requirements with pass/fail status:
- PTA completed
- PIA required and submitted (if PTA indicates PII)
- PIA reviewed and approved by ISSM
- Privacy controls addressed in SSP

---

## SSP Completeness Verification

> Feature 022: SSP Authoring & OSCAL Export

SCAs can review SSP completeness before generating the SAR and authorization package.

### Checking SSP Completeness

```
Tool: compliance_ssp_completeness
Parameters:
  system_id: "<system-guid>"
```

Returns a per-section breakdown of the 13-section NIST 800-18 SSP:
- Section status: Draft, InReview, Approved, or NotStarted
- Overall completeness percentage
- Sections requiring attention before authorization
- **Staleness warnings** for unapproved narratives (Feature 024)

### Narrative Approval Progress

SCAs can review narrative governance status to verify control narratives are approved before assessment:

```
Tool: compliance_narrative_approval_progress
Parameters:
  system_id: "<system-guid>"
```

Returns overall approval percentage, per-family breakdown, review queue of controls awaiting ISSM review, and staleness warnings for unapproved narratives.

### OSCAL Validation

After ISSM exports the OSCAL SSP, SCAs can validate the output:

```
Tool: compliance_validate_oscal_ssp
Parameters:
  system_id: "<system-guid>"
```

Validates the OSCAL SSP document against the NIST OSCAL schema and reports any structural or content issues.

### Pre-Authorization Package Checklist

Before generating the SAR, verify:

```
1. compliance_ssp_completeness     ← All 13 sections Approved
2. compliance_check_privacy_compliance ← Privacy artifacts complete
3. compliance_validate_agreements  ← Interconnections validated (ISSM)
4. compliance_generate_sar         ← SAR includes all evidence sources
```

---

## Inventory Verification

> **Feature 025** — Before assessment, verify that the HW/SW inventory is complete and accurate.

### Check Inventory Completeness

```
Tool: inventory_completeness
Parameters: { "system_id": "{system-id}" }
```

Review the completeness report for:
- Items with missing required fields
- Boundary resources without corresponding inventory entries
- Hardware items without any registered software

### Review Inventory List

```
Tool: inventory_list
Parameters: {
  "system_id": "{system-id}",
  "type": "hardware"
}
```

Cross-reference against the authorization boundary to verify all in-scope resources are registered.

### Pre-Assessment Inventory Checklist

```
1. inventory_completeness             ← Verify is_complete = true
2. inventory_list (type=hardware)     ← Verify HW entries match boundary
3. inventory_list (type=software)     ← Verify SW entries with versions
4. inventory_export                   ← Archive inventory snapshot
```

---

## See Also

- [SCA Getting Started](../getting-started/sca.md) — First-time setup and first 3 commands
- [Persona Overview](../personas/index.md) — All personas, RACI matrix, and role definitions
- [RMF Phase Reference](../rmf-phases/index.md) — Phase-by-phase workflow details
- [Quick Reference Card](../reference/quick-reference-cards.md) — Printable SCA cheat sheet

---

## Assessment-Driven POA&M Creation (Feature 039)

After completing security control assessments, SCAs can trigger automatic POA&M generation:

- **Auto-Create from Findings**: After importing scan results, use the post-import prompt or `compliance_bulk_create_poam_from_findings` to auto-generate POA&M items
- **Deduplication**: Items are not created for findings that already have POA&M entries
- **Severity Mapping**: CAT severity is mapped from finding severity to POA&M category
- **Component Linkage**: Auto-generated POA&M items inherit component associations from the originating findings
