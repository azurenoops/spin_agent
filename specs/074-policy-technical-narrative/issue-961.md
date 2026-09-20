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
on 2026-09-20; this patch will be rebased onto that prerequisite before final validation.
The dashboard intentionally requires nonempty canonical Technical content before displaying
AI assistance. Combined canonical persistence and provenance behavior must be verified.
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

Publication is blocked: the requested all-tests-pass gate is not met, and integration with
#960 still needs verification. The additional provenance boundaries above are deferred by
the recorded scope decision. No push or PR
has been performed for #961.