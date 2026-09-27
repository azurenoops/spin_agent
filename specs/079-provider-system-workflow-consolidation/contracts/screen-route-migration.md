# Mandatory mock-to-screen and route contract

**Implementation status (2026-09-26)**: This remains the target acceptance
contract, not a declaration that every row is complete. The current
[checkpoint](../tasks.md#implementation-checkpoint-and-release-gates)
distinguishes implemented/tested slices, retained legacy content, and unavailable
workflows. Mandatory mock fidelity and source/output gates are not waived.

## Reading the matrix

Targets: [provider index](../../../docs/design/provider-workspace-mock/screens/index.json),
[provider interactions](../../../docs/design/provider-workspace-mock/app.js),
[Systems pages](../../../docs/design/system-overview-mock/pages.html), and
[overview scenarios](../../../docs/design/system-overview-mock/index.html).
Implement their UI, not an interpretation that retains conflicting old screens.
No intentional visual/interaction deviation may ship without user approval.

All routes below are workspace-relative, resolved with the existing authenticated
workspace navigation. `O` means `/authorizations/offerings/:offeringId`;
`S` means `/systems/:id`. Proposed child routes are not claimed to exist yet.
Preserving route strings does not mean preserving the old page layout.

Each row inherits:

- **Roles** describe primary users, not new grants. Provider operator `P` uses
  existing provider policies; `MO` is assigned Mission Owner/System Owner;
  `I` is assigned ISSM/ISSO as permitted per action; `A` is SCA/ISSM for
  assessment actions; `AO` retains separate decision authority; `R` means a
  read-authorized user; `Admin` means the applicable scoped administrator.
- **States**: every page has loading, populated, empty, unavailable/retry, stale
  and restricted states; mutation pages add revision conflict and pending/error
  handling. The row lists its domain states in addition.
- **V**: desktop 1440px/mobile 390px comparison to the exact mock screen,
  keyboard/focus/dialog tests, no overflow, and user side-by-side review.
- **C**: production route/API/authorization/tenant-isolation test for actions,
  real persistence where applicable, retry/concurrent edit coverage.
- **L**: source/version-to-actual-output assertion. `H` asserts no unauthorized
  history mutation. None may be replaced by snapshots alone.
- Phase dependencies are the PR gates in [plan](../plan.md). Each page's
  listed record input must be loaded from the authoritative domain service;
  mock counts/names/states are fixture data only.

## Provider suite: all 36 screens

| Mock ID / task | User | Route and existing surface disposition | Authoritative inputs; actions and domain states | Contribution | Dependency/tests |
|---|---|---|---|---|---|
| `overview` / prioritize work | P | Provider root overview; replace presentation | Offering/release/impact summaries; navigate pending review, expiring source, evidence request | Next package/monitoring action | 2C,3C; V,C,L |
| `offerings` / find service | P | `/authorizations`; reuse listing/API, replace UI | Offerings and releases; filter/create/open; draft/published/withdrawn | Service identity | 3A; V,C |
| `create` / define offering | P | `/authorizations/create`; reuse intake | Offering identity, environment, owner; create draft, no authority inferred | SSP service description | 3A; V,C,L |
| `offering` / maintain service | P | `O`; reuse overview | Offering/source/scope/release projections; follow pending task | Source-backed service baseline | 3A; V,C,L |
| `sources` / review source inventory | P | `O/packages`; replace listing UI | Package versions, external decisions, citations; add/open; received/reviewed/partial | Authorization/source references | 2A,3A; V,C,L |
| `import` / add material | P | `O/import`; reuse receipt flow | Authorized files and offering context; upload/reconcile retry; received/processing/failed | Provenance/coverage | 2A; V,C |
| `analysis` / resolve coverage | P | `O/packages/:packageId`; reuse package review | Manifest, extraction checkpoints/candidates; retry/exclude with rationale; partial/unresolved | Honest source coverage | 2A; V,C,L |
| `source-review` / confirm claim | P | `O/packages/:packageId/candidates/:candidateId` proposed | Exact source/candidate revisions; review/reject; pending/approved/stale | Reviewed implementation/duties | 2A; V,C,L,H |
| `authorization` / inspect decision | P | `O/decisions/:decisionId` proposed; reuse decision services | Reviewed title/issuer/date/boundary/conditions; record/review external decision | Leveraged authorization reference | 1B,3A; V,C,L,H |
| `scope` / inspect service | P | `O/inherited-coverage`; reuse hosting setup | Immutable scope revision; inspect inclusions/exclusions | Hosting/boundary description | 2B,3A; V,C,L |
| `scope-edit` / propose revision | P | `O/inherited-coverage/propose` proposed | Prior scope and proposed changes; submit impact; draft/reviewed | Versioned hosting scope | 3A; V,C,H |
| `capabilities` / reusable protection | P | Existing CSP security-capabilities route, offering-filtered | Releases, components, authored duties; select/create | SSP/CRM inputs | 3B; V,C,L |
| `capability` / inspect implementation | P | Existing CSP capability detail; replace presentation | Working/published revision, controls, evidence, duties; edit/review | Reusable implementation | 2A,3B; V,C,L |
| `release` / publish exact revision | P | Existing capability review tab or package publication entry | Approved candidate/context hashes; preview/confirm publish; stale conflicts | Immutable release for adoption | 2A; V,C,L,H |
| `evidence` / maintain support and findings | P | `O/findings`; replace generic fallback with working UI | Provider artifacts/findings/POA&M; list/add/review | Assessment support/mission impact | 3B; V,C,L |
| `evidence-detail` / manage customer access | P | `O/evidence/:evidenceId` proposed | Retained file/hash/version and sharing decision; approve allowed access/reference | Usable mission evidence | 2B,3B; V,C,L,H |
| `finding` / remediate weakness | P | `O/findings/:findingId` proposed | Finding, remediation, evidence, reviewer; submit evidence, review closure | Provider weakness/mission risk review | 3B; V,C,H |
| `missions` / inspect consumers | P | Provider mission-systems view, reuse relationship APIs | Assignments, associations, adoptions; filter/open | Dependency trace | 3C; V,C |
| `allocation` / assign eligible scope | P | Existing hosting setup action, new mock form | Scope revision/system eligibility; assign; available/revoked | Allocation, not authorization | 2B; V,C,H |
| `customer` / inspect relationship | P | Scoped relationship detail proposed | Allocation/association/adopted release/duties; follow permitted handoff | Reproducible use of service | 2B,3C; V,C,L |
| `changes` / triage changes | P | Provider changes view proposed; reuse impact summaries | Source/release/scope changes and targets; select review | Monitoring work queue | 3C,5; V,C,H |
| `impact` / provider impact review | P | `O/impact`; reuse hash-fenced impact service | Before/after version, targets; record provider review, no mission decision | Explained implementation/document impact | 3C,5B; V,C,L,H |
| `monitoring` / service collection health | P | Provider monitoring view proposed | Collection freshness, source expiry, evaluated rules; inspect/test | Provider monitoring evidence | 5A; V,C |
| `rule` / executable condition | P | Provider rule editor proposed | Signal/scope/condition/cadence/owner; save/test/disable | Retained evaluation/review trigger | 5B; V,C,H |
| `administration` / people and connections | Admin | Existing workspace administration; mock composition | Memberships/effective grants/configured connection; lookup/review assignment | Accountability/access | 3C; V,C |
| `history` / trace retained actions | P/R | Existing audit service, provider-scoped view | Actor/source/review/release/access events; filter/read | Audit trail | 3C; V,C,H |
| `system-hosting` / associate scope | MO | `S/profile/EnvironmentAndDeployment/hosting`; replace wizard presentation | Provider allocation + reviewed mission relationship; explicitly associate/defer | SSP hosting context | 2B; V,C,L,H |
| `system-capabilities` / apply release | MO/I | `S/security-capabilities`; replace presentation | Applicable exact releases/context; select/adopt, newer available | Pinned implementation | 2B; V,C,L,H |
| `system-duties` / fulfill customer work | I; MO reads | `S/inheritance/subscriptions`; reuse review | Subscription/revision/confirmed duties; authorized confirm/update evidence; pending/stale | CRM and mission narrative | 2B; V,C,L,H |
| `system-evidence` / inspect usable artifacts | I/R | `S/evidence`; add provider provenance view | Allowed artifact/reference/access gap; view/request/upload as permitted | Evidence manifest | 2B; V,C,L |
| `system-documents` / inspect source mapping | I/R | `S/documents/preview` proposed | Approved snapshot/decision/adoption/evidence; inspect/follow source | Generated SSP/package preview | 2C; V,C,L,H |
| `system-package` / resolve blockers | I/R | `S/documents`; mock checklist | Purpose/readiness requirements; fix/revalidate/generate if permitted | Accurate submission package | 1D,2C; V,C,L |
| `system-impact` / mission disposition | I/MO as permitted | `S/conmon/impacts/:impactId` proposed | Adopted baseline and provider change; record follow-up | Staged mission document updates | 5B; V,C,L,H |
| `system-monitoring` / system health | I/R | `S/conmon`; mock health view | Reviewed resource/dependency scope + collection/evaluation state | System monitoring evidence | 5A; V,C |
| `onboarding` / establish provider workspace | Admin/P | `/onboarding/csp`; reuse intake, replace UI | Authorized identity/offering/source receipt; upload or defer, complete setup | Usable workspace/private review queue | 3A; V,C,H |
| `onboarding-status` / continue review later | Admin/P | `/onboarding/csp` completion state | Persisted receipt and setup state; open portal review | Explicit remaining work, not publication | 3A; V,C |

## Systems suite: all 30 pages

| Mock page / task | User | Route and existing surface disposition | Inputs; actions and domain states | Contribution | Dependency/tests |
|---|---|---|---|---|---|
| Readiness | MO/I/R | `S`; replace SystemDetail composition | Server purpose/readiness and next action; preparation vs monitoring | Submission/monitoring priorities | 1D,4C; V,C,L |
| Mission | MO/I | `S/profile/MissionAndPurpose`; reuse profile service | Approved/draft mission fields; save/submit/review by permission | SSP mission/system description | 1C,4A; V,C,L,H |
| Users | MO/I | `S/profile/UsersAndAccess`; reuse | Reviewed user categories/structured rows; add/edit/review | SSP users/access | 1C,4A; V,C,L |
| Environment & hosting | MO/I | `S/profile/EnvironmentAndDeployment` plus hosting child | Deployment/profile + association; review scope/save draft | SSP environment | 2B,4A; V,C,L |
| Data | MO/I | `S/profile/DataTypes`; reuse | Approved data types and categorization/privacy inputs; edit/review | SSP information/privacy references | 1C,4A; V,C,L |
| Inventory & boundary | I | `S/boundaries` + inventory tab using `security-capabilities/inventory` | Boundary resources/components/placements; review/add/remove | SSP boundary/inventory | 4A; V,C,L,H |
| Ports & interconnections | MO/I | `S/profile/PortsProtocolsAndServices`; connect interconnections | Reviewed structured ports/agreement records; add/review | SSP network/interface register | 1C,4A; V,C,L |
| Categorization & baseline | I | `S/baseline`; replace UI, keep rules | Information impact and selected control set; review/select | SSP/control applicability | 4A; V,C,L |
| Applied capabilities | MO/I | `S/security-capabilities`; reuse selected-system service | Organization placements and provider adoptions; add/apply/remove safely | SSP implementation | 2B,4A; V,C,L,H |
| Responsibilities | I; MO reads | `S/inheritance/subscriptions`, inheritance summary retained | Exact duties/provenance/revisions; confirm allocations | CRM/control responsibilities | 2B,4A; V,C,L,H |
| Narratives | I | `S/narratives`; library stays child | Approved text/proposals/library references; author/review | SSP implementation statements | 4A; V,C,L,H |
| Evidence | I; A verification | `S/evidence`; reuse evidence APIs | Artifacts/versions/source links; upload/verify under permissions | Assessment and package evidence | 2B,4A; V,C,L |
| Policies | I | `S/legal`; reuse policy assignment | Source policies/applicability; assign/remove with visible errors | SSP authorities/policies | 1C,4A; V,C,L |
| Assessment plan | A | `S/assessments?tab=plan` proposed tab | SAP scope/procedures/team/version; draft/submit/finalize | SAP | 4B; V,C,L,H |
| Assessments & results | A/I per action | `S/assessments`; environment child retained | Assessment configuration/results/imports/SAR; execute/review | SAR/results/findings | 4B; V,C,L |
| Findings & remediation | I | `S/remediation`; reuse tasks/findings | Weakness/evidence/owner; triage/assign/verify closure | SAR/POA&M links | 4B; V,C,L,H |
| POA&M | I/AO per action | `S/poam`; reuse | Items/milestones/owner/dates; create/update/disposition | POA&M | 4B; V,C,L |
| Exceptions | Authorized requester/reviewer | `S/deviations`; preserve distinct records | Waivers/risk/false-positive evidence; request/review | Risk register/decision support | 4B; V,C,H |
| Readiness checklist | I/R | `S/documents`; no second calculator | Purpose-specific requirement snapshot; fix/revalidate | Submission checklist | 1D,4B; V,C,L |
| Document previews | I/R | `S/documents/preview` proposed | Approved manifest/artifact content; inspect source mapping | SSP/SAP/SAR/POA&M | 2C,4B; V,C,L,H |
| Export packages | I | `S/documents?tab=exports` proposed tab | Purpose/validation/jobs/history; generate/download | Retained package/manifest | 2C,4B; V,C,L,H |
| eMASS reconciliation | I | `S/emass/status`; reuse | Export/import/conflict records; upload/resolve/defer | Reconciled records/exchange history | 4B; V,C,L,H |
| Recorded decisions | AO writes; R reads | `S/authorize`; keep server decision service | Source/authority/conditions/baseline; record permitted decision | Authorization record | 4B; V,C,H |
| Coverage & health | I/R | `S/conmon`; replace UI | Attributed scopes and collection/evaluation freshness; inspect | ConMon coverage/evidence | 5A; V,C |
| Rules | I | `S/conmon/rules` proposed | Versioned condition/owner/cadence; create/test/disable | Rule/evaluation history | 5B; V,C,H |
| Detected changes | I/R | `S/conmon/changes` proposed | Scoped observations, unknown/out-of-scope attribution; inspect | Monitoring evidence | 5A; V,C,L |
| Impact reviews | I/MO per action | `S/conmon/impacts` proposed | Retained baseline/deltas/targets; stage/disposition | Document/reassessment review | 5B; V,C,L,H |
| Reports | I/R | `S/conmon/reports` proposed | Retained period metrics/evidence; generate/read | Monitoring reports | 5B; V,C,L,H |
| System team | Admin/I per role policy | `S/roles`; reuse assignments | Effective roles/memberships; assign permitted roles | SSP accountable parties | 4C; V,C,L |
| Audit history | R | `S/history` proposed, existing audit API reuse subject to scope check | Retained actor/time/source actions; filter/read | Auditability | 4C; V,C,H |

## Old destinations and aliases

All 24 existing primary destinations are mapped above: overview, security
capabilities, roles, boundaries, six profile sections, baseline, inheritance,
narratives, narrative library, legal, assessments, remediation, POA&M, evidence,
deviations, authorize, documents, ConMon and eMASS.

The sixth profile section, `profile/LeveragedAuthorizations`, is not a separate
target mock page. Redirect it to Environment & hosting's reviewed source context
with a link to Document previews; retain authorized history/detail access and
do not ask users to re-enter provider decision metadata.

Preserve aliases `components/*`, `capability-coverage/*`, `capabilities/*`,
`control-inheritance`, `categorization`, `mission-purpose`, `users-access`,
`environment`, `data-types`, `ports-protocols`, `leveraged-auth`, and
`legal-regulatory`. Update the existing alias resolver once; do not add a second
redirect engine. Retain `assessments/environment` as a working configuration
destination (F8 is not a missing route).

## Release gates

For every row, record current implementation owner, API/action mapping, V/C/L/H
results, approved deviations, and retirement task. Undocumented actions cannot
be dropped just to fit a mock; route them through the matching task/details
pattern and obtain approval where design is ambiguous. Production data,
security, error handling and accessibility are real implementations, not copies
of in-memory mock state.
