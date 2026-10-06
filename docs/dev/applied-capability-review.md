# Applied capability review: local acceptance walkthrough

## October 7 local PR #1064 conflict resolution

The PR branch is being rebased onto `origin/main` at `f0e46d45`.
The inherited capability-review changes overlap upstream's canonical
responsibility draft APIs and independently reviewed component service-use scope.
Retain upstream's draft fixtures, scope decisions, component drawer and deployment
configuration instead of restoring older session-specific endpoints or treating
raw placement as reviewed service use. Preserve the later Mission, Overview and
system-definition improvements while adapting them to the current contracts.
This is a local integration only: no containers are replaced, no package is
submitted and no remote branch is updated. Historical test totals below are not
verification of this rebase; fresh targeted validation and manual acceptance are
still required.
## Focused provider offering review contracts

The Environment drawer reads canonical relationship permissions and complete
paged applicability for the exact system/assignment/offering. Its pure typed
presentation selector does not create an authorization policy, infer permissions
from visible roles, or treat published applicability as adoption. Known source
blockers are grouped with affected capabilities and translated; raw diagnostics
are disclosed. Captured scope-projection review flags are technical diagnostics,
not a replacement for the canonical relationship task state.

The read-only published-duty DTO adds nullable `totalCapabilities` and
`unavailableCapabilities` (capability ID, captured name if available, exact release
ID/revision and reason). Counts deduplicate verified matching releases and include
missing duty content; existing valid `capabilities` semantics are unchanged.
System scope `exclusions` comes from its exact retained hosting snapshot.
No private citations, new schema, accepted responsibility or approved baseline
are introduced. Older servers show count unavailable for incomplete source,
not an invented zero. Complete operational duty/private source content remains
unavailable unless a separately authorized source workflow supplies it.

Workflow origin query keys are `offeringId`, `assignmentId` and
`hostingScopeRevisionId`; the system remains route-bound. The receiver rechecks
the exact active scope and release and refuses changed/missing context.
Association choices are constrained to its assignment and revision without
preselecting capabilities. The responsibility matrix retains the complete system
baseline rather than silently narrowing it to the origin offering.

See the [manual drawer walkthrough](../guides/system-security-capabilities.md#focused-offering-review-at-port-4196).
Recorded review, package preparation/export, actual eMASS submission and an AO
decision remain distinct. This follow-up's native/SSP regression tests are
synthetic; no live package was generated or submitted.

## Requirement first passes from recorded system information

The control-detail requirement panel should show named parameter values, not
OSCAL `insert: param` tokens. Source text/IDs remain available separately for
traceability. For authorized authors, an existing selected-control draft with
missing requirement responses/parameters prepares a source-backed AI first pass.
The AI uses scoped entered profile/data records, applicable narrative grounding,
retained system policies and recorded values through the existing narrative
generator. It must not infer legal authority from a standards name or claim
observed enforcement from declared capability/evidence metadata.

Review response/parameter suggestions, their basis and remaining questions.
Use the explicit action to fill empty fields only; existing text is preserved.
Save as an unreviewed draft and obtain the existing independent review. AI
generation alone creates no approval, evidence attachment or baseline addition.
Missing source values stay readable gaps; failure/AI unavailable is visible and
manual drafting remains possible. Server-protected provenance and source
freshness checks prevent stale/foreign suggestion application.

Manual acceptance: use a synthetic control with parameterized prose and scoped
Mission/Data/policy records. Verify first-pass preview, preserved edits, named
values/placeholders, source disclosures, unsupported legal-authority questions,
explicit save/reload, separate review and actual native output. Repeat as a
viewer and another tenant/system; no unauthorized generation/save may occur.

First-pass local API rollout overlays the currently running overview image
with `ato-copilot-mcp:requirements-firstpass-1312bde8-20261005`, preserving its
source/runtime configuration, volumes, peers and separately staged files.
The Dockerfile's existing package-only build context was used for cached NuGet
archives after a real NuGet TLS/EOF restore failure; TLS was not disabled and
no dependency version changed. Retain the current overview image for rollback.
Verify real drawer/API behavior on 4196 without saving or approving demo data.

First-pass rollout verified October 5: API healthy and normalized environment/
user/ports/mounts unchanged; SQL/Redis/Chat/Docker frontend identities preserved.
Real browser configured sign-in and PT-2 auto-generation on 4196 returned 200
with five scoped records, two response drafts, zero invented parameter values
and five questions. Source gaps, particularly legal authority/processing values,
remain for review. Narrative version 1 is unchanged; no draft save, approval
or evidence/baseline mutation was performed.

Refresh Narratives, select PT-2 and review **First pass from your system records**.
Inspect suggested responses, their source basis and remaining questions. Use
**Use first pass in empty fields**, edit as needed, then explicitly save and
obtain independent review. Original OSCAL tokens stay in source disclosure,
not the main requirement wording. No legal authority or control satisfaction
is inferred from a draft.

Validation: 113 backend/generator/native tests, 53 API/overview tests,
31 Dashboard tests and 8 desktop/mobile narrative cases passed with builds/
type-check (existing warnings remain). Snapshot/history/native output retains
AI-assistance source metadata and separates it from human review. The other
overview changes already staged/running in this worktree remain intact; this
task made no commits or GitHub writes.

## Control Responsibility simple-review presentation: October 2

The actual Control Responsibility drawer was compared with
`docs/design/workspace-ui-mocks/responsibility-review-simple.html` in the main
checkout. Its earlier full first-pass panel pushed allocation choices and editable
duties below a long stack of provenance, source conflicts and questions.

The drawer now follows the specific simple-review reference: named header and
one current-responsibility status, compact provider contribution card, four
allocation cards, relevant duty fields, **What happens next**, and a fixed
review/save footer. Provider scope/evidence, source versions, saved review
history and first-pass details are expandable. Source staleness stays visible;
refreshed-suggestion comparisons open the relevant disclosure for explicit
review. Request failures show a dedicated card with failure details and reload.
The mock's sample source text was not copied.

Automatic environment-backed preparation, saved corrections, source versions,
server authorization and existing confirmation remain intact. This is a
presentation change, not a competing approval lifecycle or an inheritance grant.

Current dashboard:
`ato-copilot-dashboard:main69b7e407-simple-responsibility5173-dev-20261002`.
MCP, SQL, Redis and Chat are unchanged; port **5173** and development simulation
are retained.

Local review: refresh 5173, use **Dev ISSM**, open **SPIN Demo System →
Responsibilities**, then **Open AU-11 responsibility**. The provider card and
allocation choices should be visible immediately, without a scope picker or
Prepare action. Inspect/edit duties, expand provider evidence and saved history,
and use the existing draft-save/review/confirmation steps. Stale sources must
still be refreshed and compared before confirmation.

Verification: **3,096 dashboard tests**, actual TypeScript/development build and
**26 delivered-image browser scenarios passed**. The real AU-11 drawer passed
light/dark checks at **1440, 390 and 320 pixels**, with no horizontal clipping or
panel-scoped WCAG axe violations. Review/save failures, concurrency, permissions,
history, corrected drafts, keyboard access and comparison flows remain covered.
Existing Vite warnings remain; no backend or schema change was needed.

Rollback this presentation by redeploying only the dashboard with the retained
Overview override instead of the simple-responsibility override:
`main69b7e407-overview-mock5173-dev-20261001`. Keep the runtime environment and all
database volumes. No push or GitHub write occurred; human acceptance is pending.

## System Overview mock correction

The System Overview was compared directly with `/tmp/spin-readiness-mock.html`.
The deployed presentation now uses its white readiness hero, compact **Your work**
cards, separate team findings, documentation-at-a-glance sidebar and expandable
readiness explanation. Monitoring uses the same hierarchy with actual recorded
changes and health context. The existing system shell, navigation, server role
queue, package purpose and API permissions remain unchanged.

Dashboard image:
`ato-copilot-dashboard:main69b7e407-overview-mock5173-dev-20261001`.
MCP remains on the automatic-responsibility image. Port **5173**, development
simulation and the original demo data remain in use.

Local review:

1. Refresh **http://localhost:5173**, sign in with supported **Dev ISSM**
   simulation and open **SPIN Demo System → Overview**.
2. Before a check, verify **Readiness not checked**, **Find out what your package
   needs**, **Your work** and **Documentation at a glance**. No mock findings or
   fictional document completion appear.
3. Click **Check readiness**. Team findings come from the actual validation
   response and remain separate from personal assignments. Unknown owners are
   explicitly marked rather than guessed. Expand task details to inspect the
   original finding/remediation and follow its existing source workflow.
4. Inspect document previews and package-readiness links; they retain
   **InitialSubmission** purpose. Empty personal work is not overall readiness
   or authorization.
5. Switch to **Monitoring & follow-up**. Review actual enabled state, last check,
   findings and retained records. No fictional logging event is copied from the
   mock, and absence of changes is not proof of complete collection coverage.
6. Test light/dark themes, keyboard tab navigation and narrow layouts. Unlike the
   mock, supporting documentation remains accessible by stacking below the main
   content on narrow screens rather than disappearing.

Validation: new hierarchy/state tests failed before implementation; actual
TypeScript check and development build passed; **3,095 dashboard tests passed**
and **12 delivered-image readiness/role/package browser scenarios passed**.
The real demo's unchecked, checked and fully loaded monitoring states were
verified at **1440, 390 and 320 pixels in both themes**, with no horizontal
clipping or panel-scoped WCAG axe violations. Existing Vite warnings remain.
This UI-only correction required no backend or database change.

The real demo validation reports many more findings than the mock's three sample
items. The Overview shows the actual counts and first five findings, with a link
to the full readiness workflow. It does not convert legacy unstructured
validation messages into fabricated owners or completed tasks. Source error
codes remain inspectable; the verified System-design approval finding links to
the existing System design workflow.

Rollback only this presentation by redeploying the dashboard using the retained
runtime configuration plus the automatic-responsibility override, without the
Overview override. The previous dashboard tag is
`main69b7e407-automatic-responsibility5173-dev-20261001`. Keep all environment
settings and SQL volumes; no data rollback or removal of other services is needed.
No push or GitHub write occurred. Human visual acceptance remains pending.

## Automatic environment-backed first pass

User clarification: responsibility review must not ask users to select the
environment/provider scope again or initiate first preparation. Both the Applied
Capability editor and Control Responsibility panel now request the recorded
environment context and automatically prepare the first pass for authorized
reviewers. The scope selector and initial Prepare action are removed from these
panels. Source selection is now read-only context with a link to the owning
Environment workflow.

Both current images use the tag
`main69b7e407-automatic-responsibility5173-dev-20261001`; the earlier image
sections below are retained history. Port **5173**, development simulation and
the original SQL Server volumes are unchanged.

The server resolves current assigned scopes by published capability/control
mappings, using the applied provider capability where supplied. It ignores
removed scopes and never takes the first item in a list. Provider-qualified matrix
entries forward their existing capability context. A whole-control entry with
several provider contributions still gets an editable system/environment first
pass without arbitrarily choosing a provider; source-specific allocation review
remains explicit for provider-reliant allocations. An explicit Customer decision
retains the existing system-only human review path. AI cannot replace recorded provider mappings with assumed
customer ownership. No applicable provider scope uses authorized
system records without inventing a provider or inheritance. Environment inputs
are fingerprinted in draft provenance so changed assignments invalidate stale
assumptions.

Source-backed values are available while AI prepares; users can edit them.
Saved corrections and accepted drafts are not automatically regenerated.
Untouched source-only proposed drafts can receive their first AI pass, with
revision-fenced suggestion application. New preparation changes draft state
only, never confirmed responsibility. Preparation and confirmation use separate
callbacks to prevent a generated draft from displaying a confirmation notice.
Failures remain visible; refresh/comparison and authorized acceptance stay
explicit.

For local review: refresh **5173**, use **Dev ISSM**, open **Audit collection**,
and select **AU-11**, **AU-2**, or **AU-6**. Do not select a scope or press
Prepare. Inspect the populated duties and origin labels, make corrections and
save. The source context, provenance and Environment link remain expandable.
Missing environment/source information or AI availability is a real limitation,
not an invitation to copy the mock's duties.

Live Audit collection verification returned actual prepared provider duties for
**AU-11**, **AU-2** and **AU-6**, with HTTP 200/Prepared responses. AU-6 also
contained AI-proposed customer review text identifying that the supplied records
do not confirm customer duties; that text is not a verified duty assignment.
AU-11/AU-2 did not receive supported customer
duties; those fields stay empty with an explicit gap rather than fabricated text.
These are proposed responsibility drafts, not accepted allocations.

Final automatic-first-pass validation:

- Actual TypeScript check and development build passed.
- **3,092 dashboard tests / 323 files passed**.
- **92 responsibility integration tests passed**, including environment
  resolution, source splits, aggregate allocation guards, isolation, revisions,
  review lifecycle and downstream outputs.
- **44 delivered-image browser scenarios passed**.
- Real demo: Applied Capability, provider-qualified matrix and whole-control
  first passes are editable without scope selection or a Prepare action at
  **1440/light, 390/dark and 320/light**. Whole-control review retains a
  legitimate multi-provider notice, not a request failure or a demand to repair
  valid Environment records. Panel-scoped accessibility checks passed.
- Applied-capability counts now use exactly the environment-resolved draft
  shown for the current contribution, rather than summing unrelated provider
  scopes and whole-control proposals.
- The full solution build passed. Existing compiler/analyzer and Vite warnings
  remain; the earlier full integration scan-worker failure is documented below
  and is not represented as an all-green full integration run.
- No source statements were copied from the mock, no provider locations were
  assigned automatically, no accepted allocation was created by preparation,
  and no GitHub write or push occurred. Human acceptance remains pending.

To roll back this behavior, redeploy **both** MCP and dashboard from the retained
base runtime compose configuration without the automatic-responsibility override:

```bash
docker compose -p ato-copilot -f "$RUNTIME_COMPOSE" \
  up -d --no-build --no-deps --wait --wait-timeout 180 \
  ato-copilot ato-dashboard
```

No schema migration or data deletion is needed. Retain newly prepared draft
records and their history; source fingerprints can appear stale under the older
capture logic and must not be treated as accepted content.

## Mock fidelity correction: October 1 evening

The actual deployed panel was compared with the supplied HTML reference, not
just exercised through mocked functional tests. The supplied parent-directory
path was absent; the same reference is present in the main checkout's
`docs/design/workspace-ui-mocks/applied-capability-review.html`.

The panel now has a single named system/capability header, a lavender next-task
card, accurate tab count chips, Overview scope/duty task rows, a persistent
summary footer, and two prominent editable duty fields. Allocation, basis,
source selection, versions, history and recorded mappings use progressive
disclosure. Conflicts, stale sources, request failures and read-only restrictions
remain explicit. Successful editor changes reload canonical persisted counts.
The correction uses the existing first-pass hook and approval lifecycle; no
provider statements or AI suggestions were copied from the mock. Existing
synthetic demo records remain unchanged.

Dashboard image:
`ato-copilot-dashboard:main69b7e407-applied-mock5173-dev-20261002`.
The MCP image remains
`ato-copilot-mcp:main69b7e407-membership5173-dev-20261001`.
All five original containers are healthy; dashboard HTTP 200 on **5173**.
Development simulation and the original SQL Server demo volume are retained.

### Local manual review

1. Open **http://localhost:5173** and refresh the page to load the rebuilt assets.
   Use the supported **Dev ISSM** simulation identity.
2. Open **SPIN Demo Organization → SPIN Demo System → Applied security
   capabilities → Audit collection**. Check the single header, lavender task
   card, scope/duty task rows and fixed **Back to summary** action.
3. Select **Where it applies**. Verify recorded component names and locations.
   **Assign location** / **Change** opens the existing authorized component
   placement workflow. Do not interpret association as coverage or connectivity.
4. Select **Responsibilities** and inspect **AU-11**, **AU-2** and **AU-6**.
   The two visible fields are **Provider contribution** and **Your team's
   duties**. Empty source-backed values must stay empty rather than use the
   mock's synthetic duties.
5. The first pass is prepared automatically from the recorded environment.
   Expand **Environment context and sources** to inspect the resolved
   context, origins, applicability questions and conflicts. Correct provider
   applicability in the owning Environment workflow, not a second scope picker.
   Refreshing a suggestion must preserve corrections pending comparison.
6. Correct a duty, switch controls and tabs, and return. The correction should
   remain in this session. **Save draft** is separate from **Review saved
   responsibility** and explicit confirmation. Inspect allocation/basis/scope
   and canonical history before confirming; confirmation is not narrative
   approval, eMASS submission or authorization.
7. Test keyboard Tab/Shift+Tab, tab arrow/Home/End navigation, Escape and focus
   return. At 320/390 pixels and in dark mode, use vertical panel scrolling;
   content must not clip horizontally.

### Verified checks and limitations

- New hierarchy/disclosure tests failed before the production changes.
- Actual TypeScript command: `npx tsc -b --pretty false`; passed.
- Development dashboard build passed and includes the simulation module.
- **18 delivered-image browser scenarios passed**, including keyboard
  navigation, placement editing, preserved corrections and protected workflows.
- Real demo browser checks passed at **1440/390/320 pixels in light and dark
  themes**, with exact panel widths, fully loaded AU-11/AU-2/AU-6 contexts,
  no horizontal clipping and zero panel-scoped WCAG axe violations.
- `dotnet build Ato.Copilot.sln --no-restore -m:1` passed, with **10 existing
  warnings in untouched integration tests**. Vite also reports existing
  Browserslist, SignalR annotation, mixed-import and chunk-size warnings;
  this is not a warning-free baseline.
- Full final dashboard suite: **323 files / 3,080 tests passed**, with bounded
  concurrency (`npm test -- --reporter=dot --maxWorkers=2`).
  The count-refresh regression initially held a detached panel node across a
  successful parent reload; it now queries the current panel. An existing
  immediate-readiness assertion in ResponsibilityReviewTask was intermittent
  under the unbounded full run; its targeted rerun and final bounded full run
  passed without changing that approval screen or test.
- The prior full `.NET` solution run finished: **8,110 unit tests passed**;
  integration had **1 failure / 1,855 passed / 20 skipped**. The failure was
  ScanImportWorkerIntegrationTests' temporary-file cleanup assertion. Its
  subsequent targeted rerun passed; its intermittent cause has not been
  established, and the full integration run is not claimed green.
- Automatic environment resolution and existing responsibility lifecycle:
  **92 focused integration tests passed** (including that scan-worker rerun).
  The full solution build passed; existing compiler/analyzer warnings remain.
- Visual delivery is available for local human review; human acceptance is
  pending. This presentation correction does not independently re-prove actual
  eMASS submission, authorization or a live AI service's generated content.

### Presentation rollback

Retain the current runtime configuration, ports, environment and named volumes.
Redeploy only the dashboard service using the retained base runtime compose
configuration (without the applied-UI override), whose dashboard image is
`ato-copilot-dashboard:main69b7e407-membership5173-dev-20261001`:

```bash
docker compose -p ato-copilot -f "$RUNTIME_COMPOSE" \
  up -d --no-build --no-deps --wait --wait-timeout 180 ato-dashboard
```

Set `RUNTIME_COMPOSE` to the retained private runtime configuration; never commit
it or print its environment. Do not remove SQL, Redis or Chat as "orphans."
This UI-only rollback does not change the database or revert the access fixes.

## Current address and development simulation

User direction, October 1 evening: keep the app on **http://localhost:5173**.
The 5197 preview has been removed and is not recreated. The original SQL Server
demo data and volumes remain in use.

Current MCP/dashboard images:
`main69b7e407-membership5173-dev-20261001`.
Backend `ASPNETCORE_ENVIRONMENT=Development` and
`CacAuth:SimulationMode=true` are explicitly enabled; the dashboard was built with
`NODE_ENV=development`, including the supported simulation UI chunk.
The login bootstrap exposes all nine configured development identities.
This remains development-only and does not enable simulation in production.

The reported demo ISSM portfolio failure was reproduced as 403. Canonical active
directory/object membership now resolves its existing unlinked Person and
persisted RMF assignments in effective access. No Person was promoted or role
granted; revoked/wrong-directory/cross-tenant/disabled-tenant access remains denied,
and the rule denying tenant aggregates to system-only assignments remains intact.

The restored view exposed a second coverage 500: a SQL-incompatible membership
test on the value-converted NIST baseline list. Both organization and system
denominators now count projected lists after materialization, with a relational
regression test. **40 focused authorization/import tests passed**.

Actual deployed verification with supported Dev ISSM simulation and the real
SPIN Demo Organization database: **portfolio 200, coverage 200**, one active
SPIN Demo System, and neither error banner present in the browser. The earlier
addresses, image tags and totals below are retained deployment history.

## Current deployment after the main rebase

The session branch now descends from `origin/main` at `69b7e407` (merged #1062).
The panel uses the upstream `ResponsibilityDraftService`, `ResponsibilityFirstPass`
and canonical control/scope draft APIs. The earlier session-specific draft
store/endpoints were retired; no database tables or saved records were dropped.
The implementation was preserved in a local commit and rebased, not pushed.

Current images:

- `ato-copilot-mcp:main69b7e407-reviewf7368843-20261001`
- `ato-copilot-dashboard:main69b7e407-reviewf7368843-20261001`

The original demo MCP/dashboard containers were upgraded while preserving their
active environment, named volumes and ports (3002/5173). A copy-only,
checksum-verified database backup was taken in the original SQL volume before
upgrade. SQL Server still contains the active **SPIN Demo System** record.

The review UI on **5197** now proxies to that same original demo backend on
**3002**, so it uses the existing demo database instead of the earlier fresh
preview database. Normal sign-in and organization/system permissions still apply;
an unauthenticated/unscoped system-list request returns 403 rather than bypassing
authorization. The isolated development backend/volume on 3197 is retained, not
merged or deleted. To explicitly test it instead:

```bash
CAPABILITY_REVIEW_MCP_BASE_URL=http://mcp:3001 \
  docker compose -f docker-compose.capability-review.yml up --no-build -d
```

Post-rebase verification: backend compilation, actual dashboard type-check and
production build passed; **124 backend unit tests**, **88 integration tests**, **79 focused dashboard tests**
and **18 delivered-image browser scenarios** passed. Earlier test totals below
describe the pre-rebase implementation, not a new full-suite run on merged main.

Canonical statuses are Proposed, ComparisonRequired and Accepted. Save is not
acceptance; confirmation requires saved-state review, source/duty acknowledgements
and notes and uses upstream enforcement. Saved counts are read from canonical
authorized control/scope contexts, not inferred from component association.

### Current local walkthrough

1. Sign in on 5197 and select the original demo organization and SPIN Demo System.
   Workspace authorization is required; a raw unscoped API call is not a data browser.
2. Open Audit collection, inspect Overview and recorded component locations, then
   select AU-11, AU-2 or AU-6 in Responsibilities.
3. The canonical first pass starts with system records. Explicitly choose a recorded
   provider scope when applicable; inspect source versions, duties, exclusions,
   missing information and conflicts. Correct the populated fields.
4. Save the draft. Refresh and compare suggestions using the canonical comparison
   actions; corrections are not silently replaced. Saving does not accept inheritance.
5. Choose Review saved responsibility, verify applicability/coverage and customer
   duties, add review notes and explicitly confirm. Confirm the matrix and separately
   reviewed narrative/document outputs. No action here submits eMASS or grants an ATO.

The prior implementation notes and validation below are retained as a history of
the original session, not as evidence of a full-suite run on the rebased code.

## Prior implementation notes

The Applied Capabilities list and right-side dialog remain in place. The dialog
now has Overview, Where it applies, and Responsibilities sections. Keyboard
Left/Right, Home and End navigate the sections. Scope gaps count contributing
components with no InScope or SystemWide location; excluded locations do not
establish coverage. Responsibility confirmation counts and saved draft review
counts are separate.

Selecting a control opens its prepared responsibility first pass in the dialog.
Users can correct and save the system draft without accepting inheritance.
Unsaved corrections survive control/section changes, suggestion refresh and
placement-dialog navigation within the identity-keyed page. Browser reload
restores saved content, not unsaved edits. Switching identities/workspaces
clears in-memory edits.

The existing full capability review supports an explicit handoff of a reviewed,
saved provider draft into its existing responsibility confirmation form. This
handoff does **not** confirm anything. Source/coverage checks and notes remain
required. Organization-only allocations still use the authorized Control
Inheritance workflow; there is no competing inheritance approval API.

## Source and permission boundaries

- A current, exact provider adoption/scope pin enables the published
  control-specific Provider/Shared/Customer split. Legacy subscriptions without
  that pin use system records and explicitly request scope adoption/review.
- The release contract contains splits, not necessarily detailed customer duty
  text or exclusions. Missing information is flagged, not synthesized as fact.
- Approved policy/technical narratives are preferred to current working content.
  Working content, missing duties and unverified applicability are flagged.
- Component locations, system descriptions, scope revisions, narrative versions
  and evidence content hashes participate in preparation provenance/invalidation.
  Evidence metadata is not proof that its content has been verified.
- Optional configured AI supplies a separately labelled explanation. It cannot
  write the allocation or authoritative duty fields. A failed AI request remains
  an error; absent AI configuration is explicitly reported with the supported
  source-grounded fields.
- Generated explanations and immutable saved revisions are tenant/system/control
  scoped. ISSM/ISSO assignments are rechecked server-side for draft saves and
  existing responsibility confirmation. Provider-authored sources remain read-only.
- Draft review never changes control satisfaction, approved narratives, accepted
  inheritance, authorization decisions or submission state.

## Start locally

The isolated development Compose definition uses separately named services,
versioned image tags and a dedicated SQLite data volume by default:

```bash
docker compose -f docker-compose.capability-review.yml up --no-build -d
curl --fail http://localhost:3197/health
curl --fail http://localhost:5197/
```

This is a development simulation configuration, not a production deployment.
It does not copy existing SPIN data or synthetic mock content. For an existing
system, use an authorized test database copy by configuring
`CAPABILITY_REVIEW_DATABASE_PROVIDER` and `CAPABILITY_REVIEW_CONNECTIONSTRING`
through your local environment; do not commit credentials. Import or create only
your real authorized source records. Take a backup before enabling a new build
against an existing database. Startup additions retain existing records and add
the two draft preparation/history tables.

The session's updated local image tags are already built. Use `--no-build` to
test those verified images while container package-source TLS remains unavailable.
After that connectivity issue is resolved, `up --build -d` runs the normal clean
Dockerfile restore/build path. Do not disable TLS checks or substitute unverified
provider content to make a build or review appear successful.

If another application owns 5197, do not stop it without its owner's permission:

```bash
CAPABILITY_REVIEW_PORT=5198 \
  docker compose -f docker-compose.capability-review.yml up --no-build -d
```

For frontend-only inspection:

```bash
cd src/Ato.Copilot.Dashboard
VITE_API_PROXY_TARGET=http://localhost:3197 \
  npm run dev -- --host 127.0.0.1 --port 5197 --strictPort
```

Use 5198 instead if 5197 is occupied. A served frontend alone is not an
end-to-end working system; the matching backend must be reachable.

### Environment recovery and delivery

Initially port 5197 was owned by two Vite development previews from another
worktree, so early source validation used 5198 without stopping them. After the
user directed autonomous completion of the requested port-5197 delivery, only
those two identified listeners were terminated. Their worktree files and the
original Docker stack were not changed. The final updated isolated app is served
at `http://127.0.0.1:5197`, with its matching API at
`http://127.0.0.1:3197/health`; both returned HTTP 200.

Initial Docker image builds were attempted but Docker Desktop returned
"Docker Desktop is unable to start." Its sequential startup logs reported
"no space left on device" writing its VM/startup log. The host data volume was
at 100% while the separate repository volume had free space. No shared Docker
images, caches, volumes, logs or unrelated files were pruned, and no restart
was used to conceal this failure. After the user explicitly requested Docker
startup, host storage had recovered to 107 GiB free. The stale Docker backend
could not stop through the desktop CLI, so only its identified stalled processes
were terminated and Docker was relaunched. All five existing containers recovered
healthy with their original ports and volumes.

Updated images were subsequently built under these unique local tags:

- `ato-copilot-mcp:applied-review-145446f0-20261001`
- `ato-copilot-dashboard:applied-review-145446f0-20261001`

Container npm/NuGet HTTPS restores failed with SSL EOF/connection-reset errors.
TLS verification was not disabled. The MCP image used the existing Dockerfile's
`nuget-packages` named context and cached `.nupkg` feed. The dashboard used current
`npm run build` output, after its real TypeScript check, supplied as the existing
Dockerfile's `build` named context (`app/dist` layout) to the unchanged nginx
runtime. This is local verified-artifact packaging; the normal clean-container
restore path still needs its network issue resolved.

The isolated Compose app was started with `--no-build` and is healthy at
`http://localhost:5197` (API health: `http://localhost:3197/health`), using its
dedicated development SQLite volume. The page and login/bootstrap API returned
HTTP 200. It is not the main stack's data and contains no copied provider mock
statements. All 18 applied-capability/system-capability browser scenarios passed
against the final Docker images on 5197, including provider and organization
workflows, read-only actors, stale writes, protected evidence, keyboard access,
WCAG contrast, light/dark themes and desktop/mobile clipping checks.

## Manual walkthrough: Audit collection

Use an authorized local test system and its real **Audit collection** capability.
If AU-11, AU-2 or AU-6 is not mapped, obtain the correct source mapping or baseline
through the existing authorized workflow; do not add fabricated provider claims.

1. Open Applied Capabilities and the Audit collection review dialog. Verify
   provider/source and description against the recorded source. Inspect the
   expandable version/evidence/limitations section. Check that scope gap and
   saved-draft counts match actual records.
2. Go to Where it applies. Confirm each component's name and recorded location.
   Use Assign location/Change to select an existing authorized boundary. Check
   excluded/unassigned components remain gaps. Verify no locations are assigned
   implicitly and no deployment or monitoring coverage is asserted.
3. Open Responsibilities and AU-11. Compare the original split/source references,
   customer records and missing-information flags. When provider scope is pinned,
   verify the explicit split is copied unchanged. Without a current pin, verify
   the first pass does not invent a provider or inherited designation.
4. Correct the customer draft and notes. Switch to AU-2, then AU-6, then back to
   AU-11. Your correction must remain. Refresh suggestions: inspect the new
   suggestions without replacement of edited fields. If context changed, compare
   it and explicitly keep your edits with the refreshed context before saving.
5. Save AU-11's draft. Confirm the displayed saved version/author/time. Mark it
   reviewed for preparation and save again. Verify draft review counts change
   only after the successful request. Confirm the responsibility matrix and
   approved narratives have **not** changed yet.
6. Repeat review/correction for AU-2 and AU-6 using their own source context.
   Inspect evidence and narratives through the selected control link; content
   from another control must not be substituted.
7. For a provider draft, open its full capability review and use the reviewed,
   saved draft in the existing confirmation form. Independently verify actual
   provider coverage and customer duties, supply review notes, then confirm
   through the existing lifecycle. Customer duties exceeding that contract's
   2000-character limit must be summarized by the reviewer, never silently cut.
8. Verify the accepted allocation/customer duty/provider in the responsibility
   matrix. Preview the SSP and applicable eMASS preparation output. Existing
   approved narratives and implementation status remain preserved. Generating an
   export is not submission to eMASS or an authorization decision.
9. Repeat as a read-only user and in another tenant/system: draft mutation and
   placement actions must not bypass server permissions. Use two review sessions
   to save the same version: one may succeed; the other must show a conflict.
   Refresh/compare rather than overwriting the other reviewer.
10. Use browser request blocking/offline mode for save and AI preparation.
    Failed requests must display errors, retain corrections and never claim saved
    or reviewed. Change source/scope in a separate authorized session and confirm
    prior draft assumptions become stale without loss of saved history.
11. Test keyboard-only access, focus return/Escape and 320/390px widths. The
    right-side panel must not clip horizontally.

## Checks

```bash
dotnet build Ato.Copilot.sln
dotnet test Ato.Copilot.sln
cd src/Ato.Copilot.Dashboard
npx tsc -b --pretty false
npm test -- --run \
  src/__tests__/workspaces/SystemCapabilityList.test.tsx \
  src/__tests__/workspaces/ResponsibilityDraftEditor.test.tsx \
  src/__tests__/workspaces/SystemCapabilityResponsibility.test.tsx \
  src/__tests__/workspaces/SystemCapabilityDetail.test.tsx
PLAYWRIGHT_BASE_URL=http://localhost:5197 \
  npx playwright test e2e/tests/applied-capabilities.spec.ts
```

The TypeScript package has no `typecheck` script; its actual build/type-check
command is `tsc -b`. Only the dashboard TS project was changed.
The build's existing warning messages were compared against an isolated
`145446f0` baseline and matched. The production Vite build retains its existing
large-chunk warning; it was not suppressed or "fixed" as unrelated work.
The initial full-suite attempt encountered SQLite "database or disk is full";
disk-dependent tests passed when task-owned scratch files were placed on the
available repository volume. Full-suite results must be reported separately
from targeted successes and SQL Server-dependent skips.

### Verified session results

- Required final solution rebuild completed successfully: **0 errors, 144
  existing warnings**, matching the clean `145446f0` baseline warning messages.
  The final incremental confirmation build passed with **0 warnings and 0 errors**.
  No zero-warning clean baseline or new-warning suppression is claimed.
- Complete final solution tests passed with task scratch on the available
  repository filesystem, sequential projects and isolated xUnit collections:
  **7,899 unit tests passed; 1,816 integration tests passed; 20 integration tests
  skipped**. Subsequent targeted closure/coverage tests: **56 passed**, including
  the additional permission-revocation and known-narrative checks.
- Real SQL Server additive draft schema preservation, server workspace roles,
  simultaneous draft saves and canonical matrix/SSP/eMASS output checks passed
  in a separate required-Docker run: 7 tests passed with no skips.
- Final complete dashboard suite: **2,969 tests passed across 309 files** with
  `npm test -- --maxWorkers=1`. The exact source-readiness wait, source-qualified
  operation fixture and saved-provenance validation were corrected rather than
  weakening assertions. `npx tsc -b --pretty false` and the production build passed.
- All **18 Chromium capability scenarios passed against the final nginx/MCP
  images on 5197**. This includes panel and system-workflow WCAG checks, keyboard
  access, selected controls, preserved corrections, provider/org placement and
  recovery, stale removal, read-only actors, protected evidence, and light/dark
  desktop/mobile layouts. The panel also passed 320px no-clipping checks.
- The final touched-UI coverage run measured **97.57% executable lines and 81.7%
  branches**. All four new UI modules measured **100% executable-line coverage**.
  Existing unchanged paths in the touched detail/list modules account for the
  remaining full-file lines; full-file coverage is not misrepresented as 100%.
  The two new backend preparation/persistence implementations measured **100%
  executable-line coverage (87/87 and 93/93 lines)**. The coverage UI run passed
  all 80 selected tests. Branch coverage is reported separately, not represented
  as 100%.
- Earlier broad runs exposed loading-race assertions, outdated task headings,
  source-incompatible fixtures and measured contrast failures in this workflow.
  Their correction is included in the final passing runs. No test thresholds
  were increased, failures swallowed, accessibility rules disabled or assertions
  skipped to obtain those results.
- Full .NET attempts also exposed process-wide allocation-budget interference
  from concurrent test collections and a collector/binary instrumentation
  collision. Rebuild uninstrumented binaries before the final complete run and
  do not run a .NET coverage collector concurrently with a build or another
  suite reading the same outputs. Use the supported xUnit collection isolation
  setting for a trustworthy allocation-budget measurement:

  ```bash
  dotnet test Ato.Copilot.sln --no-build -m:1 \
    -- xUnit.ParallelizeTestCollections=false
  ```

  This preserves all tests and their original performance budgets. Dedicated
  simultaneous-request tests still create and verify their concurrent requests.

The requested port is now active and the complete manual walkthrough is available
before user acceptance. Human review of real source records remains required;
automated fixture scenarios do not stand in for that review.

## Rollback

1. Stop only this preview project with
   `docker compose -f docker-compose.capability-review.yml down` (no `-v`).
   Do not stop the main stack or another worktree's servers.
2. Redeploy the previous approved image/revision. Draft storage additions are
   additive; keep both draft tables, original sources, confirmations and narrative
   history. Older code does not consume these draft rows.
3. Do not delete draft history or rewind the shared database. Any accepted
   responsibility changes require the existing audited correction workflow;
   reverting the UI is not a data rollback.
4. Local acceptance is pending the user's walkthrough. No commit, push or GitHub
   issue write was performed for this implementation.
