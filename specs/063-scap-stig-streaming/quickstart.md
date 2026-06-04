# Quickstart: SCAP/STIG Parser Streaming (063)

## Prerequisites

- .NET 9 SDK
- SQL Server or SQLite (SQLite used for local dev and tests)
- The repo at `/tmp/ato-copilot` (or your local clone)

---

## 1. Run existing tests (sanity check)

```bash
cd /tmp/ato-copilot
dotnet test tests/Ato.Copilot.Tests.Unit/ --filter "ScanImport" -v minimal
```

Expected: all existing CKL/Nessus parser tests pass (they use small fixtures).

---

## 2. Reproduce the OOM bug (optional)

Generate a large synthetic CKL file and confirm the current parser fails:

```bash
# Generate 10,000-finding CKL (≈80 MB)
dotnet run --project tests/Ato.Copilot.Tests.Load/ -- generate-ckl 10000 /tmp/large.ckl

# Run the import via the dev server API
curl -X POST http://localhost:5000/api/v1/systems/{systemId}/scans/import \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@/tmp/large.ckl"
# Current behavior: 500 Internal Server Error or OOMKill
```

---

## 3. Apply the streaming parser changes

After implementing T-063-02 and T-063-03:

```bash
dotnet test tests/Ato.Copilot.Tests.Unit/ --filter "CklParser|NessusParser" -v normal
```

All tests should pass, including the new 10K-finding fixture tests.

---

## 4. Test the batched import end-to-end

```bash
# Start the dev server
dotnet run --project src/Ato.Copilot.Mcp/ &

# Upload a 5K-finding Nessus file
curl -X POST http://localhost:5000/api/v1/systems/$SYSTEM_ID/scans/import \
  -H "Authorization: Bearer $TOKEN" \
  -F "file=@tests/fixtures/nessus-5k-findings.nessus" \
  -i

# Expected: HTTP/1.1 202 Accepted
# Body: {"importJobId":"<guid>","status":"Queued"}
# Headers: Location: /api/v1/systems/$SYSTEM_ID/scans/import/<guid>/status

# Poll status
curl http://localhost:5000/api/v1/systems/$SYSTEM_ID/scans/import/<guid>/status \
  -H "Authorization: Bearer $TOKEN"
```

---

## 5. Subscribe to SignalR progress hub

```javascript
// Browser console or test client
const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/import-progress", { accessTokenFactory: () => token })
    .build();

connection.on("ImportProgress", (msg) => {
    console.log(`${msg.processedCount}/${msg.totalCount} — ${msg.status}`);
});

await connection.start();
await connection.invoke("JoinImportGroup", importJobId);
```

---

## 6. Run the load test

```bash
dotnet test tests/Ato.Copilot.Tests.Load/ -v normal
```

Expected output:
```
[PASS] ScanImportLoadTest.FiveThousandFindingCkl_CompletesUnder60s_PeakMemoryUnder512MB
  Duration: 23.4s | Peak memory: 187 MB
```

---

## Configuration reference

`appsettings.Development.json`:
```json
{
  "ScanImport": {
    "BatchSize": 500,
    "MaxFileSizeBytes": 268435456
  }
}
```

To test with a smaller batch size (e.g., to force more SignalR events):
```json
"ScanImport": { "BatchSize": 100 }
```

---

## Generating synthetic test fixtures

```bash
# CKL with N findings
dotnet run --project tests/Ato.Copilot.Tests.Load/ -- generate-ckl 5000 /tmp/test-5k.ckl

# Nessus with N findings
dotnet run --project tests/Ato.Copilot.Tests.Load/ -- generate-nessus 5000 /tmp/test-5k.nessus
```
