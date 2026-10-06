# System Security Capabilities: local acceptance

This guide covers [azurenoops/spin_agent#1037](https://github.com/azurenoops/spin_agent/issues/1037).
The implementation is available locally; **user acceptance remains open**.
Applying a capability does not confirm inherited responsibilities, approve a
narrative, or issue an authorization decision.

## Start here

- Dashboard: [localhost:5173](http://localhost:5173).
- [Open the SPIN Demo System capability workspace](http://localhost:5173/workspaces/organizations/ef3a19e6-858f-48f8-ab35-d0ab88b54d39/systems/09d6774b-e8a1-48db-b71f-5873e27163c4/security-capabilities).
- Use an identity authorized for the selected organization and system. The live
  read-only verification used the existing development ISSM identity.
- Keep the active organization, selected system, effective roles and breadcrumbs
  visible. Changing a role label in browser settings does not grant server access.

The current demo system has **no applied capabilities, eligible library
capabilities, or authorization boundaries**. Both list views and the empty
library are real successful API responses, not illustrative mock records.
The desktop/mobile live checks made no capability-domain writes.

Populated acceptance requires explicitly authorized test records in the existing
organization/provider libraries. Do not publish retained provider package
proposals, reanalyze packages, or reset data merely to populate these screens.
Use an isolated test organization for the write scenarios below.

### Environment, hosting and assessment configuration

#### Focused offering review at port 4196

Open [Environment & hosting](http://127.0.0.1:4196/workspaces/organizations/ef3a19e6-858f-48f8-ab35-d0ab88b54d39/systems/09d6774b-e8a1-48db-b71f-5873e27163c4/profile/EnvironmentAndDeployment)
and choose a provider row's **Review**. The initial drawer contains the readable
offering/provider/system, current review states, and one contextual action.
Its action uses the canonical relationship and complete paged applicability
records with explicit permissions, never the offering name or an assumed setup
sequence. Published applicable is not system adoption; review recorded is not an
authorization decision. A captured scope projection flag is retained separately
in technical details and does not override the canonical relationship task.

Expand **What's included** for the actual source count and named capabilities.
Each capability discloses its captured description and provider/shared/customer
control allocations. Lists over ten are paged. Missing duty entries remain in
the count and are explicitly unavailable, not resolved or not applicable.
Complete operational duty text and private supporting source content are not
exposed by this projection; missing content is not inferred.

Expand **Source details and prerequisites** to inspect pinned scope/release
identifiers, hashes, revisions, exact hosting exclusions and grouped prerequisites.
Known blocker codes use plain language; raw diagnostics remain separate.
Current release differences never replace the captured selection silently.
Relationship editing, optional subscription links and previewed removal remain
available in secondary disclosures with their existing permissions and
confirmation/version checks.

In-app workflow links stay in the same tab and retain system/offering/assignment/
hosting-release context. The receiving task verifies that context and fails
explicitly if it changed. Responsibility review still shows the full system
baseline; origin context does not preselect adoption or accept a control.
Save the independent Environment draft before intentionally leaving to another
task. Canceling navigation or close preserves input; canceled navigation restores
focus to its initiating drawer link. The existing independent profile guard is
also retained.

Manual acceptance: at 1440px and 390px, inspect the initially collapsed drawer,
its current next action, all source details and optional actions. Use Tab,
Shift+Tab and Escape. On the shared system, inspect/cancel only: do not connect,
save, prepare/record a review, confirm duties, preview removal, attach a
subscription, check access or enable monitoring. In an isolated authorized test
system, also exercise 0/1/many capabilities, missing/changed source, read-only
permissions, blocked/expired previews, failed saves and stale-source rejection.
Keep text after rejected writes and obtain separate human review.

Fresh automated verification: 257 focused frontend tests, 20 Chromium scenarios,
158 backend/downstream unit tests and 13 scoped HTTP checks pass. TypeScript and
production build pass with existing warnings. Live inspection verified actual
5/8 capability counts, source-preserving navigation and zero feature writes;
retained entity/profile data is unchanged. Optional shell onboarding GETs still
return 403. These checks do not assert live SSP/eMASS export, submission or an AO
decision; user acceptance remains open.

Focused register acceptance (port 4196, October 6): the Environment draft form
and top Save Draft remain independent. Provider rows have one **Review** action:
inspect pinned published scope/duties and applicability, edit the authorization
relationship through its existing preview workflow, or open the responsibility
matrix separately. Missing prerequisites and confirmation permissions come from
the current server source. Expand optional subscription links and technical
metadata when needed. A published duty is not an accepted system duty.

Empty System subscriptions shows **No subscriptions attached.** and one primary
**Attach subscription**. A provider scope can exist without a subscription.
Populated rows have one **Manage** action for source details, scope review,
assessment access, independent monitoring and previewed detachment. In an
isolated authorized test system, verify stale/save errors keep inputs and Cancel
or Keep editing preserves drafts; shared demo inspection must not save, attach,
confirm, check access or reconcile. Test at 1440px and 390px with Tab/Shift+Tab
and Escape; closing an unchanged panel restores its invoking action.
These register changes do not verify downstream SSP/eMASS export or submission.

October 6 focused verification: 153 frontend tests and eight Environment browser
scenarios passed at port 4196, with strict TypeScript and production build.
Native live desktop/mobile inspection verified two retained provider scopes and
empty subscriptions without feature writes or page errors. Local rationale
input was discarded, not recorded. Environment/Profile response bytes and
relationship entity data remained unchanged; transport timestamp/timing metadata
was not byte-stable. Two shell onboarding GETs returned 403 and were not bypassed.
Shared API, Dashboard, Chat, Redis and SQL containers were unchanged. Manual
acceptance and SSP/eMASS export verification remain open. Broader Data-suite
failures previously reported were not rerun or diagnosed by this cleanup.

1. Confirm the system sidebar no longer has **Provider relationships**.
2. Open **Environment**. Hosting model and Environment description come first,
   followed by saved hosting associations and applied security capabilities.
   Azure scan forms and overall Profile Completeness are not above the form.
   The organization, selected system and roles remain visible; the right panel
   defaults collapsed and can be reopened. Completeness is on the system overview.
3. Confirm deployment description, network zones, locations, availability,
   recovery, RTO/RPO, maintenance and operating systems are visible together in
   one Deployment description form without expanding sections. Change a short
   description in an isolated test system, use the top Save Draft, reload and
   verify all fields, custom JSON and legacy hosting values are retained.
   Save your draft before leaving to start another task.
4. For an associated scope, choose **Use these hosting details**. Inspect the old
   and proposed descriptions in the dialog; Use in draft is disabled until the
   acknowledgement is checked. Applying the suggestion does not save. Network,
   availability, recovery and operating values are not inferred or overwritten.
5. Choose **CSP-hosted** (or Hybrid) and save. Use **Associate hosting &
   capabilities**. The system is locked to this route: select provider, select
   an existing hosting scope, select applicable capabilities, review proposed
   duties and explicitly save the associations. Already-associated hosting is
   retained. An association can be saved without subscriptions if the user has
   hosting permission but no subscription authority; the UI explains this.
6. After saving subscriptions, follow each **Confirm responsibilities** link.
   The real Coverage & duties view requires current per-control allocations,
   checks and notes from an authorized reviewer. Merely applying the capability
   or checking the association confirmation does not accept duties or issue an ATO.
7. For an uncertain save, keep the page open and use **Retry incomplete
   operations** with the original request. For a definite stale-context rejection,
   use **Review current choices**, refresh the selection and confirm again.
   This older hosting association task retains uncertain-operation keys only
   while the page stays open; it is not the durable Add from library setup engine.
8. Follow **Assessments: configure Azure assessment**. Configuration belongs here,
   not on Environment. A denied account sees an access explanation, not a Retry
   button. Authorized users retain configuration save/detach and readiness checks.
   The old Environment URL with `#azure-assessment-environment` must redirect here.
9. Organization-managed and on-premises systems do not require CSP setup. Existing
   associations stay visible even if the descriptive hosting model changes.
   Use Security Capabilities > Add from library for local organization capabilities.
   Existing provider-relationship bookmarks still work; no records are removed.

The development ISSM identity previously returned 403 from the assessment
attachment/readiness endpoints, reproduced with the prior Dashboard image.
The redesign relocates and explains this denial; it does not change backend
permissions. The live demo has no hosting allocations or capabilities, so
populated association and prefill writes use synthetic API fixtures, not retained
business data. Confirm permissions and allocated records before manual write tests.

## Test prerequisites

For full acceptance, arrange the following through the existing authorized
workflows, or use the synthetic browser suite described below:

- Two systems in one organization, plus a second inaccessible organization.
- At least two boundaries on the first system.
- Local capabilities, including a Person contributor; a directly assigned
  component with no capability link; and a contributor shared by capabilities.
- An eligible published provider capability available to both systems, with
  multiple contributors, and an organization capability usable as local support.
- A configured control baseline, mapped controls, protected evidence, and
  independently versioned Policy and Technical narratives.
- Separate system-management, assigned responsibility-review and narrative-review
  identities, including a reviewer who is not the proposal author.

Missing provider access, baselines, boundaries, source evidence or reviewer
authority should produce explicit explanations, not fabricated availability.

## 1. Applied lists and existing inventory

1. Open **Security Capabilities** from the selected system navigation. Confirm
   that the old separate Capabilities and Components entries are replaced.
2. Switch between **By capability** and **By component**. Available-but-unapplied
   library capabilities must not appear in the applied list.
3. Search, change the supported type/source/boundary filters, sort, and paginate.
   Totals must reflect all matching selected-system records, not just this page.
   Refresh and use browser Back; the URL should preserve the chosen view/filter.
4. Confirm that shared contributors appear once and directly assigned components
   remain visible without a capability link. Multiple and excluded boundary
   relationships must retain their actual states.
5. Open **Manage inventory**. Existing inventory creation, import and discovery
   remain in their original workflow. Existing bookmarked Capabilities/Components
   links should still reach a compatible system view.
6. Switch systems. The list, counts, placements and pending setup context must
   belong only to the newly selected system.

For an empty system, expect an explanation and **Add from library** when
authorized. An unavailable service must instead show an error and retry action;
it must not masquerade as an empty list.

## 2. Component drawer and placements

The redesigned **Component details** panel follows the supplied
`component-review-simple.html` reference (provided outside this worktree).
The three sections are **Overview**, **System scope** and **Evidence**.
Service-use scope uses the existing **System design** draft and independent
review. It is not an immediate infrastructure assignment or a new approval
lifecycle. Review may require resolving the rest of the design's blocking gaps.

1. From **By component**, open a component with the keyboard. Check the readable
   name, subtype, **Managed by** owner and synthetic demonstration label, when
   supplied by the source. **Thing**, identifiers and revision metadata belong
   under **Provider source and technical details**, not the header.
2. Without a saved service-use record, expect **System scope not recorded** and
   **Review system scope**. An existing immediate boundary assignment must not
   be mislabelled reviewed scope.
3. Choose **Used by this system**, select an existing **System area supported**
   and enter **How it is used**. **Save scope draft** must confirm the server
   response before announcing success. Close/reopen to verify persistence.
   The provider source and actual infrastructure placements remain unchanged.
4. Follow **Review scope draft** into **System definition > System design**.
   Inspect **Component service-use scope**, source versions and working changes.
   Submit the design and use an independent assigned ISSM to review it. A draft
   cannot silently replace the reviewed baseline. An approved design requires
   **Start working revision** before editing; under-review designs require
   authorized withdrawal. An unresolved scope decision remains a blocking gap.
5. Repeat with **Not used by this system** and **Needs confirmation**. Draft
   exclusions do not change the accepted baseline. Reviewed exclusions do not
   count toward in-scope capability applicability. Included reviewed use is
   labelled **service use**, not infrastructure containment.
6. In **Prepare a first pass**, choose the relevant mapped control and an
   authorized provider scope. Published facts and responsibility splits come
   from that scope's pinned source. No scope uses system records and attached
   evidence, without asserting inheritance. Inspect source versions and unresolved
   questions. Prepare a suggestion, then explicitly **Use proposed wording**.
   Existing wording requires a replacement acknowledgement. Correct it and save
   scope; the originating proposal revision/hash and human correction are retained.
   Preparation does not save scope or accept responsibility.
7. RPO and RTO are shown only when explicit objectives are present in the
   selected provider source. **Maximum data loss target (RPO)** and **Time to
   restore target (RTO)** always say **Results not verified**. Do not expect
   24-hour / 8-hour values without a supporting selected source.
8. Open **Evidence**. Inspect actual control-linked files; presence is not
   sufficiency. Backup tasks separately identify protected workloads,
   configuration/retention, restore results and responsibility review. Missing
   evidence must say unavailable. Follow the existing evidence and responsibility
   links rather than interpreting association as acceptance.
9. Simulate a rejected or disconnected save. Confirm no success announcement,
   entered wording remains, and **Reload saved scope** preserves corrections
   while loading current revisions. Source changes must identify affected review
   context without overwriting the baseline. Test a read-only identity as well.
   Draft permissions come from System design editing authority, not permission
   to edit provider source or immediately assign infrastructure. An unavailable
   source cannot be newly included; its historical records remain visible.
10. If infrastructure placement itself needs correction, expand **Existing
    infrastructure placements** in **System scope**. This explicitly separate
    workflow changes canonical assignments immediately. Assign an authorized area,
    refresh, then remove only that placement with its existing acknowledgement.
    Other areas, capabilities and source records remain. Local Person contributors
    still use system assignments; system-wide/legacy restrictions remain explicit.
11. Use arrow keys/Home/End on the component tabs, Tab/Shift+Tab in the modal,
    and Escape to close. Focus must return to the refreshed component opener.
    Check both themes at desktop and 390px without horizontal overflow.
12. Preview the working SSP/OSCAL contribution: service-use decisions, usage,
    area and source references must be marked draft/unapproved. Reviewed output
    must use the retained reviewed design, not a later scope draft. Final
    generation remains gated by existing source freshness/design approval checks.

### Local verification commands

From the repository root:

```bash
dotnet test tests/Ato.Copilot.Tests.Unit --filter \
  'FullyQualifiedName~SystemDesignServiceTests|FullyQualifiedName~SystemDesignExportTests|FullyQualifiedName~SystemSecurityCapabilitiesTests'
dotnet test tests/Ato.Copilot.Tests.Integration --filter \
  'FullyQualifiedName~SystemDesignHttpTests'
```

From `src/Ato.Copilot.Dashboard`:

```bash
npm test -- --run src/__tests__/workspaces/SystemComponentReview.test.tsx \
  src/__tests__/workspaces/ComponentFirstPass.test.tsx
npx tsc --noEmit
npm run dev -- --host 127.0.0.1 --port 5198 --strictPort
```

In another terminal in that directory, exercise the actual SPA with synthetic
HTTP fixtures (no retained business-data writes or model calls):

```bash
PLAYWRIGHT_BASE_URL=http://127.0.0.1:5198 npx playwright test \
  e2e/tests/component-review-simple.spec.ts --reporter=list
```

The scope endpoint is tenant/system-authorized and optimistic-version-fenced.
Scope entries are additive JSON in retained System design revisions: no new
table, no provider mutation and no automatic boundary assignment.
Existing SSP renderers receive working/approved service-use text and OSCAL
extension properties. Live AI output, a retained-data PDF/DOCX/eMASS package,
monitoring connectivity, actual recovery results and actual eMASS submission
are not established by the fixture suite; verify those separately before
claiming end-to-end submission readiness. Local user acceptance remains open.

For the original placement workflow, also verify that you can press Escape
when no write is pending. Focus should return to its opener.

To exercise stale protection, have another authorized test actor change the
placement after the first actor loads it. The old request must fail explicitly;
refresh before reviewing another mutation. Do not retry old reviewed tokens
automatically.

## 3. Capability detail

### Implementation

Open an applied capability. Check source ownership/revision, distinct contributing
components, supporting organization capabilities and actual system placements.
Adding local support must not rewrite provider authorship. Missing placement
must read as unassigned, not an invented capability-wide boundary.

### Coverage & duties

1. Review each persisted control's provider coverage, remaining customer duties,
   allocation, review status, and confirmed versus available source revisions.
2. Use an assigned responsibility reviewer. Check **Provider coverage verified**
   and **Customer duties reviewed**, enter nonblank required review notes, and
   verify that confirmation remains disabled until all requirements are valid.
3. Confirm the selected control. Refresh and verify that the checks, notes and
   exact reviewed source remain persisted.
4. Change the test source/baseline/review revision between loading and confirming.
   Expect stale-review rejection, not confirmation of the newer unseen source.
5. A system manager without responsibility-review authority must not gain it
   from having completed capability setup.

### Evidence & narratives

1. Inspect evidence owner/source/state and open a protected reference as an
   authorized user. An inaccessible record or file must report failure.
2. Inspect Policy and Technical freshness independently. One being current must
   not make the other current.
3. Generate or open an eligible proposal. Approved content must remain active
   until an authorized independent reviewer accepts the exact current proposal.
4. Review the previous/proposed content, provenance and dependency findings.
   Acknowledge review before accepting. **Return for revision** also requires a
   nonblank note. Verify that the other narrative type and approved history remain.
5. Try an author self-approval, stale proposal, and reviewer-denied identity.
   Each must remain blocked with an explanation; responsibility authority alone
   must not authorize narrative acceptance.

## 4. Add from library

1. Choose **Add from library**. The target system must stay locked to the route.
2. Select eligible local and provider capabilities. Already-applied entries must
   be disabled. Move between library pages and confirm that selections persist.
3. Continue to applicability. Choose actual contributor/boundary placements and,
   for provider capabilities, optional supporting organization capabilities.
   Provider contributors require a real boundary; local Person contributors are
   system-wide only. Existing placements must be retained.
4. Continue to the final review. Inspect every planned subscription/link, support
   relationship, placement, control change, responsibility reconciliation and
   narrative notification. Existing relationships must be explicit no-ops.
5. Confirm that **Add to system** is disabled until the exact-plan
   acknowledgement is checked. Complete the operation.
6. Verify the saved outcome and the applied list. Responsibility confirmation,
   narrative acceptance and any authorization decision must remain separate.
7. Repeat with an organization-only system and local capabilities. No CSP or
   provider selection should be required.

## 5. Recovery, partial outcomes and concurrent retry

Use the synthetic fixture or a controlled failure in an isolated test
environment. Do not stop or damage the shared local database to simulate failure.

1. After preparation, keep the operation URL. Refresh and verify that the exact
   persisted plan and its original display labels return.
2. With a partial completion, inspect saved versus unfinished writes. Refresh
   again; completed writes must remain saved.
3. Acknowledge the recovered plan and choose **Retry unfinished changes**.
   Confirm no duplicate links, subscriptions, assignments or audit events.
4. For an uncertain response or operation already in progress, use
   **Refresh saved outcomes**, not a newly prepared duplicate operation.
5. Try concurrent completion from two tabs using the same operation/revision.
   One writer must own execution; the other must receive a conflict/in-progress
   result and recover the same outcomes.
6. Change source or system relationships before retrying. Expect stale rejection
   and a new explicit review; completed writes must not be silently rolled back.

Unsubmitted drafts are scoped to the actor, organization, system and browser tab
for 24 hours. They are not saved domain records. Server operations are the
authoritative recovery state. Cancelling navigation never undoes completed work.

## 6. Remove from this system

1. Open **Remove from this system** on an applied capability. Inspect the
   authorized impact preview and retained records before acknowledging it.
2. A local record uses **Unlink from this system**; a provider record uses
   **Unsubscribe from this system**. Confirm only the reviewed system operation.
3. Verify that shared library records, all component placements, unrelated
   assignments, the second system's subscription and approved historical
   narratives remain. Placement removal is a separate deliberate action.
4. Change a source or relationship after preview. The old removal must fail
   stale; **Refresh capability** and review a new preview.
5. For partial/uncertain removal, recover the same operation and retry only
   unfinished work. Cancelling the dialog must not claim saved work was undone.

## 7. Permissions, isolation and presentation

- Repeat denied actions as a read-only member and as a CSP Admin/support identity
  without customer RMF authority. Hidden/disabled controls are not the security
  boundary: direct API requests must also be denied.
- Request another tenant's system, component, operation, proposal or evidence.
  No cross-tenant details or writes should be disclosed.
- Test both desktop and narrow mobile layouts in light and dark themes. Keep
  the system context visible; wide tables should scroll internally, not make the
  whole document overflow.
- Confirm keyboard tab order, labeled controls, announced status/errors,
  dialog focus containment/restoration, and blocked actions with reasons.

## Verification and safe synthetic walkthrough

Actual validation results and remaining acceptance status are recorded in the
[feature tasks](../../specs/078-role-aware-workspaces/tasks.md#system-level-security-capabilities-1037).
The API bodies, error codes and persistence details are in the
[selected-system contract](../../specs/078-role-aware-workspaces/contracts/system-security-capabilities.md).
The [HTTP request collection](../../src/Ato.Copilot.Mcp/system-security-capabilities.http)
provides manual API examples; use `http://localhost:3002` as its `baseUrl` for
this Docker deployment and supply authorized test identifiers before writes.

Full regression sign-off is still open. The full Dashboard retains known
baseline failures; the broader backend integration/relational runs also failed
and encountered runtime restarts on the shared Docker instance. The application
and retained-data checks passed afterwards, but that is not a root-cause fix or a
green full-suite result. Do not run many SQL Server fixtures concurrently on this
shared instance; use bounded scheduling or an isolated test runner.

The populated browser suite uses intercepted synthetic API responses and
illustrative records. It does not populate the live database or demonstrate that
the demo system has those capabilities. With the Dashboard running, execute:

```bash
cd src/Ato.Copilot.Dashboard
PLAYWRIGHT_BASE_URL=http://localhost:5173 npx playwright test \
  e2e/tests/system-security-capabilities.spec.ts --project=chromium --headed
```

The backend tests separately verify persistence, permissions, revision conflicts,
retry/concurrency and preservation. Fixture success is not a substitute for
accepting authorized populated write flows against your own local test data.

Record the tested actor/system, scenario, observed result and any remaining
failure before accepting this delivery. Do not close the issue before that
manual acceptance, and do not push or publish external updates without approval.
