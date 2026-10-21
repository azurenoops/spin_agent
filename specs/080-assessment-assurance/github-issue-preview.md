# Exact GitHub write preview — assessment assurance

Repository: **azurenoops/spin_agent**. Prepared October 20, 2026. **Approved and published October 20, 2026. Exact content, labels and native parent links verified.**

## Proposed operations

1. Create the FEATURE issue below with its exact title, body and labels; no assignee or milestone.
2. Attach FEATURE as a native sub-issue of existing epic #1038.
3. Create US1, US2 and US3 below with their exact titles, bodies and labels; no assignees or milestones.
4. Attach each story as a native sub-issue of FEATURE, resolving its returned GitHub issue ID. No placeholder parent numbers will be sent.
5. Update only this new local specification with returned issue URLs and linkage verification. No pushes, commits, issue closures, existing body edits or reassignment of existing issues are proposed.

If an equivalent issue is discovered before publication, stop that creation and report the overlap. Verify returned native parent links; failures remain explicit and do not trigger duplicate creation.

## FEATURE: [Feature] Make assessment coverage, retests and STIG revisions auditable

Labels: `enhancement`, `priority:high`, `area:rmf`

Parent: existing #1038

### Exact issue body

## Outcome

An assessor can explain what was evaluated, what remains unassessed, why remediation is verified, and which STIG revision supports each result and exported checklist.

Parent epic: #1038. Attach this feature as an actual GitHub sub-issue. Specification: `specs/080-assessment-assurance/spec.md`.

## Basis and boundaries

October 20, 2026 source review at local HEAD `145446f0`, with uncommitted local changes; not a deployed or runtime-verified audit. Existing assessment results already retain plan revisions, missing controls, evidence snapshots and SCA reviews. Remediation already supports manual verification, evidence linkage and guarded task transitions. Extend those foundations.

New scope:
- Requirement/check-level coverage and assessment-method provenance beyond control-level counts.
- Comparable, evidence-bound retest results while preserving authorized manual assessment.
- STIG revision pinning, changed-rule review and truthful CKL export metadata.

## Existing ownership — do not duplicate or reparent

#982 owns authorized Azure collection scope. #983 owns failed scans being treated as successful assessments. #1044/#1045 own monitoring rules and coverage health. #1028/#1023 own provider publication and provider-to-system workflow acceptance. #1042 owns purpose-specific package readiness. Closed #57 and #674/#688 cover prior validation-link and finding-to-POA&M foundations, not proof this new scope is delivered.

## Acceptance

- [ ] All three linked user stories have verified acceptance evidence or an explicit unresolved disposition.
- [ ] One synthetic assessment demonstrates a covered check, failed check, unavailable source, manual requirement, comparable retest and STIG revision change.
- [ ] Reviewed source identity remains traceable into actual generated assessment/checklist output.
- [ ] Scope gaps, task completion and recorded risk acceptance cannot silently manufacture effective controls or an authorization decision.
- [ ] The user can run the local walkthrough before closure.

## Architecture and delivery requirements

Extend the current assessment/import, remediation, evidence and report services. Reuse existing tenant/system authorization, role policies, source snapshots and UI components; do not introduce a parallel scanner, findings store, identity system or approval workflow.

Update the specification, plan and tasks before implementation. Add failing-first tests using synthetic data. Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln`; expected outcomes are a successful build and passing applicable tests, with actual failures and unperformed checks disclosed. Run independent TypeScript checking and affected frontend tests for touched projects. Provide local manual-test steps and an opportunity for user acceptance before closure. No claim of live Azure or receiving-eMASS validation without execution.

---

## US1: [User Story] Show check-level assessment coverage and manual evaluation gaps

Labels: `enhancement`, `priority:high`, `area:rmf`

Parent: new FEATURE issue

### Exact issue body

## User outcome

As an SCA, I can see which requirements/checks were evaluated for an applicable control and which require further automated, examine, interview or test work, without mistaking one observation for complete control coverage.

Parent feature: "[Feature] Make assessment coverage, retests and STIG revisions auditable". Attach this story as an actual sub-issue of the newly created feature. Specification: `specs/080-assessment-assurance/spec.md` — US1.

## Verified foundation and evidence limits

`AssessmentResultsWorkspaceService.cs` already projects observed, missing, excluded and duplicate control IDs. `AssessmentResultProvenance.cs` retains plan pins, evidence and human reviews. These are useful control-level foundations; this proposal adds an explicit requirement/check coverage contract. No runtime defect is asserted by this story.

## Acceptance

- [ ] An applicable assessment profile identifies expected requirements/checks, source/catalog version, method, target scope and mapping provenance.
- [ ] Each check distinguishes pass, fail, unavailable/error, not evaluated, manual review required and reviewed not-applicable; collection status and SCA determination remain separate.
- [ ] Multiple checks mapped to one control do not imply full coverage when only one ran; unknown denominators remain unknown.
- [ ] Manual/procedural requirements expose a named next task and required evidence without fabricating scanner coverage.
- [ ] Existing assessment UI and shared HTTP/MCP use cases expose consistent coverage and deny other-system/tenant records.
- [ ] Actual SAR output retains coverage gaps and source versions; package readiness consumes applicable gaps through #1042 rather than a second readiness calculator.

## Scope and tasks

This does not replace #982's collection authorization or #983's failure aggregation fixes.
- [ ] T1: Inventory supported check mappings and add failing partial/manual/unknown coverage tests.
- [ ] T2: Extend existing provenance and projections with versioned check-level coverage.
- [ ] T3: Verify API/MCP, UI and actual generated-report parity.

Local acceptance: select a synthetic control with two technical checks and one manual objective; run only one check and confirm remaining work is visible in the UI and generated report.

## Architecture and delivery requirements

Extend the current assessment/import, remediation, evidence and report services. Reuse existing tenant/system authorization, role policies, source snapshots and UI components; do not introduce a parallel scanner, findings store, identity system or approval workflow.

Update the specification, plan and tasks before implementation. Add failing-first tests using synthetic data. Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln`; expected outcomes are a successful build and passing applicable tests, with actual failures and unperformed checks disclosed. Run independent TypeScript checking and affected frontend tests for touched projects. Provide local manual-test steps and an opportunity for user acceptance before closure. No claim of live Azure or receiving-eMASS validation without execution.

---

## US2: [User Story] Bind remediation verification to comparable retest evidence

Labels: `enhancement`, `priority:high`, `area:rmf`

Parent: new FEATURE issue

### Exact issue body

## User outcome

As an authorized remediation reviewer, I can distinguish a verified technical retest from a manual attestation, failed collection or a finding merely absent from a later scan.

Parent feature: "[Feature] Make assessment coverage, retests and STIG revisions auditable". Attach this story as an actual sub-issue of the newly created feature. Specification: `specs/080-assessment-assurance/spec.md` — US2.

## Source observations — runtime reproduction required

`KanbanService.ValidateTaskAsync` evaluates each affected-resource entry, interprets returned text containing "compliant" or "pass" as success, and calculates `AllPassed` using `All` over the collected resource results. The loop calls validation with a finding/control ID and subscription, without passing that iteration's resource.

`RemediationWorkspaceService.VerifyAsync` separately records authorized manual Passed/Failed status and review notes. Evidence linkage and task edits reset verification; preserve those safeguards.

Source inspection identifies empty-target, ambiguous-text and target-comparability risks. These cases have not been reproduced through a running endpoint in this review.

## Acceptance

- [ ] First reproduce empty target coverage and ambiguous responses such as "noncompliant" and "did not pass" with tests against the real service; they cannot establish positive validation.
- [ ] Automated verification uses typed outcomes and retains original finding, retest result, target scope, check/catalog version, time and evidence identity.
- [ ] Each target is actually evaluated or explicitly uncovered; missing targets, failed credentials and partial scans remain incomplete.
- [ ] A finding absent from a later scan or a closed task alone cannot establish verified technical resolution.
- [ ] Manual verification remains available under existing permissions with actor, rationale and reviewed evidence/limitations; it is labeled separately from an automated retest.
- [ ] Stale or changed evidence invalidates affected verification; authorized exceptions retain reasons and do not imply a passing scan.
- [ ] Concurrent/retried verification is safe, tenant/system isolation holds, and downstream findings/POA&M/report projections preserve the verification basis.

## Tasks and local acceptance

- [ ] T1: Add failing ambiguous-output, zero-target, wrong-target and partial-result tests.
- [ ] T2: Extend existing verification contracts/provenance and existing HTTP/MCP consumers.
- [ ] T3: Test a two-target finding, incomplete retest, complete passing retest and documented manual assessment through the UI and retained history.

Coordinate #983's assessment failure semantics and #992's transition-service ownership. Do not replace existing task/POA&M relationships or closure permissions.

## Architecture and delivery requirements

Extend the current assessment/import, remediation, evidence and report services. Reuse existing tenant/system authorization, role policies, source snapshots and UI components; do not introduce a parallel scanner, findings store, identity system or approval workflow.

Update the specification, plan and tasks before implementation. Add failing-first tests using synthetic data. Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln`; expected outcomes are a successful build and passing applicable tests, with actual failures and unperformed checks disclosed. Run independent TypeScript checking and affected frontend tests for touched projects. Provide local manual-test steps and an opportunity for user acceptance before closure. No claim of live Azure or receiving-eMASS validation without execution.

---

## US3: [User Story] Preserve STIG revision identity through review and CKL export

Labels: `enhancement`, `priority:high`, `area:rmf`

Parent: new FEATURE issue

### Exact issue body

## User outcome

As an SCA, I can identify the exact STIG/rule revision behind an assessment, review meaningful changes, and export a checklist whose metadata matches the selected assessment evidence.

Parent feature: "[Feature] Make assessment coverage, retests and STIG revisions auditable". Attach this story as an actual sub-issue of the newly created feature. Specification: `specs/080-assessment-assurance/spec.md` — US3.

## Verified source starting points

`ScanImportRecord` already has `BenchmarkVersion`. `StigControl` includes optional `StigVersion`, and `StigBenchmark` has `Version`. However, the inspected `ScanImportService.ExportCklAsync` assigns benchmark version "1" and reads benchmark controls from the current knowledge service before generating XML.

This is a source-confirmed metadata default and a revision-provenance concern. Actual emitted XML and affected production records have not been runtime-verified in this review.

## Acceptance

- [ ] A failing service-level export test demonstrates that a non-v1 selected assessment cannot be exported as version "1".
- [ ] Import/review/export pin source benchmark and rule identity/version plus meaningful check-content identity; mixed or unavailable provenance is explicit.
- [ ] Real generated CKL XML contains source-backed benchmark metadata and results from the selected authorized system/assessment, without a fabricated default.
- [ ] A newer catalog identifies added, changed, unchanged and removed rules without rewriting historical results.
- [ ] Reuse of unchanged-rule reviews requires documented matching/applicability criteria and retained lineage; changed or ambiguous rules require reassessment.
- [ ] Missing required revision content produces an actionable unavailable state rather than silently substituting the latest catalog.
- [ ] Existing assessment/UI/MCP flows expose revisions and pending review; historical artifacts and approved baselines remain intact.
- [ ] Tests cover catalog changes, repeated imports, conflicting metadata, wrong-system selection and actual XML output.

## Tasks and local acceptance

- [ ] T1: Reproduce export metadata mismatch and define source/revision matching policy.
- [ ] T2: Extend existing catalogs, retained import provenance and export selection without a parallel STIG repository.
- [ ] T3: Import synthetic V2 content, review it, introduce a changed revision, and inspect actual generated XML plus retained earlier results.

Closed #204/#131 provide import/streaming history; do not reopen them without separately verified regression evidence.

## Architecture and delivery requirements

Extend the current assessment/import, remediation, evidence and report services. Reuse existing tenant/system authorization, role policies, source snapshots and UI components; do not introduce a parallel scanner, findings store, identity system or approval workflow.

Update the specification, plan and tasks before implementation. Add failing-first tests using synthetic data. Run `dotnet build Ato.Copilot.sln` and `dotnet test Ato.Copilot.sln`; expected outcomes are a successful build and passing applicable tests, with actual failures and unperformed checks disclosed. Run independent TypeScript checking and affected frontend tests for touched projects. Provide local manual-test steps and an opportunity for user acceptance before closure. No claim of live Azure or receiving-eMASS validation without execution.

---


## Publication results

- FEATURE: [#1066](https://github.com/azurenoops/spin_agent/issues/1066); native parent #1038; verified.
- US1: [#1067](https://github.com/azurenoops/spin_agent/issues/1067); native parent #1066; verified.
- US2: [#1068](https://github.com/azurenoops/spin_agent/issues/1068); native parent #1066; verified.
- US3: [#1069](https://github.com/azurenoops/spin_agent/issues/1069); native parent #1066; verified.

Published via the signed-in GitHub CLI after the connector returned a 403 permission error. No duplicate issue was created by the connector. No existing issue bodies, assignees, milestones or states were changed.
