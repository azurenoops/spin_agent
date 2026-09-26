# System screens: product-goal audit

Date: September 26, 2026

## Verdict

**Partially aligned. The product has useful system-definition, control, evidence, assessment, and export functions, but the current experience does not yet establish a dependable end-to-end path to a fully documented initial ATO submission and ongoing authorization maintenance.** The priority is connecting and correcting these functions, not removing their underlying records or adding more dashboards.

The strongest gaps concern package readiness, faithful document generation, and the connection between monitoring and system impact. Cosmetic improvements alone will not close them.

## Scope and confidence

This is a source-based audit of the current working checkout, including existing uncommitted changes. It covers the 24 system navigation destinations and supporting inventory, hosting-association, responsibility-review, and Azure-assessment flows. Route definitions, screen action handlers, selected API handlers, and document/monitoring service bodies were inspected. The supplied screenshots provide usability context; current code takes precedence where it has changed.

This is **not** a completed live walkthrough of every screen, a security audit, an Azure integration test, or proof of acceptance by an eMASS deployment. No live submission, generated-package comparison, or AO-role exercise was performed. “Keep” below means the function serves the product goal, not that its complete implementation has passed acceptance testing. Application code was not changed.

Product goals supplied by the user:

1. Prepare a fully documented system, with reviewed evidence and consistent artifacts, for ATO review through eMASS.
2. Maintain that documentation through scoped Azure monitoring, configurable ConMon triggers, understandable change impact, and accountable reassessment/reauthorization decisions.
3. Capture information once and reuse it. Each screen should explain its purpose, document contribution, remaining work, and next action.

## Findings and final-state corrections

### F1 — Critical: initial package generation requires a decision that the package is intended to obtain

`PackageValidationService.ValidateAsync` makes an active authorization decision a mandatory condition and tells the user to have an AO issue one before generating the package. `AuthorizationPackageService.EnqueuePackageAsync` invokes that validator and rejects generation when errors exist.

This may support assembling an already-authorized record, but conflicts with the user's initial-submission goal. The UI's generic “Generate Package” does not distinguish those purposes.

**Change:** support explicit package purposes: initial submission, authorized baseline archive, and change/reauthorization submission. Require a prior decision only where appropriate. Keep recorded AO decisions and submission preparation as distinct states.

Evidence: `src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs:41`; `AuthorizationPackageService.cs:224`; `src/Ato.Copilot.Dashboard/src/pages/Documents.tsx:591`.

### F2 — Critical: OSCAL output invents leveraged-authorization metadata

`OscalSspExportService` constructs leveraged authorizations from inheritance provider names. It labels each provider “FedRAMP Authorization,” uses the export date as `date-authorized`, and generates a new party UUID. These values are not read from the actual authorization source in that construction path.

**Change:** export retained authorization title, type, authority, dates, boundary, and source reference. Missing information must remain a visible gap; generating a package must not manufacture an authorization fact. Verify referenced parties actually exist in the resulting artifact.

Evidence: `src/Ato.Copilot.Agents/Compliance/Services/OscalSspExportService.cs:530`.

### F3 — High: profile approval is not demonstrated to feed the SSP

Profile save and approval operate on `SystemProfileSections`, including `ApprovedContent`. The inspected SSP generation path loads `RegisteredSystems`, `SspSections`, roles, boundaries, inventory, components, narratives, and related records. It renders mission/system/environment content from those sources. The profile approval methods do not project their approved content into those SSP sources.

Consequently, an approved profile badge does not establish that the same content appears in the export. An alternate integration path was not verified; this must be treated as an output-traceability gap, not a passed feature.

**Change:** define one explicit mapping from each reviewed profile field and child record to the document/export field. Display “Used in…” and a preview of the generated section. Preserve the approved version when a new draft is edited.

Evidence: `src/Ato.Copilot.Agents/Compliance/Services/SystemProfileService.cs:185`, `:395`, `:462`; `SspService.cs:484`, `:650`; `src/Ato.Copilot.Dashboard/src/pages/SystemProfile.tsx`.

### F4 — High: readiness has inconsistent meanings

The eMASS readiness service blocks on identifiers and categorized information types. SSP approval is only advisory and asks for at least one approved section. POA&M scheduling is also advisory. The package validator separately checks boundary, SSP sections, SAP, SAR, schema, and evidence. Documents labels SSP completion using narrative completion and CRM availability using baseline existence.

These are different measurements, yet users are expected to interpret them as progress toward one submission. Even the stronger validator checks approval of the SSP sections that exist; that check alone does not establish that every required section exists.

**Change:** introduce one purpose-specific readiness assessment shared by Overview, Documents, and eMASS Workflow. Each requirement needs its status, applicability, responsible role, source/version, destination artifact, and a working fix link. Retain separate labels for profile completion, narrative completion, technical export validity, and submission readiness.

Evidence: `src/Ato.Copilot.Agents/Compliance/Services/EmassExportReadinessService.cs:12`; `PackageValidationService.cs:64`; `src/Ato.Copilot.Dashboard/src/pages/Documents.tsx:107`.

### F5 — High: unknown validation or load results can resemble success or absence

Package schema-validation exceptions become warnings; evidence-summary exceptions are logged without adding a finding. The final validation result is valid whenever the error count is zero. Documents converts package-history load failure to an empty list and then says no packages have been generated. Legal & Regulatory ignores several assignment/removal errors. These behaviors prevent the user from distinguishing missing work from unavailable information.

**Change:** expose an explicit “Unable to verify” state, preserve any last-known timestamp, provide retry, and prevent final readiness from treating an uncompleted required check as passed. Draft generation can remain available with clearly identified limitations.

Evidence: `src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs:190`; `src/Ato.Copilot.Dashboard/src/pages/Documents.tsx:603`; `LegalRegulatory.tsx:96`.

### F6 — High: custom ConMon triggers are plan text, not the requested configurable evaluation workflow

The ConMon screen saves custom triggers as a list of strings. The inspected reauthorization evaluator instead checks fixed expiration windows, a fixed list of significant-change types, and a score drop greater than ten percentage points. It does not evaluate that plan's custom trigger list. Selecting “Initiate Reauthorization” can move the system to Assess when a trigger exists.

This is a monitoring/reporting foundation, but it does not fulfill the requested user-defined rules with scope, thresholds, impact explanation, and decision routing.

**Change:** separate the written monitoring plan from executable rules. A rule needs a named signal, system scope, condition, evaluation cadence, severity, owner, and response. Show the detected change and evidence, affected controls/documents, proposed follow-up, and accountable review before changing the authorization workflow. Label automated results as recommendations or review requirements according to approved policy, not an independent authorization decision.

Evidence: `src/Ato.Copilot.Dashboard/src/pages/ConMon.tsx` (`PlanSection`, `AlertsSection`); `src/Ato.Copilot.Agents/Compliance/Services/ConMonService.cs:193`, `:468`, `:780`.

### F7 — High: ConMon subscription totals do not prove system-specific impact

The ConMon overview loads enabled monitoring configurations and drift alerts using the system's Azure subscription IDs. Those queries do not narrow the returned counts to the system's selected resources or boundary. In a shared subscription, subscription-level drift can therefore appear as system drift without an attribution decision in this overview path. “Monitoring enabled” means at least one configuration exists, not that every intended scope has fresh successful telemetry.

**Change:** map each event to the system's reviewed scope. Distinguish attributed system changes, shared-provider changes, out-of-scope changes, and unknown attribution. Report connected scope, covered scope, last successful collection, missing coverage, and monitoring health separately. Trace a change through resource/component → capability/control → evidence/narrative → affected package artifact.

Evidence: `src/Ato.Copilot.Mcp/Endpoints/Dashboard/DashboardConMonEndpoints.cs:101`; `src/Ato.Copilot.Agents/Compliance/Services/ConMonService.cs:807`.

### F8 — Resolved during audit: Azure assessment destination is now registered

The initial route read did not include the destination used by Environment. A final recheck found a new `assessments/environment` route and `AssessmentEnvironment.tsx`, which renders the assessment configuration panel and links back to Assessments and Environment. The working tree changed during this audit. **This is not an outstanding route defect in the final reviewed state.** Live navigation remains an acceptance check.

Evidence: `src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx:78`; `src/Ato.Copilot.Dashboard/src/pages/AssessmentEnvironment.tsx`.

### F9 — Medium: the navigation exposes internal organization more clearly than the submission journey

There are 24 primary system destinations. Remediation, POA&M, and Deviations overlap in user purpose; Narratives and Narrative Library belong to one authoring journey; Documents and eMASS Workflow separate artifact generation from submission tracking. Overview leads with role banners and RMF progress/history rather than a single submission checklist.

The current code already collapses the right panel by default, simplifies Environment, and gives system capabilities a capability/component view. Preserve those improvements.

**Change:** group related work around five tasks, keeping specialist screens available through tabs and contextual links. Do not delete evidence, decisions, or histories merely to reduce visual clutter.

Evidence: `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx`; `src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx`; `DeviationsPage.tsx`; `Documents.tsx`; `EmassStatus.tsx`.

## Screen-by-screen disposition

Output contributions below describe the required product connection. Where a connection is not verified, the row explicitly says so.

| Current destination | Product contribution | Audit disposition / next action |
|---|---|---|
| Overview | Know what prevents submission or requires attention | **Refocus.** Lead with package readiness, next action, owner, and change review. Keep posture/history as secondary views. |
| Security Capabilities | Applied functions, components, control responsibilities | **Keep and connect.** Current system-specific list and placements are useful. Add artifact contribution and unresolved responsibility impact. |
| Roles & Permissions | Accountable authors, reviewers, and decision makers | **Keep.** SSP generation reads system role assignments. Make required vacancies actionable; verify populated names in exported roles. |
| Boundaries | Define what is inside the documented system | **Keep.** Definitions/resources feed SSP generation. Verify component placements agree with exported inventory and diagrams. |
| Mission & Purpose | System description and mission context | **Connect.** Profile approval exists; reviewed-profile-to-SSP mapping is not established by the inspected paths (F3). |
| Users & Access | User categories and access description | **Connect.** Show resulting SSP content and ensure user categories survive save/reload/export. Same F3 qualification. |
| Environment | Hosting, deployment, network and recovery description | **Keep simplified.** Azure assessment destination is now registered (F8); resolve F3; show hosting association as context, not another long form. |
| Data Types | Information description and categorization inputs | **Connect.** Explain relation to categorized information types and privacy analysis; verify one consistent source in exports. |
| Ports & Protocols | Documented ports/protocols/services | **Keep.** Verify structured rows produce the intended artifact table instead of requiring re-entry. F3 remains open. |
| Leveraged Auth | External authorization sources supporting inheritance | **Reconcile.** Reuse source-backed CSP records with explicit scope applicability. Correct OSCAL metadata (F2). |
| Categorization | Information types, CIA impact, selected baseline | **Keep.** Baseline selection and change feedback exist; make downstream controls/documents affected by changes visible. |
| Control Inheritance | Effective provider/customer/shared responsibility; CRM | **Keep within Controls.** Responsibility reconciliation writes baseline inheritance records consumed by eMASS control export. Show source and review status. |
| Narratives | Reviewed control implementation statements | **Keep.** Proposed changes and review exist; connect missing/obsolete statements to package readiness. |
| Narrative Library | Reusable source material for narratives | **Keep as an authoring subview.** Current distinction between reference claims and implementation evidence is useful. |
| Legal & Regulatory | Applicable policy and obligations | **Simplify and connect.** Screen assigns policy components. That alone does not prove the required document section is populated; fix silent errors (F5). |
| Assessments | SAP, assessment execution/results, SAR | **Keep.** Real generation/finalization actions exist. Separate plan, execute/import, and review results into understandable stages. |
| Remediation | Correct identified weaknesses | **Keep within Findings & Risk.** Task-to-POA&M linking exists; make verification evidence and closure impact visible. |
| POA&M | Weakness, owner, schedule, milestones, disposition/export | **Keep.** Direct contribution to submission and ongoing risk management. Make required-field gaps actionable. |
| Evidence | Supporting artifacts for control assessment | **Keep.** Upload/collection and control coverage exist. Counts must not imply reviewed, current, sufficient evidence. |
| Deviations | Risk acceptances, waivers, false positives and POA&M overview | **Consolidate navigation.** Preserve distinct decision types; offer a risk/exception view beside findings and POA&M. |
| Authorize | Record/review authoritative decisions and conditions | **Clarify lifecycle.** Do not require an issued decision to prepare an initial submission (F1). Clearly identify the authority/source of a recorded decision. |
| Documents | Assemble and inspect artifacts and package history | **Promote to ATO Package.** Add shared readiness, artifact preview, version/source traceability, and package purpose. |
| ConMon | Maintain the reviewed system and evidence after changes | **Extend.** Existing plans/reports/alerts are useful; executable custom rules and scoped impact still need work (F6–F7). |
| eMASS Workflow | Export readiness, workbook round trip and conflict review | **Combine with package delivery.** The current screen provides sync/conflict actions; it does not itself show a complete submit/receipt workflow. Verify the actual target integration separately. |

Supporting flows:

| Flow | Disposition |
|---|---|
| Component inventory | Keep discovery, existing-component assignment, and inventory maintenance. Reconcile the inventory shown here with boundary placement and exported inventory; do not make users maintain parallel lists. |
| CSP hosting association | Keep. The wizard selects existing allocations and distinguishes hosting association from capability subscriptions. Demonstrate the approved scope in system documents and monitoring attribution. |
| Capability responsibility review | Keep. Explicit confirmation and reconciliation are important; surface outstanding reviews directly from the applied capability and package gap. |
| Azure assessment configuration | Keep as an operational subtask. Prove collection access and selected resource scope independently from written hosting description. The dedicated configuration route now exists; verify navigation locally. |

## Proposed navigation and screen contract

1. **System definition:** mission, people/roles, data, hosting, inventory, boundary, ports and interconnections.
2. **Controls & evidence:** categorization/baseline, capabilities, responsibility allocation, narratives and references, evidence.
3. **Assessment & risk:** SAP, assessment/import, SAR, findings, remediation, POA&M and exceptions.
4. **ATO package & eMASS:** purpose-specific checklist, artifact preview, generate/export, round-trip reconciliation, submission status and recorded decision.
5. **Continuous monitoring:** connected scope and health, rules, attributed changes, impact review, evidence refresh and update packages.

Overview remains the entry point. Administrative tools and histories remain accessible without competing with the next action.

Each working screen should answer:

- What am I documenting or deciding?
- Which package artifact or ongoing monitoring requirement uses it?
- What is saved, approved, outdated, missing, or unable to be verified?
- Who acts next, and what is the one primary action?

## Recommended order

1. Correct F1 and F2 before treating package output as submission-ready.
2. Establish profile/document mappings and one shared readiness assessment (F3–F5). Verify the newly registered assessment configuration route locally (F8).
3. Validate hosting/capability/boundary/inheritance/export traceability with a single realistic system. Existing responsibility reconciliation is a useful foundation, not evidence that every export contains the full source context.
4. Add executable, scoped ConMon rules and change-to-document impact review (F6–F7).
5. Reorganize navigation and progressively simplify screens around the validated flow (F9).

## Local acceptance scenarios still required

These are verification work, not claims of tests already passed:

1. Create a system with no AO decision. Complete required reviewed data, SAP/SAR and evidence. Generate an **initial submission** package without creating a fictitious decision.
2. Put a distinctive value in every profile section, approve it, and compare the actual generated artifacts. Each value must appear in its defined destination with the correct reviewed version. Editing a new draft must preserve the approved export until review.
3. Associate a CSP hosting allocation; apply a published capability; confirm responsibilities. Verify provider source, boundary/scope, control allocation and customer duties in the resulting SSP/CRM/control export.
4. Export a leveraged authorization with a known source date and type. They must match the source exactly. Missing dates/types must never become today's date or an assumed authorization type.
5. Give two systems different resource scopes in one subscription. A change in one scope must not automatically become a change in the other. Simulate lost telemetry: health must become unknown/degraded, not clear.
6. Configure a ConMon rule, trigger it, inspect evidence and document/control impact, record review, update only the affected draft artifacts, and retain the previous approved baseline.
7. Compare all readiness displays for the same system. Missing required sections, stale evidence, unavailable checks, and unresolved required reviews must agree across screens and link to working actions.
8. Test actual eMASS export/import against the intended deployment and authorized workflow. Verify format, field mappings, receipt/status handling and conflict review. No live eMASS compatibility is certified by this audit.

## Additional source anchors

- Routing: `src/Ato.Copilot.Dashboard/src/ApplicationRoutes.tsx`.
- Applied capabilities: `src/Ato.Copilot.Dashboard/src/features/workspace-operations/system-capabilities/SystemCapabilityList.tsx`.
- Hosting selection/confirmation: `src/Ato.Copilot.Dashboard/src/features/provider-relationships/MissionAssociationWizard.tsx`.
- Responsibility projection: `src/Ato.Copilot.Core/Services/CapabilityResponsibilityService.Reconciliation.cs:12`.
- eMASS control-export inputs: `src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs:88`.
- Assessment actions: `src/Ato.Copilot.Dashboard/src/pages/Assessments.tsx:161`.
- Narrative proposals/review: `src/Ato.Copilot.Dashboard/src/pages/NarrativeWorkspace.tsx:96`.
- Remediation/POA&M linkage: `src/Ato.Copilot.Dashboard/src/pages/Remediation.tsx:221`.

Line references describe this checkout and may shift with subsequent edits. The repository already contained extensive changes before the audit; this report does not claim those changes were implemented or validated in this audit.
