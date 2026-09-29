# ATO Package Readiness experience

Date: 2026-09-29. Scope: Feature 079 US1/US4; existing issue owners
[1042](https://github.com/azurenoops/spin_agent/issues/1042),
[1043](https://github.com/azurenoops/spin_agent/issues/1043) and
[1046](https://github.com/azurenoops/spin_agent/issues/1046), children of
[1038](https://github.com/azurenoops/spin_agent/issues/1038).
Related purpose/output/schema work: #1039, #1040, #1041, #764.

The approved attachment is the task-oriented **Your path to ATO submission**
screen with an optional check drawer. Its counts, dates, owners and stale panel
are illustrative. This contract precedes implementation; unverified items below
are not completion claims.

## Inspected current path

| Connection | Current implementation / gap |
|---|---|
| System records -> validation | `PackageValidationService` reads provider provenance, approved profile snapshots, boundaries, SSP sections, SAP/SAR, POA&M/control references, schema results and evidence. Findings are mostly failure-only. |
| Purpose | Documents URL/default is `Legacy`; explicit InitialSubmission omits the AO prerequisite. Archive/change validation delegates to `AuthorizationPackageService.ValidateRetainedPackageAsync` and exact retained context. |
| Working records -> preview | Working SSP projection includes saved profile rows and is explicitly preview-only. Approved generation uses retained approval snapshots and provider/narrative lineage instead. |
| Validation -> standalone history | Current standalone POST result is not retained. Existing `PackageValidationResult` is persisted only when attached to an authorization package. |
| Validation -> initial export | Enqueue revalidates and stores the linked result, but does not pin a whole-system current-record snapshot. |
| Worker -> ZIP | Generates actual SSP/POA&M/assessment-results/SAP, SAR and evidence. Current worker can report schema failure and still continue to Completed; this is an in-scope integrity defect. |
| Retained archive/change export | Existing exact baseline bytes, decision snapshot, optional reviewed SSP change and hashes are verified; retain these protections. |
| Export -> submission | No automatic submission connector is established. `EmassExchangeRecord` stores explicitly entered human observations tied to package ID/hash; workbook reconciliation is a separate process. |
| Submission -> decision | No automatic authorization inference. Existing decision workflow records/ issues decisions separately; external decisions bind retained evidence and package hashes. |
| RMF phase/history | Existing `SystemDetailResponse` contains recorded phase and audit-derived transitions. Package milestones must not mutate or replace these. |
| Catalog -> readiness | Catalog presence/counts are not an authoritative readiness calculation. Existing long availability sections will be replaced by a compact supporting-record summary. |

Known source distinctions: component placements are not the detailed HW/SW
inventory; a baseline-backed CRM is not confirmed inheritance; approved profile
snapshots are distinct from drafts; narrative counts are not implementation
evidence. A source without a verified web editor must expose a truthful
view/explanation and supported workflow, not a fabricated edit action.

## Purpose and navigation

- Use the exact title **Your path to ATO submission** and description
  **See what is documented, what needs review, and what to do next.**
- Keep SPIN/workspace/system context and Readiness, Document previews, Export
  packages, eMASS reconciliation and Recorded decisions navigation.
- No query purpose means the existing `Legacy` selection. Recognize all four
  supported purposes; invalid values are errors, not silent fallback.
- Changing the displayed purpose requires an explicit user choice/confirmation.
  Do not change purpose merely because the mock shows InitialSubmission.
- Preserve purpose and retained selection/run identity through drawer/history,
  previews, export handoff and same-system return navigation. A different-purpose
  result must never be displayed as current for the selected purpose.
- Source links use existing authoritative workflows. An allowlisted same-system
  return target restores the readiness URL/filter/check without cross-tenant,
  cross-system or external redirects.

## Authoritative checks and retained runs

Reuse `IPackageValidationService` and retained-context validation; do not invent
browser readiness rules. Add explicit outcomes for checks actually evaluated:
passed, blocking, follow-up, not-applicable and unavailable/unevaluated, with
applicability and required/optional semantics. Required unable-to-verify checks
prevent a ready result. Counts are computed from the same returned check set.

Each check supplies a stable rule/check identity, readable title and gap,
purpose-specific rationale, concrete next steps, category/severity/outcome,
source record references or missing-data explanation, evaluated revisions/hashes,
rule version and recorded ownership when available. An expected workflow role
is not a fabricated named owner; absent ownership is **Not recorded**.
Current server-authorized view/fix actions are projected per caller, not restored
from an old permissions snapshot or inferred from displayed role text.

Persist standalone evaluations in an additive tenant/system-scoped readiness-run
record: purpose, retained selection context, evaluated source fingerprint,
record references/revisions, checks/counts, evaluation time/actor, outcome and
rule version. Runs are immutable. Historical failed/stale runs remain available.
Do not create fake packages to satisfy the existing validation-result package FK.

For current-record purposes, capture deterministic relevant source identity
before and after validation. Changes during evaluation produce an explicitly
non-current result, not a claimed coherent ready snapshot. Include the records
actually used by validation/output and meaningful revisions, not page visits.
For retained purposes, reuse the existing pinned context/hash and source-byte
verification; unrelated current drafts do not rewrite the retained archive.

GET/read operations expose latest/history/detail and current freshness without
creating an evaluation. Validation is an explicit authorized command. API changes
are additive; existing purpose/validation envelopes and callers stay compatible.
Collections/history are bounded/paginated. Requests honor cancellation.

### Additive HTTP/TypeScript contract (September 29, first bounded turn)

**Status: contract and adapter only; new server endpoints are not implemented in
this turn.** The exact exported DTO definitions are in
[`packageReadiness.ts`](../../../src/Ato.Copilot.Dashboard/src/api/packageReadiness.ts).
They reuse `PackagePurpose` and `RetainedPackageSelection` from the existing
package adapter. Existing validation/package callers and envelopes remain valid.

All new read endpoints are below
`/api/dashboard/systems/{systemId}/package-readiness`. Responses are plain
camel-case JSON, not `{data: ...}` envelopes. Every request explicitly carries
`purpose`; the adapter's omitted selection defaults only to `Legacy`.
GET retained selection parameters use the existing field names, flattened in the
query: `baselinePackageId`, `baselineContentHash`, `authorizationDecisionId`,
`changePreviewId`, `changeContentHash`, `expectedDecisionSnapshotHash`,
`expectedSourceContextHash`. POST carries these under `retainedContext`.
Current-record purposes reject retained selection; retained purposes require
baseline ID/hash and decision ID; ChangeSubmission additionally requires the
preview ID/hash; archives reject change fields. Invalid purpose is a 400.

| Method/path | Export | Plain response |
|---|---|---|
| GET root | `getPackageReadinessWorkspace(systemId, selection?, signal?)` | `PackageReadinessWorkspace` |
| GET `/runs/latest` | `getLatestPackageReadinessRun(systemId, selection?, signal?)` | `{systemId,purpose,retainedContext,selectionHash,latestRun}`; `latestRun:null` means never checked |
| GET `/runs?limit=20&offset=0` | `listPackageReadinessRuns(systemId, selection?, page?, signal?)` | scoped `{items,totalCount,limit,offset}` of run summaries |
| GET `/runs/{runId}?limit=50&offset=0&outcome=Blocking` | `getPackageReadinessRun(systemId, runId, selection?, page?, signal?)` | scoped `{run,checks:{items,totalCount,limit,offset}}`; outcome is optional |
| GET `/runs/{runId}/checks/{checkId}` | `getPackageReadinessCheck(systemId,runId,checkId,selection?,signal?)` | scoped `{runId,check}` |
| POST `/runs` | `validatePackageReadiness(systemId, selection?, signal?)` | HTTP 201 scoped `{run,checks}` first page, including blocked/failed evaluations |
| POST existing `/api/v1/systems/{systemId}/packages` | `generatePackageFromReadiness(systemId, request, signal?)` | HTTP 202 `{systemId,purpose,packageId,status,message,readinessRunId,sourceHash}` |

Scope fields are always `{systemId,purpose,retainedContext,selectionHash}`.
`retainedContext` is the selected pins or null, not the resolved internal
manifest. `selectionHash` hashes purpose and selected baseline/decision/change
IDs and baseline/change hashes; expected snapshot/source hashes are concurrency
preconditions, not selection identity. History and run/check lookup must enforce
tenant + system + purpose + exact selection. Unselected or differently scoped
runs must not leak through lookup or latest. History defaults to 20 (maximum
100); checks default to 50 (maximum 200), zero-based offsets. All-check counts
remain whole-run counts even when the detail page is filtered/paginated.

`PackageReadinessRun` fields: `id`, `outcome`, `startedAt`, `evaluatedAt`,
`evaluatedBy`, `sourceHash`, `sourceHashAfter`, `ruleVersion`, `counts`,
`recommendedCheckId`, `failure`, `freshness`.

- Stored `outcome`: `Ready | Blocked | Failed | SourceChanged`.
- `counts`: mutually exclusive `passed`, `blocking`, `followUp`,
  `notApplicable`, `unavailable`, plus `total` and `requiredUnavailable`.
  `total` equals the sum of the five outcome buckets. `requiredUnavailable`
  is a subset of `unavailable`, not an additional outcome bucket.
- `freshness`: `{state:Current|Stale|Unavailable,checkedAt,currentSourceHash,reason}`.
  This is a current read projection, not a mutation of the stored run. Rule
  version changes and time-dependent validity changes must also invalidate a
  formerly ready run; byte/source hashes alone cannot extend an expired decision.
- `failure`: null or `{code,message}`. Failure to read/evaluate sources is never
  represented as an empty Ready run. If source capture fails, hashes may be null;
  a Ready run always has matching non-null before/after identities.
- `recommendedCheckId` identifies an actual stored check, prioritizing applicable
  blockers/required unavailable checks before follow-ups. It is null when no
  unresolved evaluated check exists; the UI fetches its check detail if off-page.

`PackageReadinessCheck` fields: `id`, `ruleId`, `title`, `outcome`, `category`,
`required`, `applicability`, `why`, `missingSource`, `sources`, `nextSteps`,
`expectedRole`, `recordedOwner`, `action`.

- `outcome`: `Passed | Blocking | FollowUp | NotApplicable | Unavailable`.
- `applicability`: `Applicable | NotApplicable | Undetermined`.
  Conditional privacy uses actual PTA determination; absent determination is
  unavailable/undetermined, not a passed or universal PIA requirement.
- `sources`: `{kind,recordId,revision,contentHash,label}`; nullable revision/hash
  means genuinely unrecorded, never fabricated. IDs combine rule identity and
  applicable source identity, not message text or timestamps.
- `recordedOwner`: null (display **Not recorded**) or
  `{personId,displayName,role,assignmentId,scope}` from persisted ownership.
  `expectedRole` alone never populates a named owner.
- `action`: `{canView,canEdit,path,label,reason}`. `path` is null or an allowlisted
  same-system relative route; no arbitrary external/cross-system redirect.
  `label` is `Open` or null. Unsupported editor or denied access has an explicit
  reason and no edit authority. Historical checks retain their evaluated facts
  but receive freshly resolved current caller actions. Role names use canonical
  persisted naming, not browser persona. GET cannot acquire an edit lock.

Workspace fields: scope plus `source:{state:Available|Unavailable,hash,ruleVersion,
reason}`, `latestRun`, `permissions:{canValidate,validateReason,canGenerate,
generateReason}`, `progress`, `documents`, `rmf`.
`latestRun` is a run summary or null, not an automatic evaluation.
`progress` contains exactly five entries (`prepare`, `validate`, `export`,
`emass`, `decision`), each `{id,state,description,records,totalCount,action}`;
state is `NotChecked | Ready | Blocked | Stale | Failed | Recorded | NotRecorded |
Unavailable`. Up to five recent actual records per entry retain
`{kind,id,status,recordedAt,purpose,sourceHash,sourceRelationship,action}`.
`sourceRelationship` is `CurrentSource | Historical | Unknown`: unknown lineage
must never be displayed as current. Export records use stored package lifecycle,
eMASS records use human-observed exchange outcomes and package/hash linkage,
decisions retain positive/negative/expired facts. `Recorded` is not success.
No automatic connector or sequential milestone completion is inferred.
`documents` contains bounded category summaries
`{kind,title,presence,status,reviewState,sourceState,validationOutcome,recordCount,
records,action}`. Nullable states mean not established. Presence (`Present |
Missing | Unavailable`) never establishes approval or validation.
`rmf:{phase,transitions,totalCount}` contains at most five recorded transitions
`{id,fromPhase,toPhase,occurredAt,actor}` independent of package milestones.

Generation request fields are `purpose`, `retainedContext?`, `readinessRunId`,
`expectedSourceHash`, `evidenceMode` (`Embedded|ManifestOnly`) and
`includeEvidence:true`. The existing endpoint adds these readiness fields without
removing legacy requests. Server admission and worker completion require a
current Ready run with matching system, purpose, selection, rule version and
source hash. Old callers must undergo equivalent fresh internal evaluation and
pinning, not bypass integrity. Stale/mismatched runs return HTTP 409
`READINESS_SOURCE_CHANGED` or `READINESS_CONTEXT_MISMATCH`; blocked/failed runs
return 409 `READINESS_NOT_READY`. Authorization denial is 403; inaccessible
resources are 404. Structured errors retain `error`, `errorCode`, `suggestion`.
The worker must validate the exact emitted bytes and fail, not Complete, on
invalid/missing required artifacts or source drift. Package/run/source linkage
is additive metadata; historical downloads are not invalidated by later edits.
For new outputs, `package-metadata.json` preserves its existing `packageId`,
`systemId`, `purpose`, `receivingWorkflowOutcome` and adds
`readiness:{schemaVersion:1,runId,selectionHash,sourceHash,ruleVersion,evaluatedAt}`.
Retained outputs gain this metadata entry without rewriting their exact
`package-context.json` or retained predecessor bytes. Validate metadata shape,
scope and linkage as well as each emitted artifact; never update old archives
in place. Persistence adds readiness-run/source linkage without changing the
existing required package-validation FK.

The new adapter fails closed on scope, receipt, count or action-path mismatch,
propagates HTTP errors/cancellation and never falls back to legacy validation.
The second implementation turn owns backend schema/DI/tests and generation guards.

### UI state machine

| State | Required presentation |
|---|---|
| Not checked | No ready claim/count fabricated from absence; Check readiness. |
| Checking | Busy status, guarded duplicate actions, prior result not mislabeled current. |
| Current | Latest matching-purpose result, blocking/follow-up counts and evaluated snapshot/time. |
| Stale | Source changed; retained result is historical, recheck required. Show stale panel only here. |
| Failed | Explicit failed evaluation and diagnostic/retry; not an empty passing checklist. |
| Unavailable/denied | Explain inaccessible state; do not convert errors/unknowns to zeros. |

## Prioritized work and drawer

- One recommended next task, then Blocking checks / Follow-up / All checks.
- Server prioritization: applicable blockers (including required unavailable
  checks) before follow-ups, with deterministic rule/source ordering. Prefer an
  authorized fix action; otherwise show a permitted view or responsibility
  explanation, never an unauthorized edit button.
- All checks includes passed and conditional/not-applicable results rather than
  synthesizing success from the absence of findings.
- Drawer state is URL-backed with a retained run/check identity. Back/forward,
  direct links, Escape, focus restoration, narrow screens and dark mode work.
- The drawer explains outcome, requirement, why it matters, evaluated source,
  missing information, next steps, recorded owner, time/revisions/rule details
  and the existing resolution workflow.
- Saving a source form does not locally resolve the finding. On return, refresh
  freshness; revalidation determines the new outcome. Old results remain inspectable.

## Supporting records and conditionality

Summarize existing SSP, SAP, SAR, POA&M, responsibility, privacy, inventory and
other applicable source records. Keep presence, draft/finalized state, review,
freshness and validation outcome as different attributes.

Privacy applicability must use the persisted PTA/PIA workflow; missing
determination is not an invented universal PIA requirement. Open POA&M items
are not inherently a prohibition on submission. Responsibility availability is
not acceptance of inheritance. Inventory presence is not proof of CM-8 compliance.
Do not turn unsupported/unavailable evaluation into a passing check.

Link existing working previews, retained exports and source workflows. Working
draft preview content must not be represented as the approved export baseline.
Do not add parallel artifact editors, authorization forms or synthetic documents.

## Package progress, not RMF phases

1. **Prepare package**: grounded in authoritative evaluated requirements, not a
   completion percentage or a count of available files.
2. **Validate package**: actual run outcome and freshness for the chosen purpose.
3. **Export**: actual queued/completed/failed retained package/artifact records,
   with purpose and source identity where recorded. Historical/unknown lineage
   is labeled, not assumed current.
4. **eMASS submission**: only actual recorded exchange/receipt/import evidence
   linked to the appropriate export; label human-recorded observations and the
   lack of a live connector. Download alone never completes this milestone.
5. **AO decision**: actual same-system retained decision/history, including
   negative/expired decisions. A receipt/import observation never creates it.

Show recorded RMF phase/history separately. A later source change may require
another validation/export while prior submissions/decisions stay recorded.
InitialSubmission never requires or fabricates a pre-existing AO decision;
other purposes retain their stricter/pinned decision rules.

## Export integrity

An action relying on readiness must verify matching purpose, system, selected
context and current evaluated source identity on the server. A stale ready
result is insufficient. Guard current-record generation at enqueue and worker
boundaries; retain run/source references with its output. Validate the actual
emitted artifacts, not a different regenerated document. Schema failure or
missing required evidence cannot finish as a successful package.

Preserve historical downloads and source provenance, subject to existing record,
evidence and retention authorization. Generation/export does not create a
submission observation or AO decision.

## Acceptance and delivery

Synthetic tests cover all purposes; InitialSubmission without a decision;
not-run/failed/current/stale/denied; required unavailable and conditional checks;
counts and real source actions; source changes and concurrent edits; tenant/system
isolation; export without submission; submission without authorization; source
changes reflected in working versus approved outputs and retained artifacts.

Run solution build/test, Dashboard type checking, targeted frontend/backend and
browser checks. Record baseline failures separately. Supply the local readiness
URL and manual sequence: select purpose -> validate -> inspect gap -> source ->
save/review -> return -> revalidate -> preview -> export status. No external
publication, issue closure, push or live eMASS acceptance is implied.
