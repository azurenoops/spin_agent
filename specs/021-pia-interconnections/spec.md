# Feature 021: PIA Service + System Interconnections

**Created**: 2026-03-07  
**Status**: Strategic Plan  
**Purpose**: Enable Security Posture Intelligence Navigator to manage Privacy Impact Assessments (PIAs), Privacy Threshold Analyses (PTAs), and system interconnection inventories with ISA/MOU tracking — completing the two mandatory Prepare-phase deliverables currently missing from the RMF lifecycle.

---

## Clarifications

### Session 2026-03-07

- Q: Should the PIA be a standalone document or embedded in the SSP? → A: Standalone document with its own lifecycle (Draft → UnderReview → Approved → Expired). The SSP references the PIA by ID but does not contain it. This matches OMB M-03-22 guidance requiring a separate, publicly posted PIA.
- Q: Are all systems required to complete a PIA? → A: No. Systems that do not collect, maintain, or disseminate PII only need a Privacy Threshold Analysis (PTA). The PTA determines whether a full PIA is required. Both are modeled.
- Q: How do interconnections relate to the authorization boundary? → A: Authorization boundaries (`AuthorizationBoundary`) track Azure resources *within* the system. Interconnections track *external* system-to-system links that cross the boundary. They are complementary — the boundary defines what's inside, interconnections define what crosses.
- Q: Should ISA/MOU documents be stored as file content or references? → A: References only (document title, location, and metadata). Actual ISA/MOU documents are stored in external document management systems (SharePoint, eMASS). Security Posture Intelligence Navigator tracks their existence, status, and expiration.
- Q: What happens when an ISA expires? → A: The ConMon service detects ISA expiration during monitoring checks and creates a `SignificantChange` record with `ChangeType = "New Interconnection"`. This triggers reauthorization evaluation per existing ConMon workflow.
- Q: What happens to the PTA/PIA when security categorization info types change? → A: PTA is automatically invalidated when information types change — forces re-analysis. If a PIA was approved, its status is set to `UnderReview` pending the new PTA determination. This ensures the privacy assessment always reflects the current system state.
- Q: Can a single interconnection have multiple agreements (ISA + MOU)? → A: Yes. Multiple agreements are allowed per interconnection. The gate requires at least one Signed and current agreement per active interconnection. All agreement expirations are tracked independently — if any agreement expires, it is flagged in validation, but the gate only fails if no Signed+current agreement remains.
- Q: How should ambiguous SP 800-60 info types (may or may not contain PII) be handled by PTA auto-detection? → A: Flag ambiguous types as "potential PII — human confirmation required" in PTA output. The final PTA determination is deferred until the user confirms or denies PII presence for each flagged type. Known-PII types (D.8.x, D.17.x, D.28.x) are auto-classified without confirmation.
- Q: Should PTA/ISA gates retroactively block systems already past the Prepare phase? → A: No. Gates are only enforced at the Prepare→Categorize boundary. Systems already past Prepare are unaffected but receive advisory warnings in the privacy compliance dashboard (`compliance_check_privacy_compliance`). This avoids disrupting active ATO processes while driving compliance adoption for new systems.
- Q: Should the `PtaDetermination` enum include a `PendingConfirmation` value for ambiguous PII cases? → A: Yes. Add `PendingConfirmation` as a fourth enum value. The privacy gate treats it as unsatisfied (same as `PiaRequired` without an approved PIA). The dashboard shows "action required" for systems with this determination. This clearly distinguishes "we don't know yet" from "PIA is needed" in reporting and gate logic.

---

## Part 1: The Problem

### Why This Matters

Two mandatory RMF Prepare-phase deliverables have **zero implementation** in Security Posture Intelligence Navigator despite being required by federal law and DoD policy:

1. **Privacy Impact Assessment (PIA)** — Required by the E-Government Act of 2002 §208, OMB M-03-22, OMB Circular A-130, and DoDI 5400.11 for any system that collects, maintains, or disseminates Personally Identifiable Information (PII). A system cannot receive an ATO if it processes PII without a current PIA.

2. **System Interconnections + ISA/MOU** — Required by NIST SP 800-47 Rev 1, NIST SP 800-18 §3.2, and DoDI 8510.01 for any system that exchanges data with external systems. Interconnection Security Agreements (ISAs) and Memoranda of Understanding (MOUs) must be signed before data flows begin.

The irony: the knowledge base already references both as required deliverables — [rmf-process.json](../../src/Ato.Copilot.Agents/KnowledgeBase/Data/rmf-process.json) lists "Privacy Impact Assessment (PIA)" as a Categorize output (line 137), and [navy-workflows.json](../../src/Ato.Copilot.Agents/KnowledgeBase/Data/navy-workflows.json) lists ISAs as required documents for High-impact systems (line 59). But **no service, model, or tool exists to produce or manage them**.

### The Current Gap

| What Teams Must Do | What Security Posture Intelligence Navigator Can Do Today |
|---------------------|-------------------------------|
| Conduct Privacy Threshold Analysis (PTA) for every system | Nothing — no PTA model or workflow |
| Complete PIAs for systems processing PII | Nothing — no PIA model, questionnaire, or document generation |
| Identify all system interconnections | Nothing — `AuthorizationBoundary` tracks Azure resources only, not system-to-system links |
| Draft and track ISA/MOU agreements | Nothing — no agreement model or lifecycle tracking |
| Link ISA expiration to ConMon monitoring | `SignificantChange` has a "New Interconnection" type but no source data to trigger it |
| Include interconnections in SSP §10 | `SspService` generates 4 of 8 NIST 800-18 sections — §10 (Interconnections) is missing |
| Gate Prepare→Categorize on privacy readiness | `RmfLifecycleService` only checks roles + boundary — no privacy or interconnection gates |

### The Opportunity

Security Posture Intelligence Navigator already has:
- `SecurityCategorization` with `InformationType` records containing SP 800-60 categories — these can identify PII-processing information types
- `ConMonService` with `SignificantChange` including `ChangeType = "New Interconnection"` — just needs source data
- `SspService` ready for additional section generation — §10 slot is empty
- `RmfLifecycleService` with extensible gate conditions — new gates can be added alongside existing role/boundary checks
- Knowledge base with PIA and ISA guidance already curated

The missing piece is **data models + services** — entities for PTA/PIA/Interconnection/Agreement and services to manage them.

---

## Part 2: The Product

### What We're Building

**Privacy & Interconnection Management** adds the ability to conduct Privacy Threshold Analyses, generate Privacy Impact Assessments, register system interconnections, track ISA/MOU agreements, and enforce privacy/interconnection gates in the RMF lifecycle.

### What It Is

- A **Privacy Threshold Analysis (PTA)** workflow that determines whether a full PIA is required based on the system's PII processing characteristics
- A **PIA generation engine** that produces OMB-compliant PIA documents from structured questionnaire responses with AI-assisted narrative drafting
- A **system interconnection registry** that tracks all external system-to-system data flows
- An **ISA/MOU lifecycle tracker** that monitors agreement status, expiration, and renewal requirements
- **RMF gate enforcement** that prevents Prepare→Categorize advancement until privacy and interconnection requirements are satisfied

### What It Is NOT

- Not a Privacy Act System of Records Notice (SORN) generator — SORNs are published in the Federal Register via a separate legal process
- Not a data loss prevention (DLP) system — we track what PII exists, we don't prevent its exfiltration
- Not a document storage system — ISA/MOU documents are referenced by URL, not stored in Security Posture Intelligence Navigator
- Not a network monitoring tool — interconnections are documented, not actively monitored at the network layer
- Not a consent management platform — individual opt-in/opt-out is outside our scope

### Interfaces

| Surface | User | Purpose |
|---------|------|---------|
| **MCP Tools** | ISSO, ISSM | `compliance_create_pta`, `compliance_generate_pia`, `compliance_review_pia` |
| **MCP Tools** | ISSO, Engineer | `compliance_add_interconnection`, `compliance_list_interconnections`, `compliance_update_interconnection` |
| **MCP Tools** | ISSM | `compliance_generate_isa`, `compliance_register_agreement`, `compliance_update_agreement`, `compliance_validate_agreements` |
| **MCP Tools** | ISSO, ISSM | `compliance_certify_no_interconnections` |
| **MCP Tools** | SCA, ISSM, AO | `compliance_check_privacy_compliance` |
| **VS Code (@ato)** | ISSO, ISSM | `@ato Does this system need a PIA?` → runs PTA analysis |
| **VS Code (@ato)** | Engineer | `@ato Add an interconnection to the DISA SIPR network` |
| **Teams Bot** | ISSM | Adaptive Card showing PIA status and upcoming ISA expirations |

---

## Part 3: Regulatory Framework

### 3.1 Privacy Impact Assessment Requirements

PIAs are governed by multiple authorities that define what must be documented:

**E-Government Act of 2002 §208** requires PIAs for:
- Systems that collect, maintain, or disseminate PII
- Systems undergoing new development or significant modification
- New collections of PII for 10+ people (excluding agencies/employees acting in official capacity)

**OMB M-03-22** mandates PIAs include:
1. What information is collected and why
2. Intended use of the information
3. With whom the information will be shared
4. Notice and consent mechanisms
5. How the information is secured
6. Whether a System of Records Notice (SORN) is required

**DoDI 5400.11** adds DoD-specific requirements:
- Privacy Officer review and signature
- Annual review cycle
- Linkage to the system's RMF authorization package

### 3.2 Interconnection Security Agreement Requirements

**NIST SP 800-47 Rev 1** defines the ISA lifecycle:
1. Planning the interconnection
2. Establishing the interconnection
3. Maintaining the interconnection
4. Disconnecting the interconnection

**NIST SP 800-18 Rev 1 §3.2** requires the SSP to document:
- List of all interconnected systems
- Type of connection (direct, VPN, API, etc.)
- ISA/MOU documentation status
- Ports, protocols, and services used
- Security measures applied (encryption, authentication)

**DoDI 8510.01** requires:
- Interconnection documentation before ATO issuance
- ISA/MOU renewal before expiration
- New interconnections treated as significant changes triggering reauthorization review

---

## Part 4: Personas & Needs

| Persona | Needs |
|---------|-------|
| **ISSO** | Conduct PTAs to determine PIA requirements; complete PIA questionnaires with AI assistance; register system interconnections and data flows; track ISA/MOU status |
| **ISSM** | Review and approve PIAs; generate ISA/MOU templates; validate all agreements are current before ATO submission; oversight of privacy compliance posture |
| **SCA** | Verify PIA completeness during assessment; validate all interconnections have signed agreements; confirm privacy controls (PT family) are addressed |
| **AO** | See aggregate privacy risk before authorization; confirm PIA and ISA/MOU compliance gates are satisfied; understand interconnection risk exposure |
| **Engineer** | Document technical interconnection details (protocols, ports, encryption); provide data flow information for PIA and ISA drafting |

### Persona ↔ RBAC Role Mapping

| Persona | RBAC Enum Value | Notes |
|---------|-----------------|-------|
| ISSO | `Compliance.Analyst` | Creates PTAs, generates PIAs, registers interconnections |
| ISSM | `Compliance.SecurityLead` | Reviews PIAs, generates ISAs, manages agreements |
| SCA | `Compliance.Auditor` | Validates agreements, checks privacy compliance |
| AO | `Compliance.AuthorizingOfficial` | Views privacy compliance dashboard |
| Engineer | `Compliance.PlatformEngineer` | Registers interconnections with technical details |

---

## Part 5: Capabilities

### Phase 1: Privacy Threshold Analysis & PIA Foundation [Caps 1.x]

**[Cap 1.1]** — PTA Questionnaire: Analyze a registered system's information types from `SecurityCategorization` to identify PII processing. Cross-reference SP 800-60 categories against PII indicators. Known-PII info type families (D.8.x Personnel Records, D.17.x Health/Medical, D.28.x Financial) are auto-classified without confirmation. Ambiguous info types that *could* contain PII depending on implementation are flagged as "potential PII — human confirmation required" — the PTA determination is deferred until the user confirms or denies PII presence for each flagged type. Generate a structured PTA determination (`PiaRequired` / `PiaNotRequired` / `Exempt` / `PendingConfirmation`) with supporting rationale. A `PendingConfirmation` determination is treated as gate-unsatisfied — the system cannot advance past Prepare until the user resolves all flagged info types and the determination is updated.

**[Cap 1.2]** — PIA Questionnaire Generation: For systems requiring a PIA, generate a structured questionnaire aligned with OMB M-03-22 requirements. Pre-populate answers where data exists (e.g., information types from categorization, system description from registration, safeguards from control baseline).

**[Cap 1.3]** — PIA Document Generation: Produce a complete PIA document from questionnaire responses. AI-assisted narrative drafting using the system's existing compliance data (categorization, controls, boundary). Output in markdown format suitable for export and regulatory submission.

**[Cap 1.4]** — PIA Lifecycle Management: Track PIA status through Draft → UnderReview → Approved → Expired. Link PIA to system registration and authorization package. When `SecurityCategorization` information types change, automatically invalidate the PTA and set any approved PIA to `UnderReview` — forcing re-analysis to ensure privacy determinations reflect the current system state. PIA expiration is detected during ConMon monitoring: PIAs with `ExpirationDate` in the past are automatically set to `Expired` status and a `SignificantChange` record is created, re-opening the privacy gate.

**[Cap 1.5]** — PIA Review & Approval: ISSM reviews PIA content for completeness and accuracy. Records approval with reviewer identity and timestamp. Flags deficiencies for ISSO remediation.

### Phase 2: System Interconnection Registry [Caps 2.x]

**[Cap 2.1]** — Register Interconnection: Record a system-to-system connection with target system name, owner, connection type, data flow direction, data classification, protocols, and security measures. Validate against authorization boundary — the target must be external to the registered system's boundary.

**[Cap 2.2]** — List Interconnections: Query all active, suspended, or terminated interconnections for a system. Filter by status, direction, classification. Include agreement status summary per interconnection.

**[Cap 2.3]** — Update Interconnection: Modify interconnection details (protocol changes, data classification changes). Changes to classification or security measures trigger ISA review requirement.

**[Cap 2.4]** — Suspend/Terminate Interconnection: Mark interconnections as suspended (temporary) or terminated (permanent). Terminated interconnections are retained for audit trail but excluded from active counts and SSP documentation.

### Phase 3: ISA/MOU Agreement Tracking [Caps 3.x]

**[Cap 3.1]** — Generate ISA Template: AI-draft an ISA based on interconnection details — pre-populates system names, data classification, protocols, security measures, and points of contact from the interconnection record and RMF role assignments.

**[Cap 3.2]** — Register Agreement: Record an ISA or MOU with agreement type, effective date, expiration date, signatory information, and document reference (URL). Link to one or more interconnections.

**[Cap 3.3]** — Agreement Validation: Check all active interconnections for signed, current agreements. Each interconnection may have multiple agreements (e.g., ISA + MOU); the gate requires at least one Signed and non-expired agreement per active interconnection. Flag interconnections without any agreement, with all agreements expired, or with agreements expiring within 90 days. Individual agreement expirations are tracked independently for renewal alerting even if the gate is still satisfied.

**[Cap 3.4]** — Agreement Expiration Monitoring: Integrate with `ConMonService` to detect approaching ISA/MOU expirations and PIA expirations. Create `SignificantChange` records when agreements expire without renewal. Detect PIAs past their `ExpirationDate` and set status → Expired, re-opening the privacy gate.

**[Cap 3.5]** — Agreement Registration & Update: Register pre-existing ISA/MOU/SLA agreements with agreement type, effective date, expiration date, signatory information, and document reference. Update agreement status through the lifecycle (Draft → PendingSignature → Signed → Expired → Terminated). Enables users to record externally-signed agreements and advance them to Signed status so that Gate 4 can be satisfied.

### Phase 4: Gate Enforcement & Integration [Caps 4.x]

**[Cap 4.1]** — Privacy Readiness Gate: Add PTA completion as a Prepare→Categorize gate condition in `RmfLifecycleService`. If PTA indicates PIA required, the PIA must be in Approved status to advance. A PTA with `PendingConfirmation` determination is also gate-unsatisfied until resolved. Gates are only enforced at the Prepare→Categorize boundary — systems already past Prepare are unaffected but receive advisory warnings in the privacy compliance dashboard.

**[Cap 4.2]** — Interconnection Documentation Gate: Add interconnection validation as a Prepare→Categorize gate condition. System must either have documented interconnections with signed agreements, or be certified as having no external interconnections (via `compliance_certify_no_interconnections`). Same boundary-only enforcement — systems in downstream phases receive advisory warnings only.

**[Cap 4.3]** — SSP §10 Integration: Provide interconnection data to `SspService` for System Interconnections section generation. Populates SSP with connection type, partner system, agreement status, and security measures per NIST 800-18 §3.2.

**[Cap 4.4]** — Authorization Pre-check: Validate PIA and ISA/MOU compliance before ATO submission. `AuthorizationService` checks that PIA is approved (if required) and all ISAs are signed and current.

**[Cap 4.5]** — Privacy Compliance Dashboard: Aggregate privacy and interconnection status for a system — PTA status, PIA status, interconnection count, agreement coverage percentage, upcoming expirations. Used by AO and ISSM for risk-informed decisions.

---

## Part 6: Architecture

### 6.1 Privacy Assessment Flow

```
ISSO → compliance_create_pta
         │
         ├─ Retrieves SecurityCategorization + InformationTypes
         ├─ Cross-references SP 800-60 categories for PII indicators
         ├─ Generates PTA determination:
         │    ├─ "PIA Not Required" → PTA complete, gate satisfied
         │    ├─ "PIA Required" → Must complete PIA before gate satisfied
         │    └─ "Exempt" → Government-to-government, gate satisfied
         │
         └─ Stores PrivacyThresholdAnalysis entity

ISSO → compliance_generate_pia
         │
         ├─ Validates PTA exists and indicates PIA required
         ├─ Generates OMB M-03-22 questionnaire with pre-populated data
         ├─ AI drafts narrative sections from system context
         ├─ Creates PrivacyImpactAssessment entity (Status = Draft)
         │
         └─ Returns PIA document in markdown

ISSM → compliance_review_pia
         │
         ├─ Validates PIA exists in Draft or UnderReview status
         ├─ Records review: Approve or RequestRevision
         ├─ If Approved: PIA.Status → Approved, sets ExpirationDate (+1 year)
         ├─ If RequestRevision: PIA.Status → Draft, records deficiencies
         │
         └─ Privacy gate now satisfied (if Approved)
```

### 6.2 Interconnection & ISA Flow

```
Engineer/ISSO → compliance_add_interconnection
         │
         ├─ Validates system exists
         ├─ Records interconnection details
         ├─ Creates SystemInterconnection entity
         │
         └─ Returns interconnection summary

ISSM → compliance_generate_isa
         │
         ├─ Retrieves interconnection + system details
         ├─ Pre-populates ISA template from:
         │    ├─ Interconnection metadata (classification, protocols)
         │    ├─ RmfRoleAssignments (POC contacts)
         │    └─ AuthorizationBoundary (boundary description)
         ├─ AI drafts ISA using NIST 800-47 structure
         │
         └─ Returns ISA document in markdown

ISSM → compliance_validate_agreements
         │
         ├─ Queries all active interconnections
         ├─ Checks each has a signed, non-expired agreement
         ├─ Reports:
         │    ├─ ✅  Compliant interconnections
         │    ├─ ⚠️  Agreements expiring within 90 days
         │    └─ ❌  Missing or expired agreements
         │
         └─ Interconnection gate: satisfied only if all ✅
```

### 6.3 Gate Integration in RMF Lifecycle

Current gates (Prepare → Categorize):
```
Gate 1: RMF Roles Assigned      ← existing (RmfLifecycleService line ~317)
Gate 2: Authorization Boundary   ← existing (RmfLifecycleService line ~320)
```

New gates (this feature):
```
Gate 3: Privacy Readiness        ← PTA completed; if PIA required, PIA Approved
Gate 4: Interconnections Documented ← All active interconnections have signed ISAs,
                                      OR system certified as "No External Interconnections"
```

---

## Part 7: Integration Points

### Existing Services Used

| Service | How Used | File |
|---------|----------|------|
| `IRmfLifecycleService` | Add gates 3 + 4 to `EvaluateGateConditionsAsync` | `RmfLifecycleService.cs` |
| `ICategorizationService` | Read `InformationType` records for PII detection | `CategorizationService.cs` |
| `ISspService` | Provide interconnection data for SSP §10 generation | `SspService.cs` |
| `IAuthorizationService` | Add PIA + ISA pre-checks to authorization readiness | `AuthorizationService.cs` |
| `IConMonService` | Register ISA expiration monitoring events | `ConMonService.cs` |

### New Services Created

| Service | Purpose | Interface |
|---------|---------|-----------|
| `PrivacyService` | PTA analysis, PIA generation/review, privacy compliance checks | `IPrivacyService` |
| `InterconnectionService` | System interconnection CRUD, ISA generation, agreement validation | `IInterconnectionService` |

### New MCP Tools

| Tool | Caps | RBAC |
|------|------|------|
| `compliance_create_pta` | 1.1 | Analyst, SecurityLead |
| `compliance_generate_pia` | 1.2, 1.3 | Analyst, SecurityLead |
| `compliance_review_pia` | 1.4, 1.5 | SecurityLead |
| `compliance_add_interconnection` | 2.1 | PlatformEngineer, Analyst |
| `compliance_list_interconnections` | 2.2 | All roles |
| `compliance_update_interconnection` | 2.3 | Analyst, SecurityLead |
| `compliance_generate_isa` | 3.1 | SecurityLead |
| `compliance_register_agreement` | 3.2, 3.5 | SecurityLead |
| `compliance_update_agreement` | 3.5 | SecurityLead |
| `compliance_validate_agreements` | 3.3 | Auditor, SecurityLead |
| `compliance_certify_no_interconnections` | 4.2 | Analyst, SecurityLead |
| `compliance_check_privacy_compliance` | 4.5 | Auditor, SecurityLead, AuthorizingOfficial |

---

## Part 8: What This Changes

### New Entities

| Entity | Purpose | Storage |
|--------|---------|---------|
| `PrivacyThresholdAnalysis` | PTA determination per system | New DbSet |
| `PrivacyImpactAssessment` | PIA document with lifecycle | New DbSet |
| `PiaSection` | Individual PIA questionnaire responses | Owned by PIA |
| `SystemInterconnection` | System-to-system link | New DbSet |
| `InterconnectionAgreement` | ISA/MOU tracking | New DbSet |

See [data-model.md](data-model.md) for full entity definitions.

### New Enums

| Enum | Values | Purpose |
|------|--------|---------|
| `PtaDetermination` | PiaRequired, PiaNotRequired, Exempt, PendingConfirmation | PTA outcome |
| `PiaStatus` | Draft, UnderReview, Approved, Expired | PIA lifecycle |
| `PiaReviewDecision` | Approved, RequestRevision | Reviewer action |
| `InterconnectionType` | Direct, Vpn, Api, Federated, Wireless, RemoteAccess | Connection type |
| `DataFlowDirection` | Inbound, Outbound, Bidirectional | Data flow |
| `InterconnectionStatus` | Proposed, Active, Suspended, Terminated | Interconnection lifecycle |
| `AgreementType` | Isa, Mou, Sla | Agreement classification |
| `AgreementStatus` | Draft, PendingSignature, Signed, Expired, Terminated | Agreement lifecycle |

### Modified Entities

| Entity | Change | Purpose |
|--------|--------|---------|
| `RegisteredSystem` | Add navigation properties: `PrivacyThresholdAnalysis`, `PrivacyImpactAssessment`, `SystemInterconnections` | Link privacy and interconnection data to system |

### Service Layer Changes

| Service | Change |
|---------|--------|
| `RmfLifecycleService` | Add Gate 3 (privacy readiness) and Gate 4 (interconnection documentation) to `EvaluateGateConditionsAsync` |
| `SspService` | Add method to generate SSP §10 (System Interconnections) from `SystemInterconnection` records |
| `AuthorizationService` | Add PIA approval check and ISA validation to authorization pre-checks |
| `ConMonService` | Add ISA and PIA expiration detection to monitoring cycle |

### Tool Layer Changes

| File | Change |
|------|--------|
| `ComplianceMcpTools.cs` | Register 12 new tools |
| `ServiceCollectionExtensions.cs` | Register `IPrivacyService`, `IInterconnectionService` |

---

## Part 9: What We're NOT Building

- **SORN Generator** — System of Records Notices are a legal publication process outside RMF. We track whether a SORN is required and its reference number, but don't generate them.
- **PII Scanner** — We don't scan databases or files for PII. We rely on human-completed PTA/PIA questionnaire responses about what PII the system collects.
- **Network Topology Mapper** — Interconnections are manually documented, not auto-discovered from network configuration.
- **ISA/MOU Document Storage** — We store references (URLs) to agreements, not the documents themselves. Organizations use SharePoint, eMASS, or other DMS.
- **Privacy Overlay Application** — CNSSI 1253 privacy overlays are a separate spec concern (referenced in gap analysis). This spec handles PTA/PIA and interconnection documentation only.
- **Automated ISA Enforcement** — We don't block network traffic for expired ISAs — we document and alert.

---

## Part 10: Success Criteria

### Acceptance Tests

| ID | Scenario | Expected Result |
|----|----------|-----------------|
| AT-01 | ISSO runs PTA for system with no PII info types | PTA returns `PiaNotRequired`, gate satisfied |
| AT-02 | ISSO runs PTA for system with PII info types (e.g., D.8.1 Personnel Records) | PTA returns `PiaRequired`, gate not yet satisfied |
| AT-03 | ISSO generates PIA for system with PTA = PiaRequired | PIA created in Draft status with pre-populated sections |
| AT-04 | ISSM approves PIA | PIA status → Approved, expiration set to +1 year, privacy gate satisfied |
| AT-05 | ISSM requests PIA revision | PIA status → Draft, deficiency notes recorded |
| AT-06 | Engineer registers interconnection with full details | `SystemInterconnection` created with Proposed status |
| AT-07 | ISSM generates ISA from interconnection data | ISA document produced with correct system details and contacts |
| AT-08 | ISSM validates agreements — all signed | Validation passes, interconnection gate satisfied |
| AT-09 | ISSM validates agreements — one expired | Validation fails, expired agreement flagged |
| AT-10 | System advances Prepare→Categorize with PTA + interconnections satisfied | All 4 gates pass, RMF step advances |
| AT-11 | System advances Prepare→Categorize without PTA | Gate 3 fails, step advancement blocked with descriptive error |
| AT-12 | System advances Prepare→Categorize with unsigned ISA | Gate 4 fails, step advancement blocked |
| AT-13 | ConMon cycle detects ISA expiring within 90 days | Warning in ConMon report, no `SignificantChange` yet |
| AT-14 | ConMon cycle detects expired ISA | `SignificantChange` created with type "New Interconnection" |
| AT-15 | SCA checks privacy compliance for authorization package | Comprehensive report: PTA status, PIA status, interconnection coverage, agreement gaps |
| AT-16 | SSP generation includes §10 with interconnection data | SSP section lists all active interconnections with agreement status |
| AT-17 | System with no PII and no interconnections advances normally | PTA = PiaNotRequired + certified "no interconnections" satisfies both gates |
| AT-18 | RBAC: Engineer cannot approve PIA | Access denied — SecurityLead role required |
| AT-19 | RBAC: Analyst cannot generate ISA | Access denied — SecurityLead role required |
| AT-20 | PIA expired (>1 year since approval) auto-flags during ConMon | PIA status → Expired, privacy gate re-opens |
| AT-21 | ISSO runs PTA for system with ambiguous info types → PendingConfirmation → user confirms PII → determination updated to PiaRequired | PTA determination transitions PendingConfirmation → PiaRequired, gate blocked until PIA completed |
| AT-22 | Info types changed on system with Approved PIA → PTA invalidated → PIA set to UnderReview | PTA deleted, PIA status → UnderReview, privacy gate re-opens |
| AT-23 | System certified as "no interconnections" → interconnection added → certification flag cleared | `HasNoExternalInterconnections` set to false, Gate 4 now requires agreement validation |
