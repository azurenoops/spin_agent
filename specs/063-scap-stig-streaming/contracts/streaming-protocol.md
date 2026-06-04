# Streaming Protocol Contract: SCAP/STIG Import Progress (063)

## Transport

**Protocol**: ASP.NET Core SignalR (WebSocket with Long-Polling fallback)
**Hub URL**: `/hubs/import-progress`
**Authentication**: JWT Bearer — same token used for REST API calls

---

## Client → Server Messages

### `JoinImportGroup`

Subscribe to progress events for a specific import job.

```typescript
connection.invoke("JoinImportGroup", importJobId: string): Promise<void>
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `importJobId` | `string` (GUID) | The `importJobId` returned by `POST /api/v1/systems/{systemId}/scans/import` |

**Side effect**: The caller's SignalR connection is added to the group named
`import-{importJobId}`. All future `ImportProgress` broadcasts for that job
are delivered to this connection.

**Authorization**: The calling user must have `ScanImport.Read` for the system
that owns the job. Hub returns `HubException("Forbidden")` if not.

---

### `LeaveImportGroup`

Unsubscribe from progress events.

```typescript
connection.invoke("LeaveImportGroup", importJobId: string): Promise<void>
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `importJobId` | `string` (GUID) | Job ID previously joined |

**Side effect**: Connection is removed from the group. No further `ImportProgress`
events are delivered for this job to this connection.

---

## Server → Client Messages

### `ImportProgress`

Broadcast after each batch of findings is persisted, and once more when the
import reaches a terminal state (`Completed`, `Failed`, or `Cancelled`).

```typescript
connection.on("ImportProgress", (message: ImportProgressMessage) => { ... });
```

#### `ImportProgressMessage` Schema

```typescript
interface ImportProgressMessage {
  importJobId:     string;          // GUID — identifies the import job
  status:          ImportStatus;    // current job status
  processedCount:  number;          // findings persisted so far (cumulative)
  totalCount:      number | null;   // total findings in file; null until determined
  percentComplete: number | null;   // 0.0–100.0; null when totalCount is unknown
  errorMessage:    string | null;   // set only when status = "Failed"
  timestamp:       string;          // ISO 8601 UTC, e.g. "2026-06-03T14:23:05.123Z"
  batchNumber:     number;          // 1-based batch index (useful for debugging)
  batchSize:       number;          // number of findings in this batch
}

type ImportStatus = "Queued" | "Processing" | "Completed" | "Failed" | "Cancelled";
```

#### C# server-side record (source of truth)

```csharp
public record ImportProgressMessage(
    Guid   ImportJobId,
    string Status,
    int    ProcessedCount,
    int?   TotalCount,
    double? PercentComplete,
    string? ErrorMessage,
    DateTimeOffset Timestamp,
    int    BatchNumber,
    int    BatchSize
);
```

---

## Broadcast timing

| Event | When |
|-------|------|
| First `ImportProgress` | After batch 1 is persisted (`status = "Processing"`) |
| Mid-run `ImportProgress` | After each subsequent batch |
| Final `ImportProgress` | After last batch + record status updated to terminal state |

For a 5,000-finding import with `BatchSize = 500`:
- 10 mid-run events (batches 1–10)
- 1 final event (terminal)
- Total: 11 events per import

---

## Example event sequence

```jsonc
// After batch 1 (500 findings)
{
  "importJobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Processing",
  "processedCount": 500,
  "totalCount": 5000,
  "percentComplete": 10.0,
  "errorMessage": null,
  "timestamp": "2026-06-03T14:23:06.100Z",
  "batchNumber": 1,
  "batchSize": 500
}

// After batch 10 (5000 findings)
{
  "importJobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Processing",
  "processedCount": 5000,
  "totalCount": 5000,
  "percentComplete": 100.0,
  "errorMessage": null,
  "timestamp": "2026-06-03T14:23:42.800Z",
  "batchNumber": 10,
  "batchSize": 500
}

// Terminal event
{
  "importJobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Completed",
  "processedCount": 5000,
  "totalCount": 5000,
  "percentComplete": 100.0,
  "errorMessage": null,
  "timestamp": "2026-06-03T14:23:43.050Z",
  "batchNumber": 10,
  "batchSize": 0
}
```

### Failure event

```jsonc
{
  "importJobId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Failed",
  "processedCount": 1500,
  "totalCount": 5000,
  "percentComplete": 30.0,
  "errorMessage": "XML parsing error at line 4821: unexpected end of element '<VULN>'",
  "timestamp": "2026-06-03T14:23:20.400Z",
  "batchNumber": 3,
  "batchSize": 0
}
```

---

## Connection lifecycle

- Clients should call `JoinImportGroup` immediately after starting the
  SignalR connection and before (or immediately after) submitting the upload.
- If the client reconnects mid-import, it must call `JoinImportGroup` again —
  group membership is not persisted across reconnections.
- After receiving a terminal-status event, clients should call
  `LeaveImportGroup` to allow server-side group cleanup.
- `ImportProgressHub.OnDisconnectedAsync` automatically removes the client from
  all groups on disconnect (ASP.NET Core SignalR default behavior).

---

## Hub registration (server)

```csharp
// Program.cs
app.MapHub<ImportProgressHub>("/hubs/import-progress");
```

```csharp
// Startup / ServiceCollection
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 32 * 1024; // 32 KB — progress messages are small
});
```
