# SPIN workspace UI mock references

## Local commit verification — October 9, 2026

The approved commit includes application, documentation, tests and prototype
sources only. Generated reports, screenshots and unrelated agent/skill/prompt
changes remain excluded. Fresh verification passed 489 Vitest cases in 43 files,
52 Chromium offering/provider cases, `npx tsc --noEmit`, the production build,
the grounding-port guard and its six tests, and the standalone prototype
checkers (63 offering, 87 package and 400 seven-persona assertions).

Review reproduced stale accessible-name selectors in the reusable
`provider-offering-production.check.mjs`: it still expected the earlier scope,
release and inline mission layout. The checker now follows the current
Provider duties / Customer duties, paired release panel and mission handoff,
preserving exact source values, read-only inspection and before/after snapshots.
The synthetic browser cases verify these current interactions. The live
backend checker and native exports have not been rerun for this commit.
React act/local connection warnings and the reported build warnings remain;
successful automation is not manual user acceptance.

### Screenshot-aligned presentation revision — October 9

Restyle only the isolated persona prototype to the supplied SPIN provider
overview: a 62px white header, 168px white navigation rail, lavender-gray canvas,
compact offering register and quiet right-hand task support. Embed the existing
Dashboard `src/assets/spin-logo.svg` as a data URI, with small purple navigation
accents, flat 8px panels and compact
tables throughout the nine-section workspace. PEO Digital remains the provider;
Flank Speed remains its portfolio, not a renamed provider. Demo identity, help
and reset controls stay compact and visibly simulated.

Keep the original seven-person identities, grants, immutable version handoffs,
readiness checks and `spin.offering-personas.v1` storage/reset semantics intact.
Navigation opens only the active identity's permitted records; administration
does not gain substantive authority. Add failing screenshot-structure/geometry
checks before changing presentation, then rerun the entire existing connected
story and accessibility/network checks at desktop and mobile sizes. This is
not an application, policy, database, deployment or original-mock modification.

The compact navigation rail opens Overview, Offerings, Mission systems, Changes,
Administration, My work and Audit history. Demo help / Knowledge Base explain
the isolated scope; administration is restricted for non-admin identities.
Mobile tables reflow into labeled records and the selected persona section
scrolls into view without moving page focus. Existing drawer Escape/dirty guards
and keyboard tab navigation remain intact.

Manual review: reload the same port-4199 URL below, open either a service name
or **Open offering**, switch between the seven demo personas, and inspect the
nine compact sections. Use Demo help for scope/role explanations; reset remains
optional and confirmation-gated. Browser tests use fresh isolated contexts and
do not clear an existing user's local state.

Verified October 9: screenshot-shell tests first failed on the original header
height; a subsequent mobile selected-tab test failed for the ISSO default.
After the presentation and tab-strip correction, the complete checker passes
**394 assertions**, retaining all 316 prior workflow/security/readiness checks.
Additional checks cover 1480px geometry, the offering register, functional
navigation/help, restricted administration, every persona landing/workspace at
1440/390px, visible selected sections, WCAG, zero failed/outbound requests and
zero CSP violations or JavaScript errors. Both JavaScript syntax checks pass.
The original and restyled desktop overview plus mobile and all seven persona
workspace captures were visually inspected; disposable captures were removed.
The existing Python listener (PID 75251) still returns HTTP 200 on port 4199.
Production code, original mocks and other running previews were not changed.
Manual user acceptance remains available at the same URL; it is not claimed
as completed.

## Ongoing offering management persona prototype — October 8

Overview and Offerings follow distinct prototype routes: Overview answers
“What needs my attention?” with live simulated persona work, blockers and
deadlines; Offerings is the searchable assigned-service register. My work is
the detailed personal queue. Each selected offering retains its own Overview.
No duplicate full catalog is rendered on provider Overview.
The revised prototype passed 400 assertions, retaining the connected
seven-person review/assessment/decision scenario, scoped access negatives,
desktop/mobile accessibility, searchable offering empty states and zero
outbound requests. Overview and Offerings have independent navigation/active
states; opening a service still uses the shared offering workspace. The absent
4199 preview was restored without replacing any other service.

Design intent (prototype only): offering-first daily work, not an ATO creation
wizard. The isolated `spin-offering-management-personas.html` and companion
JavaScript model seven different simulated people. SSM is the requested demo
label with an ISSM-backed alias explanation. A presentation-only identity
switcher selects fixed example grants; it is not authentication or a production
role grant. All records, evidence metadata, statuses and decisions are SIMULATED.

The shared nine-section workspace demonstrates provider/portfolio/offering/cloud
separation, independent customer organizations, explicit many-to-many boundary
links, mixed subscription resources and restricted summary grants. One local
permission/action handler checks scope, version and author/reviewer/assessor
independence. No API, database, cloud mutation, email or eMASS submission exists.
Local controls are educational, not a backend security boundary.

The connected story starts with a logging gap: SSM assigns a deadline; engineer
submits v1; SSM returns it; engineer submits v2; SSM accepts the exact version;
ISSO stages v2 and SSM independently approves the document; SCA records failure;
SSM reopens corrective work; engineer submits v3; SSM accepts; ISSO stages v3;
SSM approves; SCA independently reassesses; owner resolves an operational
dependency; ISSO prepares an exact change package; AO reviews and records a
simulated, scoped decision. Work acceptance never changes assessment, finding,
risk or authorization automatically. Historical versions remain inspectable.

Preview (no application build):

```bash
python3 -m http.server 4199 --bind 127.0.0.1 \
  --directory docs/design/workspace-ui-mocks
```

Open <http://127.0.0.1:4199/spin-offering-management-personas.html>.
The versioned namespace `spin.offering-personas.v1` persists only in this
browser/origin. Reset demo data requires confirmation. Storage corruption,
write failure and another-tab revision conflicts are explicit, not success.
Do not select sensitive files: upload retains only the displayed simulated
filename, never reads or sends file content.

Seven-person manual walkthrough:

- **SSM / Blair**: My Offerings → Flank Speed Azure → logging gap → assign work;
  review submitted versions, return with reasons, accept pins and approve staged
  documentation. Can also inspect the separately assigned collaboration offering.
- **Engineer / Alex**: My work → start assigned logging work → enter facts and
  evidence metadata → submit; respond to returns and corrective reopening.
  No collaboration offering, customer private evidence or review authority.
- **ISSO / Dana**: Controls & evidence → incorporate accepted material; Changes
  remediation → classify impacts and coordinate corrective work; prepare a
  contextual package only after fresh source/assessment/dependency checks.
- **SCA / Casey**: Security posture → finalize a scoped plan; independently fail
  the first approved implementation and later reassess fresh corrected evidence.
- **Owner / Ellis**: Lifecycle operations → resolve resource dependency with
  evidence and rationale; record change/renewal/retirement plans without changing
  recorded authorization or disposing a shared boundary.
- **AO / Finley**: Authorization coverage packages → review exact package pins,
  assessment, POA&M and residual risk; request clarification or record an explicit
  simulated ATO with rationale, conditions and future expiration.
- **Portal administrator / Gray**: setup/access queue → add sample customer or
  portfolio, create/redeem/revoke local invitations and administer limited scoped
  roles. Cannot self-grant security/AO roles, accept, assess, approve or authorize.

Reusable checks, using existing Dashboard Playwright/axe dependencies:

```bash
node docs/design/workspace-ui-mocks/spin-offering-management-personas.check.mjs
```

Checks target only the prototype: full UI-connected story, scope/action negatives,
version pins, persistence, filtering/pagination, validation, keyboard/dirty dialog
behavior, WCAG checks, 1440/390px layout and no outbound API requests.
Native SSP/SAP/SAR/eMASS files, production permissions, live cloud discovery,
real invitations and real authorization decisions are deliberately not implemented.
The prototype manifest preview is not a certified export or approved baseline.

Multi-role example: Alex also has a narrowly delegated review assignment for
Ellis's unrelated operational facts. Alex may accept that peer submission, but
the same normalized identity cannot accept or assess Alex's own logging
implementation. Switching presentation or passing an actor/role string does not
bypass that record/version-specific fence. This is still local demo logic, not
proof of production policy.

Prototype-only verification, October 8: the initial landing test failed with
the HTML absent (red). The reusable checker now passes **316 assertions**:
all seven identity default views at 1440/390px, the full connected UI story,
peer-review role separation, immutable returned v1 / accepted v2 / verified v3,
exact package and AO decision pins, missing owner gate, corrupt/quota/conflicting
storage, cancellation/block/overdue semantics, consent revocation, search and
pagination, dirty Escape/focus trapping, WCAG checks, zero browser JavaScript
errors and zero API/external requests or network writes. JavaScript syntax checks
also pass. Desktop/mobile overview and mobile admin drawer screenshots were
visually inspected; disposable captures were removed. This verifies the local
prototype only, not native documents, backend enforcement or deployment.

These are the original interactive HTML design prototypes, copied without modification. They embed their branding assets and have no external HTTP dependencies in their source. Open a file directly in a browser to inspect and interact with it. Read its HTML when implementation details or copy need inspection.

## Files

- [Focused source-package review prototype](provider-package-review-focused.html) —
  standalone interactive design for the inspected offering package. All 69
  extracted records and seven source entries are embedded from a read-only
  snapshot, including the recorded 16-record publication. Overview, record
  search/filter/pagination, source inspection, publication context and local
  review notes work without a live API connection. This is not a production
  implementation; prototype notes never change review or publication state.

- [Focused offering workspace prototype](provider-offering-workspace-focused.html) —
  standalone, responsive redesign of the locally inspected Azure IL5 · Shared
  services offering. Open directly in a browser; no application or API connection.
  Tabs, searchable capability details, scope, mission handoff, release comparison,
  and an explicitly simulated identity editor work in memory only.
  Reload discards simulated edits.
- [CSP catalog, authoring and publication](spin-csp-mocks.html)
- [CSP Organizations, organization detail, provisioning and support](spin-csp-organizations.html)
- [Organization library, capability detail and guided setup](spin-capability-mocks.html)

## Issue-to-screen index

The UI identifiers below match the pending issue package, not GitHub issue numbers. Each issue body also includes the repository path and its exact screen references.

| Draft | File and screen |
|---|---|
| UI-01 | [spin-csp-mocks.html](spin-csp-mocks.html) — CSP workspace strip and navigation; [spin-csp-organizations.html](spin-csp-organizations.html) — CSP Organizations navigation; [spin-capability-mocks.html](spin-capability-mocks.html) — organization workspace navigation |
| UI-02 | [spin-csp-mocks.html](spin-csp-mocks.html) — Provider catalog; switch capability/component views and expand the Azure offering |
| UI-03 | [spin-csp-mocks.html](spin-csp-mocks.html) — Capability authoring: Implementation, Coverage & duties, Subscribers; Add capability |
| UI-04 | [spin-csp-mocks.html](spin-csp-mocks.html) — Review & publish: diff, customer impact, publication gate and notification preview |
| UI-05 | [spin-csp-organizations.html](spin-csp-organizations.html) — Organizations landing page: search, filters, summary cards and row actions |
| UI-06 | [spin-csp-organizations.html](spin-csp-organizations.html) — View organization: Overview & systems, Provider subscriptions and Provider activity |
| UI-07 | [spin-csp-organizations.html](spin-csp-organizations.html) — Add organization: provisioning form |
| UI-08 | [spin-csp-organizations.html](spin-csp-organizations.html) — Enter support: reason, reference and acknowledgement |
| UI-09 | [spin-capability-mocks.html](spin-capability-mocks.html) — Security capabilities: By capability / By component and workspace/source variations |
| UI-10 | [spin-capability-mocks.html](spin-capability-mocks.html) — Capability detail: contributors, control coverage, responsibility and evidence/narratives |
| UI-11 | [spin-capability-mocks.html](spin-capability-mocks.html) — Guided setup: capability, components and final review |

## How Copilot should use these mocks

1. Open the referenced HTML and exercise the specified screen, tabs, forms and view toggles before changing the application.
2. Reuse the application’s existing SPIN assets and theme tokens. Match the mock’s hierarchy, layout and interactions while implementing through existing application components and services.
3. The issue’s vetted contracts and acceptance criteria take precedence over simulated prototype semantics. Do not copy fake metrics, hardcoded demo names, fictional revision numbers or simulated successful writes into production.
4. Keep lifecycle, onboarding, mapping review, publication, system responsibility and AO authorization distinct. Catalog availability is not blanket inheritance; local-only organization workflows remain valid.
5. Contact email is not membership; provider detail is not implicit support access. Confirmations and review actions require actual server permissions and persisted revisions.
6. These files do not establish that any backend feature exists. Do not embed them into the production app as a substitute for implementation. Test the real screens and provide a local user preview.

## Publication prerequisite

These assets and their index must be committed and published to a repository ref accessible to the implementing Copilot task before the issue batch is posted/assigned. Local-only file paths are insufficient. Include these exact files with the issue publication approval; verify the remote files exist and include their accessible repository links in the publication handoff. Issue bodies link to the immutable published revision of these files.

## Original-file integrity

- `spin-csp-mocks.html`: SHA-256 `e6be6065a127e3d14319f7a00cae8164d099b45b84450a74cec643fb9a824276`
- `spin-csp-organizations.html`: SHA-256 `9efc11fd7026b2e07477faa1ea9af994a00605090e0c8e13e7095d404b205b3f`
- `spin-capability-mocks.html`: SHA-256 `6e4ee7b358d7758d1e3e0bde9c20054c5e0f389ef95db9bd29aa3ac08c7ad8b8`

## Focused offering prototype provenance

### Focused package review prototype — October 7

Read-only inspection of offering `5417fe07-190d-4d47-9bae-a2238b6f30d4`,
package `bd4a422c-90fa-41fc-8734-cfa4ce448a28` found receipt revision 71,
ReadyForReview processing and Published publication state. All candidate and
source pages were captured: 69 records (53 Reviewed, 16 Published), seven
processed archive entries, and a recorded 16-record publication. Receipt and
review snapshots were unchanged during capture. The source itself
labels its content synthetic demonstration data; these are not authoritative
authorization or assessment claims.

The standalone mock uses these captured records, not a live connection. Its
next action inspects the already-published set instead of presenting a new
approval task. Search/type/status filters, pagination, citation/duty inspection,
source integrity details and local-only note/dirty-close interactions work.
Real record links retain offering, package, candidate and original page context.
Local notes never change source review state. Actual source downloads,
exclusions, retries, approvals and publication are not simulated as successful.

Verification passed 87 assertions at 1440px/390px both from the standalone HTML
and through the existing 4198 preview server: keyboard tabs and modal focus,
Escape/unsaved guards, focus restoration after list rerender, filtering,
pagination, captured counts, immutable snapshot, WCAG checks on all four views,
no page overflow, no runtime errors and zero API requests from the prototype.
Initial filter-label and test-selector issues were corrected before delivery.
No production application code or service was changed. Native exports were not
tested for this design-only deliverable.

Open `http://127.0.0.1:4198/provider-package-review-focused.html` or the HTML
directly. Reusable validation:

```bash
node docs/design/workspace-ui-mocks/provider-package-review-focused.check.mjs
```

### Mission use parity — October 7

Mission use now follows the mock's compact mission rows, paired hosting/
adoption facts, handoff dialog and Mission Owner guidance. The header counts
recorded hosting allocations from the actual overview; it does not manufacture
the mock's associated-system total. Customer names, relationship states,
adoption counts, selected releases and assigned scopes remain source-backed.
Unrecognized relationship values and missing/partial projection records are
explicit, and all original allocation/relationship workflows remain reachable.

View handoff uses the existing focus-managed dialog. It preserves exact
assignment/offering links, retained release metadata and scoped provenance.
Neither viewing it nor reading guidance completes reviews, adopts capabilities,
accepts inheritance or grants authorization.

Verification: two layout tests failed before implementation; 60 focused
frontend tests, 24 combined mission/release/cross-tab browser cases, strict
type-check and production build passed. The final test-typing correction passed
the 16-test release/mission suite and type-check again. Coverage of the combined
release/mission file was 172/173 statements and 72/76 branches.
Desktop/mobile mock comparisons covered association, adoption, unknown,
empty, partial, failed and stale reads, pagination, long names, keyboard trap,
Escape and trigger focus restoration. Real inspection showed one allocation
with zero adopted capabilities; identity and mission overview snapshots were
unchanged, with zero feature writes or JavaScript errors. The surrounding
organization-context GET still returns 403; existing build warnings remain.
No backend, native-export or authorization contract changed or was rerun.

Manual acceptance: open Mission use on 4196, compare the compact facts, open
View handoff, expand Allocated scope & provenance and press Escape. Test actual
allocation changes only in an isolated authorized system. Nothing was committed
or pushed, and prior work was preserved.

### Release & changes parity — October 7

The production release route now follows the mock's two-panel hierarchy:
published snapshots and working identity appear side by side, followed by a
working-change review panel. Workflow orientation links are not completion
states or publication gate results. Retained source, boundary-impact and hosting
comparison actions remain available in collapsed details.

All revisions, lifecycle and pending counts come from existing offering reads.
The identity comparison opens the existing editor and compares local edits to
the opening persisted identity; the current APIs do not provide a historical
published identity diff. Prototype discard/publish simulations are not copied.

Verification: two layout assertions failed before implementation; 65 focused
frontend tests and strict type-check/build passed. Eight new desktop/mobile
browser cases covered published, mixed/stale, empty and failed reads, exact
offering-context links, keyboard disclosure, accessibility and mock geometry.
Two retained browser cases verified missing scope handoff and identity
save/refresh. Release/mission-use unit statement and branch coverage was 100%.
Real desktop/mobile inspection used eight published capabilities at revision 3
and identity revision 11, with unchanged identity/overview snapshots, zero
feature writes and zero JavaScript errors. The surrounding organization-context
GET still returns 403. Existing build warnings remain; native exports were not
rerun because their contracts and implementation are unchanged.

Manual acceptance: open Release & changes on port 4196, inspect the paired
cards, expand Retained source & scope comparisons, then use Compare identity
edits and cancel. Use an isolated offering for save or publication testing.
No commit, push, backend rollout or live record mutation was performed.

### Approved production implementation scope

The October 7 capability-drawer correction targets the capability-name action
inside **Capabilities → Service implementations**. Match the focused mock's
readable source description, control chips, responsibility grouping and
progressive provenance, not its synthetic scope/duty claims. Source-duty values
are grouped verbatim; Shared is not a complete responsibility statement.
Offering identity, source revision and capability release remain distinct.
The mock has no implementation editor. Production retains the real canonical
workflow, retained-source review and scope links rather than adding simulated
saves. Drawer-only styles must not change provider review or Environment dialogs.
Manual review: open the local offering's `inherited-coverage?task=capabilities`
destination, activate a capability name, inspect source details, and use Escape
to return focus to the same name. Historical published payload verification is
not supported by this listing's current source/catalog contracts.

**Drawer verification:** The initial semantic parity run failed 2/16 before
production changes. Final combined offering/canonical editor/modal/source tests
passed **159/159**; modified OfferingCapabilities coverage was **97.76% lines,
92.26% branches, 90.90% functions**. Native Chromium passed **26/26**:
six new long-name/source/missing-duty/restricted-source desktop/mobile drawer
cases plus the 20 existing offering/scope fixtures (not 26 new drawer tests).
The first browser run exposed disconnected trigger focus after source retry;
tracking the exact replacement table trigger fixed that reproducible failure.
Fixtures exercise unsaved/error/success writes in isolation, never against live
records. Canonical editor behavior remains covered by existing tests.

The repeatable live checker is
`provider-offering-implementation.check.mjs` (set `OFFERING_ID`, run from the
repository root). Read-only actual/mock screenshots and verification JSON live
under Dashboard `focused-offering-live-results/implementation-*`.
Native 1440px captures show 520px drawer width, 14px type, 20px/24px header and
22px/24px body padding; 390px captures show 366px width and 18px padding.
These match the mock's measured geometry. Production intentionally extends the
mock with actual workflow actions, exact source duty groups and contract
limitations; the content is not pixel-identical synthetic copy.

Actual offering, candidate and release-reference GET snapshots were unchanged;
zero live feature writes/JavaScript errors were observed. Four existing
`/api/onboarding/organization-context` 403 responses remain visible. Final
typecheck/build passed, with existing Browserslist, SignalR annotation,
static/dynamic import and large-bundle warnings. A canonical unit test still
emits an act warning. Earlier identity-timeout root cause remains unverified.
The existing generated report and all other dirty/untracked work were left
untouched. Manual visual acceptance remains open.

Approval on October 7 targets the actual offering workspace, not an embedded
mock. The HTML contains six destinations (including Release & changes).
Production uses existing typed offering/source/review/impact/mission contracts.
Identity comparison can compare unsaved edits with the opening current identity;
the existing API does not expose a published offering-identity snapshot, so it
must not fabricate that comparison. Release comparisons hand off to existing
context-selected change-impact workflows. Retained candidate descriptions and
duty maps must never be presented as a reconstructed immutable release payload.
Live inspection verified Draft identity revision 11, eight published capabilities
at revision 3, 32 source documents, one open finding and one associated mission
with zero adopted capabilities. These observations are not production constants.

The identity query handoff is consumed on close/save. A successful save refreshes
the parent offering read; retaining `action=identity` would reopen a new editor
after that refresh unmounts the old one. Browser tests must prove the editor
closes, the returned revision is displayed and the same edit action can reopen.

Published rows can also carry retained package/candidate identities. Prefer that
existing candidate contract for source descriptions, duty maps, citations and
source revision; do not omit them merely because a canonical capability ID is
also present. The canonical release ID/revision still comes only from the
offering projection. Catalog-only rows disclose current catalog provenance.

Retained document totals come only from `sourceDocumentCount`. Package coverage
totals count entries, not necessarily documents; older projections missing the
document count must show “Not reported,” not a fabricated aggregate or zero.

### Production validation record — October 7, 2026

**Implementation files:** Dashboard
`features/provider-authorizations/AuthorizationsPage.tsx`, `OfferingOverview.tsx`,
`OfferingCapabilities.tsx`, `OfferingIdentity.tsx`,
`OfferingSectionNavigation.tsx`, `BoundaryPage.tsx`,
`providerPresentation.css`, `api.ts`, and the new `OfferingReleaseAndUse.tsx`.
The shared `system-design/UnsavedDesignGuard.tsx` adds an optional busy fence;
existing callers keep their prior behavior. Related unit tests, the new
`e2e/tests/focused-offering.spec.ts`, and this guide/spec 079 paper trail were
updated. Prior Provider Overview edits, the original dirty Playwright report,
the approved HTML/checker and untracked agent file were preserved.

**Contracts and verified flow:** no backend API schema, persistence model,
role policy or tenant filter changed. Offering reads use existing overview,
boundary, source, finding and mission projections. Source descriptions/duty
maps/citations/revisions use retained candidates where their identities are
available; catalog-only rows are labeled as current catalog context. Identity
PATCH uses `expectedRevision`, validates the returned offering/next revision,
then reloads the parent read. The existing server code requires ordinary
CSP.Admin access, checks the aggregate revision, preserves immutable boundary
and publication context, invalidates dependent review context and records
audited transactional writes. No live identity or feature write was executed
for this task. Successful and rejected writes exercised the real UI/request
path only through intercepted synthetic responses.

**Fresh validation:**

- Initial approved-layout TDD run: **8 failed / 30 passed** before implementation.
  Additional failing tests preceded identity comparison, pending navigation
  fencing, missing finding status, foreign projection/save responses, mixed
  pagination, source-contract reuse/change notices and missing document totals.
- Final focused Vitest run: **472 passed, 43 files**. Scoped V8 coverage including
  offering modules, transport and the shared guard: **95% lines/statements,
  88.7% branches, 85.6% functions**. This is not a fresh full repository/.NET run.
- `npx tsc --noEmit` and the production Dashboard build passed.
- Final presentation-only team prioritization/contact wrapping passed a
  **34-test** targeted recheck, fresh typecheck and the live desktop/mobile
  checker. The full build was not redundantly repeated for that minor refinement.
- **15 browser tests passed**: eleven focused offering cases and four retained
  Provider Overview cases. They cover 1440/390 widths, navigation keyboard
  controls, search/filter/sort/pagination, drawer trap/Escape/focus return,
  dirty-close cancellation/discard, conflict preservation, successful
  save/reopen, zero/one/many records, server-denied/foreign/stale/partial reads
  and an exact missing-boundary/hosting prerequisite handoff.
- The separate **actual production checker ran 63 fresh assertions** at
  1440/390, including retained source/finding review handoffs, recorded duties,
  boundary provenance, WCAG checks and drawer focus. These are independent of
  the old prototype-only checker even though their assertion counts coincide.
  Eight before/after API data snapshots were equal: identity, overview,
  capability/mission projection, boundaries, source versions, findings,
  authorization records and impact reviews. **Zero live feature writes,
  zero JavaScript exceptions, zero failed offering reads.** The unchanged live
  baseline remains Draft identity 11; eight published capabilities at revision
  3; 32 retained documents; one open finding; one associated mission, zero
  adopted capabilities.

**Actual warnings:** four shell reads to
`/api/onboarding/organization-context` returned **403** outside the offering
surface. They are reported explicitly by the live checker, not hidden or
changed by this task. The successful unit run prints React `act()` and local
`ECONNREFUSED` warnings from untouched hosting/impact tests, plus stale
Browserslist data. An unchanged decision lifecycle test failed once during
concurrent validation; its isolated 27-test run and the final full focused run
passed without changing that test or suppressing the failure. Build warnings:
SignalR PURE annotation placement, mixed static/dynamic
`preImpersonationUrl.ts` import, stale Browserslist data and a large bundle.

**Implemented versus unavailable:** all six destinations and existing
source/review/publication/scope/allocation/evidence handoffs remain reachable.
Comparison of unsaved identity to the opening persisted identity is implemented.
Historical published offering-identity and immutable published duty payloads are
not supplied by the listing APIs and are explicitly unavailable; working/source
review and native context-selected change-impact workflows remain separate.
No new publication, adoption or authorization lifecycle was invented. Native
SSP/eMASS exports were not regenerated; downstream export mapping, external eMASS
profile compatibility, actual submission and AO/cATO decisions are unverified.

**Local review:** follow
[`specs/079-provider-system-workflow-consolidation/quickstart.md`](../../../specs/079-provider-system-workflow-consolidation/quickstart.md).
Meaningful live desktop/mobile screenshots and final validation logs are in
`src/Ato.Copilot.Dashboard/focused-offering-live-results/`. User manual acceptance
is pending. Neither the 4196 source preview nor the 4198 mock preview was stopped.

The new focused prototype was based on browser inspection of
`http://127.0.0.1:4196/workspaces/csp/authorizations/offerings/5417fe07-190d-4d47-9bae-a2238b6f30d4`
on October 7, 2026, plus read-only offering, overview, boundary overview,
boundary revision and retained candidate endpoints. The original screen shows
eight published capabilities at revision 3, one associated mission, 32 retained
source documents, one open finding, and no pending capability proposals. The
offering identity itself is Draft at revision 11; this must not be confused with
the published capability snapshots. The linked mission has zero adopted
capabilities. This offering's own description explicitly identifies its data as
synthetic demonstration data, not an IL5 approval or an actual authorization.

Design intent: replace the ambiguous “Complete the next release” checklist
(despite no pending proposals) with a contextual next task, compact release
context, a searchable capability list, and progressive details. Counts and names
are captured observations, not live data. Candidate descriptions and control-duty
maps are retained-source context, not a reconstructed immutable release payload.
The published release is never edited by the prototype. Scope confirmation,
approval, publication, mission acceptance and AO authorization are not simulated
as completed outcomes. Missing evidence metadata and release comparisons are
explicitly unavailable.

Manual preview: open the HTML file; try every tab, search `AU-2`, filter out
published rows, sort descending, paginate, open a capability and press Escape,
then edit the service team, save a simulated draft, compare it and discard it.
Repeat at a 390px viewport. No production changes, spec changes, backend writes,
commit or publication are required to use this mock.

Automated mock-only checks (existing Dashboard Playwright and axe dependencies
required), from the repository root:

```bash
node docs/design/workspace-ui-mocks/provider-offering-workspace-focused.check.mjs
```

This opens the HTML directly, without a server or authenticated session, and
checks desktop/mobile layout, tabs, search/filter/sort/pagination, dialogs and
focus, simulated edit/save/discard behavior, WCAG axe rules, JavaScript errors
and absence of outbound API requests or writes. It does not test production
application behavior or backend exports.

### Scope & duties production comparison

The production `/boundary` tab follows the approved single Service scope card
and Provider duties / Customer duties group. Recorded services are tiles;
exclusions are a note. Complete statements, resource identifiers, hashes,
citations and rationale remain in Hosting identity & source provenance. Prior
versions and linked records remain under Supporting records, versions & workflow.

From the repository root, with the existing source preview on 4196 and mock on
4198, run the read-only native-browser comparison:

```bash
OFFERING_ID=5417fe07-190d-4d47-9bae-a2238b6f30d4 \
  node docs/design/workspace-ui-mocks/provider-offering-scope.check.mjs
```

This verifies the configured development CSP.Admin identity before sign-in,
blocks feature mutations, compares relevant GET snapshots, tests provenance,
editor cancellation/focus and accessibility, and saves mock/production captures
at 1440 and 390 pixels in the Dashboard's `focused-offering-live-results/`.
Native capture height fits the application's scrolling main container so the
customer-duty card is not clipped out of screenshot evidence. The real provider
navigation rail, actual boundary editor, excerpt notice and working-duty warning
are intentional production differences. Current boundary duties are not
reconstructed immutable release duties, mission acceptance, or authorization.
