# GitHub issue preview — Feature 079

**Status: local review only. No GitHub mutation has been performed.**

Canonical repository: `azurenoops/spin_agent`. The checkout's
`azurenoops/ato-copilot` remote redirects to that repository, verified through
the GitHub API. Source baseline: `13204325`; issue reads: September 26, 2026.
Concurrent local documentation changes are not part of that baseline.

This file previews a new umbrella issue, two narrowly scoped child issues,
additive comments, and actual parent/sub-issue changes. It does not authorize
posting, replacing issue bodies, closing issues, committing, or pushing.

## 1. Posting and substitution rules

- `UMBRELLA_ISSUE` means the number assigned to the new umbrella below.
- `SLICE_ISSUE` means the new acceptance-slice child of #1023.
- `SERVICE_MODELS_ISSUE` means the new limited-service-model child of #1002.
- These tokens are deliberately not fabricated issue numbers. First preview
  and obtain approval for the token-free umbrella creation payload. After its
  number exists, substitute it into the child creation payloads, preview them,
  and obtain approval. After child creation, resolve their numbers in comments
  and relationship operations and obtain approval for those exact writes.
  Never post unresolved tokens or assume permission for later stages.
- The body under each **Exact proposed body** and each comment block is the
  proposed payload; the surrounding review notes are not part of it.
- Preserve existing titles, bodies, labels, assignees, milestones, children,
  comments and open/closed states. No existing issue body replacement is
  proposed. New issues need no inferred assignee, milestone or label.
- Recheck parents, children, issue states and duplicate-search results
  immediately before an approved write. If they changed, reconcile and
  preview the changed proposal rather than force-reparenting.
- Verify publication of the specification and design assets before adding
  remote document links. Repository paths below identify local design
  requirements, not a claim that uncommitted files are already on GitHub.

## 2. Verified hierarchy and exact proposed changes

GitHub GraphQL `parent` and `subIssues(first: 100)` were read, not inferred
from prose saying “Parent.” All three workstream lists reported
`hasNextPage: false`. All issues in these lists were open at the read.

| Workstream | Current actual parent | Proposed actual parent |
|---|---|---|
| #1002 — distinct CSP and Mission Owner workspaces | None | `UMBRELLA_ISSUE` |
| #1023 — CSP-backed and independent ATO workflows | None | `UMBRELLA_ISSUE` |
| #1038 — Systems ATO submission and ConMon | None | `UMBRELLA_ISSUE` |

**Keep every current #1002 child under #1002:**

- #1015 — existing-login workspace entry
- #1016 — workspace context across navigation
- #1017 — scoped RMF permissions
- #1018 — provider changes and customer responsibility review
- #1019 — scoped Narrative Library
- #1025 — shared workspace navigation
- #1026 — provider capability catalog and source-package views
- #1027 — provider capability authoring
- #1028 — revision-safe review/publication
- #1029 — provider Organizations list
- #1030 — provider-scoped organization detail
- #1031 — organization creation and membership handoff
- #1032 — audited support entry
- #1033 — organization capability/component library
- #1034 — organization capability detail and narrative review
- #1035 — resumable organization capability setup
- #1037 — system capability applicability and responsibility review

**Keep every current #1038 child under #1038:**

- #1039 — initial-submission package purpose
- #1040 — source-backed leveraged authorization export
- #1041 — approved-profile-to-artifact mapping
- #1042 — purpose-specific submission readiness
- #1043 — unknown validation and failed-load states
- #1044 — executable rules and reviewed impact
- #1045 — scoped Azure attribution and coverage health
- #1046 — task-oriented Systems experience

**Exact additional parent/sub-issue operations, after approved creation:**

1. Create `UMBRELLA_ISSUE` without a parent.
2. Attach #1002, #1023 and #1038 as its three actual sub-issues.
3. Create `SLICE_ISSUE`; attach it only to #1023, which currently has no
   children. It owns the integrated acceptance slice, not the component
   fixes already assigned to other stories.
4. Create `SERVICE_MODELS_ISSUE`; attach it only to #1002.
5. Do not move #1018, #1025–#1028, #1030 or #1037 to #1023.
   Do not move #1039–#1046 to #1023 or directly under the umbrella.
6. Record cross-workstream dependencies in the bodies/comments below.
   A dependency is not an additional parent.
7. Read back the hierarchy and confirm #1002 retains all 17 existing children
   plus the one new child; #1038 retains all eight; #1023 has the one new child;
   the umbrella has exactly the three workstreams.

No relationship changes are proposed for #957, #1001, #1021, #969, #970,
#764, #647, #669, #980, #754, #676, #998 or #999. Those are referenced
dependencies, not newly owned work. Closed historical issues remain closed.

## 3. Duplicate search and story ownership

Read-only searches used `gh issue list -R azurenoops/spin_agent --state all
--search ... --limit 100`. Searches included:

| Search text | Relevant result/disposition |
|---|---|
| `"079"` | No result. |
| `"provider" "system" "consolidation"` | #1037, #1033, #1035 and historical #690; reuse current UI owners, do not create a second catalog/setup engine. |
| `"CSP" "workflow"` | #1023 and #957 among results; #1023 remains the slice feature owner. |
| `"handoff"` | #957 plus unrelated identity handoffs and historical #224. |
| `CSP Mission Owner export` | Historical #84/#422; not a dedicated acceptance-slice child. |
| `"provider" "offering"` | #1026, #1023, #1033, #1037 and historical #84. |
| `"1023"` | #1023, #1026, #1033 and #1030. |
| `"service model"` / `service model SaaS` | No result. |
| `"SaaS"` | #636, a broad strategic audit, not the service-model implementation story. |
| `"retirement"` | #1010, Narrative Library retirement; not provider/System route retirement. |
| `workflow retirement migration rollback` | No result. |

Search is evidence against obvious duplicates, not proof that none can exist.
The relevant #957, #223, #224 and #636 bodies were read. #957 owns the
responsibility reconciliation seam, not complete publication-to-generated-
artifact acceptance. Closed #223/#224 describe an older trust-chain approach;
do not reopen them or reintroduce their historical org-copy/default workflow.
#84 was identified as a closed AWS/GCP connector feature; this proposal does
not reopen it or claim those connectors work.

### Ownership binding for the umbrella specification

The concurrently authored `spec.md` defines US1–US6. Bind each to owning
story issues, not merely a feature/epic:

| Specification story | Owning story issue(s) | Feature/workstream |
|---|---|---|
| US1 — truthful initial package | #1039, #1040, #1041, #1042, #1043 | #1038 |
| US2 — complete CSP-to-mission document workflow | Proposed `SLICE_ISSUE`; integration owner, with existing seam owners retained | #1023 |
| US3 — mock-defined provider workspace | #1025, #1026, #1027, #1028, #1030; existing #1018/#1037 provide review/handoff | #1002 |
| US4 — mock-defined Systems workspace | #1046; #1037 owns capability/responsibility integration | #1038; #1037 stays under #1002 |
| US5 — scoped change impacts | #1045 then #1044; #1018 remains the provider-review dependency | #1038; #1018 stays under #1002 |
| US6 — honest additional service relationships | Proposed `SERVICE_MODELS_ISSUE` | #1002 |

For US2, #1023 is the feature owner, not a substitute for a story-level link:
the proposed acceptance child supplies that link. Keep the broader semantic
ownership below when a story spans several existing implementation seams.

| User-story outcome | Owning story issue | Supporting work; no reparenting |
|---|---|---|
| Prove the first CSP → Mission Owner → generated document/export slice, with an independent-system comparison | Proposed `SLICE_ISSUE` under #1023 | #1018, #1028, #1030, #1037, #1039–#1043 |
| Consolidate provider navigation and retire duplicate provider entry points safely | #1025 | #1015–#1017, #1029–#1032 |
| Review offering/source-backed catalog data and incomplete source analysis | #1026 | #1027, #1028, #1040 |
| Author reusable implementation and explicit duties without modifying the released source | #1027 | #1021, #1019 |
| Review/publish exact releases and explain customer impact | #1028 | #1018, #957 |
| Inspect the provider's scoped mission/customer relationship and actual service allocation | #1030 | #1029, #1037 |
| Apply a release and review system responsibilities/evidence explicitly | #1037 | #957, #1018, #1033–#1035 |
| Preserve reviewed profile values in generated artifacts | #1041 | #969, #1040 |
| Prepare purpose-correct initial/archived/change packages | #1039 | #1040, #1042, #1043 |
| Use faithful provider authorization metadata in OSCAL | #1040 | #970, #764 |
| Show one purpose-specific readiness result and truthful unknown states | #1042 | #1043 |
| Surface failed checks/loads and recover without false success | #1043 | #1042 |
| Consolidate Systems pages, package/reconciliation states and legacy-route retirement | #1046 | #1025, #1037, #1039–#1043 |
| Attribute monitoring to reviewed system/provider scope and show collection health | #1045 | #1044 |
| Evaluate configured rules and stage accountable impact/document review | #1044 | #1045, #1018 |
| Support limited SaaS/manual service relationships without new connectors | Proposed `SERVICE_MODELS_ISSUE` under #1002 | #1026, #1030, #1037, #1040 |

TDD, production-host authorization/tenant tests, actual generated-artifact
inspection, desktop/mobile design fidelity and local manual review are
acceptance obligations on these owners, not a duplicate “test everything”
implementation epic. Provider retirement belongs to #1025; Systems retirement
belongs to #1046. No additional retirement story is proposed.

## 4. New umbrella issue

**Exact proposed title**

> [Feature 079] Consolidate provider and Systems workflows around reviewed ATO artifacts

**Exact proposed body**

```markdown
## Outcome

Deliver one coherent, source-backed provider-to-mission workflow for preparing
a fully documented system package and maintaining its reviewed baseline.
This is a coordinated refactor and completion of existing services and UI,
not a wholesale backend rewrite or a second canonical data model.

Specification: `specs/079-provider-system-workflow-consolidation/spec.md`.

## Workstreams and ownership

- #1002 owns provider/workspace consolidation and keeps every existing child.
- #1023 owns the first complete CSP → Mission Owner → document/export slice,
  including the independent-system comparison.
- #1038 owns Systems/document/readiness/monitoring consolidation and keeps
  #1039–#1046 as its children.

Attach those three issues as actual sub-issues of this feature. Cross-stream
dependencies do not move their stories or transfer their acceptance criteria.
Do not close an existing issue merely because the umbrella is implemented.

## Required UI contract

Production UI MUST follow `docs/design/provider-workspace-mock/` and
`docs/design/system-overview-mock/`: their navigation, information hierarchy,
task flow, progressive disclosure, status presentation and responsive layouts.
They are required implementation targets, not optional inspiration.

Preserve real SPIN branding and authenticated workspace navigation. Prototype
records, counts, permissions and success dialogs are synthetic and MUST NOT
be shipped as real behavior. Implement real persisted operations and honest
unavailable/restricted/empty/loading/stale/error states.

Provider navigation is Overview, Offerings, Mission systems, Changes and
Administration. Offering views keep sources/authorizations, services/scope,
implementations/duties, evidence/findings and changes together. Systems retains
Overview, System definition, Controls & evidence, Assessment & risk,
ATO package & eMASS, Continuous monitoring, Team and History.

Where older issue mocks conflict with these two suites, these suites govern
the consolidated layout. Preserve older issues' domain, permission, retry,
concurrency, accessibility and compatibility requirements. Record and review
any unavoidable design deviation before implementation.

## Ordered delivery

1. FIRST: complete and verify one synthetic Azure provider source → review →
   explicit canonical release publication → scoped mission association →
   explicit release adoption → authorized responsibility review → permitted
   evidence/narratives → actual generated document/export workflow. #1023
   owns the slice; #1028/#1030/#1037/#1039–#1043 supply their existing seams.
   Include a system that uses no CSP baseline. Deliver the mock-aligned UI
   needed by this slice, not an API-only demonstration.
2. Consolidate the remaining provider offering/source/review/publication,
   evidence/finding, mission relationship and change workflows under #1002.
3. Consolidate the remaining Systems journey under #1038/#1046, preserving
   specialist records/actions and verified document/readiness behavior.
4. Complete scoped monitoring attribution/health (#1045), then executable
   rules and reviewed change-to-document impact (#1044).
5. Add limited SaaS/manual service models without new cloud connectors.

## State and provenance contract

Show and persist distinct states for source analysis/review, provider
publication, service association, capability adoption, customer responsibility
review, document approval, package preparation, export generation, actual
transfer/receipt, import/reconciliation acceptance and recorded AO decision.
Not recorded, unavailable, failed and not applicable must remain distinguishable.

No earlier state proves a later one. Publication is not authorization;
association is not adoption; adoption is not responsibility acceptance;
export is not receipt or accepted import; none is a mission-system ATO.
Provider notification delivery is not customer acknowledgement or review.

Retain source identity, reviewed version, scope, authorization title/type/
issuer/date, citations, permitted evidence and responsibility provenance.
Never substitute export time for an authorization date, infer FedRAMP from a
provider name, or invent missing authority/coverage. Approved baselines,
past releases and exported packages survive draft edits and later changes.

## Reuse and bounded scope

Reuse the existing offering/authorization, source analysis and review,
canonical release, hosting/mission association, responsibility, evidence/
finding, impact, Systems, document and export services. Reverify their current
paths and extend only the missing contracts. Do not introduce parallel
publication, inheritance, remediation, readiness or authorization engines.

SaaS/manual relationships distinguish provider identity, service model,
management arrangement, source decision category and service scope. They
must not require fabricated Azure identifiers or imply live monitoring.
No new AWS/GCP/OCI/M365 or other cloud connectors are included.

Monitoring must attribute changes to reviewed resource/boundary scope and
shared-provider dependencies, explain affected controls/documents and route
review. Rules need source, scope, condition, cadence, severity, owner,
baseline and response. Missing/stale telemetry is not healthy “zero alerts.”
Stage updates without changing the approved baseline or making AO decisions.

## Acceptance and retirement gates

- [ ] #1023's first slice works through production-host HTTP/auth/tenant
  middleware and real persisted services, not only service mocks.
- [ ] Failing-first tests cover authorized/denied actions, foreign tenant/
  system/source/artifact IDs, revoked access, stale revisions, retry,
  concurrent requests and partial failures without duplicate writes/leaks.
- [ ] Inspect actual generated SSP/OSCAL/CRM/control exports and the selected
  package format as applicable; distinctive approved fixture values, source
  dates, duties and versions agree with previews and readiness results.
- [ ] Independent systems can prepare an initial package without a provider
  subscription or fictitious prior AO decision.
- [ ] Desktop/mobile implementation is reviewed against both mock suites,
  including keyboard focus, accessible status, clipped/overflowing actions,
  error/restricted states and the complete first-slice interaction.
- [ ] Every legacy provider/System destination has a keep/group/redirect/
  retire disposition, dependency inventory, replacement, migration/backfill,
  compatibility and rollback plan. Prove old links and retained records/
  versions/permissions still work before retiring duplicate workflows.
- [ ] Rehearse migration and rollback with synthetic existing data. Preserve
  source identities, customer-approved snapshots, historical artifacts and
  operational specialist actions. Do not delete old workflows prematurely.
- [ ] Each owner supplies runnable local setup, exact commands, synthetic
  fixtures, expected outcomes, artifact inspection and migration/rollback
  steps; the user has an opportunity to test before closure.
- [ ] Record performed checks and unresolved limitations separately.
  Local exports and simulated receipts are not live eMASS acceptance.

## External validation boundary

Actual eMASS transfer, receipt, import acceptance and an AO decision require
their own recorded evidence from the authorized receiving workflow. The
September 26 source audits and HTML prototypes establish no live Azure or
eMASS validation. Keep unperformed external checks explicit.
```

## 5. New child owned by #1023

**Exact proposed title**

> [079 / #1023] Verify the first provider-to-mission generated-artifact slice

**Exact proposed body**

```markdown
## Ownership and nonduplication

Parent: #1023, under umbrella #UMBRELLA_ISSUE.

Own the integrated acceptance fixture, production-host workflow and artifact
comparison for the first delivered slice. Do not reimplement the publication
(#1028), relationship (#1030), adoption/responsibility (#1037/#957), package
purpose (#1039), authorization metadata (#1040), profile mapping (#1041),
readiness (#1042) or failure-state (#1043) contracts.

Existing #957 covers responsibility reconciliation, not the entire
publication-to-export proof. Closed #223/#224 are historical foundations,
not instructions to copy provider records into a new parallel workflow.

## Required slice

Use synthetic reviewed source material with distinctive authorization
metadata, scope, implementation, customer duties and permitted evidence.
Follow the required `docs/design/provider-workspace-mock/` and
`docs/design/system-overview-mock/` UI through explicit source review,
canonical release publication, scoped service association, exact-release
adoption, authorized responsibility review, document preview and actual
generated artifacts. Repeat initial-package preparation with an independent
system that has no CSP subscription.

## Acceptance

- [ ] Write failing-first tests before implementation; exercise the production
  host's authentication, authorization and tenant-resolution pipeline.
  A development auth bypass or mocked service success is not sufficient.
- [ ] Provider users cannot approve customer duties or inspect private
  customer artifacts merely because they published the source. Ordinary
  Mission Owner access does not imply ISSM/ISSO review or AO authority.
- [ ] Exercise two tenants and two systems, foreign-ID requests, unavailable
  evidence, stale approvals, permission loss, response timeout, concurrent
  retries and partial failure; preserve scope and prevent duplicate writes.
- [ ] Inspect actual generated SSP/OSCAL and applicable CRM/control/package
  outputs against retained reviewed source values and preview contents.
  Preserve stable references, dates, source versions and open customer duties.
- [ ] An initial package requires no invented prior AO decision. Missing
  metadata and failed required checks remain actionable gaps, not success.
- [ ] Draft/provider changes preserve previous approved narratives, releases
  and exports; later acceptance is explicit and version-checked.
- [ ] Publication, association, adoption, duty review, document approval,
  export, receipt, import acceptance and AO decision remain distinct.
- [ ] Record desktop/mobile mock-fidelity and accessibility review plus
  reproducible local manual steps and actual artifact evidence.

## Boundaries and closure

No new cloud connectors or live eMASS integration claims. Any simulated
receipt/import fixture is labeled synthetic; actual external acceptance
requires separate evidence. Keep unmet dependencies and unperformed checks
open. The user must be able to test locally before closure.
```

## 6. New limited-service-model child owned by #1002

**Exact proposed title**

> [079 / #1002] Model SaaS and manual service relationships without new connectors

**Exact proposed body**

```markdown
## Ownership and nonduplication

Parent: #1002, under umbrella #UMBRELLA_ISSUE. Deliver after #1023's first
Azure provider-to-document slice and the agreed consolidation stages.

All-state duplicate searches for “service model” found no matching story;
“SaaS” returned #636, a strategic audit rather than this implementation.
Reuse #1026 catalog, #1030 relationship, #1037 adoption and #1040 export work.
Do not reopen historical cloud-connector issue #84.

## Outcome

Extend the existing offering/source/association contracts only as needed
to represent a SaaS or manually documented service without pretending it
is an Azure subscription or a verified live integration.

Distinguish provider identity, underlying supplier/operator, service model,
managed/brokered/customer responsibility arrangement, source decision
category and service scope. An upstream source from another supplier is
not an “InheritedMicrosoftReference.” Unknown authorization facts remain
not recorded; procurement or association establishes no inherited coverage.

Follow the required provider offering/source/scope/mission screens in
`docs/design/provider-workspace-mock/` and the Environment & hosting,
Applied capabilities and Responsibilities targets in
`docs/design/system-overview-mock/`.

## Acceptance

- [ ] Failing-first tests cover retained Azure records plus SaaS/manual
  records; no fabricated Azure tenant/subscription/resource identifiers.
- [ ] Two offerings under one provider cannot acquire each other's source,
  boundary, duties, evidence or authorization automatically.
- [ ] Managed and brokered/manual arrangements can retain different duties;
  association, adoption and authorized responsibility review stay separate.
- [ ] Supported reviewed service/source fields survive real document/export
  generation; unsupported mappings surface explicit gaps.
- [ ] Manual/no-connector coverage is visibly different from monitored,
  healthy, stale or unavailable live telemetry.
- [ ] Production-host tests reject unauthorized/foreign tenant, system,
  source and artifact references; evidence-sharing rules remain enforced.
- [ ] Existing records migrate deterministically with preserved identifiers,
  reviewed snapshots and provenance; unknown legacy values require review.
  Rehearse compatibility and rollback without rewriting accepted history.
- [ ] Verify desktop/mobile mock fidelity, provide synthetic local setup
  and artifact-inspection steps, and allow user review before closure.

## Explicit exclusions

No new AWS, GCP, OCI, M365 or other cloud connector. No provider provisioning
workflow, new publication/inheritance engine, inferred authorization or
claim of live eMASS acceptance. Reuse existing canonical services.
```

## 7. Exact additive comments on existing issues

Post one corresponding block to each named issue only after approval and
token substitution. These comments intentionally preserve existing bodies.

### Comment on #1002

```markdown
## Feature 079 coordination — additive scope clarification

Proposed umbrella: #UMBRELLA_ISSUE.
Specification: `specs/079-provider-system-workflow-consolidation/spec.md`.
This feature remains the provider/workspace owner and retains all current
children. Add #SERVICE_MODELS_ISSUE as the limited SaaS/manual-model child.
#1023 owns the first complete provider → Mission Owner → document/export
slice; existing story owners supply its seams without reparenting.

The required consolidated UI targets are
`docs/design/provider-workspace-mock/` and
`docs/design/system-overview-mock/`, not optional inspiration. Where older
mock layouts differ, use these suites while retaining this feature's
permissions, context, reviewed-state, compatibility and acceptance contracts.
Reuse current canonical offering, source review, publication, association,
responsibility, evidence/finding and impact services; no wholesale rewrite.

Stage provider migration/retirement through #1025 only after replacement,
production-host authorization/tenant tests, desktop/mobile review and
rollback evidence. No current child is closed or superseded by this comment.
```

### Comment on #1023

```markdown
## Feature 079 first-slice ownership — additive clarification

Proposed umbrella: #UMBRELLA_ISSUE. This feature owns the FIRST complete
CSP → Mission Owner → actual generated document/export slice, with the
independent-system comparison. Proposed child #SLICE_ISSUE owns its
integrated fixture and acceptance proof, not duplicate component services.

Coordinate #1028 publication, #1030 scoped relationship, #1037/#957 explicit
adoption/responsibility, #1039 package purpose, #1040 source metadata,
#1041 approved-profile output, #1042 readiness and #1043 honest failure.
Those stories retain their existing parents.

Implement the slice UI to the required provider-workspace-mock and
system-overview-mock suites under `docs/design/`. Verify real persisted
operations through production-host auth/tenant middleware and compare
actual generated artifacts with reviewed source values. Preserve explicit
publication, association, adoption, duty review, document/export, receipt,
import acceptance and AO states. No provider baseline or fictitious prior
AO decision is required for the independent initial-submission path.

Retain customer duties and permitted evidence provenance; failed/missing
source checks cannot become success. Provide local manual review and
desktop/mobile fidelity evidence. No live eMASS acceptance is implied.
```

### Comment on #1038

```markdown
## Feature 079 coordination — preserve the Systems workstream

Proposed umbrella: #UMBRELLA_ISSUE. Keep #1039–#1046 as this epic's actual
children; none moves to #1023 or directly under the umbrella.
#1023 owns first-slice integration using #1039–#1043 as dependencies.

Production Systems UI MUST follow `docs/design/system-overview-mock/`
and its provider handoff in `docs/design/provider-workspace-mock/`.
These are required layout/task/responsive targets, not optional examples.
Keep the approved-baseline, readiness, failure, authorization and tenant
contracts in the current issue bodies; prototypes supply no real statuses.

After the first slice and provider consolidation, complete the Systems
journey and its #1046 migration/retirement work, then #1045 scope/health
before #1044 executable rules/impact. Do not replace backend work with
browser-only statuses. Preserve F8 as a navigation regression check,
not a reopened defect. Actual generated artifacts, production-host tests,
desktop/mobile review, rollback evidence and local user review gate closure.
```

### Comment on #1018

```markdown
## Feature 079 integration clarification

Keep parent #1002. Supply provider-change/customer-review behavior to
#1023/#SLICE_ISSUE and coordinate #1044/#1045 without moving ownership.
Use the required change/impact/responsibility views in
`docs/design/provider-workspace-mock/` and
`docs/design/system-overview-mock/`.

Trace the changed release, implementation, duty or evidence to affected
systems, controls and document versions. Delivery, acknowledgement and
completed review are separate. Reuse the durable reconciliation/proposal
pipeline; never silently accept duties, overwrite approved narratives or
change AO decisions. Carry accepted updates into actual artifact tests.
Preserve existing overlap/unsubscribe/override/idempotency criteria and
add production-host tenant/permission and desktop/mobile/manual coverage.
```

### Comment on #1025

```markdown
## Feature 079 required navigation and retirement contract

Keep parent #1002. The consolidated layout MUST follow
`docs/design/provider-workspace-mock/` and
`docs/design/system-overview-mock/`. Provider navigation is Overview,
Offerings, Mission systems, Changes and Administration. These targets
supersede conflicting older top-level mock placement, not existing
context, role, specialist-action or compatibility requirements.

Own provider legacy-route/editing-surface retirement; coordinate #1046
for Systems. Inventory each old entry point and dependent action, map its
replacement/redirect, preserve record and workspace context, and document
migration/backfill and rollback before removing duplicate workflows.
No parallel library, login or tenancy implementation.

Deliver the shell needed for #1023's first slice before broader rollout.
Verify old/new deep links, refresh/history, multi-tab context, real auth/
tenant middleware, keyboard and desktop/mobile fidelity. Rehearse
rollback and provide a runnable local walkthrough before retirement.
```

### Comment on #1026

```markdown
## Feature 079 required offering/source integration

Keep parent #1002. Implement the consolidated catalog within the required
offering/source/service/capability hierarchy in
`docs/design/provider-workspace-mock/`, not a second provider catalog.
Where placement differs from the older mock, the consolidated suite governs;
retain this issue's paging, counts, provenance and permission criteria.

Reuse existing offering/authorization and source-processing services.
Support authorized packages and individual sources with visible analyzed,
unreadable, excluded, pending-review and unresolved results. Source upload
does not auto-publish extracted records. Onboarding can hand off to the
review queue rather than force review of every extracted item.

Metadata must come from retained reviewed sources. Coordinate #1040 rather
than invent authorization dates/types or a second ATO model. Feed #1023's
slice and #SERVICE_MODELS_ISSUE's bounded model extension. Verify protected
evidence availability, production-host isolation, real artifact contribution,
desktop/mobile fidelity and manual review.
```

### Comment on #1027

```markdown
## Feature 079 required implementation/duties experience

Keep parent #1002. Follow the required capability, source-review, evidence
and release flow in `docs/design/provider-workspace-mock/`. Reuse the
existing canonical contributor, coverage, source and working/released
revision services; reverify current implementation before extending them.
The historical audit is not an instruction to build another authoring engine.

Keep explicit provider/shared/customer duties, retained evidence provenance,
revision conflict handling and protection of the published customer source.
Show the resulting system document contribution and remaining customer
work. Coordinate #1028 publication, #1037 responsibility review and #1041
artifact mapping. Validate real generated outputs, production-host
permissions/tenant isolation, and desktop/mobile/manual fidelity.
```

### Comment on #1028

```markdown
## Feature 079 first-slice publication dependency

Keep parent #1002. #1023/#SLICE_ISSUE consumes this publication contract.
Follow the required review/release/impact screens in
`docs/design/provider-workspace-mock/`.

Reverify and reuse the current source-review and canonical publication
pipeline before adding missing behavior. The earlier audit does not require
a second publication engine. Preserve exact-revision approval, immutable
release identity, concurrency/idempotency and durable downstream delivery.

Publication is neither provider authorization nor mission adoption.
Association, release adoption, customer duty review and document approval
remain explicit downstream steps. Preserve previous customer-approved
snapshots during edits, retries and delivery failures. Include actual
first-slice artifact evidence plus production-host auth/tenant, stale-revision,
desktop/mobile and manual acceptance tests before claiming completion.
```

### Comment on #1030

```markdown
## Feature 079 required mission-service relationship experience

Keep parent #1002. Follow the required Mission systems → allocation →
customer relationship → Systems Environment & hosting handoff in
`docs/design/provider-workspace-mock/` and
`docs/design/system-overview-mock/`.

Reuse existing provider hosting/mission-association services and explicit
authorized projections. Show the exact offering, allocated scope and
relationship separately from adopted capability releases, responsibilities,
notification delivery and customer acknowledgement. No provider access to
private customer data or approval authority is implied.

Supply #1023's first slice. Later coordinate #SERVICE_MODELS_ISSUE without
forcing SaaS/manual relationships into fabricated Azure scopes. Verify
source/scope provenance in actual documents, production-host tenant/role
checks, desktop/mobile fidelity and local review.
```

### Comment on #1037

```markdown
## Feature 079 required system handoff and ownership

Keep parent #1002 and all current adoption, placement, operation recovery,
permission and responsibility acceptance criteria.
#1023/#SLICE_ISSUE uses this story as a dependency, not a new parent.

The consolidated production placement MUST follow Applied capabilities,
Responsibilities and Evidence in `docs/design/system-overview-mock/` and
the corresponding handoff screens in `docs/design/provider-workspace-mock/`.
These suites govern consolidated navigation/layout where older boards
differ; preserve the older boards' substantive capability/component,
placement, batch/removal and protected-review requirements.

An allocated/associated service is not an adopted release. Adoption does
not confirm duties or complete controls. Reuse canonical setup,
responsibility, evidence and narrative services; preserve customer review
permissions and source-qualified IDs. Demonstrate the accepted source,
scope, duties and evidence in actual SSP/CRM/control/package output.
Retain independent-system support and test production-host authorization/
tenant isolation, desktop/mobile fidelity, retries and local manual review.
```

### Comment on #1039

```markdown
## Feature 079 first-slice dependency

Keep parent #1038. Supply #1023/#SLICE_ISSUE with purpose-specific initial,
authorized-archive and change/reauthorization package behavior. Initial
generation must not require or create a fictitious prior AO decision.

Use the required package/readiness/recorded-decision flow in
`docs/design/system-overview-mock/`. Persist document approval, package
purpose, export generation, actual transfer/receipt, import acceptance and
AO decision separately. Export is not receipt or authorization.

Retain existing validation requirements where applicable. Verify actual
generated package contents, legacy purpose migration/rollback,
production-host auth/tenant isolation and local desktop/mobile/manual
acceptance. No live eMASS acceptance claim follows from local generation.
```

### Comment on #1040

```markdown
## Feature 079 source-backed output priority

Keep parent #1038. This is a blocking metadata seam for #1023/#SLICE_ISSUE;
do not open a duplicate leveraged-authorization defect.

Connect the current retained offering/authorization source and reviewed
version through adoption and preview into actual generated OSCAL.
Preserve source-stated title, type, issuer, date, scope and reference;
missing/conflicting/inapplicable metadata remains a validation gap.
Do not manufacture FedRAMP labels, authorization dates or unresolved parties.

Use required source/document-preview views in both consolidated mock suites
under `docs/design/`. Repeated generated artifacts must retain coherent
stable identities. Include production-host source/artifact authorization
and tenant isolation, real artifact inspection and local manual review.
Export never creates a provider authorization or mission AO decision.
```

### Comment on #1041

```markdown
## Feature 079 reviewed-data-to-artifact dependency

Keep parent #1038. Supply #1023/#SLICE_ISSUE's actual document/export proof;
coordinate #969 and #1040 without duplicating their persistence/metadata work.

Follow `docs/design/system-overview-mock/` and its provider handoff:
show source/version, “Used in” artifact destination and actual section
preview. Reuse approved scalar and structured child data, including the
applicable provider service/scope/responsibility context.

Test distinctive approved values in generated SSP and applicable package
outputs, not just saved DTOs or preview text. Draft edits preserve approved
baseline exports. Verify missing mappings, production-host tenant/role
checks, desktop/mobile fidelity and the complete local manual path.
```

### Comment on #1042

```markdown
## Feature 079 shared readiness dependency

Keep parent #1038. #1023/#SLICE_ISSUE and #1046 consume the same server-owned
purpose-specific assessment; do not add a separate browser checklist engine.

Implement the required Overview, document-preview and package-readiness
targets in `docs/design/system-overview-mock/` and the provider handoff.
Each blocker/gap identifies applicability, owner, reviewed source/version,
artifact destination and an authorized working action link.

Keep profile progress, duty review, document approval, schema validity,
submission readiness, export, receipt/import acceptance and AO decision
distinct. #1043's unable-to-verify checks cannot become passing readiness.
Verify screen/API/artifact parity, production-host scope controls,
desktop/mobile states and local manual acceptance.
```

### Comment on #1043

```markdown
## Feature 079 truthful failure-state dependency

Keep parent #1038. Preserve the current failed-check/load/mutation criteria
and supply #1023/#SLICE_ISSUE and #1042 with explicit unable-to-verify,
unavailable, stale, missing and not-applicable results.

The required provider-workspace-mock and system-overview-mock suites under
`docs/design/` include restricted, unavailable and incomplete workflows.
Implement real recoverable states, not simulated success or successful zero
counts. A failed required check cannot establish final readiness.

Use fault-injection tests through the production host, retain scope and
last-known timestamps on retry, and verify actual artifact validation
findings. Include desktop/mobile and local manual recovery checks.
```

### Comment on #1044

```markdown
## Feature 079 scoped rule-to-document sequence

Keep parent #1038. Deliver after the first #1023 slice, consolidation and
#1045's reviewed scope/health foundation. Reuse monitoring/impact/review
services and coordinate #1018 plus #754/#676; no parallel rule scheduler.

Follow the required Rules, Detected changes and Impact reviews in
`docs/design/system-overview-mock/` and provider change/rule screens in
`docs/design/provider-workspace-mock/`. Persist source, scoped target,
condition, cadence, severity, owner, reviewed baseline and response.

Trace evidence to affected duties/controls/documents, deduplicate replay
and stage updates for authorized review without rewriting approved exports.
A recommendation never automatically issues, extends or revokes an ATO.
Verify production-host authorization/tenant isolation, actual document
updates, unavailable telemetry, desktop/mobile fidelity and local review.
```

### Comment on #1045

```markdown
## Feature 079 scope and telemetry-health foundation

Keep parent #1038. Supply #1044 only after attribution/health is verified.
Follow the required Coverage & health, Changes and Impact views in both
consolidated mock suites under `docs/design/`.

Attribute each event to reviewed system resource/boundary scope or an
explicit shared-provider dependency. Distinguish out-of-scope and unknown;
subscription attachment does not prove coverage. Show collection time,
last success, missing permissions, stale/failed telemetry and gaps apart
from enabled configuration or zero findings.

Verify two systems sharing a subscription, changed boundaries, shared
dependencies and production-host tenant isolation. Explain affected
controls/evidence/documents without claiming cATO or making AO decisions.
No new cloud connectors are included; manual service coverage remains
explicitly manual. Provide desktop/mobile and local review evidence.
```

### Comment on #1046

```markdown
## Feature 079 required Systems UI and retirement contract

Keep parent #1038. Production UI MUST follow
`docs/design/system-overview-mock/` and the Systems companion screens in
`docs/design/provider-workspace-mock/`; these are required implementation
targets, not optional inspiration.

Use the eight groups: Overview, System definition, Controls & evidence,
Assessment & risk, ATO package & eMASS, Continuous monitoring, Team and
History. Deliver the subset required by #1023's first slice first, then
complete the remaining suite. Reuse canonical services and server readiness.

Own the Systems keep/group/redirect/retire matrix with #1025's shared-shell
coordination. Inventory old actions/dependencies, preserve specialist
functions and history, and document replacement, migration/backfill,
compatibility and rollback before retiring duplicate workflows. Rehearse
rollback with existing-record fixtures and verify old links, refresh,
history and independent tabs. Preserve F8 as a navigation regression check.

Package/reconciliation UI distinguishes export generation from actual
transfer/receipt, import acceptance and recorded AO decisions; unrecorded
external outcomes remain explicit. Gate closure on production-host scope/
permission tests, real generated artifacts, desktop/mobile/keyboard review
and a locally runnable user walkthrough. No live eMASS claim is implied.
```

## 8. Sources checked and unresolved decisions

### Checked directly

- `AGENTS.md`, `.github/copilot-instructions.md` and the constitution's
  documentation/source-of-truth guidance.
- Git status, baseline `13204325`, remote URL and canonical GitHub redirect.
- Current bodies of #1002, #1023, #1038, #1018, #1025–#1028, #1030,
  #1037 and #1039–#1046; actual parents for these stories.
- All immediate children of #1002/#1023/#1038 with complete pagination.
- Duplicate searches and the nearest handoff/strategic/historical issue
  bodies described above. Historical issues are context, not present-day
  implementation verification.
- `docs/design/provider-workspace-mock/README.md` and its file inventory.
- `docs/design/system-overview-mock/README.md` and its file inventory.
- `docs/design/csp-product-validation-2026-09-26.md`.
- `docs/design/system-product-goal-audit-2026-09-26.md`.

### Explicit ambiguities and limits

1. **Issue numbers do not yet exist.** Substitute the three tokens only
   after approved creation; preview final dependent payloads before posting.
2. **Spec story links need final synchronization.** US1–US6 were read from
   the concurrently authored specification and mapped above. US2's #1023
   feature ownership must also link the proposed acceptance child once its
   approved issue number exists; US6 must link the service-model child.
   A feature-only link does not satisfy story-level hierarchy requirements.
3. **Old and new UI references differ.** The proposed comments explicitly
   give the two consolidated suites layout precedence while preserving
   existing data/security/interaction requirements. Any material exception
   still needs documented review, not silent omission.
4. **Historical “missing service” findings can be stale.** This task read
   the current issue bodies and local source-audit documents; it did not
   independently execute/re-audit the application service paths. Reuse-first
   implementation must verify those paths before adding contracts.
5. **Migration specifics require implementation inventory.** No exact
   schema or route deletion is invented here. #1025/#1046 and the bounded
   service-model child own verified inventories and rollback evidence.
6. **Receiving eMASS workflow is unverified.** No receipt, successful import,
   live Azure result, current provider entitlement or AO decision was
   established by this documentation task.
7. **No automatic closure.** The new umbrella and existing workstreams close
   only after their own criteria and children are completed or explicitly
   dispositioned with evidence, and the user can test locally.
