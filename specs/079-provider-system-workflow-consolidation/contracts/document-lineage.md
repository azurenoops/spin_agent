# Reviewed data to document/export contract

The first acceptance slice must parse real generated output. UI badges, mocks,
JSON DTO tests, and schema validity alone do not prove semantic traceability.

| Source of authority | Document destination | Required assertion |
|---|---|---|
| Approved Mission & Purpose profile | SSP system identification/description and mission | Distinctive approved name/mission survives save, review, reload, preview and export. |
| Approved Users & Access and retained child rows | SSP user populations/access description | All reviewed categories and counts survive; draft row edits do not leak. |
| Approved Environment & Deployment plus adopted hosting scope | SSP environment/deployment and boundary context | Record the actual system relationship; no subscription-equals-authorization claim. |
| Approved Data Types, categorized information types/privacy records | SSP information description and categorization references | Preserve source and approved relationships; unknown mappings are readiness gaps. |
| Approved Ports/Protocols/Services and interconnections | SSP tables and interconnection documentation | Structured rows, endpoints, approval/source versions remain intact. |
| Reviewed provider decision linked through adoption/context | OSCAL leveraged authorizations and SSP source references | Exact title/type/issuer/date/conditions; stable resolvable party IDs; no export-time source date. |
| Published release + pinned mission adoption | SSP implementation/control narrative provenance | Selected version, scope and service preserved even if provider publishes a successor. |
| Responsibility confirmation + mission completion evidence | CRM/control implementation/customer duty sections | Provider/shared/customer duties and unresolved work visible, not blanket inherited coverage. |
| Permitted provider evidence + mission artifact versions | Evidence index/manifest and allowed attachments | Hash/version/access classification and provenance resolve; private files excluded without masking the gap. |
| Reviewed narratives and SSP sections | SSP control implementation text | Approved content wins over draft, and required section absence blocks final readiness. |
| Reviewed assessment/SAP/SAR, findings and POA&M | Assessment artifacts and package | Approved/scheduled/required fields and source versions match; risk decisions not inferred from provider findings. |
| Explicit receiving-workflow outcome | Export/reconciliation history | Generated/downloaded is not received/accepted/imported. |

## Snapshot and generation

### Requirement coverage integration under validation

The [spec 074 continuation](../../074-policy-technical-narrative/contracts/requirement-coverage.md#documents-and-export)
extends this lineage contract with pinned catalogs, versioned requirement
responses, evidence mappings and review history. This integration is implemented
on the feature branch but awaits final validation and manual acceptance.
Parent requirements remain under their parent; enhancements
remain separate controls. Both must survive reviewed SSP/eMASS preparation
output with genuine catalog identifiers. Include these inputs in readiness
freshness without reinterpreting retained archives or promoting working drafts.

Resolve one authorized source/version manifest before preview/final generation.
Preserve it with purpose, validator results, generation state, and output hashes.
Detect source changes between validation and generation: retain pinned snapshot
or reject stale input explicitly, rather than silently switching to newest data.
Record failures atomically; a failed required artifact cannot yield a
success-shaped completed package.

Use schema-supported fields/references. If a required authorization date is
unknown, omit an optional structure only where policy/schema permits and flag
the gap; otherwise fail final output with actionable validation. Never invent
a date, title, issuer, party or authorization category.

## Golden synthetic values

Use an explicitly synthetic decision titled `DEMO Shared Services Decision`,
issuer `DEMO Review Authority`, source date `2025-04-17`, and a distinct export
clock. Assert exact values, party reference resolution and adopted release IDs
in OSCAL; extract DOCX content/ZIP manifest and inspect PDF rendering. These
are test fixtures only, not claims about any actual government provider.

Change the source date/title to null, make two sources conflict, withdraw a
release, restrict evidence, edit profile child rows, and interrupt generation.
Verify truthful findings, retained earlier packages, no source/AO creation,
and deterministic repeated identities in each case.
