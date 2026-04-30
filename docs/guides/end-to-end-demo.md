# ATO Copilot End-to-End Demo Runbook

> A complete, persona-based demo that showcases ATO Copilot capabilities from system intake to continuous monitoring.

---

## Purpose

Use this runbook to deliver a single end-to-end demonstration that covers the major ATO Copilot feature set across:

- RMF lifecycle execution (Prepare through Monitor)
- Multi-channel AI interaction (Dashboard, Chat, Teams, VS Code)
- Capability-driven control inheritance and boundary management
- Assessment, remediation, deviations, and POA&M lifecycle
- Document production, export, and authorization package generation
- Enterprise controls (RBAC, CAC/PIV, PIM, auditability, resilience)

---

## Demo Profile

| Property | Value |
|----------|-------|
| Duration | 60-90 minutes |
| Audience | ISSM, ISSO, SCA, AO, Engineers, Program Leadership |
| Demo System | Falcon Ops Portal |
| Hosting | Azure Government |
| Target Baseline | NIST SP 800-53 High + CNSSI 1253 overlay |
| Personas Used | ISSM, ISSO, SCA, AO, Engineer, Administrator |

---

## Success Criteria

By the end of the demo, attendees should see that ATO Copilot can:

1. Stand up a new system with guided intake.
2. Drive RMF phase progression with guardrails and role-aware actions.
3. Use security capabilities to automate control inheritance decisions.
4. Link components, findings, remediation tasks, deviations, and POA&M items.
5. Produce SSP/SAR/RAR/CRM/POA&M outputs and bundle an authorization package.
6. Continue operations with Compliance Watch, trends, and alert workflows.
7. Operate consistently across Dashboard, Teams, VS Code, and chat APIs.

---

## Pre-Demo Setup Checklist

### Environment

- MCP server running and reachable.
- Dashboard running and reachable.
- At least one test subscription connected.
- Seed data loaded (or clean environment prepared).

### Identity and Access

- CAC/PIV sign-in validated for at least one presenter account.
- PIM role activation path tested for elevated actions.
- Accounts available for ISSM, ISSO, SCA, AO personas.

### Integrations

- Teams bot installed in demo tenant.
- VS Code extension installed and authenticated.
- Optional ticketing integration configured (Jira or ServiceNow).

### Data and Artifacts

- One CSP profile available for import (for Capabilities Hub).
- Optional CRM CSV/XLSX sample ready for import.
- Optional ACAS/STIG/SCAP sample results available.

---

## Feature Coverage Map

| Capability Area | Where to Show It | Evidence of Completion |
|-----------------|------------------|------------------------|
| System Intake Wizard | Dashboard intake flow | New system created with setup completion state |
| RMF phase gating | Chat + Dashboard | Prepare->Categorize->Select->Implement->Assess->Authorize->Monitor transitions |
| Categorization and baseline | ISSM prompts | FIPS 199 notation + High baseline selected |
| Security Capabilities Hub | Capabilities page | CSP/CRM import, mappings, coverage KPIs |
| Control Inheritance + CRM | Inheritance page | Designation sources, org defaults, CRM export |
| Component-centric boundary | Components and boundaries pages | Person/Place/Thing components linked to boundaries and capabilities |
| Narrative authoring | Chat + Documents | Family narrative generation and progress metrics |
| Assessment workflow | SCA prompts | Control determinations and assessment snapshot |
| Remediation Kanban | Kanban board | Task lifecycle from Backlog to Done |
| Deviation management | Deviations page + chat | False positive / risk acceptance / waiver lifecycle |
| POA&M management | POA&M page | Create, trend, export, and optional ticket sync |
| Compliance Watch | Watch prompts and dashboard | Monitoring enabled, alerts triaged, optional auto-remediation |
| Document catalog | Documents page + tools | SSP, SAR, RAR, ConMon, authorization package generated |
| eMASS/OSCAL interoperability | Export actions | eMASS-compatible and OSCAL outputs produced |
| Knowledge base agent | Knowledge prompts | NIST/STIG/RMF explanations in-session |
| Teams channel experience | Teams bot | Adaptive-card responses + suggested actions |
| VS Code experience | VS Code extension | RMF assistant actions from editor context |
| Enterprise hardening | Ops callouts | Rate limiting, SSE reconnect, audit and correlation demonstrated |

---

## Common Audience Questions (Quick Answers)

- Q: "Is this replacing eMASS?"
- A: "No. ATO Copilot is where the team does the work; eMASS remains the official submission system."

- Q: "How is AI output controlled?"
- A: "AI drafts and suggests, but approvals, phase transitions, and governance actions remain role-controlled and auditable."

- Q: "Can this support multiple systems at scale?"
- A: "Yes. Capability reuse, org inheritance defaults, portfolio dashboards, and monitoring are designed for multi-system operations."

- Q: "What happens if teams disagree on risk?"
- A: "Use the deviation workflow for formal review, approval, expiration, and revalidation."

- Q: "How do we keep it current after authorization?"
- A: "Compliance Watch provides continuous monitoring, alerting, and trend analysis to support ongoing authorization posture."

---

## End-to-End Script

## Act 1 - Intake and Foundation (ISSM)

### Presenter Help Text

- What this act does: Captures system scope, ownership, and initial RMF readiness.
- Why it matters: Good intake controls rework risk and strengthens auditability from day one.

- Say this: "We are establishing compliance foundations: what is in scope, who is accountable, and whether gates are satisfied."

- Point to on screen: Setup status, role assignments, boundary summary, and phase gate output.

### 1. Create the system with the intake wizard

#### Presenter Help Text

- What this step does: Creates the system record and captures required RMF setup metadata.
- Why it matters: Establishes a clean, auditable baseline for all downstream workflow actions.

- Say this: "We start by registering the system and locking in ownership, scope, and setup completeness."

- Point to on screen: New system entry, setup badge, and role/boundary completion indicators.

1. Open Dashboard -> Systems -> + Add System.
2. Complete the 7-step wizard for Falcon Ops Portal.
3. Add sample components and one boundary.
4. Assign AO, ISSM, ISSO, SCA roles.
5. Set categorization inputs and finish.

Expected outcome:

- System appears in portfolio list.
- Setup status shows complete.
- Role and boundary data is visible in system detail.

### 2. Confirm RMF baseline context using chat

#### Presenter Help Text

- What this step does: Retrieves current phase state and validates readiness conditions with natural language.
- Why it matters: Demonstrates that RMF progression is policy-driven, not manual guesswork.
- Say this: "Before advancing, we verify gates in real time so nothing moves forward without prerequisites."
- Point to on screen: Phase status response, unmet/met gate checks, and suggested next actions.

Prompt examples:

```text
Register details for Falcon Ops Portal and confirm current RMF phase.
Show readiness gates to move from Prepare to Categorize.
```

Expected outcome:

- Current phase and gate status displayed.
- Suggested next actions returned.

---

## Act 2 - Categorize and Select (ISSM)

### Presenter Help Text

- What this act does: Maps mission context to FIPS 199 impact and selects the control baseline.
- Why it matters: This decision defines control depth, workload, and risk posture for the full lifecycle.
- Say this: "Categorization and baseline selection are the control-plane decisions that everything else follows."
- Point to on screen: FIPS notation, baseline selection, overlay confirmation, and KB guidance outputs.

### 3. Run categorization and baseline selection

#### Presenter Help Text

- What this step does: Translates system mission context into formal FIPS 199 impact levels and baseline scope.
- Why it matters: Controls the level of rigor and workload for implementation and assessment.
- Say this: "This is the decision point that sets the compliance depth for everything that follows."
- Point to on screen: Categorization output, baseline selection result, overlay application.

Prompt examples:

```text
Suggest SP 800-60 information types for Falcon Ops Portal.
Categorize Falcon Ops Portal as C High, I High, A Moderate.
Select High baseline with CNSSI 1253 overlay.
```

Expected outcome:

- FIPS 199 notation is generated.
- Baseline and overlay are recorded.
- System can advance to Implement.

### 4. Show knowledge assist while deciding controls

#### Presenter Help Text

- What this step does: Uses the Knowledge Base agent for explainers and references on controls and impact levels.
- Why it matters: Speeds analyst decision quality and reduces interpretation variance.
- Say this: "When teams need policy clarity, they can ask directly and get structured guidance immediately."
- Point to on screen: Control explanation, STIG references, and impact-level guidance.

Prompt examples:

```text
Explain control AC-2 and its key implementation expectations.
Search STIG guidance for account management controls.
Explain DoD impact level IL5 in plain language.
```

Expected outcome:

- Knowledge Base agent answers with structured references.
- Team sees read-only guidance mode for policy interpretation.

---

## Act 3 - Capabilities, Inheritance, and Components (Engineer + ISSO)

### Presenter Help Text

- What this act does: Demonstrates reusable capabilities, enterprise inheritance defaults, and component traceability.
- Why it matters: Enables scalable compliance by reusing proven control implementations across systems.
- Say this: "This is where we convert point-in-time control mapping into a repeatable enterprise pattern."
- Point to on screen: Coverage KPIs, inheritance sources, and component-to-capability-to-control linkage.

### 5. Populate security capabilities

#### Presenter Help Text

- What this step does: Imports or creates reusable security capabilities and maps them to controls.
- Why it matters: Converts platform security investments into repeatable compliance value.
- Say this: "Instead of re-documenting controls per system, we reuse vetted capabilities at scale."
- Point to on screen: Imported capabilities, mapping counts, and coverage cards.

1. Navigate to Capabilities Hub.
2. Import CSP profile (or CRM file).
3. Show created components, capabilities, and control mappings.
4. Open coverage KPI cards and discuss gaps.

Expected outcome:

- Coverage KPIs update.
- Capability-to-control mappings are visible.

### 6. Derive org defaults and show inheritance propagation

#### Presenter Help Text

- What this step does: Derives organization-wide inheritance defaults and propagates them across systems.
- Why it matters: Enforces consistency and preserves designation provenance for audits.
- Say this: "This replaces one-off control inheritance with enterprise defaults that are traceable and exportable."
- Point to on screen: Source filter, designation badges, and CRM export option.

1. Open Control Inheritance page.
2. Click Derive Org Defaults.
3. Filter by designation source (Org Defaults, Overrides).
4. Export CRM in one format (Custom, FedRAMP, or eMASS layout).

Expected outcome:

- Source badges and provenance are shown.
- CRM export includes designation source information.

### 7. Demonstrate component-centric traceability

#### Presenter Help Text

- What this step does: Links components to capabilities and boundaries for end-to-end implementation traceability.
- Why it matters: Makes assessment evidence attributable to specific assets and owners.
- Say this: "We can now show exactly which component implements which control and where it lives."
- Point to on screen: Component relationships and linked controls/capabilities.

1. Open Component Inventory and show Person/Place/Thing entries.
2. Link capabilities to a component.
3. Show boundary assignment for components.

Expected outcome:

- Traceability path is clear: Component -> Capability -> Control.

---

## Act 4 - Implement and Assess (ISSO + SCA)

### Presenter Help Text

- What this act does: Produces narratives and records evidence-based control determinations.
- Why it matters: Converts implementation claims into assessable, defensible authorization evidence.
- Say this: "Here we move from writing controls to proving controls."
- Point to on screen: Narrative progress, status by family, assessment outcomes, snapshot ID, SAR generation.

### 8. Generate and review control narratives

#### Presenter Help Text

- What this step does: Auto-generates inherited narratives and drafts customer-responsible sections.
- Why it matters: Reduces SSP authoring effort while retaining human approval control.
- Say this: "AI accelerates documentation, but reviewers remain accountable for final narrative quality."
- Point to on screen: Narrative status, completion percentage, family-level progress.

Prompt examples:

```text
Auto-populate inherited control narratives for Falcon Ops Portal.
Suggest narratives for AC family controls requiring customer implementation.
Show SSP completion percentage by control family.
```

Expected outcome:

- Narrative status and completion metrics update.
- Team sees AI-assisted drafting with human review expectation.

### 9. Execute control assessment

#### Presenter Help Text

- What this step does: Records control determinations, captures severity, snapshots posture, and generates SAR.
- Why it matters: Produces defensible assessment evidence for authorization decisions.
- Say this: "This is where implementation claims are validated and converted into formal assessment artifacts."
- Point to on screen: Satisfied/OTS outcomes, CAT breakdown, snapshot ID, SAR result.

Prompt examples:

```text
Assess AC-2 as Satisfied with evidence from identity logs.
Assess AC-3 as Other Than Satisfied with CAT II severity.
Take an assessment snapshot and summarize CAT findings.
Generate the SAR.
```

Expected outcome:

- Assessment results recorded.
- Snapshot and SAR are generated.

---

## Act 5 - Remediation, Deviations, and POA&M (ISSO + ISSM)

### Presenter Help Text

- What this act does: Operationalizes findings through remediation boards, exceptions governance, and POA&M tracking.
- Why it matters: Shows that compliance is managed as execution workflow, not static paperwork.
- Say this: "This act shows risk reduction in motion: assign, govern, track, and close."
- Point to on screen: Kanban transitions, deviation approvals, POA&M metrics, and export actions.

### 10. Create and work remediation tasks

#### Presenter Help Text

- What this step does: Converts findings into assignable remediation tasks with workflow status transitions.
- Why it matters: Ensures findings become accountable engineering work with visible progress.
- Say this: "This board is our execution engine for closing compliance gaps."
- Point to on screen: Task cards, assignees, and status movement across columns.

Prompt examples:

```text
Create a remediation board from the latest assessment.
Assign all CAT II findings to the platform engineering lead.
Move one task from Backlog to In Progress and then to In Review.
```

Expected outcome:

- Kanban board shows task flow and ownership.

### 11. Demonstrate deviation governance

#### Presenter Help Text

- What this step does: Creates and reviews controlled exceptions (risk acceptance, waiver, false positive).
- Why it matters: Prevents undocumented risk acceptance and enforces review discipline.
- Say this: "When remediation is delayed or impractical, we still manage risk through formal governance."
- Point to on screen: Deviation request, reviewer action, approved/pending status.

Prompt examples:

```text
Request a RiskAcceptance deviation for control AC-3 with 90-day expiration.
List pending deviations for Falcon Ops Portal.
Approve the pending CAT II deviation as ISSM.
```

Expected outcome:

- Deviation lifecycle and approvals are visible.
- Cross-page indicators appear in relevant dashboards.

### 12. Build and manage POA&M

#### Presenter Help Text

- What this step does: Builds POA&M records, tracks trend/aging, and exports in required formats.
- Why it matters: Gives leadership visibility into closure velocity and overdue risk.
- Say this: "POA&M here is a living operational backlog, not a static document."
- Point to on screen: Trend visuals, overdue counts, export workflow.

Prompt examples:

```text
Create POA&M entries for all open CAT II findings.
Show overdue POA&M items and trend for last 90 days.
Export POA&M in eMASS format.
```

Expected outcome:

- POA&M records are created and trend charts are populated.
- Export file is generated.

Optional extension:

- Show sync to Jira or ServiceNow for one POA&M item.

---

## Act 6 - Authorize and Monitor (AO + ISSM)

### Presenter Help Text

- What this act does: Generates decision-ready authorization artifacts and enables ongoing monitoring.
- Why it matters: Demonstrates continuity from ATO decision to continuous oversight.
- Say this: "Authorization is a milestone; monitoring is the operating model."
- Point to on screen: Package bundle status, ATO decision metadata, active alerts, and trend charts.

### 13. Generate authorization artifacts

#### Presenter Help Text

- What this step does: Produces RAR and bundles required artifacts for AO decisioning.
- Why it matters: Reduces package assembly time and improves submission consistency.
- Say this: "We can assemble decision-ready authorization evidence in a repeatable, audit-friendly flow."
- Point to on screen: Package bundle output and decision-related metadata.

Prompt examples:

```text
Generate the RAR for Falcon Ops Portal.
Bundle the authorization package for AO review.
Issue an ATO decision with terms and conditions.
```

Expected outcome:

- Authorization package is available.
- Decision is recorded with expiration and conditions.

### 14. Turn on continuous monitoring

#### Presenter Help Text

- What this step does: Enables watch monitoring, surfaces alerts, and supports triage/remediation actions.
- Why it matters: Extends compliance from one-time authorization to continuous risk control.
- Say this: "ATO posture must be maintained continuously; this is how we operationalize that requirement."
- Point to on screen: Monitoring status, alert severity, and trend line.

Prompt examples:

```text
Enable compliance monitoring for Falcon Ops Portal subscription in combined mode.
Show active alerts by severity.
Acknowledge one alert and run fix with dry run.
Show compliance trend for the last 30 days.
```

Expected outcome:

- Monitoring is active.
- Alert lifecycle actions are demonstrated.
- Trend and watch statistics are visible.

---

## Act 7 - Multi-Channel Experience (Administrator)

### Presenter Help Text

- What this act does: Shows consistent compliance interactions across Teams and VS Code.
- Why it matters: Increases user adoption by embedding compliance in daily collaboration and engineering tools.
- Say this: "Same platform intelligence, different channels, one consistent compliance workflow."
- Point to on screen: Adaptive card outputs in Teams and assistant responses in VS Code.

### 15. Teams bot walkthrough

#### Presenter Help Text

- What this step does: Executes compliance workflows through Teams using adaptive card responses.
- Why it matters: Increases adoption by supporting existing collaboration channels.
- Say this: "Users can run meaningful compliance actions without leaving Teams."
- Point to on screen: Adaptive card results and suggested next-action buttons.

In Teams, run:

```text
Show dashboard for Falcon Ops Portal.
Generate SSP status summary.
Show my critical alerts.
```

Expected outcome:

- Adaptive-card responses appear with suggested next actions.

### 16. VS Code workflow walkthrough

#### Presenter Help Text

- What this step does: Brings compliance intelligence directly into the engineering workspace.
- Why it matters: Shifts compliance left and shortens feedback loops during implementation.
- Say this: "Engineers can resolve compliance questions at coding time, not at the end of the cycle."
- Point to on screen: /compliance and /knowledge responses in VS Code chat.

In VS Code chat participant, run:

```text
/compliance show current system status for Falcon Ops Portal
/knowledge explain AC-2 account management evidence requirements
```

Expected outcome:

- Contextual compliance assistance appears directly in development workflow.

---

## Act 8 - Security and Operations Callouts

### Presenter Help Text

- What this act does: Highlights identity, access, audit, and resiliency controls of the platform itself.
- Why it matters: Validates enterprise readiness and trust for regulated environments.
- Say this: "These controls prove the platform is governed, observable, and resilient in production operation."
- Point to on screen: Role-based behavior, audit/correlation traces, and resilience safeguards.

Use short callouts at the end of the demo to highlight enterprise readiness:

- CAC/PIV authentication and role mapping.
- PIM just-in-time activation for privileged actions.
- RBAC-enforced operations by persona.
- End-to-end audit logging with correlation IDs.
- SSE reconnect support for resilient streaming.
- Rate limiting, pagination, and offline-mode behavior.

---

## Demo Close Script

Summarize in under 2 minutes:

1. ATO Copilot covered the full RMF lifecycle in one guided experience.
2. It combined AI assistance with governance, auditability, and role controls.
3. It linked technical implementation data to formal compliance outputs.
4. It delivered the same workflow across dashboard, chat, Teams, and VS Code.

Recommended follow-up asks for stakeholders:

- Pilot with one live system for 30 days.
- Connect production ticketing and evidence sources.
- Define approval policy for deviations and CAT I escalations.

---

## Appendix A - Fast Demo Variant (30 Minutes)

If time is constrained, run these checkpoints only:

1. Intake wizard creation + categorization.
2. Capability import + org inheritance derivation.
3. Narrative generation + one failed control assessment.
4. Kanban task creation + one POA&M export.
5. Authorization package bundle + monitoring enablement.

---

## Appendix B - Presenter Notes

- Keep one planned CAT II finding ready to avoid waiting for scan timing.
- Pre-stage at least one capability import to reduce dead air.
- Use role handoffs verbally to reinforce governance boundaries.
- If a tool call fails, show the audit trail and retry flow to demonstrate resilience.