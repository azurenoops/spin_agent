# ISSM Persona Test Execution Script

**Feature**: 020 | **Persona**: ISSM (Information System Security Manager)
**Role**: `Compliance.SecurityLead` | **Interface**: Microsoft Teams
**Test Cases**: ISSM-01 through ISSM-61 (61 total)

---

## Pre-Execution Setup

1. **Activate PIM role**: `Activate my Compliance.SecurityLead role for 8 hours — persona test suite execution`
2. **Verify role**: `Show my active PIM roles` → Confirm `Compliance.SecurityLead` is active
3. **Open Teams**: Switch to the Microsoft Teams interface with Security Posture Intelligence Navigator bot
4. **Open results template**: `docs/persona-test-cases/results-template.md`
5. **Confirm clean slate**: `Show system details for Eagle Eye` → Should return "System not found"

---

## Phase 0 — Prepare (ISSM-01 to ISSM-06)

### ISSM-01: Register Eagle Eye System

**Task**: Register a new system
**Type**: Positive test

```text
Register a new system called 'Eagle Eye' as a Major Application with
mission-critical designation in Azure Government. The acronym is 'EE',
the description is 'Mission planning and operational intelligence
platform for joint force coordination', the Azure subscription ID is
12345678-abcd-1234-abcd-123456789012, the compliance framework is
NIST80053, and the cloud environment is Azure Government.
```

> **Note**: The system requires **System Acronym**, **System Description**, **Azure Subscription IDs**, **Compliance Framework**, and **Cloud Environment** to complete registration. Including all five in the initial prompt avoids follow-up questions.

**Expected Tool**: `configuration_manage` (may be called multiple times to set each property)
**Expected Output**:
- `system_id`: GUID (record this — used in all subsequent tests)
- `name`: "Eagle Eye"
- `acronym`: "EE"
- `description`: Contains mission planning reference
- `type`: "MajorApplication"
- `environment`: "AzureGovernment"
- `subscription_ids`: includes `12345678-abcd-1234-abcd-123456789012`
- `compliance_framework`: "NIST80053"
- `rmf_step`: "Prepare"

**Verification**: system_id is a valid GUID, RMF step = Prepare, acronym = "EE", framework = NIST80053
**Record**: system_id = _______________

---

### ISSM-02: Define Authorization Boundary

**Task**: Define the authorization boundary
**Type**: Positive test | **Precondition**: ISSM-01

```text
Define the authorization boundary for Eagle Eye with these resources:
- /subscriptions/12345678-abcd-1234-abcd-123456789012/resourceGroups/rg-eagleeye-prod/providers/Microsoft.Compute/virtualMachines/vm-eagleeye-web01
- /subscriptions/12345678-abcd-1234-abcd-123456789012/resourceGroups/rg-eagleeye-prod/providers/Microsoft.Compute/virtualMachines/vm-eagleeye-app01
- /subscriptions/12345678-abcd-1234-abcd-123456789012/resourceGroups/rg-eagleeye-prod/providers/Microsoft.Sql/servers/sql-eagleeye-prod/databases/db-eagleeye
- /subscriptions/12345678-abcd-1234-abcd-123456789012/resourceGroups/rg-eagleeye-prod/providers/Microsoft.KeyVault/vaults/kv-eagleeye-prod
- /subscriptions/12345678-abcd-1234-abcd-123456789012/resourceGroups/rg-eagleeye-dev/providers/Microsoft.KeyVault/vaults/kv-eagleeye-dev
```

> **Note**: The boundary tool requires **full ARM resource IDs** (e.g., `/subscriptions/{id}/resourceGroups/{rg}/providers/{type}/{name}`). Generic names like "production VMs" will be rejected.

**Expected Tool**: `compliance_define_boundary`
**Expected Output**:
- Boundary created with resource list
- Subscription `12345678-abcd-1234-abcd-123456789012` linked
- Resource count = 5

**Verification**: All 5 resources listed in boundary

---

### ISSM-03: Exclude Resource from Boundary

**Task**: Exclude a resource from the boundary
**Type**: Positive test | **Precondition**: ISSM-02

```text
Exclude /subscriptions/12345678-abcd-1234-abcd-123456789012/resourceGroups/rg-eagleeye-dev/providers/Microsoft.KeyVault/vaults/kv-eagleeye-dev from Eagle Eye's boundary — it's in a separate authorization
```

**Expected Tool**: `compliance_exclude_from_boundary`
**Expected Output**:
- Resource `kv-eagleeye-dev` excluded confirmation
- Boundary resource count decremented to 4

**Verification**: Resource count = 4 (was 5 in ISSM-02)

---

### ISSM-04: Assign RMF Roles

**Task**: Assign ISSO and SCA roles
**Type**: Positive test | **Precondition**: ISSM-01

```text
Assign Jane Smith as ISSO and Bob Jones as SCA for Eagle Eye
```

**Expected Tool**: `compliance_assign_rmf_role` (called twice)
**Expected Output**:
- Two role assignments created
- ISSO → Jane Smith
- SCA → Bob Jones

**Verification**: Both assignments confirmed

---

### ISSM-05: List RMF Role Assignments

**Task**: Verify role assignments
**Type**: Positive test | **Precondition**: ISSM-04

```text
Show all RMF role assignments for Eagle Eye
```

**Expected Tool**: `compliance_list_rmf_roles`
**Expected Output**:
- Returns ≥ 2 assignments
- ISSO: Jane Smith
- SCA: Bob Jones

**Verification**: Both names and roles present in response

---

### ISSM-06: Advance to Categorize

**Task**: Advance RMF phase
**Type**: Positive test | **Precondition**: ISSM-01 through ISSM-04

```text
Advance Eagle Eye to the Categorize phase
```

**Expected Tool**: `compliance_advance_rmf_step`
**Expected Output**:
- RMF step changes from "Prepare" to "Categorize"

**Verification**: RMF step = "Categorize"

---

## Phase 1 — Categorize (ISSM-07 to ISSM-10)

### ISSM-07: Suggest Information Types

**Task**: Get SP 800-60 info type suggestions
**Type**: Positive test | **Precondition**: ISSM-06

```text
Suggest information types for Eagle Eye — it's a mission planning system
```

**Expected Tool**: `compliance_suggest_info_types`
**Expected Output**:
- SP 800-60 info type suggestions
- C/I/A impact levels per info type
- Mission-relevant types prioritized

**Verification**: At least one info type returned with impact levels

---

### ISSM-08: Categorize the System

**Task**: Set FIPS 199 categorization
**Type**: Positive test | **Precondition**: ISSM-06

```text
Categorize Eagle Eye as Moderate confidentiality, Moderate integrity, Low
availability with info types: Mission Operations (C:Mod, I:Mod, A:Low)
```

**Expected Tool**: `compliance_categorize_system`
**Expected Output**:
- FIPS 199 categorization saved
- Overall impact = Moderate (high-water mark)
- C=Moderate, I=Moderate, A=Low

**Verification**: Overall impact = Moderate

---

### ISSM-09: View Categorization

**Task**: Verify saved categorization
**Type**: Positive test | **Precondition**: ISSM-08

```text
Show the categorization for Eagle Eye
```

**Expected Tool**: `compliance_get_categorization`
**Expected Output**:
- FIPS 199 notation
- C/I/A impacts match ISSM-08
- Information types listed
- Overall impact level

**Verification**: Values match what was set in ISSM-08

---

### ISSM-10: Advance to Select

**Task**: Advance RMF phase
**Type**: Positive test | **Precondition**: ISSM-08

```text
Advance Eagle Eye to the Select phase
```

**Expected Tool**: `compliance_advance_rmf_step`
**Expected Output**:
- RMF step changes to "Select"

**Verification**: RMF step = "Select"

---

## Phase 2 — Select (ISSM-11 to ISSM-16)

### ISSM-11: Select Baseline

**Task**: Apply Moderate baseline
**Type**: Positive test | **Precondition**: ISSM-10

```text
Select the Moderate baseline for Eagle Eye
```

**Expected Tool**: `compliance_select_baseline`
**Expected Output**:
- Baseline applied with 325 controls
- Baseline level = "Moderate"

**Verification**: Control count = 325
**Record**: control_count = ___

---

### ISSM-12: Tailor Baseline

**Task**: Remove a control
**Type**: Positive test | **Precondition**: ISSM-11

```text
Remove control PE-1 from Eagle Eye's baseline — physical security is
inherited from the data center
```

**Expected Tool**: `compliance_tailor_baseline`
**Expected Output**:
- Tailoring record created
- Action = "Remove"
- Rationale captured
- Control count = 324

**Verification**: Control count decremented by 1

---

### ISSM-13: Set Inheritance

**Task**: Set controls as inherited
**Type**: Positive test | **Precondition**: ISSM-11

```text
Set AC-1 through AC-4 as inherited from Azure Government FedRAMP High
for Eagle Eye
```

**Expected Tool**: `compliance_set_inheritance`
**Expected Output**:
- Inheritance records created for AC-1, AC-2, AC-3, AC-4
- Provider = "Azure Government"

**Verification**: 4 inheritance records created

---

### ISSM-14: Generate CRM

**Task**: Generate Customer Responsibility Matrix
**Type**: Positive test | **Precondition**: ISSM-13

```text
Generate the Customer Responsibility Matrix for Eagle Eye
```

**Expected Tool**: `compliance_generate_crm`
**Expected Output**:
- CRM document with inherited/shared/customer columns per control
- Counts match inheritance settings from ISSM-13

**Verification**: AC-1 through AC-4 shown as inherited

---

### ISSM-15: View Baseline

**Task**: View baseline details
**Type**: Positive test | **Precondition**: ISSM-11

```text
Show the baseline details for Eagle Eye
```

**Expected Tool**: `compliance_get_baseline`
**Expected Output**:
- Baseline level
- Total controls
- Tailored count
- Inherited count
- Overlay info

**Verification**: Baseline = Moderate, totals match

---

### ISSM-16: Advance to Implement

**Task**: Advance RMF phase
**Type**: Positive test | **Precondition**: ISSM-11

```text
Move Eagle Eye to the Implement phase
```

**Expected Tool**: `compliance_advance_rmf_step`
**Expected Output**:
- RMF step changes to "Implement"

**Verification**: RMF step = "Implement"

---

## Phase 3 — Implement Oversight (ISSM-17 to ISSM-22)

### ISSM-17: Check SSP Progress

**Task**: View SSP completion
**Type**: Positive test | **Precondition**: ISSM-16

```text
What's the SSP completion percentage for Eagle Eye?
```

**Expected Tool**: `compliance_narrative_progress`
**Expected Output**:
- Overall completion %
- Per-family breakdown
- Initially low before ISSO authoring

**Verification**: Response includes percentage and family breakdown

---

### ISSM-18: Generate SSP

**Task**: Generate System Security Plan
**Type**: Positive test | **Precondition**: ISSM-16 + ISSO-04/ENG-05 (cross-persona: ISSO or Engineer authors narratives)

```text
Generate the SSP for Eagle Eye
```

**Expected Tool**: `compliance_generate_ssp`
**Expected Output**:
- Markdown SSP with System Information, Categorization, Baseline, Control Implementations
- Warnings for missing narratives

**Verification**: SSP contains 4 major sections

---

### ISSM-19: Import Prisma Cloud CSV

**Task**: Import Prisma scan results
**Type**: Positive test | **Precondition**: ISSM-16

```text
Import this Prisma Cloud CSV scan for Eagle Eye
```

**Attachment**: `test-data/prisma-cloud-scan.csv`

**Expected Tool**: `compliance_import_prisma_csv`
**Expected Output**:
- Import record created
- Findings created with Prisma-specific fields (PrismaAlertId, CloudResourceType)
- NIST controls mapped
- Effectiveness records upserted

**Verification**: Findings count > 0, Prisma fields populated

---

### ISSM-20: Import Prisma Cloud API Scan

**Task**: Import via Prisma API integration
**Type**: Positive test | **Precondition**: ISSM-16

```text
Import Prisma Cloud API scan results for Eagle Eye with auto-resolve
subscriptions
```

**Attachment**: `test-data/prisma-cloud-api-results.json`

**Expected Tool**: `compliance_import_prisma_api`
**Expected Output**:
- Import record with `auto_resolve_subscription: true`
- CLI remediation scripts extracted (RemediationCli field populated)
- Compliance standards captured

**Verification**: RemediationCli populated for auto-remediable findings

---

### ISSM-21: List Prisma Policies

**Task**: View Prisma policies
**Type**: Positive test | **Precondition**: ISSM-19 or ISSM-20

```text
Show all Prisma Cloud policies affecting Eagle Eye
```

**Expected Tool**: `compliance_list_prisma_policies`
**Expected Output**:
- Policy list with severity, cloud type, NIST mappings
- Open/resolved counts per policy

**Verification**: Policies returned with NIST control mappings

---

### ISSM-22: View Prisma Trends

**Task**: View compliance trend from Prisma
**Type**: Positive test | **Precondition**: ISSM-19 + ISSM-20 (multiple imports)

```text
Show Prisma Cloud compliance trend for Eagle Eye over the last 90 days
grouped by severity
```

**Expected Tool**: `compliance_prisma_trend`
**Expected Output**:
- Trend data with per-period open/resolved/new counts
- Grouped by severity

**Verification**: Multiple data points present, grouped by severity

→ **Handoff**: System now in Implement phase. ISSO begins SSP authoring.

---

## SAP Generation (ISSM-41 to ISSM-43)

### ISSM-41: Generate SAP

**Task**: Generate Security Assessment Plan
**Type**: Positive test | **Precondition**: System with baseline selected

```text
Generate a Security Assessment Plan for Eagle Eye
```

**Expected Tool**: `compliance_generate_sap`
**Expected Output**:
- SAP document with ~325 control entries (Moderate)
- Assessment objectives
- Methods (Examine/Interview/Test) per control
- STIG benchmark coverage
- Team placeholder, schedule placeholder
- Status = Draft

**Verification**: Status = "Draft", control entries match baseline count
**Record**: sap_id = _______________

---

### ISSM-42: Update SAP

**Task**: Add team, schedule, and method overrides
**Type**: Positive test | **Precondition**: ISSM-41

```text
Update Eagle Eye's SAP — set assessment start date to April 1, add Bob
Jones to the assessment team as Lead Assessor, override AC-2 method to
Interview
```

**Expected Tool**: `compliance_update_sap`
**Expected Output**:
- SAP updated: schedule dates set
- Team member added with role "Lead Assessor"
- AC-2 method override recorded with SCA justification

**Verification**: All 3 updates reflected

---

### ISSM-43: Finalize SAP

**Task**: Lock SAP for assessment
**Type**: Positive test | **Precondition**: ISSM-42

```text
Finalize the Security Assessment Plan for Eagle Eye
```

**Expected Tool**: `compliance_finalize_sap`
**Expected Output**:
- SAP status → Finalized
- SHA-256 content hash generated
- SAP is now immutable (subsequent update attempts rejected)

**Verification**: Status = "Finalized", hash present
**Record**: content_hash = _______________

---

## Privacy & Interconnection Management (ISSM-44 to ISSM-55)

### ISSM-44: Create Privacy Threshold Analysis

**Task**: Conduct PTA to determine if PIA is required
**Type**: Positive test | **Precondition**: System registered (ISSM-01)

```text
Create a Privacy Threshold Analysis for Eagle Eye — the system collects
Name, Email, and Social Security Number for personnel vetting. PII is
collected directly from users and shared with HR systems. Retention
period is 5 years. Legal authority is 5 USC § 301.
```

**Expected Tool**: `compliance_create_pta`
**Expected Output**:
- PTA created with PII categories (Name, Email, SSN)
- Collection method = Direct
- Sharing details recorded
- PIA required = true (due to SSN)

**Verification**: PIA required = true, 3 PII categories listed
**Record**: pta_id = _______________

---

### ISSM-45: Generate Privacy Impact Assessment

**Task**: Generate PIA from PTA data
**Type**: Positive test | **Precondition**: ISSM-44

```text
Generate a Privacy Impact Assessment for Eagle Eye based on the PTA
```

**Expected Tool**: `compliance_generate_pia`
**Expected Output**:
- PIA with 9 sections generated from PTA data
- Status = Draft
- Sections cover: authority, purpose, data elements, sharing, notice, access, safeguards, retention, accountability

**Verification**: Status = "Draft", 9 sections present
**Record**: pia_id = _______________

---

### ISSM-46: Review and Approve PIA

**Task**: Review PIA and approve it
**Type**: Positive test | **Precondition**: ISSM-45

```text
Approve the Privacy Impact Assessment for Eagle Eye — all 9 sections
reviewed, privacy controls are adequate for the PII collected
```

**Expected Tool**: `compliance_review_pia`
**Expected Output**:
- PIA status → Approved
- Reviewer comments saved
- Approval timestamp recorded

**Verification**: Status = "Approved", reviewer identity recorded

---

### ISSM-47: Check Privacy Compliance Dashboard

**Task**: View aggregated privacy gate status
**Type**: Positive test | **Precondition**: ISSM-44, ISSM-46

```text
Check privacy compliance status for Eagle Eye
```

**Expected Tool**: `compliance_check_privacy_compliance`
**Expected Output**:
- PTA completed = true
- PIA approved = true
- Active interconnections count
- Interconnections with agreements count
- Overall privacy gate = satisfied

**Verification**: Privacy gate satisfied, PTA + PIA both complete

---

### ISSM-48: Add System Interconnection

**Task**: Register an interconnection crossing the authorization boundary
**Type**: Positive test | **Precondition**: System registered (ISSM-01)

```text
Add an interconnection for Eagle Eye — bidirectional data exchange with
DISA SIPR Gateway for classified mission planning data, using IPSec VPN
over DISN
```

**Expected Tool**: `compliance_add_interconnection`
**Expected Output**:
- Interconnection created
- Direction = Bidirectional
- Status = Proposed
- Data flow description saved

**Verification**: Status = "Proposed", direction = "Bidirectional"
**Record**: interconnection_id = _______________

---

### ISSM-49: List Interconnections

**Task**: View all registered interconnections
**Type**: Positive test | **Precondition**: ISSM-48

```text
List all interconnections for Eagle Eye
```

**Expected Tool**: `compliance_list_interconnections`
**Expected Output**:
- List with at least 1 interconnection
- Per entry: remote system, direction, status, agreement status

**Verification**: At least 1 interconnection returned

---

### ISSM-50: Update Interconnection Status

**Task**: Activate a proposed interconnection
**Type**: Positive test | **Precondition**: ISSM-48

```text
Update the DISA SIPR Gateway interconnection for Eagle Eye to Active
status — circuit established and tested
```

**Expected Tool**: `compliance_update_interconnection`
**Expected Output**:
- Status changed: Proposed → Active

**Verification**: Status = "Active"

---

### ISSM-51: Generate ISA

**Task**: Generate Interconnection Security Agreement document
**Type**: Positive test | **Precondition**: ISSM-48

```text
Generate an Interconnection Security Agreement for Eagle Eye's DISA
SIPR Gateway connection
```

**Expected Tool**: `compliance_generate_isa`
**Expected Output**:
- ISA document generated with connection details
- Security requirements enumerated
- Data flow diagram reference
- Roles and responsibilities

**Verification**: ISA document contains interconnection details

---

### ISSM-52: Register ISA Agreement

**Task**: Register the signed ISA with expiration tracking
**Type**: Positive test | **Precondition**: ISSM-51

```text
Register the signed ISA for Eagle Eye's DISA SIPR interconnection —
type ISA, signed March 1, 2026, expires March 1, 2029
```

**Expected Tool**: `compliance_register_agreement`
**Expected Output**:
- Agreement registered with type = ISA
- Expiration = 2029-03-01
- Status = Active

**Verification**: Agreement type = "ISA", expiration date recorded
**Record**: agreement_id = _______________

---

### ISSM-53: Update Agreement

**Task**: Update agreement renewal information
**Type**: Positive test | **Precondition**: ISSM-52

```text
Update Eagle Eye's DISA SIPR ISA — add renewal note: annual review
scheduled for March 2027
```

**Expected Tool**: `compliance_update_agreement`
**Expected Output**:
- Agreement updated with renewal information

**Verification**: Update confirmed

---

### ISSM-54: Validate All Agreements

**Task**: Check all agreements are current and not expired
**Type**: Positive test | **Precondition**: ISSM-52

```text
Validate all interconnection agreements for Eagle Eye
```

**Expected Tool**: `compliance_validate_agreements`
**Expected Output**:
- All agreements validated
- Expiration status per agreement
- Warnings for any nearing expiration

**Verification**: All agreements valid (none expired)

---

### ISSM-55: Certify No Additional Interconnections

**Task**: Test the no-interconnections certification (negative scenario — should fail since interconnections exist)
**Type**: Negative test | **Precondition**: ISSM-48 (interconnection exists)

```text
Certify that Eagle Eye has no external interconnections
```

**Expected Tool**: `compliance_certify_no_interconnections`
**Expected Output**:
- Error: Cannot certify — active interconnections exist
- Lists existing interconnections

**Verification**: Certification rejected with existing interconnection list

---

## SSP Section Review & OSCAL Export (ISSM-56 to ISSM-61)

### ISSM-56: Review SSP Section

**Task**: Review and approve an SSP section submitted by ISSO
**Type**: Positive test | **Precondition**: ISSO has written SSP sections (cross-persona: ISSO-25+)

```text
Approve SSP Section 5 for Eagle Eye — general description is accurate
and complete
```

**Expected Tool**: `compliance_review_ssp_section`
**Expected Output**:
- Section 5 status → Approved
- Reviewer comments saved
- Review timestamp recorded

**Verification**: Section status = "Approved"

---

### ISSM-57: Check SSP Completeness

**Task**: Check overall SSP readiness
**Type**: Positive test | **Precondition**: Multiple SSP sections authored

```text
Check SSP completeness for Eagle Eye
```

**Expected Tool**: `compliance_ssp_completeness`
**Expected Output**:
- Per-section status (13 sections)
- Overall completion percentage
- Blocking issues list
- Sections not started / draft / approved

**Verification**: Completion percentage returned, per-section breakdown shows statuses
**Record**: ssp_completion_pct = ___%

---

### ISSM-58: Export OSCAL SSP

**Task**: Export OSCAL 1.1.2 SSP JSON
**Type**: Positive test | **Precondition**: SSP substantially complete

```text
Export the OSCAL SSP for Eagle Eye
```

**Expected Tool**: `compliance_export_oscal_ssp`
**Expected Output**:
- OSCAL 1.1.2 JSON document with 6 sections:
  - metadata, import-profile, system-characteristics
  - system-implementation, control-implementation, back-matter
- Statistics (control count, component count, etc.)
- Warnings for any missing data

**Verification**: OSCAL version = "1.1.2", all 6 top-level sections present
**Record**: oscal_control_count = ___

---

### ISSM-59: Validate OSCAL SSP

**Task**: Run structural validation on OSCAL SSP
**Type**: Positive test | **Precondition**: ISSM-58

```text
Validate the OSCAL SSP for Eagle Eye
```

**Expected Tool**: `compliance_validate_oscal_ssp`
**Expected Output**:
- 7 structural checks executed
- Errors vs. warnings separation
- statistics: control count, component count, etc.

**Verification**: Validation result returned with errors/warnings breakdown

---

### ISSM-60: Review SSP Section — Request Revision

**Task**: Reject an SSP section and request changes
**Type**: Positive test | **Precondition**: ISSO has written SSP section

```text
Request revision on SSP Section 12 for Eagle Eye — personnel security
section needs to include separation procedures and training frequency
```

**Expected Tool**: `compliance_review_ssp_section`
**Expected Output**:
- Section 12 status → Draft (returned for revision)
- Reviewer comments with requested changes

**Verification**: Section status reverted, comments include revision request

---

### ISSM-61: Export CKL After Assessment

**Task**: Export CKL file for STIG Viewer submission
**Type**: Positive test | **Precondition**: STIG findings exist from ISSO-09

```text
Export a CKL checklist for Eagle Eye's Windows Server 2022 STIG
```

**Expected Tool**: `compliance_export_ckl`
**Expected Output**:
- CKL XML file generated
- STIG evaluation results included
- Compatible with DISA STIG Viewer and eMASS

**Verification**: CKL file generated successfully

---

## Phase 4 — Assess Prep (ISSM-23 to ISSM-28)

### ISSM-23a: Create POA&M (from Assessment)

**Task**: Create a POA&M item from a finding generated by running an assessment
**Type**: Positive test | **Precondition**: SCA-20 (cross-persona: SCA runs `compliance_assess`)

```text
Create a POA&M item for finding {finding_id} — scheduled completion in
90 days
```

**Note**: Replace `{finding_id}` with a finding ID produced by a `compliance_assess` run.

**Expected Tool**: `compliance_create_poam`
**Expected Output**:
- POA&M record created
- Finding linked (source = assessment)
- Scheduled completion date = today + 90 days
- Status = "Ongoing"

**Verification**: Status = "Ongoing", finding linked, finding source is assessment

---

### ISSM-23b: Create POA&M (from STIG/Scan Import)

**Task**: Create a POA&M item from a finding imported via CKL or XCCDF
**Type**: Positive test | **Precondition**: Findings exist from ISSO-09 (CKL) or ISSO-10 (XCCDF) import

```text
Create a POA&M item for finding {finding_id} — scheduled completion in
90 days
```

**Note**: Replace `{finding_id}` with a finding ID from a CKL/XCCDF import.

**Expected Tool**: `compliance_create_poam`
**Expected Output**:
- POA&M record created
- Finding linked (source = STIG/scan import)
- Scheduled completion date = today + 90 days
- Status = "Ongoing"

**Verification**: Status = "Ongoing", finding linked, finding source is import

---

### ISSM-23c: Create POA&M (from Prisma Cloud)

**Task**: Create a POA&M item from a finding imported via Prisma Cloud
**Type**: Positive test | **Precondition**: Findings exist from ISSM-19 (Prisma CSV) or ISSM-20 (Prisma API) import

```text
Create a POA&M item for finding {finding_id} — scheduled completion in
90 days
```

**Note**: Replace `{finding_id}` with a finding ID from a Prisma Cloud import.

**Expected Tool**: `compliance_create_poam`
**Expected Output**:
- POA&M record created
- Finding linked (source = Prisma Cloud)
- Scheduled completion date = today + 90 days
- Status = "Ongoing"

**Verification**: Status = "Ongoing", finding linked, finding source is Prisma

---

### ISSM-23d: Verify Auto-Generated POA&M (from ACAS/Nessus)

**Task**: Verify POA&M entries auto-created by Nessus import for Cat I/II/III findings
**Type**: Positive test | **Precondition**: ISSO-12a completed (Nessus import with POA&M generation)

```text
List POA&M items for Eagle Eye with weakness source ACAS
```

**Note**: Nessus import auto-generates POA&M entries — no manual creation needed.

**Expected Tool**: `compliance_list_poam`
**Expected Output**:
- POA&M entries with WeaknessSource = "ACAS"
- Cat I → 30-day completion, Cat II → 90-day, Cat III → 180-day
- Status = "Ongoing"
- Each linked to a finding from the Nessus scan

**Verification**: At least 1 ACAS-sourced POA&M entry exists with correct scheduled dates

**Expected Tool**: `compliance_create_poam`
**Expected Output**:
- POA&M record created
- Finding linked (source = Prisma Cloud)
- Scheduled completion date = today + 90 days
- Status = "Ongoing"
- Prisma-specific fields preserved (PrismaAlertId, CloudResourceType)

**Verification**: Status = "Ongoing", finding linked, Prisma fields present

---

### ISSM-24: List POA&M Items

**Task**: View all POA&M items
**Type**: Positive test | **Precondition**: ISSM-23a, 23b, or 23c

```text
Show all POA&M items for Eagle Eye
```

**Expected Tool**: `compliance_list_poam`
**Expected Output**:
- List with status, severity, scheduled dates, finding references

**Verification**: At least 1 item returned

---

### ISSM-25: Generate RAR

**Task**: Generate Risk Assessment Report
**Type**: Positive test | **Precondition**: SCA-17 (cross-persona: SCA generates SAR after assessments)

```text
Generate the Risk Assessment Report for Eagle Eye
```

**Expected Tool**: `compliance_generate_rar`
**Expected Output**:
- RAR document with risk characterization
- Finding summary
- Residual risk assessment

**Verification**: RAR contains risk entries

---

### ISSM-26: Create Remediation Board

**Task**: Create Kanban board from assessment findings
**Type**: Positive test | **Precondition**: SCA-06 to SCA-09 (cross-persona: SCA records assessments)

```text
Create a Kanban remediation board from Eagle Eye's assessment
```

**Expected Tool**: `kanban_create_board`
**Expected Output**:
- Board created with tasks for each open finding
- Status counts returned (ToDo, InProgress, Done)

**Verification**: Task count > 0
**Record**: board_id = _______________

---

### ISSM-27: Bulk Assign Tasks

**Task**: Assign all CAT I tasks to engineer
**Type**: Positive test | **Precondition**: ISSM-26

```text
Assign all CAT I tasks on Eagle Eye's board to SSgt Rodriguez
```

**Expected Tool**: `kanban_bulk_update`
**Expected Output**:
- Multiple tasks assigned
- Confirmation with count of updated tasks

**Verification**: Updated count matches CAT I task count

---

### ISSM-28: Export Kanban to POA&M

**Task**: Export board as POA&M format
**Type**: Positive test | **Precondition**: ISSM-26

```text
Export Eagle Eye's remediation board as POA&M
```

**Expected Tool**: `kanban_export`
**Expected Output**:
- POA&M-formatted export
- All open tasks with milestones and responsible parties

**Verification**: Export contains task data in POA&M format

---

## Phase 5 — Authorize Submission (ISSM-29 to ISSM-31)

### ISSM-29: Bundle Authorization Package

**Task**: Bundle all artifacts for AO review
**Type**: Positive test | **Precondition**: ISSM-18 (SSP) + SCA-17 (SAR) + ISSM-25 (RAR) + ISSM-23a/b/c (POA&M)

```text
Bundle the authorization package for Eagle Eye
```

**Expected Tool**: `compliance_bundle_authorization_package`
**Expected Output**:
- Package with SSP + SAR + RAR + POA&M + CRM
- Completeness check with warnings for any gaps

**Verification**: Package includes all 5 artifacts

---

### ISSM-30: Advance to Authorize

**Task**: Advance RMF phase
**Type**: Positive test | **Precondition**: ISSM-29

```text
Move Eagle Eye to the Authorize phase
```

**Expected Tool**: `compliance_advance_rmf_step`
**Expected Output**:
- RMF step changes to "Authorize"

**Verification**: RMF step = "Authorize"

---

### ISSM-31: View Risk Register

**Task**: Review risk posture
**Type**: Positive test | **Precondition**: SCA-06 to SCA-09 (cross-persona: SCA records assessments)

```text
Show the risk register for Eagle Eye
```

**Expected Tool**: `compliance_show_risk_register`
**Expected Output**:
- Risk entries with severity, status, mitigation, residual risk

**Verification**: At least 1 risk entry returned

→ **Handoff**: Authorization package submitted. AO reviews and decides.

---

## Phase 6 — Monitor (ISSM-32 to ISSM-40)

### ISSM-32: Create ConMon Plan

**Task**: Establish continuous monitoring
**Type**: Positive test | **Precondition**: AO-04 (cross-persona: AO issues ATO)

```text
Create a continuous monitoring plan for Eagle Eye with monthly
assessments and quarterly reviews
```

**Expected Tool**: `compliance_create_conmon_plan`
**Expected Output**:
- ConMon plan created
- Frequency settings
- Review dates
- Stakeholder list

**Verification**: Plan created with monthly/quarterly schedule

---

### ISSM-33: Generate ConMon Report

**Task**: Generate monthly report
**Type**: Positive test | **Precondition**: ISSM-32

```text
Generate the monthly ConMon report for Eagle Eye
```

**Expected Tool**: `compliance_generate_conmon_report`
**Expected Output**:
- Compliance score
- Baseline delta
- Finding trends
- POA&M status

**Verification**: Report contains compliance score
**Record**: compliance_score = ___

---

### ISSM-34: Track ATO Expiration

**Task**: Check ATO expiration status
**Type**: Positive test | **Precondition**: AO-04 (cross-persona: AO issues ATO)

```text
When does Eagle Eye's ATO expire?
```

**Expected Tool**: `compliance_track_ato_expiration`
**Expected Output**:
- Alert level (None/Info/Warning/Urgent/Expired)
- Days remaining
- Recommended action

**Verification**: Days remaining > 0, alert level appropriate

---

### ISSM-35: Report Significant Change

**Task**: Report a security-impacting change
**Type**: Positive test | **Precondition**: AO-04 (cross-persona: AO issues ATO)

```text
Report a significant change for Eagle Eye — new interconnection with
DISA SIPR gateway
```

**Expected Tool**: `compliance_report_significant_change`
**Expected Output**:
- Change recorded
- `requires_reauthorization = true` for "New Interconnection" type

**Verification**: requires_reauthorization = true

---

### ISSM-36: Check Reauthorization Triggers

**Task**: Evaluate reauth need
**Type**: Positive test | **Precondition**: AO-04 (cross-persona: AO issues ATO) + ISSM-35

```text
Check if Eagle Eye needs reauthorization
```

**Expected Tool**: `compliance_reauthorization_workflow`
**Expected Output**:
- Triggers: expiration status, unreviewed significant changes, compliance drift

**Verification**: At least one trigger from ISSM-35

---

### ISSM-37: Multi-System Dashboard

**Task**: View portfolio overview
**Type**: Positive test | **Precondition**: ≥ 1 system registered

```text
Show the multi-system compliance dashboard
```

**Expected Tool**: `compliance_multi_system_dashboard`
**Expected Output**:
- Portfolio view with all systems
- Per system: RMF step, auth status, compliance score, open findings, expiration

**Verification**: Eagle Eye appears in dashboard

---

### ISSM-38: Export to eMASS

**Task**: Export for eMASS interoperability
**Type**: Positive test | **Precondition**: ISSM-11 (baseline) + SCA-06 to SCA-09 (cross-persona: SCA assessments)

```text
Export Eagle Eye to eMASS format
```

**Expected Tool**: `compliance_export_emass`
**Expected Output**:
- eMASS-compatible Excel workbook
- Sheets: system, controls, findings, POA&M

**Verification**: Export generated successfully

---

### ISSM-39: View Audit Log

**Task**: Review action history
**Type**: Positive test | **Precondition**: Any actions performed

```text
Show the audit log for Eagle Eye
```

**Expected Tool**: `compliance_audit_log`
**Expected Output**:
- Chronological audit trail
- Per entry: user, action, timestamp, entity

**Verification**: Multiple entries from prior test cases

---

### ISSM-40: Re-Import Prisma After Remediation

**Task**: Verify remediation via fresh scan
**Type**: Positive test | **Precondition**: ISSM-19 + ENG-08 (cross-persona: Engineer applies remediation)

```text
Import the latest Prisma Cloud scan for Eagle Eye to verify remediation
```

**Expected Tool**: `compliance_import_prisma_csv`
**Expected Output**:
- New import record
- Previously open findings now resolved
- Trend shows improvement

**Verification**: Resolved finding count > 0

---

## ISSM Results Summary

| Metric | Value |
|--------|-------|
| Total Test Cases | 61 |
| Passed | ___ |
| Failed | ___ |
| Blocked | ___ |
| Skipped | ___ |
| Avg Response Time | ___s |
| Max Response Time | ___s |

### Issues Found

| # | TC-ID | Severity | Description | Root Cause |
|---|-------|----------|-------------|------------|
| | | | | |

### Key Artifacts Created

| Artifact | ID / Value | Test Case |
|----------|-----------|-----------|
| System ID | _______________ | ISSM-01 |
| SAP ID | _______________ | ISSM-41 |
| SAP Hash | _______________ | ISSM-43 |
| PTA ID | _______________ | ISSM-44 |
| PIA ID | _______________ | ISSM-45 |
| Interconnection ID | _______________ | ISSM-48 |
| Agreement ID | _______________ | ISSM-52 |
| SSP Completion | ___% | ISSM-57 |
| OSCAL Controls | _______________ | ISSM-58 |
| Board ID | _______________ | ISSM-26 |
| Compliance Score | _______________ | ISSM-33 |

**Checkpoint**: ⬜ ISSM (61 tests) complete. Eagle Eye fully provisioned through Monitor phase with privacy, interconnections, and SSP review. ISSO testing can begin.

---

## HW/SW Inventory Review (ISSM-INV-01 to ISSM-INV-02)

### ISSM-INV-01: Review Portfolio Inventory Status

**Task**: Check inventory completeness across systems in portfolio

```text
@ato Check inventory completeness for Eagle Eye
```

**Expected Tool**: `inventory_completeness`
**Expected Output**: Completeness report with score and issue breakdown

### ISSM-INV-02: Review Inventory Export

**Task**: Review eMASS-ready inventory export before submission

```text
@ato Export the HW/SW inventory for Eagle Eye including decommissioned items
```

**Expected Tool**: `inventory_export` with `include_decommissioned` = true
**Expected Output**: Excel workbook with all items including decommissioned

---

## Narrative Governance — Approval Workflow (ISSM-NGV-01 to ISSM-NGV-05)

> Feature 024: ISSM reviews, approves, and requests revisions on submitted narratives. Batch operations and progress dashboard supported.

### ISSM-NGV-01: View Narrative Approval Progress

**Task**: Check overall narrative approval progress across all control families

```text
@ato Show the narrative approval progress for Eagle Eye
```

**Expected Tool**: `compliance_narrative_approval_progress`
**Expected Output**: Overall counts (`total_controls`, `approved`, `draft`, `in_review`, `needs_revision`, `missing`), `approval_percentage`, per-family breakdown, `review_queue`, `staleness_warnings`
**Record**: approval_percentage = ___

### ISSM-NGV-02: Review & Approve Narrative

**Task**: Approve a submitted narrative for AC-1

```text
@ato Approve the AC-1 narrative for Eagle Eye
```

**Expected Tool**: `compliance_review_narrative` with `decision` = "approve"
**Expected Output**: `previous_status: InReview`, `new_status: Approved`, `reviewed_by`, `reviewed_at`
**Record**: new_status = ___

### ISSM-NGV-03: Review & Request Revision

**Task**: Request revision of a submitted narrative with comments

```text
@ato Request revision of the AC-2 narrative for Eagle Eye — comments: "Please add specific Azure AD configuration details for account management"
```

**Expected Tool**: `compliance_review_narrative` with `decision` = "request_revision"
**Expected Output**: `new_status: NeedsRevision`, `comments` recorded
**Record**: new_status = ___

### ISSM-NGV-04: Batch Approve AC Family Narratives

**Task**: Batch approve all InReview narratives in the AC family

```text
@ato Batch approve all AC family narratives for Eagle Eye
```

**Expected Tool**: `compliance_batch_review_narratives` with `decision` = "approve", `family_filter` = "AC"
**Expected Output**: `reviewed_count`, `skipped_count`, `reviewed_controls`, `skipped_controls`
**Record**: reviewed_count = ___ | skipped_count = ___

### ISSM-NGV-05: View Narrative Version History (Audit Trail)

**Task**: Review the version history for AC-1 to verify audit trail

```text
@ato Show the full version history for the AC-1 narrative of Eagle Eye
```

**Expected Tool**: `compliance_narrative_history`
**Expected Output**: Complete version list with `authored_by`, `authored_at`, `change_reason`, `status` for each version
**Record**: total_versions = ___
