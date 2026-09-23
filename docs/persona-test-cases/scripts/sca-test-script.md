# SCA Persona Test Execution Script

**Feature**: 020 | **Persona**: SCA (Security Control Assessor)
**Role**: `Compliance.Auditor` | **Interface**: Microsoft Teams
**Test Cases**: SCA-01 through SCA-29 (29 total)

---

## Pre-Execution Setup

### T021 — Role Activation & Interface Switch

1. **Deactivate ISSO role** (if active): `@ato Deactivate my Analyst role`
2. **Activate SCA role**: `Activate my Compliance.Auditor role for 4 hours — persona test suite`
3. **Verify role**: `Show my active PIM roles` → Confirm `Compliance.Auditor` is active
4. **Switch to Teams**: Open Microsoft Teams with Security Posture Intelligence Navigator bot
5. **Verify access**: `Show system details for Eagle Eye` → Should return system in Implement/Assess phase

### Preconditions from ISSO Phase

- ✓ Eagle Eye has SSP narratives at high completion % (ISSO-01 through ISSO-07)
- ✓ CKL, XCCDF, and Nessus scans imported (ISSO-09, ISSO-10, ISSO-12a)
- ✓ Evidence collected for key controls (ISSO-19)
- ✓ Monitoring active (ISSO-13)
- ✓ Prisma imports completed (ISSM-19, ISSM-20)
- ✓ SAP finalized (ISSM-43)

---

## Phase 4 — Assess (SCA-01 to SCA-20)

### SCA-01: Take Pre-Assessment Snapshot

**Task**: Create immutable baseline for comparison
**Type**: Positive test | **Precondition**: System in Assess phase

```text
Take an assessment snapshot for Eagle Eye before I begin the assessment
```

**Expected Tool**: `compliance_take_snapshot`
**Expected Output**:
- Immutable snapshot created with timestamp
- `snapshot_id` returned
- Captures current control states

**Verification**: snapshot_id is a valid GUID
**Record**: pre_snapshot_id = _______________

---

### SCA-02: View System Baseline

**Task**: Review baseline before assessment
**Type**: Positive test | **Precondition**: System with baseline

```text
Show the baseline for Eagle Eye
```

**Expected Tool**: `compliance_get_baseline`
**Expected Output**:
- Baseline level = Moderate
- Total controls count
- Tailored controls
- Inheritance summary

**Verification**: Baseline = Moderate, controls ≈ 324

---

### SCA-03: View System Categorization

**Task**: Verify categorization for assessment scope
**Type**: Positive test | **Precondition**: System categorized

```text
Show Eagle Eye's categorization
```

**Expected Tool**: `compliance_get_categorization`
**Expected Output**:
- FIPS 199 C/I/A impacts
- Overall level = Moderate
- Information types listed

**Verification**: C=Moderate, I=Moderate, A=Low

---

### SCA-04: Check Evidence Completeness

**Task**: Verify evidence exists for controls
**Type**: Positive test | **Precondition**: Evidence collected

```text
Check evidence completeness for the AC family on Eagle Eye
```

**Expected Tool**: `compliance_check_evidence_completeness`
**Expected Output**:
- Per-control evidence status
- Controls with evidence, gaps
- Coverage percentage for AC family

**Verification**: Coverage % returned
**Record**: ac_coverage = ___%

---

### SCA-05: Verify Evidence Integrity

**Task**: Validate evidence hash
**Type**: Positive test | **Precondition**: Evidence exists

```text
Verify evidence {evidence_id}
```

**Note**: Replace `{evidence_id}` with the evidence ID from ISSO-19.

**Expected Tool**: `compliance_verify_evidence`
**Expected Output**:
- SHA-256 hash validation result
- Evidence metadata
- Collection timestamp
- Integrity = Pass or Fail

**Verification**: Integrity = Pass

---

### SCA-06: Assess Control — Satisfied (Examine)

**Task**: Record positive assessment determination
**Type**: Positive test | **Precondition**: Assessment exists

```text
Assess AC-2 as Satisfied using the Examine method — policy document
reviewed, automated provisioning verified, quarterly reviews confirmed
```

**Expected Tool**: `compliance_assess_control`
**Expected Output**:
- ControlEffectiveness record created
- determination = Satisfied
- method = Examine
- Notes saved

**Verification**: Determination = Satisfied, Method = Examine

---

### SCA-07: Assess Control — OtherThanSatisfied (CAT II)

**Task**: Record negative assessment finding
**Type**: Positive test | **Precondition**: Assessment exists

```text
Assess SI-4 as Other Than Satisfied, CAT II — monitoring is deployed
but intrusion detection signatures are 90 days out of date
```

**Expected Tool**: `compliance_assess_control`
**Expected Output**:
- ControlEffectiveness record
- determination = OtherThanSatisfied
- catSeverity = CATII
- Notes with gap description

**Verification**: Determination = OtherThanSatisfied, Severity = CAT II

---

### SCA-08: Assess Using Interview Method

**Task**: Record assessment via interview
**Type**: Positive test | **Precondition**: Assessment exists

```text
Assess CP-2 as Satisfied using the Interview method — ISSO confirmed
annual contingency plan testing and updated contact rosters
```

**Expected Tool**: `compliance_assess_control`
**Expected Output**:
- ControlEffectiveness record
- method = Interview
- Notes capture interview summary

**Verification**: Method = Interview

---

### SCA-09: Assess Using Test Method

**Task**: Record assessment via testing
**Type**: Positive test | **Precondition**: Assessment exists

```text
Assess AC-7 as Satisfied using the Test method — verified 3-attempt
lockout on all endpoints
```

**Expected Tool**: `compliance_assess_control`
**Expected Output**:
- ControlEffectiveness record
- method = Test
- Notes describe test procedure and result

**Verification**: Method = Test

---

### SCA-10: View Prisma Policies for Assessment

**Task**: Review cloud posture controls
**Type**: Positive test | **Precondition**: Prisma import completed

```text
Show Prisma Cloud policies with NIST mappings for Eagle Eye
```

**Expected Tool**: `compliance_list_prisma_policies`
**Expected Output**:
- Policy list with NIST control mappings
- Severity levels
- Open/resolved counts

**Verification**: Policies returned with NIST mappings

---

### SCA-11: Review Prisma Trend Data

**Task**: Validate remediation progress
**Type**: Positive test | **Precondition**: Multiple Prisma imports

```text
Show Prisma compliance trend for Eagle Eye to validate remediation
progress
```

**Expected Tool**: `compliance_prisma_trend`
**Expected Output**:
- Trend data showing open/resolved/new counts
- Validates remediation between imports

**Verification**: Trend data shows multiple data points

---

### SCA-12: Compare Snapshots

**Task**: Identify changes since pre-assessment
**Type**: Positive test | **Precondition**: SCA-01 + assessments recorded

```text
Compare the pre-assessment snapshot with current state for Eagle Eye
```

**Expected Tool**: `compliance_compare_snapshots`
**Expected Output**:
- Delta report
- Controls changed
- New findings, resolved findings
- Effectiveness changes

**Verification**: Delta shows assessment changes from SCA-06 through SCA-09
**Record**: controls_changed = ___

---

### SCA-13: Take Post-Assessment Snapshot

**Task**: Freeze final assessment state
**Type**: Positive test | **Precondition**: Assessment substantially complete

```text
Take a final assessment snapshot for Eagle Eye
```

**Expected Tool**: `compliance_take_snapshot`
**Expected Output**:
- Second immutable snapshot
- All assessment determinations captured

**Verification**: snapshot_id returned
**Record**: post_snapshot_id = _______________

---

### SCA-14: Get SAP

**Task**: Retrieve the finalized SAP
**Type**: Positive test | **Precondition**: SAP finalized by ISSM (ISSM-43)

```text
Show the Security Assessment Plan for Eagle Eye
```

**Expected Tool**: `compliance_get_sap`
**Expected Output**:
- Returns the finalized SAP
- Control entries, methods, team, schedule
- Status = Finalized

**Verification**: Status = Finalized

---

### SCA-15: List SAPs

**Task**: View SAP history
**Type**: Positive test | **Precondition**: ≥ 1 SAP exists

```text
List all SAPs for Eagle Eye
```

**Expected Tool**: `compliance_list_saps`
**Expected Output**:
- SAP history with status (Draft/Finalized)
- Dates, scope summaries

**Verification**: At least 1 SAP listed
**Record**: sap_count = ___

---

### SCA-16: Check SAP-SAR Alignment

**Task**: Verify assessment covers SAP scope
**Type**: Positive test | **Precondition**: SAP finalized + assessments recorded

```text
Check SAP-to-SAR alignment for Eagle Eye
```

**Expected Tool**: SAP-SAR alignment query (composite AI query)
**Expected Output**:
- Alignment report
- Planned-but-unassessed controls
- Assessed-but-unplanned controls
- Coverage percentage

**Verification**: Coverage % returned
**Record**: alignment_coverage = ___%

**Note**: This is a composite natural language query — the AI combines `compliance_get_sap` and assessment data. There is no single dedicated tool.

---

### SCA-17: Generate SAR

**Task**: Generate Security Assessment Report
**Type**: Positive test | **Precondition**: Assessments recorded

```text
Generate the Security Assessment Report for Eagle Eye
```

**Expected Tool**: `compliance_generate_sar`
**Expected Output**:
- SAR document with:
  - Executive summary
  - Per-control effectiveness determinations
  - CAT findings list
  - Evidence references
  - Prisma cloud posture data

**Verification**: SAR contains effectiveness determinations from SCA-06 through SCA-09

---

### SCA-18: Generate RAR

**Task**: Generate Risk Assessment Report
**Type**: Positive test | **Precondition**: Assessment complete

```text
Generate the Risk Assessment Report for Eagle Eye
```

**Expected Tool**: `compliance_generate_rar`
**Expected Output**:
- RAR with risk characterization per finding
- Aggregate risk assessment
- Recommended mitigations

**Verification**: RAR contains risk entries

---

### SCA-19: View Import Summary

**Task**: Review Prisma import details
**Type**: Positive test | **Precondition**: Prisma import exists

```text
Show Prisma Cloud import details for Eagle Eye's latest import
```

**Expected Tool**: `compliance_get_import_summary`
**Expected Output**:
- Per-finding import breakdown
- PrismaAlertId, CloudResourceType, NIST mappings

**Verification**: Prisma-specific fields present (PrismaAlertId, CloudResourceType)

---

### SCA-20: Run Compliance Assessment

**Task**: Run automated NIST assessment
**Type**: Positive test | **Precondition**: System with baseline + evidence

```text
Run a NIST 800-53 assessment for Eagle Eye
```

**Expected Tool**: `compliance_assess`
**Expected Output**:
- Assessment results
- Per-control pass/fail
- Compliance score
- Evidence gaps

**Verification**: Compliance score returned
**Record**: compliance_score = ___

---

## SSP & OSCAL Validation (SCA-25 to SCA-29)

### SCA-25: Check SSP Completeness

**Task**: Verify SSP readiness before including in authorization package
**Type**: Positive test | **Precondition**: SSP sections authored by ISSO

```text
Check SSP completeness for Eagle Eye
```

**Expected Tool**: `compliance_ssp_completeness`
**Expected Output**:
- 13-section status breakdown
- Completion percentage
- Blocking issues list

**Verification**: Completion percentage returned
**Record**: sca_ssp_completion_pct = ___%

---

### SCA-26: Validate Interconnection Agreements

**Task**: Verify all agreements are current before assessment sign-off
**Type**: Positive test | **Precondition**: ISSM-52 (agreements registered)

```text
Validate all interconnection agreements for Eagle Eye
```

**Expected Tool**: `compliance_validate_agreements`
**Expected Output**:
- Agreement validation results
- Expiration status per agreement
- Any expired or missing agreements flagged

**Verification**: All agreements valid (none expired)

---

### SCA-27: Export OSCAL SSP

**Task**: Export OSCAL SSP for FedRAMP/eMASS submission
**Type**: Positive test | **Precondition**: SSP substantially complete

```text
Export the OSCAL SSP for Eagle Eye
```

**Expected Tool**: `compliance_export_oscal_ssp`
**Expected Output**:
- OSCAL 1.1.2 JSON with 6 required sections
- Statistics (control count, component count)
- Warnings for incomplete data

**Verification**: OSCAL document generated with version "1.1.2"

---

### SCA-28: Validate OSCAL SSP

**Task**: Run structural validation on exported OSCAL
**Type**: Positive test | **Precondition**: SCA-27 (OSCAL export)

```text
Validate the OSCAL SSP for Eagle Eye
```

**Expected Tool**: `compliance_validate_oscal_ssp`
**Expected Output**:
- 7 structural checks executed
- Errors and warnings separated
- Statistics summary

**Verification**: Validation results returned with check details

---

### SCA-29: Check Privacy Compliance for Assessment

**Task**: Verify privacy gates are satisfied for assessment report
**Type**: Positive test | **Precondition**: PTA/PIA completed (ISSM-44/46)

```text
Check privacy compliance status for Eagle Eye
```

**Expected Tool**: `compliance_check_privacy_compliance`
**Expected Output**:
- PTA completed, PIA approved
- Interconnection agreement health
- Privacy gate status

**Verification**: Privacy gate satisfied

→ **Handoff**: SAR delivered with OSCAL validation. ISSM creates POA&M for findings and bundles authorization package.

---

## SCA Separation-of-Duties Verification (SCA-21 to SCA-24)

**Purpose**: Verify that the SCA (Auditor) role is correctly denied write/modify operations. All 4 tests must return **403 Forbidden**.

### SCA-21: DENIED — Write Narrative

**Task**: Attempt to write SSP narrative (should be denied)
**Type**: RBAC denial test | **Precondition**: SCA role active

```text
Write narrative for AC-2 on Eagle Eye: test text
```

**Expected Tool**: `compliance_write_narrative`
**Expected Response**: **403 Forbidden** — SCA (Auditor) cannot modify SSP narratives

**Verification**: HTTP 403 returned (not 404 or 500)

---

### SCA-22: DENIED — Remediate Finding

**Task**: Attempt to remediate (should be denied)
**Type**: RBAC denial test | **Precondition**: SCA role active

```text
Fix finding {finding_id} on Eagle Eye
```

**Expected Tool**: `compliance_remediate`
**Expected Response**: **403 Forbidden** — SCA cannot execute remediation

**Verification**: HTTP 403 returned

---

### SCA-23: DENIED — Issue Authorization

**Task**: Attempt to issue ATO (should be denied)
**Type**: RBAC denial test | **Precondition**: SCA role active

```text
Issue ATO for Eagle Eye
```

**Expected Tool**: `compliance_issue_authorization`
**Expected Response**: **403 Forbidden** — only AO can issue authorization decisions

**Verification**: HTTP 403 returned

---

### SCA-24: DENIED — Dismiss Alert

**Task**: Attempt to dismiss alert (should be denied)
**Type**: RBAC denial test | **Precondition**: SCA role active

```text
Dismiss alert ALT-{id}
```

**Expected Tool**: `watch_dismiss_alert`
**Expected Response**: **403 Forbidden** — only ISSM (SecurityLead) can dismiss

**Verification**: HTTP 403 returned

---

## SCA Results Summary

| Metric | Value |
|--------|-------|
| Total Test Cases | 29 |
| Positive Tests | ___/25 passed |
| RBAC Denied Tests | ___/4 returned 403 |
| Failed | ___ |
| Blocked | ___ |
| Skipped | ___ |
| Avg Response Time | ___s |

### RBAC Verification Matrix

| TC-ID | Operation | Expected | Actual | Status |
|-------|-----------|----------|--------|--------|
| SCA-21 | Write narrative | 403 | ___ | ⬜ |
| SCA-22 | Remediate finding | 403 | ___ | ⬜ |
| SCA-23 | Issue authorization | 403 | ___ | ⬜ |
| SCA-24 | Dismiss alert | 403 | ___ | ⬜ |

### Issues Found

| # | TC-ID | Severity | Description | Root Cause |
|---|-------|----------|-------------|------------|
| | | | | |

### Key Artifacts Created

| Artifact | ID / Value | Test Case |
|----------|-----------|-----------|
| Pre-Assessment Snapshot | _______________ | SCA-01 |
| Post-Assessment Snapshot | _______________ | SCA-13 |
| SAP-SAR Alignment | ___% | SCA-16 |
| Compliance Score | ___ | SCA-20 |
| SSP Completion | ___% | SCA-25 |

**Checkpoint**: ⬜ SCA (29 tests) complete. Assessment artifacts generated, OSCAL validated, RBAC enforced. AO testing can begin.

---

## HW/SW Inventory Verification (SCA-INV-01 to SCA-INV-03)

### SCA-INV-01: Check Inventory Completeness

**Task**: Verify inventory completeness before assessment

```text
@ato Check inventory completeness for Eagle Eye
```

**Expected Tool**: `inventory_completeness`
**Expected Output**: `completeness_score`, `is_complete`, `unmatched_boundary_resources`, `hardware_without_software`
**Verification**: Review any incomplete items and flag to ISSO

### SCA-INV-02: List Hardware Inventory

**Task**: Review hardware inventory against boundary

```text
@ato List all hardware inventory items for Eagle Eye
```

**Expected Tool**: `inventory_list` with `type` = "hardware"
**Expected Output**: List of hardware items matching boundary resources

### SCA-INV-03: Export Inventory Snapshot

**Task**: Archive inventory snapshot for assessment records

```text
@ato Export the inventory for Eagle Eye to Excel
```

**Expected Tool**: `inventory_export`
**Expected Output**: Base64-encoded Excel workbook for archival

---

## Narrative Governance Verification (SCA-NGV-01 to SCA-NGV-03)

> Feature 024: SCAs verify narrative approval status before assessment and cannot perform review actions.

### SCA-NGV-01: View Narrative Approval Progress

**Task**: Check overall narrative approval status before conducting assessment

```text
@ato Show the narrative approval progress for Eagle Eye
```

**Expected Tool**: `compliance_narrative_approval_progress`
**Expected Output**: Overall approval percentage, per-family breakdown, review queue, staleness warnings
**Record**: approval_percentage = ___

### SCA-NGV-02: View Narrative Version History

**Task**: Review the version history for a control under assessment

```text
@ato Show the version history for the AC-1 narrative of Eagle Eye
```

**Expected Tool**: `compliance_narrative_history`
**Expected Output**: List of versions showing authorship and approval status
**Record**: total_versions = ___

### SCA-NGV-03: Review Narrative (DENIED — RBAC)

**Task**: Attempt to review a narrative (should be denied — SCA cannot review)

```text
@ato Approve the AC-1 narrative for Eagle Eye
```

**Expected Tool**: `compliance_review_narrative`
**Expected Output**: 403 Forbidden — SCA role does not have review permissions
**Record**: HTTP status = ___
