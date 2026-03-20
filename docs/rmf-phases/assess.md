# RMF Phase 4: Assess

> Assess the controls to determine if they are implemented correctly, operating as intended, and producing the desired outcome.

---

## Phase Overview

| Attribute | Value |
|-----------|-------|
| **Phase Number** | 4 |
| **NIST Reference** | SP 800-37 Rev. 2, §3.5 |
| **Lead Persona** | SCA |
| **Supporting Personas** | ISSO (evidence/remediation), ISSM (POA&M/package), Engineer (remediation) |
| **Key Outcome** | Controls assessed, SAR and RAR generated, POA&M items created |

---

## Assessment Approaches

The Assess phase uses **two distinct assessment tools** that serve different purposes:

| Tool | Purpose | Who Runs It | How It Works |
|------|---------|-------------|--------------|
| `compliance_assess` | **Automated scan** — runs NIST 800-53 assessment against live Azure resources | ISSO or ISSM | Queries Azure Policy, Defender for Cloud, and resource configurations; produces machine-generated findings |
| `compliance_assess_control` | **Manual determination** — records SCA's formal effectiveness finding per control | SCA | SCA evaluates evidence (test/interview/examine), then records Satisfied or Other Than Satisfied with CAT severity |

**Typical workflow**: The ISSO runs `compliance_assess` first to generate automated findings, then the SCA reviews those findings and records formal determinations using `compliance_assess_control`.

!!! tip "Scan Types for `compliance_assess`"
    - **quick** — Fast summary of compliance posture (minutes)
    - **policy** — Azure Policy evaluation with NIST 800-53 control mapping
    - **full** — Deep scan with remediation recommendations (may take longer)

---

## Persona Responsibilities

### SCA (Lead — Assessment)

**Tasks in this phase**:

1. Generate Security Assessment Plan → Tool: `compliance_generate_sap`
2. Customize assessment methods → Tool: `compliance_update_sap`
3. Finalize SAP → Tool: `compliance_finalize_sap`
4. Assess each control → Tool: `compliance_assess_control`
5. Take assessment snapshots → Tool: `compliance_take_snapshot`
6. Verify evidence integrity → Tool: `compliance_verify_evidence`
7. Check evidence completeness → Tool: `compliance_check_evidence_completeness`
8. Compare assessment cycles → Tool: `compliance_compare_snapshots`
9. Generate SAR → Tool: `compliance_generate_sar`
10. Generate RAR → Tool: `compliance_generate_rar`
11. Validate OSCAL SSP → Tool: `compliance_validate_oscal_ssp`

**Natural Language Queries**:

> **"Assess control AC-2 as Satisfied using the Test method — account management procedures verified"** → `compliance_assess_control` — records determination with method and justification

> **"Assess control AC-3 as Other Than Satisfied, CAT II — mandatory access control checks missing"** → `compliance_assess_control` — records finding with DoD CAT severity

> **"Take a snapshot of system {id} before assessment begins"** → `compliance_take_snapshot` — creates immutable SHA-256-hashed snapshot

> **"Verify evidence {evidence-id} hasn't been tampered with"** → `compliance_verify_evidence` — recomputes hash, returns verified or tampered

> **"Compare snapshot {snap-1} with snapshot {snap-2}"** → `compliance_compare_snapshots` — shows score delta, new/resolved findings

> **"Generate the Security Assessment Report for system {id}"** → `compliance_generate_sar` — SAR with executive summary and CAT breakdown

> **"Generate the Risk Assessment Report for system {id}"** → `compliance_generate_rar` — RAR with per-family risk breakdown

> **"Generate a security assessment plan for system {id}"** → `compliance_generate_sap` — SAP with scope, methodology, and schedule

> **"Finalize the SAP"** → `compliance_finalize_sap` — locks SAP with SHA-256 hash, no further edits

> **"Validate the OSCAL SSP for system {id}"** → `compliance_validate_oscal_ssp` — validates exported OSCAL against schema

!!! info "Air-Gapped Note"
    All SCA assessment tools work fully offline — they operate on locally stored assessment data. Evidence collection (`compliance_collect_evidence`) requires network access to Azure resources; in air-gapped environments, evidence must be imported from prior scans or manual artifact uploads.

### Assessment Methods

| Method | When to Use | Examples |
|--------|-------------|---------|
| **Test** | Execute procedures, observe behavior | Run scans, test access controls |
| **Interview** | Question personnel about practices | Ask admin about account review |
| **Examine** | Review documentation and artifacts | Review SSP, audit logs, policies |

### DoD CAT Severity Mapping

| CAT Level | Severity | Impact |
|-----------|----------|--------|
| **CAT I** | Critical/High | Direct loss of C/I/A — immediate exploitation risk |
| **CAT II** | Medium | Potential for system compromise |
| **CAT III** | Low | Administrative or documentation gaps |

### ISSO (Support — Evidence, Automated Scan & Remediation)

**Tasks in this phase**:

1. Import CKL scan results → Tool: `compliance_import_ckl`
2. Import XCCDF scan results → Tool: `compliance_import_xccdf`
3. Run automated compliance assessment → Tool: `compliance_assess`
4. Collect evidence → Tool: `compliance_collect_evidence`
5. Import Prisma Cloud scans → Tool: `compliance_import_prisma_csv`, `compliance_import_prisma_api`
6. Import ACAS/Nessus vulnerability scans → Tool: `compliance_import_nessus`, `compliance_list_nessus_imports`
7. Write SSP sections → Tool: `compliance_write_ssp_section`
7. Check SSP completeness → Tool: `compliance_ssp_completeness`
8. Export CKL for external review → Tool: `compliance_export_ckl`
9. Create remediation board → Tool: `kanban_create_board`
10. Assign tasks to engineers → Tool: `kanban_assign_task`
11. Fix alerts → Tool: `watch_fix_alert`

**Natural Language Queries**:

> **"Run a full NIST 800-53 assessment on subscription {sub-id}"** → `compliance_assess` — automated scan across all control families against live Azure resources

> **"Run a quick compliance scan on subscription {sub-id} for the AC and IA families"** → `compliance_assess` — scoped quick scan for specific families

> **"Collect evidence for the AC family on subscription {sub-id}"** → `compliance_collect_evidence` — collects Azure resource evidence with SHA-256 hashing

> **"Create a remediation board from the latest assessment"** → `kanban_create_board` — creates Kanban board from findings

> **"Assign task REM-003 to engineer Bob Jones"** → `kanban_assign_task` — assigns remediation work

> **"Import the CKL checklist for the Windows Server 2022 STIG"** → `compliance_import_ckl` — maps STIG findings to NIST controls

> **"Import SCAP scan results for system {id}"** → `compliance_import_xccdf` — parses XCCDF automated scan output

> **"Write SSP section 5 (System Environment) for system {id}"** → `compliance_write_ssp_section` — authors NIST 800-18 SSP section

> **"What is the SSP completeness for system {id}?"** → `compliance_ssp_completeness` — per-section status and completion percentage

> **"Export a CKL checklist for eMASS upload"** → `compliance_export_ckl` — generates DISA STIG Viewer compatible file

### ISSM (Support — POA&M & Package)

**Tasks in this phase**:

1. Create POA&M items → Tool: `compliance_create_poam`
2. List/track POA&M → Tool: `compliance_list_poam`
3. Generate RAR → Tool: `compliance_generate_rar`

**Natural Language Queries**:

> **"Create a POA&M item for the missing MFA finding on IA-2(1) — assign to John Smith, due June 30"** → `compliance_create_poam` — formal POA&M with milestones and CAT severity

> **"List overdue POA&M items for system {id}"** → `compliance_list_poam` — filtered POA&M list

### Engineer (Support — Remediation)

Engineers can remediate findings using **standalone tools** (direct finding remediation) or **Kanban tools** (task-managed remediation). Both paths are valid — Kanban adds task tracking and audit trails.

**Standalone remediation tools**:

1. Generate remediation plan → Tool: `compliance_generate_plan`
2. Remediate finding → Tool: `compliance_remediate`
3. Validate fix → Tool: `compliance_validate_remediation`

**Kanban-managed remediation tools**:

4. View assigned tasks → Tool: `kanban_task_list`
5. Fix findings → Tool: `kanban_remediate_task`
6. Validate fixes → Tool: `kanban_task_validate`
7. Collect evidence → Tool: `kanban_collect_evidence`

**Natural Language Queries**:

> **"Generate a remediation plan for subscription {sub-id}"** → `compliance_generate_plan` — prioritized plan across findings

> **"Remediate finding {finding-id} with dry run"** → `compliance_remediate` — preview fix before applying

> **"Validate remediation for finding {finding-id}"** → `compliance_validate_remediation` — re-scan to confirm fix

> **"Show my assigned remediation tasks"** → `kanban_task_list` — filtered to assigned user

> **"Fix task REM-005 with dry run first"** → `kanban_remediate_task` — preview before applying

> **"Validate task REM-005"** → `kanban_task_validate` — re-scan to verify remediation

---

## Typical SCA Assessment Cycle

```
 1. compliance_generate_sap          ← SCA generates Security Assessment Plan
 2. compliance_update_sap            ← SCA customizes methodology per control (optional)
 3. compliance_finalize_sap          ← SCA locks SAP — assessment scope finalized
 4. compliance_import_ckl            ← ISSO imports STIG CKL scan results
 5. compliance_import_xccdf          ← ISSO imports SCAP XCCDF scan results
 6. compliance_assess                ← ISSO runs automated scan (quick/policy/full)
 7. compliance_collect_evidence      ← ISSO collects evidence from Azure
 8. compliance_import_prisma_csv     ← ISSO imports Prisma Cloud scan results
 9. compliance_write_ssp_section     ← ISSO authors SSP sections
10. compliance_assess_control        ← SCA formally assesses each control (batch)
11. compliance_take_snapshot         ← SCA snapshots current state
12. compliance_verify_evidence       ← SCA spot-checks evidence integrity
13. compliance_check_evidence_completeness ← SCA verifies coverage
14. compliance_ssp_completeness      ← SCA verifies SSP readiness
15. compliance_generate_sar          ← SCA generates SAR
    ── (Remediation occurs) ──
16. compliance_assess                ← ISSO re-runs automated scan
17. compliance_import_prisma_csv     ← ISSO re-imports post-remediation Prisma scan
18. compliance_prisma_trend          ← SCA validates remediation progress
19. compliance_assess_control        ← SCA re-assesses remediated controls
20. compliance_take_snapshot         ← SCA snapshots after remediation
21. compliance_compare_snapshots     ← SCA shows improvement
22. compliance_validate_oscal_ssp    ← SCA validates OSCAL SSP export
23. compliance_generate_sar          ← SCA produces updated SAR for AO
24. compliance_generate_rar          ← SCA/ISSM produces final RAR
```

### Prisma Cloud Scan Import as Assessment Input

Step 8 above introduces Prisma Cloud as an assessment data source alongside STIG/SCAP imports. Prisma imports:

- Create `ComplianceFinding` records with `Source="Prisma Cloud"` and `ScanSource=Cloud`
- Auto-generate `ControlEffectiveness` records for in-baseline NIST controls
- Create `ComplianceEvidence` (type `CloudScanResult`) linked to the assessment
- Support both CSV (console export) and API JSON (programmatic) formats

This is the primary stage for initial Prisma import. For ongoing monitoring, see the [Monitor Phase](monitor.md).

---

## Documents Produced

| Document | Owner | Format | Gate Dependency |
|----------|-------|--------|----------------|
| Security Assessment Report (SAR) | SCA | Markdown | Advisory (Assess → Authorize) |
| Risk Assessment Report (RAR) | SCA / ISSM | Markdown | Advisory |
| Plan of Action & Milestones (POA&M) | ISSM | Markdown | Informational |
| Assessment Snapshots | SCA | Immutable records | Informational |

---

## Phase Gates

| Gate | Condition | Checked By |
|------|-----------|-----------|
| Advisory | No hard block — advancement allowed regardless of assessment completion | `compliance_advance_rmf_step` |

---

## Transition to Next Phase

| Trigger | From Phase | To Phase | Handoff |
|---------|-----------|----------|---------|
| `compliance_advance_rmf_step` (advisory gate) | Assess | Authorize | SAR, RAR, POA&M bundled for AO decision |

---

## See Also

- [Previous Phase: Implement](implement.md)
- [Next Phase: Authorize](authorize.md)
- [SCA Guide](../guides/sca-guide.md) — Full SCA assessment workflow
- [ISSO Guide](../personas/isso.md) — Evidence collection and remediation
- [Remediation Kanban Guide](../guides/remediation-kanban.md) — Task management
- [POA&M Management Guide](../guides/poam-management.md) — Finding-driven POA&M auto-creation and lifecycle

### Assessment-Driven POA&M Creation (Feature 039)

After completing assessments, findings with CAT I/II/III severity can be automatically converted to POA&M items using `compliance_bulk_create_poam_from_findings`. The post-import prompt on the dashboard offers one-click bulk creation with deduplication.

### SAR Generation from Findings (Feature 041)

Generate a Security Assessment Report summarizing assessment findings:

- `compliance_generate_sar` — creates a new SAR auto-populated from assessment data
- `compliance_edit_sar_section` — edit individual SAR sections (Executive Summary, Methodology, Findings, Recommendations)
- `compliance_review_sar` — submit for review and approve
