# Glossary

> Acronyms, abbreviations, and key terms used throughout ATO Copilot.

---

## #

| Term | Definition |
|------|-----------|
| **3-Layer Model** | The Capabilities Hub architecture: Components (provider grouping) → Capabilities (security solutions) → Control Mappings (NIST control links with roles). Introduced in Feature 045 |

## A

| Term | Definition |
|------|-----------|
| **AO** | Authorizing Official — Senior official with authority to formally assume responsibility for operating a system at an acceptable level of risk |
| **ATO** | Authority to Operate — Formal authorization granted by the AO for a system to operate |
| **ATOwC** | ATO with Conditions — Authorization that includes specific constraints or compensating controls |
| **ACAS** | Assured Compliance Assessment Solution — DoD-standard vulnerability scanning system based on Tenable Nessus. Scans are imported via `compliance_import_nessus` |
| **Approval Workflow** | Narrative governance process where ISSOs submit Draft narratives for ISSM review, transitioning through Draft → InReview → Approved or NeedsRevision status lifecycle |

## B

| Term | Definition |
|------|-----------|
| **Baseline** | A predefined set of NIST 800-53 controls appropriate for a system's impact level (Low, Moderate, High) |

## C

| Term | Definition |
|------|-----------|
| **CAC** | Common Access Card — DoD smart card used for PKI-based authentication |
| **Capabilities Hub** | The unified Security Capabilities Hub page in the Dashboard. Manages CSP profile imports, CRM imports, capability-control mappings, coverage metrics, and component linking (Feature 045) |
| **CAT** | Category (STIG severity) — CAT I (Critical), CAT II (Significant), CAT III (Low) |
| **CCI** | Control Correlation Identifier — Maps NIST 800-53 controls to STIG rules |
| **CKL** | Checklist (DISA STIG Viewer format) — XML file containing STIG assessment results for a specific benchmark |
| **CISO** | Chief Information Security Officer |
| **CM** | Continuous Monitoring — Ongoing awareness of security posture (NIST SP 800-137) |
| **CMRS** | Cybersecurity Mitigation and Remediation System |
| **CNSSI** | Committee on National Security Systems Instruction |
| **ConMon** | Continuous Monitoring — See CM |
| **CRM** | Customer Responsibility Matrix — Documents shared/inherited control responsibilities between provider and tenant |
| **CSP** | Cloud Service Provider |
| **Coverage %** | The percentage of NIST baseline controls mapped to at least one security capability. Displayed on the Capabilities Hub KPI bar |
| **CUI** | Controlled Unclassified Information — Sensitive but unclassified information requiring safeguarding |

## D

| Term | Definition |
|------|-----------|
| **DATO** | Denial of Authorization to Operate — AO determination that a system must not operate |
| **DISA** | Defense Information Systems Agency |
| **DITPR** | DoD IT Portfolio Repository |

## E

| Term | Definition |
|------|-----------|
| **eMASS** | Enterprise Mission Assurance Support Service — DoD's official RMF workflow and artifact management system |

## F

| Term | Definition |
|------|-----------|
| **FedRAMP** | Federal Risk and Authorization Management Program — Standardized approach for security assessment of cloud services |
| **FIPS** | Federal Information Processing Standards |
| **FIPS 140-2** | Security standard for cryptographic modules |
| **FIPS 199** | Standards for Security Categorization of Federal Information and Information Systems |
| **FIPS 200** | Minimum Security Requirements for Federal Information and Information Systems — defines baseline control requirements |
| **FOUO** | For Official Use Only (legacy marking, replaced by CUI) |

## G–H

| Term | Definition |
|------|-----------|
| **Gap Controls** | NIST baseline controls that are not yet mapped to any security capability. Displayed on the Capabilities Hub coverage dashboard |
| **GRC** | Governance, Risk, and Compliance |
| **Hardware Inventory** | Catalog of physical and virtual computing resources (servers, workstations, network devices, storage) within a system's authorization boundary |

## I

| Term | Definition |
|------|-----------|
| **IaC** | Infrastructure as Code — Machine-readable configuration files (Terraform, Bicep, ARM templates) |
| **Inventory Completeness Check** | Automated assessment verifying all inventory items have required fields, all boundary resources have inventory entries, and all hardware has registered software |
| **ISA** | Interconnection Security Agreement — Document governing the security of a connection between two systems |
| **ISCP** | Information System Contingency Plan — Plan for restoring system operations after disruption |
| **IATT** | Interim Authority to Test — Temporary authorization for testing purposes |
| **IL** | Impact Level — DoD classification tier (IL2–IL6) determining security requirements |
| **ISSM** | Information System Security Manager — Responsible for day-to-day security operations of a system |
| **ISSO** | Information System Security Officer — Supports the ISSM in managing system security |

## J

| Term | Definition |
|------|-----------|
| **JIT** | Just-In-Time — Time-limited privilege elevation requiring explicit activation and approval |

## K–L

| Term | Definition |
|------|-----------|
| **Kanban** | ATO Copilot's visual task management system for tracking remediation work through columns (Backlog → To Do → In Progress → Review → Done) |

## M

| Term | Definition |
|------|-----------|
| **MCP** | Model Context Protocol — Protocol used by ATO Copilot to expose tools and prompts to AI assistants |
| **MOA** | Memorandum of Agreement — Formal agreement between organizations defining roles and responsibilities |
| **MOU** | Memorandum of Understanding — Agreement between organizations to share information or resources |

## N

| Term | Definition |
|------|-----------|
| **NIST** | National Institute of Standards and Technology |
| **NIST 800-37** | Risk Management Framework for Information Systems and Organizations (Rev. 2) |
| **NIST 800-53** | Security and Privacy Controls for Information Systems and Organizations (Rev. 5) |
| **NIST 800-60** | Guide for Mapping Types of Information and Information Systems to Security Categories |
| **NIST 800-137** | Information Security Continuous Monitoring for Federal Information Systems |
| **Narrative Governance** | Version control and approval workflow system for SSP control narratives — provides version history, line-level diffing, rollback, and ISSM approval workflows (Feature 024) |
| **Narrative Version** | An immutable, append-only record capturing the full text of a control narrative at a point in time, with authorship, status, and optional change reason |
| **Nessus** | Tenable vulnerability scanner that produces .nessus XML files. Plugin results are mapped to NIST 800-53 controls via `compliance_import_nessus` |
| **NSS** | National Security System |

## O

| Term | Definition |
|------|-----------|
| **ODP** | Organization-Defined Parameter — Customizable values within NIST 800-53 controls |
| **Org Default** | An org-level inheritance default derived from capability-control mappings in the Security Capabilities Library. Cascades to all system baselines automatically |
| **Org Propagation** | The automatic cascade of org-level inheritance defaults to individual system baselines when defaults are derived or capabilities change |
| **OSCAL** | Open Security Controls Assessment Language — NIST standard for machine-readable compliance data (v1.0.6 supported) |

## P

| Term | Definition |
|------|-----------|
| **PIM** | Privileged Identity Management — Azure AD feature for JIT role elevation |
| **PIV** | Personal Identity Verification — Federal smart card standard (FIPS 201) |
| **PIA** | Privacy Impact Assessment — Analysis of how PII is collected, stored, shared, and protected |
| **POA&M** | Plan of Action and Milestones — Document identifying tasks to correct security weaknesses |
| **PTA** | Privacy Threshold Analysis — Determination of whether a system requires a PIA based on PII handling |

## Q–R

| Term | Definition |
|------|-----------|
| **RAR** | Risk Assessment Report — Documents risks identified during security assessment |
| **RBAC** | Role-Based Access Control — Authorization model based on assigned roles |
| **Review Decision** | ISSM verdict on a submitted narrative — either Approve (transitions to Approved status) or Request Revision (transitions to NeedsRevision with mandatory reviewer comments) |
| **RMF** | Risk Management Framework — NIST framework for managing security risk (SP 800-37) |

## S

| Term | Definition |
|------|-----------|
| **SAP** | Security Assessment Plan — Document defining the scope, methodology, and schedule for security assessment |
| **SAR** | Security Assessment Report — Documents assessment results and findings |
| **SCA** | Security Control Assessor — Person who assesses the effectiveness of security controls |
| **SCAP** | Security Content Automation Protocol — Standard for automated vulnerability management |
| **SCIF** | Sensitive Compartmented Information Facility |
| **SRG** | Security Requirements Guide — DISA document specifying security requirements for a technology area |
| **SSP** | System Security Plan — Formal document describing how security controls are implemented |
| **SSP Section** | One of 13 NIST 800-18 sections composing a System Security Plan (System Identification through Contingency Planning) |
| **STIG** | Security Technical Implementation Guide — DISA configuration standard for IT products |

## T

| Term | Definition |
|------|-----------|
| **TLS** | Transport Layer Security — Cryptographic protocol for secure communications |

## U–V

| Term | Definition |
|------|-----------|
| **USCYBERCOM** | United States Cyber Command — combatant command that may direct cyber operations affecting system authorization |

## W

| Term | Definition |
|------|-----------|
| **Watch** | ATO Copilot's Compliance Watch subsystem — real-time monitoring, alerting, and auto-remediation engine |

## X

| Term | Definition |
|------|-----------|
| **XCCDF** | Extensible Configuration Checklist Description Format — SCAP specification for security checklists |

## Y–Z

| Term | Definition |
|------|-----------|

---

## ATO Copilot-Specific Terms

| Term | Definition |
|------|-----------|
| **Adaptive Card** | Microsoft Teams card format (v1.5) used to render structured compliance data |
| **Agent** | AI-powered compliance assistant implementing `BaseAgent` for domain-specific reasoning |
| **BaseTool** | Abstract base class for all MCP tools; provides parameter validation, authorization, and execution framework |
| **Compliance Gate** | CI/CD action that blocks deployments when CAT I/II STIG findings or unmitigated risks exist |
| **Control Effectiveness** | Assessment record tracking whether a control is Satisfied, Other Than Satisfied, or Not Applicable |
| **Control Inheritance** | Mechanism where a system inherits controls from an underlying provider (e.g., cloud platform). Supports manual, CSP profile, CRM import, and org-level default sources |
| **Control Tailoring** | Process of customizing a baseline by scoping, compensating, or adding controls |
| **Gate Condition** | Prerequisite that must be met before an RMF phase transition can occur |
| **High-Water Mark** | FIPS 199 method of determining system impact level from the highest CIA impact among all information types |
| **MCP Bridge** | HTTP-to-MCP translation layer (`McpHttpBridge`) connecting REST clients to the MCP server |
| **Persona** | RBAC role representing a compliance stakeholder (ISSM, SCA, Engineer, AO, etc.) |
| **Registered System** | An information system registered in ATO Copilot for RMF lifecycle management |
| **Risk Acceptance** | Formal AO decision to accept residual risk for a specific control finding |
| **RMF Step** | One of seven lifecycle phases: Prepare, Categorize, Select, Implement, Assess, Authorize, Monitor |
| **Auto-Seed** | ATO Copilot feature that automatically creates hardware inventory items from authorization boundary resources, mapping Azure resource types to hardware functions |
| **eMASS HW/SW Export** | Excel workbook export with Hardware and Software worksheets formatted for import into the Enterprise Mission Assurance Support Service (eMASS) |
| **Significant Change** | An event that may require system reauthorization per NIST SP 800-37 |
| **Software Inventory** | Catalog of software applications, operating systems, middleware, and security tools installed on hardware within a system's authorization boundary |

---

## Feature 033 Terms

| Term | Definition |
|------|-----------|
| **Authorization Boundary Definition** | A named security perimeter within a registered system (e.g., "Production", "Dev/Test") that groups resources, components, and capability mappings |
| **Physical Boundary** | An authorization boundary defined by physical infrastructure (e.g., data center, secure room) |
| **Logical Boundary** | An authorization boundary defined by logical infrastructure (e.g., cloud subscription, VLAN, resource group) |
| **Hybrid Boundary** | An authorization boundary combining physical and logical security perimeters |
| **Boundary-Scoped Mapping** | A capability-to-control mapping assigned to a specific authorization boundary |
| **Organization-Wide Mapping** | A capability-to-control mapping with no boundary assignment (null FK), applicable to all boundaries |
| **Composite Narrative** | An auto-generated SSP control narrative that includes sections for organization-wide and per-boundary capability mapping descriptions |
| **Primary Boundary** | The default boundary definition auto-created for each system during migration; cannot be deleted |
| **Boundary Comparison Table** | A dashboard visualization showing per-boundary compliance coverage percentages with color coding |
| **Azure Resource Discovery** | Automated discovery of Azure resources via Resource Graph API, grouped by resource group as suggested boundaries |

## Feature 044 Terms

| Term | Definition |
|------|------------|
| **Org-Level Inheritance Default** | A centralized inheritance designation derived from org-wide security capability-to-control mappings, automatically cascaded to all registered system baselines |
| **Designation Source** | Tracks how an inheritance designation was set: OrgDerived, OrgPropagation, Manual, BulkUpdate, ProfileApply, or CrmImport |
| **Cascade Propagation** | Automatic distribution of org-level inheritance defaults to all system baselines, creating OrgDerived designations and OrgPropagation audit entries |
| **System Override** | A per-system inheritance designation that differs from the org-level default — tracked as Manual, ProfileApply, CrmImport, or BulkUpdate source |
| **Revert to Org Default** | Action to reset overridden controls back to the org-level inheritance default designation |

## Feature 039 Terms

| Term | Definition |
|------|-----------|
| **POA&M** | Plan of Action & Milestones — A document tracking known security weaknesses, the planned remediation, and milestone schedule |
| **POA&M Lifecycle** | Status flow: Ongoing → Completed (with validation), Delayed (with reason), or Risk Accepted (with deviation) |
| **CAT Severity** | Category I (critical), II (moderate), III (low) — DoD vulnerability severity rating |
| **Cascade Confirmation** | Dialog shown when a linked entity changes status, asking whether to propagate the change to the POA&M item |
| **Bidirectional Sync** | Two-way synchronization between POA&M items and external ticketing systems (Jira/ServiceNow) |
| **eMASS Export** | Export in the Enterprise Mission Assurance Support Service 24-column Excel template format |
| **OSCAL POA&M** | Export in NIST Open Security Controls Assessment Language Plan of Action schema |
| **Ticketing Integration** | Configuration linking a system to an external Jira or ServiceNow instance for POA&M ticket synchronization |
| **Component Linkage** | Association between a POA&M item and one or more system components for traceability |
| **Remediation Task Sync** | Bidirectional link between a POA&M item and a Kanban remediation task |
