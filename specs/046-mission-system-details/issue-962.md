# Issue #962: Business-Context REST Handoff

Issue: https://github.com/azurenoops/spin_agent/issues/962

## Contract And Scope

Expose the four routes already used by `businessContext.ts` through the existing
system-profile service: GET/PUT per-control draft, GET flagged controls, and POST
control flags. Reuse authenticated dashboard access and authoritative Mission Owner
write / ISSM flag checks. Validate tenant-visible system and control existence before
returning data or changing it. A valid control without a draft returns HTTP 200 with
JSON null; missing resources, denied requests, and transport/server failures are errors.

The Narratives page distinguishes loading, a retrieved draft, confirmed absence, and
retrieval failure. Awaiting Mission Owner input requires both confirmed absence and
a positive control flag. Failed requests remain retryable; refresh must not retain
stale drafts or flags. Business-context text stays separate from Policy and Technical
content until the user explicitly incorporates it.

No schema, MCP envelope, permission expansion, AI generation, or unrelated test-suite
repair is intended. Existing per-system role checks and tenant query filters remain
authoritative. No new feature or user story is introduced; this repairs issue #962
under Feature 046's existing handoff contract.

## Local Hypothesis And Checks

Relational validation exposed a second handoff blocker: SQLite cannot generate the
`BusinessContextDraft` timestamp column and inserts fail its NOT NULL constraint.
Keep SQL Server timestamp behavior unchanged; configure this entity for application-
generated concurrency tokens on SQLite, and verify successful HTTP save/update plus
stale-writer rejection. This changes provider behavior, not schema or permissions.

The frontend's documented URLs are not mapped by the profile endpoint owner. A
real-host request for a seeded draft should fail with 404 before route registration
and return the projected draft afterward. Separately, a rejected client request must
render a retryable error, never an owner-input prerequisite.

- [x] Real-route draft read/write and flag round trips, null absence, missing resources.
- [x] Authentication, authoritative writer/flag roles, and cross-tenant rejection.
- [x] UI loading, flagged/unflagged absence, errors, retry, refresh, and text separation.
- [x] Focused unit/integration tests and browser E2E.
- [ ] Full backend/dashboard gates, changed-path coverage, and manual test opportunity.

## Authorized Follow-Up

On 2026-09-21 the user authorized repairing the full-suite failures before
continuing OSCAL/document-source provenance, capability coverage metrics, and
import/rollback auditing. Keep each repair regression-tested and preserve the
existing issue-962 handoff changes. No failure suppression, relaxed assertions,
or lowered latency thresholds are permitted.

First confirmed defect: the login listener tests set BroadcastChannel to
undefined, but its property-presence check still attempts construction. Require
a callable constructor so restricted browser contexts use the storage fallback.
Validate with the existing ten listener cases before proceeding to another slice.

The idle-timeout mock omits the account lookup required by the current
simulation-aware logout path. Supply a synthetic signed-in account in that
fixture; do not remove the runtime safeguard against signing out unrelated
Microsoft sessions.

Additional dashboard fixture drift: await the lazy-loaded simulation panel;
provide LoginPage's MSAL account lookup so cross-tab notifications cannot call
a missing mock method; assert the chat callback's optional attachment argument;
make the registration label query exact to avoid matching its help text.

The callback also mounts the race listener and needs its own account-cache mock.
Attachment regressions must reflect the current shared MIME allowlist (CSV/TXT
accepted, images rejected) and frontend 10 MB limit, including boundary checks;
the prior tests asserted the superseded 20 MB/image contract.

Integration root-cause evidence: CapabilityImportEndpointTests changes the
process working directory during service construction, while parallel hosts use
that directory as their content root. Two unit fixtures repeat the same pattern.
Use each fixture's explicit existing content root instead; never mutate cwd.
Keep all import/coverage assertions and run the full integration suite afterward.

Latency check: the unchanged 15-sample profile test passes alone but previously
recorded 580ms under full-suite contention. Run this timing test in a dedicated
nonparallel collection, preserving cold calls, sample count, success assertions,
and the 500ms threshold. Confirm the full integration run rather than treating
one isolated pass as sufficient evidence.

Required final gates: `dotnet build Ato.Copilot.sln`,
`dotnet test Ato.Copilot.sln`, dashboard build/typecheck, full dashboard tests,
focused provenance/audit regressions, and browser acceptance. Expected outcome:
no failed tests. External publication still requires the exact preview and approval.

### Suite Repair Results (2026-09-21)

- Full unit suite: 5666 passed, no failures.
- Full integration suite: 869 passed, 40 skipped, no failures. The unchanged
	profile latency assertion passes in the dedicated nonparallel collection.
- Full dashboard suite: 476 passed, no failures or reported unhandled errors.
- Dashboard build including TypeScript checks passes; bundle warnings remain.
- Focused directory-isolation regression run: 20 unit and 18 integration passed.
- These results supersede the failing suite counts in the original record below.
	Manual/mobile visual acceptance and the authorized provenance follow-up remain
	outstanding. PR #984 is merged and supplies the prerequisite provenance work.

## Original Verification Results

Base: `c7d1d61528691f4b6c17d6b1961b8996f3815d93`.
Branch: `fix/962-business-context-handoff` in the isolated
`/Volumes/Internal/Downloads/repos/ato-copilot-fix-962` worktree.

- TDD: two real GET cases initially returned 404; explicit JSON null required a
	JsonElement because `Results.Json(null)` produced an empty body. Additional
	route/security cases failed before wiring. SQLite HTTP save failed before the
	provider fix. Eight UI cases failed before replacing the ambiguous null cache.
- Focused integration: 37 passed, including SQLite 8000-character save, update,
  stale-writer rejection, four cross-tenant HTTP paths, service-error 404/409
  mapping, and unexpected failures remaining HTTP 500 rather than successful absence.
- Focused dashboard: 11 passed, including 403/404/500/network errors, independent
	flag failures, retry, refresh, system navigation, and explicit-only incorporation.
- Browser: two mocked-API Chromium workflows passed at 1440px and 390px with no
	runtime errors. They verify interactions, not a live backend-to-browser deployment.
- Solution build: passed, 63 warnings. Dashboard build/typecheck: passed, bundle
	size and mixed-import warnings. Locked npm installation reported 12 audit findings;
	dependency changes are outside this fix.
- Full backend (before the seven additional exception-contract cases): 5666 unit
	tests passed; integration 860 passed, 2 failed, 40 skipped.
	Failures: `ProfileOperations_P95_Under500ms_SC011` (580ms versus 500ms) and
	`PostMessage_WithEmptyConversationId_Returns400` (missing temporary content root).
- Exact-base repeat: 5666 unit tests passed; integration 830 passed, 2 failed,
	40 skipped. Both base failures were missing temporary content roots in
	`Download_ExistingEvidence_ReturnsFile` and
	`T007_ApplyProfile_Route_Returns_NotFound404_Without_Fix`. This demonstrates a
	base fixture failure category, not proof that the branch-only latency failure
	is unrelated. No suite failures were suppressed or repaired here.
- Full dashboard: 460 passed, 13 failed, 2 asynchronous errors. Exact base:
	451 passed, the identical 13 failed test names, and 2 asynchronous errors.
	Failures involve ChatInput, attachment validation, login/idle handling, and
	registration. The asynchronous errors report missing `getAllAccounts` on a mock.
- Measured added executable lines: context 11/11, endpoint 96/96, UI 70/70.
	Changed-line execution is not a claim of full branch/path coverage. The complete
	verification gate remains open because the full suites and visual acceptance
	are not green.
- Desktop panel screenshot inspected. Mobile interaction checks pass, but the
	panel screenshot is obscured/clipped by the surrounding layout; mobile visual
	acceptance remains open. No unrelated layout changes were made.

Logs and structured reports remain in `/tmp/ato-962-*`. The manual preview is
`http://127.0.0.1:4176/` (dashboard only; API/auth configuration is still required
for live data). SQL Server runtime behavior and a live full-stack browser round trip
have not been verified in this change.

## Manual Acceptance

Open a seeded system's Narratives page and expand a control. Confirm that existing
owner text is displayed separately, an unflagged empty control does not request owner
input, and a flagged empty control does. Interrupt the business-context API, confirm
a retrieval error, restore it, and retry/refresh to recover. Save and flag using an
assigned Mission Owner and ISSM respectively; verify unauthorized users are rejected.

Publication is blocked by the incomplete gates above and pending manual acceptance.
Do not represent this branch as merge-ready. An exact PR preview and explicit
approval are required before any push or PR creation.