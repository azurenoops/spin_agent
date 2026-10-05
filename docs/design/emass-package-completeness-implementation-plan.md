# Implementation plan: a fully documented eMASS package

**Date:** October 4, 2026

**Status:** Proposed; planning only, not an implementation-completion claim

**Inspected baseline:** `f4469210` plus the existing local component-review changes
**Coordination:** [Feature 041](../../specs/041-emass-package/spec.md),
[Feature 079](../../specs/079-provider-system-workflow-consolidation/spec.md),
[requirement coverage](../../specs/076-oscal-full-compliance/spec.md),
[assessment validation](../../specs/069-sca-control-validation/spec.md),
[eMASS exchange](../../specs/071-emass-workflow-sync/spec.md)

## 1. Outcome and scope

SPIN should prepare and maintain a **versioned, internally consistent,
evidence-backed package for a specified system and receiving eMASS process**.
Every required claim must be traceable to its reviewed source, responsibility,
assessment scope, result and evidence. Outstanding weaknesses must have a
documented risk response, not disappear behind a completion percentage.

Fully documented does **not** mean zero findings, an automatic ATO, or an
automatic eMASS submission. Initial preparation must not require an already
issued AO decision. A clean assessment may legitimately produce an empty
POA&M. An assessment with unresolved weaknesses may not silently do so.

This plan strengthens the existing services and workflows. It does not replace
the component redesign, create a competing assessment/review store, or rewrite
retained packages. Direct eMASS API transport and live Azure collection are
separate delivery decisions; neither is necessary to produce a correctly
documented manual handoff package.

### Basis and limitations

The preceding audit reviewed selected public RMF Insider guides, primary NIST
pages, implementation and existing test source. No live eMASS acceptance or
complete retained-data package was demonstrated. Blog examples are not binding
policy: artifact ages, approval chains, package formats and required supporting
plans must be established for the receiving organization.

Primary references: [NIST RMF](https://csrc.nist.gov/projects/risk-management/about-rmf),
[SP 800-37 Rev. 2](https://csrc.nist.gov/pubs/sp/800/37/r2/final),
[SP 800-53A Rev. 5](https://csrc.nist.gov/pubs/sp/800/53/a/r5/final).
Practical references: [submission preflight](https://rmfinsider.com/2026/09/08/how-to-submit-a-package-in-emass/),
[test results](https://rmfinsider.com/2026/09/07/emass-test-results-ccis-assessment-procedures/),
[artifact association](https://rmfinsider.com/2026/09/03/how-to-upload-artifacts-in-emass/).

## 2. Verified starting point

- [PackageValidationService](../../src/Ato.Copilot.Agents/Compliance/Services/PackageValidationService.cs)
  already blocks source-qualified requirement-coverage gaps, provider provenance
  gaps, missing required SSP sections, unfinalized SAP and unapproved SAR. However,
  its overall evidence coverage, orphaned POA&M references and unresolved
  inherited allocations/source impacts can be advisory.
- [RequirementCoverageDocumentData](../../src/Ato.Copilot.Agents/Compliance/Services/RequirementCoverageDocumentData.cs)
  already validates source-bound responses, parameters, evidence pins and
  independent human review. Reuse it; do not build a second requirement matrix.
- [PackageReadinessService](../../src/Ato.Copilot.Agents/Compliance/Services/PackageReadinessService.cs)
  retains purpose-bound checks and source hashes; generation requires matching
  readiness. Preserve its freshness/concurrency protections.
- [EmassExportReadinessService](../../src/Ato.Copilot.Agents/Compliance/Services/EmassExportReadinessService.cs)
  has separate identifier checks and weaker SSP/POA&M advisories. Different
  export routes therefore do not share one completeness definition.
- [AssessmentResultProvenance](../../src/Ato.Copilot.Core/Models/Compliance/AssessmentResultProvenance.cs)
  retains plan pins, evidence snapshots and human reviews. `ResultReview` and the
  current [assessment workspace request](../../src/Ato.Copilot.Mcp/Services/AssessmentResultsWorkspaceService.cs)
  identify reviews by control, not an explicit assessment-procedure identifier.
- [SapService](../../src/Ato.Copilot.Agents/Compliance/Services/SapService.cs)
  finalization verifies authority/revision and hashes the plan; it does not
  establish assessment execution. [SAR approval](../../src/Ato.Copilot.Agents/Compliance/Services/SecurityAssessmentReportService.cs)
  changes document status without an exhaustive required-procedure coverage gate.
- [EvidenceArtifact](../../src/Ato.Copilot.Core/Models/Compliance/EvidenceArtifactModels.cs)
  has hashes, upload time, associations and retained file versions. Upload time
  is not evidence-capture time or a reviewed statement of continued validity.
- [EmassBridgeService](../../src/Ato.Copilot.Agents/Compliance/Services/EmassBridgeService.cs)
  transforms payloads without remote transport, truncates long narratives and
  defaults control designation to System-Specific.
- [EmassWorkflowStatusService](../../src/Ato.Copilot.Agents/Compliance/Services/EmassWorkflowStatusService.cs)
  derives export freshness from local exports/changes. That is different from
  receiving-system import acceptance or CAC/PAC approval.

## 3. Implementation sequence

### Phase 0 — Define the package contract and reconcile current requirements

**Change:** replace ambiguous “eMASS-ready” assumptions with an approved,
purpose-specific preparation and handoff contract.

1. Establish target Component/instance, supported manual import formats and field
   limits, catalog/assessment-procedure/CCI mapping versions, package purpose,
   required artifacts, review roles and evidence freshness rules.
2. Reconcile historical Feature 041 statements about universal OSCAL import and
   “six required artifacts.” Separate SPIN's output bundle from formats actually
   accepted by the selected instance.
3. Define which requirements block InitialSubmission, which apply to a scoped
   assessment/change, and which only verify a retained archive. Do not subject a
   historic archive to today's full initial-submission checklist.
4. Trace existing plan, finding, risk, exception, privacy, ISA and review services
   before designing additions. Missing verification is not proof a feature is
   missing.
5. Create Feature/User Story issue linkage only after preview and approval of the
   exact external writes. Keep planning status explicit until that gate is met.

**Deliverable:** reconciled specifications, approved requirements checklist,
typed contract/data-model decisions and a synthetic acceptance fixture.

**Acceptance:** the same fixture yields the same required checks for every
entry point; target-specific policy and unknown applicability are visible.

### Phase 1 — Unify readiness and make blockers meaningful

**Change:** one canonical semantic evaluation, reused by package generation,
standalone submission exports, dashboard and applicable MCP tools.

**Affected surfaces:** `PackageValidationService`, `PackageReadinessService`,
[PackageReadinessSources](../../src/Ato.Copilot.Agents/Compliance/Services/PackageReadinessSources.cs),
`EmassExportReadinessService`,
[package endpoints](../../src/Ato.Copilot.Mcp/Endpoints/PackageEndpoints.cs),
[readiness endpoints](../../src/Ato.Copilot.Mcp/Endpoints/Dashboard/DashboardPackageReadinessEndpoints.cs),
[readiness UI](../../src/Ato.Copilot.Dashboard/src/features/systems/PackageReadinessExperience.tsx).

- Extract/reuse purpose-aware checks, not two services calling each other.
  Make the export checker an adapter to the canonical evaluator where appropriate.
- Retain check identity, required/applicable state, policy version, source pins,
  owner, finding details and resolution links. Required-but-unavailable and
  unknown-applicability must never produce Ready.
- Add confirmed target-identifier prerequisites at the correct handoff stage.
  Draft preparation can precede allocation of an eMASS ID; targeted import cannot
  claim a known receiving record when the ID is missing.
- Block submission preparation for unresolved required responsibility decisions,
  invalid source/control references and uncovered applicable requirements.
  Do not blindly turn every warning or aggregate percentage into an error.
- Preserve immutable readiness history. Include new policy, procedure mapping,
  evidence metadata and artifact applicability inputs in source hashing and
  expiration. Bump the rule version; revalidate new submissions under it.
- Make UI labels explicit: Ready for the selected preparation purpose, not
  accepted by eMASS or authorized. Keep working previews available and labelled.

**Acceptance:** bypassing the dashboard cannot bypass required checks; stale,
denied and unavailable evaluations fail visibly. Zero POA&M items pass only when
the assessed weakness/risk disposition supports that result.

### Phase 2 — Add source-qualified procedure-level assessment coverage

**Change:** extend the existing plan/result provenance rather than treating a
control-level review as a result for every assessment procedure beneath it.

**Affected surfaces:** `AssessmentResultProvenance`,
[requirement coverage models](../../src/Ato.Copilot.Core/Models/Compliance/RequirementCoverageModels.cs),
`AssessmentResultsWorkspaceService`,
[assessment result endpoints](../../src/Ato.Copilot.Mcp/Endpoints/AssessmentResultsWorkspaceEndpoints.cs),
`SapService`, `SecurityAssessmentReportService` and their DTOs/editors/exporters.

- Pin the authoritative assessment procedure and, where applicable, explicit
  CCI mapping/version. A requirement statement, procedure and CCI are different
  identifiers; do not infer a one-to-one mapping from names or numbers.
- Retain procedure-level scope, tested object/system area, actual method,
  performer and performance date, observation/result narrative, evidence version
  pins, review identity and source revision. Support examine/interview/test and
  manual assessment as well as imported observations.
- Keep imported/AI observations separate from human assessment determinations.
  A scan mapping may suggest a result; it must not fabricate tests or reviewers.
- Preserve old control-level records as legacy/unmapped until an authorized user
  reconciles them. Do not backfill one control result into every procedure.
- Finalize a SAP when its planning scope/methods/sampling/roles and required
  source mappings are complete. Do **not** require completed tests before
  finalizing the plan that precedes those tests.
- Gate SAR review and initial-package readiness against the selected approved
  assessment scope. An approved subset SAR may remain legitimate for ConMon or
  a scoped change, but cannot silently satisfy a full initial assessment.
- Enforce reviewer authority and required independence through current server
  policy, including service/MCP paths. Record approved N/A/inherited/scoped-out
  dispositions and evidence; never use a blanket `pending == 0` rule that erases
  legitimate scope distinctions.

**Acceptance:** unresolved procedure gaps cannot be hidden by an Approved SAR;
legacy, conflicting, stale and wrongly mapped results remain explicit. Approved
subset assessments cannot claim full-baseline coverage.

### Phase 3 — Reconcile findings, SAR, POA&M and residual risk

**Change:** bidirectional, source-qualified cross-artifact reconciliation.

**Affected surfaces:** `PackageValidationService`, SAR/result provenance,
[PoamService](../../src/Ato.Copilot.Core/Services/PoamService.cs),
[POA&M history/link models](../../src/Ato.Copilot.Core/Models/Poam/PoamModels.cs),
[POA&M exporter](../../src/Ato.Copilot.Agents/Compliance/Services/OscalPoamExportService.cs).

- Use existing finding/risk IDs and links where present. For each applicable
  unresolved weakness, require its retained SAR finding and an appropriate
  POA&M/risk-response record. Find missing links in both directions.
- Validate affected system/assets/procedures, source severity, assessed risk,
  owner, realistic scheduled milestones, required resources, interim mitigation,
  slippage rationale and closure evidence according to the approved policy.
- Keep scanner severity and residual risk separate. Preserve justified reassessment
  rather than forcing every risk rating to equal the raw scanner score.
- Do not auto-close because a task is complete or a new scan omitted an asset.
  Require the existing authorized closure/reassessment workflow and pinned proof.
- Trace existing risk-report outputs. If the target requires an RAR not currently
  produced, generate a reviewable report from canonical risk/mission/findings data
  or register the approved external report. Do not invent threat likelihood,
  compensating controls or signed risk acceptance.

**Acceptance:** no orphaned finding or POA&M can silently pass the applicable
gate; honest open findings with complete plans remain eligible where policy allows.

### Phase 4 — Govern supporting artifacts and evidence freshness

**Change:** make “which supporting documents are required?” a recorded,
reviewable applicability decision with actual versioned artifacts.

**Affected surfaces:** evidence/version models and existing evidence workflow,
`PackageReadinessSources`, `PackageValidationService`,
[PrivacyService](../../src/Ato.Copilot.Agents/Compliance/Services/PrivacyService.cs),
[InterconnectionService](../../src/Ato.Copilot.Agents/Compliance/Services/InterconnectionService.cs),
[ConMonService](../../src/Ato.Copilot.Agents/Compliance/Services/ConMonService.cs).

- Reuse evidence storage, approved references and review services. Introduce a
  supporting-artifact register only if no existing authoritative association
  supports artifact type, applicability, scope, owner, approval and version.
- Cover target-required CP/IR/CM/ConMon plans, SOPs, privacy determinations/PIAs,
  ISAs and risk reports. Uploaded external signed documents are valid inputs;
  SPIN need not author every document.
- Distinguish uploaded time from capture/effective/review dates. Add nullable
  missing metadata additively; unknown legacy dates remain unknown.
- Link evidence to required statements/procedures and recorded system areas,
  preserving one artifact with multiple legitimate associations.
- Evaluate expiration and relevant source/configuration changes, not a universal
  30-day cutoff. Permit authorized documented continued-validity review where
  policy allows. Missing evidence remains a gap, not an AI-generated substitute.
- For backup: retain mission/BIA objectives, protected workloads, configuration,
  retention, exercise results and responsibility review as distinct records.
  Provider RPO/RTO targets must never become measured restoration results.
- Require actual approval references for privacy-office or external agreements;
  a local role assignment or checkbox cannot stand in for an external signature.

**Acceptance:** a missing/stale required plan blocks the applicable package;
approved non-applicability is retained. Valid existing artifacts can be reused
without losing source versions or manufacturing new approvals.

### Phase 5 — Make exports loss-aware and handoff explicit

**Change:** truthful, reversible field mapping and a verifiable manual handoff.

**Affected surfaces:** `EmassBridgeService`,
[EmassExportService](../../src/Ato.Copilot.Agents/Compliance/Services/EmassExportService.cs),
[EmassExchangeService](../../src/Ato.Copilot.Agents/Compliance/Services/EmassExchangeService.cs),
[EmassRoundTripSyncService](../../src/Ato.Copilot.Agents/Compliance/Services/EmassRoundTripSyncService.cs),
`EmassWorkflowStatusService`, existing exchange UI/endpoints.

- Export from the selected reviewed package snapshot, not mutable latest working
  narratives. Map approved system-specific/common/hybrid responsibility with
  provenance; unknown designation must remain unresolved.
- Validate control/enhancement/procedure/CCI identifiers against the pinned
  receiving mapping. Do not assume uppercase conversion proves equivalence.
- Preserve full narrative content. When the receiver's field is shorter, show a
  blocking conversion issue or an explicitly reviewed short statement plus a
  referenced full artifact. Never silently truncate essential implementation text.
- Replace “LIVE” transformation language with payload preparation; retain contract
  compatibility through an explicit deprecation/migration if needed.
- Generate a handoff index with artifact versions/hashes, receiving metadata,
  control/procedure associations, limitations and exact manual upload/import steps.
- Track Exported, Upload/Import Recorded, Submission Recorded, Returned and
  Decision Recorded with provenance. Manual observations remain labelled manual;
  local export freshness does not prove remote synchronization.
- Support return comments and reviewed correction/delta packages without rewriting
  the previously exported baseline.

**Acceptance:** a round-trip fixture preserves meaning and exposes every unsupported
or lossy conversion. No generated ZIP or payload is reported as a remote submission.

### Phase 6 — Extend the existing package UI and prove generated outputs

**Change:** improve the existing readiness workspace, not a new parallel dashboard.

**Affected surfaces:** `PackageReadinessExperience`,
[PackageGenerationDialog](../../src/Ato.Copilot.Dashboard/src/components/PackageGenerationDialog.tsx),
existing assessment/evidence/responsibility/POA&M editors,
[PackageBackgroundService](../../src/Ato.Copilot.Agents/Compliance/Services/PackageBackgroundService.cs),
[retained worker](../../src/Ato.Copilot.Agents/Compliance/Services/PackageBackgroundService.Retained.cs).

- Group blocking tasks by documentation outcome, with named owner, record,
  missing proof, source version and a direct resolution action.
- Provide a reviewer coverage view across procedure/result/evidence/finding/risk.
  Show exclusions and unverified mappings, not just aggregate percentages.
- Retain keyboard/mobile/read-only/loading/save-error behavior and entered edits.
  Announce success only after server confirmation.
- Include applicable supporting documents and traceability/index outputs in the
  package. Extend existing schema/output validation and source manifests.
- Test actual bytes: PDF/DOCX content, OSCAL structures/UUID links, Excel cell
  mappings, evidence files/hashes and ZIP manifest consistency. Schema-valid JSON
  alone is not sufficient.

**Acceptance:** a reviewer can navigate every required claim to its proof and
read the same selected reviewed state in each generated format.

### Phase 7 — Keep the documented package current; live collectors separately

**Change:** turn accepted source changes into owned documentation/reassessment
tasks while preserving the approved baseline.

**Affected surfaces:** existing narrative/design change impacts,
`PackageReadinessSources`, `ConMonService`,
[ScopedMonitoringService](../../src/Ato.Copilot.Agents/Compliance/Services/ScopedMonitoringService.cs).

- Stage impacts to controls, responsibilities, evidence, plans and package
  versions. Support periodic/manual assessment updates before live Azure ingestion.
- Verify actual coverage/connectivity permissions before enabling collectors;
  unsupported canonical scope remains explicit.
- A future collector slice must prove authorized scoped event -> observed change ->
  human disposition -> evidence -> reviewed documentation -> regenerated package.
- Never infer cATO or automatically issue/revoke an authorization decision.

**Acceptance:** changing the documented baseline invalidates affected preparation,
creates traceable follow-up and leaves accepted historical bytes unchanged.

## 4. Dependencies and release slices

1. **Contract gate:** Phase 0 precedes production changes.
2. **Canonical readiness:** Phase 1 establishes the evaluator contract and rule
   version used by all later phases.
3. **Parallel work after Phase 1:** procedure coverage (Phase 2), supporting
   artifact applicability (Phase 4), and loss-aware bridge mapping (Phase 5).
4. **Finding reconciliation:** Phase 3 follows the source-qualified result contract
   in Phase 2; existing POA&M field/workflow review can proceed earlier.
5. **Submission-package acceptance:** Phase 6 requires completed applicable
   readiness, assessment, risk, artifact and handoff checks from Phases 1-5.
6. **Lifecycle maintenance:** Phase 7 follows the source/artifact contracts;
   operational Azure collectors remain an independently verified follow-on.

Ship reviewable slices, not a single untestable rewrite. Each slice updates its
own spec/contracts/tests/docs and demonstrates its downstream contribution.

## 5. Data, compatibility and authority rules

- Reuse baseline catalog bindings, requirement evidence pins, assessment/import
  provenance, narrative history, evidence versions and package readiness runs.
  Any new entity must have a demonstrated ownership/lifecycle not represented
  by those stores.
- New persisted fields are additive and nullable for legacy records; old approvals
  remain retained. Unknown legacy procedure/date/approval metadata cannot be
  backfilled as verified. Use explicit reconciliation.
- Implement required schema additions with the repository's current EF/provider
  patterns after the data-model review; verify SQLite and SQL Server paths.
- Carry rule/catalog/mapping/artifact versions into manifests and stale checks.
  Never reinterpret an old Ready run under new rules.
- Enforce authenticated tenant/system/action access on reads, review, export,
  queue and worker reauthorization. Do not trust browser role labels or client
  supplied source/tenant/approval metadata.
- Share evaluation and DTOs across Dashboard/MCP and affected channels. New tools,
  if necessary, extend BaseTool; do not add channel-specific readiness rules.
- AI may draft wording or propose mappings; only explicit source facts are
  carried directly. Preserve user corrections, proposal origin and pinned sources.
  AI does not supply missing evidence, tests, authorization or external signatures.

## 6. Test-first acceptance and validation

For every slice: write failing synthetic tests first, run them red, implement,
run green and refactor without changing accepted behavior. Use AAA markers.
Meet the constitution's 100% modified-path coverage gate; do not lower thresholds
or suppress errors. Include boundary/null/empty/max-size and concurrency tests.

Required scenarios:

- Full initial package without a prior AO decision; retained archive/change
  purpose preserves its own correct selection and immutable baseline.
- Complete compliant assessment with legitimately empty POA&M.
- Noncompliance with complete risk response remains eligible where permitted;
  missing/orphaned findings, unresolved required procedures or unapproved N/A
  cannot be hidden by document status.
- Explicit, versioned mapping across requirement/control/procedure/CCI; unmapped
  legacy and changed catalogs remain unresolved.
- Provider scope without accepted allocation does not count as inheritance.
- Capture-date/freshness/configuration-change checks distinguish missing,
  expired, valid and reviewed-continued-validity evidence.
- Supporting plan applicability, external approval and exercise records.
- No lossy narrative/designation conversion or silent remote-success fallback.
- Source change between evaluation/queue/generation, evidence byte corruption,
  retry/idempotency, revoked permission and cross-tenant references.
- Keyboard focus, narrow layouts, read-only/loading/error states, failed saves
  retaining edits, and every UI resolution link.
- Actual generated content and cross-artifact references; a deliberate mismatch
  fails even when individual OSCAL documents are schema-valid.

### Commands and expected outcomes

Start with selectors covering the changed paths; existing suites to extend include
`InitialPackagePurposeTests`, `RetainedPackageTests`, `SystemDesignExportTests`,
`EmassBridgeServiceTests` and the package readiness dashboard tests.

```bash
dotnet test tests/Ato.Copilot.Tests.Unit --filter \
  'FullyQualifiedName~InitialPackagePurposeTests|FullyQualifiedName~RetainedPackageTests|FullyQualifiedName~EmassBridgeServiceTests'
dotnet test tests/Ato.Copilot.Tests.Integration --filter \
  'FullyQualifiedName~Package'

cd src/Ato.Copilot.Dashboard
npm test -- --run src/__tests__/pages/PackageReadinessExperience.test.tsx \
  src/__tests__/api/packageReadiness.test.ts
npx tsc --noEmit
npm run build
```

Before release, run the required full solution gates from the repository root:

```bash
dotnet build Ato.Copilot.sln
dotnet test Ato.Copilot.sln
```

Expected: builds/type checks pass, relevant and regression tests pass, required
coverage is measured, and no new warnings/errors are hidden. Run existing browser
tests plus new synthetic package-flow scenarios. Any touched VS Code/M365 project
also runs its compile/build parity checks. Resolve applicable lint configuration
issues rather than claiming lint passed.

**Manual acceptance:** the user must inspect and test each slice locally before
acceptance. Final acceptance uses an authorized sanitized representative system
and an approved receiving-instance/manual-import test. No automated test may
consume real PII/CUI/classified or production data. No remote submission, push,
provider publication or GitHub write occurs without approval.

## 7. Decisions required before implementation

- Target eMASS instance, supported handoff formats and authoritative field/CCI map.
- Package purposes and Component-specific required plans/approval chain.
- Evidence freshness and authorized continued-validity/exception criteria.
- Whether an RAR is required and how its approved risk records are sourced.
- Manual handoff as the first delivery; any direct API transport is a separate
  approved project with certificates, credentials, authority and receiving tests.
- Backward compatibility and rollout for stricter readiness and old exported data.

These are approval checkpoints, not reasons to invent a default policy.
No reliable calendar estimate is given before the procedure/CCI and artifact
contracts are reconciled. The dependency order above is the implementation order.
