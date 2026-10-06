# Mandatory mock-to-screen and route contract

**Navigation label refinement (2026-09-29)**: The organization/system left
navigation group is **ATO Readiness**, replacing **ATO package & eMASS**.
Its routes, icon, five task tabs, purpose propagation and permissions remain
unchanged. The mobile group label uses the same name.

**Role-aware next actions (2026-09-28)**: Readiness uses the read-only
`GET /api/dashboard/systems/{systemId}/next-actions` projection, separately from
the manual full-package Check readiness operation. Its plain JSON response is
`{systemId, checkedAt, effectiveRoles, items, waitingOnOtherRoles}`; each item
contains `{id, title, description, path, actionLabel, responsibleRole}` and each
waiting summary contains `{role, count}`. Paths are allowlisted system-relative
screen links, and `actionLabel` is `Open`, never an approval/mutation command.
Return all determinable actions in stable priority order; presentation may show
the first three. Recompute from saved canonical state on every GET, without
generating records, changing status or running whole-package validation.
Tenant/system access and effective persisted role precedence come from
`ISystemWorkspaceAccessService`; browser persona/role headers cannot add tasks.
Applicable roles are unioned, while administrator/oversight/read-only access alone
does not confer specialist work. Mission Owners receive incomplete/revision
authoring or saved-draft submission links, not ISSM reviews. ISSMs receive
submitted profile/category reviews and authorized management work, not another
Mission Owner's authoring checklist. Access context and individual user categories
are distinct governance units. Optional leveraged authorizations are not required
profile tasks. Submitted work assigned to another role appears only in the
waiting summary. Modern boundary definitions and component assignments count as
real boundary state; do not reuse the legacy static phase teaser.
SCA, AO and ISSO items require both applicable roles/permissions and verified
canonical lifecycle gates; uncertain specialized follow-up is omitted.
Regression coverage includes role overrides/inheritance, draft/submitted/approved
progress, item replacement/removal, modern boundary state and denied access.
The bounded initial projection covers five mandatory profile sections plus
independently governed user categories; saved Draft means a submission follow-up,
not a claim of completeness. It does not invent a completeness-percentage gate.
ISSM management identifies absent boundary definitions or unassigned component
scope, respecting modern in-scope placements and the lifecycle's legacy
component-assignment fallback. ISSO work covers unsubmitted control narratives
and existing nondeleted evidence explicitly classified as `Unclassified`.
Narrative review requires an UnderReview implementation and its current saved
version. SCA work in Assess covers missing SAPs only with nonempty control
baselines, the latest draft SAP, and missing SARs only with saved control-
effectiveness determinations. AO work is navigation to package/decision
prerequisite review in Authorize, not a claim that issuing a decision is allowed.
Further SAR review/renewal and evidence-completeness suggestions are intentionally
omitted rather than synthesizing eligibility. Successful GETs use `Cache-Control:
no-store`. Canonical role strings are `MissionOwner`, `Issm`, `Isso`, `Sca` and
`AuthorizingOfficial`; `effectiveRoles` may also include nonspecialist roles.

**External interconnection editing contract (2026-09-28)**: Ports &
interconnections uses a right-side editor backed by canonical
`SystemInterconnection` records, not the abbreviated document catalog.
`GET /api/dashboard/systems/{systemId}/interconnections` returns paged
`items`, `total`, `page`, `pageSize`, and `canManageInterconnections`;
`GET .../interconnections/{interconnectionId}` returns full detail.
`POST .../interconnections` retains legacy quick-add compatibility and accepts
complete editable fields; `PUT .../interconnections/{interconnectionId}` replaces
the editable details through `IInterconnectionService`. Full fields are target
name/owner/acronym, connection type, flow direction, classification, data
description, protocol/port/security-measure arrays and authentication method.
Optional text can be cleared with empty strings; empty arrays clear collections.
GET requires system read access; POST/PUT require exact system-management
authority (legacy dashboard uses ComplianceReader/ComplianceWriter respectively).
Every record lookup is tenant-filtered and scoped to the route's system.
Responses expose read-only lifecycle status/reason, authorization-to-connect,
agreement summaries and creation/modification provenance. Editing details must
not change those fields, approve agreements, certify absence of connections, or
erase history. Unknown/protected mutation properties are rejected.
Writes return a persisted receipt; the typed dashboard adapter rereads canonical
detail and propagates receipt/read failures rather than reporting false success.
List defaults to 50 records (maximum 200); callers can request further pages.
Regression coverage must verify full-field round trips, unchanged agreements/
status/history, invalid input, cross-system/tenant denial and reader/write fences.
Canonical `UpdateInterconnectionAsync` additionally accepts optional owner/acronym
updates; omitted arguments retain existing values for non-dashboard callers.
The current system-management policy grants this operation to applicable ISSM
assignments; SystemOwner, ISSO and Assessor assignments alone remain read-only.
The adapter exports `InterconnectionEditableFields`, `SystemInterconnectionDetail`,
`SystemInterconnectionsResponse`, `listSystemInterconnections`,
`getSystemInterconnection`, `createSystemInterconnection`, and
`updateSystemInterconnection`. List arguments are `(systemId, signal?, page = 1,
pageSize = 50)`; detail uses `(systemId, id, signal?)`; update uses
`(systemId, id, editableFields)`. Optional null text is serialized as empty text.
For local verification, run the `InterconnectionEditingHttpTests` integration
class and the existing `InterconnectionServiceTests`, `InterconnectionToolTests`
and `SspInterconnectionTests` unit classes. Once the drawer is integrated, manual
acceptance must open an existing local synthetic record, edit and reopen it,
confirm unchanged lifecycle/agreement details, cancel without saving, and
confirm that a read-only assignment has no write controls.

**Implementation status (2026-09-26)**: This remains the target acceptance
contract, not a declaration that every row is complete. The current
[checkpoint](../tasks.md#implementation-checkpoint-and-release-gates)
distinguishes implemented/tested slices, retained legacy content, and unavailable
workflows. Mandatory mock fidelity and source/output gates are not waived.

**Live next-action correction (2026-09-28)**: The Readiness overview must not
render a fixed starter checklist or derive the actionable card from automatic
InitialSubmission package validation. This paragraph supersedes the earlier
automatic-validation instruction: load the role-scoped
`GET /api/dashboard/systems/{systemId}/next-actions` projection on entry and
refresh it after saved progress, on browser focus or explicit task refresh.
Only `items` supplies actionable links; `waitingOnOtherRoles` is a separate,
non-actionable summary. Full-package **Check readiness** remains a separate,
manual overall checklist and must never assign its other-role findings to the
Mission Owner. Completed actions disappear or become the appropriate saved-state
follow-up. Loading/failure is explicit, without a success-shaped empty list or
static fallback. Task IDs are deterministic within the system and tied to their
source section, persisted category or singleton workflow plus the current action;
they are not generated from display titles or response timestamps. Paths are
allowlisted system-relative screen routes, never arbitrary URLs. Profile and
category actions link to their actual sections. Preserve authoring/submission/
review separation; neither task loading nor overall validation automatically
generates, submits, approves or advances RMF state. The typed adapter exports
`getSystemNextActions` and `SystemNextActionsResponse` from
`src/Ato.Copilot.Dashboard/src/api/systemNextActions.ts`.

**Boundary record semantics correction (2026-09-28)**: The single main table
lists boundary definitions, including boundaries with zero assigned components.
Its count and rows must use the same definition records. Use Boundary, Type,
Description and Role columns with Open; retain the compact mock presentation.
Open targets that exact boundary's management drawer, where component assignments,
their scope/details and Add components belong. Do not filter out an empty
boundary or label component assignments as boundary records. This supersedes
the earlier main-table component-placement interpretation below.

**Boundary visual correction (2026-09-28)**: Compare the running Inventory &
boundary page directly with `system-overview-mock/pages.html#Inventory%20%26%20boundary`.
Keep the mock's compact title/description/table/callout card and three short
support blocks. Do not show a redundant selector for a single boundary, generic
type/source subrows, or a long inventory tutorial on the main page. Preserve
multi-boundary selection, exact placement/source details, inventory guidance and
source handoffs in the management workflow. Match typography, action alignment,
table spacing and button treatments using browser measurements against the
reference. Real empty records and scope status must remain honest; do not seed
mock rows or manufacture review state for visual similarity.

**Readiness refinement (2026-09-27)**: Remove the entire "System diagnostics &
RMF phase management" disclosure from the system Readiness page, including its
legacy charts, phase controls, embedded role assignment and auxiliary polling.
Keep Preparing for ATO and Monitoring & follow-up. This is a presentation
removal, not deletion of retained phase history, domain services or other system
task pages.

**Exact offering-tab correction (2026-09-27)**: The requested reference is
`docs/design/provider-workspace-mock/index.html#offering` and its five offering
sections. Use exactly Overview, Authorizations & sources, Services & scope,
Capabilities & responsibilities, and Evidence & findings; change-impact remains
reachable through its existing task actions rather than an extra sixth tab.
Match the reference tab spacing, active underline, horizontal mobile scrolling,
offering breadcrumbs/headers and each section's card/table/support composition.
Keep real release revisions, source counts and permission states, not fixture
values. Preserve the subsequently requested CRUD dialogs. Visual acceptance must
include all five deployed tabs, not only isolated card or route tests.

**Mission relationship/action correction (2026-09-27)**: Match the provider
`#missions`, `#allocation` and `#customer` flows, not merely their labels.
Assign service scope must open named organization/system and permitted-scope
selection without requiring users to transcribe GUIDs. View relationship must
retain the selected offering/allocation and remain in the Mission systems
navigation context. Show actual pinned customer releases separately from current
offering publications. Provider handoff previews are explicitly read-only;
they must not impersonate a customer or claim access to private mission content.
Query-driven task links must react to navigation, close cleanly and work again
when reopened. Review-impact labels must identify the scope they actually open.

**Capability detail correction (2026-09-27)**: `tab=implementation` and
`tab=responsibilities` must render distinct, named panels. Implementation shows
the capability purpose, actual service contributors and retained sources.
Responsibilities shows the saved control-duty allocation and missing duty
detail explicitly; it must not duplicate the implementation page or merely
expand its editor. Working-revision editing uses a focused dialog. Keep exact
review/publication fences and link the offering's capability list to the correct
detail tab. Match the capability mock's banner, cards and support composition
without inventing proposed release content or customer completion.

**CRUD presentation refinement (2026-09-27)**: Use the existing modal dialog
pattern for focused create/edit operations and destructive confirmations on
provider and organization/system task pages. Keep record summaries and tables
visible when no operation is open. Keep filters, search, read-only details and
long-form document authoring in their task context rather than forcing every
interaction into a modal. Dialog changes must preserve server authorization,
revision fences, explicit review/confirmation steps and errors. Retain entered
values after failed saves, prevent dismissal during writes, and verify keyboard
focus, cancellation and mobile scrolling.

## Reading the matrix

**Single boundary surface correction (2026-09-28)**: The main boundary page
must not expose both the selected-boundary panel and a second expandable
boundary-management list. The user's subsequent attached layout clarifies that
each component row's **Open** and the header's **Review boundary** open the same
right-side boundary-management drawer. That drawer exposes the selected
component/placement, boundary details and permitted component assignment and
boundary CRUD. Remove the duplicate boundary-definition table entirely; do not
merely move it into another dialog. No duplicate empty state or P-16 guidance
box sits below the mock's main panel. Keep mutation controls authorized and
explicit and lock dismissal during pending writes. Opening a drawer acquires no
edit lock. Verify the actual server on port 5197, not only the Docker dashboard.

**Components & system scope clarification (2026-10-06)**: The task formerly
called Inventory & boundary keeps `S/boundaries`. Raw scope decisions, named
definitions with unverified purpose, recorded internal groups and external/shared
service connections remain distinct. Draft saves use governed design revision
fencing; source definition/placement writes are explicitly immediate.

**Inventory & boundary refinement (2026-09-28)**: Follow the exact boundary
mock with a selected mission-boundary panel and compact component/scope rows,
Review boundary as a read-only primary action, and documentation/review/source
handoffs. Boundary management remains explicitly available but must not dominate
the page with a duplicate table. Open must inspect the selected component or
boundary, not mutate its scope, acquire an edit lock, or imply approval.
Create/edit/remove and placement changes retain their existing authorization,
concurrency and lock gates.

Explain the inventory relationship accurately: RMF CM-8 component accountability
still applies to cloud-native systems, with customer-managed workloads/software
and provider-managed dependencies clearly separated. This boundary table
establishes inclusion/exclusion, not completeness of HW/SW metadata. Existing
component discovery/import and the separate detailed InventoryItems/MCP workbook
workflow are not assumed to be automatically synchronized. Disclose missing
capture/export mappings instead of claiming a complete HW/SW artifact.

**Data handling context dialog (2026-09-28)**: Remove Information handling
context from beneath the Data table and provide a dedicated system-wide dialog.
Data Overview and Highest Sensitivity Level are section-level fields, not
attributes to duplicate into every data-type row. Keep per-type source,
destination, classification and regulations in each record's dialog. Cancelling
system-wide context restores its opening scalar values without discarding row
drafts. Saving persists context and pending rows atomically through the existing
profile API. Preserve section-level review and read-only/pending-write guards.

**Exact Data page refinement (2026-09-28)**: Follow `pages.html#Data` with
**Add data type** as the header primary action, compact information-type rows
and one **Open** action per record for inspection. Keep source, destination,
regulations and full descriptions in the focused record dialog; edit/remove
remain explicit, authorized draft operations. Accept meaningful data-type names
such as Mission support records rather than limiting users to a fixed taxonomy.
Preserve the existing stored fields and real sensitivity labels; do not replace
them with fabricated FIPS impact levels or privacy determinations. Saved Data
review remains section-level unless a separate persisted contract is introduced;
do not imply independent per-row approval. Save draft persists both context and
rows; failed writes preserve inputs, and review never submits unsaved edits.
Keep all six System definition destinations and the actual SSP/categorization
handoffs scoped to the selected system.

**Environment header simplification (2026-09-28)**: Remove the top **Review
hosting scope** CTA now that the Provider hosting card owns selection and
scope-row review. Keep Choose provider hosting, row actions, draft save, and
existing scoped hosting routes available; do not replace the removed CTA with
another duplicate header action.

**Scope review versus description-copy correction (2026-09-28)**: Copying a
scope description into a profile draft is not a persisted authorization-
relationship review. Rename that action/dialog explicitly. Provide a separate
relationship-review action using the existing preview/review API, exact
relationship/allocation revisions, server permission checks, rationale and
explicit confirmation. Never flip the table state locally or assume provider
authorization covers a mission system. A separate-boundary determination can
be recorded by permitted mission roles; covered-scope determinations retain
AO and source-evidence gates. Refresh from canonical records after a confirmed
review; unresolved/stale sources must remain review required.

**Provider hosting card/drawer refinement (2026-09-28)**: Follow the user's
attached Provider hosting card. Replace detailed available-CSP rows on the page
with one **Choose provider hosting** CTA. Show associated scopes in a compact
table; when none are associated, show the explicit empty state and available
scope count. The CTA opens a right-side selection drawer with readable
provider/offering names (for example Flankspeed · Azure IL5 · Shared services).
Technical allocation/source IDs and long scope references belong under
**Details**, not in the main card or selection label. Keep association explicit,
authorized, revision-checked and idempotent.

Group the Environment sidebar into **Documentation & review**, leaving
**Contributes to your SSP’s environment and hosting section.** visible.
The user's subsequent clarification keeps this section **expanded**, not in a
collapsed disclosure. Place
Network Zones and Recovery immediately under hosting, preserving the visible
ATO preparation guidance and existing field data. Do not change other system
pages' sidebar behavior.

**Environment preparation visibility (2026-09-28)**: Network zones/deployment
locations and recovery/operating details are ATO preparation inputs, not hidden
advanced options. Expand both sections by default and provide visible
recorded/unrecorded field counts with brief SSP/contingency guidance. These are
local draft-completeness indicators, not an authorization verdict or new backend
validation rule. Applicability, adequacy and provider/customer responsibility
remain subject to the system's baseline and reviewer determination. Explain
provider-managed/non-applicable details in the deployment description instead
of fabricating values; preserve existing payload fields and review gates.

**Direct scope association refinement (2026-09-28)**: Available provider
allocations must be visible on Environment & hosting, with an explicit
**Associate CSP scope** action rather than an ambiguous Open link or collapsed
list. Confirm the selected allocation, system and current revision in a focused
dialog, then use the existing authorized/idempotent association API. Preserve
permission flags and reject stale allocation context. Successful association
refreshes the associated-scope table, without saving the environment draft,
adopting capabilities or accepting duties. Explain the sequence: CSP allocation,
mission association during system definition, relationship review, capability/
duty review, then document preparation. Keep the mock's two-column fields,
compact scope rows and quiet support blocks; no mock facts or automatic writes.

**Exact Environment & hosting correction (2026-09-27)**: Match the requested
Environment mock specifically: Review hosting scope as the page primary action;
hosting model and cloud environment together; deployment description followed
by a compact provider-scope table. Consolidate the duplicate hosting/capability
summaries into one component. Distinguish recorded associations from available
allocations and never label an unassociated allocation as used by the system.
Open inspects the exact allocation/relationship; source-review and association
links must reach the real scoped workflow, without implying capability adoption
or responsibility acceptance. Keep Save Draft separate, preserve existing scalar
keys, all supported clouds and legacy values, and retain explicit review and
permission fences. Detail/confirmation operations use dialogs; raw identifiers
and provenance belong in inspection rather than dominating the main table.

**Four-document preview workspace (2026-09-27)**: Document previews provides
SSP, SAP, SAR and POA&M choices with URL-backed selection and the same complete,
formal read-only presentation. Each choice loads its own authoritative document
model, not a relabeled SSP or browser-assembled export. SAP/SAR absence is an
explicit unavailable-document state with a source-workflow link, not an invented
report; an empty POA&M register is clearly distinguished from authorization.
Selecting or refreshing previews must not generate/finalize assessments, mutate
records, issue decisions, or submit packages. Retained SSP preview behavior and
approved-export gates remain unchanged.

**Formal SSP presentation (2026-09-27)**: Use a FedRAMP-inspired document
presentation: cover, document-control facts, recorded revision history, table of
contents, numbered sections and restrained paper-style typography/tables. Use
only generated system metadata; do not invent versions, authors, signatures,
classification markings or authorization. Clearly identify the result as a SPIN
working preview, not an official FedRAMP package. The section organization follows
the actual generated OSCAL data and is not a claim of exact template conformity.
All generated values remain accessible in the readable document.

**Complete SSP preview and Users cleanup (2026-09-27)**: The readable SSP view
must expose every section/value in the generated document, including all profile
contributions, metadata, boundaries, implementation/inventory, controls,
provider references and back matter. A `contribution` query highlights a source;
it must never filter other SSP data away. Keep the unchanged OSCAL source view
and working/approved-source safeguards. Do not fabricate data absent from the
generated document; surface source gaps.

Remove the inline Users access-context form/review block below its table.
System-wide context remains separately editable/reviewable in a focused dialog,
reachable from the ownership sidebar. Cancelling that dialog discards its scalar
edits without discarding category drafts. Preserve existing persistence,
permissions and independent per-category review.

**Contribution preview correction (2026-09-27)**: A fresh request alone is not
sufficient. The preview advertised as current working data must include saved
profile and category drafts, marked as unreviewed working sources, and its
readable view must show the selected page's contribution rather than only the
registration description. Profile links carry the requested section; source
mapping links return to that section. Unsaved browser edits are not document
sources. Retained draft previews remain drafts and must not bypass approved
SSP/export or package-readiness gates. Ordinary exports continue to use immutable
approved context/category sources. Verify saved edits change the actual OSCAL
preview and readable content while approved export content remains unchanged.

**Users-specific correction (2026-09-27)**: `pages.html#Users` requires
**Add user category** as the single page-header primary action, not Save Draft.
Use a compact category table with meaningful category/context, count, access
and sensitivity labels, and one **Open** action per row for readers and editors.
Open inspects the selected record without changing it; edit/remove/reorder are
explicit permitted actions in its dialog. Named mission populations are valid
category names, not limited to a hard-coded taxonomy. Counts are nonnegative
integers; missing counts are not zero. Do not invent per-row approval states
or treat documented categories as account provisioning.

Keep **Apply to draft** and **Save Draft** distinct and visible: row changes are
local until the existing section save persists scalars and rows together.
Retain pending-write locks, failed edits, saved-review boundaries, actual
read-only access, and exact system context across all six tabs. Do not populate
the live table with mock populations or fabricated approval badges.

**Persistence and individual review correction (2026-09-27, user rehearsal)**:
Real Users PUT returned 200 while ignoring child rows. Save must persist child
rows and scalar content atomically, return canonical rows, and survive a fresh
API/database read. Omitted child arrays preserve rows; explicit empty arrays
express deletion, subject to authorization/review guards. Do not accept mock-only
save verification as database evidence.

The user's subsequent "reviews are on individual users" instruction is applied
to each **user-category row** on this page, not to directory accounts. Individual
submission/approval/revision/withdrawal must be persisted server-side with row
revision fences, role/tenant checks and retained approved sources. One row's
approval cannot approve its siblings. Previous whole-section Users review UI
must not masquerade as individual review or provide a bypass. Context fields,
approved document projection and readiness must remain consistent with this
row-level governance. No client-only approval flags or inferred status badges.

**System definition correction (2026-09-27)**: Follow `pages.html#Mission` and
the six System definition destinations: heading/action before section navigation,
then actual governance status, source-backed record content and three slim support
strips. Do not prepend the generic contribution/navigation block or duplicate
Environment as a second main tab. Keep hosting association and component inventory
as explicit task actions. Mission identity/owner are read-only canonical records;
profile save must not silently rename the system or assign a role. Preserve the
existing Mission Statement and Business Purpose fields rather than relabeling
business purpose as an unsupported impact field.

Save draft must save current scalar/child edits; submission must not silently
submit an older saved draft. Report skipped submit/withdraw results as errors,
not success. Focused row editing/removal and revision/withdrawal confirmation use
dialogs. Preview contribution opens the real document preview; package readiness
preserves InitialSubmission. Retain unknown source fields, approved content,
server authorization, review locks and errors. Verify all six tab targets,
desktop/mobile layout, failed saves, pending writes and review transitions.
Use **Add port / service** for a PPS draft row and **Add interconnection** for
an external-system relationship; the mock's generic **Add connection** label
must not conceal that distinction.

**System Readiness correction (2026-09-27)**: The root system overview owns its
header, Readiness/monitoring navigation, status strip, next-action card and
three support strips in the order shown by `pages.html#Readiness`. Do not
render an extra generic task-navigation block above that header. Readiness is
unknown until explicitly checked; show server counts rather than mock counts.
After checking, prioritize actionable server findings and route Review actions
to their actual task. Continue preparation and package links preserve
InitialSubmission through document validation and generation. Preview
contribution opens the actual system document preview. View mode uses URL
state so direct links/back navigation work; no navigation issues an ATO.

**Organization/system navigation correction (2026-09-27)**: Use the
`system-overview-mock/pages.html` navigation: Portfolio, Systems, Security
Capabilities and Knowledge Base at the top; the attached sidebar refinement
shows only the eight canonical section groups with the selected system's name
above them. Keep the thirty individual destinations in section navigation and
the grouped mobile page selector, not expanded underneath sidebar headings.
The subsequent user refinement adds one meaningful icon to each of the eight
section links; do not restore the expanded thirty-link sidebar. The SPIN logo
in the organization/system top bar is enlarged to 42px while retaining its
aspect ratio and existing account controls. Match link spacing,
purple active state, 220px desktop (including icons) / 180px intermediate sidebar, and a grouped
mobile page selector below 650px. Section navigation scrolls horizontally
instead of wrapping. Preserve scoped URLs, legacy route support, keyboard
access, account/help/chat controls and all server permission gates.

**Provider administration correction (2026-09-27)**: Follow `#administration`
with Provider team and Connections panels plus an authority/separate-workspaces
sidebar. Only show provider identities/roles actually supplied by the API; the
current session is not a full provider roster and directory search results are
not role assignments. Find user opens real configured Entra lookup in a dialog;
View role inspects actual server-granted authority. Azure scope metadata must
not be labeled a verified connection. Reference configuration selects the
owning offering and opens its real reference workflow. Preserve initial
organization administrator enrollment as a separate advanced operation, not
provider-team management. No mock identities, fabricated connection health, or
automatic grants.

**Changes workspace correction (2026-09-27)**: The Changes landing page is a
purpose-led queue with summary metrics and Change / Source / Impact / State
columns, not a raw list of every historical review. Accepted/rejected review
history remains accessible without being mislabeled as pending merely because
its original preview expired. Review, evidence and monitoring actions retain
the selected offering and open the exact record or explicit read-only inspector.
Selected reviews stay in the Changes navigation context. Provider decisions,
customer actions and recorded-source availability remain distinct; no live
collection health or semantic before/after diff is fabricated.

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
| Components & system scope | MO/I | `S/boundaries` + source inventory using `security-capabilities/inventory` | Governed inclusion/operator/rationale draft and review; separately labeled immediate source placement changes | Reviewed SSP scope/inventory | 4A; V,C,L,H |
| Ports & interconnections | MO/I | `S/profile/PortsProtocolsAndServices`; connect interconnections | Reviewed structured ports/agreement records; add/review | SSP network/interface register | 1C,4A; V,C,L |
| Categorization & baseline | I | `S/baseline`; replace UI, keep rules | Information impact and selected control set; review/select | SSP/control applicability | 4A; V,C,L |
| Applied capabilities | MO/I | `S/security-capabilities`; reuse selected-system service | Organization placements and provider adoptions; add/apply/remove safely | SSP implementation | 2B,4A; V,C,L,H |
| Responsibilities | I; MO reads | `S/inheritance/subscriptions`, inheritance summary retained | Exact duties/provenance/revisions; confirm allocations | CRM/control responsibilities | 2B,4A; V,C,L,H |

**Canonical CSP adoption path (2026-09-28)**: Provider hosting owns the
customer-side selection and adoption of exact CSP-published releases. Applied
capabilities remains the system inventory and review surface for both provider
adoptions and organization-managed capabilities, but its generic library setup
offers only organization-managed records. A user who needs to add CSP hosting
or a CSP capability is routed to Provider hosting; the generic setup must not
create an unpinned provider subscription that appears applied in one surface
and “Not applied” in the provider relationship. Responsibility confirmation
remains a separate ISSM/ISSO action after adoption.
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
