# ATO Copilot Documentation

> AI-powered NIST Risk Management Framework compliance assistant for DoD teams.

---

## What Is ATO Copilot?

ATO Copilot is an AI-powered assistant that guides DoD teams through every step of the NIST Risk Management Framework (RMF) — from system registration through continuous monitoring. It combines real Azure compliance scanning with RMF workflow automation, natural language interaction, and document generation.

**ATO Copilot IS:**

- A copilot that knows the RMF process and guides users step by step
- An assistant with the full NIST 800-53 Rev 5 catalog embedded (1,000+ controls)
- A scanner that queries real Azure infrastructure via Policy, Defender, and ARM APIs
- A document generator that produces SSP, SAR, RAR, POA&M, and CRM from actual assessment data
- A remediation tracker with Kanban boards linked to compliance findings
- A continuous monitor that detects compliance drift and creates graduated alerts
- A natural language interface where each persona sees information tailored to their role

**ATO Copilot is NOT:**

- A replacement for eMASS (it exports *to* eMASS)
- A GRC platform (it is a productivity copilot)
- A vulnerability scanner (it orchestrates Azure Policy + Defender for Cloud)

---

## Choose Your Persona

Select your role to get started:

| Persona | Role | Start Here |
|---------|------|------------|
| **ISSM** | Information System Security Manager | [Getting Started](getting-started/issm.md) · [Full Guide](guides/issm-guide.md) |
| **ISSO** | Information System Security Officer | [Getting Started](getting-started/isso.md) · [Full Guide](personas/isso.md) |
| **SCA** | Security Control Assessor | [Getting Started](getting-started/sca.md) · [Full Guide](guides/sca-guide.md) |
| **AO** | Authorizing Official | [Getting Started](getting-started/ao.md) · [Full Guide](guides/ao-quick-reference.md) |
| **Engineer** | Platform Engineer / System Owner | [Getting Started](getting-started/engineer.md) · [Full Guide](guides/engineer-guide.md) |
| **Administrator** | Copilot Infrastructure Management | [Full Guide](personas/administrator.md) |

---

## Supported Interfaces

| Surface | Primary Users | How to Access |
|---------|--------------|---------------|
| **VS Code (GitHub Copilot Chat)** | Engineers, ISSOs | `@ato` participant with `/compliance`, `/knowledge`, `/config` slash commands |
| **Microsoft Teams (M365 Bot)** | ISSMs, AOs, SCAs | Adaptive Cards for dashboards, assessments, approvals |
| **MCP Server API** | All (via any MCP client) | REST + SSE + stdio transport — powers all surfaces above |
| **CLI** | Engineers, ISSOs | Direct MCP tool invocations for scripting and automation |

---

## Applicable Standards

| Standard | How ATO Copilot Uses It |
|----------|------------------------|
| DoDI 8510.01 | Defines the 7-step RMF lifecycle ATO Copilot implements |
| NIST SP 800-37 Rev 2 | RMF framework including Step 0 (Prepare) |
| NIST SP 800-53 Rev 5 | Full control catalog embedded (254K lines, sourced from OSCAL) |
| NIST SP 800-60 Vol 1 & 2 | Information type catalog for FIPS 199 categorization |
| FIPS 199 | Security categorization (C/I/A impact → Low/Moderate/High) |
| CNSSI 1253 | DoD overlay controls mapped to Impact Levels (IL2–IL6) |
| DISA STIGs | Technology-specific security configuration rules |

---

## RBAC Roles

ATO Copilot enforces role-based access control at every tool invocation. Your role determines what you can do:

| RBAC Role | Maps To | Access Level |
|-----------|---------|-------------|
| `Compliance.Administrator` | Administrator | Full infrastructure management, all tools |
| `Compliance.SecurityLead` | ISSM | System registration, categorization, SSP, POA&M, ConMon, dashboards |
| `Compliance.Analyst` | ISSO | Control narratives, evidence collection, remediation execution, monitoring |
| `Compliance.Auditor` | SCA | Assessment, effectiveness determination, SAR/RAR generation (read-only) |
| `Compliance.AuthorizingOfficial` | AO | Authorization decisions, risk acceptance, risk register |
| `Compliance.PlatformEngineer` | Engineer | IaC scanning, STIG lookups, remediation tasks, evidence collection |
| `Compliance.Viewer` | Stakeholder | Read-only access to dashboards and reports |

**Role Resolution**: CAC certificate → 4-tier chain: (1) explicit mapping by thumbprint, (2) Azure AD group membership, (3) Azure RBAC on subscription, (4) default to PlatformEngineer.

---

## Quick Links

- [RMF Phase Reference](rmf-phases/index.md) — Step-by-step walkthrough of all 7 RMF phases
- [NL Query Reference](guides/nl-query-reference.md) — Natural language query examples by category
- [Tool Inventory](reference/tool-inventory.md) — Complete list of 114 MCP tools
- [Document Catalog](guides/document-catalog.md) — All documents ATO Copilot produces
- [Troubleshooting](reference/troubleshooting.md) — Common errors and resolutions
- [Quick Reference Cards](reference/quick-reference-cards.md) — Printable cheat sheets by persona
- [Glossary](reference/glossary.md) — Terms and definitions
- [Database Providers](database-providers.md) — Supported providers, configuration, connection strings, and migration usage
