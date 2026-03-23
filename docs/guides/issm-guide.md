# ISSM Guide — RMF System Registration Workflow

> Feature 015: Persona-Driven RMF Workflows

This guide walks an Information System Security Manager (ISSM) through the system registration and RMF lifecycle workflow using the ATO Copilot MCP tools.

!!! tip "New to ATO Copilot?"
    If this is your first time using ATO Copilot as an ISSM, start with the [ISSM Getting Started](../getting-started/issm.md) page for prerequisites, first-time setup, and your first 3 commands.

---

## Prerequisites

- Access to the ATO Copilot MCP server
- `Compliance.SecurityLead` role assigned (see [Persona Overview](../personas/index.md) for role details)
- Knowledge of the system's Azure resource inventory

---

## Workflow Overview

```
1. Register System
2. Identify System Components (People, Places, Things)
3. Define Authorization Boundary
4. Assign RMF Roles
5. Advance to Categorize Phase
6. (Continue through RMF lifecycle...)
```

!!! info "NIST SP 800-37 Rev 2 Task Order"
    Per Tasks P-16 and P-17, asset identification (Components) precedes authorization boundary definition. Populate your component inventory first, then define the boundary around those identified assets.

---

## Step 1: Register the System

Use `compliance_register_system` to register a new information system:

```
Tool: compliance_register_system
Parameters:
  name: "My Application System"
  system_type: "MajorApplication"
  mission_criticality: "MissionCritical"
  hosting_environment: "Government"
  acronym: "MAS"
  description: "Primary mission application hosted in Azure Government"
```

The system starts in the **Prepare** phase. Note the returned `id` — you'll need it for all subsequent operations.

**System Types:**
- `MajorApplication` — Standalone application performing a specific function
- `GeneralSupportSystem` — Interconnected set of IT resources under same control
- `Enclave` — Collection of computing environments within a defined boundary
- `PlatformIt` — Shared IT infrastructure service
- `CloudServiceOffering` — Cloud-based service offering

---

## Step 2: Identify System Components

Before defining the authorization boundary, inventory the system's components using the **People, Places, and Things** model. Navigate to the system-level **Components** page in the dashboard or use MCP tools:

- **People** — Security roles and personnel (ISSM, ISSO, SCA, System Admin)
- **Places** — Locations where system components reside (Azure Gov East, Data Center)
- **Things** — Technical assets and tools (Entra ID, Defender, Key Vault, SQL Database)

Add components via the dashboard at `/systems/{systemId}/components` or use the MCP API. For Azure-hosted systems, use **Discover from Azure** to auto-import cloud resources as "Thing" components.

!!! tip "Component-Assessment Linkage"
    Azure-backed components automatically link to compliance findings by matching Azure resource IDs. Per-component risk summaries (open finding count, severity, overdue remediations) appear on the **Assessment detail view** and **Remediation page** — not on the Components page itself.

---

## Step 3: Define the Authorization Boundary

Add Azure resources to the system's authorization boundary:

```
Tool: compliance_define_boundary
Parameters:
  system_id: "<system-guid>"
  resources:
    - resource_id: "/subscriptions/xxx/resourceGroups/rg-prod/providers/Microsoft.Compute/virtualMachines/vm-app"
      resource_type: "Microsoft.Compute/virtualMachines"
      resource_name: "Production App Server"
    - resource_id: "/subscriptions/xxx/resourceGroups/rg-prod/providers/Microsoft.Sql/servers/sql-prod"
      resource_type: "Microsoft.Sql/servers"
      resource_name: "Production Database"
```

If a resource should be excluded (e.g., managed by another team):

```
Tool: compliance_exclude_from_boundary
Parameters:
  system_id: "<system-guid>"
  resource_id: "/subscriptions/xxx/resourceGroups/rg-shared/providers/..."
  rationale: "Managed under separate ATO by shared services team"
```

---

## Step 4: Assign RMF Roles

Assign required personnel roles:

```
Tool: compliance_assign_rmf_role
Parameters:
  system_id: "<system-guid>"
  role: "Issm"
  user_id: "jane.smith@agency.gov"
  user_display_name: "Jane Smith"
```

**Required Roles:**
| Role | Description |
|------|-------------|
| `AuthorizingOfficial` | Senior official who accepts risk and grants ATO |
| `Issm` | Manages the security program for assigned systems |
| `Isso` | Implements and monitors security controls day-to-day |
| `Sca` | Assesses security controls for effectiveness |
| `SystemOwner` | Program manager responsible for the system |

List current role assignments with:

```
Tool: compliance_list_rmf_roles
Parameters:
  system_id: "<system-guid>"
```

---

## Step 5: Advance to Categorize

Once the system has at least one role assigned and at least one boundary resource, you can advance to the Categorize phase:

```
Tool: compliance_advance_rmf_step
Parameters:
  system_id: "<system-guid>"
  target_step: "Categorize"
```

### Gate Requirements

Each transition has specific gate conditions that must be met:

| From → To | Requirements |
|-----------|-------------|
| Prepare → Categorize | ≥1 RMF role assigned + ≥1 boundary resource |
| Categorize → Select | Security categorization defined + ≥1 information type |
| Select → Implement | Control baseline exists |
| Implement → Assess | Advisory check (no hard block) |
| Assess → Authorize | Advisory check (no hard block) |
| Authorize → Monitor | Advisory check (no hard block) |

If gate conditions aren't met, the tool returns detailed `gate_results` explaining what's missing.

### Force Override

In exceptional cases, an authorized official can force-advance past failed gates:

```
Tool: compliance_advance_rmf_step
Parameters:
  system_id: "<system-guid>"
  target_step: "Categorize"
  force: true
```

Force overrides are logged for audit purposes. Use sparingly.

### Regression (Moving Backward)

Moving to an earlier phase always requires `force: true` and is logged as a regression event:

```
Tool: compliance_advance_rmf_step
Parameters:
  system_id: "<system-guid>"
  target_step: "Prepare"
  force: true
```

---

## Step 6: View System Status

At any time, retrieve the full system status:

```
Tool: compliance_get_system
Parameters:
  system_id: "<system-guid>"
```

This returns the current RMF phase, security categorization, boundary resource count, role assignments, and control baseline (if defined).

To see all registered systems:

```
Tool: compliance_list_systems
Parameters:
  page: 1
  page_size: 20
  active_only: true
```

---

## Security Categorization Workflow (User Story 2)

After the system is registered and advanced to the **Categorize** phase, the ISSM performs FIPS 199 categorization.

### Step 1: Get Suggested Information Types

Use the AI-assisted suggestion tool to identify relevant SP 800-60 information types:

```
Tool: compliance_suggest_info_types
Parameters:
  system_id: "<system-guid>"
  description: "Financial management and audit logging system"
```

The tool returns a ranked list of suggested information types with confidence scores, SP 800-60 identifiers, and default C/I/A impact levels.

### Step 2: Categorize the System

Apply the selected information types with their impact levels:

```
Tool: compliance_categorize_system
Parameters:
  system_id: "<system-guid>"
  information_types:
    - sp800_60_id: "C.3.1.4"
      name: "Financial Management"
      confidentiality_impact: "Moderate"
      integrity_impact: "Moderate"
      availability_impact: "Low"
    - sp800_60_id: "C.3.5.8"
      name: "Information Security"
      confidentiality_impact: "Moderate"
      integrity_impact: "High"
      availability_impact: "Moderate"
  justification: "Categorized per mission requirements and SP 800-60 Vol. 2"
```

The tool computes:
- **High-water mark**: Maximum C/I/A across all information types → Overall categorization
- **DoD Impact Level**: Low→IL2, Moderate→IL4, High→IL5
- **NIST Baseline**: Derived from overall categorization
- **FIPS 199 Notation**: Formal `SC System = {(confidentiality, X), ...}` string

### Step 3: Verify Categorization

Retrieve and review the stored categorization:

```
Tool: compliance_get_categorization
Parameters:
  system_id: "<system-guid>"
```

### Adjusting Impact Levels

If a provisional impact level needs adjustment, set `uses_provisional: false` and provide `adjustment_justification`:

```json
{
  "sp800_60_id": "C.3.1.4",
  "name": "Financial Management",
  "confidentiality_impact": "High",
  "integrity_impact": "Moderate",
  "availability_impact": "Low",
  "uses_provisional": false,
  "adjustment_justification": "Elevated confidentiality due to PII and financial data"
}
```

### Re-categorization

Calling `compliance_categorize_system` again fully **replaces** the previous categorization. The old information types are removed and replaced with the new set.

---

## Next Steps

After completing security categorization, the ISSM continues with control baseline selection and tailoring.

---

## Step 5: Select Control Baseline

Use `compliance_select_baseline` to select the NIST SP 800-53 control baseline matching the system's FIPS 199 categorization. The system must already be categorized.

```
Tool: compliance_select_baseline
Parameters:
  system_id: "<system-guid>"
  apply_overlay: true
```

**Baseline Levels:**
- **Low** — 152 controls (IL2 systems)
- **Moderate** — 329 controls (IL4 systems)
- **High** — 400 controls (IL5/IL6 systems)

**CNSSI 1253 Overlay:** When `apply_overlay` is true (default), the tool automatically applies the CNSSI 1253 overlay matching the DoD Impact Level derived from categorization. This adds enhancement controls specific to the IL.

### Re-selecting a Baseline

Calling `compliance_select_baseline` again fully **replaces** the previous baseline, including all tailoring and inheritance records. Use this after re-categorization.

---

## Step 6: Tailor the Baseline

Use `compliance_tailor_baseline` to add organization-specific controls or remove non-applicable controls:

```
Tool: compliance_tailor_baseline
Parameters:
  system_id: "<system-guid>"
  tailoring_actions:
    - control_id: "ZZ-99"
      action: "Added"
      rationale: "Organization-specific security monitoring control"
    - control_id: "PE-5"
      action: "Removed"
      rationale: "Not applicable — system is 100% cloud-hosted with no physical media"
```

**Tailoring Best Practices:**
- Always provide a meaningful rationale — this is captured in the audit trail
- Overlay-required controls can be removed but will generate a WARNING
- Added controls appear in the baseline and CRM alongside NIST controls
- Review tailoring decisions with the AO before proceeding

---

## Step 7: Set Control Inheritance

### Option A: Apply Org-Level Defaults (Recommended)

If your organization has defined security capabilities with NIST control mappings, use **Derive Org Defaults** to automatically set inheritance designations across all systems:

1. Navigate to the **[Security Capabilities Hub](/capabilities)** and import a CSP profile or CRM spreadsheet (see the [Capabilities Hub guide](security-capabilities.md)).
2. Navigate to the **Control Inheritance** page for any system.
3. Click **Derive Org Defaults** — this scans org-wide capability-control mappings and creates inheritance defaults.
4. All systems receive `OrgDerived` designations for mapped controls.
5. Review the results: the summary bar shows Org Defaults and Overrides counts.

Org defaults cascade automatically — when you add or update capabilities in the Security Capabilities Hub, defaults are re-derived and propagated to all system baselines.

To override an org default for a specific system, use the inline editor or bulk update — the designation source changes to Manual.

To restore an org default after overriding, select the control(s) and click **Revert to Org Defaults**.

### Option B: Manual Inheritance Setting

Use `compliance_set_inheritance` to map controls to their inheritance provider (e.g., a FedRAMP-authorized CSP):

```
Tool: compliance_set_inheritance
Parameters:
  system_id: "<system-guid>"
  inheritance_mappings:
    - control_id: "AC-1"
      inheritance_type: "Inherited"
      provider: "Azure Government (FedRAMP High)"
    - control_id: "AC-2"
      inheritance_type: "Shared"
      provider: "Azure Government"
      customer_responsibility: "Customer configures access policies and reviews accounts quarterly"
    - control_id: "AU-2"
      inheritance_type: "Customer"
```

**Inheritance Types:**
- **Inherited** — Fully satisfied by the CSP (e.g., physical security controls for cloud-hosted systems)
- **Shared** — Partially satisfied by the CSP; customer has documented responsibility
- **Customer** — Entirely the customer's responsibility to implement

---

## Step 8: Generate Customer Responsibility Matrix

Use `compliance_generate_crm` to generate a CRM grouped by NIST 800-53 control family:

```
Tool: compliance_generate_crm
Parameters:
  system_id: "<system-guid>"
```

The CRM shows inheritance coverage by family and highlights undesignated controls that still need inheritance mapping. Use this to:
- Track SSP completion progress
- Identify gaps in inheritance documentation
- Report to the AO on control responsibility distribution

---

## Further Steps

After completing baseline selection and tailoring, the ISSM workflow continues with:

- **User Story 4**: Control Implementation — Document how each control is implemented
- **User Story 5**: Assessment — Evaluate control effectiveness with the SCA role
- **User Story 6/7**: Assessment Artifacts — Snapshots, evidence verification, SAR generation
- **User Story 8**: Authorization — POA&M, RAR, authorization package (see below)

---

## Authorization Workflow (US8)

After assessment is complete, the ISSM prepares the authorization package for the Authorizing Official (AO).

### Create POA&M Items

For each open finding, create a formal Plan of Action & Milestones entry:

```json
{
  "system_id": "<system-guid>",
  "finding_id": "<finding-guid>",
  "weakness": "Missing MFA enforcement for privileged accounts",
  "control_id": "IA-2(1)",
  "cat_severity": "CatI",
  "poc": "John Smith",
  "scheduled_completion": "2025-06-30",
  "resources_required": "Azure AD P2 license and 20 hours engineering",
  "milestones": "[{\"description\":\"Configure Conditional Access policies\",\"target_date\":\"2025-03-31\"},{\"description\":\"Enable MFA for all admins\",\"target_date\":\"2025-05-31\"},{\"description\":\"Validate and close finding\",\"target_date\":\"2025-06-30\"}]"
}
```

Tool: `compliance_create_poam`

The POA&M links to the specific finding and NIST control, with a CAT severity and milestone schedule.

### List & Track POA&M Items

Monitor POA&M progress with filtering:

```json
{
  "system_id": "<system-guid>",
  "status_filter": "Ongoing",
  "overdue_only": "true"
}
```

Tool: `compliance_list_poam`

Filter options:
- **status_filter**: `Ongoing`, `Completed`, `Delayed`, `RiskAccepted`
- **severity_filter**: `CatI`, `CatII`, `CatIII`
- **overdue_only**: `true` to show only items past their scheduled completion

### Generate Risk Assessment Report (RAR)

Generate the RAR for AO review:

```json
{
  "system_id": "<system-guid>",
  "assessment_id": "<assessment-guid>"
}
```

Tool: `compliance_generate_rar`

The RAR includes:
- Executive summary with aggregate risk level
- Per-family risk breakdown (AC, AU, IA, etc.)
- CAT severity analysis (CAT I/II/III counts)
- Markdown content ready for inclusion in the authorization package

### Bundle Authorization Package

Compile all documents into a single authorization package:

```json
{
  "system_id": "<system-guid>",
  "include_evidence": "true"
}
```

Tool: `compliance_bundle_authorization_package`

The package includes:

| Document | Source |
|----------|--------|
| System Security Plan (SSP) | From `ComplianceDocuments` |
| Security Assessment Report (SAR) | From `ComplianceDocuments` |
| Risk Assessment Report (RAR) | Generated dynamically |
| Plan of Action & Milestones (POA&M) | Generated from POA&M items |
| Customer Responsibility Matrix (CRM) | From `ComplianceDocuments` |
| ATO Letter | From `ComplianceDocuments` |

Documents not yet created will show as `not_found` in the bundle status.

### View Risk Register

After AO issues a decision, review accepted risks:

```json
{
  "system_id": "<system-guid>",
  "status_filter": "active"
}
```

Tool: `compliance_show_risk_register`

The risk register shows all risk acceptances with their expiration dates, compensating controls, and finding details. Past-due acceptances are automatically expired on query.

---

## Complete ISSM Workflow

```
1. Register System                    → compliance_register_system
2. Define Authorization Boundary      → compliance_define_boundary
3. Assign RMF Roles                   → compliance_assign_rmf_role
4. Categorize System (FIPS 199)       → compliance_categorize_system
5. Select Control Baseline            → compliance_select_baseline
6. Tailor & Set Inheritance           → compliance_tailor_baseline, compliance_set_inheritance
7. Write Control Narratives           → compliance_write_narrative
8. Generate SSP                       → compliance_generate_ssp
9. Assess Controls (with SCA)         → compliance_assess_control
10. Generate SAR                      → compliance_generate_sar
11. Create POA&M Items                → compliance_create_poam
12. Generate RAR                      → compliance_generate_rar
13. Bundle Authorization Package      → compliance_bundle_authorization_package
14. AO Issues Decision                → compliance_issue_authorization (AO only)
15. Monitor Risk Register             → compliance_show_risk_register
```

## Continuous Monitoring Workflow (US9)

### Creating a ConMon Plan

After authorization, establish a continuous monitoring plan to track ongoing compliance:

```text
Create ConMon plan for system {system_id} with monthly assessments
```

The `compliance_create_conmon_plan` tool creates (or updates) a plan with:
- **Assessment frequency**: Monthly, Quarterly, or Annually
- **Annual review date**: When the full plan review occurs
- **Report distribution**: Who receives ConMon reports (role names or user IDs)
- **Significant change triggers**: Custom triggers beyond the 10 built-in types

### Generating Periodic Reports

Generate compliance reports that track score drift from the authorization baseline:

```text
Generate a monthly ConMon report for system {system_id}, period 2026-02
```

The `compliance_generate_conmon_report` tool produces:
- Current compliance score vs. authorized baseline
- New and resolved findings since last report
- Open and overdue POA&M items
- Markdown report content suitable for distribution
- **Watch data enrichment** (Phase 17): Monitoring enabled status, active drift alert count, auto-remediation rule count, and last monitoring check timestamp — automatically populated from ComplianceWatchService data when monitoring is configured for the system's subscriptions

### Tracking ATO Expiration

Monitor authorization expiration with graduated alerts:

```text
Check ATO expiration for system {system_id}
```

The `compliance_track_ato_expiration` tool provides alerts at:
- **90 days** (Info): Begin reauthorization planning
- **60 days** (Warning): Submit reauthorization package
- **30 days** (Urgent): Escalate to AO immediately
- **Expired**: System operating without authorization

**Phase 17 Enhancement**: Each alert level above "None" automatically creates a `ComplianceAlert` through the alert pipeline, triggering notifications via `AlertNotificationService`. Graduated severity: Low@90d, Medium@60d, High@30d, Critical@expired.

### Reporting Significant Changes

Report changes that may trigger reauthorization:

```text
Report a significant change for system {system_id}: New Interconnection — "Added VPN tunnel to partner org"
```

The `compliance_report_significant_change` tool automatically classifies whether the change requires reauthorization based on 10 built-in trigger types (New Interconnection, Major Upgrade, Data Type Change, etc.).

**Phase 17 Enhancement**: When a significant change requires reauthorization, a `ComplianceAlert` (type: Violation, severity: High) is automatically created and routed through the notification pipeline. Additionally, when ComplianceWatchService detects drift exceeding the configured threshold (default: 5 resources), it automatically reports a significant change of type `configuration_drift`.

### Reauthorization Workflow

Check for reauthorization triggers or initiate the workflow:

```text
Check reauthorization triggers for system {system_id}
Initiate reauthorization for system {system_id}
```

The `compliance_reauthorization_workflow` tool detects three trigger types:
1. ATO expiration (< 30 days remaining)
2. Unreviewed significant changes requiring reauthorization
3. Compliance score drift (> 10% below authorization baseline)

When initiated, the system's RMF step regresses to **Assess** and significant changes are marked as triggered.

### Multi-System Dashboard

View portfolio-wide compliance status:

```text
Show the multi-system compliance dashboard
```

The `compliance_multi_system_dashboard` tool provides an at-a-glance view of all systems with impact level, RMF step, authorization status, compliance score, open findings, POA&M items, and alert counts.

### Complete ISSM Workflow (Extended)

```
1.  Register System                    → compliance_register_system
2.  Define Authorization Boundary      → compliance_define_boundary
3.  Assign RMF Roles                   → compliance_assign_rmf_role
4.  Categorize System (FIPS 199)       → compliance_categorize_system
5.  Select Control Baseline            → compliance_select_baseline
6.  Tailor & Set Inheritance           → compliance_tailor_baseline, compliance_set_inheritance
7.  Write Control Narratives           → compliance_write_narrative
8.  Generate SSP                       → compliance_generate_ssp
9.  Assess Controls (with SCA)         → compliance_assess_control
10. Generate SAR                       → compliance_generate_sar
11. Create POA&M Items                 → compliance_create_poam
12. Generate RAR                       → compliance_generate_rar
13. Bundle Authorization Package       → compliance_bundle_authorization_package
14. AO Issues Decision                 → compliance_issue_authorization (AO only)
15. Monitor Risk Register              → compliance_show_risk_register
16. Create ConMon Plan                 → compliance_create_conmon_plan
17. Generate Periodic Reports          → compliance_generate_conmon_report
18. Track ATO Expiration               → compliance_track_ato_expiration
19. Report Significant Changes         → compliance_report_significant_change
20. Check Reauthorization Triggers     → compliance_reauthorization_workflow
21. View Portfolio Dashboard           → compliance_multi_system_dashboard
22. Send Notifications                 → compliance_send_notification
23. Export to eMASS (Excel)            → compliance_export_emass
24. Import from eMASS (Excel)          → compliance_import_emass
25. Export OSCAL JSON                  → compliance_export_oscal
26. Upload Document Template           → compliance_upload_template
27. List Document Templates            → compliance_list_templates
28. Update Document Template           → compliance_update_template
29. Delete Document Template           → compliance_delete_template
```

---

## eMASS & OSCAL Interoperability

ATO Copilot supports bidirectional data exchange with eMASS and OSCAL-compliant
systems, enabling ISSMs to work seamlessly across tools.

### Exporting to eMASS

Use `compliance_export_emass` to generate eMASS-compatible Excel spreadsheets:

- **Controls export**: Produces a `.xlsx` with standard eMASS column headers
  (System Name, Control Identifier, Implementation Status, Narrative, etc.)
- **POA&M export**: Produces a `.xlsx` matching the eMASS POA&M import template
  (POA&M ID, Weakness, Security Control Number, Milestones, etc.)
- **Full export**: Generates both worksheets in separate files

The exported files can be uploaded directly to eMASS without modification.

### Importing from eMASS

Use `compliance_import_emass` to ingest eMASS Excel exports:

1. **Dry-run first**: Always start with `dry_run: true` to preview changes
2. **Review conflicts**: Check the `conflict_details` array for mismatches
3. **Choose strategy**:
   - `skip` — Keep existing data, ignore conflicting imported values
   - `overwrite` — Replace existing data with imported values
   - `merge` — Combine narratives with separator, use latest status
4. **Apply changes**: Re-run with `dry_run: false` when satisfied

### OSCAL JSON Export

Use `compliance_export_oscal` for machine-readable compliance data:

- **SSP model**: Complete System Security Plan with control implementations
- **Assessment Results**: Assessment findings and effectiveness determinations
- **POA&M**: Plan of Action and Milestones with weakness details

All exports conform to OSCAL v1.0.6 specification and can be validated with
OSCAL validation tools.

---

## Document Templates & PDF Export

US11 adds the ability to upload custom DOCX templates and export compliance
documents in PDF and DOCX formats.

### Uploading Custom Templates

Use `compliance_upload_template` to upload a DOCX template for a specific
document type (SSP, SAR, POA&M, or RAR). Templates must be valid DOCX files
encoded as base64. The service validates:

- **File format** — Must be a valid ZIP archive with `word/document.xml`
- **Merge fields** — Scans for `{{field_name}}` placeholders and reports which
  are recognized, missing, or unknown for the document type

Each document type defines a schema of supported merge fields (e.g.,
`{{system_name}}`, `{{categorization}}`, `{{control_implementations}}` for SSP).
Templates with missing required fields still upload but produce warnings.

### Generating PDF Documents

Enhanced `compliance_generate_document` now accepts a `format` parameter:

| Format     | Output                              |
|------------|-------------------------------------|
| `markdown` | Markdown text (default, unchanged)  |
| `pdf`      | Base64-encoded PDF via QuestPDF     |
| `docx`     | Base64-encoded DOCX document        |

For PDF and DOCX formats, pass `system_id` to populate data from the database.
Optionally pass `template` with a template ID to use a previously uploaded
custom template for DOCX generation.

### Managing Templates

- `compliance_list_templates` — List all uploaded templates, optionally filtered
  by `document_type`
- `compliance_update_template` — Rename a template or replace its file content
- `compliance_delete_template` — Remove a template by ID

### Example Workflow

```text
1. Upload your organization's SSP template:
   compliance_upload_template(name="DISA SSP Template",
     document_type="ssp", file_base64="UEsDB...")

2. Generate a PDF from live data:
   compliance_generate_document(document_type="ssp",
     format="pdf", system_id="sys-001")

3. Generate DOCX using your custom template:
   compliance_generate_document(document_type="ssp",
     format="docx", system_id="sys-001", template="<template-id>")
```

---

## Air-Gapped Environment Notes

!!! warning "Monitor Phase — Disconnected Environments"
    In air-gapped or disconnected environments, the following limitations apply to ISSM Monitor phase workflows:
    
    - **eMASS export** (`compliance_export_emass`) generates the Excel file locally — manual transfer to eMASS via removable media is required.
    - **Notifications** (`compliance_send_notification`) are limited to local channels (VS Code, audit log); external email/webhook notifications are unavailable.
    - **OSCAL export** (`compliance_export_oscal`) works fully offline (file generation only).
    - **Watch monitoring** requires network access to Azure Policy/Defender for event-driven mode — use **scheduled-only mode** with local policy cache.
    - **ConMon reports** (`compliance_generate_conmon_report`) work fully offline using locally cached data.

---

## SCAP/STIG Viewer Import Workflow

> Feature 017: SCAP/STIG Viewer Import

ATO Copilot supports importing DISA STIG Viewer CKL checklists and SCAP Compliance Checker XCCDF results directly into the compliance database. Imported data auto-creates compliance findings, assessment evidence, and control effectiveness records.

### Import CKL Checklists

Use `compliance_import_ckl` to import manual STIG checklist results from DISA STIG Viewer:

```
Tool: compliance_import_ckl
Parameters:
  system_id: "<system-guid>"
  file_content: "<base64-encoded .ckl file>"
  file_name: "windows_server_2022.ckl"
  conflict_resolution: "Skip"
  dry_run: "true"
```

!!! tip "Dry Run First"
    Always run with `dry_run: true` first to preview changes. Review the summary, then re-run with `dry_run: false` to persist.

### Import XCCDF Scan Results

Use `compliance_import_xccdf` to import automated SCAP scan results:

```
Tool: compliance_import_xccdf
Parameters:
  system_id: "<system-guid>"
  file_content: "<base64-encoded .xccdf file>"
  file_name: "scan_results.xccdf"
  conflict_resolution: "Overwrite"
```

### Export CKL for eMASS Upload

Export current assessment state as a CKL checklist for upload to eMASS or review in DISA STIG Viewer:

```
Tool: compliance_export_ckl
Parameters:
  system_id: "<system-guid>"
  benchmark_id: "Windows_Server_2022_STIG"
```

### Conflict Resolution Strategies

| Strategy | Behavior |
|----------|----------|
| `Skip` | Keep existing findings unchanged (default) |
| `Overwrite` | Replace existing findings with new import data |
| `Merge` | Keep whichever finding has the higher severity |

### Review Import History

```
Tool: compliance_list_imports
Parameters:
  system_id: "<system-guid>"
  benchmark_id: "Windows_Server_2022_STIG"  (optional filter)
```

For detailed per-finding breakdown of a specific import:

```
Tool: compliance_get_import_summary
Parameters:
  import_id: "<import-record-id>"
```

### Typical ISSM STIG Import Workflow

```
1. Receive CKL/XCCDF files from assessors or scanning team
2. compliance_import_ckl (dry_run: true)  ← Preview findings
3. compliance_import_ckl (dry_run: false) ← Persist findings
4. compliance_list_imports               ← Verify import record
5. compliance_get_import_summary         ← Review per-finding details
6. compliance_export_ckl                 ← Export for eMASS upload
```

---

## Import Prisma Cloud Scan Results

Cloud systems using Prisma Cloud for CSPM can import scan results directly into ATO Copilot for compliance tracking.

### CSV Import from Prisma Console

1. Export the compliance CSV from Prisma Cloud Console → Alerts → Compliance
2. Import the CSV file:

```
Tool: compliance_import_prisma_csv
Parameters:
  file_content: "<base64-encoded CSV>"
  file_name: "prisma-alerts-2026-03-05.csv"
  system_id: "<system-guid>"     (optional — omit ONLY for auto-resolve based on Azure subscription IDs)
  conflict_resolution: "skip"    (default; "overwrite" to update existing)
  dry_run: true                  (preview first, then set to false)
```

3. Review the import summary — note `unmappedPolicies` count and `unresolvedSubscriptions`
4. For unmapped policies, consider adding NIST control mappings via your Prisma Cloud policy configuration

### API JSON Import (Enhanced)

For richer data including remediation scripts and alert history, export from the Prisma Cloud API:

```
Tool: compliance_import_prisma_api
Parameters:
  file_content: "<base64-encoded JSON>"
  file_name: "prisma-api-alerts.json"
  system_id: "<system-guid>"
```

API JSON imports include `remediable_count`, `cli_scripts_extracted`, and `alerts_with_history` metrics.

### Multi-Subscription Resolution

When `system_id` is omitted, ATO Copilot auto-resolves Azure subscription IDs to registered systems. If a subscription is unregistered, the import reports it in `unresolvedSubscriptions`. Register the subscription's system first, then re-import.

### Re-Import After Remediation

After remediation work is completed:

```
1. Export fresh Prisma Cloud scan results
2. compliance_import_prisma_csv (conflict_resolution: "overwrite")
3. compliance_prisma_trend (system_id)  ← View remediation progress
4. compliance_generate_conmon_report    ← Updated ConMon reflects new scan data
```

---

## Cloud Posture Oversight

As an ISSM, you oversee cloud security posture across multiple systems.

### Directing ISSOs to Import Prisma Scans

Instruct ISSOs to regularly import Prisma scan results at these cadence points:

- **Pre-assessment**: Import latest scans before initiating the Assess phase
- **Post-remediation**: Re-import after engineers address Prisma findings
- **Periodic ConMon**: Monthly or quarterly per your ConMon plan schedule

### Reviewing Trend Data Across Systems

```
Tool: compliance_prisma_trend
Parameters:
  system_id: "<system-guid>"
  group_by: "nist_control"   (optional — or "resource_type")
```

Review `remediationRate`, `newFindings`, and `resolvedFindings` to track compliance drift.

### Prisma Findings in ConMon Reports

Prisma-sourced findings automatically appear in ConMon reports (`compliance_generate_conmon_report`) as open/resolved finding counts. Effectiveness records created by Prisma imports feed into SAR generation.

### Policy Catalog Review

```
Tool: compliance_list_prisma_policies
Parameters:
  system_id: "<system-guid>"
```

Lists all unique Prisma policies observed, with NIST control mappings, open/resolved counts, and affected resource types. Use this to identify which policies lack NIST mappings and coordinate with your cloud team.

---

## Security Assessment Plan Review

> Feature 018: SAP Generation

As ISSM, you review and verify SAP scope before the SCA finalizes it.

### Review SAP Status

Check if a SAP exists and its current status:

```
Tool: compliance_list_saps
Parameters:
  system_id: "<system-guid>"
```

### Review SAP Content

Retrieve the full SAP document to verify scope, team assignments, and assessment methods:

```
Tool: compliance_get_sap
Parameters:
  system_id: "<system-guid>"
```

Review:

- **Schedule**: Assessment window is realistic and coordinated with stakeholders
- **Team composition**: All required roles are assigned (SCA, ISSO, ISSM, AO representative)
- **Scope notes**: Assessment scope covers all applicable controls
- **Method overrides**: Any non-standard assessment methods have documented rationale

### Request SAP Updates

If changes are needed before finalization, update the Draft SAP:

```
Tool: compliance_update_sap
Parameters:
  system_id: "<system-guid>"
  schedule_start: "2026-04-01T00:00:00Z"
  schedule_end: "2026-04-30T00:00:00Z"
  scope_notes: "Include interconnection controls per ISA review findings"
```

### Confirm Finalization

After the SCA finalizes the SAP, verify the integrity hash:

```
Tool: compliance_get_sap
Parameters:
  system_id: "<system-guid>"
```

A finalized SAP will have `status: "Finalized"` and a `content_hash` (SHA-256) for tamper detection. Finalized SAPs are immutable.

---

## Privacy Oversight

> Feature 021: Privacy & Interconnection

### PTA Determination Review

After an ISSO conducts the Privacy Threshold Analysis, review the determination:

```
Tool: compliance_check_privacy_compliance
Parameters:
  system_id: "<system-guid>"
```

Check `pta_determination` — if `PiaRequired`, verify the PIA has been generated and submitted for your review.

### PIA Approval/Rejection

Review and approve (or request revision) on the Privacy Impact Assessment:

```
Tool: compliance_review_pia
Parameters:
  system_id: "<system-guid>"
  decision: "Approved"
  reviewer_comments: "All 8 sections adequately address PII handling, retention, and disposal."
```

To request revisions:

```
Tool: compliance_review_pia
Parameters:
  system_id: "<system-guid>"
  decision: "RequestRevision"
  reviewer_comments: "Section 3 lacks specificity on PII retention periods."
  deficiencies: '["Section 3: Missing retention period details", "Section 5: No data sharing agreements referenced"]'
```

!!! note "PIA Expiration"
    Approved PIAs expire annually. Track expiration via `compliance_check_privacy_compliance` — the `expiring_within_90_days` field alerts you to upcoming renewals.

---

## Interconnection Agreement Management

> Feature 021: Privacy & Interconnection

### Validate Agreement Compliance

Check that all active interconnections have signed, current agreements:

```
Tool: compliance_validate_agreements
Parameters:
  system_id: "<system-guid>"
```

Review:

- `isFullyCompliant`: Must be `true` for Gate 4 (Interconnection Documentation) to pass
- `expiringWithin90DaysCount`: Schedule renewal before expiration
- `missingAgreementCount`: Any interconnection without an agreement blocks authorization

### Generate ISA for New Interconnections

When a new interconnection is registered, generate the ISA document:

```
Tool: compliance_generate_isa
Parameters:
  interconnection_id: "<interconnection-guid>"
```

Review the AI-drafted ISA for completeness, then register it:

```
Tool: compliance_register_agreement
Parameters:
  interconnection_id: "<interconnection-guid>"
  agreement_type: "isa"
  title: "ISA — Eagle Eye ↔ JIRA Cloud"
  status: "pending_signature"
  expiration_date: "2027-06-01T00:00:00Z"
```

### Agreement Lifecycle

Update agreement status as signatures are obtained:

```
Tool: compliance_update_agreement
Parameters:
  agreement_id: "<agreement-guid>"
  status: "signed"
  signed_by_local: "Jane Smith, ISSM"
  signed_by_local_date: "2026-03-15T00:00:00Z"
  signed_by_remote: "Bob Jones, External ISSM"
  signed_by_remote_date: "2026-03-18T00:00:00Z"
```

### Certify No Interconnections

If a system has no external interconnections, certify to satisfy the gate:

```
Tool: compliance_certify_no_interconnections
Parameters:
  system_id: "<system-guid>"
  certify: true
```

### ISA/MOU Expiration Monitoring

Include agreement expiration tracking in your ConMon routine:

```
1. compliance_validate_agreements (system_id)        ← Check all agreements
2. Review expiringWithin90DaysCount                  ← Identify upcoming renewals
3. compliance_update_agreement (status: "expired")   ← Mark expired agreements
4. compliance_generate_isa (interconnection_id)      ← Draft renewal ISA
5. compliance_register_agreement                     ← Register renewed agreement
```

---

## SSP Section Review

> Feature 022: SSP Authoring & OSCAL Export

### Check SSP Completeness

Monitor the overall SSP readiness:

```
Tool: compliance_ssp_completeness
Parameters:
  system_id: "<system-guid>"
```

Review:

- `overall_readiness_percent`: Target 100% before authorization
- `blocking_issues`: Lists sections still in Draft or not yet authored
- Per-section status: `Draft` → `UnderReview` → `Approved`

### Review SSP Sections

When ISSOs or Engineers submit sections for review, approve or request revision:

```
Tool: compliance_review_ssp_section
Parameters:
  system_id: "<system-guid>"
  section_number: 5
  decision: "approve"
  reviewer: "jane.smith@agency.gov"
  comments: "Section accurately describes the system environment and deployment topology."
```

To request revision:

```
Tool: compliance_review_ssp_section
Parameters:
  system_id: "<system-guid>"
  section_number: 12
  decision: "request_revision"
  reviewer: "jane.smith@agency.gov"
  comments: "Personnel screening requirements need to reference agency-specific policy."
```

### SSP Section Review Workflow

```
1. compliance_ssp_completeness (system_id)              ← Check overall status
2. Identify sections in "UnderReview" status
3. compliance_write_ssp_section (read section content)   ← Read current content
4. compliance_review_ssp_section (approve/revise)        ← Make decision
5. Repeat until overall_readiness_percent = 100%
```

---

## Narrative Governance — Approval Workflow

> Feature 024: Version Control + Approval Workflow

ISSMs are responsible for reviewing and approving control implementation narratives before they are included in the SSP.

### Reviewing a Single Narrative

When an ISSO or Engineer submits a narrative for review, approve or request revision:

```
Tool: compliance_review_narrative
Parameters:
  system_id: "<system-guid>"
  control_id: "AC-1"
  decision: "approve"
```

```
Tool: compliance_review_narrative
Parameters:
  system_id: "<system-guid>"
  control_id: "AC-2"
  decision: "request_revision"
  comments: "Section 2 needs more detail on audit log retention periods."
```

### Batch Review by Family

Approve all UnderReview narratives in a control family at once:

```
Tool: compliance_batch_review_narratives
Parameters:
  system_id: "<system-guid>"
  family_filter: "AC"
  decision: "approve"
```

### Checking Approval Progress

```
@ato What is the narrative approval progress for Eagle Eye?
```

Tool: `compliance_narrative_approval_progress` — shows overall %, per-family breakdown, review queue, and staleness warnings.

### Viewing Version History

```
@ato Show me the version history for AC-1 in Eagle Eye
```

Tool: `compliance_narrative_history` — review all versions before making a decision.

### ISSM Narrative Governance Workflow

```
1. compliance_narrative_approval_progress               ← Check review queue
2. compliance_narrative_history (control_id)             ← Review version history
3. compliance_narrative_diff (from/to versions)          ← Compare changes
4. compliance_review_narrative (approve/request_revision) ← Make decision
5. compliance_batch_review_narratives (family)           ← Batch approve family
6. Repeat until approval_percentage = 100%
```

---

## OSCAL Export for Authorization Package

> Feature 022: SSP Authoring & OSCAL Export

### Export OSCAL SSP

Generate the OSCAL 1.1.2 SSP JSON for inclusion in the authorization package:

```
Tool: compliance_export_oscal_ssp
Parameters:
  system_id: "<system-guid>"
  include_back_matter: true
  pretty_print: true
```

### Validate Before Submission

Always validate the OSCAL output before including in the authorization package:

```
Tool: compliance_validate_oscal_ssp
Parameters:
  system_id: "<system-guid>"
```

Check:

- `is_valid`: Must be `true`
- `errors`: Must be empty
- `warnings`: Review and address if possible (e.g., missing back-matter references)

### Pre-Authorization Checklist

Before submitting the authorization package, verify:

```
1. compliance_ssp_completeness    ← 100% readiness
2. compliance_validate_oscal_ssp  ← Valid OSCAL output
3. compliance_check_privacy_compliance ← Privacy gate satisfied
4. compliance_validate_agreements ← Interconnection gate satisfied
5. compliance_list_saps           ← SAP finalized
6. compliance_bundle_authorization_package ← Package everything
```

---

## HW/SW Inventory Management

> **Feature 025** — Register, manage, import/export, and verify completeness of hardware and software inventory items for eMASS and SSP §11.

### Quick-Start: Auto-Seed from Boundary

If the system already has an authorization boundary defined, auto-seed creates inventory items from boundary resources:

```
Tool: inventory_auto_seed
Parameters: { "system_id": "{system-id}" }
```

The tool maps Azure resource types to hardware functions (VMs → Server, NSGs → NetworkDevice, Storage → Storage) and links each item back to its boundary resource for traceability.

### Registering Inventory Items

```
Tool: inventory_add_item
Parameters: {
  "system_id": "{system-id}",
  "item_name": "web-server-01",
  "type": "hardware",
  "function": "Server",
  "manufacturer": "Dell",
  "ip_address": "10.0.0.1"
}
```

For software installed on hardware:

```
Tool: inventory_add_item
Parameters: {
  "system_id": "{system-id}",
  "item_name": "RHEL 9.2",
  "type": "software",
  "function": "OperatingSystem",
  "vendor": "Red Hat",
  "version": "9.2",
  "parent_hardware_id": "{hw-item-id}"
}
```

### Completeness Check

Before exporting to eMASS, verify inventory completeness:

```
Tool: inventory_completeness
Parameters: { "system_id": "{system-id}" }
```

The tool checks three dimensions:
1. **Missing required fields** — items lacking manufacturer, IP (for servers), vendor (for software)
2. **Unmatched boundary resources** — boundary resources with no corresponding inventory entry
3. **Hardware without software** — servers/workstations with no installed software registered

### Export to eMASS Excel

```
Tool: inventory_export
Parameters: { "system_id": "{system-id}" }
```

Produces an Excel workbook with separate Hardware and Software worksheets matching the eMASS format.

### Import from Excel

```
Tool: inventory_import
Parameters: {
  "system_id": "{system-id}",
  "file_base64": "{base64-encoded-xlsx}",
  "dry_run": true
}
```

Use `dry_run: true` to preview the import before persisting. Row-level errors are reported with worksheet name, row number, and error detail.

### Decommissioning

```
Tool: inventory_decommission_item
Parameters: {
  "item_id": "{item-id}",
  "rationale": "End of life — replaced by web-server-02"
}
```

Decommissioning a hardware item automatically cascades to all installed software.

### ISSM Inventory Workflow

```
1. inventory_auto_seed                ← Quick-start from boundary
2. inventory_add_item (repeat)        ← Add items not in boundary
3. inventory_completeness             ← Verify coverage
4. inventory_export                   ← Export for eMASS
5. compliance_generate_ssp            ← §11 includes HW/SW tables
```

---

## Implementation Roadmap Workflow

> Feature 031: Implementation Roadmap

The Implementation Roadmap feature generates a phased action plan for closing compliance gaps, with AI-driven clustering, effort estimates, risk projections, and Kanban integration.

### Generate a Roadmap

```
Tool: compliance_generate_roadmap
Parameters:
  system_id: "<system-guid>"
```

The system must have a selected baseline and at least one unmapped control (gap). The tool generates a multi-phase roadmap with:

- **AI-driven clustering** of controls into phases (falls back to severity-based grouping if AI is unavailable)
- **Effort estimates** informed by historical Kanban task completion data
- **Risk reduction projections** using weighted severity scores (CAT I=10, CAT II=5, CAT III=1)
- **NIST dependency ordering** ensuring prerequisites precede dependent controls

### View the Roadmap

```
Tool: compliance_get_roadmap
Parameters:
  system_id: "<system-guid>"
  include_items: true
```

Or view in the dashboard at `/systems/<id>/roadmap` with timeline visualization, risk reduction curve, and expandable phase tables.

### Track Progress

```
Tool: compliance_get_roadmap_progress
Parameters:
  system_id: "<system-guid>"
```

Shows overall completion %, per-phase progress, overdue detection, and actual vs. projected risk reduction.

### Bridge to Kanban

```
Tool: compliance_create_board_from_roadmap
Parameters:
  system_id: "<system-guid>"
```

Creates a pre-populated Kanban board with one task per roadmap item. Status changes on Kanban tasks automatically sync back to the roadmap.

### Update and Reassign

```
Tool: compliance_update_roadmap
Parameters:
  system_id: "<system-guid>"
  move_item: { "item_id": "<id>", "target_phase_id": "<id>" }
```

Supports moving items between phases, updating effort estimates, reassigning roles, merging phases, and splitting phases. Changes propagate to linked Kanban tasks.

### Export PDF

```
Tool: compliance_export_roadmap_pdf
Parameters:
  system_id: "<system-guid>"
```

Generates a PDF report with summary metrics, phase timeline, and detailed control tables.

---

## See Also

- [ISSM Getting Started](../getting-started/issm.md) — First-time setup and first 3 commands
- [Persona Overview](../personas/index.md) — All personas, RACI matrix, and role definitions
- [RMF Phase Reference](../rmf-phases/index.md) — Phase-by-phase workflow details
- [ISSO Guide](../personas/isso.md) — ISSO workflows for day-to-day operations
- [Compliance Watch Guide](compliance-watch.md) — Detailed continuous monitoring documentation
- [Quick Reference Card](../reference/quick-reference-cards.md) — Printable ISSM cheat sheet

---

## Boundary Oversight (Feature 033)

ISSMs can review and manage authorization boundaries for systems under their purview:

- **Boundary Review**: View boundary definitions, resource assignments, and coverage metrics on the Boundary Management page
- **Gap Analysis**: Use the boundary selector to compare compliance coverage across different security perimeters
- **SSP §11 Verification**: The SSP Authorization Boundary section auto-generates with per-boundary resource tables and component inventories
- **Boundary Comparison**: The gap analysis page shows a comparison table when "All Boundaries" is selected, highlighting coverage differences

---

## POA&M Oversight (Feature 039)

ISSMs have full oversight of POA&M items across systems:

- **Dashboard Review**: Navigate to `/systems/{systemId}/poam` for the POA&M management dashboard with summary metrics, severity heatbar, and item table
- **Trend Analysis**: Use the Trends tab to review open-over-time, closure rates, aging breakdown, and time-to-close distributions
- **eMASS Export**: Export POA&M data in eMASS Excel format for submission to authorization systems
- **Ticketing Management**: Configure Jira/ServiceNow integration from the Ticketing tab and monitor sync status
- **Chat Commands**: Use `compliance_poam_metrics` for quick status checks and `compliance_export_poam` for on-demand exports
