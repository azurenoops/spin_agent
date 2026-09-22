# Feature Specification: ACAS/Nessus Scan Import

**Feature Branch**: `026-acas-nessus-import`  
**Created**: 2025-03-12  
**Status**: Draft  
**Input**: User description: "Import vulnerability scan results from ACAS .nessus files. Teams export from ACAS and import to Security Posture Intelligence Navigator can automatically map to controls."

## Clarifications

### Session 2025-03-12

- Q: What uniquely identifies an ACAS finding for conflict resolution purposes? → A: Plugin ID + Hostname + Port (one finding per plugin per host per port)
- Q: Who should be authorized to import .nessus files? → A: ISSO + SCA + System Admin only
- Q: Which ACAS finding severities should generate POA&M weakness entries? → A: Critical + High + Medium (CAT I + CAT II)
- Q: Should the plugin-family-to-control-family heuristic mapping table be curated (fixed) or user-extensible? → A: Curated (fixed) — ship a built-in mapping table, extend in future releases
- Q: Should informational (severity 0) plugins be persisted as findings in the database? → A: Exclude from findings, include in summary counts only (lean, compliance-focused)

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Import ACAS .nessus File (Priority: P1)

An ISSO, SCA, or System Admin exports a .nessus vulnerability scan file from ACAS (Assured Compliance Assessment Solution) and imports it into Security Posture Intelligence Navigator for a registered system. The system parses the XML, extracts all vulnerability findings (plugins), and persists them as compliance findings linked to the target system.

**Why this priority**: This is the foundational capability — without file parsing and finding creation, no downstream features (control mapping, reporting) can function. It delivers immediate value by centralizing vulnerability data that currently lives in disconnected ACAS exports.

**Independent Test**: Can be fully tested by uploading a sample .nessus file for a registered system and verifying that findings appear with correct plugin IDs, severities, CVEs, and host information.

**Acceptance Scenarios**:

1. **Given** a registered system exists and the user has an exported .nessus file from ACAS, **When** the user imports the file via the MCP tool, **Then** the system parses all `<ReportHost>` entries and creates a `ScanImportRecord` with status `Completed` and individual findings for each plugin result.
2. **Given** a .nessus file containing multiple hosts, **When** the file is imported, **Then** findings are created for each host-plugin combination with the correct hostname, IP address, and port information preserved.
3. **Given** a .nessus file with plugins of varying severity (Critical, High, Medium, Low, Informational), **When** imported, **Then** each non-informational finding reflects the correct severity mapped to the existing CAT severity scale (Critical/High → CAT I, Medium → CAT II, Low → CAT III). Informational plugins (severity 0) are counted in the import summary but not persisted as individual findings.
4. **Given** an invalid or malformed .nessus file, **When** the user attempts import, **Then** the system returns a clear error message indicating the parse failure and no partial data is persisted.
5. **Given** the same .nessus file is imported a second time for the same system, **When** using "Skip" conflict resolution, **Then** duplicate findings are skipped and the import record reflects the skip count.

---

### User Story 2 — Automatic Control Mapping (Priority: P1)

After importing a .nessus file, Security Posture Intelligence Navigator automatically maps each vulnerability finding to the applicable NIST 800-53 controls. This mapping uses the plugin's CVE references to resolve through CCI (Control Correlation Identifier) crosswalks and the existing STIG-to-CCI-to-NIST chain, as well as direct plugin-family-to-control-family heuristics for findings without CVE cross-references.

**Why this priority**: Automatic control mapping is the core differentiator — without it, teams must manually correlate thousands of vulnerability findings to controls, which is the primary pain point this feature solves.

**Independent Test**: Can be tested by importing a .nessus file containing plugins with known CVEs and verifying that the resulting findings are linked to the correct NIST 800-53 controls.

**Acceptance Scenarios**:

1. **Given** a .nessus plugin result includes CVE references that map to known CCI entries, **When** the import completes, **Then** the finding's resolved NIST control IDs contain the correct NIST 800-53 control IDs derived from the CVE → CCI → NIST chain.
2. **Given** a vulnerability plugin has no CVE references but belongs to a known plugin family (e.g., "Windows: Microsoft Bulletins"), **When** imported, **Then** the system applies plugin-family-to-control-family heuristic mapping and flags the mapping confidence as "Heuristic" rather than "Definitive".
3. **Given** a plugin has CVE references that do not resolve to any known CCI entry, **When** imported, **Then** the finding is created with an empty control mapping and a warning is logged indicating the unresolved CVEs.
4. **Given** the system has a control baseline assigned, **When** findings are mapped to controls, **Then** only controls within the baseline generate `ControlEffectiveness` records; out-of-baseline control mappings are preserved on the finding but do not create effectiveness records.

---

### User Story 3 — Import Summary and Dry-Run Preview (Priority: P2)

Before committing an import, the user can perform a dry-run to preview what the import will produce — how many findings will be created, updated, or skipped, and which controls will be affected. After a committed import, the user receives a comprehensive summary.

**Why this priority**: Dry-run capability builds user confidence and prevents unintended data changes, especially for large .nessus files with thousands of plugins. Essential for operational trust but not required for core data flow.

**Independent Test**: Can be tested by running a dry-run import and verifying the preview report matches a subsequent committed import.

**Acceptance Scenarios**:

1. **Given** a user uploads a .nessus file with dry-run mode enabled, **When** the import completes, **Then** the system returns a full preview (findings count by severity, control mappings, host inventory) without persisting any data.
2. **Given** a committed import completes, **When** the user views the import record, **Then** the summary includes: total plugins processed, findings created, findings updated, findings skipped, unresolved mappings, hosts scanned, and NIST controls affected.
3. **Given** a .nessus file with 5,000+ plugins, **When** a dry-run is performed, **Then** the preview completes and returns results without timeout or excessive resource consumption.

---

### User Story 4 — Import History and Re-Import Management (Priority: P2)

Users can view the history of all ACAS/Nessus imports for a system, compare results across scan dates, and re-import updated scans with configurable conflict resolution (skip, overwrite, or merge).

**Why this priority**: Import management enables ongoing vulnerability tracking across assessment cycles. Important for continuous monitoring but not required for initial import functionality.

**Independent Test**: Can be tested by importing two .nessus files for the same system on different dates and verifying the import history shows both, with correct finding counts and the ability to re-import with overwrite.

**Acceptance Scenarios**:

1. **Given** multiple .nessus files have been imported for a system over time, **When** the user queries import history, **Then** each import record is listed with date, file name, host count, finding counts, and status.
2. **Given** a new ACAS scan is performed and the user imports the updated .nessus file with "Overwrite" conflict resolution, **When** the import processes a plugin-host combination that already exists, **Then** the existing finding is updated with the new scan data and the import record reflects the update count.
3. **Given** a user imports with "Merge" conflict resolution, **When** a finding already exists for the same plugin-host, **Then** the system keeps the more-recent status and appends any new details or comments without losing historical data.

---

### User Story 5 — POA&M and Weakness Source Integration (Priority: P3)

Open vulnerability findings from ACAS imports automatically populate as weaknesses in the Plan of Action & Milestones (POA&M) workflow with `WeaknessSource` set to "ACAS". This enables teams to track remediation of ACAS-identified vulnerabilities through the existing POA&M lifecycle.

**Why this priority**: POA&M integration provides downstream value from imported scan data but depends on existing POA&M functionality and the core import pipeline being in place first.

**Independent Test**: Can be tested by importing a .nessus file with open Critical/High/Medium findings and verifying that corresponding POA&M weakness entries are created with source "ACAS".

**Acceptance Scenarios**:

1. **Given** ACAS findings with severity Critical, High, or Medium are imported, **When** the import completes, **Then** corresponding weakness entries are created (or updated) in the POA&M with `WeaknessSource` = "ACAS" and the associated NIST control ID from the mapping.
2. **Given** a previously open ACAS finding is now resolved in a subsequent scan import, **When** the updated .nessus file is imported, **Then** the corresponding POA&M weakness entry is flagged for review/closure.

---

### Edge Cases

- What happens when a .nessus file contains hosts that are not associated with the registered system? Findings are imported and linked to the system; host identity mismatches are logged as warnings.
- How does the system handle a .nessus file exported from a non-ACAS Tenable Nessus scanner? The same parser applies — the .nessus XML schema is identical regardless of whether the source is ACAS or commercial Nessus.
- What happens when a plugin has a severity of "0" (Informational)? Informational plugins are excluded from finding persistence but are counted in the import summary totals. Host-level metadata from informational plugins (e.g., OS detection, service enumeration) is captured in the host properties.
- How does the system handle a .nessus file with no `<ReportHost>` entries? The import completes with status `CompletedWithWarnings` and a warning that no hosts were found.
- What happens when the .nessus file exceeds the maximum allowed size? The import is rejected before parsing with a clear error message specifying the size limit.
- How are plugins with CVSS scores but no CVE references handled? The CVSS score is preserved on the finding; control mapping falls back to plugin-family heuristics.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST parse standard Tenable .nessus XML files (NessusClientData_v2 schema) and extract all `<ReportHost>` blocks and their `<ReportItem>` entries.
- **FR-002**: System MUST extract the following fields from each plugin result: plugin ID, plugin name, plugin family, severity (0–4), risk factor, CVE references, CVSS base score, synopsis, description, solution, plugin output, port, protocol, and service name.
- **FR-003**: System MUST extract host-level properties from each `<ReportHost>`: hostname, IP address, operating system, MAC address, scan start time, and scan end time.
- **FR-004**: System MUST map Nessus severity levels to the existing CAT severity scale: severity 4 (Critical) → CAT I, severity 3 (High) → CAT I, severity 2 (Medium) → CAT II, severity 1 (Low) → CAT III. Severity 0 (Informational) plugins are excluded from finding persistence and counted in the import summary only.
- **FR-005**: System MUST automatically map vulnerability findings to NIST 800-53 controls using STIG-ID cross-references from `<xref>` elements resolved through the existing CCI crosswalk chain, supplemented by plugin-family heuristic mapping when STIG-ID xrefs are unavailable.
- **FR-006**: System MUST provide fallback control mapping via a curated, built-in plugin-family-to-control-family heuristic table when STIG-ID xref mapping is not available, and flag such mappings with a confidence indicator of "Heuristic" rather than "Definitive". The mapping table is not user-editable in this release.
- **FR-007**: System MUST support three conflict resolution strategies for re-imports: Skip (keep existing), Overwrite (replace with new), and Merge (keep more-recent, append details). A finding is uniquely identified by the composite key of Plugin ID + Hostname + Port.
- **FR-008**: System MUST support dry-run mode that produces a full import preview without persisting any data.
- **FR-009**: System MUST compute a SHA-256 hash of the uploaded file to detect duplicate imports and provide a warning when a previously imported file is re-submitted.
- **FR-010**: System MUST enforce a maximum file size limit for uploaded .nessus files, consistent with the existing scan import size constraints.
- **FR-011**: System MUST create a `ScanImportRecord` for each import operation with accurate counts of findings created, updated, skipped, and errored.
- **FR-012**: System MUST create individual `ScanImportFinding` records for each plugin-host-port combination to maintain an audit trail.
- **FR-013**: System MUST create or update `ControlEffectiveness` records for in-baseline NIST controls affected by the imported findings.
- **FR-014**: System MUST generate POA&M weakness entries for open Critical/High/Medium findings with `WeaknessSource` set to "ACAS".
- **FR-015**: System MUST log all import operations including success, failure, warnings, and unresolved control mappings for audit purposes.
- **FR-016**: System MUST validate that the target system exists and is a registered system before processing the import.
- **FR-017**: System MUST preserve the scan timestamp from the .nessus file's `HOST_START` and `HOST_END` properties for temporal tracking across assessment cycles.
- **FR-018**: System MUST restrict .nessus import operations to users with ISSO, SCA, or System Admin roles. Unauthorized users MUST receive an access denied error.

### Key Entities

- **NessusReportHost**: Represents a scanned host extracted from a .nessus file. Contains hostname, IP address, OS, MAC address, and scan time window. One .nessus file can contain multiple hosts.
- **NessusPluginResult**: Represents a single vulnerability finding for a host-port combination. Contains plugin metadata (ID, name, family, severity), CVE references, CVSS scores, remediation guidance, and port/protocol information. Uniquely identified by Plugin ID + Hostname + Port. Many plugin results belong to one host.
- **ScanImportRecord** (existing): Extended with a new import type value to track .nessus file imports using the same audit infrastructure as CKL/XCCDF/Prisma imports.
- **ScanImportFinding** (existing): Stores per-finding audit trail. For Nessus imports, the vulnerability ID field stores the plugin ID and the rule ID field stores the primary CVE (if available).
- **ControlEffectiveness** (existing): Updated when imported findings map to in-baseline NIST controls, reflecting the vulnerability impact on control effectiveness.
- **POA&M Weakness** (existing): Created for open Critical/High/Medium findings with `WeaknessSource` = "ACAS" and the mapped NIST control ID.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can import a standard .nessus file and see all vulnerability findings appear in the system within 60 seconds for files containing up to 10,000 plugin results.
- **SC-002**: At least 80% of vulnerability findings with CVE references are automatically mapped to the correct NIST 800-53 controls without manual intervention.
- **SC-003**: Users can perform a dry-run preview of any .nessus file import and receive a complete summary before committing changes.
- **SC-004**: 100% of Critical, High, and Medium severity findings from ACAS imports generate corresponding POA&M weakness entries, eliminating manual transcription of ACAS findings into the POA&M.
- **SC-005**: Teams can track vulnerability trends across consecutive ACAS scans by comparing import summaries over time for the same system.
- **SC-006**: Zero data loss during re-imports — conflict resolution strategies preserve or update findings according to the selected strategy without orphaning records.
- **SC-007**: Reduce the time for teams to correlate ACAS scan results to NIST 800-53 controls from hours of manual work to under 5 minutes per scan file.

## Assumptions

- Teams have access to export .nessus files from ACAS in the standard NessusClientData_v2 XML format. Security Posture Intelligence Navigator does not connect to ACAS directly.
- The existing CCI crosswalk data (used for CKL/XCCDF imports) includes sufficient CVE-to-CCI mappings to resolve the majority of common vulnerability CVEs. Where gaps exist, the system falls back to heuristic mapping.
- Plugin-family-to-control-family heuristic mappings follow established DISA patterns (e.g., "Windows: Microsoft Bulletins" → SI-2 Flaw Remediation, "Firewalls" → SC-7 Boundary Protection).
- File size limits and base64 encoding constraints are consistent with the existing scan import infrastructure.
- ACAS exports use the same .nessus XML schema as commercial Tenable Nessus — no ACAS-specific XML extensions need to be handled differently.
- Informational (severity 0) plugins are excluded from finding persistence to reduce storage bloat; their counts are tracked in the import summary and host-level metadata (OS, hostname) is captured on the host record.

## Scope Boundaries

| In Scope                                              | Out of Scope                                |
|-------------------------------------------------------|---------------------------------------------|
| Parsing .nessus XML (NessusClientData_v2)             | Direct ACAS/Tenable API integration         |
| Automatic CVE → CCI → NIST control mapping            | Running or scheduling ACAS scans            |
| Plugin-family heuristic control mapping               | ACAS credential management                  |
| Import history and re-import with conflict resolution | Real-time vulnerability feed subscriptions  |
| Dry-run preview mode                                  | Nessus policy/template management           |
| POA&M weakness generation for open findings           | Automated remediation or patching           |
| Multi-host file support                               | Network topology discovery from scan data   |
| Severity mapping to CAT scale                         | Custom plugin development or management     |
| Curated plugin-family heuristic mapping table          | User-editable heuristic mapping configuration |

## Documentation Updates Required

The following existing documentation pages must be updated to cover ACAS/Nessus scan import workflows:

| Document | Update Required |
|----------|----------------|
| **ISSO Guide** (`docs/guides/issm-guide.md` — ISSO workflow section) | Add "Import ACAS/Nessus Scan Results" workflow: step-by-step for exporting .nessus from ACAS → `compliance_import_nessus` → review import summary → resolve unmapped findings → track remediation via POA&M |
| **SCA Guide** (`docs/guides/sca-guide.md`) | Add "Assess Controls Using ACAS Scan Data" section: how imported vulnerability findings appear in `ControlEffectiveness`, using import history for trend analysis, combined STIG + ACAS evidence review |
| **Engineer Guide** (`docs/guides/engineer-guide.md`) | Add "ACAS Vulnerability Remediation Workflow" section: viewing ACAS-sourced findings with remediation guidance (plugin solution text), port-specific findings, CVE details, and tracking fix verification via re-import |
| **ISSM Guide** (`docs/guides/issm-guide.md`) | Add "Vulnerability Scan Oversight" section: directing ISSOs to import ACAS scans, reviewing scan trends across systems and scan dates, ACAS findings in ConMon reports, POA&M weakness tracking |
| **AO Quick Reference** (`docs/guides/ao-quick-reference.md`) | Add "ACAS Scan Import" entry: overview of how ACAS vulnerability data flows into control effectiveness and POA&M, enabling risk-based authorization decisions |
| **Agent Tool Catalog** (`docs/architecture/agent-tool-catalog.md`) | Add entries for new MCP tools: `compliance_import_nessus`, `compliance_list_nessus_imports` with parameters, descriptions, and example invocations |
| **Data Model** (`docs/architecture/data-model.md`) | Add `NessusXml` to the `ScanImportType` enum documentation, document Nessus-specific fields on `ScanImportFinding` (plugin ID, plugin family, CVE references, CVSS score, port/protocol), and add the plugin-family heuristic mapping table |
| **MCP Server API** (`docs/api/mcp-server.md`) | Document the `compliance_import_nessus` and `compliance_list_nessus_imports` MCP tool APIs with parameter schemas, response format, error codes, and usage examples |
| **RMF Assess Phase** (`docs/rmf-phases/assess.md`) | Add ACAS/Nessus scan import as an assessment input alongside STIG/SCAP and Prisma Cloud imports |
| **RMF Monitor Phase** (`docs/rmf-phases/monitor.md`) | Add ACAS periodic re-import as a ConMon data source, vulnerability trend analysis for drift detection between scan cycles |
| **Tool Inventory** (`docs/reference/tool-inventory.md`) | Add new Nessus import tools with parameters, RBAC roles (ISSO, SCA, System Admin), and example invocations |
| **STIG Coverage** (`docs/reference/stig-coverage.md`) | Note that ACAS/Nessus findings supplement STIG coverage via CVE → CCI → NIST mapping and plugin-family heuristics |
| **Getting Started — ISSO** (`docs/getting-started/isso.md`) | Add ACAS import as a quickstart step for ISSOs setting up a new system |
| **Getting Started — SCA** (`docs/getting-started/sca.md`) | Add ACAS import as a quickstart step for SCAs performing initial assessments |
| **Glossary** (`docs/reference/glossary.md`) | Add entries for: ACAS (Assured Compliance Assessment Solution), Nessus Plugin, Plugin Family, CVSS (Common Vulnerability Scoring System) |
| **Persona Test Cases — Tool Validation** (`docs/persona-test-cases/tool-validation.md`) | Add `compliance_import_nessus` and `compliance_list_nessus_imports` to the Tool Validation Matrix under the Assessment section with corresponding persona TC-IDs |
| **Persona Test Cases — Test Data Setup** (`docs/persona-test-cases/test-data-setup.md`) | Add test data constants for ACAS import: sample .nessus file path, expected plugin count, expected host count, ACAS-specific system constants; add a `Sample Nessus Scan` entry (`.nessus` format) to the T006 Test Data Files table |
| **Persona Test Cases — Results Template** (`docs/persona-test-cases/results-template.md`) | Add ACAS import test rows for ISSO (import .nessus, review findings, verify POA&M), SCA (verify control mapping, assess effectiveness), ISSM (review scan trends, direct imports), and RBAC denial rows for unauthorized personas (AO, Engineer) |
| **Persona Test Cases — Environment Checklist** (`docs/persona-test-cases/environment-checklist.md`) | Add pre-requisite check for sample .nessus file availability, verify `compliance_import_nessus` tool registration, and confirm plugin-family heuristic mapping table is loaded |
| **ISSO Test Script** (`docs/persona-test-cases/scripts/isso-test-script.md`) | Add ACAS import test steps: import .nessus file via `compliance_import_nessus`, verify findings appear with correct severities and control mappings, review import summary, verify POA&M weakness entries created with source "ACAS", perform dry-run preview, re-import with updated scan |
| **SCA Test Script** (`docs/persona-test-cases/scripts/sca-test-script.md`) | Add ACAS assessment test steps: verify imported ACAS findings appear in ControlEffectiveness for baseline controls, validate CVE → CCI → NIST mapping accuracy, review heuristic vs. definitive mapping confidence, compare ACAS findings against STIG assessment results |
| **ISSM Test Script** (`docs/persona-test-cases/scripts/issm-test-script.md`) | Add vulnerability oversight test steps: direct ISSO to import ACAS scan, review import history across scan dates, verify ACAS findings in ConMon posture, review POA&M completeness for ACAS-sourced weaknesses |
| **Engineer Test Script** (`docs/persona-test-cases/scripts/engineer-test-script.md`) | Add RBAC denial test step: attempt `compliance_import_nessus` as Engineer role and verify access denied (403). Add remediation workflow: view ACAS findings with solution text, track fix verification via re-import |
| **AO Test Script** (`docs/persona-test-cases/scripts/ao-test-script.md`) | Add RBAC denial test step: attempt `compliance_import_nessus` as AO role and verify access denied (403). Add review step: view ACAS vulnerability summary as input to authorization decision |
| **Cross-Persona Test Script** (`docs/persona-test-cases/scripts/cross-persona-test-script.md`) | Add ACAS scan lifecycle scenario: ISSM directs import → ISSO imports .nessus → SCA validates control mapping → Engineer views remediation guidance → ISSO re-imports after remediation → SCA verifies improved posture → AO reviews updated risk |
| **Unified RMF Test Script** (`docs/persona-test-cases/scripts/unified-rmf-test-script.md`) | Add ACAS import steps to the Assess and Monitor phases: import .nessus during assessment, verify findings feed into control effectiveness, re-import during continuous monitoring to track remediation progress |

## Test Cases

### Unit Tests

| ID | Test Name | Description | Requirement |
|----|-----------|-------------|-------------|
| UT-001 | Parse valid .nessus file | Parse a well-formed NessusClientData_v2 XML file and verify all `ReportHost` and `ReportItem` entries are extracted correctly | FR-001 |
| UT-002 | Extract plugin fields | Verify plugin ID, name, family, severity, risk factor, CVE refs, CVSS score, synopsis, description, solution, plugin output, port, protocol, and service name are extracted from a `ReportItem` | FR-002 |
| UT-003 | Extract host properties | Verify hostname, IP address, OS, MAC address, HOST_START, and HOST_END are extracted from `ReportHost` tag properties | FR-003, FR-017 |
| UT-004 | Severity mapping — Critical | Nessus severity 4 maps to CAT I | FR-004 |
| UT-005 | Severity mapping — High | Nessus severity 3 maps to CAT I | FR-004 |
| UT-006 | Severity mapping — Medium | Nessus severity 2 maps to CAT II | FR-004 |
| UT-007 | Severity mapping — Low | Nessus severity 1 maps to CAT III | FR-004 |
| UT-008 | Severity mapping — Informational excluded | Nessus severity 0 plugins are counted but not persisted as findings | FR-004 |
| UT-009 | STIG-ID xref to NIST mapping | Plugin with known STIG-ID xref resolves to correct NIST 800-53 controls via STIG-ID  CCI  NIST chain | FR-005 |
| UT-010 | Plugin family heuristic mapping | Plugin without STIG-ID xref but with known family maps to expected NIST control family with "Heuristic" confidence | FR-006 |
| UT-011 | Heuristic confidence flag | Heuristic-mapped findings are flagged as "Heuristic" confidence, not "Definitive" | FR-006 |
| UT-012 | Unresolved mapping warning | Plugin with no STIG-ID xref and no heuristic match produces a warning and empty control mapping | FR-005 |
| UT-013 | SHA-256 hash computation | File hash is computed correctly and matches expected value for a known input | FR-009 |
| UT-014 | Finding identity key — dedup | Two plugins with same Plugin ID + Hostname + Port are treated as duplicates; different ports are treated as distinct | FR-007 |
| UT-015 | Conflict resolution — Skip | Re-import with Skip strategy keeps existing finding unchanged and increments skip count | FR-007 |
| UT-016 | Conflict resolution — Overwrite | Re-import with Overwrite strategy replaces existing finding data | FR-007 |
| UT-017 | Conflict resolution — Merge | Re-import with Merge strategy keeps more-recent status and appends new details | FR-007 |
| UT-018 | Malformed XML rejection | Invalid XML input throws a parse error with descriptive message and no partial data | FR-001 |
| UT-019 | Empty ReportHost handling | .nessus file with no `<ReportHost>` entries returns CompletedWithWarnings with appropriate warning | Edge Case |
| UT-020 | Multi-host extraction | .nessus file with 3 hosts produces findings linked to correct host metadata for each | FR-001, FR-003 |
| UT-021 | Plugin family mapping table completeness | All entries in the curated heuristic mapping table resolve to valid NIST 800-53 control families | FR-006 |
| UT-022 | POA&M threshold — Critical | Critical severity finding generates POA&M weakness with source "ACAS" | FR-014 |
| UT-023 | POA&M threshold — High | High severity finding generates POA&M weakness with source "ACAS" | FR-014 |
| UT-024 | POA&M threshold — Medium | Medium severity finding generates POA&M weakness with source "ACAS" | FR-014 |
| UT-025 | POA&M threshold — Low excluded | Low severity finding does NOT generate a POA&M weakness | FR-014 |
| UT-026 | Informational excluded from POA&M | Informational plugins do not generate POA&M weaknesses | FR-014 |

### Integration Tests

| ID | Test Name | Description | Requirement |
|----|-----------|-------------|-------------|
| IT-001 | End-to-end import — single host | Import a .nessus file with one host and verify ScanImportRecord, ScanImportFindings, ControlEffectiveness, and POA&M weakness records are created correctly | FR-001 through FR-014 |
| IT-002 | End-to-end import — multi-host | Import a .nessus file with multiple hosts and verify findings are created for each host-plugin-port combination | FR-001, FR-003, FR-012 |
| IT-003 | Dry-run mode | Import with dry_run=true and verify preview counts match but no records are persisted in the database | FR-008 |
| IT-004 | Duplicate file detection | Import the same .nessus file twice and verify SHA-256 duplicate warning is returned on second import | FR-009 |
| IT-005 | Re-import with Skip | Import, then re-import same file with Skip resolution; verify existing findings are unchanged and skip count is accurate | FR-007 |
| IT-006 | Re-import with Overwrite | Import, modify host finding status, re-import with Overwrite; verify findings are replaced with new data | FR-007 |
| IT-007 | Re-import with Merge | Import, re-import with additional details using Merge; verify combined data without loss | FR-007 |
| IT-008 | Control mapping — CVE chain | Import file with plugins containing known CVEs; verify correct NIST controls via CCI crosswalk | FR-005, FR-013 |
| IT-009 | Control mapping — heuristic fallback | Import file with plugins lacking CVEs but having known families; verify heuristic mapping applied with "Heuristic" confidence | FR-006 |
| IT-010 | Baseline-scoped effectiveness | Import for system with assigned baseline; verify ControlEffectiveness only created for in-baseline controls | FR-013 |
| IT-011 | POA&M weakness creation | Import file with Critical/High/Medium findings; verify POA&M weakness entries created with WeaknessSource "ACAS" | FR-014 |
| IT-012 | POA&M weakness closure signal | Import with previously Open finding now resolved; verify corresponding POA&M entry flagged for closure | FR-014 |
| IT-013 | Import history query | Import 3 .nessus files for same system; query import history and verify all 3 records with correct metadata | FR-011 |
| IT-014 | System validation — nonexistent system | Attempt import for a system ID that does not exist; verify error returned and no data persisted | FR-016 |
| IT-015 | File size limit enforcement | Attempt import of a .nessus file exceeding the size limit; verify rejection before parsing | FR-010 |
| IT-016 | RBAC — authorized roles | Verify ISSO, SCA, and System Admin can perform imports successfully | FR-018 |
| IT-017 | RBAC — unauthorized roles | Verify AO, ISSM, Engineer roles receive access denied when attempting import | FR-018 |
| IT-018 | Large file performance | Import a .nessus file with 10,000+ plugin results and verify completion within 60 seconds | SC-001 |
| IT-019 | Audit logging | Import a file and verify all import operations (success, warnings, unresolved mappings) are logged | FR-015 |
| IT-020 | Scan timestamp preservation | Import file and verify HOST_START and HOST_END timestamps are stored on the import record for temporal tracking | FR-017 |
