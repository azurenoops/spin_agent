# ATO Copilot: Product Identity, Persona Workflows & RMF Alignment

**Created**: 2026-02-27  
**Status**: Strategic Plan  
**Purpose**: Define what ATO Copilot *is*, who it's for, and how it maps to the real DoD RMF process — then identify what we have, what we're missing, and what to build next.

---

## Part 1: What Is ATO Copilot?

### The Problem

Getting a DoD system through the Risk Management Framework (RMF) is a **6–18 month process** that involves dozens of artifacts, hundreds of security controls, multiple independent assessments, and coordination across organizational boundaries. The process is defined by DoDI 8510.01, requires compliance with NIST SP 800-53 Rev 5 (1,000+ controls), DISA STIGs (thousands of rules per technology), and CNSSI 1253 overlays. It's document-heavy, repetitive, error-prone, and largely manual.

The key friction points:

1. **ISSMs/ISSOs spend weeks** documenting how each control is implemented, writing the same boilerplate across systems.
2. **SCAs spend weeks** manually checking controls, cross-referencing evidence, and writing assessment reports.
3. **Engineers are told** to "fix STIG findings" with no context on what the finding means for their specific technology stack.
4. **AOs receive authorization packages** that are hundreds of pages and must make risk decisions based on dense, inconsistent documentation.
5. **Everyone** fights with eMASS trying to get data in and out.
6. **After ATO is granted**, continuous monitoring becomes another manual slog of monthly reports and annual reviews.

### The Product

**ATO Copilot is an AI-powered assistant that guides DoD teams through every step of the Risk Management Framework** — from system registration through continuous monitoring — by combining real Azure compliance scanning with RMF workflow automation, natural language interaction, and document generation.

It is **not** a replacement for eMASS. It is **not** a GRC platform. It is **not** a vulnerability scanner.

It **is** a copilot — a smart assistant that:
- **Knows the RMF process** and guides users through it step by step
- **Knows NIST 800-53** (the full Rev 5 catalog is embedded) and can explain any control
- **Scans real Azure infrastructure** using Policy + Defender + ARM APIs to produce actual compliance data (supports Azure Commercial, Azure Government, and air-gapped Azure Government IL5/IL6 regions via configurable environment profiles)
- **Generates real documents** (SSP, SAR, POA&M) from actual assessment data — not templates
- **Tracks remediation** through finding-to-fix on Kanban boards
- **Monitors continuously** for compliance drift with alerting
- **Speaks each persona's language** — showing the ISSM a dashboard, the SCA an assessment checklist, and the Engineer a STIG fix

### Interfaces

| Surface | User | Purpose |
|---------|------|---------|
| **VS Code (GitHub Copilot Chat)** | Engineers, ISSOs | `@ato` participant with `/compliance`, `/knowledge`, `/config` slash commands. IaC compliance scanning. Live in-editor experience. |
| **Microsoft Teams (M365 Bot)** | ISSMs, AOs, SCAs | Adaptive Cards for dashboards, assessments, approvals. Where leadership and security teams work. |
| **MCP Server API** | All (via any MCP client) | REST + SSE + stdio transport. Powers everything above. |

---

## Part 2: The DoD RMF Process (DoDI 8510.01)

The RMF is a **7-step lifecycle** (NIST SP 800-37 Rev 2 added "Prepare" as Step 0, making it 7 steps; DoDI 8510.01 uses 6 steps starting at Categorize). For this plan we use the **NIST 7-step model** because Step 0 (Prepare) is where half the real work happens.

```
┌──────────┐    ┌─────────────┐    ┌──────────┐    ┌─────────────┐
│  PREPARE  │───▶│ CATEGORIZE  │───▶│  SELECT   │───▶│ IMPLEMENT   │
│  (Step 0) │    │  (Step 1)   │    │ (Step 2)  │    │  (Step 3)   │
└──────────┘    └─────────────┘    └──────────┘    └─────────────┘
                                                         │
                    ┌──────────┐    ┌─────────────┐      │
                    │ MONITOR  │◀───│ AUTHORIZE   │◀──── │
                    │ (Step 6) │    │  (Step 5)   │      │
                    └────┬─────┘    └─────────────┘      │
                         │               ▲               │
                         │    ┌──────────┴──┐            │
                         └───▶│   ASSESS    │◀───────────┘
                              │  (Step 4)   │
                              └─────────────┘
```

### Step 0: Prepare
**What happens**: Organization establishes governance, risk strategy, and system-level preparation. System is registered, authorization boundary defined, roles assigned, and asset baseline established.

**Key artifacts**: System Registration, Authorization Boundary Diagram, Organizational Risk Strategy, Stakeholder Register (RMF Role Assignments), Asset Inventory, Information Types

**Who does the work**: System Owner, ISSM

### Step 1: Categorize
**What happens**: System's information types are analyzed per FIPS 199 and NIST SP 800-60 to determine Confidentiality, Integrity, Availability impact (Low/Moderate/High). For DoD, CNSSI 1253 maps this to Impact Levels (IL2–IL6). The categorization drives everything downstream.

**Key artifacts**: FIPS 199 Categorization, CNSSI 1253 Overlay Applicability, Information Type Inventory

**Who does the work**: System Owner, Information Owner, ISSM

### Step 2: Select
**What happens**: Based on categorization, the correct NIST 800-53 Rev 5 control baseline is selected (Low=131, Moderate=325, High=421 controls). Controls are tailored (add/remove with rationale), overlays applied (CNSSI 1253, DoD-specific), and inheritance declared (which controls are covered by the CSP vs. the customer). DISA STIGs are mapped to applicable controls.

**Key artifacts**: Control Baseline with Tailoring, Customer Responsibility Matrix (CRM), STIG Applicability Matrix

**Who does the work**: ISSO, ISSM, with SCA review

### Step 3: Implement
**What happens**: Selected controls are actually built/configured in the system. Each control implementation is documented in the SSP. STIG configurations are applied. SOPs are written.

**Key artifacts**: System Security Plan (SSP) with control implementation details, STIG Checklists (completed), Configuration Baselines, SOPs

**Who does the work**: System Owner (Engineer), ISSO, System Administrators

### Step 4: Assess
**What happens**: An independent SCA evaluates whether controls are actually working. This involves reviewing documentation, running scans (ACAS/SCAP), interviewing operators, and testing controls. Findings are severity-rated (CAT I/II/III). Results go into the SAR. A POA&M is created for any findings that can't be fixed immediately.

**Key artifacts**: Security Assessment Plan (SAP), Security Assessment Report (SAR), Risk Assessment Report (RAR), Plan of Action & Milestones (POA&M)

> **Note on SAP**: The Security Assessment Plan (SAP) is a pre-assessment planning document typically owned by the SCA. ATO Copilot does not generate SAPs — assessment planning is an organizational process outside scope. The copilot supports assessment *execution* (§3.1–3.6) and *reporting* (SAR/RAR).

**Who does the work**: SCA (independent), with ISSO/ISSM support

### Step 5: Authorize
**What happens**: The AO reviews the authorization package (SSP + SAR + RAR + POA&M), evaluates residual risk, and makes a decision:
- **ATO** (Authority to Operate) — approved, typically 3 years
- **ATO with Conditions** — approved with specific remediation requirements
- **IATT** (Interim Authority to Test) — limited approval for testing only, typically 6 months
- **DATO** (Denial of Authorization to Operate) — rejected, system cannot operate. DATO sets the system to read-only mode for all non-Admin roles, generates a persistent alert, and prevents RMF step advancement until a new assessment cycle is initiated.

**Key artifacts**: Authorization Decision Letter, Terms & Conditions, Risk Acceptance Memorandum

### RMF Phase Transition Audit Trail

Every successful RMF phase transition MUST persist an immutable audit entry in the same database transaction as the system phase update. The entry records the system, previous phase, target phase, actor identity, timestamp, whether gate failures were overridden, and optional transition notes. Rejected transitions MUST NOT create successful transition entries.

The system detail experience MUST expose the transition history in reverse chronological order. Authorization package generation MUST include the same history so reviewers can establish when and by whom the system advanced through RMF.

**Who decides**: AO (and only the AO)

### Step 6: Monitor
**What happens**: After ATO is granted, the system enters continuous monitoring. Monthly/quarterly assessments track compliance drift. POA&M items are tracked to closure. Configuration changes are evaluated for "significant change" that might trigger re-authorization. The ATO expires (typically every 3 years) and the cycle repeats.

**Key artifacts**: Continuous Monitoring Plan, Monthly/Quarterly ConMon Reports, Annual Security Review, Updated POA&M, Reauthorization Package (when needed)

**Who does the work**: ISSO (day-to-day), ISSM (oversight), SCA (periodic assessments)

---

## Part 3: The Personas

### Persona 1: ISSM (Information System Security Manager)

**Real-world role**: The quarterback of the RMF process. Manages the security posture for one or more systems. Reports to the AO. Oversees ISSOs. Owns the authorization package.

**Maps to**: `Compliance.SecurityLead` (existing), `Compliance.Administrator` (for copilot infrastructure management only — AO delegation now handled by `Compliance.AuthorizingOfficial`)

**What they do day-to-day**:
- Track where each system is in the RMF lifecycle
- Review compliance dashboards and trend reports
- Coordinate remediation priorities across teams
- Prepare authorization packages for the AO
- Manage POA&M items and milestone deadlines
- Respond to compliance alerts and significant changes
- Brief leadership on security posture
- Interface with eMASS

**What they need from ATO Copilot**:

| Need | RMF Step | Status Today |
|------|----------|-------------|
| Register a system and define its authorization boundary | Step 0 | **MISSING** — no system registration concept |
| Assign RMF roles (AO, ISSO, SCA) to a system | Step 0 | **MISSING** — no role assignment tracking |
| Perform FIPS 199 categorization and map to DoD IL | Step 1 | **MISSING** — framework is a flat string |
| View and manage control baselines with tailoring | Step 2 | **MISSING** — no baseline management |
| Declare control inheritance (provider vs customer) | Step 2 | **MISSING** — all controls treated as customer |
| Generate a CRM (Customer Responsibility Matrix) | Step 2 | **MISSING** |
| Track SSP completeness (how many controls documented?) | Step 3 | **PARTIAL** — SSP generates but no per-control tracking |
| Review assessment results and create POA&M | Step 4 | **HAVE** — assessment + Kanban POA&M |
| Prepare authorization package for AO | Step 5 | **PARTIAL** — can generate SSP/SAR/POA&M but not as a bundled package |
| Track ATO status (type, expiration date, conditions) | Step 5 | **MISSING** — no authorization decision tracking |
| View compliance dashboard across all systems | Step 6 | **PARTIAL** — monitoring exists but not multi-system |
| Track POA&M milestones and overdue items | Step 6 | **PARTIAL** — Kanban SLA dates but no formal POA&M milestones |
| Generate ConMon reports | Step 6 | **PARTIAL** — trends exist but not formatted reports |
| Detect significant changes and flag for reauth | Step 6 | **MISSING** |
| Export data to eMASS format | All | **MISSING** |

### Persona 2: SCA / Auditor (Security Control Assessor)

**Real-world role**: Independent assessor who evaluates whether controls actually work. Cannot implement or fix — only assess. Must be organizationally independent from the team that implemented the controls. Produces the SAR and RAR.

**Maps to**: `Compliance.Auditor` (existing — read-only enforcement is correct)

**What they do day-to-day**:
- Review the SSP to understand claimed implementations
- Test controls using scanning tools, interviews, and documentation review
- Rate findings by severity (DoD CAT I/II/III)
- Collect and verify evidence
- Document findings in the SAR
- Produce the RAR (residual risk determination)
- Provide the AO with a risk-informed recommendation

**What they need from ATO Copilot**:

| Need | RMF Step | Status Today |
|------|----------|-------------|
| Review SSP implementation claims for assigned system | Step 4 | **PARTIAL** — can view SSP but it's not per-control reviewable |
| Run independent assessment of controls | Step 4 | **HAVE** — compliance assessment tool |
| Collect evidence and verify its integrity (hash, chain of custody) | Step 4 | **PARTIAL** — evidence collection exists but no hash verification or chain of custody |
| Mark controls as Satisfied / Other Than Satisfied | Step 4 | **MISSING** — no per-control effectiveness determination |
| Create findings with DoD CAT severity levels | Step 4 | **PARTIAL** — findings exist but use generic severity (Critical/High/Medium/Low), not CAT I/II/III |
| Take an immutable assessment snapshot | Step 4 | **MISSING** |
| Compare assessments across cycles | Step 4 | **MISSING** |
| Generate SAR document | Step 4 | **HAVE** — DocumentGenerationTool generates SAR from assessment data |
| Generate RAR document | Step 4 | **MISSING** — no risk assessment report |
| Create POA&M items for "Other Than Satisfied" controls | Step 4 | **PARTIAL** — Kanban tasks from findings, but not formal POA&M format |
| Verify evidence hasn't been tampered with | Step 4 | **MISSING** — no hash verification |
| Review non-compliance trends over time | Step 6 | **HAVE** — compliance trend/history tools |
| Audit log of who did what and when | All | **HAVE** — audit logging middleware |
| Read-only enforcement (cannot modify system) | All | **HAVE** — Auditor role is read-only |

### Persona 3: Platform Engineer / System Owner

**Real-world role**: Builds and operates the system being authorized. Responsible for actually implementing the controls and fixing the findings. Often has no security background — needs things explained in terms of their technology (Azure, Terraform, Kubernetes, etc.).

**Maps to**: `Compliance.PlatformEngineer` (existing), `Compliance.Analyst` (when acting as ISSO)

**What they do day-to-day**:
- Write IaC (Bicep/Terraform/ARM) to deploy infrastructure
- Implement security configurations per STIG requirements
- Respond to remediation tasks assigned by the ISSM
- Fix findings identified during assessment
- Collect evidence proving their fixes work
- Write control implementation narratives for the SSP (or help the ISSO do it)

**What they need from ATO Copilot**:

| Need | RMF Step | Status Today |
|------|----------|-------------|
| Understand what a NIST control means *for Azure* | Step 3 | **HAVE** — KnowledgeBase agent explains controls with Azure guidance |
| Understand what a STIG finding means *for my technology* | Step 3 | **PARTIAL** — 7 curated STIG entries, not the full library |
| Scan my IaC files for compliance before deploying | Step 3 | **HAVE** — IaC scanner (basic, 5 rules) |
| Get told exactly what code to change to fix a finding | Step 3 | **MISSING** — findings give recommendation text, not code |
| Get a fix suggestion as a VS Code Quick Fix / lightbulb | Step 3 | **MISSING** |
| See compliance issues as squiggly underlines in my editor | Step 3 | **MISSING** — results only in webview panel |
| Block non-compliant IaC in CI/CD before it merges | Step 3 | **MISSING** — no CI/CD integration |
| View my assigned remediation tasks | Step 4 | **HAVE** — Kanban tasks with assignment |
| Execute a remediation fix and validate it worked | Step 4 | **HAVE** — remediation + validation tools (execution simulated) |
| Collect evidence that my fix is in place | Step 4 | **HAVE** — evidence collection tool |
| Write a control implementation narrative for the SSP | Step 3 | **MISSING** — no per-control implementation authoring |
| Get a draft narrative suggested by AI | Step 3 | **MISSING** |

### Persona 4: AO (Authorizing Official) — *Limited Interaction*

**Real-world role**: Senior leader (O-6/GS-15+) who accepts risk and signs the ATO. Interacts with ATO Copilot infrequently but for the most consequential decisions.

**Maps to**: `Compliance.AuthorizingOfficial` (new — dedicated role with authorization-decision-only permissions, separated from `Compliance.Administrator` which handles copilot infrastructure)

**What they need from ATO Copilot**:

| Need | RMF Step | Status Today |
|------|----------|-------------|
| View authorization package summary (score, risk, findings) | Step 5 | **MISSING** — no package summary view |
| Issue authorization decision (ATO/ATOwC/IATT/DATO) | Step 5 | **MISSING** |
| Accept residual risk for specific findings with justification | Step 5 | **MISSING** — only alert dismiss |
| Set terms and conditions | Step 5 | **MISSING** |
| Set authorization expiration | Step 5 | **MISSING** |
| View risk register of accepted risks | Step 5/6 | **MISSING** |
| Receive escalated compliance alerts | Step 6 | **PARTIAL** — escalation configured but not delivered |

---

## Part 4: Gap Analysis — ATO Copilot vs. Real RMF

### What We Have That's Strong

| Capability | RMF Step | Quality |
|------------|----------|---------|
| NIST 800-53 Rev 5 full catalog (254K lines, OSCAL) | All | **Excellent** |
| Live Azure compliance scanning (Policy + Defender + ARM) | Step 4 | **Strong** |
| 10 per-family compliance scanners | Step 4 | **Strong** |
| Evidence collection from Azure APIs with SHA-256 hashing | Step 4 | **Strong** |
| Kanban remediation tracking (20 tools, full CRUD) | Step 4/6 | **Strong** |
| Compliance Watch monitoring with alerts (23 tools) | Step 6 | **Strong** |
| Document generation (SSP, SAR, POA&M in Markdown) | Step 3-5 | **Functional** |
| KnowledgeBase with NIST/STIG/RMF/FedRAMP education | All | **Good** |
| RBAC enforcement (Auditor read-only, role-based access) | All | **Good** |
| CAC session management (DB-backed) | All | **Good infra** |
| PIM role activation workflow (DB-persisted) | All | **Good infra** |
| Remediation engine (3-tier: AI + structured + ARM) | Step 3/4 | **Functional** |
| IaC compliance scanner | Step 3 | **Basic** (5 rules) |

### What's Missing by RMF Step

| RMF Step | What's Missing | Impact |
|----------|---------------|--------|
| **Step 0: Prepare** | System registration, authorization boundary definition, RMF role assignment, lifecycle state tracking | **Showstopper** — no organizing entity |
| **Step 1: Categorize** | FIPS 199 C/I/A categorization, CNSSI 1253 mapping, information type tagging, DoD IL calculation | **Showstopper** — baseline can't be determined |
| **Step 2: Select** | Control baseline management, tailoring with rationale, inheritance declarations, CRM generation, STIG-to-NIST mapping display | **Showstopper** — wrong controls assessed |
| **Step 3: Implement** | Per-control implementation narratives, SSP completeness tracking, AI-suggested narratives, IaC fix suggestions, inline diagnostics | **Major gap** — SSP substance missing |
| **Step 4: Assess** | Per-control effectiveness determination, CAT I/II/III severity mapping, immutable snapshots, assessment comparison, RAR generation, evidence chain of custody | **Moderate gap** — assessment works but artifacts incomplete |
| **Step 5: Authorize** | Authorization decision types (ATO/IATT/DATO), risk acceptance with expiration, terms & conditions, authorization package bundling | **Showstopper** — process can't conclude |
| **Step 6: Monitor** | ConMon plans, formal ConMon reports, significant change tracking, reauthorization triggers, ATO expiration tracking, POA&M milestones with due dates | **Moderate gap** — monitoring works but not RMF-structured |
| **Cross-cutting** | eMASS export, formatted PDF/DOCX output, notification delivery, CI/CD compliance gates, expanded STIG library, full CNSSI 1253 overlay data | **Important** — usability and DoD interop |

### Heat Map

```
Step 0 (Prepare):     ░░░░░░░░░░  0% — Nothing exists
Step 1 (Categorize):  ░░░░░░░░░░  0% — Nothing exists
Step 2 (Select):      ░░░░░░░░░░  5% — NIST catalog loaded but no baseline management
Step 3 (Implement):   ██░░░░░░░░ 25% — IaC scan + KB education, no SSP authoring
Step 4 (Assess):      ██████░░░░ 60% — Scanning + evidence + SAR, but no CAT, no snapshots
Step 5 (Authorize):   ░░░░░░░░░░  0% — Nothing exists
Step 6 (Monitor):     ████████░░ 80% — Watch + Kanban strong, no ConMon structure
```

**The bottom line**: ATO Copilot is strong at Steps 4 (Assess) and 6 (Monitor) — the parts most amenable to automation. But Steps 0–2 and 5 essentially don't exist, and these are the **structural steps** that frame the entire process. Without them, the system is a compliance scanner with Kanban, not an RMF copilot.

---

## Clarifications

### Session 2026-02-27

- Q: Is ATO Copilot single-deployment multi-system, per-system deployment, or hybrid? → A: Single deployment, multi-system. One MCP server + DB serves all registered systems. `RegisteredSystem` is a row. RBAC scoped per-system.
- Q: Do all controls in the tailored baseline require SSP implementation narratives, or only customer-responsible controls? → A: All controls require a narrative. Inherited controls get a standard inherited-control narrative auto-populated (referencing the CRM). Customer and Shared controls require human-authored narratives (AI can suggest drafts).
- Q: Can the copilot function without live Azure connectivity (air-gapped, disconnected)? → A: Azure is required for assessments. Air-gapped environments still have Azure (e.g., Azure Government IL5/IL6 isolated regions). The copilot needs configurable Azure environment profiles (endpoints, auth, proxy) to support air-gapped Azure deployments.
- Q: Should the AO (Authorizing Official) share the Administrator role, or get a dedicated role for separation of duties? → A: Add a dedicated `Compliance.AuthorizingOfficial` RBAC role. AO gets authorization-decision-only permissions (issue ATO, accept risk, set terms). `Administrator` retains copilot infrastructure management only. Enforces DoD separation of duties.
- Q: Should generated documents (SSP, SAR, etc.) follow exact DISA templates, ATO Copilot's own format, or support pluggable templates? → A: Both B and C. ATO Copilot ships a built-in compliant format covering all DoDI 8510.01 / NIST required sections as the default. Additionally, a pluggable template engine allows organizations to upload their own DOCX templates. At generation time, users choose: "ATO Copilot format" or a named custom template.

---

## Part 5: The Plan

### Principle: Follow the RMF Steps

Rather than organizing features by personas or by "what's cool," we organize by **RMF step** — because that's how the work actually flows. Each step has clear inputs, activities, outputs, and responsible personas. The copilot should guide users through these steps in order.

### Phase 1: RMF Foundation (Steps 0–2) — *"Register, Categorize, Select"*

**Goal**: Establish the system registration and control baseline — the organizing entity that everything else depends on.

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 1.1 | **System Registration** | ISSM | Register a system with name, type (per DoDI 8510.01: Major Application, Enclave, Platform IT), DoD IL, hosting environment, and Azure environment profile. Creates the anchor entity. |
| 1.2 | **Authorization Boundary** | ISSM | Define in-boundary resources (Azure resource IDs + types) and out-of-boundary resources with rationale (inherited, common control). This tells the scanner *what to scan*. |
| 1.3 | **RMF Role Assignment** | ISSM | Assign AO, ISSM, ISSO, SCA, System Owner to a system. Tracked for audit and access control. |
| 1.4 | **RMF Lifecycle State Machine** | ISSM | Track which step the system is in (Prepare → Categorize → Select → Implement → Assess → Authorize → Monitor) with gate conditions for advancement. |
| 1.5 | **FIPS 199 Categorization** | ISSM | Set Confidentiality/Integrity/Availability impact levels per information type. Calculate high-water mark. Map to DoD IL. Minimum 1 information type required. If categorization is changed after a ControlBaseline exists, the system warns that the baseline may need re-selection and the advance_rmf_step gate re-validation will apply. |
| 1.6 | **Control Baseline Selection** | ISSM | Automatically select the correct NIST 800-53 Rev 5 baseline based on categorization. Show total control count with breakdown. |
| 1.7 | **CNSSI 1253 Overlay Application** | ISSM | Apply DoD-specific overlay controls based on IL. Add new data source for full CNSSI 1253 overlay mapping. |
| 1.8 | **Control Tailoring** | ISSM | Add/remove controls with documented rationale. Warn on overlay-required controls. Audit-log all decisions. |
| 1.9 | **Control Inheritance** | ISSM | Mark controls as Inherited (CSP), Shared (split), or Customer. Inherited controls excluded from scanning. |
| 1.10 | **CRM Generation** | ISSM, SCA | Generate Customer Responsibility Matrix: inherited/shared/customer counts, rationale, STIG applicability. |
| 1.11 | **STIG Mapping Display** | ISSM, Engineer | Show DISA STIG IDs and CAT levels mapped to each NIST control in the baseline. |

**New entities**: `RegisteredSystem`, `AuthorizationBoundary`, `RmfRoleAssignment`, `SecurityCategorization`, `ControlBaseline`, `ControlTailoring`, `ControlInheritance`

**Deployment model**: Single MCP server + single database serving all registered systems. `RegisteredSystem` is a row in the shared DB and acts as the foreign key for all per-system data (categorization, baseline, assessments, authorizations, POA&M, evidence). RBAC is scoped per-system: an ISSO assigned to System A cannot modify System B's data. Per-system RBAC enforcement is implemented at the service layer: every tool invocation resolves the caller's `RmfRoleAssignment` records for the target `system_id` and validates the required role before proceeding. Tools without a `system_id` parameter (e.g., `list_systems`) filter results to systems where the caller has at least one role assignment (or all systems for `Compliance.Administrator`).

**Data additions**: CNSSI 1253 overlay mappings (new JSON data file)

**Azure Environment Profiles**: System registration includes an Azure environment configuration specifying:
- **Cloud environment**: Azure Commercial, Azure Government, Azure Government (Air-Gapped IL5), Azure Government (Air-Gapped IL6)
- **ARM endpoint** (e.g., `https://management.usgovcloudapi.net` vs air-gapped internal endpoint)
- **Authentication endpoint** (Entra ID government vs isolated)
- **Defender for Cloud endpoint**
- **Policy endpoint**
- **Proxy configuration** (if required for air-gapped egress)
- **Subscription ID(s)** within the boundary

All Azure SDK calls use the registered system's environment profile rather than a global default. This enables one ATO Copilot instance to manage systems across both connected and air-gapped Azure environments simultaneously.

### Phase 2: SSP Authoring & Engineer Experience (Step 3) — *"Implement and Document"*

**Goal**: Help Engineers implement controls and ISSOs document them in the SSP.

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 2.1 | **Per-Control Implementation Authoring** | ISSO, Engineer | Write/edit implementation narratives per control. Track status (Implemented/Partial/Planned/N-A). Show SSP completeness %. All controls in the tailored baseline require a narrative: inherited controls are auto-populated with a standard inherited-control statement referencing the CRM; customer and shared controls require human authoring (AI drafts available via 2.2). |
| 2.2 | **AI-Suggested Implementation Narratives** | ISSO, Engineer | KnowledgeBase agent suggests draft narratives based on system type, IL, and Azure implementation patterns. ISSO reviews and saves. Suggestions include a confidence score (0.0–1.0). Suggestions with confidence < 0.5 are flagged as "low confidence — requires significant review." All AI-suggested narratives are marked `AiSuggested = true` and require explicit human review (`ReviewedBy` + `ReviewedAt`) before counting toward SSP completeness. |
| 2.3 | **SSP Generation with Control Details** | ISSM | Generate formal SSP containing per-control implementation narratives, CRM, boundary description, interconnections, ports/protocols. User selects output template: **ATO Copilot format** (built-in, covers all DoDI 8510.01 / NIST SP 800-18 required sections) or a **custom organizational template** (uploaded DOCX with merge fields). |
| 2.4 | **IaC Suggested Fixes** | Engineer | IaC scanner findings include `suggestedFix` as unified diff. Language-specific (Bicep/HCL/ARM JSON). |
| 2.5 | **VS Code Inline Diagnostics** | Engineer | Findings as `Diagnostic` entries with squiggly underlines. CAT I/II → Error, CAT III → Warning. Hover shows control + STIG info. |
| 2.6 | **VS Code Quick Fix (Code Actions)** | Engineer | Lightbulb Code Actions to apply suggested fixes. "Apply All Fixes" for non-conflicting changes. |
| 2.7 | **Expanded STIG Library** | All | Load the full DISA STIG catalog (or top 200 rules by frequency) instead of the current 7 entries. |
| 2.8 | **IaC Scanner Rule Expansion** | Engineer | Expand from 5 rules to 50+ rules covering the most common STIG/NIST findings for Azure IaC. |

**New entities**: `ControlImplementation`

**Enriched output** (not a new entity): IaC scan findings gain `suggestedFix` (unified diff), `stigRuleId`, and `catLevel` fields on existing finding results.

### Phase 3: Assessment Artifacts & Authorization (Steps 4–5) — *"Assess, Decide, Authorize"*

**Goal**: Complete the assessment workflow and enable the AO to make a formal authorization decision.

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 3.1 | **DoD CAT Severity Mapping** | SCA | Map finding severities to DoD CAT levels: CAT I (Critical/High — direct loss of C/I/A), CAT II (Medium — potential loss), CAT III (Low — degrades). |
| 3.2 | **Per-Control Effectiveness Determination** | SCA | Mark each assessed control as Satisfied / Other Than Satisfied with evidence links and notes. |
| 3.3 | **Immutable Assessment Snapshots** | SCA | Take a frozen snapshot of assessment state with SHA-256 integrity hash. Cannot be modified. |
| 3.4 | **Assessment Comparison (Diff)** | SCA | Compare two snapshots side-by-side: controls that changed, score delta, new/resolved findings. |
| 3.5 | **Evidence Chain of Custody** | SCA | Enrich evidence with collector identity, collection method, access log, tamper detection (hash recompute). |
| 3.6 | **Evidence Completeness Check** | SCA, ISSM | Report which controls have verified evidence vs. missing evidence. Show completeness %. |
| 3.7 | **RAR Generation** | SCA | Generate Risk Assessment Report: residual risk by family, aggregate risk level, threat/vuln analysis. Supports ATO Copilot format or custom template. |
| 3.8 | **Formal POA&M with DoD Fields** | ISSM | POA&M with DoD-required columns: weakness source, severity (CAT), POC, resources required, cost estimate, milestones with dates. |
| 3.9 | **POA&M Milestone Tracking** | ISSM | Milestones with target dates, auto-overdue detection, alert generation, Kanban task linkage. |
| 3.10 | **Authorization Decision Workflow** | AO | Issue ATO/ATOwC/IATT/DATO with terms, conditions, expiration, residual risk acceptance. Requires `Compliance.AuthorizingOfficial` role (not `Administrator`). |
| 3.11 | **Risk Acceptance with Expiration** | AO | Accept risk per-finding with justification, compensating control, expiration date. Auto-expire and revert: when a risk acceptance expires, `RiskAcceptance.IsActive` is set to `false`, the associated finding's CAT severity is restored to active status, any linked POA&M item reverts from `RiskAccepted` to `Ongoing`, and an alert is sent to both the AO and ISSM. Compensating controls are flagged for re-evaluation. Requires `Compliance.AuthorizingOfficial` role. |
| 3.12 | **Risk Register** | All | View all active/expired/revoked acceptances. Read-only for SCA. |
| 3.13 | **Authorization Package Bundling** | ISSM | Generate complete package (SSP + SAR + RAR + POA&M + CRM + ATO Letter) as ZIP. Export is blocked unless the system has an active, unexpired AO authorization decision. The package includes the identity-bound AO decision, decision timestamp, expiration, residual-risk justification, and terms or conditions. Each document is rendered using the system's selected template (ATO Copilot default or custom organizational template). |

**New entities**: `AssessmentRecord`, `ControlEffectiveness`, `AuthorizationDecision`, `RiskAcceptance`, `PoamItem` (enriched)

**Enriched entities**: `ComplianceSnapshot` (add SHA-256 hash, immutability flag), `ComplianceFinding` (add `CatSeverity`), `ComplianceEvidence` (add chain-of-custody fields)

**Computed views** (not stored entities): Risk Register is an aggregated view over `RiskAcceptance` records filtered by status.

**RBAC change**: New `Compliance.AuthorizingOfficial` role added (total: 7 roles). Authorization tools (3.10, 3.11, 3.12 write) restricted to this role. `Compliance.Administrator` loses authorization decision permissions and retains only copilot infrastructure management.

### Phase 4: Continuous Monitoring & Lifecycle (Step 6) — *"Stay Authorized"*

**Goal**: Structure the existing monitoring capabilities into the RMF ConMon framework and close the lifecycle loop.

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 4.1 | **ConMon Plan** | ISSM | Create a formal ConMon plan: assessment frequency, annual review date, significant change triggers, reporting distribution. |
| 4.2 | **ConMon Reports** | ISSM, SCA | Generate monthly/quarterly reports: score vs. authorized baseline, trend, new/resolved findings, POA&M progress, upcoming deadlines. |
| 4.3 | **ATO Expiration Tracking** | ISSM, AO | Track authorization expiration with graduated alerts at 90/60/30 days. Dashboard shows all systems with status and expiration. When an ATO expires without reauthorization: `AuthorizationDecision.IsActive` is set to `false`, a persistent "Authorization Expired" alert is generated, the multi-system dashboard shows "EXPIRED" status, and ConMon reports flag the system as operating without authorization. The system does NOT automatically enter read-only mode (unlike DATO) — but the ISSM and AO receive daily escalating alerts until a reauthorization cycle is initiated or a new authorization decision is issued. |
| 4.4 | **Significant Change Detection** | ISSM | Report and assess significant changes (new interconnection, major upgrade, data type change). Flag for reauthorization when applicable. |
| 4.5 | **Reauthorization Workflow** | ISSM | When reauth needed (expiration or significant change), guide the user back through Steps 4–5 with previous assessment as baseline. |
| 4.6 | **Multi-System Dashboard** | ISSM, AO | View all registered systems with: name, IL, current RMF step, authorization status, expiration, compliance score, alert count. Queries the single shared database across all `RegisteredSystem` rows the user has access to (per RBAC). |
| 4.7 | **Notification Delivery** | All | Actually deliver notifications via Teams (Adaptive Card), VS Code (information message), or both. CAT I findings override quiet hours. |

**New entities**: `ConMonPlan`, `ConMonReport`, `SignificantChange`

### Phase 5: Interoperability & Production Readiness — *"Connect to the Real World"*

**Goal**: Bridge ATO Copilot to the DoD ecosystem and make it production-grade.

| # | Capability | Persona | Description |
|---|-----------|---------|-------------|
| 5.1 | **Formatted Document Export (PDF/DOCX)** | ISSM, SCA | Export all artifacts as formatted PDF/DOCX. Two template modes: **(1) ATO Copilot format** — built-in compliant format covering all DoDI 8510.01 required sections (per NIST SP 800-18 for SSP, NIST SP 800-53A for SAR, etc.); not locked to a specific DISA template revision. **(2) Custom organizational template** — organizations upload their own DOCX templates with merge fields (mail-merge style); the template engine injects data into placeholders. Templates are managed per-organization via the Configuration Agent. Users choose template at generation time. |
| 5.1a | **Template Management** | Administrator | Upload, list, update, and delete custom DOCX templates. Each template declares which document type it applies to (SSP, SAR, RAR, POA&M, CRM, ConMon Report, ATO Letter). Validate merge fields on upload. |
| 5.2 | **eMASS Data Exchange** | ISSM | Export to eMASS-compatible Excel (xlsx) via ClosedXML. Import eMASS Excel data with conflict resolution (skip/overwrite/merge). Dry-run mode for preview. |
| 5.3 | **CI/CD Compliance Gate** | Engineer | GitHub Actions action that scans IaC in PRs. Blocks on CAT I/II. Respects risk acceptances. |
| 5.4 | **Real PIM Integration** | ISSM | Replace hardcoded eligible roles with Entra ID PIM API (Microsoft Graph). |
| 5.5 | **Real Script Execution** | Engineer | Replace `Task.Delay(100ms)` with actual az CLI / PowerShell subprocess execution. |
| 5.6 | **Full STIG Expansion** | All | Expand STIG library from ~200 priority rules (loaded in Phase 2, §2.7) to ~880 rules covering all common DoD technologies. Add CCI→NIST mapping (~7,575 entries). See §2.7 for initial expansion scope. |

---

## Part 6: What This Changes About the Copilot

### Agent Model

The current 3-agent model (Compliance, KnowledgeBase, Configuration) still works, but the **Compliance Agent needs to become RMF-step-aware**:

| Agent | New Responsibility |
|-------|-------------------|
| **Compliance Agent** | All RMF operational tools — but now organized by RMF step. The agent uses the system's current RMF step as context to prioritize relevant tools. If a system is in Step 2 (Select), control tailoring and inheritance tools are surfaced first. |
| **KnowledgeBase Agent** | Unchanged — explains controls, STIGs, RMF, FedRAMP. But now also suggests implementation narratives (Phase 2). |
| **Configuration Agent** | Unchanged — manages copilot connection settings. |

### Tool Organization

Instead of a flat list of 80+ tools, tools should be **grouped by RMF step** in the prompt/routing logic:

```
Step 0 (Prepare):
  - register_system, define_boundary, assign_roles, list_systems, show_registration

Step 1 (Categorize):
  - categorize_system, show_categorization, update_categorization

Step 2 (Select):
  - show_baseline, set_inheritance, tailor_control, show_crm, show_stig_mapping

Step 3 (Implement):
  - document_implementation, show_implementation_status, suggest_implementation
  - generate_ssp, scan_iac (existing)

Step 4 (Assess):
  - start_assessment, assess_control, take_snapshot, compare_snapshots
  - verify_evidence, check_evidence_completeness, generate_sar, generate_rar
  - run_assessment (existing), collect_evidence (existing)

Step 5 (Authorize):
  - show_auth_package, issue_authorization, accept_risk, show_risk_register

Step 6 (Monitor):
  - create_conmon_plan, generate_conmon_report, report_significant_change
  - track_ato_expiration, show_poam_status
  - watch_* (existing 23 tools), kanban_* (existing 20 tools)
```

### What the User Experience Looks Like

**ISSM in Teams asks**: `Show me where we are with ACME Portal`

The copilot responds with an Adaptive Card showing:
- System: ACME Portal | IL5 | Azure Government
- RMF Step: **Step 4 — Assess** (in progress)
- Compliance Score: 78% (target: 85%)
- SSP Completeness: 92% (301/325 controls documented)
- Assessment: ASM-003 in progress by SCA Martinez
- Open Findings: 12 (2 CAT I, 4 CAT II, 6 CAT III)
- POA&M Items: 8 (2 overdue)
- ATO Expiration: N/A (not yet authorized)
- Next Action: Complete assessment, generate SAR

**Engineer in VS Code asks**: `@ato /compliance What STIG findings do I need to fix?`

The copilot responds with their assigned Kanban tasks, each showing the STIG Rule ID, CAT level, affected resource, and a "See Fix" link that shows the suggested code change.

**SCA in Teams asks**: `Take a snapshot of the ACME Portal assessment`

The copilot creates an immutable snapshot and responds with an Adaptive Card: Snapshot ID, timestamp, score, control count, evidence count, SHA-256 hash.

---

## Part 7: Build Sequence & Rationale

### Why This Order?

1. **Phase 1 first** because you cannot do any RMF step correctly without knowing *which system*, *what IL*, and *which controls*. Every tool we built in the past (assessment, remediation, monitoring) is scanning "a subscription" — not "a system going through RMF." The system registration entity is the foreign key for everything.

2. **Phase 2 second** because once you have a system and baseline, the biggest user pain is documenting implementations in the SSP and fixing STIG findings. This is where engineers and ISSOs spend 60% of their time.

3. **Phase 3 third** because assessment and authorization are the culmination — you can't build them well until the upstream data (SSP, baseline, categorization) exists for real.

4. **Phase 4 fourth** because ConMon happens after ATO. We already have strong monitoring tools; this phase structures them into the RMF lifecycle.

5. **Phase 5 last** because interop (eMASS, PDF, CI/CD) is usability polish — important but not structural.

### Rough Timeline Estimate

| Phase | Scope | Estimate |
|-------|-------|----------|
| Phase 1 | 11 capabilities, ~6 new entities, data additions | 3–4 weeks |
| Phase 2 | 8 capabilities, 2 new entities, scanner expansion | 2–3 weeks |
| Phase 3 | 13 capabilities, ~7 new entities, document generation | 4–5 weeks |
| Phase 4 | 7 capabilities, 3 new entities | 2–3 weeks |
| Phase 5 | 6 capabilities, production integrations | 3–4 weeks |
| **Total** | **46 capabilities** | **14–19 weeks** |

---

## Part 8: Documentation Deliverables

Each phase must ship with documentation. The copilot is a complex, multi-surface product operating in a compliance-critical domain — undocumented features are unusable features. Documentation is a **first-class deliverable**, not an afterthought.

### 8.1 Architecture & Design Documentation

| Document | Location | Contents | Updated When |
|----------|----------|----------|-------------|
| **Architecture Overview** | `docs/architecture/overview.md` | High-level system architecture, component diagram (MCP Server → Agents → Tools → Azure), data flow, transport protocols (SSE, stdio, REST), deployment topology (Docker containers, ports) | Phase 1 and on every structural change |
| **Data Model Reference** | `docs/architecture/data-model.md` | All EF Core entities with relationships, field descriptions, constraints, and an ER diagram (Mermaid). Updated as new entities land per phase. | Every phase |
| **Agent & Tool Catalog** | `docs/architecture/agent-tool-catalog.md` | Every agent and every tool: name, description, parameters, return type, required RBAC role, RMF step mapping. Auto-generated from code attributes where possible. | Every phase |
| **RMF Step Map** | `docs/architecture/rmf-step-map.md` | Matrix of RMF steps × tools × personas × artifacts. The single-page reference showing "what exists and where it fits." | Every phase |
| **Security Architecture** | `docs/architecture/security.md` | RBAC model (roles, permissions, enforcement points), CAC/session management, PIM integration, audit logging, data protection (encryption at rest/in transit), and how ATO Copilot's own security posture aligns with the controls it assesses. | Phase 1 and Phase 5 |

### 8.2 User & Operator Guides

| Document | Location | Audience | Contents |
|----------|----------|----------|----------|
| **ISSM User Guide** | `docs/guides/issm-guide.md` | ISSM | End-to-end walkthrough of the RMF lifecycle using ATO Copilot: registering a system, categorizing, selecting/tailoring baselines, tracking SSP completeness, reviewing assessments, preparing authorization packages, managing ConMon. Written as a step-by-step workflow with screenshots/Adaptive Card examples. |
| **SCA / Auditor Guide** | `docs/guides/sca-guide.md` | SCA | How to perform an independent assessment: reviewing SSP claims, running assessments, marking per-control effectiveness, collecting/verifying evidence, generating SAR/RAR, taking snapshots, comparing assessment cycles. Emphasizes read-only enforcement and evidence integrity. |
| **Engineer Guide** | `docs/guides/engineer-guide.md` | Platform Engineer | Using `@ato` in VS Code: slash commands, IaC scanning, inline diagnostics, Quick Fix actions, understanding STIG findings, viewing/completing remediation tasks, collecting evidence, writing implementation narratives. |
| **AO Quick Reference** | `docs/guides/ao-quick-reference.md` | AO | One-page guide: viewing authorization package summary, issuing decisions (ATO/ATOwC/IATT/DATO), accepting risk, setting terms/conditions/expiration. Deliberately brief — AOs have limited time. |
| **Teams Bot Guide** | `docs/guides/teams-bot-guide.md` | All Teams users | Installing the bot, available commands, Adaptive Card interactions, notification preferences, approval workflows. |
| **Deployment & Operations Guide** | `docs/guides/deployment.md` | DevOps / Operators | Docker deployment, environment variables, database setup (EF Core migrations), Azure service principal configuration, SSL/TLS, health checks, scaling, backup/restore, upgrading between versions. |

### 8.3 API & Integration Documentation

| Document | Location | Contents |
|----------|----------|----------|
| **MCP Server API Reference** | `docs/api/mcp-server.md` | All MCP tools as an API reference: request/response schemas (JSON), transport options (SSE, stdio, HTTP), authentication, error codes, rate limits. Grouped by RMF step. |
| **eMASS Integration Guide** | `docs/api/emass-integration.md` | Export formats (Excel xlsx field mappings), import workflow, conflict resolution, delta sync behavior, field mapping table (ATO Copilot field → eMASS field). *(Phase 5)* |
| **CI/CD Integration Guide** | `docs/api/cicd-integration.md` | GitHub Actions usage: action YAML, inputs/outputs, IaC scanning in PRs, blocking on CAT I/II, respecting risk acceptances, badge generation. *(Phase 5)* |
| **VS Code Extension API** | `docs/api/vscode-extension.md` | Chat participant commands, contributed diagnostics, Code Action providers, webview panel messages, configuration settings, extension activation events. |

### 8.4 Reference Data Documentation

| Document | Location | Contents |
|----------|----------|----------|
| **NIST 800-53 Rev 5 Coverage** | `docs/reference/nist-coverage.md` | Which controls from the catalog ATO Copilot can actively scan/assess vs. which require manual attestation. Coverage percentage by family. Updated as scanner rules expand. |
| **STIG Coverage Matrix** | `docs/reference/stig-coverage.md` | STIG IDs included in the library, mapped to NIST controls, with CAT levels and applicability by technology (Azure, Windows Server, SQL, etc.). |
| **DoD Impact Level Reference** | `docs/reference/impact-levels.md` | IL2–IL6 definitions, Azure environment requirements, control implications, CNSSI 1253 overlay summary. Sourced from existing `impact-levels.json` but presented in human-readable form. |
| **RMF Process Reference** | `docs/reference/rmf-process.md` | The 7-step RMF process with DoD-specific guidance, service-specific variations (Navy/Army/Air Force), artifact checklists per step, and role responsibilities. Sourced from existing `rmf-process.json` + DoDI 8510.01. |
| **Glossary** | `docs/reference/glossary.md` | All DoD/RMF/NIST acronyms and terms used in the product: AO, ATO, ATOwC, CAC, CAT, CNSSI, ConMon, CRM, DATO, DISA, eMASS, FIPS, IATT, IL, ISSM, ISSO, NIST, OSCAL, PIV, PIM, POA&M, RAR, RMF, SAP, SAR, SCA, SCAP, SSP, STIG, and more. |

### 8.5 Developer Documentation

| Document | Location | Contents |
|----------|----------|----------|
| **Contributing Guide** | `docs/dev/contributing.md` | How to add a new tool (agent registration, RBAC attribute, tool class, unit test, integration test), how to add a new entity (EF Core migration), how to add a new Adaptive Card, how to add reference data. |
| **Testing Guide** | `docs/dev/testing.md` | Test project structure, naming conventions, mock patterns (Azure SDK mocks, DB context mocks), running tests locally, coverage requirements, how to write tests for each layer (tool → agent → MCP → extension). |
| **Code Style & Conventions** | `docs/dev/code-style.md` | C# conventions (nullable reference types, async patterns, result types), TypeScript conventions, naming conventions for tools/agents/entities/tests, folder structure rules. |
| **Release & Versioning** | `docs/dev/release.md` | Versioning strategy (SemVer), changelog format, Docker image tagging, extension marketplace publishing, migration strategy for breaking changes. |

### 8.6 Documentation Delivery Rules

1. **No phase ships without docs**: Each phase's PR includes documentation updates for every new capability. This is a merge-blocking requirement.
2. **Persona guides are scenario-driven**: Written as "How do I..." walkthroughs, not API references. Use the copilot's own Adaptive Cards and VS Code screenshots as illustrations.
3. **Auto-generation where possible**: The Agent & Tool Catalog and MCP API Reference should be generated from code attributes/XML doc comments to stay in sync. Manual docs supplement, not duplicate.
4. **Single source of truth**: Reference data docs (NIST coverage, STIG coverage, IL reference) are generated from the actual JSON data files embedded in the application. If the data changes, the docs regenerate.
5. **Glossary-first onboarding**: New team members and users start with the Glossary and RMF Process Reference before touching any guide. These two documents are the prerequisite.
6. **Changelog per phase**: Each phase produces a `CHANGELOG.md` entry summarizing new capabilities, new entities, new tools, breaking changes, and known limitations.

---

## Part 9: What We're NOT Building

To keep scope focused, the following are explicitly out of scope (documentation deliverables are covered in Part 8 above):

| Out of Scope | Reason |
|-------------|--------|
| **Replacing eMASS** | eMASS is the DoD system of record. We feed it, not replace it. |
| **SCAP/ACAS scanner** | We integrate with Azure native tools. SCAP/ACAS are separate scanner products. |
| **GRC platform capabilities** (workflow designer, policy authoring, risk register as standalone product) | We're a copilot, not Archer or ServiceNow GRC. |
| **Classified network deployment** | IL6 and above require SIPR infrastructure we can't simulate. |
| **CAC hardware integration** | Smart card readers require OS-level drivers; out of scope for a chat copilot. |
| **Multi-tenant SaaS** | Single-tenant deployment model. Multi-tenant is a separate product decision. |
| **Real-time SIEM integration** | We monitor compliance, not security events. Defender for Cloud is our data source. |

---

## Part 9a: Post-Implementation Integration — Monitoring & Alert Pipeline

**Added**: 2026-02-28 | **Trigger**: Post-Phase 16 analysis identified 3 CRITICAL + 2 HIGH integration gaps between services built across Features 005 and 015.

### Background

Three monitoring services and two alert services were built in separate features and operate in isolation despite overlapping responsibilities:

| Service | Scope Unit | Feature | Lifetime | Purpose |
|---------|-----------|---------|----------|---------|
| `ComplianceMonitoringService` | Subscription | Pre-015 | Singleton | Ad-hoc compliance status, trends, history, audit |
| `ComplianceWatchService` | Subscription | 005 | Singleton | Automated monitoring, drift baselines, alert creation, auto-remediation |
| `ConMonService` | System (RMF) | 015 | Scoped | ConMon plans, formal reports, expiration, significant changes |
| `AlertManager` | Subscription | 005 | Singleton | Alert CRUD, status transitions, SLA deadlines |
| `AlertNotificationService` | Alert | 005 | Singleton | Notification dispatch (chat/email/webhook), rate limiting, digests |

### Problem Statement

1. **ConMon reports are incomplete**: `ConMonService.GenerateReportAsync()` queries `ControlEffectivenessRecords`, `AuthorizationDecisions`, and `PoamItems` directly from the database but never consults `IComplianceWatchService` or `IComplianceMonitoringService`. This means ConMon reports exclude: monitoring status, drift alerts, auto-remediation activity, and live Azure scan results.

2. **Scope mismatch**: Watch/Alert services operate on `subscriptionId`. ConMon operates on `systemId`. `ComplianceAlert` has no `RegisteredSystemId` FK. The `RegisteredSystem.AzureEnvironmentProfile.SubscriptionIds` field exists but no service bridges system IDs to subscription IDs for alert filtering.

3. **Silent alert creation**: `AlertManager.CreateAlertAsync()` persists alerts but does NOT call `AlertNotificationService`. `ComplianceWatchService` calls `CreateAlertAsync()` 4 times (drift, violations, auto-remediation, manual) but has no `IAlertNotificationService` dependency. Result: alerts are created silently — users only learn about them when they proactively query, or after SLA violation triggers `EscalationHostedService`.

4. **ConMon notifications are stub-only**: The `compliance_send_notification` tool returns status data via MCP response with an explicit TODO comment: *"Notification delivery is currently MCP-side only."* Spec §4.7 requires actual delivery via Teams/VS Code. Expiration warnings and significant change events are not pushed to users.

5. **No automatic significant change detection**: `ConMonService.ReportChangeAsync()` requires manual invocation. `ComplianceWatchService` has drift detection that discovers configuration changes, but drift alerts are never forwarded to the ConMon significant change pipeline.

### Requirements

#### 9a.1 System-to-Subscription Bridge

- Add nullable `RegisteredSystemId` FK to `ComplianceAlert` entity (backward-compatible: null for pre-Feature 015 alerts).
- When `ComplianceWatchService` creates alerts for a subscription, resolve the subscription's owning `RegisteredSystem` (lookup via `AzureEnvironmentProfile.SubscriptionIds`) and populate the FK.
- `ConMonService` report generation and dashboard queries should filter Watch/Alert data using this FK.

#### 9a.2 Alert → Notification Pipeline

- After `AlertManager.CreateAlertAsync()` persists a new alert, call `IAlertNotificationService.SendNotificationAsync()` to deliver immediate notifications. The notification service's existing quiet-hours suppression and rate limiting apply.
- This makes notification delivery automatic for ALL alert sources (Watch drift, Watch violations, Watch auto-remediation, manual alerts).
- `IAlertNotificationService` is injected as an optional dependency on `AlertManager` (backward-compatible — null skips notification).

#### 9a.3 ConMon Report Enrichment

- Inject `IComplianceWatchService` into `ConMonService`.
- `GenerateReportAsync()` enriches reports with:
  - Whether continuous monitoring is enabled for the system's subscriptions
  - Count of active drift alerts
  - Auto-remediation rule count and activity summary
  - Monitoring frequency and last check timestamp
- Inject `IComplianceMonitoringService` into `ConMonService` to include live Azure scan status (compliance score from Azure Policy, Defender findings) alongside assessment-derived scores.

#### 9a.4 ConMon Event → Alert Pipeline

- When `ConMonService.CheckExpirationAsync()` detects an alert level of "Warning", "Urgent", or "Expired", auto-create a `ComplianceAlert` via `IAlertManager`:
  - Type: `ComplianceExpiration`
  - Severity: Info (90 days), Warning (60 days), High (30 days), Critical (expired)
  - The AlertManager → AlertNotificationService pipeline handles delivery.
- When `ConMonService.ReportChangeAsync()` records a significant change that `RequiresReauthorization`, auto-create a `ComplianceAlert`:
  - Type: `SignificantChange`
  - Severity: High
  - Includes change description and reauthorization flag.

#### 9a.5 Watch Drift → Significant Change Auto-Detection

- When `ComplianceWatchService.DetectDriftAsync()` detects drift on resources belonging to a registered system, AND the number of drifted resources exceeds a configurable threshold (default: 5), automatically call `IConMonService.ReportChangeAsync()` with `changeType = "configuration_drift"`.
- This creates a `SignificantChange` record and triggers the reauthorization evaluation.
- Threshold is configurable via `MonitoringOptions.SignificantDriftThreshold`.

#### 9a.6 Service Responsibility Clarification

- Document the distinction between `ComplianceMonitoringService` (ad-hoc status API) and `ComplianceWatchService` (automated continuous monitoring engine). Both are valid but serve different use cases.
- Add XML doc comments to each service class clarifying scope and relationship.

### What This Changes

| Component | Change |
|-----------|--------|
| `ComplianceAlert` entity | Add nullable `RegisteredSystemId` FK |
| `AlertManager` | Add optional `IAlertNotificationService` constructor dependency; call after `CreateAlertAsync` |
| `ComplianceWatchService` | Resolve `RegisteredSystemId` when creating alerts for mapped subscriptions |
| `ConMonService` | Inject `IComplianceWatchService`, `IComplianceMonitoringService`, `IAlertManager`; enrich reports; auto-create alerts for expiration/changes |
| `ConMonTools` (`compliance_send_notification`) | Replace stub with real alert creation + notification pipeline |
| `MonitoringOptions` | Add `SignificantDriftThreshold` (default: 5) |
| DI registration | Update constructor wirings in `ServiceCollectionExtensions.cs` |
| EF Core migration | Add FK to `ComplianceAlert` |

---

## Part 10: Summary

**ATO Copilot's identity**: An AI assistant that guides DoD teams through the 7-step RMF process, combining real Azure compliance scanning with workflow automation and document generation.

**The core gap**: Steps 0 (Prepare), 1 (Categorize), 2 (Select), and 5 (Authorize) don't exist at all. This means the copilot can scan and monitor, but it can't frame that work within the RMF lifecycle that DoD requires.

**The fix**: Build the RMF lifecycle as the backbone (system → categorization → baseline → implementation → assessment → authorization → monitoring), then every existing tool (scanning, Kanban, Watch, KB, IaC) snaps into its proper step.

**The three personas** (ISSM, SCA, Engineer) each have clear workflows at each step, with proper RBAC enforcement already in place. The persona model maps cleanly to existing roles.

**The build order** follows the RMF steps themselves — foundation first, then implementation, then assessment/authorization, then monitoring, then interop.
