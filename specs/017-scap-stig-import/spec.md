# Feature 017: SCAP/STIG Viewer Import (.ckl/.xccdf)

**Created**: 2026-03-01  
**Status**: Strategic Plan  
**Purpose**: Enable Security Posture Intelligence Navigator to ingest real-world assessment scan results from DISA STIG Viewer (.ckl) and SCAP Compliance Checker (.xccdf) files, mapping them to registered systems, creating compliance findings, and linking evidence to the existing assessment pipeline.

---

## Clarifications

### Session 2026-03-03

- Q: How should `Not_Reviewed` CKL entries be handled — skip (no finding) or create as Open? → A: Create `ComplianceFinding` with `FindingStatus.Open` and note "Not yet reviewed" — unreviewed rules are tracked as open until explicitly assessed.
- Q: When re-importing, should effectiveness re-evaluate all findings for a NIST control or only consider the latest import? → A: Re-evaluate all `ComplianceFinding` records for the NIST control after each import — effectiveness always reflects the aggregate current state of all mapped rules.
- Q: What happens when the exact same file (identical SHA-256 hash) is imported twice for the same system? → A: Warn but allow — proceed with import, include a warning in the response: "File previously imported on {date}". This supports legitimate re-imports (e.g., retry with different conflict resolution) while catching accidental duplicates.
- Q: Should findings and effectiveness records be created for NIST controls resolved via CCI but outside the system's control baseline? → A: Create `ComplianceFinding` records (for audit trail) but skip `ControlEffectiveness` for controls outside the baseline. Include out-of-baseline controls in import warnings.
- Q: Should CKL export include only rules with assessment data, or the full STIG benchmark? → A: Export the complete STIG benchmark — all rules from `StigControl` for the selected benchmark, filling in statuses from matching `ComplianceFinding` records, defaulting to `Not_Reviewed` for rules with no assessment data. This produces eMASS-compliant CKL files.

---

## Part 1: The Problem

### Why This Matters

In every DoD ATO process, engineers and ISSOs run DISA STIG Viewer and SCAP Compliance Checker (SCC) against their systems to evaluate compliance with Security Technical Implementation Guides. These tools produce structured XML output:

- **STIG Viewer** exports `.ckl` (Checklist) files — one per STIG benchmark per system. A typical ATO package has 5–20 CKL files (Windows Server, SQL Server, IIS, .NET, etc.).
- **SCAP Compliance Checker (SCC)** exports `.xccdf` (XCCDF Results) files — automated scan results from DISA's SCAP benchmarks.

Today, these results live in files on shared drives or in eMASS. Teams manually cross-reference CKL findings against their control baselines, hand-copy finding details into POA&Ms, and re-enter data into multiple systems. **There is no automated path to get scan results into Security Posture Intelligence Navigator.**

### The Current Gap

| What Teams Do Today | What Security Posture Intelligence Navigator Can Do Today |
|---------------------|-------------------------------|
| Run STIG Viewer, save .ckl files per system | Nothing — no CKL parser exists |
| Run SCAP SCC, export .xccdf results | Nothing — XCCDF fields exist on `StigControl` but no import |
| Manually count Open/Not A Finding/Not Applicable | `ComplianceFinding` tracks findings but only from Azure scans |
| Copy finding details into POA&M spreadsheets | POA&M tools exist but require manual finding creation |
| Upload CKL files to eMASS | eMASS export exists but no CKL/XCCDF round-trip |
| Cross-reference STIG findings to NIST controls | STIG→CCI→NIST mapping exists in `cci-nist-mapping.json` |

### The Opportunity

Security Posture Intelligence Navigator already has:
- `StigControl` records with XCCDF fields (StigId, VulnId, RuleId, BenchmarkId, StigVersion)
- `ComplianceFinding` with `StigFinding` bool and `StigId` field
- `ControlEffectiveness` for per-control assessment determinations
- `ComplianceEvidence` with `CollectionMethod` supporting "Automated" and "Manual"
- CCI→NIST mapping (~7,575 entries) linking STIGs to NIST 800-53 controls
- `IAssessmentArtifactService` with `AssessControlAsync` for effectiveness upsert
- `IEmassExportService` with conflict resolution pattern (Skip/Overwrite/Merge + dry-run)

The missing piece is **file parsing** — getting data out of CKL/XCCDF XML and into the existing entity pipeline.

---

## Part 2: The Product

### What We're Building

**SCAP/STIG Viewer Import** allows users to upload `.ckl` and `.xccdf` files through MCP tools, parse the XML, map findings to the registered system's control baseline, and create `ComplianceFinding` + `ControlEffectiveness` + `ComplianceEvidence` records automatically.

### What It Is

- A **file import pipeline** that parses DISA-standard XML formats
- A **mapping engine** that resolves STIG findings → CCI references → NIST 800-53 controls
- An **assessment integration** that feeds parsed scan results into the existing assessment workflow
- A **CKL export** capability that generates `.ckl` files from Security Posture Intelligence Navigator assessment data (for eMASS upload)

### What It Is NOT

- Not a SCAP scanner — we import results from DISA SCC, we don't run scans
- Not a STIG Viewer replacement — we import/export CKL files, we don't provide a STIG editing UI
- Not an ACAS/Nessus integration — vulnerability scan import is a separate feature
- Not an eMASS API integration — file-based exchange only (API integration is a separate feature)

### Interfaces

| Surface | User | Purpose |
|---------|------|---------|
| **MCP Tools** | ISSO, SCA | `compliance_import_ckl`, `compliance_import_xccdf`, `compliance_export_ckl`, `compliance_list_imports`, `compliance_get_import_summary` |
| **VS Code (@ato)** | Engineer, ISSO | `@ato Import my STIG Viewer checklist` → triggers file picker → parses and imports |
| **Teams Bot** | ISSM, SCA | Upload CKL attachment → bot detects file type → imports and shows summary card |

---

## Part 3: CKL and XCCDF File Formats

### 3.1 DISA STIG Viewer CKL Format

The `.ckl` file is an XML document produced by DISA STIG Viewer. Each CKL represents one STIG benchmark evaluated against one target system.

**Structure:**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!--DISA STIG Viewer :: 2.17-->
<CHECKLIST>
  <ASSET>
    <ROLE>None</ROLE>
    <ASSET_TYPE>Computing</ASSET_TYPE>
    <HOST_NAME>web-server-01</HOST_NAME>
    <HOST_IP>10.0.1.100</HOST_IP>
    <HOST_MAC>00:0A:95:9D:68:16</HOST_MAC>
    <HOST_FQDN>web-server-01.example.mil</HOST_FQDN>
    <TARGET_COMMENT></TARGET_COMMENT>
    <TECH_AREA></TECH_AREA>
    <TARGET_KEY>4089</TARGET_KEY>
    <WEB_OR_DATABASE>false</WEB_OR_DATABASE>
    <WEB_DB_SITE></WEB_DB_SITE>
    <WEB_DB_INSTANCE></WEB_DB_INSTANCE>
  </ASSET>
  <STIGS>
    <iSTIG>
      <STIG_INFO>
        <SI_DATA>
          <SID_NAME>version</SID_NAME>
          <SID_DATA>3</SID_DATA>
        </SI_DATA>
        <SI_DATA>
          <SID_NAME>stigid</SID_NAME>
          <SID_DATA>Windows_Server_2022_STIG</SID_DATA>
        </SI_DATA>
        <SI_DATA>
          <SID_NAME>releaseinfo</SID_NAME>
          <SID_DATA>Release: 1 Benchmark Date: 23 Mar 2023</SID_DATA>
        </SI_DATA>
        <SI_DATA>
          <SID_NAME>title</SID_NAME>
          <SID_DATA>Microsoft Windows Server 2022 Security Technical Implementation Guide</SID_DATA>
        </SI_DATA>
        <!-- ... more SI_DATA elements ... -->
      </STIG_INFO>
      <VULN>
        <STIG_DATA>
          <VULN_ATTRIBUTE>Vuln_Num</VULN_ATTRIBUTE>
          <ATTRIBUTE_DATA>V-254239</ATTRIBUTE_DATA>
        </STIG_DATA>
        <STIG_DATA>
          <VULN_ATTRIBUTE>Severity</VULN_ATTRIBUTE>
          <ATTRIBUTE_DATA>high</ATTRIBUTE_DATA>
        </STIG_DATA>
        <STIG_DATA>
          <VULN_ATTRIBUTE>Group_Title</VULN_ATTRIBUTE>
          <ATTRIBUTE_DATA>SRG-OS-000003-GPOS-00004</ATTRIBUTE_DATA>
        </STIG_DATA>
        <STIG_DATA>
          <VULN_ATTRIBUTE>Rule_ID</VULN_ATTRIBUTE>
          <ATTRIBUTE_DATA>SV-254239r849090_rule</ATTRIBUTE_DATA>
        </STIG_DATA>
        <STIG_DATA>
          <VULN_ATTRIBUTE>Rule_Ver</VULN_ATTRIBUTE>
          <ATTRIBUTE_DATA>WN22-AU-000010</ATTRIBUTE_DATA>
        </STIG_DATA>
        <STIG_DATA>
          <VULN_ATTRIBUTE>Rule_Title</VULN_ATTRIBUTE>
          <ATTRIBUTE_DATA>Windows Server 2022 must be configured to audit...</ATTRIBUTE_DATA>
        </STIG_DATA>
        <STIG_DATA>
          <VULN_ATTRIBUTE>CCI_REF</VULN_ATTRIBUTE>
          <ATTRIBUTE_DATA>CCI-000018</ATTRIBUTE_DATA>
        </STIG_DATA>
        <STIG_DATA>
          <VULN_ATTRIBUTE>CCI_REF</VULN_ATTRIBUTE>
          <ATTRIBUTE_DATA>CCI-000172</ATTRIBUTE_DATA>
        </STIG_DATA>
        <!-- ... more STIG_DATA attributes per VULN ... -->
        <STATUS>Open</STATUS>
        <FINDING_DETAILS>Audit policy not configured for Account Management - User Account Management.</FINDING_DETAILS>
        <COMMENTS>Remediation scheduled for Sprint 42.</COMMENTS>
        <SEVERITY_OVERRIDE></SEVERITY_OVERRIDE>
        <SEVERITY_JUSTIFICATION></SEVERITY_JUSTIFICATION>
      </VULN>
      <!-- ... hundreds more VULN elements ... -->
    </iSTIG>
  </STIGS>
</CHECKLIST>
```

**Key CKL Fields:**

| Field | Location | Maps To |
|-------|----------|---------|
| `HOST_NAME` | `ASSET` | Target system hostname |
| `stigid` | `STIG_INFO/SI_DATA` | `StigControl.BenchmarkId` |
| `version` | `STIG_INFO/SI_DATA` | STIG release version |
| `Vuln_Num` | `VULN/STIG_DATA` | `StigControl.VulnId` / `ComplianceFinding.StigId` |
| `Rule_ID` | `VULN/STIG_DATA` | `StigControl.RuleId` |
| `Rule_Ver` | `VULN/STIG_DATA` | `StigControl.StigVersion` |
| `Severity` | `VULN/STIG_DATA` | `StigSeverity` → `CatSeverity` |
| `CCI_REF` | `VULN/STIG_DATA` (multiple) | CCI→NIST mapping → `ControlEffectiveness.ControlId` |
| `STATUS` | `VULN` | `Open` / `NotAFinding` / `Not_Applicable` / `Not_Reviewed` |
| `FINDING_DETAILS` | `VULN` | `ComplianceFinding.Description` |
| `COMMENTS` | `VULN` | `ComplianceFinding.RemediationGuidance` (if relevant) |
| `SEVERITY_OVERRIDE` | `VULN` | Override the default severity |
| `SEVERITY_JUSTIFICATION` | `VULN` | Justification for severity override |

**CKL STATUS Values:**

| CKL Status | Meaning | Maps To |
|------------|---------|---------|
| `Open` | Finding is present, not remediated | `FindingStatus.Open` + `EffectivenessDetermination.OtherThanSatisfied` |
| `NotAFinding` | Checked, no vulnerability found | `FindingStatus.Remediated` + `EffectivenessDetermination.Satisfied` |
| `Not_Applicable` | Rule doesn't apply to this system | `FindingStatus.Accepted` (N/A) + no `ControlEffectiveness` record |
| `Not_Reviewed` | Not yet evaluated | `FindingStatus.Open` with note "Not yet reviewed" — tracked as open until explicitly assessed |

### 3.2 XCCDF Results Format

SCAP Compliance Checker (SCC) produces XCCDF results — an NIST-standardized XML format defined in NIST SP 800-126 Rev 3.

**Structure (simplified):**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<TestResult xmlns="http://checklists.nist.gov/xccdf/1.2"
            id="xccdf_mil.disa.stig_testresult_default"
            start-time="2026-02-15T14:30:00"
            end-time="2026-02-15T14:45:22">
  <benchmark href="xccdf_mil.disa.stig_benchmark_Windows_Server_2022_STIG"/>
  <title>SCAP SCC Scan Results</title>
  <identity authenticated="true" privileged="true">SYSTEM</identity>
  <target>web-server-01</target>
  <target-address>10.0.1.100</target-address>
  <target-facts>
    <fact name="urn:scap:fact:asset:identifier:host_name" type="string">web-server-01</fact>
    <fact name="urn:scap:fact:asset:identifier:os_name" type="string">Microsoft Windows Server 2022</fact>
    <fact name="urn:scap:fact:asset:identifier:os_version" type="string">10.0.20348</fact>
    <!-- ... more facts ... -->
  </target-facts>
  <rule-result idref="xccdf_mil.disa.stig_rule_SV-254239r849090_rule" time="2026-02-15T14:32:11"
               severity="high" weight="10.0">
    <result>fail</result>
    <check system="http://oval.mitre.org/XMLSchema/oval-definitions-5">
      <check-content-ref href="scap_mil.disa.stig_comp_U_MS_Windows_Server_2022_STIG_V1R1_SCAP_1-2_Benchmark-oval.xml"
                         name="oval:mil.disa.stig.windows_server_2022:def:254239"/>
    </check-check>
    <message severity="info">Registry value not configured as expected.</message>
  </rule-result>
  <rule-result idref="xccdf_mil.disa.stig_rule_SV-254240r849093_rule" time="2026-02-15T14:32:15"
               severity="medium" weight="10.0">
    <result>pass</result>
  </rule-result>
  <!-- ... more rule-result elements ... -->
  <score system="urn:xccdf:scoring:default" maximum="100">72.5</score>
</TestResult>
```

**Key XCCDF Result Fields:**

| Field | Location | Maps To |
|-------|----------|---------|
| `benchmark/@href` | `TestResult` | `StigControl.BenchmarkId` |
| `target` | `TestResult` | Target system hostname |
| `start-time` / `end-time` | `TestResult` | Scan timestamp |
| `rule-result/@idref` | `rule-result` | `StigControl.RuleId` (extract `SV-XXXXXX` portion) |
| `rule-result/@severity` | `rule-result` | `StigSeverity` → `CatSeverity` |
| `rule-result/@weight` | `rule-result` | `StigControl.Weight` |
| `rule-result/result` | `rule-result` | See mapping below |
| `rule-result/message` | `rule-result` | `ComplianceFinding.Description` |
| `score` | `TestResult` | Import metadata (XCCDF compliance score) |

**XCCDF Result Values:**

| XCCDF Result | Meaning | Maps To |
|-------------|---------|---------|
| `pass` | Rule requirement satisfied | `EffectivenessDetermination.Satisfied` |
| `fail` | Rule requirement not met | `FindingStatus.Open` + `EffectivenessDetermination.OtherThanSatisfied` |
| `error` | Scan encountered an error | Flagged for manual review |
| `unknown` | Could not determine result | Flagged for manual review |
| `notapplicable` | Rule not applicable to target | No finding created; mark N/A |
| `notchecked` | Rule was not evaluated | Skipped (log warning) |
| `notselected` | Rule was not selected for scan | Skipped |
| `informational` | Informational only | `FindingSeverity.Informational` (enum value exists), no effectiveness impact |
| `fixed` | Was failing, auto-remediated by SCAP | `FindingStatus.Remediated` + evidence of auto-fix |

---

## Part 4: Personas & Needs

### ISSO (Information System Security Officer)

| Need | Description | Status |
|------|-------------|--------|
| Import CKL files from STIG Viewer | Upload one or more CKL files for a registered system | **MISSING** |
| Import XCCDF results from SCAP SCC | Upload automated scan results | **MISSING** |
| See import summary with pass/fail/N-A counts | After import, see a dashboard of results | **MISSING** |
| View import history for a system | Track which scans were imported, when, by whom | **MISSING** |
| Resolve conflicts when re-importing | Choose skip/overwrite/merge for existing findings | **MISSING** |
| Map STIG findings to NIST controls automatically | CCI→NIST resolution happens on import | **MISSING** |
| Export CKL for eMASS upload | Generate .ckl from assessment data | **MISSING** |

### SCA (Security Control Assessor)

| Need | Description | Status |
|------|-------------|--------|
| Validate imported scan results | Review what was imported before accepting into assessment | **MISSING** |
| Use imported data for effectiveness determinations | SCAP results auto-populate `ControlEffectiveness` | **MISSING** |
| Compare SCAP results across scan cycles | Trend analysis between imports | **MISSING** |
| Verify evidence chain from scan import | Imported CKL/XCCDF becomes tamper-evident evidence | **MISSING** |

### Engineer

| Need | Description | Status |
|------|-------------|--------|
| Upload STIG Viewer CKL from VS Code | `@ato Import this CKL file` with file picker | **MISSING** |
| See which STIG findings were imported as Open | Filter findings by STIG import source | **MISSING** |
| Export updated CKL after remediation | Generate CKL reflecting current remediation status | **MISSING** |

---

## Part 5: Capabilities

### Phase 1: CKL Import & Mapping (Core)

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 1.1 | **CKL File Parser** | — | Parse DISA STIG Viewer `.ckl` XML files. Extract ASSET info, STIG_INFO metadata, and all VULN entries with STATUS, FINDING_DETAILS, COMMENTS, severity, CCI references. Handle malformed/truncated XML gracefully with detailed error reporting. |
| 1.2 | **STIG Finding Resolution** | — | Map parsed VULN entries to existing `StigControl` records via VulnId/RuleId/StigVersion. Report unmatched entries (STIG rules not in the curated library). |
| 1.3 | **CCI→NIST Control Mapping** | — | For each matched STIG finding, resolve CCI references to NIST 800-53 control IDs using the existing `cci-nist-mapping.json`. Link findings to the registered system's control baseline. |
| 1.4 | **ComplianceFinding Creation** | ISSO, SCA | Create `ComplianceFinding` records for each `Open` CKL entry. Set `StigFinding = true`, `StigId`, `CatSeverity`, `Source = "CKL Import"`, `ScanSource = ScanSourceType.Combined`. |
| 1.5 | **ControlEffectiveness Upsert** | SCA | For each unique NIST control resolved from CKL findings **that exists in the system's control baseline**, upsert `ControlEffectiveness` via `IAssessmentArtifactService.AssessControlAsync`. `NotAFinding` → Satisfied; `Open` → OtherThanSatisfied. Set `AssessmentMethod = "Test"`. Controls outside the baseline get `ComplianceFinding` records only (no effectiveness). |
| 1.6 | **Evidence Auto-Creation** | ISSO | Create `ComplianceEvidence` records from the CKL file. `EvidenceType = "StigChecklist"`, `EvidenceCategory = Configuration`, `CollectionMethod = "Manual"` (CKL is human-evaluated; XCCDF uses `"Automated"`). Content contains parsed finding summary. `ContentHash` computed via SHA-256. |
| 1.7 | **Import Conflict Resolution** | ISSO | When importing a CKL for a system that already has findings for the same STIG rules: `skip` (keep existing), `overwrite` (replace with imported), `merge` (keep more-recent, append details). Follows the `EmassImportOptions` pattern. |
| 1.10 | **Duplicate File Detection** | ISSO | On import, check if a `ScanImportRecord` with the same `FileHash` + `RegisteredSystemId` already exists. If so, proceed with the import but include a warning: "File previously imported on {date} (import ID: {id})". This supports legitimate re-imports with different conflict resolution while alerting to accidental duplicates. |
| 1.8 | **Dry-Run Mode** | ISSO, SCA | Preview import results without persisting changes. Show: findings to create, findings to update, controls affected, conflicts detected. |
| 1.9 | **Import MCP Tool** | ISSO | `compliance_import_ckl` tool accepting base64-encoded file content, system_id, conflict_resolution strategy, and dry_run flag. Returns import summary. |

### Phase 2: XCCDF Import

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 2.1 | **XCCDF Results Parser** | — | Parse SCAP SCC XCCDF results XML (NIST SP 800-126 Rev 3). Extract TestResult metadata, target info, rule-result elements with pass/fail/error status, severity, and check references. |
| 2.2 | **Rule ID Resolution** | — | Extract DISA STIG rule IDs from XCCDF `idref` attributes (e.g., `xccdf_mil.disa.stig_rule_SV-254239r849090_rule` → `SV-254239r849090_rule`). Match to `StigControl.RuleId`. |
| 2.3 | **Automated Scan Evidence** | SCA | XCCDF results are more authoritative than manual CKL checks (machine-verified). Evidence records include scan timestamps, tool identity, OVAL check references. `CollectionMethod = "Automated"`. |
| 2.4 | **XCCDF Score Capture** | SCA | Capture the XCCDF compliance score from the `<score>` element. Store as import metadata for trend tracking. |
| 2.5 | **Import MCP Tool** | ISSO | `compliance_import_xccdf` tool with same interface pattern as CKL import. |

### Phase 3: CKL Export

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 3.1 | **CKL Generation** | ISSO, Engineer | Generate a complete `.ckl` file for a STIG benchmark from Security Posture Intelligence Navigator assessment data. Enumerate **all** `StigControl` records matching the benchmark, fill in statuses from corresponding `ComplianceFinding` records, and default unassessed rules to `Not_Reviewed`. Includes ASSET metadata from `RegisteredSystem`. Produces eMASS-compliant CKL files with no gaps. |
| 3.2 | **Benchmark Selection** | ISSO | Choose which STIG benchmark to export (system may have findings from multiple STIGs). List available benchmarks based on imported/assessed STIG data. |
| 3.3 | **Export MCP Tool** | ISSO | `compliance_export_ckl` tool returning base64-encoded CKL XML content. |

### Phase 4: Import Management & Observability

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 4.1 | **Import History Tracking** | ISSO, SCA | Track all imports: file name, file hash, import timestamp, imported by, system, finding counts (open/pass/N-A/skipped), conflict resolution used. |
| 4.2 | **Import Summary Dashboard** | ISSM, SCA | View import statistics per system: total imports, latest import date, STIG benchmarks covered, overall pass rate, trend across imports. |
| 4.3 | **List Imports Tool** | ISSO | `compliance_list_imports` tool with filtering by system, date range, benchmark. |
| 4.4 | **Get Import Summary Tool** | SCA | `compliance_get_import_summary` tool returning detailed breakdown of a specific import with finding-level details. |
| 4.5 | **Unmatched Rules Report** | ISSO | List STIG rules from imported files that don't match any `StigControl` in the curated library. Helps identify gaps in STIG coverage. |

---

## Part 6: Mapping Pipeline Architecture

### Import Flow (CKL)

```
┌──────────────┐     ┌──────────────┐     ┌──────────────────┐     ┌──────────────────┐
│  CKL File    │────▶│  CKL Parser  │────▶│  STIG Resolver   │────▶│  CCI→NIST Mapper │
│  (XML)       │     │  (extract    │     │  (match VulnId/  │     │  (CCI refs →     │
│              │     │   VULNs)     │     │   RuleId to      │     │   NIST controls) │
└──────────────┘     └──────────────┘     │   StigControl)   │     └────────┬─────────┘
                                          └──────────────────┘              │
                                                                            ▼
┌──────────────────┐     ┌──────────────────┐     ┌──────────────────┐     ┌──────────────────┐
│  Import Record   │◀────│  Evidence Creator │◀────│  Effectiveness   │◀────│  Finding Creator  │
│  (history)       │     │  (SHA-256 hash,  │     │  Upsert          │     │  (ComplianceFinding│
│                  │     │   chain of       │     │  (per NIST       │     │   with StigFinding │
│                  │     │   custody)       │     │   control)       │     │   = true)          │
└──────────────────┘     └──────────────────┘     └──────────────────┘     └──────────────────┘
```

### Resolution Logic

1. **Parse** CKL/XCCDF XML → list of `ParsedCklEntry` or `ParsedXccdfResult` DTOs
2. **Resolve STIG** — For each result, lookup `StigControl` by:
   - Primary: `VulnId` (e.g., `V-254239`)
   - Fallback: `RuleId` (e.g., `SV-254239r849090_rule`)
   - Fallback: `StigVersion` (e.g., `WN22-AU-000010`)
   - Log unmatched entries
3. **Resolve NIST controls** — For each matched STIG, use CCI references:
   - CCI → `cci-nist-mapping.json` → NIST 800-53 control IDs
   - Validate each NIST control exists in the system's `ControlBaseline.ControlIds`
   - For controls **in baseline**: create `ComplianceFinding` + `ControlEffectiveness` records
   - For controls **outside baseline**: create `ComplianceFinding` only (audit trail), skip `ControlEffectiveness`, add warning: "{N} NIST controls resolved but not in system baseline: {list}"
4. **Check conflicts** — For each finding, check if a `ComplianceFinding` already exists with the same `StigId` + `AssessmentId`
5. **Apply** — Based on conflict resolution strategy, create/update/skip
6. **Re-evaluate effectiveness** — For each NIST control affected by this import, query **all** current `ComplianceFinding` records (not just those from this import). If any finding for the control is `Open`, determination = `OtherThanSatisfied`. Only when all findings are `NotAFinding`/`Remediated` does the determination become `Satisfied`. This ensures effectiveness reflects the aggregate state across all imports and manual assessments.
7. **Record** — Create `ScanImportRecord` with summary statistics

### Severity Mapping

| CKL/XCCDF Severity | `StigSeverity` | `CatSeverity` | `FindingSeverity` |
|---------------------|----------------|---------------|-------------------|
| `high` | `High` | `CatI` | `High` |
| `medium` | `Medium` | `CatII` | `Medium` |
| `low` | `Low` | `CatIII` | `Low` |

---

## Part 7: Integration Points

### 7.1 Existing Services Used

| Service | How It's Used |
|---------|--------------|
| `IStigKnowledgeService` | Lookup `StigControl` by VulnId/RuleId for resolution |
| `IAssessmentArtifactService` | `AssessControlAsync` — upsert `ControlEffectiveness` from parsed results |
| `IBaselineService` | `GetBaselineAsync` — validate NIST controls are in system's baseline |
| `IRmfLifecycleService` | `GetSystemAsync` — validate system exists and is in appropriate RMF step |
| `IComplianceAssessmentService` | Create/get assessment context for findings |

### 7.2 New Service

| Service | Purpose |
|---------|---------|
| `IScanImportService` | Core import orchestration: parse, resolve, map, create findings, track history |

### 7.3 File Transfer

**Challenge**: MCP tools communicate via JSON-RPC. There is no multipart file upload endpoint on the MCP server.

**Solution**: Accept file content as **base64-encoded string** in the MCP tool parameter. This works because:
- CKL files are typically 100KB–2MB (XML, highly compressible)
- XCCDF results are typically 50KB–500KB
- Base64 encoding adds ~33% overhead, keeping payloads under 3MB
- The MCP JSON-RPC protocol has no hard message size limit in our implementation
- The existing chat attachment endpoint (10MB limit) could be extended for larger files in the future

**File size limit**: 5MB after base64 decoding. Files larger than this are rejected with a helpful error suggesting splitting by benchmark.

### 7.4 Assessment Context

Imported findings need an `AssessmentId` (FK to `ComplianceAssessment`). On import:
1. If `assessment_id` is provided, use it (must be active, not completed)
2. If no assessment exists for the system, create one automatically with `source = "STIG/SCAP Import"`
3. If an active assessment exists, use it (warn if not the most recent)

---

## Part 8: What This Changes

### Entity Model Changes

| Entity | Change | Details |
|--------|--------|---------|
| `ScanImportRecord` | **NEW** | Import history tracking with file metadata, finding counts, status |
| `ScanImportFinding` | **NEW** | Per-finding import detail for audit trail |
| `ComplianceFinding` | **ENRICHED** | New `ImportRecordId` FK (nullable), new `ScanSource` value |
| `ComplianceEvidence` | **ENRICHED** | New `EvidenceType` value: `"StigChecklist"`, `"ScapResult"` |

### Service Layer Changes

| Component | Change |
|-----------|--------|
| `IScanImportService` / `ScanImportService` | **NEW** — CKL/XCCDF parsing, STIG resolution, finding creation, import history |
| `ICklParser` / `CklParser` | **NEW** — CKL XML parsing with XElement/XDocument |
| `IXccdfParser` / `XccdfParser` | **NEW** — XCCDF XML parsing |
| `IStigKnowledgeService` | **EXTENDED** — Add `GetStigControlByRuleIdAsync(string ruleId)` for XCCDF resolution |
| `AtoCopilotContext` | **MODIFIED** — Add `ScanImportRecords`, `ScanImportFindings` DbSets |

### Tool Layer Changes

| Tool | Parent File | Description |
|------|-------------|-------------|
| `ImportCklTool` | `ScanImportTools.cs` | Parse and import .ckl file |
| `ImportXccdfTool` | `ScanImportTools.cs` | Parse and import .xccdf file |
| `ExportCklTool` | `ScanImportTools.cs` | Generate .ckl from assessment data |
| `ListImportsTool` | `ScanImportTools.cs` | List import history for a system |
| `GetImportSummaryTool` | `ScanImportTools.cs` | Detailed import result breakdown |

### MCP Registration

5 new tools registered in `ComplianceMcpTools.cs`.

---

## Part 9: What We're NOT Building

| Out of Scope | Reason |
|-------------|--------|
| **SCAP Scanner** | We import results, we don't run DISA SCC |
| **ACAS/Nessus .nessus import** | Different format, different parser — separate feature |
| **OVAL Definition parsing** | XCCDF results reference OVAL but we don't parse the OVAL XML itself |
| **Real-time scan triggering** | No API to invoke SCAP SCC remotely |
| **Streaming file upload** | MCP protocol uses JSON-RPC; multipart upload is a future enhancement |
| **CKL editing UI** | We're an import/export pipeline, not a STIG Viewer replacement |
| **Automatic STIG library expansion from CKL** | Unmatched rules are logged, not auto-added to the curated library |
| **Batch import of ZIP archives** | Single file per tool call; batch via multiple calls |

---

## Part 10: Success Criteria

| Criterion | Measurement |
|-----------|-------------|
| CKL import parses all VULN entries correctly | 100% of STIG Viewer-exported CKL files parse without error (tested with 5+ benchmark types) |
| XCCDF import parses all rule-result entries correctly | 100% of SCAP SCC-exported XCCDF files parse without error |
| STIG→CCI→NIST mapping resolves correctly | ≥95% of CKL findings with CCI references map to valid NIST controls |
| Findings created with correct severity and status | All CKL STATUS values correctly mapped to ComplianceFinding status |
| ControlEffectiveness records created per NIST control | One effectiveness record per unique NIST control from resolved findings |
| Evidence chain integrity | Imported file content hashed with SHA-256; hash verifiable post-import |
| Conflict resolution works correctly | Skip/Overwrite/Merge strategies produce expected results |
| Dry-run shows accurate preview | Dry-run output matches actual import results when run non-dry |
| CKL export produces valid XML | Exported CKL files re-importable by DISA STIG Viewer |
| Import history queryable | All imports tracked with searchable metadata |
| Performance: CKL import < 10s for 500 VULNs | Single CKL file with ~500 VULN entries processed in < 10 seconds |
| Performance: XCCDF import < 5s for 500 rule-results | XCCDF with ~500 rules processed in < 5 seconds |

---

## Part 11: Build Sequence & Rationale

### Why This Order?

1. **Phase 1 (CKL Import) first** because CKL is the most common format. Every DoD team uses STIG Viewer, and CKL files are the universal exchange format for STIG assessment results.

2. **Phase 2 (XCCDF Import) second** because XCCDF extends the same pipeline with a different parser. The STIG resolution and NIST mapping logic from Phase 1 is reused.

3. **Phase 3 (CKL Export) third** because export completes the round-trip. Teams need to upload CKL files to eMASS after updating remediation status in Security Posture Intelligence Navigator.

4. **Phase 4 (Management) last** because import history and summary tools are observability features built on top of working import functionality.

### Timeline Estimate

| Phase | Scope | Estimate |
|-------|-------|----------|
| Phase 1 | CKL parser, STIG resolver, finding/evidence creation, conflict resolution, MCP tool | 3–4 days |
| Phase 2 | XCCDF parser, rule-result mapping, MCP tool | 1–2 days |
| Phase 3 | CKL generator, benchmark selection, MCP tool | 1–2 days |
| Phase 4 | Import history entity, list/summary tools, unmatched rules report | 1–2 days |
| **Total** | **5 tools, 2 parsers, 2 new entities, 1 new service** | **6–10 days** |
