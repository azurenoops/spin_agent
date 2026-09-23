# Feature 016: Comprehensive User & Persona Documentation

**Created**: 2026-02-28
**Status**: Feature Specification
**Purpose**: Provide all-encompassing user documentation for Security Posture Intelligence Navigator organized by persona, covering natural language interaction patterns, RMF phase workflows, document production, assessment guidance, and cross-persona collaboration.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Persona Definitions](#2-persona-definitions)
3. [Persona 1: ISSM — Information System Security Manager](#3-persona-1-issm)
4. [Persona 2: ISSO — Information System Security Officer](#4-persona-2-isso)
5. [Persona 3: SCA — Security Control Assessor](#5-persona-3-sca)
6. [Persona 4: AO — Authorizing Official](#6-persona-4-ao)
7. [Persona 5: Platform Engineer / System Owner](#7-persona-5-platform-engineer)
8. [Persona 6: Administrator](#8-persona-6-administrator)
9. [Cross-Persona Collaboration Matrix](#9-cross-persona-collaboration-matrix)
10. [RMF Phase Reference](#10-rmf-phase-reference)
11. [Document Production Catalog](#11-document-production-catalog)
12. [Natural Language Query Reference](#12-natural-language-query-reference)
13. [Interface Guide](#13-interface-guide)
14. [Appendices](#14-appendices)

---

## 1. Overview

### 1.1 What Is Security Posture Intelligence Navigator?

Security Posture Intelligence Navigator is an AI-powered assistant that guides DoD teams through every step of the NIST Risk Management Framework (RMF) — from system registration through continuous monitoring. It combines real Azure compliance scanning with RMF workflow automation, natural language interaction, and document generation.

**Security Posture Intelligence Navigator is NOT:**
- A replacement for eMASS (it exports *to* eMASS)
- A GRC platform (it is a productivity copilot)
- A vulnerability scanner (it orchestrates Azure Policy + Defender for Cloud)

**Security Posture Intelligence Navigator IS:**
- A copilot that knows the RMF process and guides users step by step
- An assistant that has the full NIST 800-53 Rev 5 catalog embedded (1,000+ controls)
- A scanner that queries real Azure infrastructure via Policy, Defender, and ARM APIs
- A document generator that produces SSP, SAR, RAR, POA&M, and CRM from actual assessment data
- A remediation tracker with Kanban boards linked to compliance findings
- A continuous monitor that detects compliance drift and creates graduated alerts
- A natural language interface where each persona sees information tailored to their role

### 1.2 Supported Interfaces

| Surface | Primary Users | How to Access |
|---------|--------------|---------------|
| **VS Code (GitHub Copilot Chat)** | Engineers, ISSOs | `@ato` participant with `/compliance`, `/knowledge`, `/config` slash commands |
| **Microsoft Teams (M365 Bot)** | ISSMs, AOs, SCAs | Adaptive Cards for dashboards, assessments, approvals |
| **MCP Server API** | All (via any MCP client) | REST + SSE + stdio transport — powers all surfaces above |
| **CLI** | Engineers, ISSOs | Direct MCP tool invocations for scripting and automation |

### 1.3 Applicable Standards

| Standard | How Security Posture Intelligence Navigator Uses It |
|----------|------------------------|
| DoDI 8510.01 | Defines the 7-step RMF lifecycle Security Posture Intelligence Navigator implements |
| NIST SP 800-37 Rev 2 | RMF framework including Step 0 (Prepare) |
| NIST SP 800-53 Rev 5 | Full control catalog embedded (254K lines, sourced from OSCAL) |
| NIST SP 800-60 Vol 1 & 2 | Information type catalog for FIPS 199 categorization |
| FIPS 199 | Security categorization (C/I/A impact → Low/Moderate/High) |
| CNSSI 1253 | DoD overlay controls mapped to Impact Levels (IL2–IL6) |
| DISA STIGs | Technology-specific security configuration rules |
| NIST SP 800-18 | SSP structure and required sections |
| NIST SP 800-53A | Assessment procedures referenced by SAR generation |

### 1.4 RBAC Roles

Security Posture Intelligence Navigator enforces role-based access control at every tool invocation. A user's role determines what they can do:

| RBAC Role | Maps To | Access Level |
|-----------|---------|-------------|
| `Compliance.Administrator` | Administrator | Full infrastructure management, all tools |
| `Compliance.SecurityLead` | ISSM | System registration, categorization, SSP, POA&M, ConMon, dashboards |
| `Compliance.Analyst` | ISSO | Control narratives, evidence collection, remediation execution, monitoring |
| `Compliance.Auditor` | SCA | Assessment, effectiveness determination, SAR/RAR generation (read-only enforcement — cannot modify system data) |
| `Compliance.AuthorizingOfficial` | AO | Authorization decisions, risk acceptance, risk register (authorization-only permissions) |
| `Compliance.PlatformEngineer` | Engineer | IaC scanning, STIG lookups, remediation tasks, evidence collection, narrative authoring |
| `Compliance.Viewer` | Stakeholder | Read-only access to dashboards and reports |

**Role Resolution**: CAC certificate → 4-tier chain: (1) explicit mapping by thumbprint, (2) Azure AD group membership, (3) Azure RBAC on subscription, (4) default to PlatformEngineer.

---

## Clarifications

### Session 2026-02-28

- Q: What is the primary documentation deliverable format? → A: Static documentation site (MkDocs or DocFX) generated from Markdown files in `docs/`.
- Q: Should the documentation cover multi-system portfolio workflows? → A: Yes — include a dedicated "Portfolio Management" section covering multi-system workflows, bulk operations, and delegation.
- Q: How should air-gapped/disconnected environment differences be documented? → A: Inline callouts within persona sections noting behavior differences per tool.
- Q: Should the documentation include onboarding/getting-started guidance? → A: Yes — per-persona "Getting Started" subsection within each persona chapter (first-time setup, prerequisites, first 3 commands).
- Q: Should error handling and troubleshooting be documented? → A: Yes — appendix organized by error category (RBAC, gates, connectivity, evidence, expiration) with resolution steps.

---

## 2. Persona Definitions

### 2.1 RMF Role Assignments

Each registered system has named personnel in specific RMF roles. These are tracked by Security Posture Intelligence Navigator and linked to the RBAC system:

| RMF Role | RBAC Mapping | Description |
|----------|-------------|-------------|
| **AuthorizingOfficial** | `Compliance.AuthorizingOfficial` | Senior official who accepts risk and grants ATO |
| **ISSM** | `Compliance.SecurityLead` | Manages the security program for assigned systems |
| **ISSO** | `Compliance.Analyst` | Implements and monitors security controls day-to-day |
| **SCA** | `Compliance.Auditor` | Independent assessor of security control effectiveness |
| **SystemOwner** | `Compliance.PlatformEngineer` | Program manager/engineer responsible for the system |

### 2.2 Persona–RMF Phase Responsibility Matrix

| RMF Phase | ISSM | ISSO | SCA | AO | Engineer |
|-----------|------|------|-----|-----|----------|
| **Prepare** | **Lead** | Support | — | Informed | Support |
| **Categorize** | **Lead** | Support | — | Informed | Consulted |
| **Select** | **Lead** | Support | Review | — | Consulted |
| **Implement** | Oversight | **Lead** | — | — | **Lead** |
| **Assess** | Support | Support | **Lead** | Informed | Support |
| **Authorize** | Package prep | Support | SAR delivery | **Decide** | — |
| **Monitor** | **Lead** | **Day-to-day** | Periodic assess | Escalation | Remediation |

---

## 3. Persona 1: ISSM — Information System Security Manager

> *"The quarterback of the RMF process."*

### 3.1 Role Summary

The ISSM manages the security posture for one or more systems. They report to the AO, oversee ISSOs, own the authorization package, and coordinate the entire RMF lifecycle from registration through continuous monitoring.

**RBAC Role**: `Compliance.SecurityLead`
**Primary Interface**: Microsoft Teams, MCP API
**Systems Managed**: One or more registered systems

### 3.2 Getting Started (ISSM)

**Prerequisites:**
1. CAC enrolled with Security Posture Intelligence Navigator (thumbprint mapped or Azure AD group membership as `Compliance.SecurityLead`)
2. Azure subscription linked to Security Posture Intelligence Navigator server
3. Access to Microsoft Teams (primary) or MCP API client

**First-Time Setup:**

```
Step 1: Verify your identity and role
  → "What role am I logged in as?"
  → Expected: Compliance.SecurityLead

Step 2: Register your first system
  → "Register a new system called 'My System' as a Major Application
      in Azure Government"

Step 3: View the system you just created
  → "Show system details for {id}"
```

**What to do next:** Define the authorization boundary (§3.3 Phase 0), then assign RMF roles and advance through the lifecycle.

### 3.3 Daily Activities
- Track where each system is in the RMF lifecycle
- Review compliance dashboards and trend reports
- Coordinate remediation priorities across teams
- Prepare authorization packages for the AO
- Manage POA&M items and milestone deadlines
- Respond to compliance alerts and significant changes
- Brief leadership on security posture
- Interface with eMASS for official record-keeping

### 3.4 ISSM Workflow by RMF Phase

#### Phase 0: Prepare

**Goal**: Register the system, define its authorization boundary, assign roles, and establish the organizational baseline.

**Natural Language Queries:**

```
Register a new system called 'ACME Portal' as a Major Application with
mission-critical designation in Azure Government

Define the authorization boundary for system {id} — add the production VMs,
SQL database, and Key Vault

Assign Jane Smith as ISSM and Bob Jones as ISSO for system {id}

Show me all registered systems

What roles are assigned to system {id}?

Advance system {id} to the Categorize phase
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_register_system` | Creates system entity with initial RMF step = Prepare |
| 2 | `compliance_define_boundary` | Adds Azure resource IDs to the authorization boundary |
| 3 | `compliance_exclude_from_boundary` | Optionally excludes shared/inherited resources with rationale |
| 4 | `compliance_assign_rmf_role` | Assigns AO, ISSM, ISSO, SCA, SystemOwner to the system |
| 5 | `compliance_list_rmf_roles` | Verifies all required roles are assigned |
| 6 | `compliance_advance_rmf_step` | Advances to Categorize (gate: ≥1 role + ≥1 boundary resource) |

**Gate Conditions (Prepare → Categorize):**
- At least one RMF role assigned to the system
- At least one resource in the authorization boundary

**Documents Produced**: None (informational artifacts only)

---

#### Phase 1: Categorize

**Goal**: Perform FIPS 199 security categorization using SP 800-60 information types. Determine the C/I/A high-water mark and DoD Impact Level.

**Natural Language Queries:**

```
Suggest information types for system {id} — it's a financial management
and audit logging system

Categorize system {id} with these information types:
- Financial Management (C: Moderate, I: Moderate, A: Low)
- Information Security (C: Moderate, I: High, A: Moderate)

What is the current categorization for system {id}?

What DoD Impact Level was derived for system {id}?

Advance to the Select phase
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_suggest_info_types` | AI suggests SP 800-60 information types with confidence scores |
| 2 | `compliance_categorize_system` | Applies information types, computes high-water mark, maps to DoD IL |
| 3 | `compliance_get_categorization` | Reviews stored categorization and FIPS 199 notation |
| 4 | `compliance_advance_rmf_step` | Advances to Select (gate: categorization exists + ≥1 info type) |

**Key Outputs:**
- FIPS 199 Notation: `SC System = {(confidentiality, MODERATE), (integrity, HIGH), (availability, MODERATE)}`
- Overall Categorization: High (the maximum across C/I/A)
- DoD Impact Level: IL5 (derived from categorization + NSS flag)
- NIST Baseline: High (determined by overall categorization)

**Documents Produced**: FIPS 199 Categorization Report (embedded in SSP)

---

#### Phase 2: Select

**Goal**: Select the NIST 800-53 baseline, apply CNSSI 1253 overlay, tailor controls, declare inheritance, and generate the CRM.

**Natural Language Queries:**

```
Select the control baseline for system {id} with CNSSI 1253 overlay

How many controls are in the baseline for system {id}?

Remove control PE-5 from the baseline — not applicable, system is 100%
cloud-hosted with no physical media

Set AC-1 as inherited from Azure Government FedRAMP High

Set AC-2 as shared with Azure Government — customer configures access
policies and reviews accounts quarterly

Generate the Customer Responsibility Matrix for system {id}

Show me the STIG mappings for AC-2

Advance to the Implement phase
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_select_baseline` | Selects Low/Moderate/High baseline, optionally applies CNSSI 1253 overlay |
| 2 | `compliance_tailor_baseline` | Adds/removes controls with documented rationale |
| 3 | `compliance_set_inheritance` | Marks controls as Inherited/Shared/Customer with provider info |
| 4 | `compliance_get_baseline` | Reviews baseline with tailoring and inheritance |
| 5 | `compliance_generate_crm` | Generates Customer Responsibility Matrix grouped by family |
| 6 | `compliance_show_stig_mapping` | Shows DISA STIG IDs mapped to NIST controls |
| 7 | `compliance_advance_rmf_step` | Advances to Implement (gate: baseline exists) |

**Baseline Control Counts:**
- Low: ~152 controls (IL2)
- Moderate: ~329 controls (IL4)
- High: ~400 controls (IL5/IL6)

**Inheritance Types:**
| Type | Meaning | SSP Narrative |
|------|---------|---------------|
| **Inherited** | Fully satisfied by CSP (e.g., physical security) | Auto-populated standard statement |
| **Shared** | Partially CSP, partially customer | Requires customer responsibility documentation |
| **Customer** | Entirely the customer's responsibility | Requires full human-authored narrative |

**Documents Produced**: Customer Responsibility Matrix (CRM)

---

#### Phase 3: Implement (ISSM Oversight)

**Goal**: Oversee SSP authoring, track narrative completion, and generate the formal SSP document.

**Natural Language Queries:**

```
What's the SSP completion status for system {id}?

Show narrative progress for the AC family

Generate the System Security Plan for system {id}

Generate the SSP as PDF

Generate SSP using our custom DISA template
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_narrative_progress` | Tracks per-family completion % (inherited auto-filled, customer pending) |
| 2 | `compliance_generate_ssp` | Generates formal SSP with system info, categorization, baseline, all narratives |
| 3 | `compliance_advance_rmf_step` | Advances to Assess (advisory gate — no hard block) |

**Documents Produced**: System Security Plan (SSP) — Markdown, PDF, or DOCX

---

#### Phase 4: Assess (ISSM Support)

**Goal**: Support the SCA during assessment by managing POA&M items and preparing the authorization package.

**Natural Language Queries:**

```
Create a POA&M item for the missing MFA finding on IA-2(1) — assign to
John Smith, due June 30

List overdue POA&M items for system {id}

Show all CAT I POA&M items

Generate the Risk Assessment Report for system {id}
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_create_poam` | Creates formal POA&M with milestones, CAT severity, POC, resources |
| 2 | `compliance_list_poam` | Lists POA&M items with status/severity/overdue filters |
| 3 | `compliance_generate_rar` | Generates RAR with per-family risk breakdown and aggregate risk level |

**Documents Produced**: Plan of Action & Milestones (POA&M), Risk Assessment Report (RAR)

---

#### Phase 5: Authorize (ISSM Package Preparation)

**Goal**: Bundle the complete authorization package and deliver it to the AO for decision.

**Natural Language Queries:**

```
Bundle the authorization package for system {id} including evidence

What documents are ready for the authorization package?

Show the risk register for system {id}
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_bundle_authorization_package` | Bundles SSP + SAR + RAR + POA&M + CRM + ATO Letter |
| 2 | `compliance_show_risk_register` | Reviews all active/expired risk acceptances |

**Authorization Package Contents:**

| Document | Source | Required |
|----------|--------|----------|
| System Security Plan (SSP) | `compliance_generate_ssp` | Yes |
| Security Assessment Report (SAR) | `compliance_generate_sar` | Yes |
| Risk Assessment Report (RAR) | `compliance_generate_rar` | Yes |
| Plan of Action & Milestones (POA&M) | `compliance_create_poam` / `compliance_list_poam` | Yes |
| Customer Responsibility Matrix (CRM) | `compliance_generate_crm` | Yes |
| ATO Letter | Generated from authorization decision | After AO decision |

**Documents Produced**: Authorization Package (bundled ZIP)

---

#### Phase 6: Monitor

**Goal**: Establish continuous monitoring, generate periodic reports, track ATO expiration, detect significant changes, manage reauthorization triggers, and maintain the portfolio dashboard.

**Natural Language Queries:**

```
Create a ConMon plan for system {id} with monthly assessments and annual
review on June 15

Generate a monthly ConMon report for system {id}, period 2026-02

Check ATO expiration for system {id}

Report a significant change for system {id}: New Interconnection — added
VPN tunnel to partner organization

Check reauthorization triggers for system {id}

Show the multi-system compliance dashboard

Show all systems with expired ATOs

Send a notification about the expiration alert for system {id}

Export system {id} data to eMASS format

Export OSCAL JSON for system {id}
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_create_conmon_plan` | Creates/updates ConMon plan with frequency, review date, distribution |
| 2 | `compliance_generate_conmon_report` | Generates report with score delta, findings, POA&M status, Watch data |
| 3 | `compliance_track_ato_expiration` | Checks expiration with graduated alerts (90d/60d/30d/expired) |
| 4 | `compliance_report_significant_change` | Records change, flags for reauthorization if applicable |
| 5 | `compliance_reauthorization_workflow` | Checks triggers (expiration, significant changes, score drift) |
| 6 | `compliance_multi_system_dashboard` | Portfolio view: all systems with IL, RMF step, auth status, score |
| 7 | `compliance_send_notification` | Delivers alerts via Teams/VS Code/alert pipeline |
| 8 | `compliance_export_emass` | Exports to eMASS-compatible Excel format |
| 9 | `compliance_export_oscal` | Exports in OSCAL v1.0.6 JSON format |

**Expiration Alert Levels:**

| Days Remaining | Alert Level | Severity | Action Required |
|----------------|-------------|----------|-----------------|
| ≤ 90 days | Info | Low | Begin reauthorization planning |
| ≤ 60 days | Warning | Medium | Submit reauthorization package |
| ≤ 30 days | Urgent | High | Escalate to AO immediately |
| Expired | Expired | Critical | System operating without authorization |

**Significant Change Types (Built-in):**
1. New Interconnection
2. Major Upgrade
3. Data Type Change
4. Architecture Change
5. Security Policy Change
6. Boundary Change
7. Key Personnel Change
8. Incident Response
9. Compliance Drop
10. Configuration Drift (auto-detected by Watch service)

**Documents Produced**: ConMon Reports (monthly/quarterly/annual), Reauthorization Package

> **🔒 Air-Gapped Note (Monitor Phase)**: In disconnected environments:
> - `compliance_export_emass` generates the Excel file locally — manual transfer to eMASS via removable media is required.
> - `compliance_send_notification` is limited to local channels (VS Code, audit log); external email/webhook notifications are unavailable.
> - `compliance_export_oscal` works fully offline (file generation only).
> - Watch event-driven monitoring requires network to Azure Policy/Defender — use scheduled-only mode with local policy cache.

---

### 3.5 ISSM Complete Workflow Summary

```
 1. compliance_register_system            ← Register the system
 2. compliance_define_boundary            ← Define authorization boundary
 3. compliance_assign_rmf_role            ← Assign all required RMF roles
 4. compliance_advance_rmf_step           ← Prepare → Categorize
 5. compliance_suggest_info_types         ← Get AI-suggested info types
 6. compliance_categorize_system          ← FIPS 199 categorization
 7. compliance_advance_rmf_step           ← Categorize → Select
 8. compliance_select_baseline            ← Select NIST 800-53 baseline
 9. compliance_tailor_baseline            ← Add/remove controls
10. compliance_set_inheritance            ← Declare inheritance
11. compliance_generate_crm              ← Generate CRM
12. compliance_advance_rmf_step           ← Select → Implement
13. compliance_batch_populate_narratives  ← Auto-fill inherited narratives
14. compliance_narrative_progress         ← Track SSP completion
15. compliance_generate_ssp              ← Generate System Security Plan
16. compliance_advance_rmf_step           ← Implement → Assess
17. (SCA performs assessment — see §5)
18. compliance_create_poam               ← Create POA&M items
19. compliance_generate_rar              ← Generate RAR
20. compliance_bundle_authorization_package ← Bundle for AO
21. compliance_advance_rmf_step           ← Assess → Authorize
22. (AO issues decision — see §6)
23. compliance_advance_rmf_step           ← Authorize → Monitor
24. compliance_create_conmon_plan         ← Establish ConMon plan
25. compliance_generate_conmon_report     ← Periodic reports
26. compliance_track_ato_expiration       ← Monitor expiration
27. compliance_report_significant_change  ← Report changes
28. compliance_multi_system_dashboard     ← Portfolio oversight
29. compliance_export_emass              ← eMASS sync
```

---

## 4. Persona 2: ISSO — Information System Security Officer

> *"Day-to-day security operations — the hands on the keyboard."*

### 4.1 Role Summary

The ISSO implements and monitors security controls under ISSM oversight. They write SSP narratives, collect evidence, manage Watch monitoring, handle alerts, and coordinate remediation with engineers.

**RBAC Role**: `Compliance.Analyst`
**Primary Interface**: VS Code (`@ato`), Microsoft Teams
**Reports to**: ISSM

### 4.2 Getting Started (ISSO)

**Prerequisites:**
1. CAC enrolled with `Compliance.Analyst` role
2. Assigned as ISSO to one or more systems by the ISSM (via `compliance_assign_rmf_role`)
3. VS Code with GitHub Copilot Chat extension installed (primary interface)

**First-Time Setup:**

```
Step 1: Verify your role and system assignments
  → @ato "What systems am I assigned to?"
  → Expected: List of systems where you are ISSO

Step 2: Check the current RMF phase for your system
  → @ato "Show system details for {id}"

Step 3: Start your primary workflow
  If Implement phase → "Show narrative progress for system {id}"
  If Monitor phase  → "Show monitoring status for all subscriptions"
```

**What to do next:** If the system is in Implement phase, begin authoring narratives (§4.3 Phase 3). If in Monitor phase, enable Watch monitoring (§4.3 Phase 6).

### 4.3 Daily Activities
- Write and review control implementation narratives
- Collect evidence from Azure infrastructure
- Monitor compliance Watch alerts and triage new findings
- Coordinate remediation tasks with engineers on Kanban boards
- Respond to drift alerts and configuration changes
- Run periodic compliance assessments
- Ensure monitoring is enabled and properly configured

### 4.4 ISSO Workflow by RMF Phase

#### Phase 3: Implement (ISSO Lead)

**Goal**: Author SSP control narratives, batch-populate inherited controls, and get AI-assisted suggestions for customer controls.

**Natural Language Queries:**

```
Auto-populate the inherited control narratives for system {id}

Suggest a narrative for AC-2 on system {id}

Write the narrative for AC-2: "Account management is implemented using
Azure Active Directory with conditional access policies..."

What's the narrative completion for the SC family?

Show all controls missing narratives for system {id}
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_batch_populate_narratives` | Auto-fills inherited/shared controls (~40-60% coverage) |
| 2 | `compliance_suggest_narrative` | AI-generated draft for customer controls (confidence score included) |
| 3 | `compliance_write_narrative` | Write/update narrative with status (Implemented/Partial/Planned/N-A) |
| 4 | `compliance_narrative_progress` | Track per-family completion → target 100% |

**AI Suggestion Confidence Levels:**
| Score | Meaning | Action |
|-------|---------|--------|
| ≥ 0.85 | High confidence (inherited controls) | Review and accept |
| 0.70–0.84 | Good confidence (shared controls) | Review and customize |
| 0.50–0.69 | Moderate confidence (customer controls) | Significant review needed |
| < 0.50 | Low confidence — flagged | Write manually |

> **🔒 Air-Gapped Note**: In disconnected environments, `compliance_suggest_narrative` and `compliance_batch_populate_narratives` (AI-generated suggestions) are unavailable. Write all narratives manually using `compliance_write_narrative`. Inherited control narratives can still be auto-populated from the embedded control catalog (no network required).

---

#### Phase 4: Assess (ISSO Support)

**Goal**: Collect evidence, support the SCA during assessment, and assist with finding remediation.

**Natural Language Queries:**

```
Collect evidence for the AC family on subscription {sub-id}

Run a compliance assessment on subscription {sub-id}

Show me all critical findings from the last assessment

Create a remediation board from the latest assessment

Assign task REM-003 to engineer Bob Jones

Fix alert ALT-12345 with dry run first
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_collect_evidence` | Collects Azure resource evidence with SHA-256 hashing |
| 2 | `compliance_assess` | Runs NIST 800-53 compliance assessment (policy + Defender + ARM) |
| 3 | `kanban_create_board` | Creates Kanban board from assessment findings |
| 4 | `kanban_assign_task` | Assigns remediation tasks to engineers |
| 5 | `watch_fix_alert` | Remediates findings with dry-run safety |

---

#### Phase 6: Monitor (ISSO Day-to-Day)

**Goal**: Manage continuous monitoring, triage alerts, maintain baselines, and handle auto-remediation.

**Natural Language Queries:**

```
Enable daily monitoring for subscription {sub-id}

Show monitoring status for all subscriptions

Show all critical alerts from the last 7 days

Acknowledge alert ALT-12345

What drifted this week?

Show me the compliance trend for subscription {sub-id}

Configure auto-remediation for Low severity drift alerts

Show alert statistics for the last 30 days

Set quiet hours from 22:00 to 06:00 on weekdays

Configure escalation: if Critical alert is not acknowledged in 30 minutes,
notify the ISSM
```

**Watch Monitoring Tools:**

| Tool | Purpose |
|------|---------|
| `watch_enable_monitoring` | Enable scheduled/event-driven/combined monitoring |
| `watch_disable_monitoring` | Disable monitoring for a subscription |
| `watch_configure_monitoring` | Update frequency or mode |
| `watch_monitoring_status` | View all monitoring configurations |
| `watch_show_alerts` | List alerts with severity/status/family/time filters |
| `watch_get_alert` | Full alert details with history and correlations |
| `watch_acknowledge_alert` | Acknowledge (pauses SLA escalation) |
| `watch_fix_alert` | Remediate with optional dry-run |
| `watch_dismiss_alert` | Dismiss false positive (requires justification, officer only) |
| `watch_create_rule` | Create custom alert rules |
| `watch_list_rules` | List active alert rules |
| `watch_suppress_alerts` | Suppress alert patterns with expiration |
| `watch_configure_quiet_hours` | Set notification quiet hours |
| `watch_configure_notifications` | Configure channels (Chat, Email, Webhook) |
| `watch_configure_escalation` | Define escalation paths for SLA violations |

**Alert Lifecycle States:**

```
New → Acknowledged → InProgress → Resolved
  ↓        ↓
Dismissed  Escalated (SLA violation)
```

| State | Description | SLA Timer |
|-------|-------------|-----------|
| **New** | Alert created, awaiting triage | Running |
| **Acknowledged** | Team aware, triaging | Paused |
| **InProgress** | Remediation underway | Paused |
| **Resolved** | Finding fixed and verified | Stopped |
| **Dismissed** | False positive (justification required) | Stopped |
| **Escalated** | SLA violated, escalated per configured path | Escalation timer |

**SLA Due Dates by Severity:**
| Severity | Due Date | Escalation |
|----------|----------|------------|
| Critical | 24 hours | After 30 minutes unacknowledged |
| High | 7 days | After 4 hours unacknowledged |
| Medium | 30 days | After 24 hours unacknowledged |
| Low | 90 days | After 7 days unacknowledged |

---

## 5. Persona 3: SCA — Security Control Assessor

> *"Independent assessor — can observe and evaluate, never modify."*

### 5.1 Role Summary

The SCA independently evaluates whether controls are implemented and effective. They cannot implement or fix controls — only assess. They produce the SAR and RAR, and their effectiveness determinations feed the AO's authorization decision.

**RBAC Role**: `Compliance.Auditor` (read-only enforcement — cannot modify system data)
**Primary Interface**: Microsoft Teams, MCP API
**Independence**: Must be organizationally independent from the implementation team

### 5.2 Getting Started (SCA)

**Prerequisites:**
1. CAC enrolled with `Compliance.Auditor` role
2. Assigned as SCA to one or more systems by the ISSM
3. Organizationally independent from the implementation team (per DoDI 8510.01)
4. Access to Microsoft Teams or MCP API client

**First-Time Setup:**

```
Step 1: Verify your identity and read-only role
  → "What role am I logged in as?"
  → Expected: Compliance.Auditor

Step 2: Review the system's SSP before assessment
  → "Show narrative progress for system {id}"
  → "Show the baseline for system {id}"

Step 3: Begin your first assessment
  → "Assess control AC-1 as Satisfied using the Examine method —
      policy document reviewed and current"
```

**What to do next:** Systematically assess each control (§5.4), take snapshots, and generate SAR.

> ⚠️ **Important**: As SCA you have read-only access. You cannot modify narratives, fix findings, or issue authorization decisions. If you attempt a write operation, Security Posture Intelligence Navigator will return an RBAC denial with explanation.

### 5.3 Assessment Methods

| Method | When to Use | Examples |
|--------|-------------|---------|
| **Test** | Execute procedures, observe behavior, validate configurations | Run scans, test access controls, verify encryption |
| **Interview** | Question personnel about policies, procedures, practices | Ask admin about account review process |
| **Examine** | Review documentation, logs, configuration artifacts | Review SSP, audit logs, policy documents |

> **🔒 Air-Gapped Note (SCA)**: All assessment tools (`compliance_assess_control`, `compliance_take_snapshot`, `compliance_verify_evidence`, `compliance_compare_snapshots`, `compliance_generate_sar`, `compliance_generate_rar`) work fully offline — they operate on locally stored assessment data. Evidence collection (`compliance_collect_evidence`) requires network access to Azure resources; in air-gapped environments, evidence must be imported from prior scans or manual artifact uploads.

### 5.4 DoD CAT Severity Mapping

| CAT Level | Severity | Impact | Examples |
|-----------|----------|--------|----------|
| **CAT I** | Critical/High | Direct loss of C/I/A — immediate exploitation risk | Missing encryption, no access control enforcement |
| **CAT II** | Medium | Potential for system compromise | Weak password policies, incomplete logging |
| **CAT III** | Low | Administrative or documentation gaps | Missing audit review procedures, incomplete labels |

### 5.5 SCA Workflow — Phase 4: Assess

**Goal**: Assess each control's effectiveness, collect and verify evidence, produce immutable snapshots, and generate the SAR.

**Natural Language Queries:**

```
Assess control AC-2 as Satisfied using the Test method — account
management procedures verified against STIG checklist

Assess control AC-3 as Other Than Satisfied, CAT II — mandatory access
control checks missing for privileged operations

Take an assessment snapshot for system {id}

Verify evidence {evidence-id} hasn't been tampered with

Check evidence completeness for system {id} on the AC family

Compare snapshot {snap-1} with snapshot {snap-2} to see remediation progress

Generate the Security Assessment Report for system {id}

Generate the Risk Assessment Report for system {id}

Show controls with missing evidence

What's the overall compliance score?
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_assess_control` | Record per-control determination (Satisfied / OtherThanSatisfied + CAT) |
| 2 | `compliance_take_snapshot` | Create immutable SHA-256-hashed snapshot of assessment state |
| 3 | `compliance_verify_evidence` | Recompute hash → verified or tampered |
| 4 | `compliance_check_evidence_completeness` | Per-control evidence coverage report |
| 5 | `compliance_compare_snapshots` | Before/after comparison showing score delta and changes |
| 6 | `compliance_generate_sar` | Generate SAR with executive summary, CAT breakdown, per-family results |
| 7 | `compliance_generate_rar` | Generate RAR with residual risk determination |

**SAR Contents:**
1. Executive Summary — system, assessor, overall score, finding counts
2. CAT Severity Breakdown — CAT I/II/III counts with risk categorization
3. Control Family Results — per-family pass/fail with CAT mapping
4. Risk Summary — assessment-to-authorization risk posture
5. Detailed Findings — per-control determination details

**Typical SCA Assessment Cycle:**

```
 1. compliance_assess_control       ← Assess each control (batch)
 2. compliance_take_snapshot        ← Snapshot current state
 3. compliance_verify_evidence      ← Spot-check evidence integrity
 4. compliance_check_evidence_completeness ← Verify coverage
 5. compliance_generate_sar         ← Generate SAR
    ── (Remediation occurs) ──
 6. compliance_assess_control       ← Re-assess remediated controls
 7. compliance_take_snapshot        ← Snapshot after remediation
 8. compliance_compare_snapshots    ← Show improvement
 9. compliance_generate_sar         ← Updated SAR for AO decision
10. compliance_generate_rar         ← Final RAR
```

**Documents Produced**: Security Assessment Report (SAR), Risk Assessment Report (RAR), Assessment Snapshots

### 5.6 SCA RBAC Constraints

| Tool | Access |
|------|--------|
| `compliance_assess_control` | **Allowed** — SCA's primary function |
| `compliance_take_snapshot` | **Allowed** — creates immutable records |
| `compliance_verify_evidence` | **Allowed** — evidence integrity verification |
| `compliance_check_evidence_completeness` | **Allowed** — coverage reporting |
| `compliance_compare_snapshots` | **Allowed** — trend analysis |
| `compliance_generate_sar` | **Allowed** — SAR generation |
| `compliance_generate_rar` | **Allowed** — RAR generation |
| `compliance_write_narrative` | **DENIED** — SCA cannot modify SSP |
| `compliance_remediate` | **DENIED** — SCA cannot fix findings |
| `compliance_issue_authorization` | **DENIED** — only AO can authorize |
| `watch_dismiss_alert` | **DENIED** — cannot dismiss alerts |

---

## 6. Persona 4: AO — Authorizing Official

> *"Infrequent interaction, most consequential decisions."*

### 6.1 Role Summary

The AO is a senior leader (typically O-6/GS-15+) who accepts risk and signs the authorization decision. They interact with Security Posture Intelligence Navigator infrequently but make the highest-stakes decisions: granting or denying authorization to operate.

**RBAC Role**: `Compliance.AuthorizingOfficial` (dedicated role — separated from Administrator for separation of duties per DoDI 8510.01)
**Primary Interface**: Microsoft Teams (Adaptive Cards)
**Decision Authority**: Authorization decisions, risk acceptance

### 6.2 Getting Started (AO)

**Prerequisites:**
1. CAC enrolled with `Compliance.AuthorizingOfficial` role (typically provisioned by Administrator)
2. Designated as AO for one or more systems
3. Access to Microsoft Teams (primary — Adaptive Cards for authorization decisions)

**First-Time Setup:**

```
Step 1: Verify your role
  → "What role am I logged in as?"
  → Expected: Compliance.AuthorizingOfficial

Step 2: View your portfolio
  → "Show the multi-system compliance dashboard"

Step 3: Review an authorization package
  → "Show the authorization package summary for system {id}"
```

**What to do next:** When the ISSM delivers an authorization package, review the SAR/RAR, evaluate residual risk, and issue your decision (§6.4).

### 6.3 Authorization Decision Types

| Type | Description | Expiration | System State After |
|------|-------------|------------|-------------------|
| **ATO** | Authority to Operate — full authorization | Required (typically 3 years) | Monitor phase |
| **ATOwC** | ATO with Conditions — authorization with stipulations | Required | Monitor phase (conditions tracked) |
| **IATT** | Interim Authority to Test — limited testing only | Required (typically 6 months) | Monitor phase (limited scope) |
| **DATO** | Denial of Authorization — system cannot operate | None | Read-only mode, advancement blocked |

### 6.4 AO Workflow — Phase 5: Authorize

**Goal**: Review the authorization package, evaluate residual risk, make the authorization decision, and accept risk on specific findings.

**Natural Language Queries:**

```
Show the authorization package summary for system {id}

What's the compliance score and finding breakdown for system {id}?

Issue an ATO for system {id} expiring January 15, 2028 with Low residual
risk — all CAT I findings remediated, 2 CAT III findings accepted

Issue an ATO with conditions for system {id} — MFA enforcement must be
completed within 90 days, quarterly POA&M reviews required

Accept risk on finding {finding-id} for control CM-6 (CAT III) — configuration
deviation documented and approved, compensating control: continuous
monitoring alerts configured, expires December 31, 2026

Deny authorization for system {id} — 3 unmitigated CAT I findings with no
remediation plan

Show the risk register for system {id}

What risks have I accepted that are expiring soon?
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_bundle_authorization_package` | Review bundled package (SSP + SAR + RAR + POA&M + CRM) |
| 2 | `compliance_show_risk_register` | Review existing risk acceptances |
| 3 | `compliance_issue_authorization` | Issue ATO/ATOwC/IATT/DATO decision |
| 4 | `compliance_accept_risk` | Accept risk on individual findings post-decision |

**Key Authorization Behaviors:**
- **Supersedes prior decisions**: Any existing active authorization is automatically deactivated
- **Compliance score captured**: Score at decision time is recorded permanently
- **RMF advancement**: System moves to Monitor phase on ATO/ATOwC/IATT
- **Open findings recorded**: All open findings at decision time are captured in the record
- **DATO effects**: System enters read-only mode, generates persistent alert, blocks RMF advancement

**Risk Acceptance Lifecycle:**
1. AO accepts risk with justification + compensating control + expiration date
2. Risk acceptance is active → finding severity is documented but accepted
3. Expiration date arrives → acceptance auto-expires, finding reverts to active
4. Linked POA&M items revert from `RiskAccepted` to `Ongoing`
5. Alert sent to both AO and ISSM

**Documents Produced**: Authorization Decision Letter, Risk Acceptance Memorandum, Terms & Conditions

### 6.5 AO in Monitor Phase

The AO receives escalated alerts and expiration notifications:

```
Show all systems with ATOs expiring in the next 90 days

Show the compliance dashboard — filter to my authorized systems

What significant changes have been reported for system {id}?
```

| Tool | Purpose |
|------|---------|
| `compliance_multi_system_dashboard` | Portfolio view of all authorized systems |
| `compliance_track_ato_expiration` | Graduated expiration alerts |
| `compliance_reauthorization_workflow` | Check triggers and initiate reauthorization |
| `compliance_show_risk_register` | Review accepted risks approaching expiration |

---

## 7. Persona 5: Platform Engineer / System Owner

> *"Fix the findings, implement the controls, prove it works."*

### 7.1 Role Summary

The engineer builds and operates the system being authorized. They implement controls, fix findings, write IaC, and collect evidence. They often have limited security background and need guidance in terms of their specific technology stack (Azure, Terraform, Kubernetes).

**RBAC Role**: `Compliance.PlatformEngineer`
**Primary Interface**: VS Code (`@ato` participant)
**Reports to**: ISSO (operational), ISSM (oversight)

### 7.2 Getting Started (Engineer)

**Prerequisites:**
1. CAC enrolled with `Compliance.PlatformEngineer` role (default if no explicit mapping)
2. VS Code with GitHub Copilot Chat extension installed
3. Azure subscription access for the system being authorized

**First-Time Setup:**

```
Step 1: Verify your role
  → @ato "What role am I logged in as?"
  → Expected: Compliance.PlatformEngineer

Step 2: Check your assigned tasks
  → @ato "Show my assigned remediation tasks"

Step 3: Learn about your first control
  → @ato /knowledge "What does AC-2 mean for Azure?"
```

**What to do next:** If you have Kanban tasks, start fixing findings (§7.4 Phase 4). If in Implement phase, begin writing narratives (§7.4 Phase 3).

### 7.3 Daily Activities
- Write Infrastructure as Code (Bicep/Terraform/ARM) and deploy infrastructure
- Implement security configurations per STIG requirements
- Fix compliance findings assigned via Kanban board
- Collect evidence proving fixes work
- Write control implementation narratives (with AI assistance)
- Run IaC compliance scans before deploying

### 7.4 Engineer Workflow by RMF Phase

#### Phase 3: Implement (Engineer Lead)

**Goal**: Implement controls, write narratives, scan IaC for compliance, and apply STIG configurations.

**Natural Language Queries:**

```
What does NIST control AC-2 mean for Azure?

Show STIG findings for Azure SQL Database

Scan my Bicep file for compliance issues

What's the suggested fix for this STIG finding?

Suggest a narrative for control SC-7 on system {id}

Write the narrative for SC-7: "Network boundary protection is implemented
using Azure Firewall with default-deny rules..."

Auto-populate inherited control narratives for system {id}

What controls still need narratives?
```

**Tool Sequence:**

| Step | Tool | What Happens |
|------|------|-------------|
| 1 | `compliance_batch_populate_narratives` | Auto-fill inherited controls (~40-60% coverage) |
| 2 | `compliance_suggest_narrative` | AI draft for customer controls with confidence score |
| 3 | `compliance_write_narrative` | Write/update narrative (Implemented/Partial/Planned/N-A) |
| 4 | `compliance_narrative_progress` | Track completion per-family |
| 5 | `compliance_show_stig_mapping` | Look up STIG rules mapped to NIST controls |

**VS Code Integration:**
- **IaC Diagnostics**: Compliance findings appear as squiggly underlines (CAT I/II → Error, CAT III → Warning)
- **Quick Fix**: Lightbulb Code Actions to apply suggested fixes from STIG findings
- **Hover Info**: Shows NIST control + STIG rule + CAT severity on hover

---

#### Phase 4: Assess (Engineer Support — Remediation)

**Goal**: Fix compliance findings, execute remediation scripts, validate fixes, and collect evidence.

**Natural Language Queries:**

```
Show my assigned remediation tasks

Show task REM-005 details

Move REM-005 to In Progress

Fix task REM-005 with dry run first

Fix task REM-005

Validate task REM-005 — check if the fix worked

Collect evidence for task REM-005

Move REM-005 to In Review

Show all overdue tasks assigned to me
```

**Kanban Remediation Tools:**

| Tool | Purpose |
|------|---------|
| `kanban_board_show` | View board overview with task counts per column |
| `kanban_task_list` | List/filter tasks (severity, assignee, family, status) |
| `kanban_get_task` | Full task detail with control ID, resources, script, history |
| `kanban_move_task` | Move task between columns (status transitions) |
| `kanban_remediate_task` | Execute remediation script for a task |
| `kanban_task_validate` | Re-scan resources to verify remediation |
| `kanban_collect_evidence` | Collect compliance evidence for a task |
| `kanban_add_comment` | Add comments and @mentions |

**Kanban Status Flow:**

```
Backlog → ToDo → InProgress → InReview → Done
                     ↕
                  Blocked
```

**Status Transition Rules:**
| Transition | Rule |
|-----------|------|
| → Blocked | Requires blocker comment |
| Blocked → | Requires resolution comment |
| → Done | Requires validation pass (or officer override) |
| → InProgress | Auto-assigns if unassigned |
| → InReview | Triggers automatic validation scan |
| Done → anything | Terminal — cannot reopen |

---

#### Phase 6: Monitor (Engineer — Fix Drift)

**Goal**: Respond to Watch alerts by fixing drift findings.

**Natural Language Queries:**

```
Show my alerts

Fix alert ALT-12345 with dry run

Fix alert ALT-12345

Show compliance trend for my subscription
```

---

## 8. Persona 6: Administrator

> *"Copilot infrastructure management — not authorization decisions."*

### 8.1 Role Summary

The Administrator manages Security Posture Intelligence Navigator server configuration, template management, and infrastructure settings. This role is explicitly separated from the AO role to enforce DoD separation of duties.

**RBAC Role**: `Compliance.Administrator`
**Primary Interface**: MCP API, VS Code

### 8.2 Administrator Capabilities

**Natural Language Queries:**

```
Upload our organization's SSP template (DOCX)

List all uploaded document templates

Update template {id} with new file content

Delete template {id}

Configure the Azure subscription connection

Set up proxy configuration for air-gapped environment
```

**Template Management Tools:**

| Tool | Purpose |
|------|---------|
| `compliance_upload_template` | Upload custom DOCX template for SSP/SAR/POA&M/RAR |
| `compliance_list_templates` | List all uploaded templates, filter by document type |
| `compliance_update_template` | Update template name or replace file content |
| `compliance_delete_template` | Remove a template |

**Template Merge Fields:**
Templates use `{{field_name}}` placeholders that are automatically populated with system data:
- `{{system_name}}`, `{{system_acronym}}`, `{{system_type}}`
- `{{categorization}}`, `{{impact_level}}`, `{{fips_notation}}`
- `{{control_implementations}}`, `{{baseline_summary}}`
- `{{findings_summary}}`, `{{poam_items}}`, `{{risk_acceptances}}`

---

## 9. Cross-Persona Collaboration Matrix

### 9.1 Who Hands Off to Whom

| From → To | Trigger | What's Handed Off |
|-----------|---------|-------------------|
| **ISSM → ISSO** | System registered, baseline selected | System ID, control list for narrative authoring |
| **ISSM → Engineer** | Controls need implementation | Kanban tasks, STIG requirements, narrative assignments |
| **ISSO → Engineer** | Findings identified | Kanban remediation tasks with severity and SLA |
| **Engineer → ISSO** | Remediation complete | Task moved to InReview, evidence collected |
| **ISSO → SCA** | SSP ready for assessment | System ID, assessment scope, evidence package |
| **SCA → ISSM** | Assessment complete | SAR, RAR, effectiveness determinations |
| **ISSM → AO** | Package ready for decision | Bundled authorization package |
| **AO → ISSM** | Decision issued | Authorization decision, risk acceptances, conditions |
| **ISSO → ISSM** | Significant change detected | Change record, reauthorization flag |
| **Watch Service → ISSO** | Drift/violation detected | Alert with severity, control ID, resource ID |
| **ISSO → ISSM** | SLA escalation | Unacknowledged critical/high alerts |

### 9.2 Shared Tools (Multiple Personas)

| Tool | ISSM | ISSO | SCA | AO | Engineer |
|------|------|------|-----|-----|----------|
| `compliance_get_system` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `compliance_list_systems` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `compliance_get_categorization` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `compliance_get_baseline` | ✅ | ✅ | ✅ | ✅ | ✅ |
| `compliance_narrative_progress` | ✅ | ✅ | ✅ | — | ✅ |
| `compliance_show_risk_register` | ✅ | ✅ | ✅ (read) | ✅ | — |
| `compliance_multi_system_dashboard` | ✅ | ✅ | ✅ | ✅ | — |
| `compliance_show_stig_mapping` | ✅ | ✅ | ✅ | — | ✅ |
| `compliance_check_evidence_completeness` | ✅ | ✅ | ✅ | — | — |
| `compliance_compare_snapshots` | ✅ | ✅ | ✅ | — | — |

### 9.3 Exclusive Tools (Single Persona)

| Tool | Exclusive To | Reason |
|------|-------------|--------|
| `compliance_issue_authorization` | AO | Only AO can accept risk and authorize |
| `compliance_accept_risk` | AO | Risk acceptance requires AO authority |
| `compliance_assess_control` | SCA | Independence requirement — SCA only |
| `compliance_generate_sar` | SCA | Assessment report — SCA product |
| `watch_dismiss_alert` | ISSM (Officer) | Requires justification, compliance officer only |

### 9.4 Portfolio Management (Multi-System Workflows)

ISSMs and AOs often manage portfolios of dozens of systems simultaneously. This section covers workflows that span multiple systems.

#### 9.4.1 Portfolio Dashboard

The multi-system dashboard provides a single view of all registered systems with key metrics:

**Natural Language Queries:**

```
Show the multi-system compliance dashboard

Show all systems at the Assess phase

Which systems have compliance scores below 80%?

Show systems with expired or expiring ATOs

Compare compliance trends across all IL5 systems

Show portfolio risk summary
```

**Dashboard Data Points Per System:**

| Field | Description |
|-------|-------------|
| System Name | Registered name |
| Impact Level | IL2–IL6 |
| Current RMF Phase | Prepare through Monitor |
| Authorization Status | ATO/ATOwC/IATT/DATO/None |
| Expiration Date | ATO expiration with color-coded urgency |
| Compliance Score | Latest assessment percentage |
| Open Findings | CAT I / CAT II / CAT III counts |
| POA&M Status | Overdue / On Track / Completed counts |
| ConMon Status | Last report date, next due date |

#### 9.4.2 Bulk Operations

**ISSM Bulk Workflows:**

```
Export all my systems to eMASS format

Generate ConMon reports for all systems in the Monitor phase,
period 2026-02

Show overdue POA&M items across all systems

Bulk assign all High severity Kanban tasks in system {id} to
the security team

Check ATO expiration for all systems
```

**Available Bulk Tools:**

| Tool | Bulk Capability |
|------|----------------|
| `compliance_multi_system_dashboard` | All systems in one view |
| `compliance_list_systems` | Filter/search across all systems |
| `compliance_list_poam` | POA&M items across systems (when `systemId` omitted) |
| `compliance_track_ato_expiration` | All systems' expiration status |
| `kanban_bulk_update` | Bulk assign, move, or set due dates on tasks |
| `kanban_export` | Export board as CSV or POA&M for portfolio reporting |
| `compliance_export_emass` | Per-system export (loop for portfolio) |

#### 9.4.3 Delegation Patterns

When an ISSM manages many systems, delegation to ISSOs is critical:

| Pattern | How It Works |
|---------|-------------|
| **System-level ISSO assignment** | Each system has a named ISSO via `compliance_assign_rmf_role` — that ISSO owns day-to-day operations |
| **Alert routing** | Watch alerts route to the system's assigned ISSO first; escalation path goes to ISSM |
| **Kanban task assignment** | ISSM creates boards; ISSOs assign tasks to engineers per system |
| **ConMon responsibility** | ISSM sets the ConMon plan; ISSO executes monitoring and generates reports |
| **Reporting rollup** | ISSM uses dashboard for portfolio view; drills into individual systems as needed |

**Delegation Natural Language Queries:**

```
Who is the ISSO for system {id}?

Show all systems where Bob Jones is the assigned ISSO

Reassign ISSO role for system {id} from Bob Jones to Sarah Lee

Show alert summary grouped by ISSO

Which ISSOs have overdue tasks?
```

#### 9.4.4 AO Portfolio View

The AO typically authorizes many systems and needs portfolio-level risk visibility:

```
Show all systems I have authorized

What is my total portfolio risk exposure?

Show risk acceptances expiring in the next 90 days across all systems

Which of my authorized systems have CAT I findings?

Show authorization decisions I have issued this year
```

---

## 10. RMF Phase Reference

### 10.1 Phase Transition Gate Conditions

| Transition | Gate Requirements | Enforced? |
|-----------|-------------------|-----------|
| Prepare → Categorize | ≥1 RMF role assigned + ≥1 boundary resource | **Hard gate** |
| Categorize → Select | SecurityCategorization exists + ≥1 InformationType | **Hard gate** |
| Select → Implement | ControlBaseline exists | **Hard gate** |
| Implement → Assess | Advisory (no hard block) | Advisory |
| Assess → Authorize | Advisory (no hard block) | Advisory |
| Authorize → Monitor | Advisory (no hard block) | Advisory |
| Any backward movement | Requires `force: true` | **Hard gate** |

### 10.2 RMF Phase → Tool Mapping

| Phase | Primary Tools |
|-------|--------------|
| **Prepare** | `register_system`, `define_boundary`, `exclude_from_boundary`, `assign_rmf_role`, `list_rmf_roles`, `get_system`, `list_systems`, `advance_rmf_step` |
| **Categorize** | `suggest_info_types`, `categorize_system`, `get_categorization`, `advance_rmf_step` |
| **Select** | `select_baseline`, `tailor_baseline`, `set_inheritance`, `get_baseline`, `generate_crm`, `show_stig_mapping`, `advance_rmf_step` |
| **Implement** | `write_narrative`, `suggest_narrative`, `batch_populate_narratives`, `narrative_progress`, `generate_ssp`, `advance_rmf_step` |
| **Assess** | `assess_control`, `take_snapshot`, `compare_snapshots`, `verify_evidence`, `check_evidence_completeness`, `generate_sar`, `create_poam`, `list_poam`, `generate_rar`, `advance_rmf_step` |
| **Authorize** | `issue_authorization`, `accept_risk`, `show_risk_register`, `bundle_authorization_package`, `advance_rmf_step` |
| **Monitor** | `create_conmon_plan`, `generate_conmon_report`, `track_ato_expiration`, `report_significant_change`, `reauthorization_workflow`, `multi_system_dashboard`, `send_notification`, `export_emass`, `export_oscal` + all Watch tools |

### 10.3 What Each Phase Produces

| Phase | Artifacts Created | Format Options |
|-------|-------------------|----------------|
| Prepare | System Registration Record, Authorization Boundary, Role Assignments | JSON (MCP response) |
| Categorize | FIPS 199 Categorization, Information Type Inventory | JSON, embedded in SSP |
| Select | Control Baseline, Tailoring Record, Inheritance Map, CRM | JSON, Markdown, PDF, DOCX, Excel |
| Implement | Control Implementation Narratives, SSP | Markdown, PDF, DOCX |
| Assess | Effectiveness Determinations, Assessment Snapshots, SAR, RAR, POA&M | Markdown, PDF, DOCX, Excel |
| Authorize | Authorization Decision, Risk Acceptances, Authorization Package | Markdown, PDF, DOCX, ZIP bundle |
| Monitor | ConMon Plan, ConMon Reports, Significant Change Records, Alerts | Markdown, PDF, DOCX, eMASS Excel, OSCAL JSON |

---

## 11. Document Production Catalog

### 11.1 Document Reference

| Document | Full Name | Standard | Who Produces | When Produced | Output Formats |
|----------|-----------|----------|-------------|---------------|----------------|
| **SSP** | System Security Plan | NIST SP 800-18, DoDI 8510.01 | ISSO/ISSM | Implement phase | Markdown, PDF, DOCX |
| **SAR** | Security Assessment Report | NIST SP 800-53A | SCA | Assess phase | Markdown, PDF, DOCX |
| **RAR** | Risk Assessment Report | DoDI 8510.01 | SCA/ISSM | Assess phase | Markdown, PDF, DOCX |
| **POA&M** | Plan of Action & Milestones | OMB A-130, DoDI 8510.01 | ISSM | Assess through Monitor | Markdown, PDF, Excel |
| **CRM** | Customer Responsibility Matrix | FedRAMP, DoDI 8510.01 | ISSM | Select phase | Markdown, PDF, DOCX |
| **ATO Letter** | Authorization Decision Letter | DoDI 8510.01 | AO (via tool) | Authorize phase | Markdown, PDF, DOCX |
| **ConMon Report** | Continuous Monitoring Report | NIST SP 800-137 | ISSM | Monitor phase (periodic) | Markdown, PDF, DOCX |
| **Authorization Package** | Bundled Package (SSP+SAR+RAR+POA&M+CRM+ATO) | DoDI 8510.01 | ISSM | Authorize phase | ZIP bundle |
| **eMASS Export** | eMASS-Compatible Spreadsheet | DISA eMASS format | ISSM | Any phase | Excel (.xlsx) |
| **OSCAL Export** | OSCAL v1.0.6 JSON | NIST OSCAL | ISSM | Any phase | JSON |

### 11.2 SSP Sections

The generated System Security Plan contains these sections:

| Section | Content |
|---------|---------|
| 1. System Information | Name, type, mission criticality, hosting environment, RMF phase, boundary description |
| 2. Security Categorization | FIPS 199 notation, C/I/A impacts, DoD IL, information types with provisional/adjustment details |
| 3. Control Baseline | Baseline level, overlay applied, total controls, tailoring summary, inheritance coverage |
| 4. Control Implementations | Per-family grouped controls with narrative text, status, inheritance type, STIG mappings |

### 11.3 SAR Sections

| Section | Content |
|---------|---------|
| 1. Executive Summary | System identification, assessor, overall score, total findings |
| 2. CAT Severity Breakdown | CAT I/II/III counts with risk categorization |
| 3. Control Family Results | Per-family pass/fail rates with CAT mapping |
| 4. Risk Summary | Assessment-to-authorization risk posture |
| 5. Detailed Findings | Per-control determination, method, evidence links, notes |

### 11.4 RAR Sections

| Section | Content |
|---------|---------|
| 1. Executive Summary | Aggregate risk level, recommendation |
| 2. Per-Family Risk | Risk score by NIST control family |
| 3. Threat/Vulnerability Analysis | Finding counts by severity category |
| 4. Residual Risk | Accepted risks, compensating controls |

### 11.5 POA&M Fields (DoD-Required)

| Field | Description |
|-------|-------------|
| POA&M ID | Auto-generated identifier |
| Weakness | Description of the finding |
| Security Control | NIST 800-53 control ID |
| CAT Severity | CAT I, CAT II, or CAT III |
| Point of Contact | Responsible individual |
| Resources Required | Budget, personnel, tools needed |
| Scheduled Completion | Target remediation date |
| Milestones | Target dates for intermediate steps |
| Status | Ongoing, Completed, Delayed, Risk Accepted |

### 11.6 Document Template System

Security Posture Intelligence Navigator supports two template modes:

1. **Security Posture Intelligence Navigator Format** (default) — Built-in compliant format covering all DoDI 8510.01 required sections. Not locked to a specific DISA template revision.

2. **Custom Organizational Template** — Organizations upload DOCX templates with `{{merge_field}}` placeholders. The template engine validates merge fields on upload and injects data at generation time.

**Example Workflow:**

```
1. Upload template:     compliance_upload_template(name="DISA SSP v3",
                          document_type="ssp", file_base64="UEsDB...")

2. Generate with template: compliance_generate_ssp(system_id="sys-001",
                             format="docx", template="<template-id>")

3. Generate default PDF:   compliance_generate_ssp(system_id="sys-001",
                             format="pdf")
```

---

## 12. Natural Language Query Reference

### 12.1 System Registration & Setup

```
Register a new system called 'ACME Portal' as a Major Application,
mission-critical, hosted in Azure Government

List all registered systems

Show system details for {id}

What systems am I assigned to?

Add the production VMs and database to system {id}'s boundary

Exclude the shared logging service from system {id}'s boundary — it's
under a separate ATO by shared services

Assign Jane Smith as ISSM for system {id}

Who is assigned to system {id}?
```

### 12.2 Categorization

```
What information types should I use for a financial management system?

Categorize system {id} as Moderate confidentiality, High integrity,
Moderate availability

What's the DoD Impact Level for system {id}?

What's the FIPS 199 notation for system {id}?

Re-categorize system {id} — we added PII data types
```

### 12.3 Baseline & Controls

```
Select the NIST 800-53 baseline for system {id}

How many controls are in the High baseline?

Apply the CNSSI 1253 overlay for IL5

Remove PE-5 from the baseline — not applicable for cloud-only systems

Set AC-1 as inherited from Azure Government FedRAMP High

Generate the Customer Responsibility Matrix

What STIG rules map to AC-2?

Show all controls in the AC family

What controls require customer implementation?
```

### 12.4 SSP Authoring

```
Auto-populate inherited control narratives

Suggest a narrative for AC-2 on system {id}

Write the narrative for SC-7: "Network segmentation is..."

What's the SSP completion percentage?

Show narrative progress for the IA family

Which controls still need narratives?

Generate the SSP for system {id}

Generate SSP as PDF using our DISA template
```

### 12.5 Assessment

```
Assess control AC-2 as Satisfied — tested and verified

Assess AC-3 as Other Than Satisfied, CAT II — missing MAC enforcement

Take a snapshot of the current assessment state

Compare the before and after snapshots

Is evidence complete for the AC family?

Has evidence {id} been tampered with?

Generate the Security Assessment Report

What's the overall compliance score?

How many CAT I findings are there?
```

### 12.6 Authorization

```
Bundle the authorization package for system {id}

Issue an ATO for system {id} expiring January 2028 with Low residual risk

Issue ATO with conditions — MFA required within 90 days

Accept risk on finding {id} for CM-6 — compensating control: monitoring alerts

Deny authorization — 3 unmitigated CAT I findings

Show the risk register

What accepted risks are expiring soon?
```

### 12.7 Continuous Monitoring

```
Create a ConMon plan with monthly assessments

Generate the February 2026 ConMon report

When does the ATO expire for system {id}?

Report a significant change: new VPN interconnection

Check reauthorization triggers

Show the multi-system dashboard

Which systems have expired ATOs?

Export to eMASS format

Export OSCAL JSON
```

### 12.8 Compliance Watch

```
Enable daily monitoring for subscription {sub-id}

Show monitoring status

Show all critical alerts

Show alerts from the last 7 days for the AC family

What drifted this week?

Acknowledge alert ALT-12345

Fix alert ALT-12345 with dry run

Dismiss alert ALT-12345 — false positive, documented in ticket SNOW-123

Show alert statistics for the last 30 days

Show compliance trend for subscription {sub-id}

Configure quiet hours from 22:00 to 06:00 weekdays

Escalate Critical alerts to ISSM if not acknowledged in 30 minutes

Create a suppression rule for PE controls expiring March 31
```

### 12.9 Kanban & Remediation

```
Create a remediation board from the latest assessment

Show the board overview

Show my assigned tasks

Assign REM-003 to Bob Jones

Move REM-005 to In Progress

Fix REM-005 with dry run

Validate REM-005

Collect evidence for REM-005

Move REM-005 to In Review

Show all overdue tasks

Export the board as POA&M format

Bulk assign all High severity tasks to the security team
```

### 12.10 Knowledge & Education

```
What does NIST control AC-2 mean?

Explain control SC-7 in terms of Azure implementation

What are the FedRAMP Moderate requirements for encryption at rest?

What STIG rules apply to Azure SQL Database?

What is a CAT I finding?

Explain the difference between ATO and ATOwC

What triggers reauthorization?

What is a Customer Responsibility Matrix?
```

### 12.11 PIM (Privileged Identity Management)

```
What PIM roles am I eligible for?

Activate the Reader role for subscription {sub-id} — running quarterly
compliance assessment

List my active PIM roles

Deactivate the Contributor role — remediation complete

Show PIM activation history
```

---

## 13. Interface Guide

### 13.1 VS Code (GitHub Copilot Chat)

**How to Access**: Type `@ato` in the GitHub Copilot Chat panel.

**Slash Commands:**
| Command | Purpose |
|---------|---------|
| `/compliance` | Compliance scanning, assessment, remediation |
| `/knowledge` | NIST, STIG, RMF, FedRAMP knowledge queries |
| `/config` | Server configuration and connection settings |

**In-Editor Features:**
- **IaC Diagnostics**: Compliance findings as squiggly underlines in Bicep/Terraform/ARM files
- **Quick Fix (Lightbulb)**: Code actions to apply suggested STIG fixes
- **Hover Info**: NIST control + STIG rule + CAT severity on hover over flagged code
- **RMF Overview Panel**: Webview showing system status, timeline, and metrics

**Example VS Code Session:**

```
User: @ato /compliance Scan my main.bicep for compliance issues
→ Returns findings with CAT severity, STIG rule IDs, and inline suggestions

User: @ato /knowledge What does SC-7 mean for Azure networking?
→ Returns control explanation with Azure-specific implementation guidance

User: @ato Fix finding SC-7-001 in main.bicep
→ Applies suggested code fix as a VS Code Quick Fix
```

### 13.2 Microsoft Teams (M365 Bot)

**How to Access**: Message the Security Posture Intelligence Navigator bot in Teams or use it in a channel.

**Adaptive Cards:**
| Card | Purpose | Primary Persona |
|------|---------|-----------------|
| System Summary Card | Registered system overview (name, IL, phase, score) | ISSM |
| Categorization Card | FIPS 199 categories with C/I/A levels | ISSM |
| Authorization Card | ATO/ATOwC/IATT/DATO decision details | AO |
| Dashboard Card | Multi-system portfolio view with color-coded status | ISSM, AO |

**Example Teams Session:**

```
User: Show the compliance dashboard
→ Returns Dashboard Adaptive Card with all systems, scores, and alerts

User: What systems have ATOs expiring in 90 days?
→ Returns filtered list with expiration dates and alert levels

User: Issue ATO for ACME Portal expiring Jan 2028
→ Returns Authorization Card with decision summary for confirmation
```

### 13.3 MCP API (Direct)

All tools are accessible via the MCP server API using any MCP-compatible client:
- **Transport**: REST, Server-Sent Events (SSE), stdio
- **Authentication**: CAC certificate → role resolution
- **Response Format**: JSON with `{ status, data, metadata }` envelope

### 13.4 CI/CD (GitHub Actions)

> **🔒 Air-Gapped Note (Interfaces)**: In disconnected environments:
> - **VS Code**: Works fully — MCP server runs locally via stdio transport. AI-powered suggestions require proxy to LLM endpoint or are unavailable.
> - **Teams**: Unavailable (requires M365 cloud connectivity).
> - **MCP API**: Works fully locally with REST + stdio. SSE transport works within local network only.
> - **CI/CD**: Works within local GitLab/Jenkins instances; GitHub Actions requires connectivity.

**Compliance Gate Action:**
```yaml
- uses: ./.github/actions/ato-compliance-gate
  with:
    subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
    scan-type: iac
    fail-on: cat-i,cat-ii
    respect-risk-acceptances: true
```

Blocks PRs with CAT I/II findings. Respects active risk acceptances. Results appear as PR check annotations.

---

## 14. Appendices

### Appendix A: Complete MCP Tool Inventory

#### A.1 RMF Lifecycle Tools (Feature 015)

| # | Tool Name | Phase | Description |
|---|-----------|-------|-------------|
| 1 | `compliance_register_system` | Prepare | Register system for RMF tracking |
| 2 | `compliance_list_systems` | All | List registered systems |
| 3 | `compliance_get_system` | All | Get full system details |
| 4 | `compliance_advance_rmf_step` | All | Advance/regress RMF phase |
| 5 | `compliance_define_boundary` | Prepare | Define authorization boundary |
| 6 | `compliance_exclude_from_boundary` | Prepare | Exclude resource from boundary |
| 7 | `compliance_assign_rmf_role` | Prepare | Assign RMF role to user |
| 8 | `compliance_list_rmf_roles` | Prepare | List role assignments |
| 9 | `compliance_categorize_system` | Categorize | FIPS 199 categorization |
| 10 | `compliance_get_categorization` | Categorize | View categorization |
| 11 | `compliance_suggest_info_types` | Categorize | AI-suggest SP 800-60 info types |
| 12 | `compliance_select_baseline` | Select | Select NIST 800-53 baseline |
| 13 | `compliance_tailor_baseline` | Select | Add/remove controls |
| 14 | `compliance_set_inheritance` | Select | Set control inheritance |
| 15 | `compliance_get_baseline` | Select | View baseline details |
| 16 | `compliance_generate_crm` | Select | Generate CRM |
| 17 | `compliance_write_narrative` | Implement | Write control narrative |
| 18 | `compliance_suggest_narrative` | Implement | AI-suggest narrative |
| 19 | `compliance_batch_populate_narratives` | Implement | Auto-fill inherited narratives |
| 20 | `compliance_narrative_progress` | Implement | Track SSP completion |
| 21 | `compliance_generate_ssp` | Implement | Generate SSP document |
| 22 | `compliance_assess_control` | Assess | Record control effectiveness |
| 23 | `compliance_take_snapshot` | Assess | Immutable assessment snapshot |
| 24 | `compliance_compare_snapshots` | Assess | Compare snapshots |
| 25 | `compliance_verify_evidence` | Assess | Evidence integrity check |
| 26 | `compliance_check_evidence_completeness` | Assess | Evidence coverage report |
| 27 | `compliance_generate_sar` | Assess | Generate SAR |
| 28 | `compliance_issue_authorization` | Authorize | Issue ATO/ATOwC/IATT/DATO |
| 29 | `compliance_accept_risk` | Authorize | Accept risk on finding |
| 30 | `compliance_show_risk_register` | Authorize | View risk register |
| 31 | `compliance_create_poam` | Assess | Create POA&M item |
| 32 | `compliance_list_poam` | Assess | List POA&M items |
| 33 | `compliance_generate_rar` | Assess | Generate RAR |
| 34 | `compliance_bundle_authorization_package` | Authorize | Bundle SSP+SAR+RAR+POA&M+CRM |

#### A.2 Continuous Monitoring Tools (Feature 015)

| # | Tool Name | Description |
|---|-----------|-------------|
| 35 | `compliance_create_conmon_plan` | Create/update ConMon plan |
| 36 | `compliance_generate_conmon_report` | Generate periodic report |
| 37 | `compliance_track_ato_expiration` | Check expiration status |
| 38 | `compliance_report_significant_change` | Report significant change |
| 39 | `compliance_reauthorization_workflow` | Check/initiate reauthorization |
| 40 | `compliance_multi_system_dashboard` | Portfolio dashboard |
| 41 | `compliance_send_notification` | Send notification via channels |

#### A.3 Interoperability Tools (Feature 015)

| # | Tool Name | Description |
|---|-----------|-------------|
| 42 | `compliance_export_emass` | Export to eMASS Excel format |
| 43 | `compliance_import_emass` | Import eMASS Excel with conflict resolution |
| 44 | `compliance_export_oscal` | Export OSCAL v1.0.6 JSON |
| 45 | `compliance_show_stig_mapping` | NIST-to-STIG cross-reference |

#### A.4 Template Management Tools (Feature 015)

| # | Tool Name | Description |
|---|-----------|-------------|
| 46 | `compliance_upload_template` | Upload custom DOCX template |
| 47 | `compliance_list_templates` | List templates by document type |
| 48 | `compliance_update_template` | Update template content |
| 49 | `compliance_delete_template` | Delete template |

#### A.5 Core Compliance Tools (Features 001–014)

| # | Tool Name | Description |
|---|-----------|-------------|
| 50 | `compliance_assess` | Run NIST 800-53 assessment |
| 51 | `compliance_get_control_family` | Get control family info |
| 52 | `compliance_generate_document` | Generate compliance documents |
| 53 | `compliance_collect_evidence` | Collect Azure evidence |
| 54 | `compliance_remediate` | Remediate findings |
| 55 | `compliance_validate_remediation` | Validate remediation |
| 56 | `compliance_generate_plan` | Generate remediation plan |
| 57 | `compliance_audit_log` | View audit trail |
| 58 | `compliance_history` | View compliance history |
| 59 | `compliance_status` | Current compliance posture |
| 60 | `compliance_monitoring` | Monitoring setup |

#### A.6 Compliance Watch Tools (Feature 005)

| # | Tool Name | Description |
|---|-----------|-------------|
| 61 | `watch_enable_monitoring` | Enable scheduled monitoring |
| 62 | `watch_disable_monitoring` | Disable monitoring |
| 63 | `watch_configure_monitoring` | Update frequency/mode |
| 64 | `watch_monitoring_status` | View monitoring status |
| 65 | `watch_show_alerts` | List alerts with filters |
| 66 | `watch_get_alert` | Get alert details |
| 67 | `watch_acknowledge_alert` | Acknowledge alert |
| 68 | `watch_fix_alert` | Remediate alert finding |
| 69 | `watch_dismiss_alert` | Dismiss alert (officer only) |
| 70 | `watch_create_rule` | Create custom alert rule |
| 71 | `watch_list_rules` | List alert rules |
| 72 | `watch_suppress_alerts` | Suppress alert pattern |
| 73 | `watch_list_suppressions` | List suppressions |
| 74 | `watch_configure_quiet_hours` | Set notification quiet hours |
| 75 | `watch_configure_notifications` | Configure channels |
| 76 | `watch_configure_escalation` | Define escalation paths |
| 77 | `watch_alert_history` | Natural language alert queries |
| 78 | `watch_compliance_trend` | Compliance score over time |
| 79 | `watch_alert_statistics` | Alert counts and metrics |
| 80 | `watch_auto_remediation_create` | Create auto-remediation rule |
| 81 | `watch_auto_remediation_list` | List auto-remediation rules |
| 82 | `watch_auto_remediation_status` | View execution status |
| 83 | `watch_capture_baseline` | Capture compliance baseline |

#### A.7 Kanban Remediation Tools (Feature 002)

| # | Tool Name | Description |
|---|-----------|-------------|
| 84 | `kanban_create_board` | Create board from assessment |
| 85 | `kanban_board_show` | Display board overview |
| 86 | `kanban_get_task` | Get task details |
| 87 | `kanban_create_task` | Create remediation task |
| 88 | `kanban_assign_task` | Assign/reassign task |
| 89 | `kanban_move_task` | Move task between columns |
| 90 | `kanban_task_list` | List/filter tasks |
| 91 | `kanban_task_history` | View task audit trail |
| 92 | `kanban_task_validate` | Validate remediation |
| 93 | `kanban_add_comment` | Add comment/@mention |
| 94 | `kanban_task_comments` | List comments |
| 95 | `kanban_edit_comment` | Edit comment (24hr window) |
| 96 | `kanban_delete_comment` | Delete comment (1hr window) |
| 97 | `kanban_remediate_task` | Execute remediation script |
| 98 | `kanban_collect_evidence` | Collect evidence for task |
| 99 | `kanban_bulk_update` | Bulk assign/move/set dates |
| 100 | `kanban_export` | Export as CSV or POA&M |
| 101 | `kanban_archive_board` | Archive completed board |

#### A.8 PIM & Authentication Tools (Feature 003)

| # | Tool Name | Description |
|---|-----------|-------------|
| 102 | `pim_list_eligible` | List PIM-eligible roles |
| 103 | `pim_list_active` | List active PIM roles |
| 104 | `pim_activate_role` | Activate PIM role |
| 105 | `pim_deactivate_role` | Deactivate PIM role |
| 106 | `pim_extend_role` | Extend role activation |
| 107 | `pim_approve_request` | Approve activation request |
| 108 | `pim_deny_request` | Deny activation request |
| 109 | `pim_history` | View activation history |
| 110 | `jit_request_access` | Request just-in-time access |
| 111 | `jit_list_sessions` | List active JIT sessions |
| 112 | `jit_revoke_access` | Revoke JIT session |
| 113 | `cac_status` | Check CAC session status |
| 114 | `cac_sign_out` | Sign out CAC session |

---

### Appendix B: End-to-End Scenario — Full RMF Lifecycle

This scenario walks through the complete lifecycle of registering a system and taking it through ATO.

**Scenario**: "ACME Portal" — a mission-critical Major Application hosted in Azure Government (IL5).

#### Day 1: ISSM Registers the System

```
User (ISSM):
  Register a new system called 'ACME Portal' as a Major Application
  with mission-critical designation in Azure Government IL5

Security Posture Intelligence Navigator:
  ✅ System registered: ACME Portal (ID: sys-acme-001)
  Current RMF Phase: Prepare
  Next steps:
  1. Define the authorization boundary
  2. Assign RMF roles
  3. Advance to Categorize phase

User (ISSM):
  Define the authorization boundary — add 3 production VMs, the SQL
  database, Key Vault, and the App Service

  Assign Jane Smith as ISSM, Bob Jones as ISSO, Dr. Alice Chen as SCA,
  and COL Davis as Authorizing Official

  Advance to Categorize
```

#### Day 2: ISSM Categorizes the System

```
User (ISSM):
  Suggest information types for ACME Portal — it handles financial
  transactions and stores PII

Security Posture Intelligence Navigator:
  Suggested types (confidence scores):
  1. C.3.1.4 Financial Management (0.92)
  2. C.2.4.1 General Information (0.87)
  3. C.3.5.8 Information Security (0.85)

User (ISSM):
  Categorize with Financial Management (C:High, I:High, A:Moderate)
  and Information Security (C:Moderate, I:High, A:Moderate)

Security Posture Intelligence Navigator:
  ✅ Categorization complete:
  FIPS 199: SC ACME Portal = {(C, HIGH), (I, HIGH), (A, MODERATE)}
  Overall: HIGH | DoD IL: IL5 | Baseline: High (400 controls)
```

#### Days 3–5: ISSM Selects and Tailors Controls

```
User (ISSM):
  Select the High baseline with CNSSI 1253 overlay

  Set all physical security controls as inherited from Azure Government

  Generate the CRM

  Advance to Implement
```

#### Weeks 2–6: ISSO and Engineers Author SSP

```
User (ISSO):
  Auto-populate inherited control narratives

  Suggest narratives for all AC family customer controls

User (Engineer):
  Write the narrative for SC-7: "Network boundary protection is implemented
  using Azure Firewall Premium with TLS inspection, default-deny egress
  rules, and Azure DDoS Protection Standard..."

User (ISSO):
  What's the SSP completion percentage?

Security Posture Intelligence Navigator:
  SSP Progress: 78% complete
  ├─ AC: 18/20 ██████████████████░░ 90%
  ├─ AU: 12/14 █████████████████░░░ 86%
  ├─ SC: 15/22 ██████████████░░░░░░ 68%
  └─ (14 more families...)
  Missing: 88 narratives across 14 families
```

#### Weeks 7–8: SCA Assesses Controls

```
User (SCA):
  Assess AC-2 as Satisfied — tested Azure AD account management against
  STIG checklist, all 15 sub-requirements met

  Assess AC-3 as Other Than Satisfied, CAT II — missing mandatory access
  control for 3 storage accounts

  Take an assessment snapshot

  Generate the Security Assessment Report

Security Posture Intelligence Navigator:
  SAR Generated:
  Overall Score: 87.3%
  Findings: 0 CAT I, 12 CAT II, 31 CAT III
  Recommendation: Conditional authorization pending CAT II remediation
```

#### Week 9: ISSM Creates POA&M and Bundles Package

```
User (ISSM):
  Create POA&M items for all 12 CAT II findings

  Generate the RAR

  Bundle the authorization package with evidence
```

#### Week 10: AO Issues Decision

```
User (AO):
  Review the authorization package for ACME Portal

  Issue ATO with conditions — CAT II findings must be remediated within
  90 days, quarterly POA&M reviews required. Expiration: January 15, 2029.
  Accept risk on 5 CAT III findings with compensating controls.

Security Posture Intelligence Navigator:
  ✅ Authorization Issued: ATO with Conditions
  System: ACME Portal | Expires: 2029-01-15
  Risk Level: Medium | Score at Decision: 87.3%
  Conditions: CAT II remediation within 90 days
  Risk Acceptances: 5 findings accepted (expires 2027-01-15)
  System moved to Monitor phase
```

#### Ongoing: Continuous Monitoring

```
User (ISSM):
  Create a ConMon plan with monthly assessments, annual review June 15

  Generate the February 2026 ConMon report

  Show the multi-system dashboard

Security Posture Intelligence Navigator (automated):
  ⚠️ Alert: ATO for ACME Portal expires in 90 days
  Action: Begin reauthorization planning

  🔴 Alert: Configuration drift detected — 7 resources drifted beyond
  threshold. Significant change auto-reported.
```

---

### Appendix C: Glossary

| Term | Definition |
|------|-----------|
| **ATO** | Authority to Operate — formal authorization for a system to operate in a DoD environment |
| **ATOwC** | ATO with Conditions — authorization with specific remediation requirements |
| **AO** | Authorizing Official — senior leader who accepts risk and grants authorization |
| **CAC** | Common Access Card — DoD identity credential used for authentication |
| **CAT I/II/III** | Category severity levels for findings per DoDI 8510.01 |
| **CNSSI 1253** | Committee on National Security Systems Instruction — DoD overlay for NIST controls |
| **ConMon** | Continuous Monitoring — ongoing assessment of system compliance posture |
| **CRM** | Customer Responsibility Matrix — documents which controls are customer vs. CSP responsibility |
| **DATO** | Denial of Authorization to Operate — system is not authorized to operate |
| **eMASS** | Enterprise Mission Assurance Support Service — DoD GRC platform of record |
| **FIPS 199** | Federal Information Processing Standards Publication 199 — security categorization |
| **FedRAMP** | Federal Risk and Authorization Management Program — cloud authorization framework |
| **IATT** | Interim Authority to Test — limited authorization for testing purposes |
| **IL** | Impact Level — DoD classification (IL2 through IL6) |
| **ISSM** | Information System Security Manager — manages security program for systems |
| **ISSO** | Information System Security Officer — day-to-day security operations |
| **MCP** | Model Context Protocol — the API protocol used by Security Posture Intelligence Navigator |
| **NIST 800-53** | National Institute of Standards and Technology Special Publication 800-53 — security controls catalog |
| **OSCAL** | Open Security Controls Assessment Language — machine-readable compliance data format |
| **PIM** | Privileged Identity Management — just-in-time role activation |
| **POA&M** | Plan of Action and Milestones — tracks remediation of security findings |
| **RAR** | Risk Assessment Report — documents residual risk for AO decision |
| **RMF** | Risk Management Framework — DoD system authorization lifecycle (DoDI 8510.01) |
| **SAR** | Security Assessment Report — documents assessment findings for AO review |
| **SCA** | Security Control Assessor — independent assessor of control effectiveness |
| **SP 800-60** | NIST Special Publication 800-60 — information type categorization |
| **SSP** | System Security Plan — primary system authorization document |
| **STIG** | Security Technical Implementation Guide — DISA technology-specific security rules |

---

### Appendix D: Quick Reference Cards

#### ISSM Quick Reference

```
┌─────────────────────────────────────────────────────────┐
│                 ISSM Quick Reference                    │
├─────────────────────────────────────────────────────────┤
│ REGISTER:  "Register system {name} as {type} in {env}" │
│ BOUNDARY:  "Add resources to system {id}'s boundary"    │
│ ROLES:     "Assign {name} as {role} for system {id}"    │
│ CATEGORIZE:"Categorize system {id} with {info types}"   │
│ BASELINE:  "Select baseline for system {id}"            │
│ TAILOR:    "Remove {control} — {rationale}"             │
│ SSP:       "Generate SSP for system {id}"               │
│ POAM:      "Create POA&M for finding {id}"              │
│ PACKAGE:   "Bundle authorization package"               │
│ DASHBOARD: "Show multi-system dashboard"                │
│ CONMON:    "Generate monthly report for system {id}"    │
│ EMASS:     "Export system {id} to eMASS"                │
└─────────────────────────────────────────────────────────┘
```

#### SCA Quick Reference

```
┌─────────────────────────────────────────────────────────┐
│                  SCA Quick Reference                    │
├─────────────────────────────────────────────────────────┤
│ ASSESS:    "Assess {control} as {determination}"        │
│ SNAPSHOT:  "Take assessment snapshot for system {id}"    │
│ EVIDENCE:  "Verify evidence {id}"                       │
│ COMPLETE:  "Check evidence completeness for {family}"   │
│ COMPARE:   "Compare snapshots {a} and {b}"              │
│ SAR:       "Generate SAR for system {id}"               │
│ RAR:       "Generate RAR for system {id}"               │
│                                                         │
│ ⚠️ Read-only: Cannot modify system, fix findings,      │
│    or issue authorization decisions                     │
└─────────────────────────────────────────────────────────┘
```

#### AO Quick Reference

```
┌─────────────────────────────────────────────────────────┐
│                   AO Quick Reference                    │
├─────────────────────────────────────────────────────────┤
│ REVIEW:    "Show authorization package for system {id}" │
│ AUTHORIZE: "Issue ATO for system {id} expiring {date}"  │
│ CONDITIONS:"Issue ATOwC — {conditions}"                 │
│ RISK:      "Accept risk on finding {id} — {rationale}"  │
│ DENY:      "Deny authorization — {reason}"              │
│ REGISTER:  "Show risk register for system {id}"         │
│ DASHBOARD: "Show all my authorized systems"             │
│ EXPIRATION:"What ATOs expire in the next 90 days?"      │
└─────────────────────────────────────────────────────────┘
```

#### Engineer Quick Reference

```
┌─────────────────────────────────────────────────────────┐
│               Engineer Quick Reference                  │
├─────────────────────────────────────────────────────────┤
│ LEARN:     "What does {control} mean for Azure?"        │
│ STIG:      "What STIG rules apply to {technology}?"     │
│ SCAN:      "Scan my Bicep file for compliance"          │
│ NARRATIVE: "Suggest narrative for {control}"             │
│ WRITE:     "Write narrative for {control}: {text}"      │
│ TASKS:     "Show my assigned tasks"                     │
│ FIX:       "Fix task REM-{id} with dry run"             │
│ VALIDATE:  "Validate task REM-{id}"                     │
│ EVIDENCE:  "Collect evidence for task REM-{id}"         │
│ PROGRESS:  "Show narrative progress for {family}"       │
└─────────────────────────────────────────────────────────┘
```

---

### Appendix E: Documentation Delivery

**Primary Format**: Static documentation site generated from Markdown source files in `docs/` using MkDocs or DocFX.

- **Source**: Markdown files organized per-persona and per-phase in `docs/guides/`
- **Build**: CI/CD pipeline generates static site on merge to main
- **Search**: Full-text search across all documentation pages
- **Navigation**: Persona-based left nav with RMF phase sub-sections
- **Versioning**: Documentation versioned alongside product releases
- **Existing Guides**: Current `docs/guides/` files (issm-guide.md, sca-guide.md, ao-quick-reference.md, engineer-guide.md, compliance-watch.md, remediation-kanban.md) become pages in the site

### Appendix F: Common Errors & Troubleshooting

#### F.1 RBAC / Authorization Errors

| Error | Cause | Resolution |
|-------|-------|------------|
| `Access denied: Compliance.Auditor cannot invoke compliance_write_narrative` | SCA attempting to modify SSP (write operation) | SCA role is read-only by design. Ask the assigned ISSO or Engineer to write the narrative. |
| `Access denied: Compliance.PlatformEngineer cannot invoke compliance_issue_authorization` | Engineer attempting to issue an ATO decision | Only the AO (`Compliance.AuthorizingOfficial`) can issue authorization decisions. |
| `Access denied: Compliance.Analyst cannot invoke watch_dismiss_alert` | ISSO attempting to dismiss an alert | Only officers (`Compliance.SecurityLead` or above) can dismiss alerts. Escalate to ISSM. |
| `Role not recognized` | CAC certificate not mapped to any RBAC role | Contact Administrator to map your CAC thumbprint, or verify Azure AD group membership. Default fallback is `PlatformEngineer`. |

#### F.2 RMF Gate Validation Errors

| Error | Cause | Resolution |
|-------|-------|------------|
| `Cannot advance: Prepare → Categorize requires at least 1 RMF role and 1 boundary resource` | Tried to advance before assigning roles or defining boundary | Use `compliance_assign_rmf_role` and `compliance_define_boundary` first. |
| `Cannot advance: Categorize → Select requires SecurityCategorization` | Tried to advance before categorizing | Use `compliance_categorize_system` with at least one information type. |
| `Cannot advance: Select → Implement requires ControlBaseline` | Tried to advance before selecting baseline | Use `compliance_select_baseline` to select Low/Moderate/High baseline. |
| `Cannot regress RMF step without force: true` | Tried to move backward in the RMF lifecycle | Add `force: true` parameter to `compliance_advance_rmf_step` (ISSM only). This is intentionally guarded. |
| `System in DATO status: advancement blocked` | AO denied authorization; system is in read-only mode | The AO must issue a new authorization decision (ATO/ATOwC/IATT) before the system can advance. |

#### F.3 Evidence & Integrity Errors

| Error | Cause | Resolution |
|-------|-------|------------|
| `Evidence verification failed: hash mismatch` | Evidence artifact was modified after collection | Re-collect evidence using `compliance_collect_evidence`. If intentional modification, document the change and re-collect. |
| `Evidence completeness: 12 controls missing evidence` | Not all controls in scope have associated evidence | Use `compliance_check_evidence_completeness` to identify gaps, then collect evidence for each missing control. |
| `Snapshot creation failed: no assessment data` | Tried to take a snapshot before any controls were assessed | Assess at least one control using `compliance_assess_control` first. |

#### F.4 Authorization & Risk Errors

| Error | Cause | Resolution |
|-------|-------|------------|
| `Risk acceptance expired` | An accepted risk passed its expiration date | The AO must either re-accept the risk with a new expiration date or the finding reverts to active. Linked POA&M items revert to `Ongoing`. |
| `Authorization superseded` | A new authorization decision deactivated the prior one | Expected behavior — only one active authorization per system. Review the new decision. |
| `Cannot bundle: SAR not generated` | Tried to bundle authorization package before SCA generated the SAR | SCA must complete assessment and run `compliance_generate_sar` first. |
| `Cannot bundle: SSP not generated` | SSP has not been generated yet | Run `compliance_generate_ssp` to produce the SSP before bundling. |

#### F.5 Azure Connectivity Errors

| Error | Cause | Resolution |
|-------|-------|------------|
| `Azure Policy query failed: connection timeout` | Cannot reach Azure Resource Manager APIs | Check network connectivity. In air-gapped environments, use scheduled monitoring with local policy cache. |
| `Defender for Cloud unavailable` | Azure Defender not enabled or unreachable | Verify Defender for Cloud is enabled on the subscription. In disconnected mode, assessment runs against cached policy data only. |
| `Subscription not found: {sub-id}` | Subscription ID is incorrect or Security Posture Intelligence Navigator lacks access | Verify the subscription ID and ensure the Security Posture Intelligence Navigator service principal has Reader access. |

#### F.6 Monitoring & Alert Errors

| Error | Cause | Resolution |
|-------|-------|------------|
| `Monitoring already enabled for subscription {sub-id}` | Attempted to enable monitoring that is already active | Use `watch_configure_monitoring` to update frequency or mode instead. |
| `Alert not found: ALT-{id}` | Alert ID is incorrect or alert has been archived | Use `watch_show_alerts` to list current alerts. Archived alerts are available via `watch_alert_history`. |
| `Cannot dismiss: justification required` | Tried to dismiss alert without providing a reason | Include a justification string when calling `watch_dismiss_alert`. |
| `SLA escalation triggered` | Alert remained unacknowledged beyond SLA threshold | Acknowledge the alert immediately. Review escalation configuration with `watch_configure_escalation`. |
| `Quiet hours active: notification suppressed` | Notification not delivered due to quiet hours configuration | Alert is still recorded — check `watch_show_alerts` after quiet hours end. Adjust with `watch_configure_quiet_hours`. |

#### F.7 Kanban & Remediation Errors

| Error | Cause | Resolution |
|-------|-------|------------|
| `Cannot move to Done: validation failed` | Task remediation did not pass re-scan validation | Review the validation results, fix remaining issues, and re-validate with `kanban_task_validate`. An officer can override with `force: true`. |
| `Cannot move from Blocked: resolution comment required` | Tried to unblock a task without explaining the resolution | Add a comment explaining how the blocker was resolved before moving the task. |
| `Cannot reopen: Done is terminal` | Tried to move a completed task back to an earlier column | Create a new task if additional work is needed on the same finding. |
| `Dry run completed: no changes applied` | Used `dryRun: true` on remediation | Expected behavior — review the dry run output, then re-run without `dryRun` to apply changes. |

---

*This specification covers all user interactions with Security Posture Intelligence Navigator across all personas and all RMF phases. It serves as both a planning document for feature development and a foundation for end-user documentation.*
