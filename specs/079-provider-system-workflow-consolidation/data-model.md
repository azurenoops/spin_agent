# Authority, retained versions, and lineage

Status: proposed completion of existing contracts, not a database migration.
Source paths and verification limits are in [research.md](research.md).

| Authority | Existing records/services to preserve | Required completion |
|---|---|---|
| Provider identity/offering | `ProviderOffering`, `ProviderAuthorizationService` | Service model and management arrangement independent of cloud/authority category. |
| External decisions | Provider authorization revisions with source citations | Title, issuer, type, dates, conditions and applicability from reviewed source only. |
| Boundary/service scope | Boundary revisions, `ProviderHostingScopeRevision` | Keep immutable predecessors; distinguish service eligibility from system authorization. |
| Source material | Retained package versions, manifest/content hashes, analysis checkpoints/candidates | Per-entry coverage and exclusions; review pins exact content/profile/version. |
| Published implementation | `ProviderCapabilityRelease`, catalog-context snapshot | Immutable release references remain readable after supersession/withdrawal. |
| Provider allocation | Hosting assignment | Identifies eligible tenant/system/scope revision; not a mission association. |
| Mission association | Provider mission relationship | Explicit authorized selection and retained scope, not auto-applied capabilities. |
| Applied provider release | `CapabilityAdoptionSnapshot`; subscription `CurrentAdoptionSnapshotId` and `AdoptionSelectionRevision` | Explicit selected adoption pins release/context/assignment; compare-and-update revision protects concurrency. Preserve prior snapshots; never choose newest provider release by timestamp. |
| Responsibility | `CapabilitySubscription`, confirmation, `ControlInheritance` projection | Exact reviewed revision and actor; no duplicate responsibility engine. |
| Organization placement | `SystemCapabilityLink`, organization `SecurityCapability` | Retain if distinct from provider adoption; retire only proven redundant writes. |
| Evidence | Mission artifacts/versions and provider finding evidence | Add explicit shareability/access and adoption/source lineage using existing storage. |
| Approved system content | Profile `ApprovedContent`, reviewed narratives, SSP sections | Export all scalar/structured fields from retained approved versions, not live drafts. |
| Export/package | `SspExport`, authorization packages, stored files/hashes | Purpose, validation snapshot, pinned source/version manifest; retain failed jobs distinctly. |
| eMASS reconciliation | Import session/conflicts, export workflow status | Receipt/import/reconciliation state requires recorded outcome, not download inference. |
| AO decision | Existing `AuthorizationDecision` | Separate server-authorized actor/action from package generation or recommendations. |
| Monitoring | Existing plan/watch/drift/significant-change and provider impacts | Executable rule version, evaluation/collection health, scoped targets and separate dispositions. |
| Recorded authorization facts (E2) | Existing `ProviderDecision` / offering overview / provider monitoring | Project validity, package readiness, and monitoring health independently. Do not store a composite health enum. |
| Scope fit (E6) | New `ScopeFitRecord` on the existing mission relationship / subscription | Declared use, cited offering version, cited adopted release, result `InScope`/`Exception`/`OutOfScope`/unevaluated, evaluator, evaluated-at, rationale. Not an `ImpactReview` disposition. No MO SSP body. |

## Invariants

- Every record has its existing tenant/ownership boundary; immutable references
  do not grant cross-tenant read access.
- Review approves a specific revision, not a mutable name. A changed hash or
  revision invalidates stale approval/confirmation.
- Adoption pins historical context; withdrawing a source opens review work,
  never deletes history or silently updates a submitted package.
- Evidence references retain source/version/control/duty attribution plus
  permission to view/include. An inaccessible citation is not usable evidence.
- A preview/export snapshot is a projection of reviewed records. Do not create
  a second editable authorization or capability catalog inside documents.
- New nullable lineage on legacy rows means unknown until reviewed. It must not
  become a manufactured "approved legacy release."
- Existing subscriptions with no current adoption selection require explicit
  re-adoption. Unsubscribe/reactivation clears selection through existing
  mutation paths; replay of an old idempotent request cannot restore selection.
- Internal record identity stays stable. OSCAL IDs must use persisted or
  deterministic identity mapping and resolve consistently across artifacts.
- Scope-fit results never default missing to in-scope. `OutOfScope` never
  mutates recorded decision validity, CRM allocation, or adopted release.
- Scope-fit citations are exact revisions. “Newest offering” is not a legal
  evaluation target.
- Offering monitoring health and scope-fit MUST NOT store or project Mission
  Owner subscription telemetry. Product-brief source of truth: no implied
  MO-sub telemetry.

## Proposed logical snapshot (not finalized DTO/entity names)

```text
System + workspace identity
  Package purpose + receiving-workflow selection
  Approved profile/narrative/assessment/risk version references
  Adopted release/context/scope/assignment references
  Reviewed decision source metadata and stable party references
  Confirmed responsibility revision references
  Authorized evidence version/reference manifest
  Readiness requirement results + evaluation timestamp/version
  Output artifact hashes + prior package reference
```

Extend existing export/package snapshot machinery where possible; final schema
and indexes follow safe legacy-data inspection in PRs 1A-1C. Do not add a generic
workflow database or dual-write business entities merely to implement the UI.
