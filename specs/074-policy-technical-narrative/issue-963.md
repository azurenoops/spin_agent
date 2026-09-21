# Issue #963: Narrative Write Governance

Branch: `fix/963-narrative-governance`, stacked on PR #985 at `39cf777`.
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