# Evidence catalog contract (US7)

Status: implemented and locally verified, September 28, 2026.
Parent feature: [azurenoops/spin_agent#216](https://github.com/azurenoops/spin_agent/issues/216).
The new user-story issue requires external-write approval.

## Existing behavior verified

- `EvidenceRepository.tsx` separately loads the mission list/summary and
  `ProviderEvidencePanel`. Manual and automated records are independently paged
  by `DashboardEvidenceEndpoints.cs`; merging those pages is not global paging.
- Manual list rows currently return a null control ID even when they have an
  implementation FK. Detail resolves that FK. Automated list membership checks
  matching control IDs; the catalog must use actual system provenance instead.
- `EvidenceArtifact` has uploaded-by metadata, file hash, system/target FKs, and
  retained `EvidenceVersion` rows, but no authoritative owner/review verdict.
- The upload dialog sends `controlId`; the handler currently requires an
  implementation/capability ID. Resolve and validate the selected control in the
  route system rather than leave the catalog upload workflow broken.
- `ProviderEvidenceSharingService` authorizes the mission, exact assignment,
  tenant/system, assignment revision, offering lifecycle, and active sharing.
  `SummaryContentAsync` rechecks the grant and content hash. Current permission
  is `ApprovedSummaryOnly`; no mission private-attachment route exists.
- Control validation links are a separate existing store. Upload permission is
  not validation-link permission. Link targets must be validated, not assumed
  authorized because their parent control is accessible.

## Read projection

Root: `/api/dashboard/systems/{systemId}/evidence-catalog`.

List query: `view=all|system|provider`, `search`, `family`, `category`, `source`,
`dateFrom`, `dateTo`, `sortBy`, `sortOrder`, `page`, `pageSize`.
The client omits unset optional values; sending empty date strings is invalid for
the endpoint's nullable date binder. The real client was smoke-tested against
the running SQL-backed API, in addition to fixture-based browser verification.

Response includes:

- Explicit `systemId`; server-resolved action permissions and denial reasons.
  List permissions include `canUpload/uploadReason`,
  `canManageEvidence/manageReason`, and `canManageLinks/linkReason`.
- Per-source state (`available`, `denied`, `unavailable`) with safe message and
  retry guidance. Cancellation is not converted to a source failure.
- Filtered tab counts before paging. Counts affected by unavailable/denied
  sources are null, not zero; a separate available-record total may be shown as
  partial. No complete-count or no-records claim when a required source failed.
- A globally sorted, deduplicated page from the accessible source union. Default
  order: descending upload/collection/sharing date, then source-qualified ID.
  Counts and rows use identical filter inclusion rules.
- Stable IDs: `artifact:<id>`, `automated:<id>`, `provider:<shareId>`. Deduplicate
  only identical qualified IDs. Equal hashes do not collapse distinct ownership
  or grants. Provider versions are identified by their retained share IDs.
- Rows: readable name, source kind/label, permitted date metadata, category,
  distinct linked controls and titles, next action, and availability state.
  Names use file names, automated descriptions/types, or approved summary text;
  no private provider attachment name is inferred.
- Attention count reflects known missing system control links after filtering.
  Provider link metadata that was not shared is unknown, not a missing link.

Detail route: `GET .../evidence-catalog/{qualifiedId}`.

Reauthorize on every request. Return the exact system and qualified identity,
public provenance, permitted actions/reasons, dates, uploader/collector/approver
as distinct roles, known owner or null, retained version/hash, existing control
links, actual history, and current file/summary access. Never return a storage
path, storage credential, private attachment, or unapproved provider finding.
Unknown owner/review/relevance/currency must remain null/unknown.

## Access and mutations

- Use existing authenticated file/version download routes and the provider
  summary content route; never link to storage keys or use an unauthenticated
  iframe to bypass the API client. Recheck scope/grants at content access.
- File availability is established using the storage provider for system
  artifacts. An unavailable check is not a successful file-exists result.
- Provider summaries display "Summary only" and expose the protected summary
  download only. A revoked/denied record displays access unavailable without
  retaining previously loaded protected details. Do not implement a new request
  workflow or grant private attachment access.
- Upload, replacement, deletion, and collection retain their server operation
  policies and audit/version behavior. Validate target system and tenant,
  evidence identity, related control/capability, and available revision/hash
  preconditions. Surface 400, 403, 404/410, 409, and unavailable-source errors.
- Replacement accepts optional multipart `expectedHash`; deletion accepts
  optional query `expectedHash` for existing callers. The catalog always sends
  the retained hash. The service validates tracked state, and content hash and
  deletion state participate in EF optimistic concurrency at persistence.
- `POST .../evidence-catalog/{qualifiedId}/links` accepts
  `{ "controlId": "AC-2", "expectedHash": "<retained hash>" }`. This route supports
  system uploads only, reuses validation links, and validates the artifact in
  the same persistence transaction. Workspace linking uses the independently
  resolved validation-link permission; legacy linking retains Auditor/Admin
  policy. It is not granted by evidence-management permission alone.
- Reuse control validation links for authorized system-artifact associations;
  retain direct/capability/automated links as distinct provenance. Linking a
  provider summary is unavailable unless the existing server contract supports
  that source type. Do not relabel it a customer artifact.
- Automated records are included only through a system-owned assessment.
  Collection requires an existing assessment and a configured subscription;
  failed collection does not persist an error snapshot as evidence. Automated
  records currently have no catalog file download route, so file availability
  remains `Unknown` and download is disabled, rather than fabricated.
- Manual artifacts do not have an authoritative numeric current-version or
  separate owner field. Those detail fields remain null; retained hashes and
  historical version metadata remain available.

## UI state and semantics

One catalog with All evidence, System evidence, Provider shared. The mock's
provider empty state replaces the table in that tab. No pagination for empty
results. Filters/page and selected qualified ID are URL state; tab changes inside
the drawer replace its history entry. Scope changes abort reads and clear all
rows, details, and forms. Keyboard focus is trapped and returned on close.

Overview, Linked controls, History contain only persisted/projection-backed
values. Size, uploader, exact timestamps, hashes, versions, and technical
provenance are detail content. Record presence and sharing approval never imply
assessor acceptance, control satisfaction, package readiness, or authorization.

## Verified downstream connections and gaps

- `SspExportService.Evidence.cs` retains approved provider summary provenance in
  SSP manifests/back-matter and revalidates grant/hash for retained exports.
- `AuthorizationPackageService.GenerateEvidenceManifestAsync` includes uploaded
  artifact metadata/hashes; `PackageBackgroundService` can bundle evidence files
  and OSCAL documents. This is package preparation, not eMASS submission.
- `AssessmentArtifactService` records assessor determinations and evidence IDs;
  assessment snapshot hashing covers automated `ComplianceEvidence` hashes.
- `EmassExportService` produces current control/POA&M Excel data, not a complete
  universal evidence-version manifest.
- No verified automatic translation of `ControlValidationLink` into SSP
  supporting citations or the package evidence manifest was found. Provider
  summaries, uploaded artifacts, and automated records have different consumers.
  Do not claim a newly added catalog link updates every downstream document.

## Local verification

- 44 production-route integration tests passed (catalog, evidence CRUD,
  provider-summary sharing/revocation, and validation-link authorization).
- 57 focused frontend tests passed; TypeScript checking and production dashboard
  build passed. Existing build warnings are not suppressed.
- Desktop/mobile browser cases passed in light and dark themes, including
  refresh/Back/Escape, focus trapping/restoration, the alternate provider empty
  state, and protected-download revocation clearing.
- Local API image `ato-copilot-mcp:evidence-catalog-20260928` is deployed with the
  previous runtime settings and volumes. Health and catalog smoke requests pass.
- The currently shared browser account has only a Flankspeed provider workspace.
  Its organization route is denied. No access grants were created to bypass that
  boundary; manual organization testing requires an authorized account.
- Whole-solution/unit-project compilation remains blocked by pre-existing
  `CapabilityResponsibilityResponse.PendingImpacts` constructor mismatches in
  `SystemSecurityCapabilitiesTests` and `ProviderEvidenceDocumentTests`.
  The production projects and focused integration suite compile successfully.
