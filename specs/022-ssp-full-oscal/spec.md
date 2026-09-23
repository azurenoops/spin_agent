# Feature 022: SSP 800-18 Full Sections + OSCAL Output

**Created**: 2026-03-09
**Status**: Strategic Plan
**Phase**: 3 — Compliance
**Purpose**: Complete the System Security Plan (SSP) by implementing all NIST SP 800-18 Rev 1 required sections, producing OSCAL-compliant SSP JSON export with full system-characteristics and back-matter, and enabling structured SSP section management with per-section authoring, review, and completeness tracking.

---

## Clarifications

### Session 2026-03-09

- Q: How many NIST 800-18 sections does the SSP currently cover? → A: Five of the required sections. `SspService.GenerateSspAsync()` currently generates: §1 System Information, §2 Security Categorization, §3 Control Baseline, §4 Control Implementations, and §10 System Interconnections (added in Feature 021). Sections §5–§9, §11–§13 are not implemented.
- Q: What are the missing NIST 800-18 sections? → A: §5 System Environment & Special Considerations (operating environment, physical location, hardware/software inventory), §6 System Interconnections & Information Sharing (expanded detail beyond §10 table — data sharing agreements, information exchange rules), §7 Applicable Laws & Regulations (legal/regulatory requirements applicable to the system), §8 Minimum Security Controls (tailored baseline with justification for deviations — already partially covered in §3 and §4 but missing the tailoring rationale section), §9 Completion Date & System Operational Status (system lifecycle status, milestone dates, operational status), §11 Authorization Boundary Description (narrative & diagram reference for all resources in scope), §12 Personnel Security (screening requirements, access agreements, training requirements), §13 Contingency Planning (contingency plan reference, backup/recovery procedures, alternate processing sites).
- Q: Should we generate OSCAL SSP JSON from scratch or improve the existing output? → A: Improve and extend the existing `EmassExportService.BuildOscalSsp()`. The current output has metadata + system-characteristics + control-implementation but is missing `import-profile`, `system-implementation` (components, inventory-items, users), and `back-matter` (references, resource links). The goal is OSCAL 1.1.2 compliance so the output validates against the official OSCAL SSP JSON schema.
- Q: Should the SSP support incremental section authoring or only full generation? → A: Both. Add a `SspSection` entity that allows authors to write, review, and approve individual SSP sections independently. Full SSP generation assembles all approved sections plus auto-generated sections. This gives teams visibility into what's complete vs. incomplete.
- Q: How should the OSCAL version target be handled? → A: Target OSCAL 1.1.2 (latest stable release). The `oscal-version` field in metadata must read `"1.1.2"`. The JSON output must conform to the NIST OSCAL SSP JSON Schema. We do NOT need to support OSCAL XML or YAML — JSON only.
- Q: Should section content be free-form markdown or structured fields? → A: Hybrid. Auto-generated sections (§1, §2, §3, §4, §7, §9, §10, §11) pull from existing entities and produce structured output. Authored sections (§5, §8, §12, §13) store free-form markdown with metadata (author, reviewed-by, status). §6 is a hybrid — auto-populated from hosting environment data but with editable narrative context.
- Q: Should we validate the OSCAL output against the schema? → A: Yes. Add a validation step that checks the generated JSON against known required fields. We don't need to bundle the full JSON Schema at runtime — instead, validate structurally: required top-level keys present, UUIDs valid, control-ids match baseline, all referenced component UUIDs resolve. Report validation warnings (not hard failures) so users can fix issues before submission.
- Q: What about the existing OSCAL export in EmassExportService? → A: Refactor the SSP-specific OSCAL generation out of `EmassExportService.BuildOscalSsp()` and into a dedicated `OscalSspExportService`. The eMASS service keeps its `ExportOscalAsync` method but delegates SSP generation to the new service. Assessment Results and POA&M OSCAL exports stay in `EmassExportService` unchanged.
- Q: Should the authorization boundary section reference Azure resources or be narrative-only? → A: Both. Auto-generate a resource inventory table from `AuthorizationBoundary` entities (already in the DB — resource ID, type, name, inheritance provider). Add a narrative description field for the human-authored boundary justification. The OSCAL output maps boundary resources to `system-implementation.components` and `inventory-items`.
- Q: How should contingency planning (§13) be handled given we don't have a contingency plan service? → A: §13 is an authored section — the ISSO/ISSM writes a narrative referencing their external contingency plan document. We store the reference (document title, location, last-tested date) as structured metadata plus the narrative. This follows the same pattern as ISA/MOU tracking in Feature 021 — reference, not content.

### Session 2026-03-10

- Q: How should OSCAL baseline profile URIs in `import-profile.href` be resolved (Cap 3.3)? → A: Static constant strings per baseline level (Low/Moderate/High), compiled into the service. No runtime HTTP fetching or configuration file lookup.
- Q: Must SspSection status follow a strict linear progression or can states be skipped (Cap 1.1)? → A: Strict sequential: NotStarted→Draft→UnderReview→Approved. Auto-generated sections start at Draft (skipping NotStarted) since they have content immediately. No forward-skipping allowed.
- Q: Should OSCAL export require all prerequisite data or produce a partial document with warnings (Cap 3.8)? → A: Partial — export whatever data is available, populate empty OSCAL sections with schema-valid placeholders, and return warnings listing gaps. Does not block export for incomplete data.
- Q: Which auto-generated vs. authored section classification is authoritative — Clarification Q6 or Cap 1.2/Part 3? → A: Cap 1.2/Part 3 is authoritative. Auto-generated = §1,§2,§3,§4,§7,§9,§10,§11. Authored = §5,§8,§12,§13. Hybrid = §6. Q6 updated to match.
- Q: Should `WriteSspSectionAsync` use optimistic concurrency checking via the Version field (Cap 1.1/1.2)? → A: Yes — optimistic concurrency. Caller passes expected version; write rejected with conflict error if DB version differs. Standard EF Core concurrency token pattern.

---

## Part 1: The Problem

### Why This Matters

The System Security Plan is the **single most important document** in the ATO package. Every federal system — from a minor application to a national security system — must have a complete SSP before it can receive an Authorization to Operate. NIST SP 800-18 Rev 1 defines 13 required sections that together describe the system's environment, security posture, and risk profile.

Security Posture Intelligence Navigator currently generates **5 of 13 required sections**. An SSP missing 8 sections is incomplete and cannot be submitted for authorization. Assessors (SCAs) will reject the package at first review. This means teams still maintain parallel documents in Word/SharePoint — defeating the purpose of an integrated compliance platform.

Additionally, the federal government is moving toward **OSCAL (Open Security Controls Assessment Language)** as the standard machine-readable format for security artifacts. OMB M-22-09 and the FedRAMP automation initiative require OSCAL-formatted SSPs for automated validation. The current OSCAL SSP output from `EmassExportService.BuildOscalSsp()` produces a minimal JSON document that covers metadata and control implementations but is missing the `system-implementation`, `import-profile`, and `back-matter` sections required by the OSCAL SSP schema. This output will not validate against the NIST OSCAL 1.1.2 schema.

### The Current Gap

| What Teams Must Do | What Security Posture Intelligence Navigator Can Do Today |
|---------------------|-------------------------------|
| Submit complete SSP with all 13 NIST 800-18 sections | Generates 5 of 13 sections (§1, §2, §3, §4, §10) |
| Describe system operating environment (§5) | Nothing — no physical/logical environment model |
| Document applicable laws and regulations (§7) | Nothing — no law/regulation registry |
| Record system operational status and milestones (§9) | `RegisteredSystem.CurrentRmfStep` only — no lifecycle milestones |
| Describe authorization boundary with resource inventory (§11) | `AuthorizationBoundary` entities exist but no SSP section renders them |
| Document personnel security requirements (§12) | `RmfRoleAssignment` captures roles but no screening/training requirements |
| Reference contingency plan and recovery procedures (§13) | Nothing — no contingency plan metadata |
| Export OSCAL-compliant SSP JSON for FedRAMP/eMASS ingest | Minimal OSCAL output — missing `system-implementation`, `import-profile`, `back-matter` |
| Validate OSCAL output before submission | No validation — output may contain structural errors |
| Track SSP section completion status | No per-section tracking — it's all-or-nothing generation |

### The Opportunity

Security Posture Intelligence Navigator already has the foundational data to populate most missing sections:

- **§11 Authorization Boundary**: `AuthorizationBoundary` entities with resource IDs, types, and inheritance providers — just need section rendering and OSCAL mapping
- **§10 Interconnections**: Already implemented in Feature 021 — `SystemInterconnection` + `InterconnectionAgreement` data feeds the section
- **§2 Categorization**: Complete with `SecurityCategorization` + `InformationType` records
- **§4 Controls**: `ControlImplementation` narratives + `ControlInheritance` matrix
- **§1 System Info**: `RegisteredSystem` + `RmfRoleAssignment` for personnel
- **OSCAL infrastructure**: `EmassExportService` already produces JSON with metadata and control-implementation — needs extension, not rebuild

The missing pieces are: **new sections that require authored content** (§5, §7, §9, §12, §13), **section-level lifecycle management** (authoring → review → approval), and **OSCAL schema compliance** (system-implementation, components, inventory-items, back-matter).

---

## Part 2: The Product

### What We're Building

**SSP 800-18 Full Sections + OSCAL Output** completes the System Security Plan by adding all missing NIST SP 800-18 sections with a hybrid authoring model (auto-generated from data + human-authored narratives), introduces structured SSP section management with per-section lifecycle tracking, and produces schema-compliant OSCAL 1.1.2 SSP JSON for FedRAMP and eMASS submission.

### What It Is

- **Complete SSP generation** covering all 13 NIST SP 800-18 Rev 1 required sections
- **Structured section management** — individual SSP sections can be authored, reviewed, and approved independently with status tracking
- **Auto-generated sections** that pull from existing entities (RegisteredSystem, SecurityCategorization, ControlBaseline, ControlImplementation, AuthorizationBoundary, SystemInterconnection, RmfRoleAssignment)
- **Authored sections** with free-form markdown for content that requires human expertise (operating environment, applicable laws, contingency planning, personnel security)
- **OSCAL 1.1.2 SSP JSON export** with full `system-security-plan` structure including `import-profile`, `system-characteristics`, `system-implementation` (components, inventory-items, users), `control-implementation`, and `back-matter`
- **OSCAL structural validation** that checks the generated JSON for required fields, valid UUIDs, control-id consistency, and component cross-references before export
- **SSP completeness dashboard** showing per-section status (Auto/Draft/Reviewed/Approved/Missing) with overall readiness percentage

### What It Is NOT

- Not a word processor — authored sections use markdown, not rich text editing
- Not a PDF/DOCX renderer — this feature produces markdown and OSCAL JSON; PDF/DOCX template rendering is Feature 015 Cap 5.1
- Not an OSCAL XML or YAML generator — JSON only per current federal guidance
- Not a contingency plan generator — §13 stores references and metadata for externally authored contingency plans
- Not an OSCAL assessment-results or POA&M generator — those remain in `EmassExportService`
- Not a schema validator using the actual NIST JSON Schema file — we perform structural validation, not JSON Schema Draft 7 validation

### Interfaces

| Surface | User | Purpose |
|---------|------|---------|
| **MCP Tool** | ISSO, ISSM | `compliance_write_ssp_section` — Author/update a named SSP section |
| **MCP Tool** | ISSM, SCA | `compliance_review_ssp_section` — Approve or request revision of a section |
| **MCP Tool** | ISSO, ISSM | `compliance_ssp_completeness` — Get section-by-section completion status |
| **MCP Tool** | ISSO, ISSM | `compliance_generate_ssp` — Generate full or partial SSP (existing, enhanced) |
| **MCP Tool** | ISSO, ISSM | `compliance_export_oscal_ssp` — Export OSCAL 1.1.2 SSP JSON |
| **MCP Tool** | SCA | `compliance_validate_oscal_ssp` — Validate OSCAL SSP structure before submission |
| **VS Code (@ato)** | ISSO | `@ato What SSP sections are still incomplete?` → runs completeness check |
| **VS Code (@ato)** | ISSM | `@ato Generate a complete SSP for my system` → full document generation |
| **VS Code (@ato)** | ISSM | `@ato Export OSCAL SSP for FedRAMP submission` → OSCAL JSON export |

---

## Part 3: Regulatory Framework

### Governing Authorities

| Authority | Requirement | Relevance |
|-----------|-------------|-----------|
| **NIST SP 800-18 Rev 1** | Defines the SSP structure: 13 required sections covering system description through contingency planning | Primary structural reference — this feature implements all 13 sections |
| **NIST SP 800-37 Rev 2** (RMF) | Step 4 (Implement) requires documenting control implementations in the SSP | SSP generation is a Step 4 deliverable; completeness gates authorization |
| **NIST SP 800-53 Rev 5** | 1,189 security and privacy controls; SSP §4 must document implementation of selected controls | Control narratives already implemented — this feature adds surrounding context |
| **NIST OSCAL 1.1.2** | Open Security Controls Assessment Language — machine-readable SSP format | Target schema for JSON export; required by FedRAMP Automation |
| **FIPS 199** | Security categorization standard | §2 (Security Categorization) references FIPS 199 notation |
| **FIPS 200** | Minimum security requirements for federal information systems | §8 (Minimum Security Controls) references FIPS 200 baseline determination |
| **OMB Circular A-130** | Federal information security and privacy management | Mandates SSP as a core lifecycle document |
| **OMB M-22-09** | Federal Zero Trust Architecture Strategy | Drives OSCAL adoption for automated security assessment |
| **DoDI 8510.01** | DoD RMF for Information Technology | Requires SSP as part of the authorization package |
| **FedRAMP Authorization Act** (2022) | Codifies FedRAMP program; mandates OSCAL for cloud service authorization | OSCAL SSP JSON is the submission format for FedRAMP |
| **CNSSI 1253** | Security categorization and control selection for NSS | Overlay controls documented in §8 tailoring section |

### NIST SP 800-18 Rev 1 — Required SSP Sections

| § | Section Name | Current Status | Feature 022 Action |
|---|-------------|---------------|---------------------|
| 1 | System Name / Title / Unique Identifier | ✅ Implemented | Enhance — add DITPR-ID, eMASS-ID fields |
| 2 | Security Categorization | ✅ Implemented | Enhance — add FIPS 200 minimum security requirements reference |
| 3 | System Owner / Authorizing Official | ✅ Partial (in §1 key personnel) | Restructure — dedicated §3 section with AO, SO, ISSO, ISSM details |
| 4 | Information System Type (Major App / General Support) | ✅ Partial (in §1) | Move system type + mission criticality to dedicated §4 |
| 5 | General Description / Purpose | ✅ Partial (description field) | Enhance — add operational context, users served, mission alignment |
| 6 | System Environment | 🔴 Missing | **New** — physical/logical environment, data center, cloud region |
| 7 | System Interconnections / Information Sharing | ✅ Implemented as §10 | Restructure — move to correct §7 position per 800-18 mapping |
| 8 | Related Laws / Regulations / Policies | 🔴 Missing | **New** — applicable laws, regulations, organizational policies |
| 9 | Minimum Security Controls | ✅ Partial (§3 + §4) | Enhance — add tailoring rationale, compensating controls, overlay justification |
| 10 | Control Implementation Descriptions | ✅ Implemented | No change — per-control narratives |
| 11 | Authorization Boundary | 🔴 Missing | **New** — boundary narrative + resource inventory from `AuthorizationBoundary` |
| 12 | Personnel Security | 🔴 Missing | **New** — screening requirements, access agreements, training |
| 13 | Contingency Plan | 🔴 Missing | **New** — contingency plan reference, BCP/DRP metadata, test dates |

---

## Part 4: Personas & Needs

### ISSO (Information System Security Officer)

The ISSO is the primary SSP author. They write section narratives, populate system environment details, and maintain the SSP throughout the system lifecycle. For the ISSO, an incomplete SSP means manual document maintenance in parallel systems. They need:
- Ability to author individual SSP sections with markdown content
- Auto-populated sections from existing data to reduce duplicated effort
- Clear visibility into which sections are complete vs. incomplete
- OSCAL export that passes structural validation on first attempt

### ISSM (Information System Security Manager)

The ISSM reviews and approves SSP sections before the document is finalized. They validate that narratives are accurate, complete, and consistent with the authorization package. They need:
- Per-section review workflow (approve / request revision)
- Complete SSP generation that assembles all approved sections
- OSCAL export for FedRAMP/eMASS submission
- Completeness dashboard showing readiness percentage

### SCA (Security Control Assessor)

The SCA evaluates the SSP during the assessment phase. An incomplete SSP blocks the assessment. They need:
- Ability to validate SSP completeness before starting assessment
- OSCAL structural validation to catch errors before eMASS import
- Confidence that all 13 sections are present and populated

### AO (Authorizing Official)

The AO makes the risk-based authorization decision. They rely on a complete SSP as the foundation of the authorization package. They need:
- Assurance that the SSP covers all required sections
- Summary completeness status without reading the full document

### Platform Engineer

The engineer provides technical details for system environment (§6), authorization boundary (§11), and infrastructure configuration. They need:
- Ability to contribute technical content to specific sections
- Auto-generated boundary descriptions from registered Azure resources

---

## Part 5: Capabilities

### User Story 1: SSP Section Management

Enable structured authoring, review, and tracking of individual SSP sections.

**[Cap 1.1]** — SSP Section Entity: Create `SspSection` entity with fields: `Id`, `RegisteredSystemId`, `SectionNumber` (int 1–13), `SectionTitle`, `Content` (markdown), `Status` (enum: NotStarted, Draft, UnderReview, Approved), `IsAutoGenerated` (bool), `AuthoredBy`, `AuthoredAt`, `ReviewedBy`, `ReviewedAt`, `ReviewerComments`, `Version` (int, auto-increment on update). The entity has a unique constraint on (RegisteredSystemId, SectionNumber). **Lifecycle rules**: Status transitions must be strictly sequential — NotStarted→Draft→UnderReview→Approved. Auto-generated sections (§1, §2, §3, §4, §7, §9, §10, §11) start at Draft. Authored sections start at NotStarted. No forward-skipping (e.g., NotStarted→Approved is invalid). Tools must validate transitions and reject invalid state changes. **Concurrency**: `Version` is an EF Core concurrency token. All write operations require the caller to pass the expected version; the write is rejected with a conflict error if the DB version differs, preventing silent overwrites.

**[Cap 1.2]** — Write SSP Section Tool: `compliance_write_ssp_section` allows the ISSO/ISSM to author or update a specific SSP section by number. For auto-generated sections (§1, §2, §3, §4, §7, §9, §10, §11), the tool regenerates from data and stores the result. For authored sections (§5, §8, §12, §13), the tool accepts markdown content and saves it. For the hybrid section (§6), the tool auto-populates hosting environment data and merges with provided content. Setting content on an auto-generated section overrides the auto-generation with a manual override flag. Updates increment the version number and set status to Draft. **Submit for review**: An optional `submit_for_review` boolean parameter (default false) transitions a Draft section to UnderReview status. The parameter is only valid when the section is in Draft status; submitting from other states returns an `INVALID_STATUS_FOR_SUBMIT` error. **Concurrency**: Updates to existing sections require an `expected_version` parameter; the write is rejected with a concurrency conflict error if the stored version differs. New sections (first write) do not require a version.

**[Cap 1.3]** — Review SSP Section Tool: `compliance_review_ssp_section` allows the ISSM/SCA to approve or request revision of a section. Approval sets status to Approved with reviewer ID and timestamp. Rejection sets status to Draft with reviewer comments describing required changes.

**[Cap 1.4]** — SSP Completeness Tool: `compliance_ssp_completeness` returns a section-by-section status report showing: section number, title, status (NotStarted/Draft/UnderReview/Approved), auto-generated flag, last modified date, author, and content word count. Includes an overall readiness percentage (approved sections / total sections * 100) and a list of blocking issues.

### User Story 2: Complete SSP Section Generation

Implement all missing NIST 800-18 sections in `SspService`.

**[Cap 2.0a]** — §1 System Identification Enhancement: Enhance the existing §1 generator to include `DitprId` and `EmassId` from `RegisteredSystem`. These identifiers are required for eMASS and DITPR submission.

**[Cap 2.0b]** — §2 Security Categorization Enhancement: Enhance the existing §2 generator to include a FIPS 200 minimum security requirements reference based on the system's categorization level.

**[Cap 2.0c]** — §4 Information System Type: New auto-generated section. Outputs system type (Major Application vs General Support System), mission criticality, operational status from `RegisteredSystem.OperationalStatus`, operational date, and disposal date. Renders the `OperationalStatus`, `OperationalDate`, and `DisposalDate` fields added to `RegisteredSystem`.

**[Cap 2.1]** — §3 System Owner / Authorizing Official: Auto-generate from `RmfRoleAssignment` records. Outputs a structured section with System Owner, Authorizing Official, ISSO, ISSM, and other key personnel with contact information. Separate from the §1 key personnel table — this section provides detailed role descriptions and responsibilities per NIST 800-18 §2.3.

**[Cap 2.2]** — §5 General Description / Purpose: Authored section. The ISSO writes a narrative describing the system's purpose, operational concept, user base, mission alignment, and scope. If `RegisteredSystem.Description` exists, it is used as a starting template.

**[Cap 2.3]** — §6 System Environment: Hybrid section. Auto-populates cloud region from `RegisteredSystem.HostingEnvironment` for Azure-hosted systems. The ISSO provides additional narrative covering physical location, logical environment description, hardware/software inventory summary, network topology, physical security, and environmental controls. Structured metadata (physical location, cloud region, etc.) is represented as markdown subsections within the `SspSection.Content` field — not as separate database columns.

**[Cap 2.4]** — §7 System Interconnections / Information Sharing: Auto-generated from `SystemInterconnection` + `InterconnectionAgreement` entities. This is the current §10 content (Feature 021) repositioned to the correct NIST 800-18 section number. Content includes connection type, target system, data flow direction, classification, agreement status, security measures, and a narrative summary of information sharing policies. Editable override available.

**[Cap 2.5]** — §8 Related Laws / Regulations / Policies: Authored section. The ISSO documents applicable federal laws (E-Government Act, FISMA, Privacy Act), executive orders, agency-specific policies, and organizational directives.

**[Cap 2.6]** — §9 Minimum Security Controls: Enhanced auto-generation combining existing §3 (baseline) and §4 (control implementations) data with new tailoring rationale data. Section outputs: selected baseline level, overlay applied, total control count by responsibility type, summary of tailored controls (added/removed with rationale from `ControlTailoring`), compensating controls, and a link to the full control detail in §10.

**[Cap 2.7]** — §10 Control Implementation Descriptions: Existing functionality — per-control narratives grouped by family. No changes required. (Section numbering note: the current §4 "Control Implementations" maps to NIST 800-18 §10.)

**[Cap 2.8]** — §11 Authorization Boundary: Auto-generated from `AuthorizationBoundary` entities + authored narrative. Resource inventory table: Azure resource ID, resource type, display name, in-boundary status, inheritance provider. Narrative description of what's inside and outside the boundary. For excluded resources, show exclusion rationale. Total resource count by type.

**[Cap 2.9]** — §12 Personnel Security: Authored section. Auto-populated role list from `RmfRoleAssignment`. The ISSO adds screening requirements (by role/level), access agreement requirements (NDA, AUP, rules of behavior), security training requirements (initial, annual, role-based), and personnel separation/transfer procedures. Structured metadata (screening levels, training requirements, etc.) is represented as markdown subsections within the `SspSection.Content` field — not as separate database columns.

**[Cap 2.10]** — §13 Contingency Plan: Authored section with structured metadata. Fields: contingency plan document reference (title, location, version), last test date, test type (tabletop/functional/full-scale), recovery time objective (RTO), recovery point objective (RPO), alternate processing site, backup procedures summary. The ISSO writes the narrative and provides plan references.

### User Story 3: OSCAL 1.1.2 SSP Export

Produce schema-compliant OSCAL SSP JSON with full structure.

**[Cap 3.1]** — Dedicated OSCAL SSP Export Service: Create `OscalSspExportService` implementing `IOscalSspExportService`. Refactor SSP-specific OSCAL generation out of `EmassExportService.BuildOscalSsp()`. The eMASS service delegates to the new service for SSP model type. Assessment Results and POA&M generation stay in `EmassExportService` unchanged.

**[Cap 3.2]** — OSCAL `metadata` Section: Generate compliant metadata with: `title`, `last-modified` (ISO 8601), `version` (from system version or "1.0"), `oscal-version` ("1.1.2"), `roles` (mapped from `RmfRoleAssignment`), `parties` (organizations and individuals from role assignments), `responsible-parties` (linking roles to party UUIDs).

**[Cap 3.3]** — OSCAL `import-profile` Section: Generate profile import referencing the selected NIST 800-53 baseline. The `href` field uses static constant strings per baseline level (Low, Moderate, High) compiled into the service — no runtime HTTP fetching or configuration lookup. Example: Moderate baseline → `https://raw.githubusercontent.com/usnistgov/oscal-content/main/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_MODERATE-baseline_profile.json`. Include overlay references if `ControlBaseline.OverlayApplied` is set.

**[Cap 3.4]** — OSCAL `system-characteristics` Section: Generate from `RegisteredSystem` + `SecurityCategorization` + `AuthorizationBoundary`. Map fields: `system-name`, `system-name-short` (acronym), `description`, `security-sensitivity-level`, `system-information` (information-types from `InformationType` with C/I/A categorizations), `security-impact-level` (from FIPS 199 levels), `authorization-boundary` (description + resource diagram reference), `network-architecture` (description from §6 environment), `data-flow` (from interconnections).

**[Cap 3.5]** — OSCAL `system-implementation` Section: Generate `components` from `AuthorizationBoundary` entities (each Azure resource becomes a component with UUID, type, title, description, status). Generate `inventory-items` from boundary resources with component references. Generate `users` from `RmfRoleAssignment` records with role-id references and privilege levels. Generate `leveraged-authorizations` from `ControlInheritance` providers (CSP FedRAMP authorizations).

**[Cap 3.6]** — OSCAL `control-implementation` Section: Enhance existing implementation. Map `ControlImplementation` records to `implemented-requirements` with: `uuid`, `control-id`, `description` (narrative), `props` (implementation-status), `responsible-roles` (from inheritance type), `by-components` (link to system-implementation component UUIDs), `statements` (for multi-part controls with parameter values).

**[Cap 3.7]** — OSCAL `back-matter` Section: Generate `resources` array with references to: SSP document itself (self-reference), authorization boundary diagram, contingency plan, ISA/MOU documents (from `InterconnectionAgreement` document references), PIA document reference, and any other attached document references. Each resource has UUID, title, description, and `rlinks` (resource links with href and media-type).

**[Cap 3.8]** — OSCAL Export Tool: `compliance_export_oscal_ssp` generates the complete OSCAL SSP JSON document and returns it as a JSON string. Parameters: `system_id` (required), `include_back_matter` (bool, default true), `pretty_print` (bool, default true). The tool calls `OscalSspExportService.ExportAsync()` and returns the JSON. Output uses kebab-case property naming per OSCAL convention. **Incomplete data handling**: Export is never blocked by missing data. If prerequisite data is absent (e.g., no SecurityCategorization, no ControlBaseline, missing authored sections), the service populates those OSCAL sections with schema-valid placeholders (empty arrays, descriptive placeholder strings) and returns warnings listing each gap. This supports iterative authoring — teams export throughout the process to check progress.

### User Story 4: OSCAL Structural Validation

Validate OSCAL SSP JSON for structural correctness before submission.

**[Cap 4.1]** — OSCAL Validation Service: Create `OscalValidationService` implementing `IOscalValidationService` with method `ValidateSspAsync(string oscalJson)` returning `OscalValidationResult`. The service checks: (1) required top-level key `system-security-plan` present, (2) required child sections present (`metadata`, `import-profile`, `system-characteristics`, `system-implementation`, `control-implementation`), (3) all UUIDs are valid format, (4) `control-id` values in implemented-requirements match the import-profile baseline, (5) component UUIDs referenced in by-components exist in system-implementation, (6) party UUIDs referenced in responsible-parties exist in metadata.parties, (7) `oscal-version` matches target version.

**[Cap 4.2]** — Validation Result Model: `OscalValidationResult` record with fields: `IsValid` (bool — true if no errors), `Errors` (List — structural errors that will cause import failure), `Warnings` (List — non-blocking issues such as missing optional fields), `Statistics` (control count, component count, inventory-item count, user count, back-matter resource count).

**[Cap 4.3]** — Validation Tool: `compliance_validate_oscal_ssp` takes a `system_id`, generates the OSCAL SSP, validates it, and returns the validation result. Does not require the user to provide raw JSON — generates and validates in one step. Returns errors, warnings, and statistics.

### User Story 5: Enhanced SSP Generation

Upgrade `SspService.GenerateSspAsync()` to produce a complete 13-section document.

**[Cap 5.1]** — Section Renumbering: Align the internal section numbering to match NIST 800-18 Rev 1. The current §1 → §1, §2 → §2, §3 → §9 (Minimum Security Controls), §4 → §10 (Control Implementations), §10 → §7 (Interconnections). New sections §3–§6, §8, §11–§13 added in correct positions.

**[Cap 5.2]** — Full SSP Assembly: `GenerateSspAsync()` assembles all 13 sections in order. For auto-generated sections, generate from entities. For authored sections, pull from `SspSection` records. If an authored section has no content, output a placeholder with "[Section not yet authored]" and add a warning. The `sections` parameter still allows selective generation.

**[Cap 5.3]** — SSP Section Key Mapping: Update the `sections` parameter values to match the new numbering: `system_identification` (§1), `categorization` (§2), `personnel` (§3), `system_type` (§4), `description` (§5), `environment` (§6), `interconnections` (§7), `laws_regulations` (§8), `minimum_controls` (§9), `control_implementations` (§10), `authorization_boundary` (§11), `personnel_security` (§12), `contingency_plan` (§13). Maintain backward compatibility: old keys (`system_information`, `baseline`, `controls`) map to new keys.

**[Cap 5.4]** — SSP Document Metadata: Add header metadata to the generated SSP: document version, generation date, system name, system ID, overall categorization level, baseline level, narrative completion percentage, section completion count. This appears at the top of the markdown output as a YAML front-matter block.

**[Cap 5.5]** — SSP Completeness Warnings: Enhanced warnings in the generated document: missing/incomplete sections highlighted, unapproved sections flagged, auto-generated sections that have been manually overridden noted, narrative completion percentage per control family.

---

## Part 6: Architecture

### Data Flow

```
┌──────────────────────────────────────────────────────────────────┐
│                        MCP Tool Layer                            │
│  write_section  review_section  completeness  generate  export   │
└────────┬─────────────┬──────────────┬───────────┬─────────┬──────┘
         │             │              │           │         │
         ▼             ▼              ▼           ▼         ▼
┌──────────────────────────────────────────────────────────────────┐
│                       ISspService (Enhanced)                     │
│  WriteSspSectionAsync  ReviewSspSectionAsync  GetCompletenessAsync│
│  GenerateSspAsync (13 sections)                                   │
└────────────────────────────┬─────────────────────────────────────┘
                             │
         ┌───────────────────┼────────────────────┐
         ▼                   ▼                    ▼
┌────────────────┐ ┌──────────────────┐ ┌──────────────────────────┐
│ IOscalSspExport│ │  AtoCopilotContext│ │  IOscalValidationService │
│  Service       │ │  (DbSets)        │ │                          │
│  ExportAsync() │ │  SspSections     │ │  ValidateSspAsync()      │
│                │ │  RegisteredSystem│ │  → Errors, Warnings,     │
│  ┌──────────┐  │ │  Categorization  │ │    Statistics             │
│  │  OSCAL   │  │ │  Baseline        │ └──────────────────────────┘
│  │  1.1.2   │  │ │  Implementations │
│  │   JSON   │  │ │  Boundary        │
│  └──────────┘  │ │  Interconnections│
└────────────────┘ │  Roles           │
                   │  Privacy (PTA/PIA)│
                   └──────────────────┘
```

### New Entities

```
SspSection
├── Id (GUID string)
├── RegisteredSystemId (FK → RegisteredSystem)
├── SectionNumber (int, 1–13)
├── SectionTitle (string)
├── Content (string, markdown)
├── Status (SspSectionStatus enum)
├── IsAutoGenerated (bool)
├── HasManualOverride (bool)
├── AuthoredBy (string)
├── AuthoredAt (DateTime)
├── ReviewedBy (string?)
├── ReviewedAt (DateTime?)
├── ReviewerComments (string?)
├── Version (int)
└── RegisteredSystem (nav prop)

SspSectionStatus enum
├── NotStarted
├── Draft
├── UnderReview
└── Approved

ContingencyPlanReference
├── Id (GUID string)
├── RegisteredSystemId (FK → RegisteredSystem)
├── DocumentTitle (string)
├── DocumentLocation (string, URL)
├── DocumentVersion (string?)
├── LastTestedDate (DateTime?)
├── TestType (string? — tabletop/functional/full-scale)
├── RecoveryTimeObjective (string? — e.g., "4 hours")
├── RecoveryPointObjective (string? — e.g., "1 hour")
├── AlternateProcessingSite (string?)
├── BackupProceduresSummary (string?)
├── CreatedBy (string)
├── CreatedAt (DateTime)
└── RegisteredSystem (nav prop)
```

### OSCAL SSP JSON Structure (Target Output)

```json
{
  "system-security-plan": {
    "uuid": "...",
    "metadata": {
      "title": "System Name SSP",
      "last-modified": "2026-03-09T...",
      "version": "1.0",
      "oscal-version": "1.1.2",
      "roles": [...],
      "parties": [...],
      "responsible-parties": [...]
    },
    "import-profile": {
      "href": "...NIST baseline profile URI..."
    },
    "system-characteristics": {
      "system-name": "...",
      "system-name-short": "...",
      "description": "...",
      "security-sensitivity-level": "moderate",
      "system-information": { "information-types": [...] },
      "security-impact-level": {
        "security-objective-confidentiality": "moderate",
        "security-objective-integrity": "moderate",
        "security-objective-availability": "moderate"
      },
      "authorization-boundary": { "description": "..." },
      "network-architecture": { "description": "..." },
      "data-flow": { "description": "..." }
    },
    "system-implementation": {
      "users": [...],
      "components": [...],
      "inventory-items": [...],
      "leveraged-authorizations": [...]
    },
    "control-implementation": {
      "description": "...",
      "implemented-requirements": [...]
    },
    "back-matter": {
      "resources": [...]
    }
  }
}
```

### Service Layer

| Service | Responsibility |
|---------|---------------|
| `SspService` (enhanced) | Section CRUD, completeness tracking, full SSP generation |
| `OscalSspExportService` (new) | OSCAL 1.1.2 SSP JSON generation from entity data |
| `OscalValidationService` (new) | Structural validation of OSCAL SSP JSON |
| `EmassExportService` (modified) | Delegates SSP OSCAL generation to `OscalSspExportService` |

---

## Part 7: Integration Points

### Existing Services Modified

| Service | Change |
|---------|--------|
| `SspService` | Add `WriteSspSectionAsync`, `ReviewSspSectionAsync`, `GetSspCompletenessAsync`. Enhance `GenerateSspAsync` to produce 13 sections with corrected numbering. |
| `EmassExportService` | `BuildOscalSsp()` delegates to `OscalSspExportService.ExportAsync()` |
| `ISspService` | Add 3 new interface methods for section management |

### Existing Tools Modified

| Tool | Change |
|------|--------|
| `compliance_generate_ssp` | Update `sections` parameter to accept new section keys per Cap 5.3. Backward-compatible old keys still work. |
| `compliance_export_oscal` | When `model=ssp`, delegate to new `OscalSspExportService` |

### New Tools

| Tool | RBAC | Description |
|------|------|-------------|
| `compliance_write_ssp_section` | PimTier.Write | Author or update a named SSP section |
| `compliance_review_ssp_section` | PimTier.Write | Approve or request revision of a section |
| `compliance_ssp_completeness` | PimTier.Read | Get per-section completion status |
| `compliance_export_oscal_ssp` | PimTier.Read | Export OSCAL 1.1.2 SSP JSON |
| `compliance_validate_oscal_ssp` | PimTier.Read | Validate OSCAL SSP structural correctness |

### New DbSets

| DbSet | Entity | Table | Notes |
|-------|--------|-------|-------|
| `SspSections` | `SspSection` | `SspSections` | Unique constraint on (RegisteredSystemId, SectionNumber) |
| `ContingencyPlanReferences` | `ContingencyPlanReference` | `ContingencyPlanReferences` | One per system |

### RegisteredSystem Extensions

Add optional fields to `RegisteredSystem`:
- `DitprId` (string?, max 50) — DoD IT Portfolio Repository identifier
- `EmassId` (string?, max 50) — eMASS system identifier
- `OperationalStatus` (enum?: Operational, UnderDevelopment, Disposed, MajorModification)
- `OperationalDate` (DateTime?) — when the system became operational
- `DisposalDate` (DateTime?) — planned or actual disposal date

---

## Part 8: What This Changes

### Breaking Changes

**Section key renaming**: The `sections` parameter in `compliance_generate_ssp` changes from the current keys (`system_information`, `categorization`, `baseline`, `controls`, `interconnections`) to NIST 800-18 aligned keys (`system_identification`, `categorization`, `personnel`, `system_type`, `description`, `environment`, `interconnections`, `laws_regulations`, `minimum_controls`, `control_implementations`, `authorization_boundary`, `personnel_security`, `contingency_plan`). **Backward compatibility maintained** — old keys are mapped to new keys internally.

**Section numbering in output**: The generated SSP markdown will use the correct NIST 800-18 section numbers (§1–§13) instead of the current arbitrary numbering (§1–§4, §10). The content is re-ordered accordingly. The current §3 (Control Baseline) content moves to §9; the current §4 (Control Implementations) moves to §10; the current §10 (Interconnections) moves to §7.

### Non-Breaking Changes

- `EmassExportService.ExportOscalAsync(systemId, OscalModelType.Ssp)` continues to work — it delegates internally to the new service
- All existing SSP tools continue to work with their current parameters
- Existing `ControlImplementation` and `ControlBaseline` entities are unchanged
- Narrative authoring workflow (`WriteNarrativeAsync`, `SuggestNarrativeAsync`, `BatchPopulateNarrativesAsync`) is unchanged

### Database Schema Additions

- New table: `SspSections` with unique constraint on `(RegisteredSystemId, SectionNumber)`
- New table: `ContingencyPlanReferences`
- New columns on `RegisteredSystems`: `DitprId`, `EmassId`, `OperationalStatus`, `OperationalDate`, `DisposalDate`

---

## Part 9: What We're NOT Building

| Excluded Item | Rationale |
|---------------|-----------|
| PDF/DOCX rendering | Feature 015 Cap 5.1 handles formatted export with templates — this feature produces markdown and JSON |
| OSCAL XML or YAML output | Federal guidance converging on JSON; XML adds complexity with no current demand |
| Full JSON Schema validation | Resource-intensive to bundle and execute JSON Schema Draft 7 at runtime; structural validation catches 95% of issues |
| Contingency plan generation | External document — we store references and metadata only |
| System architecture diagrams | Diagrams require visual rendering; we store description text and diagram document references |
| OSCAL Assessment Results or POA&M improvements | Out of scope — those remain in `EmassExportService` |
| Custom organizational SSP templates | Covered by Feature 015 Cap 5.1 template engine |
| Automated OSCAL import (consuming external OSCAL SSPs) | Future feature — this spec covers export only |
| Multi-system SSP (common control provider SSP) | Single-system SSPs only in this iteration |
| Hardware/software inventory management | §6 accepts narrative descriptions; active inventory management is a separate capability |

---

## Part 10: Success Criteria

### Acceptance Tests

| ID | Scenario | Expected Result |
|----|----------|-----------------|
| AT-01 | ISSO authors SSP §6 (System Environment) with markdown content | `SspSection` created with Status=Draft, correct section number, author recorded |
| AT-02 | ISSO updates existing SSP §6 content | Version incremented, content updated, status reset to Draft |
| AT-03 | ISSM approves SSP §6 | Status → Approved, reviewer ID and timestamp recorded |
| AT-04 | ISSM requests revision of SSP §12 with comments | Status → Draft, reviewer comments stored |
| AT-05 | ISSO runs completeness check on system with 5 authored sections | Report shows 13 sections: 5 auto-generated (Approved), 5 authored (Draft/Approved), 3 NotStarted. Readiness percentage accurate. |
| AT-06 | Generate full SSP with all 13 sections present | Markdown document contains §1–§13 in correct order with NIST 800-18 titles |
| AT-07 | Generate full SSP with missing authored sections | Placeholder text "[Section not yet authored]" appears for missing sections, warnings list them |
| AT-08 | Generate SSP §11 (Authorization Boundary) for system with 10 Azure resources | Section contains resource inventory table with all 10 resources, types, inheritance providers |
| AT-09 | Generate SSP §7 (Interconnections) matches Feature 021 content | Interconnection table with target system, connection type, data flow, classification, agreement status |
| AT-10 | Generate SSP §9 (Minimum Security Controls) with tailored controls | Section shows baseline level, total controls, tailored controls with add/remove rationale |
| AT-11 | Export OSCAL SSP JSON for system with complete data | Valid JSON with all 6 top-level sections: metadata, import-profile, system-characteristics, system-implementation, control-implementation, back-matter |
| AT-12 | OSCAL metadata contains roles and parties from RmfRoleAssignment | `roles` array has ISSO/ISSM/AO/SO entries; `parties` array has corresponding person records with UUIDs |
| AT-13 | OSCAL system-implementation has components from AuthorizationBoundary | Each boundary resource maps to a component with UUID, type, title, status |
| AT-14 | OSCAL system-implementation has users from RmfRoleAssignment | Each role assignment maps to a user entry with role-id reference |
| AT-15 | OSCAL import-profile references correct baseline catalog | Moderate baseline → Moderate profile URI; High baseline → High profile URI |
| AT-16 | OSCAL control-implementation has all baseline controls | Every control in `ControlBaseline.ControlIds` has a matching `implemented-requirement` |
| AT-17 | OSCAL back-matter has document references | ISA/MOU agreements, PIA reference, contingency plan reference appear as back-matter resources |
| AT-18 | Validate OSCAL SSP with all required sections → passes | `IsValid=true`, no errors, statistics show correct counts |
| AT-19 | Validate OSCAL SSP missing system-implementation → error | `IsValid=false`, error: "Required section 'system-implementation' is missing" |
| AT-20 | Validate OSCAL SSP with invalid component UUID reference | Warning: "Component UUID 'xxx' referenced in by-components not found in system-implementation" |
| AT-21 | `compliance_export_oscal` with `model=ssp` uses new OscalSspExportService | Output matches new service output (delegation test) |
| AT-22 | Generate SSP using old section keys (`baseline`, `controls`) | Backward compatible — maps to new keys and generates correct sections |
| AT-23 | SSP §3 (Personnel) shows AO, SO, ISSO, ISSM with role descriptions | Personnel section distinct from §1 key personnel table — includes responsibilities |
| AT-24 | SSP §13 (Contingency Plan) with ContingencyPlanReference data | Section shows plan title, location, last test date, RTO/RPO, alternate site |
| AT-25 | SSP §8 (Laws/Regulations) with authored content | Section shows applicable laws and regulations as authored by ISSO |
| AT-26 | OSCAL SSP `oscal-version` field reads "1.1.2" | Version string matches target OSCAL version |
| AT-27 | RBAC: PimTier.Read user cannot write SSP sections | Access denied on `compliance_write_ssp_section` |
| AT-28 | RBAC: PimTier.Read user can export OSCAL and run validation | `compliance_export_oscal_ssp` and `compliance_validate_oscal_ssp` succeed |
| AT-29 | Auto-generated section (§11) regenerated after boundary resource added | Content reflects the new resource; version incremented |
| AT-30 | SSP section with manual override flag preserves content on regeneration | Auto-generated section with `HasManualOverride=true` retains manual content |

### Measurable Outcomes

| ID | Metric | Target |
|----|--------|--------|
| SC-001 | SSP section coverage | 13/13 NIST 800-18 sections generated |
| SC-002 | OSCAL structural validation pass rate | ≥95% of exports pass validation on first attempt |
| SC-003 | OSCAL required sections | 6/6 top-level sections present in every export |
| SC-004 | Section authoring response time | < 3s per section write operation |
| SC-005 | Full SSP generation response time | < 15s for a system with 325 controls |
| SC-006 | OSCAL SSP export response time | < 20s for a system with 325 controls |
| SC-007 | OSCAL validation response time | < 5s for a complete SSP JSON |
| SC-008 | Backward compatibility | 100% of existing `compliance_generate_ssp` calls work with old section keys |
| SC-009 | Test coverage | ≥80% unit test coverage on new services |
| SC-010 | Completeness tracking accuracy | Readiness percentage matches actual section status count |
