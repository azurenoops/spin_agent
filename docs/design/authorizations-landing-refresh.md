# Authorizations landing refresh

Scope: presentation of the existing offering list only. Replace the dominant warning and sparse half-width cards with a quiet context note, focused search area, structured cards and contextual setup links. Keep the shared workspace shell.

Data: use existing Offering fields only. Lifecycle is labeled as offering lifecycle, never authorization standing. Boundary and hosting statuses represent recorded references only. Counts come from the paged API total, not page-level calculations. No telemetry, findings or authorization metrics are fabricated.

Preserve creation and import paths. Add clear-search recovery for no matches and distinct empty-catalog guidance. Provide keyboard focus, light/dark styles, narrow-screen wrapping and scoped links. Before implementation, add a failing behavior test for new setup links and empty-search recovery; run the existing authorization tests and Dashboard type check. Provide a local screenshot and manual verification steps. User manual testing remains pending until performed.

No backend, migration, authority, publication or tenant-isolation changes. Rollback only this presentation delta; preserve other working-tree changes.

## Unified package import CTAs - September 24, 2026

The latest user-approved behavior supersedes the separate scoped-upload entry
described below. Label the global CTA **Import authorization package** and the
offering shortcut **Add package to this offering** (include the offering name
in its accessible label). Both open the same file-first uploader.

Retain existing URLs: global `/authorizations/import` and scoped
`/authorizations/offerings/:offeringId/import`. After receipt, append `packageId`
to that same import URL so refresh retains both the receipt and selected
offering. Show analysis/scope review, then use the existing revision-checked
association form with the offering preselected. Manual fallback must preserve
that context too. Block denied/missing offering reads and mismatched existing
associations; do not silently switch offerings.

Uploading still creates an unassociated receipt, not authority, a boundary,
an offering or a published release. Existing exact-version successor intake and
onboarding contracts remain unchanged. Verify labels, shared upload behavior,
receipt reload, explicit association, error recovery, and desktop/mobile layout
before manual acceptance.

### Unified import verification

The focused run passed 45 provider-authorization unit tests plus the changed
catalog CTA test. Measured line coverage was 100% for FileFirstImport and
OfferingList, and 81.2% for AuthorizationsPage after the mobile correction. Dashboard `tsc --noEmit` and
the production build passed; existing build warnings remain.

Eleven Chromium scenarios passed: both entry points at 1440px and 390px,
receipt refresh, editable cited scope, explicit revision-checked association,
and the seven responsive/light/dark landing checks. Browser mutations used
synthetic API fixtures; no live package was uploaded or associated.
The routing unit test isolates upload identity because Node WebCrypto rejects
the JSDOM FileReader ArrayBuffer; browser tests retain real file reading and
hashing. Browser selectors use accessible roles for populated selects and
textareas, and open the existing boundary-authoring disclosure before inspection.

The broader 114-test run initially had six failures: that new routing test's
crypto harness mismatch, plus five existing organization-provisioning tests
whose API mock omits `getDirectoryConnections`. The latter failures were also
present in the pre-implementation red run and are not changed here. Earlier
OfferingIntake test limitations recorded below were not revalidated or hidden.
No full-suite regression-free claim is made.

Visual inspection after those checks found that the existing fixed-width
offering sidebar squeezed the scoped import into a narrow, clipped column at
390px. A page-level overflow assertion did not catch this. Both import entry
points must therefore use the full-width page without the offering sidebar;
the scoped import keeps a Back to offering link. Add failing minimum-width
and viewport-bound assertions for the uploader and confirmation region before
making this route-local correction. Other offering sections retain their sidebar.
The added regression measured an unusable 85.94px uploader before the change
against a 326px minimum. After the correction, all 11 browser scenarios and
45 provider-authorization tests passed again; the 390px screenshot now shows
the complete, full-width uploader.

Manual acceptance: open `/workspaces/csp/authorizations`. The header's
**Import authorization package** starts without an offering selected; a card's
**Add package to this offering** starts with that offering identified. Both
start with source files, retain their receipt URL through refresh, and require
explicit boundary confirmation and association after analysis. Upload must not
create authority or publish anything. User acceptance remains pending.

### Local Docker handoff

Rebuilt and replaced Dashboard only, retaining development simulation. The final
container is healthy and serves HTTP 200; image ID begins `23ca865c2de3`.
MCP, Chat, SQL and Redis retained the same container IDs and start times.
No data reset, live upload, association, boundary save or publication was performed.
The previous Dashboard image is retained as
`ato-copilot-ato-dashboard:pre-unified-import-20260924`.

Read-only checks against the deployed Flankspeed workspace verified both import
routes: global intake has no selected offering, while scoped intake identifies
Azure IL5 and exposes Back to offering without the sidebar. Both show the source
file selector before any boundary picker. The browser automation click initially
timed out waiting for visibility/stability; direct route navigation then verified
the deployed forms. Actual CTA navigation is covered by the 11 passing mocked-API
Chromium scenarios; user live acceptance is still pending.

## Local validation

Three new OfferingList tests passed after failing against the original presentation. Dashboard `npx tsc --noEmit` passed. The existing OfferingWorkflow/OfferingIntake selection had 16 passing and 5 failing tests: four intake receipt/retry/rejection assertions and one workflow receipt-link assertion. Their root cause and pre-change baseline were not established; no full-suite success is claimed. Logs were captured in `/tmp/authorization-refresh-existing-tests.log`. No .NET code changed or .NET checks run.

An isolated Vite harness rendered the real OfferingList with a synthetic API response and a simplified workspace shell. Playwright was used because agent-browser was not installed. Verified rendering, no-match recovery, no browser script errors, dark mode and no page-wide overflow at 390px. Screenshots: `authorizations-landing-refresh/desktop.png`, `dark.png`, `mobile.png`. These are component previews, not verification against the live backend.

Manual check: open `/workspaces/csp/authorizations` in the running Dashboard. Verify the offering count, search and clear action, create form, scoped Define boundary/hosting links, Upload package, and Manage offering. Check dark mode and narrow-screen layout. No authorization status or aggregate metrics have been inferred from catalog lifecycle.

### Header creation action

Move offering creation from the list accordion to a Create offering CTA beside Import existing authorization package. Use a dedicated `/authorizations/create` form view and navigate to the newly persisted offering after success. Retain the existing create contract and validation; leave import preparation's inline creation available.

### Header CTA browser verification

The real SPA passed two Playwright scenarios at 1440px and 390px with synthetic API responses. Both header CTAs were visible; Create offering opened the form, required name/environment input was submitted, and the persisted response navigated to the new offering. No backend data was written. Screenshots: `header-1440.png` and `header-390.png`. Test: `e2e/tests/authorization-landing.spec.ts`. Manual user testing remains pending; the earlier broader upload-suite limitations remain unchanged.

### Distinct import and create paths

Header Import starts with files and a durable retained receipt. It shows analysis status and extracted boundary-claim suggestions before offering selection/creation and boundary confirmation. Manual Create offering remains blank authoring. A selected source claim is only an editable suggestion; environment and resource coverage are not inferred. No candidate means explicit manual completion, not invented metadata. Existing scoped upload actions retain their workflow.

File-first implementation validation: Dashboard `tsc --noEmit` passed. Four Chromium checks passed at 1440px and 390px, covering manual creation and file-first upload with mocked API responses, durable receipt reload, explicit scope selection, and no automatic offering writes. Browser captures are in `authorizations-landing-refresh/import-{1440,390}.png` and `confirm-{1440,390}.png`. The focused unit run passed 22 tests and failed one existing scoped-upload receipt test in OfferingWorkflow; no full-suite or live-backend acceptance claim is made.

Manual acceptance: open Authorizations, choose Import existing authorization package, select source files and upload. Confirm the receipt survives refresh, processing leads to scope review, and a selected claim prefills editable fields. Select or create the offering, confirm the exact boundary and associate the package. Verify nothing is published by upload. Separately verify Create offering opens the blank manual form. When claims are unavailable, verify the explicit manual fallback. This acceptance remains pending.

### Attached screenshot alignment - September 24, 2026

The latest screenshot supersedes the header-creation placement above for the
landing page. Keep Import existing authorization package as its sole hero action;
restore the existing collapsed Create an offering form between search and the
cards. Successful manual creation navigates to the saved offering. Preserve the
dedicated creation URL for existing links and the separate file-first import flow.

Use a pale page background, vertically stacked full-width offering cards and a
right-hand workflow guide at desktop widths, stacking the guide below the list
on narrow screens. Retain the existing shared navigation/account controls,
dynamic provider identity, lifecycle badge, recorded-scope indicators, upload,
manage and contextual boundary/hosting actions. Do not hard-code Flankspeed or
Azure IL5 data from the screenshot. Keep loading/error/retry, empty/no-match,
pagination, keyboard access and dark mode. No backend or shared-layout changes.

Baseline before this change: OfferingList and OfferingWorkflow ran 19 tests,
18 passed and the existing scoped-upload receipt-link test failed. Preserve and
report that failure separately; do not claim a green full workflow suite.

Screenshot-specific validation:

- Six new/updated behavior assertions failed before implementation, alongside
  the known receipt-link failure. The wide-desktop browser check also reproduced
  the side-by-side cards before the layout change.
- Final focused unit/coverage run: **25 passed, one pre-existing receipt-link
  failure unchanged**. OfferingList has 100% line/function and 97.5% branch
  coverage; the two selected application files have 84.64% combined line coverage.
- **Seven Chromium checks passed:** inline keyboard-operated creation,
  name/environment validation, navigation to the persisted result, scoped links,
  light/dark colors and layout at 1440px, 1097px and 390px, plus vertically
  stacked cards at 1680px. Screenshots were visually reviewed. Browser mutations
  used synthetic API responses, not live records.
- Dashboard `tsc --noEmit` and production build passed. Build warnings remain
  for Browserslist data, SignalR annotations, CSS syntax, mixed imports and
  bundle size; this was not a warning-free build.
- A read-only local preview loaded the existing Flankspeed/Azure IL5 offering.
  No Docker container, backend, database or live offering was changed.

Manual acceptance: open the local Dashboard preview, verify the import-only
hero and collapsed Create an offering form beneath search. Expand it with the
keyboard and verify existing cards remain available. Check boundary/hosting,
Upload package and Manage offering links, search/clear, narrow-screen stacking
and Settings theme selection. The dedicated `/authorizations/create` URL still
works. Manual user acceptance remains pending.

### Local Docker deployment - September 24, 2026

At the user's request, rebuilt and replaced only `ato-dashboard` using the
existing development simulation build setting and `linux/amd64` platform.
The container reached healthy state and serves the updated Authorizations page
at `http://localhost:5173/workspaces/csp/authorizations`.

- Deployed image: `sha256:258b4ae6e8602ee1143548359a1fdfc6a27a6e4f131f18f0c18d5ee69cc13741`.
- The HTTP-served `index-Byoxq7gz.js` SHA-256 matches the file inside the new
  container: `2fda94071e451945c51b4a06d845bef89fe1cd403a2aa15fb5c72df1c35ed5ed`.
- Browser verification loaded the existing Flankspeed/Azure IL5 offering,
  import-only hero, collapsed inline creation, recorded-scope indicators and
  offering-scoped links. The account menu still reports Dev User (Simulated).
- MCP, Chat, SQL and Redis container IDs and start timestamps were unchanged.
  No stack reset, volume removal or business-data writes were performed.
- Existing build warnings and the baseline receipt-link test failure described
  above remain. Two HTTP 403 console events were observed on page load, also
  seen in the earlier preview; their endpoints and causes were not investigated
  by this deployment. No error-free full-workflow claim is made.

Use the Docker URL above for manual acceptance; user testing remains pending.

### Header CTA placement follow-up - September 24, 2026

The user's latest instruction supersedes the screenshot's inline creation
placement. Put Create offering next to Import existing authorization package in
the landing hero and remove the collapsed creation form below search. Reuse the
existing `/authorizations/create` page, validation and saved-offering navigation.
Preserve creation from the import page, scoped uploads, search, cards, themes
and responsive layout. No shared-shell, API or data changes.

Verify removal of the inline form, both hero actions for populated and empty
catalogs, keyboard navigation to creation, validation, rejection recovery and
desktop/mobile layout before local manual acceptance.

Validation: the changed placement tests failed before implementation. After
implementation, 26 focused unit tests passed with the same pre-existing
receipt-link failure. Seven Chromium scenarios passed across desktop/mobile,
light/dark themes, keyboard creation and stacked cards. Dashboard type checking
and production build passed with the existing warnings.

Rebuilt and replaced only the local Docker Dashboard. Its healthy container uses
image `sha256:e49980cba229aa09d1f7580e0b806eabfc4ffccaae7948a6834406d3f90e6738`.
The served `index-CJkggNxA.js` hash matches the container file. Browser verification
confirmed both header actions, no inline creation form, and navigation to the
existing blank creation page without saving data. Development simulation remains
included. Backend/data container IDs and start times are unchanged. The two
previously observed 403 console events remain; no full-workflow success is claimed.
Manual user acceptance is pending at the same Docker URL.

Package detail refinement (September 25): replace duplicate receipt/metric blocks with a compact package summary, a contextual next-step message and Records / Source files navigation. Preserve exception and exclusion counts visibly. Place model-call counts, receipt revision and analysis enrichment in Processing details. Keep source-file failures actionable through the Source files tab. Publication controls remain accessible in a collapsed section until a selection, saved preview, error or publication outcome exists. Keep existing revision checks, citations, exclusions and approval requirements unchanged.

Package review validation: Dashboard typecheck passed; 29 focused tests passed and one upload test failed at Node SubtleCrypto digest because its jsdom buffer is not accepted (before an upload request). Both desktop and mobile browser review/approval/publication checks passed using synthetic API responses, including lost-publication-response recovery. Previews: `package-review-1440.png`, `package-review-390.png`. Manually verify a Needs Attention package: exception/exclusion counts remain visible, Review source issues opens Source files, processing diagnostics and retry are expandable, and switching views retains the publication selection. No deployment was performed.
