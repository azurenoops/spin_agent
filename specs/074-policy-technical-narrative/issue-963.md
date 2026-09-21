# Issue #963: Narrative Write Governance

Branch: `fix/963-narrative-governance`, originally based on PR #985 at `39cf777`.
PR #985 merged as `0ff7622`; its file tree is identical to `39cf777`.
Existing bug: https://github.com/azurenoops/spin_agent/issues/963.

## Verified Finding

DualNarrativeService.UpdateAsync directly writes fields, resets review state to
Draft, and saves without checking UnderReview or recording a version. This violates
Feature 024 FR-010 and Feature 074's independent authoring/history contract.

## Repair Contract

- Reject UnderReview dual edits and single regeneration before writes or model use.
- Preserve omitted halves, approved history, tenant scope, and author attribution.
- Allowed writes create immutable content/provenance snapshots and advance version.
- Expose version/review metadata and honor expected-version conflicts across HTTP,
  MCP, and dashboard writers. Concurrent persistence must not silently overwrite.
- Explicit regeneration may replace customized Technical content; ordinary automatic
  population must retain its existing custom-content protection. Never auto-approve.

## Validation Plan

Start with failing dual-writer lock tests, then version/snapshot tests. Add focused
regeneration, HTTP/MCP, concurrency, and browser regressions using synthetic data.
Run unit/integration/dashboard suites, TypeScript/build, and scoped browser tests.
Record manual acceptance and publication preview before any external writes.

## Implemented Contract

- Dual saves enforce review/version checks, append attributable post-edit snapshots,
  advance version, and preserve omitted halves and approved pointers.
- Manual Technical edits clear generated provenance and set manual customization.
- Single regeneration preserves an existing current-version snapshot (or captures
  the prior state when missing), then records the generated Draft as the next version.
- Document-source generation forwards the read version to the SSP generated writer,
  resolves the request author per invocation, and preserves governance error codes.
- `CurrentVersion` and `ApprovalStatus` are EF concurrency tokens. This is metadata
  on existing columns, not a schema/column addition. SQLite tests cover transaction
  rollback when concurrent content or review changes occur during model generation.
- Public/dashboard PATCH and dashboard single-regeneration return 409 conflicts;
  MCP Policy/Technical tools preserve the same error codes and expose version metadata.
- Dashboard edits pin the read version even across polling, retain text on failure,
  handle the API client's unwrapped error envelope, and disable review-locked editors.

## Verification and Remaining Gates

The final rebased backend run passed 5,712 unit tests and 889 integration tests;
20 RLS tests and 20 Nessus tests marked as requiring a Cosmos DB emulator were
skipped. The SQLite governance suite passed all 19 tests, including real MCP writers
and four model-duration races. The dashboard suite passed 486 tests. TypeScript,
the solution build, and the dashboard production build passed.
Playwright passed synthetic API-backed interactions at 1440px and 390px, covering
versioned writes, retained drafts after conflicts, and review locks. These are not
live-model or full-stack browser tests. No deployed data was changed.

Final focused coverage covered 125/147 changed executable C# lines (85.0%), using
93 unit and 19 integration tests. This is not complete branch/path coverage and does not
satisfy the Constitution's full modified-path coverage gate. SQL Server concurrency
and RLS behavior, full modified-path coverage, and manual acceptance remain open.
Screenshot inspection confirmed existing mobile table/summary clipping; desktop
conflict content and retained text were visible. No unrelated layout redesign is
included. Dependency installation reported 12 audit vulnerabilities; no dependency
changes were made by this fix.

## Local Manual Acceptance

Use this branch's dashboard at `http://127.0.0.1:4177/` with an authenticated local
API and synthetic system. The Vite server alone does not provide a test backend.

1. Edit Policy on a Draft; confirm Technical is unchanged, version increments, and
   history records the requesting author and both halves.
2. Edit Technical; confirm generated badges clear and the content is customized.
3. Start editing in two sessions. Save one, then save the other. Confirm 409, no
   misleading Saved indicator, and preservation of the rejected local text.
4. Submit for review. Confirm both editors and Regenerate are locked, and direct
   PATCH/MCP/regeneration requests reject the write without adding history.
5. On an Approved narrative, explicitly regenerate with and without document sources.
   Confirm a new Draft version, unchanged Policy/approved history, and correct author.
6. Preserve a rejected draft before reloading; compare and reapply intentionally.

Publication requires the user's approval of the exact push/PR preview. Manual
acceptance has not been recorded; any publication must remain a draft.