# Feature Specification: SCAP/STIG Parser Streaming at Scale (5K Findings)

**Feature Branch**: `063-scap-stig-streaming`
**Created**: 2026-06-03
**Status**: Draft
**GitHub Issue**: #131
**Epic**: SCAP/STIG Parser Streaming at Scale

## Background

The scan import pipeline (`ScanImportService.cs`, 3,051 lines) ingests CKL and
Nessus XML files produced by SCAP-compliant scanners and maps their findings into
the `ScanImportFindings` and `Findings` EF Core DbSets. Two structural
deficiencies make it unreliable for real-world enterprise scans.

### Verified state of the code (current `main`)

1. **Full-file memory load in every XML parser.**
   `CklParser.cs` and `NessusParser.cs` both do:
   ```csharp
   XDocument.Load(new MemoryStream(fileContent))
   ```
   This materialises the entire XML document into a `MemoryStream`, then
   parses the DOM into an in-memory `XDocument` object tree, then walks the tree
   to emit findings. A typical Nessus scan of a large system produces 50–200 MB
   of XML containing 5,000–15,000 individual findings. At that size the
   `MemoryStream` + `XDocument` together consume 3–5× the raw file size in
   managed heap — regularly exceeding the 512 MB working set threshold of the
   container and causing OOMKill or `OutOfMemoryException` on the server.

2. **Single bulk `AddRange` + `SaveChangesAsync` in `ScanImportService`.**
   After parsing, the service does:
   ```csharp
   _context.ScanImportFindings.AddRange(importFindings);
   await _context.SaveChangesAsync();
   ```
   EF Core tracks every entity in the change tracker simultaneously. For 5,000
   findings this saturates the change tracker, and the resulting SQL `INSERT`
   statement exceeds SQL Server's 2,100-parameter limit, causing the import to
   fail with `SqlException: The incoming request has too many parameters`.

3. **No dedicated REST upload endpoint.**
   The only way to initiate a scan import today is through the MCP chat tool
   (agent-driven). Users must paste file paths into the chat or describe their
   files textually — there is no `IFormFile`-based endpoint in `Endpoints/`
   analogous to the eMASS or SSP PDF upload endpoints. This forces a UX detour
   through the chat interface for what is a routine operational task.

4. **No progress visibility for long-running imports.**
   A SignalR hub is wired to SSP export progress, but the scan import path
   writes no events and the UI shows no live progress indicator. A 5,000-finding
   import that takes 30–90 seconds appears to the user as a hung request.

5. **No import cancellation.**
   There is no mechanism to abort an in-progress import. If a user uploads the
   wrong file, they must wait for the full import to complete (or time out) and
   then delete the resulting record.

6. **No load test.**
   `.github/workflows/ci.yml` contains no scan import benchmark. There is no
   automated gate to catch performance regressions before they reach production.

### Why this matters

Every government system that uses SCAP-compliant scanners (Tenable Nessus,
OpenSCAP, STIG Viewer) produces large CKL/Nessus XML outputs on every scan
cycle. If the import pipeline cannot handle these files the tool is unusable for
its primary ATO automation purpose. The fix must land before any customer
onboarding that involves live Nessus scans.

## Clarifications

### Session 2026-06-03

- **Q: Should the streaming rewrite use `XmlReader` (pull model) or
  `System.Xml.XmlReader`-backed `XDocument.LoadAsync` with chunked reads?**
  **A:** Use raw `XmlReader` (SAX-style pull model). `XDocument.LoadAsync`
  still builds the full DOM in memory even when called with `async`. Only a
  true element-by-element `XmlReader` loop achieves O(finding) memory. Each
  finding element is read into a lightweight DTO, yielded via `IAsyncEnumerable`,
  then discarded before the next is read.

- **Q: Should batch size (currently proposed at 500) be configurable?**
  **A:** Yes — expose `ScanImport:BatchSize` in `appsettings.json`, defaulting
  to 500. Individual batch sizes above 1000 should log a warning.

- **Q: Should each batch use its own `DbContext` transaction, or should we rely
  on implicit per-`SaveChangesAsync` transactions?**
  **A:** Implicit is fine. Each `SaveChangesAsync` call is its own DB
  transaction. If a batch fails, the record in `ScanImportRecords` is updated
  with `Status = Failed` and the partial import (prior batches) is retained —
  duplicates on retry are prevented by upsert on `(ScanImportRecordId,
  FindingId)`.

- **Q: What file types should the REST upload endpoint accept?**
  **A:** Three formats: CKL (`application/xml` or `.ckl` extension), Nessus
  (`application/xml` or `.nessus` extension), and SCAP XCCDF
  (`application/xml` or `.xml` extension with an XCCDF namespace root element).
  Max file size is 256 MB (enforced by Kestrel `MaxRequestBodySize` and
  validated with `400 PAYLOAD_TOO_LARGE`).

- **Q: Should the REST endpoint be async (202 Accepted + poll) or synchronous?**
  **A:** 202 Accepted. The endpoint enqueues the import job, returns an
  `importJobId` (`ScanImportRecords.Id`), and the client polls
  `GET /api/v1/systems/{systemId}/scans/import/{importJobId}/status` or
  subscribes to the SignalR `ImportProgress` hub for live updates.

- **Q: What SignalR hub should be used — a new `ImportProgressHub` or the
  existing SSP export hub?**
  **A:** New `ImportProgressHub` at `/hubs/import-progress`. Reusing the SSP
  hub would couple unrelated concerns and confuse clients subscribed to SSP
  events.

- **Q: What cancellation mechanism should be used?**
  **A:** `DELETE /api/v1/systems/{systemId}/scans/import/{importJobId}` sets a
  cancellation flag in `ScanImportRecords.CancelRequested = true`. The import
  background job checks `CancellationToken.IsCancellationRequested` between
  batches and exits cleanly, leaving the record with `Status = Cancelled`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — CKL and Nessus parsers stream XML instead of loading into memory (Priority: P1)

**As a** system owner
**I want** the scan import pipeline to process CKL and Nessus files without
loading the entire file into memory
**So that** a 200 MB Nessus scan of my 1,500-host enclave does not OOM the
server.

**Why this priority**: P1 — the current parsers cannot process real enterprise
scans. This is a correctness issue, not a performance optimisation.

**Independent Test**: Load a synthetic 100,000-finding CKL file (generated by
the load-test fixture); assert peak managed heap stays below 512 MB during
parse; assert all 100,000 findings are yielded in order.

**Acceptance**
- `CklParser.ParseAsync(Stream stream)` accepts a raw `Stream` and uses
  `XmlReader.Create(stream)` internally — no `MemoryStream` wrapping.
- `NessusParser.ParseAsync(Stream stream)` follows the same contract.
- Both parsers yield `IAsyncEnumerable<ScanImportFindingDto>` — callers
  receive findings one at a time without waiting for the full file.
- The existing `byte[] fileContent` overloads are kept for backward
  compatibility but delegate to `new MemoryStream(fileContent)` (now safe
  because callers are responsible for ensuring in-memory content fits).
- Unit tests assert the new streaming path with a 10,000-element synthetic CKL
  and Nessus fixture and verify correct field mapping.

### User Story 2 — ScanImportService processes findings in batches (Priority: P1)

**As a** system owner
**I want** a 5,000-finding Nessus import to complete without a SQL parameter
overflow error
**So that** I can run my weekly vulnerability scan import without manual
intervention.

**Why this priority**: P1 — the current bulk insert fails deterministically at
~700 findings on SQL Server due to the 2,100-parameter limit.

**Independent Test**: Import a synthetic 5,000-finding Nessus fixture; assert
`ScanImportRecords.Status = Completed`; assert exactly 5,000 rows in
`ScanImportFindings`; assert no `SqlException` in the import log.

**Acceptance**
- `ScanImportService` consumes the `IAsyncEnumerable<ScanImportFindingDto>`
  from the parser and buffers findings into batches of `ScanImport:BatchSize`
  (default 500).
- Each batch calls `AddRange` + `SaveChangesAsync` independently.
- The change tracker is cleared (`_context.ChangeTracker.Clear()`) after each
  batch to prevent accumulation.
- `ScanImportRecords.ProcessedCount` is updated after each batch so the polling
  endpoint and SignalR hub can report live progress.
- Upsert semantics: if a finding with the same `(ScanImportRecordId, RuleId,
  HostName)` already exists, the row is updated rather than duplicated (supports
  safe retry).
- Batch size above 1,000 logs a `Warning` with the configured value.

### User Story 3 — REST file upload endpoint for scan import (Priority: P1)

**As a** system owner
**I want** to upload a CKL, Nessus, or SCAP XCCDF file directly via a REST API
**So that** I don't have to paste file paths into the chat interface.

**Why this priority**: P1 — the chat-only path is not suitable for automated
CI/CD pipelines or bulk operational workflows.

**Independent Test**: `POST /api/v1/systems/{systemId}/scans/import` with a
valid CKL multipart upload returns `202` with a `Location` header pointing to
the status endpoint; the returned `importJobId` is retrievable from
`GET .../status` with `Status = Queued`.

**Acceptance**
- `POST /api/v1/systems/{systemId}/scans/import` accepts `multipart/form-data`
  with a single `file` field.
- Supported content types: `.ckl`, `.nessus`, `.xml` (XCCDF namespace
  auto-detected). Unsupported types return `415 UNSUPPORTED_FILE_TYPE`.
- Max file size: 256 MB. Exceeding returns `400 PAYLOAD_TOO_LARGE`.
- The endpoint requires the caller to hold `ScanImport.Write` permission for
  the target system.
- Returns `202 Accepted` with body `{ "importJobId": "<guid>", "status":
  "Queued" }` and `Location: /api/v1/systems/{systemId}/scans/import/{id}/status`.
- The import is executed by a background `IHostedService` (or `IBackgroundTaskQueue` pattern) — not on the HTTP thread.
- `GET /api/v1/systems/{systemId}/scans/import/{importJobId}/status` returns
  the current `ScanImportRecords` row as a DTO.
- `DELETE /api/v1/systems/{systemId}/scans/import/{importJobId}` sets
  `CancelRequested = true` and returns `202 Accepted`.

### User Story 4 — Import progress reported via SignalR (Priority: P2)

**As a** system owner who uploaded a large Nessus file
**I want** to see a live progress bar in the UI while my import runs
**So that** I know the system is working and can estimate completion time.

**Why this priority**: P2 — the streaming batch import (US1/US2) makes long
imports survivable; this story makes them observable.

**Independent Test**: Subscribe to `ImportProgressHub` before submitting a
5,000-finding import; assert at least 10 `ImportProgress` events are received
during the run; assert the final event has `status = Completed` and
`processedCount = 5000`.

**Acceptance**
- `ImportProgressHub` is registered at `/hubs/import-progress`.
- After each batch, the import service broadcasts an `ImportProgress` message
  (see `contracts/streaming-protocol.md` for schema) to the group named
  `import-{importJobId}`.
- Clients join the group by calling `JoinImportGroup(importJobId)` on the hub.
- The final event (status `Completed` or `Failed`) is broadcast after the
  last batch or on error.
- Hub authentication uses the same JWT bearer middleware as the REST API.

### User Story 5 — Load test validates 5K finding import performance (Priority: P2)

**As a** developer
**I want** a CI-gated load test that verifies a 5,000-finding CKL import
completes in under 60 seconds with peak memory below 512 MB
**So that** performance regressions are caught before they reach production.

**Why this priority**: P2 — the streaming refactor (US1/US2) makes the
performance envelope predictable; this story locks it in.

**Independent Test**: The load test itself is the test; it must fail the CI job
if thresholds are breached.

**Acceptance**
- A new xUnit test project `Ato.Copilot.Tests.Load` (or a k6 script in
  `tests/load/`) contains a `ScanImportLoadTest` that:
  - Generates a synthetic 5,000-finding CKL XML fixture (helper in
    `TestFixtures/CklFixtureGenerator.cs`).
  - Calls `ScanImportService` directly (in-process) with SQLite in-memory DB.
  - Asserts wall-clock time < 60 seconds.
  - Asserts `GC.GetTotalMemory(false)` peak < 512 MB (sampled via a background
    monitoring thread).
- CI workflow `ci.yml` gains a `load-test` job that runs this test on every
  PR targeting `main`.
- The load test is skipped in Debug builds to avoid false positives on slow
  CI runners (use `[Fact(Skip = "...")]` gated on `BUILD_CONFIGURATION`).
