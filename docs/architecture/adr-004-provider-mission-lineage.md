# ADR 004: Provider-to-mission lineage and mock-defined UI

**Status**: Planning direction approved; implementation pending  
**Feature**: [079](../../specs/079-provider-system-workflow-consolidation/spec.md)  
**Baseline**: `13204325`

## Context

Provider offerings, reviewed source processing, immutable releases, hosting
assignments, adoption and responsibility reconciliation already exist. Their
connections to permitted mission evidence and approved document/export output
are incomplete or unverified. Rebuilding the product would discard useful
authorization, tenant isolation, history and transactional review behavior.
Keeping confusing old screens would fail the user's required mock UI.

## Decision

Implement the [provider and Systems mocks](../design/index.md) as the UI
specification. Reuse or refactor underlying services and selectively rebuild
components to meet it. Prove one complete CSP -> Mission Owner -> document/export
slice before broad screen replacement.

Retain separate states for provider publication, allocation, mission association,
adoption, responsibility confirmation, document approval, export, eMASS
receipt/import/reconciliation and authorization. Connecting cloud resources is
neither authorization nor cATO.

Generate previews and artifacts from the same reviewed version manifest.
Provider source facts must reach output via retained context/adoption references,
not provider-name heuristics or export-time authorization dates. Evidence
references need explicit access/distribution semantics.

Consolidate readiness into a server-owned purpose-specific assessment composed
from existing validators. Keep profile completion, phase gates and schema
validity distinct. Unknown/failed required checks cannot count as passed.

## Alternatives rejected

- Full rewrite: unnecessary risk to working service contracts, isolation and
  history; no evidence that it is required.
- Cosmetic restyling of old pages: does not satisfy the required mocks or repair
  disconnected data paths.
- New parallel workflow engines: duplicate authority and indefinite migration.
- Deleting all association models: placement, subscription and adoption can
  represent different domain purposes; prove redundancy before deletion.

## Consequences and gates

Each changed unit needs current-code evidence, alternatives, dependency/caller
trace, migration, tests, cutover/retirement owner and rollback. Old workflows are
removed after replacement acceptance or retained only behind explicitly bounded
compatibility tasks. Historical records remain readable.

Verify full-host authorization/tenant isolation, actual generated output,
desktop/mobile mock fidelity, keyboard interactions and local manual review.
External eMASS acceptance remains unverified until actually observed.
