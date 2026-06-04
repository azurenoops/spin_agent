# Implementation Plan: SCAP/STIG Parser Streaming (063)

## Delivery order

```
Phase 1 (US1) → Phase 2 (US2) → Phase 3 (US3) → Phase 4 (US4) → Phase 5 (US5)
```

Phases 1 and 2 are strictly ordered (US2 depends on US1's `IAsyncEnumerable`
contract). Phase 3 depends on the service from Phase 2. Phase 4 depends on the
service from Phase 2 and is independent of Phase 3. Phase 5 depends on Phases
1–2 and can be done in parallel with Phases 3–4.

---

## Phase 1 — Parser Streaming (~2 days)

**Goal**: Replace `XDocument` DOM parsing with `XmlReader` streaming in both
`CklParser` and `NessusParser`.

### Step-by-step

1. Define `IScanParser<TDto>` interface in
   `src/Ato.Copilot.Agents/Compliance/Services/ScanImport/Parsers/`:
   ```csharp
   public interface IScanParser<TDto>
   {
       IAsyncEnumerable<TDto> ParseAsync(Stream stream, CancellationToken ct = default);
   }
   ```

2. Refactor `CklParser`:
   - New method: `public async IAsyncEnumerable<ScanImportFindingDto> ParseAsync(Stream stream, ...)`
   - Use `XmlReader.Create(stream, new XmlReaderSettings { Async = true })`
   - Loop: `while (await reader.ReadAsync())` — detect `<VULN>` start elements
   - On `<VULN>` start: call a private `ReadVulnElement(reader)` that advances
     the reader through child elements and returns a `ScanImportFindingDto`
   - `yield return dto` before advancing past `</VULN>`

3. Refactor `NessusParser` with the same pattern over `<ReportItem>` elements.

4. Write unit tests (T-063-04, T-063-05).

5. Update callers in `ScanImportService` to use the new streaming signature
   (temporarily buffer to `List<>` if US2 is not done yet — remove buffer in
   Phase 2).

**Key risk**: CKL STIG files use namespaced elements in some versions. Verify
`reader.LocalName` (not `reader.Name`) is used for element matching to handle
namespace prefixes transparently.

---

## Phase 2 — Batched DB Writes (~2 days)

**Goal**: Replace single `AddRange` + `SaveChangesAsync` with batch loop.

### Step-by-step

1. Add `ScanImportOptions` config class and bind in `Program.cs`.

2. Replace the import loop in `ScanImportService.ImportAsync`:
   ```csharp
   var batch = new List<ScanImportFinding>(options.BatchSize);
   await foreach (var dto in parser.ParseAsync(stream, ct))
   {
       batch.Add(MapToEntity(dto, record.Id));
       if (batch.Count >= options.BatchSize)
       {
           await UpsertBatchAsync(batch, ct);
           batch.Clear();
           await UpdateProgressAsync(record, ct);
           await BroadcastProgressAsync(record); // Phase 4
       }
   }
   if (batch.Count > 0)
       await UpsertBatchAsync(batch, ct);
   ```

3. Implement `UpsertBatchAsync` using `ExecuteSqlRawAsync` with provider-aware SQL.

4. Implement `UpdateProgressAsync` as a targeted UPDATE to avoid re-tracking
   the full record.

5. Wire `CancellationToken` check between batches.

---

## Phase 3 — REST Endpoint (~2 days)

**Goal**: Add `POST /api/v1/systems/{systemId}/scans/import` minimal API endpoint.

### Step-by-step

1. Create `ScanImportEndpoints.cs` following the pattern in existing endpoint
   files (e.g., `EmassEndpoints.cs`).
2. Add file type detection helper `ScanFileTypeDetector.Detect(Stream)`.
3. Implement `IBackgroundTaskQueue` + `ScanImportWorker : BackgroundService`.
4. Add status and cancel endpoints.
5. Register all in `Program.cs`.

---

## Phase 4 — SignalR Progress (~1 day)

**Goal**: Wire `ImportProgressHub` and broadcast from `ScanImportService`.

### Step-by-step

1. Create `ImportProgressHub.cs` in `src/Ato.Copilot.Mcp/Hubs/`.
2. Register at `/hubs/import-progress` in `Program.cs`.
3. Inject `IHubContext<ImportProgressHub>` into `ScanImportService` (or the
   background worker).
4. Broadcast after each batch and on terminal state.
5. Update frontend `ScanImportPage` to show progress bar.

---

## Phase 5 — Load Test (~1 day)

**Goal**: CI gate for 5K finding import.

### Step-by-step

1. Create `tests/Ato.Copilot.Tests.Load/` project.
2. Implement `CklFixtureGenerator`.
3. Write load test, add CI job.

---

## Risk register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| `XmlReader` breaking change in CKL namespace handling | Medium | High | Test with real STIG Viewer CKL exports from DISA |
| SQL Server upsert MERGE syntax vs SQLite INSERT OR REPLACE divergence | High | Medium | Provider-aware `UpsertBatchAsync` with compile-time-safe switch |
| SignalR group cleanup for long-lived connections | Low | Low | Hub `OnDisconnectedAsync` removes client from group |
| EF Core `ChangeTracker.Clear()` side-effects on concurrently tracked entities | Low | Medium | Import service uses short-lived `DbContext` scope per batch |
