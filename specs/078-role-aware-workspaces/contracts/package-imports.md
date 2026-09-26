# Provider package ingestion and review contract

Implementation authorized locally on 2026-09-23. No deployment, GitHub writes or
push authorized. This extends Feature 048 US9 and Feature 078 #1026/#1027/#1028
under #1002; it does not introduce another capability release or system ATO model.
The revised scope in `docs/design/provider-source-package-flow.md` supersedes
earlier automatic-publication and import-deferral language.

## Decisions and compatibility

- Both existing multipart ingress paths persist original uploads, hashes and a
  manifest before acknowledging receipt. `Prefer: respond-async` requests receive
  `202`, `Location`, and `{ status: "success", data: PackageStatus, metadata }`.
  The Dashboard opts in explicitly. Non-negotiating callers retain their legacy
  tally response after processing the durable package synchronously; all results
  are staged for portal review, never published. A missing idempotency key uses
  a provider/content-derived key for legacy callers; new clients send a stable
  `Idempotency-Key`. Same key/different content is `409`.
- The bookmarked legacy component-library import button navigates to the same
  portal import/review page; it no longer starts a request-lifetime upload or
  presents an "Import complete" catalog-inventory preview. Legacy API callers
  retain negotiated compatibility independently of this UI handoff.
- `POST /api/csp/onboarding/atos/upload` remains optional and available before
  activation. `POST /api/csp/inherited-components/import` retains the active
  profile requirement. Completing onboarding only activates the profile.
- Staged candidates live separately from globally readable provider catalog
  rows. This prevents old direct component-publish or capability-review APIs
  from bypassing package approval. Publication materializes only approved
  candidate revisions, then uses existing working-revision/preview/approval/
  release services for capability releases. Existing published records and
  unrelated drafts are never overwritten or blanket-published.
- Duplicate resolution is explicit rejection, keeping a distinct record with a
  rationale, or reusing an eligible published contributor. No name-based merge.
  Replacing a published record must not mutate it during analysis or review.
- Authorization-reference metadata is source-backed proposed/recorded reference
  information, not verification of a decision and not a mission-system ATO.
- The exact Dashboard wizard route `/onboarding/csp` (including a trailing slash)
  selects ordinary CSP request scope, just like `/workspaces/csp`. Shared
  transport strips stale organization/support selectors and cancels responses
  after a workspace change. The selector is not authority: the server still
  verifies directory identity, role, provider ownership and onboarding access.

## Persistent state and recovery

Add provider-owned package, entry, candidate and approval persistence through
the existing EF model/schema-addition pattern for SQLite and SQL Server. Reuse
`IFileStorageProvider` for opaque generated storage keys; retain SHA-256, byte
length, MIME, original name and archive path. Never derive storage paths from
untrusted archive names. Schema changes are additive; no existing data reset or
publication backfill. Rollback must retain new tables and stored sources until
explicit retention/backup decisions are made.

SQLite startup also applies `CspInheritedCatalogSchemaAdditions` before
workspace initialization. The legacy migration baseline omits existing CSP
catalog tables needed by duplicate detection and canonical publication.
The additive catalog bootstrap repairs that prerequisite without resetting
provider profiles, receipts, source artifacts or existing catalog records.
Tests cover the migration baseline and repeated startup, not only databases
created directly from the current EF model.

The package ledger is the durable queue. The existing wizard queue persists job
metadata but dispatches through an in-memory channel without startup recovery;
it cannot alone satisfy this workflow. A bounded database-polling worker claims
received/retryable packages with an optimistic lease/version, recovers expired
leases after restart, and checkpoints entries/candidates atomically. Stable
entry/candidate identities prevent duplicate results after a lost commit response.
Cancellation preserves unfinished work. Explicit retry processes only unfinished
entries, preserves reviewed candidates, and invalidates approvals if source or
candidate revisions change. Database writes use EF execution strategies.

The package ledger also retains a private `AnalysisCheckpointJson` generated
analysis checkpoint, separate from human-edited candidates. It carries prior
analysis output/budget state for unfinished-entry retries; original bytes remain
in file storage, not base64 database payloads. Schema upgrade adds the nullable
checkpoint column on SQLite and SQL Server. A previously processed package
without a checkpoint cannot safely claim unfinished-only recovery: retain its
sources and return explicit resubmission guidance with a new idempotency key,
instead of silently rerunning all completed analysis.

- Processing: `Received`, `Processing`, `ReadyForReview`, `NeedsAttention`, `Failed`.
- Entry coverage: `Pending`, `Processed`, `Unsupported`, `Unreadable`, `Failed`,
  `Excluded`. Every exclusion has an explicit reason; partial coverage stays
  visible. Processed with zero candidates is successful analysis, not failure.
- Review: `NeedsReview`, `Reviewed`, `Rejected`, `Approved`, `Published`.
- Publication: `Unpublished`, `Publishing`, `Published` (failed/retryable stages
  remain explicit and must not claim the entire set was published).
  `PartiallyPublished` distinguishes a published subset from remaining
  non-rejected, unpublished component/capability candidates.

## Bounded analysis

Default limits: 50 MiB total uploaded bytes, 200 MiB total expanded bytes, 1,000
entries, nested archive depth 3, 50 MiB per expanded entry, 1,000 PDF pages,
2 million extracted characters, 20,000 source segments and 10,000 generated
candidates per package. Enumerate archive directory records
too; they are explicitly excluded as non-document containers. Report excess
limits on the containing entry and never mark an unenumerated archive complete.
Do not execute macros, embedded code, remote links, or document instructions.

Read all supported content units, not just headings: PDF text pages, DOCX
paragraphs/tables/header/footer/notes, XLSX worksheets/rows, JSON structured
records, XML elements, CSV rows and text sections. Inspect supported embedded
attachments and nested ZIPs within the same budget. Unsupported/encrypted/
image-only content gets actionable coverage exceptions when OCR or decoders are
unavailable. Preserve artifact ID, path, segment locator and verbatim supporting
quote for every candidate. Model output must reference existing segments and
quotes; confidence never changes the review gate. A configured model may enrich
structured extraction, but unavailable analysis is not "zero candidates" or
full coverage. Tests use deterministic synthetic analysis only.

### PDF attachments and optional configured semantic analysis

PDF attachment references in embedded-file name trees, associated-file arrays
and file-attachment annotations are individually accounted for. Names and
reference locations produce stable child identities under the owning PDF.
Attachment discovery is bounded by 4,096 traversal nodes and a tree depth of 32,
in addition to package entry, expanded-byte and nested-container limits.
Supported attachment streams enter the same bounded extraction traversal.
Encrypted, malformed, external-only, or unsupported-filter attachments remain
individual actionable exceptions; remote files and PDF actions are never
followed. An attachment must not disappear merely because its decoder is
unavailable. PDF text retains page-level segments; attachment or OCR exceptions
cannot be cleared by successful text/model analysis. PdfPig parsing itself is
cooperatively bounded, not a claim of process-level hostile-PDF isolation.

The analyzer constructor may accept the already registered optional
`Microsoft.Extensions.AI.IChatClient`. No model/client is created or replaced,
and no endpoint, credentials, model ID, tools or deployment are supplied by
package content. The existing backend-owned singleton registration remains the
registration authority. Missing configuration continues to produce explicit
incomplete semantic coverage, not successful zero-candidate analysis.

Default semantic limits are 64 logical model calls across returned checkpoint
history, 32 retained segments and 24,000 source characters per call, 64,000
response characters, 8,192 requested output tokens, 30 seconds per call, and
two minutes per analyzer invocation. Existing aggregate candidate/text limits
still apply. Oversized source units, exhausted limits, provider errors,
timeouts, invalid output and incomplete responses remain actionable coverage
exceptions. Responses are not salvaged by silently dropping or truncating
invalid fields. Cancellation propagates; the analyzer performs no durable
state mutation, and the backend retains responsibility for persisting returned
checkpoints and bounding abandoned/repeated attempts.

Successfully extracted but semantically incomplete source segments may be
analyzed in bounded batches. A separate system instruction treats all source
text as untrusted data, not executable instructions. Tools are disabled.
Model output must be strict structured data identifying the exact analyzed
segment keys, candidate kinds/fields and citations. Citations must identify
segments actually supplied in that batch and contain exact nonempty source
quotes; asserted field values must be supported by those quotes. Unknown
fields, invented citations, tool requests and incomplete batch coverage are
rejected. A valid completed batch may contain zero candidates. Every emitted
candidate remains a proposal, never an approval, publication, verified
authorization or system ATO decision.
An exact source-backed proposal already retained from structured extraction
keeps its original key and content; acknowledging it does not create or charge
a duplicate candidate. Conflicting or invalid response records are not silently
discarded.

Public analyze/resume method signatures remain unchanged. Additive private
checkpoint progress records preserve charged semantic calls, successfully
analyzed segment keys and PDF attachment-enumeration progress. Semantic-only
retry reuses retained extraction/candidates and does not rerun completed source
or model work. Existing checkpoints default these new fields to zero/empty;
selected legacy PDF entries must account for previously unenumerated
attachments before claiming complete coverage. Backend JSON normalization and
rehydration must preserve these fields. Explicit reviewer exclusions account
for intentionally unanalyzed containers without changing their retained
enumeration progress or claiming their contents were decoded. Tests use synthetic PDFs and mocked
chat clients only; no live model calls, OCR installation or deployment.

A retained PDF may have a Processed entry while its checkpoint still has
unfinished attachment enumeration or unanalyzed segments belonging to that PDF.
Retry selection must include such a PDF root, while completed root semantics
must not be replayed merely because a child attachment remains unfinished.
Explicit source/ancestor exclusions take precedence. Backend selector tests
cover this distinction separately from the analyzer's native extraction tests.

## HTTP surface

All responses use the existing structured envelope. Every request authenticates
and rechecks ordinary CSP administrator authority, no impersonation/support
context, current provider ownership and operation scope. Subscriber reads of
private package metadata, candidates or bytes are denied. Content responses use
attachment disposition and `nosniff`; never expose raw storage paths or execute
HTML source. Errors include a stable code, message and corrective suggestion;
validation 400/422, denied 403, absent 404, stale/conflict 409, over-limit 413.

Base: `/api/csp/package-imports`.

| Method/path | Contract |
|---|---|
| GET base | Paged package summaries (`page`, `pageSize`, default 25, max 100). |
| GET `/{id}` | `PackageStatus`: packageId, operationId, name, revision, processingState, publicationState, coverage counts, lastError, timestamps. |
| GET `/{id}/entries` | Paged entries with artifactId, archivePath, mediaType, status, reason, candidateCount, exclusionReason; no source body. |
| GET `/{id}/artifacts/{artifactId}/content` | Protected original/entry content, never arbitrary URLs. |
| GET `/{id}/candidates` | Paged candidates, type/review filters; source citations, duplicate matches, dependencies and exact revision. |
| GET `/{id}/review-state` | Persisted decision recovery: packageId, revision, preview (nullable existing preview DTO), previewIsStale, publication (nullable latest persisted publication DTO). Never infer success from a browser flag. |
| PATCH `/{id}/candidates/{candidateId}` | expectedRevision, edited name/description/type/classification/category/controlDuties/contributor IDs, review action, rationale, duplicate resolution; reset approval on edits. |
| PATCH `/{id}/entries/{entryId}` | expectedRevision, explicit exclusion rationale; invalidate old approval. |
| POST `/{id}/retry` | Stable idempotency key; resume unfinished processing, return durable status. |
| POST `/{id}/approval-previews` | Selected candidate IDs/revisions and expected package revision; server validates citations, reviewed state, duplicate resolutions, dependency closure, exclusions and exact impact. Returns previewId/hash/revision, blockers and selected records. |
| POST `/{id}/approve` | Exact previewId/hash/revision; persist actor/approval only if still eligible. |
| POST `/{id}/publish` | Exact approved previewId/hash/revision plus stable idempotency key; replay persisted outcomes, publish only approved set through existing releases. |

### Additive review recovery and authorization-reference projection

The recovery read returns the most recent saved preview/approval and the latest
persisted publication outcome independently. A later preview must not erase a
prior publication outcome. It recomputes staleness against package revisions,
selected revisions, published dependencies and impact. Stale decisions remain
visible but cannot enable publication. Refresh and uncertain responses use this
read; the server remains the authority and existing idempotency keys remain
stable. Older callers can ignore the new endpoint.

Approval rows add `CreatedVersion` and nullable `PublishedVersion` ordering
columns on SQLite and SQL Server. Recovery orders persisted decisions and
publication outcomes independently by those versions, rather than guessing
from wall-clock timestamps. Ambiguous pre-upgrade histories without a reliable
order return an explicit recovery error; they must not select an arbitrary
approval or report an invented publication outcome.

Source-backed proposed authorization metadata uses an additional private
candidate type, `AuthorizationReference`, not a competing provider authorization
or system ATO entity. Candidate responses and edits gain an optional nullable
`authorizationReference` object:

| Field | Meaning |
|---|---|
| `reference` | Required source-supported reference/title, maximum 2,000 characters |
| `issuer` | Optional stated issuing authority, maximum 500 characters |
| `issuedAt` | Optional stated ISO-8601 date/time |
| `expiresAt` | Optional stated ISO-8601 date/time, not earlier than issuedAt |

The analyzer emits this type only from explicit source statements/structured
records with existing citation identity, path, locator and quote. It does not
infer an authorization from component mappings or confidence. The metadata is
stored in the existing private candidate ledger, using the same revision,
edit/review/rejection and audit path. Reference candidates are not independently
publishable components/capabilities and cannot satisfy their dependencies.
Changes invalidate old package approval. Existing callers/types without the
optional object retain their current behavior.

Reference-only candidate names may contain up to 2,000 characters so a
source-derived reference title can be reviewed without truncation. The edit
request accepts a nullable `componentType`, ignored for authorization
references; reference review must not require inventory classification.
Inventory names remain limited to 256 characters and require a valid component
type. Candidate responses retain the existing string `componentType` field.
The reference editor must preserve these type-specific limits and send null
for the irrelevant component type without weakening inventory validation.
Optional-model proposal names retain the analyzer's stricter 256-character
limit; their reference metadata still supports 2,000 characters. This does not
truncate deterministic reference titles or reduce the human-review allowance.

Portal review exposes this candidate type and the source-supported fields. The
offering source panel links the package and reviewed reference candidates using
the paginated candidate read with `type=AuthorizationReference` and
`reviewState=Reviewed`; proposals/rejections are not labelled recorded references.
All labels explicitly say reviewed reference, not verified authorization.
No automatic write is made to `ProviderAuthorizationRecord`, mission systems,
subscriptions, inherited-control confirmations or ATO decisions.

#### Explicit analyzer source records

The deterministic analyzer accepts the following synthetic JSON declaration:

```json
{
  "authorizationReferences": [{
    "name": "Synthetic authorization memorandum",
    "reference": "SYNTHETIC-REFERENCE-01",
    "issuer": "Synthetic issuing office",
    "issuedAt": "2026-01-02",
    "expiresAt": "2027-01-02T00:00:00Z"
  }]
}
```

A singular `authorizationReference` object or a record explicitly declaring
`"kind": "AuthorizationReference"` is also supported. XML
`authorizationReference` elements and CSV/XLSX rows with that explicit `kind`
use the same field names. Missing optional metadata remains null. Calendar-only
dates are represented as midnight UTC; timestamps require an explicit `Z` or
numeric offset, which is retained. Invalid lengths, dates, conflicting kinds,
or ambiguous duplicate reference fields are coverage exceptions, not guessed
values. Reference metadata is never truncated.

These candidates retain the normal archive-entry identity, locator, source
quote, candidate limits and resume checkpoint behavior. Reference source IDs
are not eligible targets when resolving component/capability dependencies.
Unknown fields and unstructured prose remain incomplete semantic coverage
unless optional configured analysis successfully validates every retained
source segment. Extracting a reference from XML/tabular content alone does not
claim that all other content was semantically analyzed. Filenames, control mappings and source
claims such as `verified: true` never establish a verified authorization.

Implementation DTOs are authoritative for precise property names and are to be
shared with UI before wiring. Preview binds dependencies and impact, not just
candidate content. Unresolved coverage requires explicit exclusion before
approval; excluded entries' candidates and dependent candidates are ineligible.
Selection is bounded (100 records). Publication revalidates the full selection
before any writes and checkpoints release outcomes for safe resumption.

## UI and verification

Onboarding renders only upload/receipt/processing/exception information and can
continue after durable receipt. No extracted inventory or record-review gate.
Portal route `/workspaces/csp/security-capabilities/imports` lists packages;
`/imports/{packageId}` hosts paginated component/capability review, source
citations, duplicate/dependency resolution and distinct preview/approve/publish.
Offering source panel connects these packages and recorded references.

TDD: mixed/nested synthetic packages; all-entry coverage; no auto-publication on
submit/import/high confidence; receipt/worker restart and concurrent retry;
rejection/exclusion/dependency closure; changed revision/impact invalidation;
only approved-set publication and replay; published/unrelated draft preservation;
provider ownership and impersonation/subscriber denial. UI tests cover loading,
denial, partial failures, stale approval and accessible narrow-screen operation.
Run `dotnet build Ato.Copilot.sln`, `dotnet test Ato.Copilot.sln`, Dashboard
`npx tsc --noEmit`, focused Vitest/Playwright. Report failures and coverage
shortfalls explicitly. Manual authorized acceptance is a separate open gate.
