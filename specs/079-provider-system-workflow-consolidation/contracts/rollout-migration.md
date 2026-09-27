# Migration, compatibility, rollout and retirement

## Before mutation

Inventory records/callers on an isolated copy: provider releases/adoptions,
capability subscriptions, organization placements, exported source references,
and role/evidence grants. Count collisions, missing references and legacy rows.
Do not mutate production or infer missing authorization facts during planning.

## Expand, migrate, cut over, retire

1. Add nullable provenance, package-purpose and snapshot metadata using existing
   schema-addition patterns for SQLite and SQL Server. Repeated upgrade must
   preserve reviews, hashes, history and files.
2. Backfill only verifiable relationships. Mark ambiguous lineage unresolved;
   retain old IDs and audit provenance. Produce a reconciliation report.
3. Use temporary adapters/dual reads with an owner and removal criterion.
   Canonical writes are singular; do not create two publication/adoption engines.
4. Introduce mock-defined screens behind the smallest necessary rollout gate.
   Existing URLs redirect to the matching authorized new task, preserving
   workspace/system identity, query, history and permitted operations.
5. Pilot the deterministic slice and then full mock screen coverage. Review
   denied/error/stale states, export contents, data migration and historical reads.
6. Remove superseded editors/routes/hooks/state/styles/endpoints/dependencies
   only after import/runtime/other-client/job/MCP usage is traced and migrated.
   Retain read access to history; a redirect can outlive a retired editor.

## Measurable cutover gates

- Source-to-target record counts reconcile with explicit exception list.
- No lost version/actor/hash/tenant identity in migrated records.
- No legacy writes from tested clients/jobs/tools after canonical cutover.
- All old deep-link tests pass; no unapproved mock deviations.
- First slice and affected regression suites pass; manual review is offered and
  user acceptance recorded separately.
- Each remaining compatibility path has a named issue/PR and exit condition.

## Rollback

Disable new navigation only to an authorized, semantically safe compatible
surface. Retain additive records and version snapshots; do not destructively
down-migrate or return to fabricated authorization metadata. If a critical
export path cannot operate truthfully, disable final generation with an explicit
error rather than fall back to known-invalid output.

Keep rollout health distinct: errors/latency, unknown readiness checks,
unresolved provenance, legacy reads/writes, job retries, denied evidence and
unreconciled package outcomes. Structured logs contain diagnostic IDs, not
PII/CUI/source text.
