# System policy workspace contract

September 28, 2026; implemented locally after document-first specification.
External issue creation/linkage remains pending explicit approval.

## Verified starting point

The LegalRegulatory page uses the organization Policy component library and
component system assignments. Quick Add Common can create a shared source before
assignment. Current assignments contain IDs, boundary scope, created date and
actor only: no system rationale, retained source, or applicability review.
Source records have created/modified timestamps, not native numbered versions.
Generic component assignments can auto-link capabilities and cascade narratives;
those side effects must not silently replace approved narrative content when a
user is merely recording policy applicability. Existing policy membership can
also be indirect through capability links; do not pretend such a row is unlinkable.

## Additive persistence

Extend `ComponentSystemAssignment` with nullable policy-reference metadata and an
optimistic revision, not a second policy library. Retain source ID, fingerprint,
capture time, and safe source fields with the rationale (required, 1-500 trimmed
characters). Existing rows remain explicitly unretained until an authorized,
explicit save; never invent a historical revision during a read.

Changing rationale preserves retained source content. Library edits must produce
a visible source-changed indicator and cannot replace the retained snapshot.
Durable audit history records create/edit/unlink with actor/system/reference and
source revision. Unlink keeps the shared policy and other systems' assignments.

Implemented fields on the existing assignment are `PolicyRationale`,
`PolicySourceSnapshotJson`, `PolicySourceRevision`, `PolicySourceCapturedAt`,
`PolicySourceModifiedAt`, `PolicyReferenceKey`, and optimistic `PolicyRevision`
(default zero). A filtered tenant/reference unique index protects concurrent
creation. Existing null metadata is not synthesized on reads or rationale edits.
Assignment-less legacy rows have stable `legacy:<policyId>` IDs; capability-only
rows use `indirect:<policyId>` and cannot be directly edited/unlinked.

## API surface

Root: `/api/dashboard/systems/{systemId}/policy-workspace`.

- GET root: system identity/name, permissions, assignment list, filtered total,
  unfiltered total, page/pageSize. Filters: search, status, sourceChanged.
- GET `/library`: authorized Policy sources, search by name/topic, paging and
  already-linked flags. The client retains the existing curated common-reference
  suggestions separately; suggestions are not persisted or treated as sources.
- GET `/sources/{policyId}`: authorized current source, actual revision token,
  readable created/updated version label and related controls.
- POST `/references`: `{policyId, expectedSourceRevision, rationale}`. Reject
  stale source/duplicate or unauthorized assignments. Return the saved reference.
- GET `/references/{referenceId}`: exact retained snapshot, current source/change
  indication, rationale, membership kind, related controls, audit history,
  permissions and scoped unlink impact.
- PATCH `/references/{referenceId}`: `{expectedRevision, rationale}`; retain source.
- DELETE `/references/{referenceId}?expectedRevision=...`: unlink only, audited.
- Source creation reuses `ComponentService.CreateOrgComponentAsync` and existing
  org-library permission, with name (200), topic/subtype (100), description (2000)
  limits. A scoped POST `/library` may wrap that operation for explicit permission
  projection and return the newly created source, not an assignment.

Permission flags/reasons are server authoritative: canAssign, canCreateLibrary,
canEdit and canRemove are not inferred from the persona. Return 403/404 for
denied/inaccessible scope, 409 for duplicates/stale revisions, 400 for validation,
and honest failures/retry states. Source reads never return another system's
private assignment metadata through an organization library projection.

Applicability review is **not implemented**. Return an informational gap, not
functional Approved/Rejected states. Source Active remains independent.
Common suggestions require explicit library creation confirmation if absent;
no write occurs from tab selection, Continue, Back or Cancel.

### Compatibility and source protection

- Generic component assignment/unlink calls for Policy sources return
  `409 POLICY_WORKSPACE_REQUIRED`, directing callers to the scoped, revision-
  checked policy reference API. Non-policy component workflows are unchanged.
- Inventory/intake policy actions hand off to this guided flow. Intake opens a
  new tab so wizard progress is preserved; a selected source is freshly read and
  is not assigned automatically. Existing direct references open their details.
- Shared Policy deletion is rejected while direct system references exist.
  Policy reclassification is rejected so retained references stay accessible.
  Ordinary source metadata edits remain permitted and flag source changes.
- The organization library displays guard/preview failures rather than silently
  swallowing them. Removal audit records survive later deletion of an unused
  shared source.
- `alreadyLinked` denotes a direct assignment only. Indirect/assignment-less
  policies can receive an explicit rationale and snapshot; direct rows replace
  pseudo-rows, and unlink can reveal the still-existing indirect membership.

## UI and downstream meaning

One Add policy entry point, one organization-library link, no empty table/search/
pagination. Show one drawer step at a time and an accurate rationale counter.
Selection/review detail state is URL-addressable; lists retain filters/page/focus.
Abort requests and clear forms/detail on system changes. Use existing site themes.

Show related controls only when backed by actual capability mappings. No separate
supporting-reference field currently exists on assignments; don't imply storage.
The collapsed SSP explanation must describe verified downstream consumers and
gaps, not claim assignment saves make a system ready for eMASS or authorization.

## Verified downstream findings

- `SspService` can render active components as inventory with name/type/topic/
  description/owner, not retained policy-reference citations.
- `OscalSspExportService.RecordedSources` emits generic component identities and
  descriptions. This is not an applicability review or rationale lineage.
- `NarrativeTemplateService` enrichment selects Thing, Person, and Place
  components, not Policy components. A policy link does not currently insert
  policy context into implementation statements.
- Retained source/rationale metadata added here has no verified consumer in SSP
  supporting references or every eMASS output. This remains an explicit gap.
- Existing legal-authority mapping and unsupported historical section guidance
  are separate issues (#970/#975 in azurenoops/spin_agent); this UI does not claim
  automatic mapping or a specific mandatory SSP section.

## Manual acceptance

Open `/workspaces/organizations/{tenantId}/systems/{systemId}/legal` with an
identity assigned to that organization/system. In this local session the
dashboard is `http://127.0.0.1:5197` and API is `http://127.0.0.1:3002`.

1. Confirm the empty page has one Add policy action and no empty list/search.
2. Choose an existing source, inspect View source, Continue, type a rationale,
   verify its real counter and preview, then save.
3. Verify the populated row and retained details. Refresh/Back/Escape must retain
   list state and return focus. Test narrow width and both themes.
4. Edit the shared source through an authorized library workflow, then refresh
   the reference: source-changed is visible and the retained content is unchanged.
5. Edit only the rationale; verify snapshot retention and audit history.
6. Test duplicate and stale-version requests, read-only actions, wrong-system
   links, and source/read failures. Failures must not become empty/success states.
7. Use Common references to search a suggestion. If absent, explicitly create a
   library source, verify return to selection, and Cancel without assignment.
8. Preview unlink impact, unlink, then verify the shared source and another
   system's assignment still exist. Indirect membership must remain explained.

The currently shared browser identity has only a provider workspace; an
organization-assigned identity is needed for manual system acceptance. No access
grant is added or bypassed by this implementation.

## Local verification record

- Policy, schema, guarded legacy mutation, and existing component integration
  tests pass (24 tests).
- Policy/transport/library/handoff and existing system-domain frontend tests pass
  (83 tests); TypeScript checking and production dashboard build pass.
- Browser tests pass for the full choose/explain/save/details/edit/unlink and
  library-create/return/cancel flow at desktop/mobile widths in light/dark themes.
- The tagged API image `ato-copilot-mcp:policy-workspace-20260928` is deployed via
  the existing local Compose configuration; health is HTTP 200. All seven policy
  metadata columns were read back from the configured SQL Server schema.
- The current provider-only browser cannot validate the target organization's
  policy records. Scoped raw access returns 404 rather than widening visibility.
- Full-solution compilation remains blocked by four existing missing
  `CapabilityResponsibilityResponse.PendingImpacts` arguments in
  `SystemSecurityCapabilitiesTests` and `ProviderEvidenceDocumentTests`.
  Production projects and the focused integration suite compile successfully.
