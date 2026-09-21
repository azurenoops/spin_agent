# Narrative Provenance Follow-Up

Authorized on 2026-09-21 after issue #962 suite repairs. Prerequisite: merged
PR #984 / issue #961. This continues the existing provenance contract, not a new
feature or user story.

## Contract

- Persist OSCAL model/fallback origin independently of confidence. Old drafts
  have unknown origin; approval must not promote them to model-generated content.
- Document-source model success retains AI provenance; deterministic fallback is
  automatic, not AI. Manual narrative writes still clear generation provenance.
- Capability metrics count only nonempty canonical, non-migrated model content.
- Import replacements must not inherit the replaced text's provenance. Rollback
  must restore content and its provenance together, with auditable prior state.
- Preserve tenant scope, authorization, and Policy/Technical ownership boundaries.

## Checks

Start with OSCAL persisted draft/reload/approval tests for model output with and
without confidence, invalid JSON, and model failure. Persist an explicit fragment
derivation marker with a conservative Unknown default and upgrade existing stores.
No confidence-derived backfill or model-origin inference is permitted.

Subsequent document, metric, and import tests must fail before production changes.

Audit decision: NarrativeVersion currently stores text without provenance. Add one
nullable JSON snapshot column for canonical Policy/Technical content, legacy
projection, migration/customization state, and generation flags. Snapshot existing
version writers at the same time as their content; never infer legacy origin.
OSCAL imports record before/after versions and the import run ID in the change
reason. Technical replacements clear old generation flags; Policy-only imports
preserve Technical provenance. Preview/unchanged imports do not create history.
Rollback restores the snapshot and copy-forwards a new version; legacy versions
restore their text with unknown (non-AI, non-automatic) provenance.

Full unit/integration/dashboard suites, production build/typecheck, scoped browser
checks, and manual acceptance remain publication gates.

Merged-fixture compatibility: #984's provenance page tests mock business-context
reads but omit #962's independent flagged-control read. Add that mock returning
an empty list; retain every provenance assertion and rerun the full dashboard suite.

The full dashboard repeat exposed a drawer fixture race: its helper waited for the
API invocation, not the subsequent loaded render. Await the disclosure control
before assertions; retain all collapsed-state and remap checks.

Batch audit check: repeated control requirements must see versions staged earlier
in the same import, not only database rows. Each version number must remain unique
and retain its own before/after content; test a two-update batch before correction.

## Verification (2026-09-21)

- Final full backend run: 5,704 unit tests passed; 873 integration tests passed,
  40 SQL Server RLS tests skipped. Final test command rebuilt the solution.
- Full dashboard: 483 tests passed across 67 files. TypeScript and Vite production
  build passed; existing bundle-size warnings remain.
- Chromium synthetic-API browser flow: desktop 1440px and mobile 390px both passed.
  Mobile screenshot still shows surrounding layout clipping; visual acceptance is open.
- Combined unit/integration Cobertura: 230/230 changed executable C# lines covered
  relative to origin/main. This is line coverage, not proof of complete branch/path
  coverage or SQL Server execution. Earlier dashboard changed-line measurement is
  recorded in issue-962.md; no fresh frontend coverage measurement was made here.
- Repeated-control audit regression failed with versions `{1, 2, 2, 3}` before
  the local-tracker fix; focused import/governance suite then passed 43 tests.
- Evidence: `/tmp/ato-followup-release-backend.log`,
  `/tmp/ato-followup-release-coverage/`, `/tmp/ato-followup-final-ui-tests.log`,
  `/tmp/ato-followup-release-ui-build.log`, `/tmp/ato-followup-final-browser.log`.

## Manual Acceptance and Remaining Gates

1. With an authenticated local backend, open a synthetic system's Narratives page.
   Verify flagged/no-draft, saved-draft, failed-read/retry, and refreshed context.
   Copy owner context explicitly and verify only Policy changes.
2. Generate document-source text with model success and fallback; verify AI versus
   automatic labels, then manually edit Technical and verify provenance clears.
3. Decompose, reload, and approve model/fallback drafts; verify retained origin.
4. Preview then import Policy-only and Technical OSCAL changes. Verify before/after
   history, unchanged-import idempotency, under-review rejection, and rollback.
5. Validate the additive schema on a backed-up SQL Server test database, including
   a second startup; run the 40 environment-dependent RLS tests there.
6. Review desktop and mobile layout. Browser interaction passes do not resolve the
   observed mobile clipping. Manual acceptance is not yet recorded.

The local Vite server is at http://127.0.0.1:4176/; it is frontend-only until an
authenticated backend is configured. Automated browser tests intercept APIs with
synthetic fixtures, and no live model or cloud service was used. Full branch/path
coverage remains unverified. Do not describe this branch as merge-ready yet.