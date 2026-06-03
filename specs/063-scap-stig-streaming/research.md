# Research: SCAP/STIG Parser Streaming (063)

## Problem sizing

### File size distribution (observed from DoD/IC customers)
| Scanner | Small system (≤500 hosts) | Large system (1,500+ hosts) |
|---------|--------------------------|----------------------------|
| Tenable Nessus | 5–20 MB, ~1K findings | 50–200 MB, 5K–15K findings |
| STIG Viewer CKL | 1–5 MB per STIG, dozens of STIGs | 20–80 MB combined |
| OpenSCAP XCCDF | 2–10 MB | 15–50 MB |

### Memory profile of current approach
`XDocument.Load(new MemoryStream(fileContent))` creates:
1. `byte[]` of raw file content (passed in by caller) — 1× file size
2. `MemoryStream` wrapping the byte array — shared, minimal overhead
3. `XDocument` DOM — typically 2–4× raw file size in managed heap
4. `XElement` query results (`Descendants("VULN")`) — full list held until enumeration

For a 100 MB Nessus file: ~300–500 MB peak managed heap. Container OOMKill
threshold in the target deployment environment: 512 MB.

### `XmlReader` memory profile
- Reads in 4–8 KB chunks from the underlying stream (buffered by `StreamReader`)
- Only the current element subtree is in memory during processing
- A single finding element (`<VULN>` in CKL, `<ReportItem>` in Nessus) is
  typically 2–5 KB
- Peak heap for 100 MB file: ~1–2 MB (the current element + string allocations)

## EF Core batch size analysis

### SQL Server parameter limit
- SQL Server: 2,100 parameters per statement
- A `ScanImportFinding` entity has ~8 mapped columns
- Safe batch ceiling: `2100 / 8 = 262` rows per `AddRange`
- Chosen default of 500 uses `ExecuteSqlRaw` / `BulkInsert` strategies that
  generate multiple statements per batch (EF Core splits automatically when
  using `AddRange` + `SaveChanges`, but with constant per-entity overhead)
- Recommendation: use `EFCore.BulkExtensions` or `SqlBulkCopy` for SQL Server
  if 500-row EF batches prove slow in benchmarks

### EF Core change tracker overhead
Each tracked entity allocates ~1–2 KB in the change tracker state machine.
5,000 entities × 1.5 KB = 7.5 MB — acceptable but compounded by related
navigation properties being loaded. `ChangeTracker.Clear()` after each batch
prevents accumulation.

## SignalR design

### Group-based fan-out
Each import job gets a named group `import-{importJobId}`. Only clients
subscribed to that specific job receive progress events — no broadcast to
unrelated connections. This avoids the N×M fan-out problem if multiple users
have concurrent imports.

### Event frequency
With BatchSize=500 and 5,000 findings, the import service broadcasts exactly
10 `ImportProgress` events + 1 final event. At < 1 KB per event this is
negligible. If batch size is reduced to 100 for fine-grained progress, 50
events are still trivial.

## Existing hub infrastructure
The project already has a SignalR hub wired to SSP export progress (class name
confirmed in codebase). The new `ImportProgressHub` should follow the same
auth/group pattern to stay consistent.

## File type auto-detection

| Format | Root element | Key namespace/attribute |
|--------|-------------|------------------------|
| CKL | `<CHECKLIST>` | No namespace |
| Nessus | `<NessusClientData_v2>` | No namespace |
| XCCDF | `<Benchmark>` | `xmlns="http://checklists.nist.gov/xccdf/..."` |

Detection reads the first `XmlReader.Read()` loop until it hits an `Element`
node; checks `LocalName` and `NamespaceURI`. Falls back to file extension if
root element is ambiguous.

## Cancellation flow
`CancelRequested` is polled between batches (not via `CancellationToken.Register`
callbacks on the DB context). This avoids mid-transaction cancellation which
could leave partial batch data in an inconsistent state. The check is:
```csharp
if (record.CancelRequested) { /* set status = Cancelled, break */ }
```
The polling adds one DB round-trip per batch (~10 round-trips for 5K/500 batch
size). This is acceptable.
