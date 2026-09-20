# Issue #961: Truthful Narrative Provenance

## Contract

- `AiSuggested` records successful model generation of the current technical/combined draft,
  not baseline selection, attempted generation, approval, or implementation status.
- Deterministic templates and fallback text use `IsAutoPopulated`, with `AiSuggested = false`.
- Replacing technical/combined content manually clears AI and automatic provenance. Editing
  Policy alone preserves Technical provenance; the UI identifies AI assistance as Technical.
- Empty canonical content and migrated legacy rows are not proof of model generation and
  must not contribute to the dashboard AI count or display an AI-generated badge.
- No narrative content is added, removed, or rewritten merely to change provenance labels.
- Implementation and approval statuses remain independent. Existing review/grounding guards
  must continue to recognize automatic scaffold text even when it is not model-generated.

## Scope And Verification

Issue: https://github.com/azurenoops/spin_agent/issues/961

This branch started from main at `c8bdafc`. PR #976 for #960 merged as `c7d1d61`
on 2026-09-20; this patch is now rebased onto that prerequisite.
The dashboard intentionally requires nonempty canonical Technical content before displaying
AI assistance. Combined canonical persistence and provenance behavior passed focused tests.
Review/version governance changes from #963 are outside this patch.
No new schema, MCP envelope, role permissions, or tenant-filter changes are planned.

Scope decision (2026-09-20): complete baseline/capability generation, deterministic cascades,
manual dual edits, and Narratives-page labels/counts in #961. Keep the separately identified
OSCAL, document-source, and coverage-metric paths as explicitly deferred follow-up work;
do not introduce schema or unrelated test-suite repairs into this patch. Publication still
requires the user's approval of the exact PR preview and any non-green validation exceptions.

- [x] Baseline regression: deterministic scaffolds are Auto, never AI.
- [x] Capability model success/fallback regression and provenance persistence.
- [x] Manual dual-edit and migrated/empty/mixed-content regression coverage.
- [x] Dashboard count/badge regression and browser E2E.
- [ ] Backend unit/integration, dashboard typecheck/build, and scoped E2E validation.

Manual check: select a baseline, inspect Auto labels, regenerate using a configured model,
then edit the Technical narrative. Policy edits must not relabel Technical provenance.

## Validation Record

### Post-Rebase Validation (2026-09-20)

- Base: `c7d1d61` (#960 / PR #976). The rebase preserves canonical generation writes,
  canonical grounding reads, and the Policy-preserving regeneration browser regression.
- Focused backend unit classes: 74/74 passed. Baseline, capability-regeneration endpoint,
  and #961 assessment integration slice: 13/13 passed.
- Narrative dashboard tests: 9/9 passed. Dashboard production build passed, including
  TypeScript with `noEmit` enabled. There is no standalone `typecheck` script in this checkout.
- Combined Playwright Policy/Technical edit and regeneration flow: 1/1 passed. API responses
  are synthetic; real backend/model acceptance testing remains pending.
- Full backend solution with coverage: unit 5,677 passed / 1 failed; integration 833 passed /
  3 failed / 40 skipped. Unit request-size and two integration host-startup failures reference
  deleted temporary content roots. A system-profile P95 latency assertion also failed.
- Clean current-base full backend run: unit 5,617 passed / 49 failed; integration 832 passed /
  0 failed / 40 skipped. Working-directory and host-startup failures also occur on the base;
  counts vary, so the runs are not asserted to have identical failures. Existing test fixtures
  change process-wide working directories while other tests create web hosts.
- Full dashboard: branch 458 passed / 13 failed / 2 asynchronous errors; current base
  451 passed / 13 failed / 2 asynchronous errors. Failed test names and TypeError signatures
  match exactly. Failures are in auth, chat attachments, and system registration, not narratives.
- Combined backend Cobertura reports exercised 19/19 changed instrumented C# lines. Narrative
  UI coverage exercised 12/12 changed instrumented TSX lines; whole-page line coverage is
  54.63%, not 100%. These measurements do not imply coverage of every unchanged path.
- Manual preview: `http://127.0.0.1:4175/`. Live end-to-end testing requires a configured
  backend built from this branch and a model for successful AI generation. Manual acceptance
  and the previously recorded mobile clipping remain outstanding.
- Decision: prepare a draft PR preview only. Do not waive the all-tests-pass gate or publish
  without explicit approval of the exact preview and its validation exceptions.

### Pre-Rebase History

- Focused regressions passed after recorded failing tests for baseline creation, capability
  generation/fallback, deterministic cascades, manual edits, review guards, and batch replacement.
- Dashboard provenance rendering: 7/7 passed. Production TypeScript build passed.
- Scoped Playwright browser flow: 1/1 passed with synthetic API responses, including Policy
  and Technical edits. This is not verification against a live backend or model.
- Full backend unit run: 5,667 passed, 1 failed. The failure was
  `RateLimitingTests.ExemptEndpoints_AreNotRateLimited`, whose host content root pointed to a
  deleted `csp-coverage-*` temporary directory. All 5 rate-limiter tests passed in isolation.
- Full integration run: 833 passed, 2 failed, 40 skipped. The failures were a role-matrix host
  startup with a deleted `import-integration-*` content root and scan-import temporary-file
  cleanup. A focused branch run covering these classes and the changed integration slice
  passed 42/42; the two failing classes passed 33/33 on clean `c8bdafc`. Full-suite causes
  are not yet resolved, and clean-base full backend suites have not been verified.
- Full dashboard run: 456 passed, 13 failed, 2 asynchronous errors. Clean `c8bdafc` had
  449 passed with the same 13 failures and 2 asynchronous errors.
- Mobile screenshot renders, but existing wide-table/stat-label clipping remains at 390px.
- Modified-path coverage has not been measured. Manual acceptance testing is pending.

## Outstanding Provenance Boundaries

The contract above is the intended behavior, not a claim that every writer now satisfies it.
The final audit identified these existing paths, which this patch has not changed:

- `OscalDecompositionService.DecomposeAsync` can fall back to the original narrative after
  a model/JSON failure. `ApproveAsync` subsequently sets `AiSuggested = true` unconditionally;
  the fallback derivation marker is not persisted with the draft fragments.
- `DocumentNarrativeGenerateAdapterTool` knows whether source-based model generation succeeded,
  but saves through `SspService.WriteNarrativeAsync`, which clears automatic/AI provenance.
- Capability coverage metrics still consume raw `AiSuggested`, unlike the Narratives page's
  canonical-content and migration checks. Import and rollback provenance are not fully audited.
- Batch template replacement compares against a template regenerated from current system
  metadata; behavior after metadata changes between baseline selection and replacement is
  not covered by the new regression.

The all-tests-pass gate is not met. Integration with #960 has passed focused validation;
the additional provenance boundaries above are deferred by the recorded scope decision.
Batch replacement deliberately retains conservative exact-template matching: metadata drift
can cause a scaffold to be skipped rather than risk replacing completed automatic text.
No push or PR has been performed for #961. Draft publication requires explicit approval;
merge readiness and manual acceptance are not claimed.