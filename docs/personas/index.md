# Persona Overview

> ATO Copilot supports five primary personas aligned to NIST SP 800-37 RMF roles, plus an infrastructure Administrator role.

---

## Role Definitions

| Persona | RBAC Role | Description | Primary Interface |
|---------|-----------|-------------|-------------------|
| **ISSM** | `Compliance.SecurityLead` | Manages the security program; owns the authorization package | Teams, MCP API |
| **ISSO** | `Compliance.Analyst` | Day-to-day security operations — implements and monitors controls | VS Code (`@ato`), Teams |
| **SCA** | `Compliance.Auditor` | Independent assessor of control effectiveness (read-only) | Teams, MCP API |
| **AO** | `Compliance.AuthorizingOfficial` | Accepts risk and issues authorization decisions | Teams (Adaptive Cards) |
| **Engineer** | `Compliance.PlatformEngineer` | Builds/operates the system; implements controls and fixes findings | VS Code (`@ato`) |
| **Mission Owner** | `MissionOwner` | System mission authority — provides business context and system details | Dashboard, VS Code (`@ato`) |
| **Administrator** | `Compliance.Administrator` | Manages ATO Copilot templates and infrastructure configuration | MCP API, VS Code |

---

## RACI Matrix — Persona Responsibilities by RMF Phase

| RMF Phase | ISSM | ISSO | SCA | AO | Engineer | Mission Owner |
|-----------|------|------|-----|-----|----------|---------------|
| **Prepare** | **R** (Lead) | A (Support) | — | I (Informed) | A (Support) | — |
| **Categorize** | **R** (Lead) | A (Support) | — | I (Informed) | C (Consulted) | A (System Profile) |
| **Select** | **R** (Lead) | A (Support) | C (Review) | — | C (Consulted) | — |
| **Implement** | I (Oversight) | **R** (Lead) | — | — | **R** (Lead) | A (Business Context) |
| **Assess** | A (Support) | A (Support) | **R** (Lead) | I (Informed) | A (Support) | — |
| **Authorize** | A (Package) | A (Support) | A (SAR delivery) | **R** (Decide) | — | — |
| **Monitor** | **R** (Lead) | **R** (Day-to-day) | C (Periodic) | I (Escalation) | A (Remediation) | I (Informed) |

**Legend**: **R** = Responsible (does the work), **A** = Accountable (supports/assists), **C** = Consulted, **I** = Informed

---

## RBAC Role Resolution

ATO Copilot resolves user roles through a 4-tier chain:

1. **Custom Header** — `X-User-Roles` (used in development/testing)
2. **Azure AD Group** — Mapped from group membership claims in JWT
3. **System-Level RMF Assignment** — Per-system role from `compliance_assign_rmf_role`
4. **Default** — `Compliance.PlatformEngineer` (if no explicit mapping exists)

The most specific match wins. System-level assignments enable the same user to hold different roles on different systems (e.g., ISSO on System A, Engineer on System B).

---

## Separation of Duties

ATO Copilot enforces separation between roles per DoDI 8510.01:

| Constraint | Enforcement |
|------------|-------------|
| SCA independence | `Compliance.Auditor` is read-only — cannot modify SSP, fix findings, or authorize |
| AO separation | `Compliance.AuthorizingOfficial` is distinct from `Compliance.Administrator` |
| Officer-only dismissal | Only `Compliance.SecurityLead` (ISSM) can dismiss alerts via `watch_dismiss_alert` |
| Assessment exclusivity | Only `Compliance.Auditor` (SCA) can record assessment determinations via `compliance_assess_control` |
| Authorization exclusivity | Only `Compliance.AuthorizingOfficial` (AO) can issue decisions via `compliance_issue_authorization` |

---

## Persona Guides

| Persona | Getting Started | Full Guide |
|---------|----------------|------------|
| ISSM | [Getting Started](../getting-started/issm.md) | [ISSM Guide](../guides/issm-guide.md) |
| ISSO | [Getting Started](../getting-started/isso.md) | [ISSO Guide](isso.md) |
| SCA | [Getting Started](../getting-started/sca.md) | [SCA Guide](../guides/sca-guide.md) |
| AO | [Getting Started](../getting-started/ao.md) | [AO Guide](../guides/ao-quick-reference.md) |
| Engineer | [Getting Started](../getting-started/engineer.md) | [Engineer Guide](../guides/engineer-guide.md) |
| Administrator | — | [Administrator Guide](administrator.md) |

---

## Cross-Persona Handoff Summary

| From | To | Trigger | What Transfers |
|------|----|---------|----------------|
| ISSM → ISSO | System registered, baseline selected | System ID, control list for narrative authoring |
| ISSM → Engineer | Controls need implementation | Kanban tasks, STIG requirements |
| ISSO → Engineer | Findings identified | Kanban remediation tasks with severity and SLA |
| Engineer → ISSO | Remediation complete | Task moved to InReview, evidence collected |
| ISSO → SCA | SSP ready for assessment | System ID, assessment scope, evidence |
| SCA → ISSM | Assessment complete | SAR, RAR, effectiveness determinations |
| ISSM → AO | Package ready for decision | Bundled authorization package |
| AO → ISSM | Decision issued | Authorization decision, risk acceptances, conditions |
| ISSO → ISSM | Significant change detected | Change record, reauthorization flag |
| Watch → ISSO | Drift/violation detected | Alert with severity, control ID, resource ID |
| ISSO → ISSM | SLA escalation | Unacknowledged critical/high alerts |

---

## See Also

- [RMF Phase Reference](../rmf-phases/index.md) — Phase-by-phase workflow details
- [Tool Inventory](../reference/tool-inventory.md) — Complete list of 114 MCP tools
- [Quick Reference Cards](../reference/quick-reference-cards.md) — Per-persona cheat sheets
