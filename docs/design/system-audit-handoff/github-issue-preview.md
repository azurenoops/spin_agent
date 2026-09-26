# GitHub issue publication preview

Target repository: `azurenoops/ato-copilot`.

Proposed writes: create the following **one parent and eight child issues**, then attach the eight children using GitHub sub-issue relationships. No existing issue bodies, labels, assignees, milestones or parents will change. No code push, PR or Copilot assignment is included. Titles and bodies below are the exact proposed text. Issue numbers are assigned by GitHub; hierarchy is applied after creation.

Duplicate check: read 708 existing issues (all states) on September 26, 2026. Related existing work is referenced in the bodies. F8 is resolved and excluded. Published after user approval, including the requested review-and-close instruction. See `published-issues.json` for assigned issue numbers and URLs.

---

## EPIC — [Epic] Align system workflows with eMASS ATO submission and continuous monitoring

## Product outcome

SPIN AGENT must help a Mission Owner prepare a fully documented, reviewed system package for initial ATO submission to eMASS, then maintain that baseline through scoped Azure monitoring and accountable change review. Every screen must identify its artifact contribution and next action.

## Audit basis

September 26, 2026 source-based audit of 24 system navigation destinations and supporting flows in a changing local checkout. Findings require current-code reproduction; this audit is not live eMASS acceptance. Local reference: `docs/design/system-product-goal-audit-2026-09-26.md` (may not yet be present on the remote branch). Child issue bodies contain the actionable context.

## Workstreams

- F1: Support initial ATO submission packages without a pre-existing authorization decision
- F2: Export source-backed leveraged authorization metadata in OSCAL SSP
- F3: Trace approved system profile data into generated SSP and package artifacts
- F4: Unify purpose-specific submission readiness across system overview, package and eMASS screens
- F5: Expose unknown validation and failed loads instead of passing or empty states
- F6: Implement executable ConMon rules with reviewed change impact and reauthorization recommendations
- F7: Attribute Azure monitoring changes to reviewed system scope and expose coverage health
- F9: Organize system screens around ATO submission and ongoing monitoring tasks

F8 (Azure assessment destination) was resolved in the final source recheck. Retain navigation acceptance coverage; do not open it as an outstanding bug.

## Existing work to coordinate

- #969: structured profile child persistence.
- #970: OSCAL legal-authority mapping.
- #764: pre-export schema validation.
- #980 and #647: phase-readiness/gate enforcement.
- #754 and #676: monitoring cadence/scheduling.
- #1025: shared workspace navigation.
- #998 and #999: ConMon report/count correctness.

Link these as related dependencies; do not move existing issues from their current parents or reopen closed issues without verifying the need.

## Delivery order

1. F1 and F2: package purpose and truthful authorization metadata.
2. F3, F4 and F5: reviewed data mapping, shared readiness, truthful failure states.
3. F7 then F6: scoped monitoring attribution and executable rules/impact review.
4. F9: integrate the validated journey and progressive screen simplification.

## Epic acceptance

- [ ] All eight findings are resolved with evidence or explicitly dispositioned after current-code verification.
- [ ] A synthetic initial package generates before an AO decision, with source-faithful content and consistent readiness.
- [ ] Approved profile and responsibility data appears in actual generated artifacts; draft updates preserve the approved baseline.
- [ ] Two systems sharing an Azure subscription receive correctly scoped monitoring impacts and honest telemetry health.
- [ ] A configured rule produces evidence, reviewed impact and staged document updates without manufacturing authorization.
- [ ] Actual target eMASS export/import validation is documented separately; unperformed live checks remain explicit.
- [ ] The user receives local manual-test steps before completion is declared.


## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.


---

## F1 — [System audit] Support initial ATO submission packages without a pre-existing authorization decision

## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F1

Priority: Critical.

PackageValidationService requires an active authorization decision and AuthorizationPackageService rejects generation when that validation fails. This conflicts with preparing the initial package for an AO decision. The finding is based on the inspected source path, not a live generation test.

## Required outcome

Introduce explicit initial-submission, authorized-baseline archive, and change/reauthorization package purposes. Define purpose-specific requirements across persistence, services, API/MCP contracts and the package UI. Initial submission must not create or require a fictitious AO decision. Preserve applicable requirements for other package purposes.

## Acceptance criteria

- [ ] An otherwise complete initial package generates with no AO decision, and creates no decision or authorization status change.
- [ ] Authorized-baseline archives require the appropriate recorded decision; change packages identify their reviewed baseline.
- [ ] Purpose, reviewed source versions and validation results are retained with the package.
- [ ] Tests cover each purpose, legacy requests/migration behavior, missing applicable requirements and unauthorized calls.
- [ ] A user can locally preview and generate a synthetic initial package and inspect its actual contents.

## Source starting points

- `src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs`
- `src/Ato.Copilot.Agents/Compliance/Services/AuthorizationPackageService.cs`
- `src/Ato.Copilot.Dashboard/src/pages/Documents.tsx`

## Related work and scope boundary

Related: #647 (gate enforcement), #669 (package ownership). This issue owns package-purpose semantics, not a blanket relaxation of validation.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.


---

## F2 — [System audit] Export source-backed leveraged authorization metadata in OSCAL SSP

## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F2

Priority: Critical.

OscalSspExportService constructs leveraged authorizations from provider names, appends “FedRAMP Authorization,” uses the export date as date-authorized and creates a fresh party UUID. These values do not come from the retained authorization source in that construction path.

## Required outcome

Resolve the applicable retained authorization record and its reviewed version. Export faithful metadata using schema-supported fields and references. Surface missing or ambiguous source data as a gap rather than inventing dates, authorization type, authority or coverage. Define stable identity/reference handling for repeated exports.

## Acceptance criteria

- [ ] Known authorization title, type and date are preserved exactly; export time is never substituted for authorization time.
- [ ] Referenced parties resolve to actual exported parties; identifiers remain coherent across artifacts.
- [ ] Missing, conflicting or inapplicable source records produce explicit validation findings under the package-purpose policy.
- [ ] Synthetic fixtures cover multiple providers, dates, missing metadata and repeated exports; inspect real generated OSCAL.
- [ ] No source authorization or AO decision is created by exporting.

## Source starting points

- `src/Ato.Copilot.Agents/Compliance/Services/OscalSspExportService.cs`

## Related work and scope boundary

Related: #970 owns incorrect legal-authority section mapping; #764 owns export schema validation. This issue owns leveraged-authorization provenance.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.


---

## F3 — [System audit] Trace approved system profile data into generated SSP and package artifacts

## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F3

Priority: High.

Profile save/approval writes SystemProfileSections and ApprovedContent. Inspected SSP generation reads RegisteredSystems and SspSections plus related records. An approved-profile-to-export bridge was not verified; reproduce the missing connection before implementing a new one.

## Required outcome

Document and implement the canonical mapping from all six profile sections, including structured child records, to applicable generated artifact fields. Reuse the reviewed source, show “Used in” and the generated-section preview, and keep draft edits separate from the approved export baseline.

## Acceptance criteria

- [ ] A field-to-artifact mapping covers mission, users, environment, data, ports/protocols/services and leveraged authorizations.
- [ ] Distinctive approved fixture values survive save/reload and appear in their documented export destinations.
- [ ] Draft edits do not replace approved scalar or child data in approved-baseline exports.
- [ ] Missing or unsupported mappings surface as gaps rather than a misleading completion badge.
- [ ] Tests compare actual artifact values and reviewed versions; local manual steps cover the full flow.

## Source starting points

- `src/Ato.Copilot.Agents/Compliance/Services/SystemProfileService.cs`
- `src/Ato.Copilot.Agents/Compliance/Services/SspService.cs`
- `src/Ato.Copilot.Dashboard/src/pages/SystemProfile.tsx`

## Related work and scope boundary

Depends on or coordinates with #969 for structured child persistence. Do not duplicate that bug. Coordinate F2 for leveraged authorization output and #970 for legal section mapping.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.


---

## F4 — [System audit] Unify purpose-specific submission readiness across system overview, package and eMASS screens

## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F4

Priority: High.

EmassExportReadinessService, PackageValidationService and Documents use different completeness rules. One approved SSP section can satisfy the export advisory while other screens use narrative percentages. Approval of existing sections does not establish that every required section exists.

## Required outcome

Provide one server-owned, purpose-specific submission-readiness assessment consumed by Overview, Documents and eMASS Workflow. Keep profile progress, RMF phase advancement, schema validity and submission readiness distinguishable. Each requirement identifies applicability, severity, owner, source/version, artifact destination and a working action link.

## Acceptance criteria

- [ ] The three screens agree on blockers for the same source snapshot and package purpose.
- [ ] Required section presence is checked as well as approval; optional/not-applicable items are not silently made mandatory.
- [ ] Missing, stale, incomplete and unable-to-verify results are distinct and accessible.
- [ ] Fix links resolve in the correct tenant/system and remain usable through direct navigation.
- [ ] Tests cover complete and incomplete packages, unavailable checks, purpose differences and UI/API parity.

## Source starting points

- `src/Ato.Copilot.Agents/Compliance/Services/EmassExportReadinessService.cs`
- `src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs`
- `src/Ato.Copilot.Dashboard/src/pages/Documents.tsx`
- `src/Ato.Copilot.Dashboard/src/pages/EmassStatus.tsx`

## Related work and scope boundary

Coordinate F1, F3 and F5. #980 owns phase-advancement UI mismatch and #647 gate enforcement; this issue owns submission readiness, not duplicate phase gates.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.


---

## F5 — [System audit] Expose unknown validation and failed loads instead of passing or empty states

## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F5

Priority: High.

Package schema-check exceptions become warnings and evidence-summary exceptions add no finding; validity is based on zero errors. Documents converts package-history load failure to an empty list. Legal & Regulatory suppresses assignment/removal errors.

## Required outcome

Make failed required checks explicitly unable to verify and prevent final submission readiness from passing them. Preserve meaningful draft-generation behavior. Show unavailable/stale states and retry for affected UI loads and mutations without pretending records are absent or operations succeeded.

## Acceptance criteria

- [ ] Required schema/evidence checks that cannot run prevent a final-ready result and retain diagnostic context.
- [ ] A failed package-history request shows unavailable/retry, not “No packages generated.”
- [ ] Legal assignment/removal failures are visible and do not falsely report success.
- [ ] Retries preserve correct prior data and last-known timestamps without dropping tenant/system scope.
- [ ] Fault-injection tests cover validation exceptions, API failures and successful recovery.

## Source starting points

- `src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs`
- `src/Ato.Copilot.Dashboard/src/pages/Documents.tsx`
- `src/Ato.Copilot.Dashboard/src/pages/LegalRegulatory.tsx`

## Related work and scope boundary

Coordinate #764 for the existing schema gate. F5 owns unknown/error semantics and surfaced failures; F4 consumes those results.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.


---

## F6 — [System audit] Implement executable ConMon rules with reviewed change impact and reauthorization recommendations

## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F6

Priority: High.

ConMon saves custom triggers as strings, while CheckReauthorizationAsync uses fixed expiration, change-type and score rules. This does not establish the requested configurable rule-to-system-impact workflow.

## Required outcome

Separate written monitoring-plan text from executable rules. Reuse existing monitoring infrastructure where possible. Rules need a signal, scoped target, condition, cadence, severity, owner and response. Show evidence and affected controls/documents, route review, preserve the approved baseline, and distinguish a recommendation from an accountable authorization decision.

## Acceptance criteria

- [ ] Authorized users create/edit/test/disable a persisted rule and can see evaluation health and history.
- [ ] A synthetic signal causes a deterministic, deduplicated rule evaluation with evidence and explained impact.
- [ ] Affected control/evidence/narrative/artifact updates are staged for review; approved content remains preserved.
- [ ] Reassessment/reauthorization recommendations cite the rule and approved criteria; workflow mutation is explicitly authorized and audited.
- [ ] Tests cover nonmatching signals, replay, disabled rules, unauthorized operations, failed telemetry and review decisions.

## Source starting points

- `src/Ato.Copilot.Dashboard/src/pages/ConMon.tsx`
- `src/Ato.Copilot.Agents/Compliance/Services/ConMonService.cs`

## Related work and scope boundary

Coordinate F7 for scope attribution and F4 for package impact. Reuse #754/#676 for assessment cadence work; do not duplicate their schedule/calendar scope.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.


---

## F7 — [System audit] Attribute Azure monitoring changes to reviewed system scope and expose coverage health

## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F7

Priority: High.

The inspected ConMon overview queries enabled monitoring and drift counts by subscription IDs. It does not restrict those counts to the system boundary/resources. Enabled means a configuration exists, not that every intended scope has fresh successful telemetry.

## Required outcome

Attribute events against the reviewed system resource/boundary scope and relevant shared-provider dependencies. Distinguish system, shared-provider, out-of-scope and unknown changes. Present connected scope, monitored coverage, last successful collection, gaps and health separately. Preserve source evidence and trace component/control/document impact.

## Acceptance criteria

- [ ] Two systems sharing a subscription receive correct distinct attribution for scoped resource changes.
- [ ] Shared-provider changes show dependency-based impact without implying universal system coverage.
- [ ] Unmapped events are unknown, not silently attributed or discarded as safe.
- [ ] Missing/failed/stale telemetry shows degraded or unknown health, never a clear posture based only on zero alerts.
- [ ] Tests cover boundary revisions, resource moves/deletion, shared dependencies and tenant isolation.

## Source starting points

- `src/Ato.Copilot.Mcp/Endpoints/Dashboard/DashboardConMonEndpoints.cs`
- `src/Ato.Copilot.Agents/Compliance/Services/ConMonService.cs`

## Related work and scope boundary

Provides scope/health semantics for F6. Related #998/#999 own historical-report and overdue-count defects, not event attribution.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.


---

## F9 — [System audit] Organize system screens around ATO submission and ongoing monitoring tasks

## Product goal

Prepare a fully documented system for ATO submission to eMASS and maintain its reviewed baseline through continuous monitoring.

## Audit finding F9

Priority: Medium.

The system exposes 24 primary destinations; overlapping risk views and separated package/export screens obscure the next submission task. Preserve recent Environment, capability/component and collapsed-panel improvements. Audit F8 is resolved in source and must not be reopened as a route defect.

## Required outcome

Organize existing screens around System Definition, Controls & Evidence, Assessment & Risk, ATO Package & eMASS, and Continuous Monitoring. Lead Overview with readiness and next action. Keep specialist records, permissions and history. Each screen states its purpose, artifact contribution, status and responsible next action.

## Acceptance criteria

- [ ] All 24 existing destinations have a documented keep/group/redirect disposition with no lost records or actions.
- [ ] Old deep links, back/forward and workspace/system context continue to work.
- [ ] Readiness uses F4 rather than new browser-only rules or sample success states.
- [ ] Keyboard/mobile navigation and denied/loading/empty/error/stale states are tested.
- [ ] Local user walkthrough completes initial package preparation and monitoring impact review with clear next actions; include F8 route verification.

## Source starting points

- `src/Ato.Copilot.Dashboard/src/components/layout/SystemLayout.tsx`
- `src/Ato.Copilot.Dashboard/src/pages/SystemDetail.tsx`
- `src/Ato.Copilot.Dashboard/src/pages/Documents.tsx`
- `src/Ato.Copilot.Dashboard/src/pages/EmassStatus.tsx`

## Related work and scope boundary

Coordinate #1025 for shared workspace shell/navigation. This issue owns the in-system task journey. Integrate F4 and F6/F7; do not replace their backend work with UI-only status.

## Delivery requirements

Reverify the current execution path before changing code: the September 26 audit inspected a changing local checkout, not a deployed system. Update the relevant spec, plan and tasks before implementation. Preserve tenant/system authorization, reviewed versions and unrelated working-tree changes. Add meaningful regression tests, run applicable checks, and provide local manual-test steps and actual artifact evidence. Do not claim live Azure or eMASS acceptance without performing that test. Follow repository approval rules for external writes.


## Issue review and closure

Review this issue and related issues before implementation to confirm current scope, dependencies and completed work. When all acceptance criteria are met, document the implementation, verification results, actual artifact evidence where applicable, and local manual-test instructions. Close the issue only after completion is verified and the user has been given the opportunity to test locally. Keep unresolved criteria and unperformed required checks open and explicit. Review related issues for completion, but do not close them merely because this issue is fixed; verify their own criteria. Close the parent only after every child is completed or explicitly dispositioned with evidence.
