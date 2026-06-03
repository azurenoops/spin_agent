# Requirements Checklist: SCAP/STIG Parser Streaming (063)

## Functional Requirements

### US1 — Streaming parsers
- [ ] `CklParser.ParseAsync(Stream)` uses `XmlReader`, not `XDocument`
- [ ] `NessusParser.ParseAsync(Stream)` uses `XmlReader`, not `XDocument`
- [ ] Both return `IAsyncEnumerable<ScanImportFindingDto>`
- [ ] Memory usage is O(finding), not O(file)
- [ ] Backward-compat `byte[]` overloads preserved with `[Obsolete]`
- [ ] Malformed XML raises `ScanImportException` with parseable message
- [ ] Unit tests cover 1-finding, 10K-finding, and malformed XML for each parser

### US2 — Batched DB writes
- [ ] `ScanImportService` processes findings in configurable batches (default 500)
- [ ] `ChangeTracker.Clear()` called after each batch
- [ ] `ScanImportRecords.ProcessedCount` updated after each batch
- [ ] Upsert on `(ScanImportRecordId, RuleId, HostName)` — no duplicate rows on retry
- [ ] `CancelRequested` checked between batches; status set to `Cancelled` on exit
- [ ] `BatchSize > 1000` logs a `Warning`
- [ ] Integration test: 5K Nessus → `Status = Completed`, 5K rows, no exception

### US3 — REST upload endpoint
- [ ] `POST /api/v1/systems/{systemId}/scans/import` accepts `multipart/form-data`
- [ ] Accepted types: `.ckl`, `.nessus`, `.xml` (XCCDF); `415` for others
- [ ] Max file: 256 MB; `400 PAYLOAD_TOO_LARGE` if exceeded
- [ ] Returns `202 Accepted` with `importJobId` and `Location` header
- [ ] Import runs off the HTTP thread (background worker)
- [ ] `GET .../status` returns `ScanImportStatusDto`
- [ ] `DELETE .../` sets `CancelRequested = true`, returns `202`
- [ ] `ScanImport.Write` permission required for POST/DELETE
- [ ] `ScanImport.Read` permission required for GET status
- [ ] Integration test: valid upload → 202; oversized → 400; bad type → 415

### US4 — SignalR progress
- [ ] `ImportProgressHub` registered at `/hubs/import-progress`
- [ ] `JoinImportGroup(importJobId)` and `LeaveImportGroup(importJobId)` implemented
- [ ] `ImportProgress` broadcast after each batch
- [ ] Final `ImportProgress` broadcast with terminal status
- [ ] Hub authenticated via same JWT bearer middleware as REST API
- [ ] UI progress bar component renders `processedCount / totalCount`

### US5 — Load test
- [ ] `tests/Ato.Copilot.Tests.Load/` project created
- [ ] `CklFixtureGenerator` generates valid CKL XML with N findings
- [ ] Load test: 5K findings < 60s wall-clock, < 512 MB peak memory
- [ ] CI `load-test` job runs on PR → `main`

---

## Non-Functional Requirements

- [ ] Parser streaming does not break existing import dashboard (history still reads from `ScanImportRecords`)
- [ ] New REST endpoint follows existing Minimal API route conventions
- [ ] `ScanImport:BatchSize` is documented in `appsettings.json` schema
- [ ] No new EF Core migrations for `ScanImportFindings` (upsert via raw SQL)
- [ ] Migration for `ScanImportRecords.CancelRequested` column added if missing
- [ ] All new code covered by existing or new xUnit tests at the unit level

---

## Out of Scope for This Epic
- Prisma parsers (`PrismaCsvParser`, `PrismaApiJsonParser`) — not OOM-prone
- SCAP XCCDF full parsing support (basic file type detection only)
- Import history pagination UI changes
- Agent-driven import path (chat tool) — remains unchanged
