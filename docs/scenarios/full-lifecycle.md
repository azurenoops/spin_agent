# End-to-End Scenario: ACME Portal

> Full RMF lifecycle walkthrough — from system registration through continuous monitoring.

---

## Scenario Overview

**System**: ACME Portal — a mission-critical Major Application hosted in Azure Government (IL5).

This scenario follows five personas through every RMF phase, demonstrating natural language queries and tool interactions at each step.

| Property | Value |
|----------|-------|
| System Name | ACME Portal |
| System Type | Major Application |
| Environment | Azure Government |
| Impact Level | IL5 |
| Overall Impact | HIGH |
| ISSM | Jane Smith |
| ISSO | Bob Jones |
| SCA | Dr. Alice Chen |
| AO | COL Davis |

---

## Day 1 — Prepare: ISSM Registers the System

### Step 1: Register

```text
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
```

### Step 2: Define Boundary

```text
User (ISSM):
  Define the authorization boundary — add 3 production VMs, the SQL
  database, Key Vault, and the App Service

Security Posture Intelligence Navigator:
  ✅ Authorization boundary updated: 6 resources added
  Resources: 3× Virtual Machine, 1× SQL Database, 1× Key Vault, 1× App Service
```

### Step 3: Assign Roles

```text
User (ISSM):
  Assign Jane Smith as ISSM, Bob Jones as ISSO, Dr. Alice Chen as SCA,
  and COL Davis as Authorizing Official

Security Posture Intelligence Navigator:
  ✅ Roles assigned:
  • Jane Smith → ISSM (Compliance.SecurityLead)
  • Bob Jones → ISSO (Compliance.Analyst)
  • Dr. Alice Chen → SCA (Compliance.Auditor)
  • COL Davis → AO (Compliance.AuthorizingOfficial)
```

### Step 4: Advance to Categorize

```text
User (ISSM):
  Advance to Categorize

Security Posture Intelligence Navigator:
  ✅ Phase advanced: Prepare → Categorize
  Gate conditions met: ✓ 4 RMF roles assigned, ✓ 6 boundary resources
```

---

## Day 2 — Categorize: ISSM Classifies the System

### Step 1: Suggest Information Types

```text
User (ISSM):
  Suggest information types for ACME Portal — it handles financial
  transactions and stores PII

Security Posture Intelligence Navigator:
  Suggested types (confidence scores):
  1. C.3.1.4 Financial Management (0.92)
  2. C.2.4.1 General Information (0.87)
  3. C.3.5.8 Information Security (0.85)
```

### Step 2: Categorize

```text
User (ISSM):
  Categorize with Financial Management (C:High, I:High, A:Moderate)
  and Information Security (C:Moderate, I:High, A:Moderate)

Security Posture Intelligence Navigator:
  ✅ Categorization complete:
  FIPS 199: SC ACME Portal = {(C, HIGH), (I, HIGH), (A, MODERATE)}
  Overall: HIGH | DoD IL: IL5 | Baseline: High (400 controls)
```

### Step 3: Advance to Select

```text
User (ISSM):
  Advance to Select

Security Posture Intelligence Navigator:
  ✅ Phase advanced: Categorize → Select
  Gate conditions met: ✓ FIPS 199 categorization recorded
```

---

## Days 3–5 — Select: ISSM Tailors Controls

### Step 1: Select Baseline

```text
User (ISSM):
  Select the High baseline with CNSSI 1253 overlay

Security Posture Intelligence Navigator:
  ✅ Baseline selected: High (400 controls)
  Overlay applied: CNSSI 1253
  Total controls in scope: 412
```

### Step 2: Set Inheritance

```text
User (ISSM):
  Set all physical security controls as inherited from Azure Government

Security Posture Intelligence Navigator:
  ✅ Inheritance set for 23 controls:
  PE-1 through PE-23 → Fully Inherited from Azure Government
  Provider: Azure Government FedRAMP High P-ATO
```

### Step 3: Generate CRM

```text
User (ISSM):
  Generate the CRM

Security Posture Intelligence Navigator:
  ✅ CRM generated: 412 controls mapped
  • Fully Inherited: 23 controls
  • Shared: 89 controls
  • Customer Responsible: 300 controls
```

### Step 4: Advance to Implement

```text
User (ISSM):
  Advance to Implement

Security Posture Intelligence Navigator:
  ✅ Phase advanced: Select → Implement
  Gate conditions met: ✓ Baseline selected with 412 controls
```

---

## Weeks 2–6 — Implement: ISSO and Engineers Author SSP

### ISSO: Auto-Populate Inherited Narratives

```text
User (ISSO):
  Auto-populate inherited control narratives

Security Posture Intelligence Navigator:
  ✅ 112 narratives auto-populated:
  • Fully Inherited: 23 narratives from Azure Gov P-ATO
  • Shared (provider portion): 89 narratives
  Remaining: 300 customer narratives needed
```

### ISSO: AI-Suggested Narratives

```text
User (ISSO):
  Suggest narratives for all AC family customer controls

Security Posture Intelligence Navigator:
  Generated 20 narrative suggestions for AC family:
  • AC-1: Access Control Policy (confidence: 0.91) ✅
  • AC-2: Account Management (confidence: 0.88) ✅
  • AC-3: Access Enforcement (confidence: 0.85) ✅
  • ... (17 more)
  ⚠️ All suggestions require human review before acceptance
```

### Engineer: Write Technical Narrative

```text
User (Engineer):
  Write the narrative for SC-7: "Network boundary protection is implemented
  using Azure Firewall Premium with TLS inspection, default-deny egress
  rules, and Azure DDoS Protection Standard. All inbound traffic routes
  through Azure Front Door with WAF policy v2..."

Security Posture Intelligence Navigator:
  ✅ Narrative saved for SC-7 (Boundary Protection)
  Author: Engineer | Status: Draft
  Word count: 287 | Sections: Implementation, Evidence
```

### ISSO: Track Progress

```text
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

---

## Weeks 7–8 — Assess: SCA Evaluates Controls

### SCA: Assess Controls

```text
User (SCA):
  Assess AC-2 as Satisfied — tested Azure AD account management against
  STIG checklist, all 15 sub-requirements met

Security Posture Intelligence Navigator:
  ✅ AC-2 assessed: Satisfied
  Method: Test | Evidence: Azure AD audit logs, STIG checklist
```

```text
User (SCA):
  Assess AC-3 as Other Than Satisfied, CAT II — missing mandatory access
  control for 3 storage accounts

Security Posture Intelligence Navigator:
  ✅ AC-3 assessed: Other Than Satisfied (CAT II)
  Finding: Missing MAC for 3 storage accounts
  POA&M recommended: Yes
```

### SCA: Take Snapshot

```text
User (SCA):
  Take an assessment snapshot

Security Posture Intelligence Navigator:
  ✅ Snapshot created: SNAP-2026-02-15-001
  Controls assessed: 412 | Score: 87.3%
  Findings: 0 CAT I, 12 CAT II, 31 CAT III
  Hash: sha256:a1b2c3d4... (immutable)
```

### SCA: Generate SAR

```text
User (SCA):
  Generate the Security Assessment Report

Security Posture Intelligence Navigator:
  ✅ SAR generated: SAR-ACME-2026-001
  Sections: Executive Summary, Methodology, Findings (43), Recommendations
  Overall Score: 87.3%
  Findings: 0 CAT I, 12 CAT II, 31 CAT III
  Recommendation: Conditional authorization pending CAT II remediation
```

---

## Week 9 — ISSM Prepares Authorization Package

### Create POA&M

```text
User (ISSM):
  Create POA&M items for all 12 CAT II findings

Security Posture Intelligence Navigator:
  ✅ 12 POA&M items created:
  • POA&M-001: AC-3 — Missing MAC for storage accounts (90 days)
  • POA&M-002: AU-6 — Incomplete audit review process (60 days)
  • ... (10 more)
  Total estimated remediation: 90 days
```

### Generate RAR

```text
User (ISSM):
  Generate the RAR

Security Posture Intelligence Navigator:
  ✅ RAR generated: RAR-ACME-2026-001
  Sections: Risk Summary, Residual Risks (43), Recommendations
  Overall Risk Level: Medium
```

### Bundle Package

```text
User (ISSM):
  Bundle the authorization package with evidence

Security Posture Intelligence Navigator:
  ✅ Authorization package bundled: PKG-ACME-2026-001
  Contents:
  • System Security Plan (SSP)
  • Security Assessment Report (SAR)
  • Risk Assessment Report (RAR)
  • Plan of Action & Milestones (12 items)
  • Customer Responsibility Matrix (CRM)
  • Evidence artifacts (SHA-256 verified)
```

---

## Week 10 — Authorize: AO Issues Decision

### AO: Review Package

```text
User (AO):
  Review the authorization package for ACME Portal

Security Posture Intelligence Navigator:
  Authorization Package: PKG-ACME-2026-001
  ┌──────────────────────────────────────┐
  │ System: ACME Portal (IL5)            │
  │ Score: 87.3% | Risk: Medium          │
  │ CAT I: 0 | CAT II: 12 | CAT III: 31 │
  │ POA&M: 12 items (90-day plan)        │
  │ SCA Recommendation: ATOwC            │
  └──────────────────────────────────────┘
```

### AO: Issue Authorization

```text
User (AO):
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

---

## Ongoing — Monitor: Continuous Monitoring

### ISSM: Create ConMon Plan

```text
User (ISSM):
  Create a ConMon plan with monthly assessments, annual review June 15

Security Posture Intelligence Navigator:
  ✅ ConMon plan created:
  • Monthly assessment: 1st of each month
  • Annual review: June 15
  • POA&M review: Quarterly
  • ATO expiration: January 15, 2029
```

### ISSM: Generate Monthly Report

```text
User (ISSM):
  Generate the February 2026 ConMon report

Security Posture Intelligence Navigator:
  ✅ ConMon report generated: ConMon-ACME-2026-02
  Score: 89.1% (↑1.8% from January)
  POA&M: 3 of 12 items closed
  New findings: 0 | Resolved: 3
  Next report due: March 1, 2026
```

### ISSM: Portfolio Dashboard

```text
User (ISSM):
  Show the multi-system dashboard

Security Posture Intelligence Navigator:
  ┌──────────────┬─────┬─────────┬───────┬─────────┬──────┐
  │ System       │ IL  │ Phase   │ Auth  │ Score   │ Exp  │
  ├──────────────┼─────┼─────────┼───────┼─────────┼──────┤
  │ ACME Portal  │ IL5 │ Monitor │ ATOwC │  89.1%  │ 2029 │
  │ HR System    │ IL4 │ Monitor │ ATO   │  94.2%  │ 2028 │
  │ Log Analyzer │ IL2 │ Assess  │ None  │  72.4%  │  —   │
  └──────────────┴─────┴─────────┴───────┴─────────┴──────┘
```

### Automated: Expiration Alert

```text
Security Posture Intelligence Navigator (automated):
  ⚠️ Alert: ATO for ACME Portal expires in 90 days
  Action: Begin reauthorization planning
```

### Automated: Drift Detection

```text
Security Posture Intelligence Navigator (automated):
  🔴 Alert: Configuration drift detected — 7 resources drifted beyond
  threshold. Significant change auto-reported.
  Action required: ISSO must review and ISSM must determine if
  reauthorization is needed.
```

---

## Scenario Summary

| Phase | Duration | Lead | Key Outputs |
|-------|----------|------|-------------|
| Prepare | Day 1 | ISSM | System registered, boundary defined, roles assigned |
| Categorize | Day 2 | ISSM | FIPS 199: HIGH, IL5, 400-control baseline |
| Select | Days 3–5 | ISSM | 412 controls, 23 inherited, CRM generated |
| Implement | Weeks 2–6 | ISSO/Eng | SSP at 100% completion (412 narratives) |
| Assess | Weeks 7–8 | SCA | SAR: 87.3%, 0 CAT I, 12 CAT II, 31 CAT III |
| Authorize | Week 10 | AO | ATOwC issued, expires 2029-01-15 |
| Monitor | Ongoing | ISSM/ISSO | Monthly ConMon, Watch alerts, POA&M tracking |

---

## See Also

- [RMF Phase Overview](../rmf-phases/index.md) — Phase-by-phase documentation
- [Persona Overview](../personas/index.md) — Role definitions
- [NL Query Reference](../guides/nl-query-reference.md) — Full query catalog
- [Getting Started Hub](../getting-started/index.md) — Per-persona onboarding
