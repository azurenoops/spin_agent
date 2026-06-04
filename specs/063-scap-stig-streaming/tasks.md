# Tasks: SCAP/STIG Parser Streaming at Scale (063)

## Legend
- **P1** = must ship for feature to be usable
- **P2** = important, ships in same milestone
- `[ ]` = not started · `[~]` = in progress · `[x]` = done

---

## Phase 1 — Parser Streaming (US1)

- [ ] **T-063-01** — Create `IScanParser<T>` interface with
  `IAsyncEnumerable<T> ParseAsync(Stream stream, CancellationToken ct)`
  contract in `ScanImport/Parsers/`.
  _Owner: backend · Est: 1h_

- [ ] **T-063-02** — Refactor `CklParser.ParseAsync` to use
  `XmlReader.Create(stream)` with `XmlReaderSettings { Async = true }`.
  Remove internal `MemoryStream` wrapping. Yield each `<VULN>` element as
  `ScanImportFindingDto` before advancing to the next.
  _Owner: backend · Est: 3h_

- [ ] **T-063-03** — Refactor `NessusParser.ParseAsync` to use `XmlReader`
  streaming loop over `<ReportItem>` elements. Same yield-per-finding pattern.
  _Owner: backend · Est: 3h_

- [ ] **T-063-04** — Write unit tests for streaming `CklParser`:
  - 1-finding fixture (smoke)
  - 10,000-finding synthetic fixture (correctness + field mapping)
  - Malformed XML (assert `ScanImportException` thrown)
  _Owner: backend · Est: 2h_

- [ ] **T-063-05** — Write unit tests for streaming `NessusParser` (same
  three cases as T-063-04).
  _Owner: backend · Est: 2h_

- [ ] **T-063-06** — Keep backward-compat `byte[]` overloads delegating to
  `new MemoryStream(fileContent)`. Add `[Obsolete]` attributes.
  _Owner: backend · Est: 30m_

---

## Phase 2 — Batched DB Writes (US2)

- [ ] **T-063-07** — Add `ScanImport:BatchSize` (int, default 500) to
  `appsettings.json` and bind via `ScanImportOptions` config class.
  _Owner: backend · Est: 30m_

- [ ] **T-063-08** — Refactor `ScanImportService.ImportAsync` to consume
  `IAsyncEnumerable<ScanImportFindingDto>` from the parser, buffer into
  `List<ScanImportFindingDto>` of size `BatchSize`, then call `AddRange` +
  `SaveChangesAsync` + `ChangeTracker.Clear()` per batch.
  _Owner: backend · Est: 4h_

- [ ] **T-063-09** — Implement upsert semantics on
  `(ScanImportRecordId, RuleId, HostName)` using
  `ExecuteSqlRawAsync` with `MERGE` (SQL Server) / `INSERT OR REPLACE`
  (SQLite) depending on `DatabaseOptions.Provider`.
  _Owner: backend · Est: 3h_

- [ ] **T-063-10** — Update `ScanImportRecords.ProcessedCount` after each
  batch (single-column `UPDATE` — do not re-save the full entity).
  _Owner: backend · Est: 1h_

- [ ] **T-063-11** — Log warning when `BatchSize > 1000`.
  _Owner: backend · Est: 15m_

- [ ] **T-063-12** — Write integration test: 5,000-finding Nessus import
  via `ScanImportService` with SQLite in-memory; assert `Status = Completed`,
  exactly 5,000 rows, no exception.
  _Owner: backend · Est: 2h_

---

## Phase 3 — REST Upload Endpoint (US3)

- [ ] **T-063-13** — Add `ScanImportEndpoints.cs` in
  `src/Ato.Copilot.Mcp/Endpoints/` (minimal API pattern matching existing
  endpoints). Register `POST /api/v1/systems/{systemId}/scans/import`.
  _Owner: backend · Est: 2h_

- [ ] **T-063-14** — Implement file type detection: read root element
  namespace to distinguish CKL (`xmlns:...`) from XCCDF from Nessus.
  Return `415` for unknown types.
  _Owner: backend · Est: 2h_

- [ ] **T-063-15** — Wire Kestrel `MaxRequestBodySize = 268435456` (256 MB)
  for the import endpoint only (per-endpoint override via
  `IRequestSizeLimitMetadata`).
  _Owner: backend · Est: 1h_

- [ ] **T-063-16** — Implement `IBackgroundTaskQueue` pattern (or use
  `BackgroundService` + `Channel<T>`) to enqueue import jobs off the HTTP
  thread.
  _Owner: backend · Est: 3h_

- [ ] **T-063-17** — Add `GET /api/v1/systems/{systemId}/scans/import/{id}/status`
  endpoint returning `ScanImportStatusDto`.
  _Owner: backend · Est: 1h_

- [ ] **T-063-18** — Add `DELETE /api/v1/systems/{systemId}/scans/import/{id}`
  endpoint setting `CancelRequested = true`.
  _Owner: backend · Est: 1h_

- [ ] **T-063-19** — Add `ScanImport.Write` and `ScanImport.Read` permission
  checks to the new endpoints.
  _Owner: backend · Est: 1h_

- [ ] **T-063-20** — Integration test: POST valid CKL → 202 + `importJobId`;
  POST oversized file → 400; POST `.pdf` → 415.
  _Owner: backend · Est: 2h_

---

## Phase 4 — SignalR Progress (US4)

- [ ] **T-063-21** — Create `ImportProgressHub` at `/hubs/import-progress`
  with `JoinImportGroup(string importJobId)` and
  `LeaveImportGroup(string importJobId)` methods.
  _Owner: backend · Est: 2h_

- [ ] **T-063-22** — Inject `IHubContext<ImportProgressHub>` into
  `ScanImportService`; broadcast `ImportProgress` message after each batch.
  _Owner: backend · Est: 1h_

- [ ] **T-063-23** — Broadcast final `ImportProgress` event
  (`status = Completed | Failed | Cancelled`) after import loop exits.
  _Owner: backend · Est: 30m_

- [ ] **T-063-24** — Add UI progress bar component (React) that subscribes to
  `ImportProgressHub` and renders `processedCount / totalCount` percentage.
  _Owner: frontend · Est: 3h_

- [ ] **T-063-25** — Integration test: submit 5K import; subscribe to hub;
  assert ≥ 10 progress events received; final event has `status = Completed`.
  _Owner: backend · Est: 2h_

---

## Phase 5 — Load Test (US5)

- [ ] **T-063-26** — Create `tests/Ato.Copilot.Tests.Load/` project with
  xUnit + SQLite in-memory.
  _Owner: backend · Est: 1h_

- [ ] **T-063-27** — Implement `CklFixtureGenerator.GenerateAsync(int
  findingCount, Stream output)` that writes a valid CKL XML with N `<VULN>`
  elements to a stream.
  _Owner: backend · Est: 2h_

- [ ] **T-063-28** — Write `ScanImportLoadTest`: generate 5K CKL, call
  `ScanImportService`, assert time < 60s, assert peak memory < 512 MB.
  _Owner: backend · Est: 2h_

- [ ] **T-063-29** — Add `load-test` job to `.github/workflows/ci.yml`
  running on PR → `main`; skip in Debug configuration.
  _Owner: devops · Est: 1h_

---

## Estimated Total
| Phase | Est |
|-------|-----|
| 1 — Parser Streaming | ~11.5h |
| 2 — Batched DB Writes | ~10.75h |
| 3 — REST Endpoint | ~13h |
| 4 — SignalR Progress | ~8.5h |
| 5 — Load Test | ~6h |
| **Total** | **~50h** |
