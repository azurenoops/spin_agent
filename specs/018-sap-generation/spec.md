# Feature 018: Security Assessment Plan (SAP) Generation

**Created**: 2026-03-04  
**Status**: Strategic Plan  
**Purpose**: Enable Security Posture Intelligence Navigator to generate Security Assessment Plans (SAPs) for registered systems by combining the control baseline, assessment objectives from NIST SP 800-53A, assessment methods (Test/Examine/Interview), scope boundaries, and scheduling — producing a structured document that meets NIST RMF Step 4 requirements and DoD assessment standards.

---

## Clarifications

### Session 2026-03-04

- Q: Spec 015 explicitly placed SAP out of scope ("assessment planning is an organizational process outside scope"). Why bring it in now? → A: The codebase has matured significantly since spec 015. The OSCAL catalog already contains assessment objectives (`assessment-objective` parts) extractable via `NistControlsService.ExtractObjectives()`. The `ControlBaseline`, `ControlEffectiveness`, `ControlInheritance`, and evidence models are now fully implemented. With this foundation, SAP generation is no longer organizational guesswork — it's structured document assembly from data already in the system.
- Q: Should the SAP be automatically populated or require manual input for each control? → A: Hybrid. Assessment objectives and methods are **auto-populated** from the OSCAL catalog and baseline. The SCA provides high-level inputs (scope, schedule, team, boundaries) and can **override** individual control assessment methods. The goal is 80% auto-generation with 20% human refinement.
- Q: What assessment methods does 800-53A define? → A: Three: **Examine** (reviewing documents, mechanisms, activities), **Interview** (questioning personnel), and **Test** (exercising mechanisms under specified conditions). Each control has one or more prescribed methods in the OSCAL catalog.
- Q: Should inherited controls appear in the SAP? → A: Yes, but marked as "Inherited — Assessed by Provider." The SAP documents the full control scope, including inheritance designations, so the assessor knows which controls require direct assessment vs. provider attestation review.
- Q: How does SAP relate to SAR? → A: SAP is the **plan** (what will be assessed, how, when, by whom). SAR is the **results** (what was found). SAP is created before assessment begins; SAR is generated after. The SAP scope should align with the eventual SAR structure.
- Q: Should the SAP include STIG/SCAP scan plans? → A: Yes. If the system has STIG benchmarks (from Feature 017 imports or curated STIG data), the SAP includes a "Technical Testing" section listing which STIG benchmarks and SCAP profiles will be used, aligning with the `compliance_import_ckl` / `compliance_import_xccdf` workflow.
- Q: What output formats should SAP support? → A: Markdown (for chat/inline display), DOCX (for template-based generation via the existing `DocumentTemplateService`), and PDF (via QuestPDF). Follows the same pattern as SSP/SAR generation.
- Q: Can multiple SAPs coexist per system, and what happens when a new SAP is generated while a Draft or Finalized SAP already exists? → A: One Draft + unlimited historical Finalized. Only one Draft SAP may exist at a time; generating a new SAP while a Draft exists overwrites it. Finalized SAPs are retained as immutable history (read-only). Generating a new SAP after finalization creates a fresh Draft alongside the existing Finalized records. `compliance_list_saps` returns all SAPs (Draft + Finalized history).
- Q: Does the OSCAL 800-53 Rev 5 catalog contain `assessment-method` parts per control, or is a second OSCAL data source needed? → A: The Rev 5 control catalog does not reliably contain `assessment-method` named parts — those are in the separate SP 800-53A assessment procedures catalog. To keep implementation simple, default all controls to all three methods (Examine, Interview, Test), which aligns with 800-53A prescribing all three for virtually every Moderate/High control. The SCA customizes per control via `method_overrides`. No second OSCAL file is needed.
- Q: Which `ComplianceRoles` can generate, update, finalize, and view SAPs? → A: Generate/Update/Finalize restricted to `Analyst` (SCA), `SecurityLead` (ISSM), and `Administrator`. Get/List available to all roles except `Viewer`. This maps SCA→Analyst and ISSM→SecurityLead, following the existing pattern where assessment operations require `Analyst` and document generation requires `compliance:generate-documents`.
- Q: What system prerequisites must exist before a SAP can be generated? → A: A selected `ControlBaseline` is required (cannot build assessment scope without it — reject with error). Warn but proceed if the system is not yet in `RmfPhase.Assess` or has no `RmfRoleAssignment` with SCA role. This matches the `compliance_import_ckl` validation pattern: hard requirement on baseline, soft warnings on lifecycle state.
- Q: Should evidence requirements in the SAP be static prose or include a live cross-reference of existing evidence? → A: Static prose plus evidence gap summary. Generate method-derived evidence requirement text (Examine → "policy/configuration documents", etc.) AND query `ComplianceEvidence` per control to include a brief gap indicator (e.g., "Collected: 2/3 artifacts"). This gives the SCA actionable context about assessment readiness without bloating the SAP with full evidence record listings.

---

## Part 1: The Problem

### Why This Matters

The Security Assessment Plan (SAP) is a mandatory RMF Step 4 deliverable. Per NIST SP 800-37 Rev 2 and DoDI 8510.01, every system authorization requires a formally documented assessment plan before assessment execution begins. The SAP defines:

- **What** controls will be assessed (scope)
- **How** each control will be assessed (methods: Test, Examine, Interview)
- **Who** will perform the assessment (assessment team)
- **When** the assessment will occur (schedule)
- **Where** the assessment boundary extends (system scope and limitations)

### The Current Gap

| What Teams Do Today | What Security Posture Intelligence Navigator Can Do Today |
|---------------------|-------------------------------|
| Manually create SAP documents in Word from templates | Nothing — SAP generation was explicitly out of scope |
| Copy control lists from the baseline into SAP tables | `ControlBaseline.ControlIds` exists but is not rendered into SAP format |
| Look up 800-53A assessment procedures per control, manually | OSCAL catalog contains `assessment-objective` parts; `ExtractObjectives()` exists but is only used by `GetControlEnhancementAsync` |
| Assign Test/Examine/Interview methods per control | `ControlEffectiveness.AssessmentMethod` stores the method but there is no pre-assessment plan |
| Define assessment scope and exclusions manually | `AuthorizationBoundary` tracks boundary resources but isn't fed into SAP |
| Track inheritance in SAP manually | `ControlInheritance` entities exist but aren't rendered into assessment planning |
| Create assessment schedule manually | No scheduling artifacts exist |
| Iterate on SAP drafts for weeks before SCA approval | No draft/approval workflow |

### The Opportunity

Security Posture Intelligence Navigator already has all the data needed to auto-generate a SAP:

- **Control baseline** — `ControlBaseline.ControlIds` with tailoring rationale and inheritance designations
- **Assessment objectives** — OSCAL `assessment-objective` parts extracted via `NistControlsService.ExtractObjectives()`
- **Assessment methods** — Default all three (Examine, Interview, Test) per 800-53A convention for Moderate/High baselines. SCA overrides per control as needed.
- **Inheritance** — `ControlInheritance` entities with `InheritanceType` (Inherited/Shared/Customer) and provider info
- **Authorization boundary** — `AuthorizationBoundary` entities with in-scope/excluded resources
- **System context** — `RegisteredSystem` with `Name`, `Acronym`, `HostingEnvironment`, `MissionCriticality`
- **STIG coverage** — `StigControl` records with benchmark associations; `ScanImportRecord` history from Feature 017
- **Document generation** — `DocumentTemplateService` with DOCX mail-merge and QuestPDF for PDF; `DocumentGenerationService` for Markdown
- **Role assignments** — `RmfRoleAssignment` with SCA role mapped to assessor team

The missing piece is **assembly** — combining these data sources into a structured SAP document with SCA-provided overrides for scope, schedule, and methodology.

---

## Part 2: The Product

### What We're Building

**SAP Generation** allows the SCA to generate a Security Assessment Plan for a registered system through MCP tools. The tool auto-populates the SAP from existing system data (baseline, objectives, methods, boundary, inheritance) and accepts overrides for scope, schedule, assessment team, and per-control method assignments.

### What It Is

- A **document generation pipeline** that assembles SAP content from existing entities and OSCAL catalog data
- A **pre-assessment planning** tool that produces the required RMF Step 4 deliverable
- A **per-control methodology planner** that maps 800-53A assessment objectives and methods to baseline controls
- A **draft management** system that allows SAP refinement before finalization
- A **multi-format exporter** producing Markdown, DOCX, and PDF outputs

### What It Is NOT

- Not an assessment execution engine — it plans assessments, it does not run them (`compliance_assess_control` does that)
- Not a scheduling/calendar system — it captures assessment schedule metadata, not calendar integrations
- Not an assessor credentialing system — it records team members, not their qualifications
- Not an automated 800-53A procedure generator — it uses objectives from the OSCAL catalog, not custom procedure scripts
- Not a SAP approval workflow — it generates and stores the document, but does not enforce a multi-signature review chain

### Interfaces

| Surface | User | Purpose | RBAC |
|---------|------|---------|------|
| **MCP Tools** | SCA, ISSM | `compliance_generate_sap`, `compliance_update_sap`, `compliance_finalize_sap` | `Analyst`, `SecurityLead`, `Administrator` |
| **MCP Tools** | SCA, ISSM, ISSO | `compliance_get_sap`, `compliance_list_saps` | All roles except `Viewer` |
| **VS Code (@ato)** | SCA | `@ato Generate a Security Assessment Plan for system X` → produces SAP with assessment methodology | `Analyst`+ |
| **Teams Bot** | ISSM | `@Security Posture Intelligence Navigator show SAP status for system X` → summary card with assessment scope and schedule | `SecurityLead`+ |

---

## Part 3: SAP Document Structure (NIST SP 800-53A / DoD SAP Template)

A SAP conforming to NIST SP 800-37 Rev 2 Task A-1 and DoDI 8510.01 Enclosure 6 contains the following sections:

### 3.1 SAP Section Structure

| Section | Title | Content Source |
|---------|-------|----------------|
| 1 | **Introduction** | System name, acronym, purpose, RMF context |
| 2 | **System Description** | `RegisteredSystem` — hosting environment, mission criticality, system type, boundary |
| 3 | **Assessment Scope** | `ControlBaseline.ControlIds` — total control count, families, tailored/inherited breakdown |
| 4 | **Assessment Objectives** | Per-control objectives from OSCAL `assessment-objective` parts |
| 5 | **Assessment Methods** | Per-control Test/Examine/Interview mapping from OSCAL + SCA overrides |
| 6 | **Assessment Procedures** | Per-control assessment steps derived from objectives and methods |
| 7 | **Controls Excluded from Assessment** | Inherited controls (provider-assessed), tailored-out controls |
| 8 | **STIG/SCAP Testing Plan** | STIG benchmarks applicable to system, SCAP scan profiles |
| 9 | **Assessment Team** | `RmfRoleAssignment` SCA entries + user-provided team members |
| 10 | **Assessment Schedule** | User-provided milestone dates (start, end, milestones) |
| 11 | **Rules of Engagement** | Assessment constraints, limitations, system availability windows |
| 12 | **Artifact & Evidence Requirements** | Per-control evidence expectations based on method |
| 13 | **Risk Assessment Approach** | CAT severity mapping, residual risk methodology |
| 14 | **Appendix A: Control Assessment Matrix** | Full control × method × objective table |
| 15 | **Appendix B: STIG Benchmark List** | STIG IDs, versions, rule counts |

### 3.2 Per-Control Assessment Entry

For each control in the baseline, the SAP includes:

```
┌──────────────────────────────────────────────────────────────┐
│ Control: AC-2 — Account Management                          │
│ Family: Access Control (AC)                                 │
│ Baseline: Moderate                                          │
│ Inheritance: Customer Responsibility                        │
│                                                             │
│ Assessment Objectives:                                      │
│   (a) Define types of accounts allowed and specifically     │
│       prohibited                                            │
│   (b) Assign account managers                               │
│   (c) Require conditions for group membership               │
│   (d) Specify authorized users, group/role membership       │
│   ...                                                       │
│                                                             │
│ Assessment Method: Examine, Interview, Test                 │
│                                                             │
│ Procedures:                                                 │
│   Examine: Review account management policy, system         │
│            configuration, and account listing                │
│   Interview: Discuss account management process with        │
│              system administrators                          │
│   Test: Attempt unauthorized account creation; verify       │
│         account disable after inactivity period             │
│                                                             │
│ Evidence Required:                                          │
│   - Account management policy document                     │
│   - System account listing with roles                      │
│   - Configuration screenshots or exports                   │
│                                                             │
│ STIG Coverage: Windows_Server_2022_STIG (V-254239, V-...)  │
│ SCAP Profile: DISA_STIG_Windows_Server_2022_V1R1           │
└──────────────────────────────────────────────────────────────┘
```

### 3.3 Assessment Method Definitions (NIST SP 800-53A)

| Method | Definition | Typical Artifacts |
|--------|------------|-------------------|
| **Examine** | Review, inspect, observe, study, or analyze assessment objects (specifications, mechanisms, activities) | Policies, configuration files, system documentation, architecture diagrams |
| **Interview** | Discuss with individuals or groups to facilitate understanding, clarification, or corroboration | Interviews with system admins, ISSOs, developers, users |
| **Test** | Exercise assessment objects under specified conditions to compare actual vs. expected behavior | Automated scans (STIG/SCAP), penetration tests, functional tests, vulnerability scans |

---

## Part 4: Personas & Needs

### SCA (Security Control Assessor) — Primary

| Need | Description | Status |
|------|-------------|--------|
| Generate SAP from baseline | Auto-populate SAP with controls, objectives, and methods from existing data | **MISSING** |
| Customize assessment methods per control | Override default Test/Examine/Interview assignments | **MISSING** |
| Define assessment scope and exclusions | Mark controls for out-of-scope assessment (inherited, N/A) | **MISSING** |
| Set assessment schedule | Define start date, end date, and milestones | **MISSING** |
| Specify assessment team members | List assessors with roles and organizations | **MISSING** |
| Include STIG/SCAP testing plan | Auto-populate with applicable STIG benchmarks | **MISSING** |
| Draft and refine SAP | Iteratively update SAP before finalizing | **MISSING** |
| Finalize SAP | Lock SAP to prevent further edits (immutable record) | **MISSING** |
| Export SAP as DOCX/PDF | Download formatted SAP document for submission | **MISSING** |

### ISSM (Information System Security Manager)

| Need | Description | Status |
|------|-------------|--------|
| Review SAP before assessment | View generated SAP to verify scope and methodology | **MISSING** |
| Track SAP status | See whether SAP is draft or finalized | **MISSING** |
| Ensure SAP covers full baseline | Verify all baseline controls have assessment entries | **MISSING** |
| View SAP history | Track when SAPs were generated and finalized | **MISSING** |

### ISSO (Information System Security Officer)

| Need | Description | Status |
|------|-------------|--------|
| Provide evidence availability info | Inform assessor what evidence is already collected | **HAVE** (via `compliance_check_evidence_completeness`) |
| View assessment plan for preparation | Know what controls will be tested and by what method | **MISSING** |

### Engineer

| Need | Description | Status |
|------|-------------|--------|
| View testing requirements | Know which STIG benchmarks and controls will be assessed | **MISSING** |
| Prepare evidence artifacts | Know what documentation is needed per control | **MISSING** |

---

## Part 5: Capabilities

### Phase 1: SAP Data Assembly & Generation (Core)

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 1.1 | **Assessment Objective Extraction** | — | For each control in the baseline, extract assessment objectives from the OSCAL catalog via `NistControlsService.GetControlEnhancementAsync()`. Parse `assessment-objective` parts recursively. Cache per control. |
| 1.2 | **Assessment Method Resolution** | — | For each control, assign assessment methods. Default all controls to all three methods (Examine, Interview, Test) — this aligns with 800-53A prescribing all three for virtually every Moderate/High control. No OSCAL `assessment-method` part extraction is needed; the default is correct for DoD. Allow SCA override per control via `method_overrides`. |
| 1.3 | **Control Scope Assembly** | — | Build the assessment scope from `ControlBaseline.ControlIds`, annotating each control with: inheritance type (from `ControlInheritance`), tailoring status (from `ControlTailoring`), and whether it requires direct assessment or provider attestation review. |
| 1.4 | **STIG/SCAP Test Plan Builder** | — | For each control in scope, find associated `StigControl` records via NIST→CCI→STIG reverse mapping. Group by benchmark. Include benchmark ID, version, applicable rule count, and scan import history (from `ScanImportRecord`). |
| 1.5 | **Evidence Requirements Mapping** | — | For each control and method combination, generate evidence requirements as static prose (Examine → document/configuration artifacts, Interview → personnel list, Test → scan tools and test procedures). Additionally, query `ComplianceEvidence` records per control to include an evidence gap summary (e.g., "Collected: 2/3 artifacts") giving the SCA actionable assessment readiness context. Does not list full evidence record details — use `compliance_check_evidence_completeness` for that. |
| 1.6 | **SAP Document Entity** | — | Create `SecurityAssessmentPlan` entity to persist SAP metadata and content. Tracks system, assessment, scope, status (Draft/Finalized), content, and generation metadata. |
| 1.7 | **SAP Content Generator** | SCA | Assemble all sections (§3.1) into a structured Markdown document. Includes: Introduction, System Description, Assessment Scope, per-control Assessment Matrix, STIG/SCAP Testing Plan, team, schedule, rules of engagement, evidence requirements. |
| 1.8 | **Generate SAP MCP Tool** | SCA | `compliance_generate_sap` tool accepting `system_id`, optional `assessment_id`, `schedule_start`, `schedule_end`, `team_members`, `scope_notes`, `method_overrides`, `format`. **Prerequisites**: system must have a selected `ControlBaseline` (hard error if missing). Warns if system is not in `RmfPhase.Assess` or has no SCA role assignment. Returns SAP content with metadata. |

### Phase 2: SAP Customization & Draft Management

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 2.1 | **SAP Update Tool** | SCA | `compliance_update_sap` — modify SAP sections: schedule, team, rules of engagement, per-control method overrides, scope notes. Only works on Draft SAPs. |
| 2.2 | **Per-Control Method Override** | SCA | Allow SCA to change the assessment method for individual controls (e.g., change AC-2 from "Examine, Test" to "Examine, Interview, Test"). Persisted as fields on `SapControlEntry` (`IsMethodOverridden`, `OverrideRationale`, `AssessmentMethods`). |
| 2.3 | **Assessment Team Management** | SCA | Add/remove assessment team members with name, organization, role (Lead Assessor, Assessor, Technical SME), and contact info. Stored as `SapTeamMember` child entities with cascade delete. |
| 2.4 | **Rules of Engagement** | SCA | Free-text rules of engagement section: assessment restrictions, system availability windows, points of contact, escalation procedures. |
| 2.5 | **SAP Finalization** | SCA, ISSM | `compliance_finalize_sap` — locks the SAP, sets status to `Finalized`, computes SHA-256 content hash for integrity, records finalized timestamp and finalizer. Finalized SAPs cannot be modified. |

### Phase 3: SAP Retrieval & Export

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 3.1 | **Get SAP Tool** | SCA, ISSM | `compliance_get_sap` — retrieve a specific SAP by ID or the latest SAP for a system. Returns content and metadata. |
| 3.2 | **List SAPs Tool** | ISSM | `compliance_list_saps` — list all SAPs for a system with status, dates, scope summary. |
| 3.3 | **DOCX Export** | SCA, ISSM | Generate SAP as DOCX using `DocumentTemplateService` mail-merge with new `sap` merge-field schema. |
| 3.4 | **PDF Export** | SCA, ISSM | Generate SAP as PDF using QuestPDF following existing document rendering patterns. |
| 3.5 | **SAP-to-SAR Alignment** | SCA | When generating a SAR (`compliance_generate_sar`), cross-reference the finalized SAP to verify all planned controls were assessed. Include "SAP Alignment" section in SAR showing planned vs. actual assessment coverage. |

### Phase 4: Integration & Observability

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 4.1 | **SAP Status in RMF Lifecycle** | ISSM | Include SAP status in RMF step tracking. System in Assess phase should show SAP state: None → Draft → Finalized. |
| 4.2 | **SAP Completeness Validation** | SCA | Validate SAP has all required sections populated: scope covers all baseline controls, at least one method per control, team has at least one assessor, schedule has start/end dates. Return warnings for incomplete sections. |
| 4.3 | **Structured Logging** | — | Log SAP generation/update/finalization with system ID, SAP ID, control count, duration, user. |
| 4.4 | **SAP Summary in System Dashboard** | ISSM | Expose SAP metadata via `GetSapStatusAsync` service method returning latest SAP state (None/Draft/Finalized), scope coverage percentage, and assessment readiness indicator. Dashboard UI integration is out of scope for this feature. |

---

## Part 6: Architecture

### SAP Generation Pipeline

```
┌──────────────────┐     ┌──────────────────┐     ┌──────────────────┐
│  RegisteredSystem │     │  ControlBaseline  │     │  OSCAL Catalog   │
│  (name, acronym,  │     │  (ControlIds,     │     │  (assessment-    │
│   hosting, type)  │     │   tailorings,     │     │   objective,     │
│                   │     │   inheritances)   │     │   assessment-    │
└────────┬──────────┘     └────────┬──────────┘     │   method parts)  │
         │                         │                └────────┬─────────┘
         │                         │                         │
         ▼                         ▼                         ▼
    ┌──────────────────────────────────────────────────────────────────┐
    │                    SAP Assembly Service                          │
    │                                                                  │
    │  1. Load system context (RegisteredSystem, boundary, roles)      │
    │  2. Load baseline controls with inheritance & tailoring          │
    │  3. For each control:                                            │
    │     a. Extract assessment objectives from OSCAL                  │
    │     b. Default all three assessment methods; apply SCA overrides │
    │     c. Map to STIG/SCAP benchmarks via CCI reverse lookup        │
    │     d. Identify evidence requirements by method type             │
    │     e. Annotate with inheritance designation                     │
    │  4. Build STIG testing plan (benchmarks, rule counts)            │
    │  5. Merge user inputs (schedule, team, ROE, scope notes)         │
    │  6. Render document in requested format                          │
    └──────────────────────────────────────────────────────────────────┘
         │              │              │
         ▼              ▼              ▼
    ┌──────────┐  ┌──────────┐  ┌──────────┐
    │ Markdown │  │   DOCX   │  │   PDF    │
    │ (inline) │  │ (template│  │ (Quest   │
    │          │  │  merge)  │  │  PDF)    │
    └──────────┘  └──────────┘  └──────────┘
```

### OSCAL Assessment Objective Extraction

The NIST OSCAL catalog contains `assessment-objective` parts for each control. Assessment **methods** are NOT reliably present in the Rev 5 control catalog (they live in the separate SP 800-53A assessment procedures catalog). For simplicity, all controls default to all three methods (Examine, Interview, Test); the SCA overrides per control as needed.

```
OscalControl
  └── Parts[]
        ├── Name: "statement"             → Control requirement text
        ├── Name: "guidance"              → Supplemental guidance
        └── Name: "assessment-objective"  → Assessment objectives (ALREADY EXTRACTED)
```

### SAP Entity Relationships

```
RegisteredSystem (1) ──── (*) SecurityAssessmentPlan
                                  │
                                  ├── SapControlEntry (*) ── (1) per baseline control
                                  │     ├── ControlId
                                  │     ├── Objectives[]
                                  │     ├── Methods[] (Test/Examine/Interview)
                                  │     ├── InheritanceType
                                  │     ├── StigBenchmarks[]
                                  │     └── EvidenceRequirements[]
                                  │
                                  ├── SapTeamMember (*)
                                  │     ├── Name, Organization, Role
                                  │     └── ContactInfo
                                  │
                                  └── Metadata
                                        ├── Status (Draft/Finalized)
                                        ├── ScheduleStart, ScheduleEnd
                                        ├── RulesOfEngagement
                                        ├── ScopeNotes
                                        └── ContentHash (finalized)
```

---

## Part 7: Integration Points

### 7.1 Existing Services Used

| Service | How It's Used |
|---------|--------------|
| `INistControlsService` | `GetControlEnhancementAsync` — extract assessment objectives and methods per control. `GetControlAsync` — control titles and families. |
| `IBaselineService` | `GetBaselineAsync` — retrieve control baseline with inheritance and tailoring details |
| `IRmfLifecycleService` | `GetSystemAsync` — system context. `GetRoleAssignmentsAsync` — SCA team members from role assignments |
| `IStigKnowledgeService` | `GetStigsByCciChainAsync` — find STIG controls mapped to NIST controls via CCI chain reverse lookup for STIG testing plan |
| `IDocumentTemplateService` | `RenderDocxAsync`, `RenderPdfAsync` — DOCX/PDF export with merge-field schemas |
| `IAssessmentArtifactService` | `CheckEvidenceCompletenessAsync` — existing evidence status per control |
| `IScanImportService` | `ListImportsAsync` — scan import history for STIG/SCAP testing plan context |

### 7.2 New Service

| Service | Purpose |
|---------|---------|
| `ISapService` / `SapService` | SAP generation, update, finalization, retrieval, export |

### 7.3 Cross-Feature Dependencies

| Feature | Relationship |
|---------|-------------|
| Feature 015 (Persona Workflows) | `ControlBaseline`, `ControlInheritance`, `ControlTailoring`, `RmfRoleAssignment` entities |
| Feature 017 (SCAP/STIG Import) | `ScanImportRecord` history for STIG testing plan; `StigControl` for NIST→STIG reverse mapping |
| Feature 008 (Compliance Engine) | `ComplianceAssessment`, `ControlEffectiveness` for SAP→SAR alignment |
| Feature 007 (NIST Controls) | OSCAL catalog, `NistControlsService` for objectives and method extraction |

---

## Part 8: What This Changes

### Entity Model Changes

| Entity | Change | Details |
|--------|--------|---------|
| `SecurityAssessmentPlan` | **NEW** | SAP metadata, content, status, scope, schedule, team. FK to `RegisteredSystem` and optional `ComplianceAssessment`. |
| `SapControlEntry` | **NEW** | Per-control assessment plan entry: control ID, objectives, methods, inheritance, STIG coverage, evidence requirements. FK to `SecurityAssessmentPlan`. |
| `SapTeamMember` | **NEW** | Assessment team member: name, organization, role, contact. FK to `SecurityAssessmentPlan`. |

### Service Layer Changes

| Component | Change |
|-----------|--------|
| `ISapService` / `SapService` | **NEW** — SAP generation, update, finalization, retrieval |
| `INistControlsService` | **NO CHANGE NEEDED** — Assessment objectives already extracted via `GetControlEnhancementAsync`. Assessment methods default to all three; no OSCAL method extraction required. |
| `DocumentTemplateService` | **EXTENDED** — Add `sap` merge-field schema for DOCX export |
| `AtoCopilotContext` | **MODIFIED** — Add `SecurityAssessmentPlans`, `SapControlEntries`, `SapTeamMembers` DbSets |

### Tool Layer Changes

| Tool | Parent File | Description |
|------|-------------|-------------|
| `GenerateSapTool` | `SapTools.cs` | Generate SAP from baseline + OSCAL data |
| `GetSapTool` | `SapTools.cs` | Retrieve SAP by ID or latest for system |
| `UpdateSapTool` | `SapTools.cs` | Modify SAP sections (draft only) |
| `ListSapsTool` | `SapTools.cs` | List SAPs for a system |
| `FinalizeSapTool` | `SapTools.cs` | Lock SAP with SHA-256 integrity hash |

### MCP Registration

5 new tools registered in `ComplianceMcpTools.cs`.

---

## Part 9: What We're NOT Building

| Out of Scope | Reason |
|-------------|--------|
| **SAP approval workflow** | Multi-signature approval chains are organizational process — SAP is generated and finalized, but approval routing is outside the tool |
| **Assessor credentialing** | Validating SCA qualifications (e.g., DISA Assessor Training) is an HR/credentialing concern |
| **Calendar integration** | SAP captures schedule dates but does not integrate with Outlook/Teams calendar |
| **Custom 800-53A procedure authoring** | SAP uses OSCAL-derived objectives; custom procedure scripting per control is out of scope |
| **Automated assessment triggering** | SAP plans assessments, it does not execute them — `compliance_assess_control` handles execution |
| **SAP versioning / diff** | One Draft SAP at a time; Finalized SAPs retained as immutable history. Generating a new SAP while a Draft exists overwrites the Draft. No diff between SAP versions. |
| **OSCAL SAP format export** | OSCAL Assessment Plan model is complex; initial release produces Markdown/DOCX/PDF. OSCAL AP export is a future enhancement. |
| **Distributed/multi-team assessment planning** | SAP is one plan per system, not a distributed task assignment system across multiple assessment teams |

---

## Part 10: Success Criteria

| Criterion | Measurement |
|-----------|-------------|
| SAP covers 100% of baseline controls | Every control in `ControlBaseline.ControlIds` has a `SapControlEntry` |
| Assessment objectives populated for ≥90% of controls | OSCAL catalog coverage for `assessment-objective` parts across baseline controls. Below 90% triggers a validation warning in `ValidateSapAsync` (reported via `SapValidationResult.ControlsMissingObjectives`). |
| Assessment methods assigned to all controls | Every `SapControlEntry` has at least one method (Test/Examine/Interview) |
| Inherited controls annotated correctly | Controls with `ControlInheritance` records marked as "Inherited — Assessed by Provider" with provider name |
| STIG benchmarks listed for applicable controls | Controls with CCI→STIG mappings show benchmark coverage in SAP |
| SAP renders as valid Markdown | Output passes Markdown linting and is readable in chat |
| SAP exports as valid DOCX | DOCX opens correctly in Microsoft Word with all sections populated |
| SAP exports as valid PDF | PDF renders with correct page layout, headers, and table formatting |
| Finalized SAP is immutable | No update operations succeed on a Finalized SAP; SHA-256 hash verifiable |
| SAP-to-SAR alignment detectable | When SAR is generated, out-of-plan controls and unassessed planned controls are flagged |
| Performance: SAP generation < 15s for Moderate baseline | ~325 controls processed with OSCAL lookups in < 15 seconds |
| Performance: SAP generation < 30s for High baseline | ~421 controls processed in < 30 seconds |
| SCA override persists correctly | Per-control method overrides are stored and reflected in subsequent SAP retrievals |

---

## Part 11: Build Sequence & Rationale

### Why This Order?

1. **Phase 1 (Core Generation) first** because the primary value is producing the SAP document. This requires OSCAL extraction, baseline assembly, and Markdown rendering — the core pipeline.

2. **Phase 2 (Customization) second** because the SCA needs to refine the auto-generated SAP before finalization. Method overrides, team management, and rules of engagement are essential for real-world use.

3. **Phase 3 (Export & Retrieval) third** because DOCX/PDF export and SAP retrieval are delivery mechanisms built on top of the generated content.

4. **Phase 4 (Integration) last** because SAP-to-SAR alignment, RMF lifecycle integration, and observability are enhancement features that build on a working SAP pipeline.

### Timeline Estimate

| Phase | Scope | Estimate |
|-------|-------|----------|
| Phase 1 | OSCAL method extraction, scope assembly, STIG test plan, SAP entity, Markdown generator, GenerateSap tool | 4–5 days |
| Phase 2 | UpdateSap tool, method overrides, team management, rules of engagement, finalization | 2–3 days |
| Phase 3 | GetSap, ListSaps tools, DOCX merge schema, PDF rendering | 2–3 days |
| Phase 4 | SAP-SAR alignment, RMF lifecycle integration, completeness validation, structured logging | 1–2 days |
| **Total** | **5 tools, 3 new entities, 1 new service, OSCAL extension** | **9–13 days** |
