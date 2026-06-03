# Tasks: API Mismatch Fixes (Epic 052)

**Feature**: 052-api-mismatch-fixes
**Spec**: [../spec.md](../spec.md)
**Plan**: [../plan.md](../plan.md)

Tasks are ordered within each phase. `[P]` markers indicate priority. Issue refs in brackets.

---

## Phase 1 — Frontend attachment forwarding

### T001 — [P1] Pass attachments from `handleSend` to `onSend` [#141]

**File**: `src/Ato.Copilot.Dashboard/src/components/chat/ChatInput.tsx`

**What**: Line 32 calls `onSend(trimmed)`. Change to:

```typescript
const files = attachments.map((a) => a.file);
onSend(trimmed, files.length > 0 ? files : undefined);
```

The `FileAttachment` type (imported from `../../types/chat`) has a `file: File` property. Extract it here.

**Verify**: Unit test — render `<ChatInput>`, attach a mock file, click Send, assert `onSend` was called with the file in the second argument.

---

### T002 — [P1] Update `onSend` caller to serialize multipart when attachments present [#141]

**File**: Locate the component that renders `<ChatInput onSend={...}>` (search for `onSend` prop consumer — likely `ChatPanel.tsx` or `ChatView.tsx`).

**What**: When `onSend` is invoked with a non-empty `attachments` array, build a `FormData` body and POST to `/mcp/chat/stream` with `Content-Type: multipart/form-data`:

```typescript
const body = new FormData();
body.append('message', content);
if (conversationId) body.append('conversationId', conversationId);
attachments?.forEach((f) => body.append('attachment[]', f));
// POST body — do NOT set Content-Type manually; browser sets boundary automatically
```

When `attachments` is absent or empty, preserve the existing JSON path.

**Verify**: Integration — POST multipart to `/mcp/chat/stream` with a real PDF fixture; confirm the response includes the file reference.

---

### T003 — [P2] Update `ChatInputProps.onSend` type to thread `FileAttachment[]` cleanly [#141]

**File**: `src/Ato.Copilot.Dashboard/src/components/chat/ChatInput.tsx`

**What**: The prop currently types the second arg as `File[]`. To avoid double-mapping (T001 maps `FileAttachment → File`, caller must not re-map), confirm the prop signature matches the extraction done in T001. No type change needed if `File[]` is already correct; this task is a verification + comment task.

**Verify**: TypeScript compilation passes with zero errors.

---

## Phase 2 — Backend attachment reception

### T004 — [P1] Extend `ChatRequest` to accept `IFormFileCollection` [#141]

**File**: `src/Ato.Copilot.Mcp/Server/McpHttpBridge.cs`

**What**: Add property to `ChatRequest` (line ~420):

```csharp
/// <summary>
/// Files attached by the user. Populated from multipart/form-data binding;
/// null when the request body is application/json.
/// </summary>
public IFormFileCollection? Attachments { get; set; }
```

`IFormFileCollection` is `Microsoft.AspNetCore.Http.IFormFileCollection`.

**Verify**: The class still deserializes cleanly from JSON (the property is nullable, so `System.Text.Json` skips it).

---

### T005 — [P1] Update `HandleChatStreamRequestAsync` to detect multipart and bind form [#141]

**File**: `src/Ato.Copilot.Mcp/Server/McpHttpBridge.cs`

**What**: Replace the current JSON-only deserialization in `HandleChatStreamRequestAsync` (~line 186) with a content-type branch:

```csharp
ChatRequest? chatRequest;

if (context.Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true)
{
    var form = await context.Request.ReadFormAsync(ct);
    chatRequest = new ChatRequest
    {
        Message = form["message"].FirstOrDefault() ?? string.Empty,
        ConversationId = form["conversationId"].FirstOrDefault(),
        Attachments = form.Files.Count > 0 ? form.Files : null,
        // Context, Action, ActionContext remain JSON-only for now (Phase 3 if needed)
    };
}
else
{
    chatRequest = await JsonSerializer.DeserializeAsync<ChatRequest>(
        context.Request.Body, _jsonOptions, ct);
}
```

**Verify**: Integration test — POST multipart to `/mcp/chat/stream`; confirm the bound `chatRequest.Attachments` has the correct file count.

---

### T006 — [P1] Add MIME-type validation helper and call site [#141]

**File**: `src/Ato.Copilot.Mcp/Server/McpHttpBridge.cs`

**What**: After form binding, before agent invocation, add:

```csharp
private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
{
    "application/pdf", "text/plain", "text/csv",
    "application/json", "application/xml"
};

private static IResult? ValidateAttachments(IFormFileCollection? files)
{
    if (files == null || files.Count == 0) return null;

    var rejected = files
        .Where(f => f.Length == 0 || !AllowedMimeTypes.Contains(f.ContentType))
        .Select(f => f.FileName)
        .ToList();

    if (rejected.Count > 0)
        return Results.BadRequest(new
        {
            ok = false,
            code = "UNSUPPORTED_ATTACHMENT_TYPE",
            message = "One or more attachments have unsupported types or are empty.",
            rejectedFiles = rejected
        });

    return null;
}
```

Call `ValidateAttachments(chatRequest.Attachments)` immediately after binding; if non-null, return the error result.

**Verify**: Unit test — call `ValidateAttachments` with an `image/png` file; assert it returns a non-null bad-request result.

---

### T007 — [P2] Pass attachment content to `ProcessChatRequestAsync` [#141]

**File**: `src/Ato.Copilot.Mcp/Server/McpServer.cs`

**What**: `ProcessChatRequestAsync` currently accepts `message, conversationId, context, history, action, actionContext`. Add an optional `IEnumerable<IFormFile>? attachments = null` parameter. Inside the method, if attachments are present, serialize them to a temporary in-memory representation (filename + base64 content) and append to the message context passed to the agent.

**Note**: This task documents the server-side agent integration surface. The exact agent prompt engineering is out of scope for this epic; the task only ensures attachment data is not lost between the HTTP layer and the agent call.

**Verify**: The method signature change compiles. A unit test asserts the method accepts a file collection without throwing.

---

## Phase 3 — OSCAL URL documentation guard [#145]

### T008 — [P3] Add `@remarks` JSDoc to OSCAL URL helpers [#145]

**File**: `src/Ato.Copilot.Dashboard/src/api/exports.ts`

**What**: Above each of `oscalPoamUrl`, `oscalAssessmentResultsUrl`, `oscalSapUrl` (lines 91, 95, 99), add:

```typescript
/**
 * Returns the OSCAL POAM export URL for browser navigation.
 * @remarks
 * Returns a browser-navigation URL. Do NOT call this via fetch() or Axios —
 * the auth interceptor is not applied. Use only as `<a href>` or `window.open()` target.
 */
export function oscalPoamUrl(systemId: string): string { ... }
```

**Verify**: TypeScript compilation passes. No runtime changes.

---

## Phase 4 — Route audit documentation [#142, #143, #144]

### T009 — [P3] Add inline comments confirming confirmed-correct route prefixes [#142, #143, #144]

**Files**:
- `src/Ato.Copilot.Dashboard/src/api/exports.ts` — `downloadExportUrl` and `oscal*Url`
- `src/Ato.Copilot.Dashboard/src/api/package.ts` — `v1Client` declaration
- `src/Ato.Copilot.Dashboard/src/api/sar.ts` — `v1Client` declaration
- `src/Ato.Copilot.Dashboard/src/api/sap.ts` — `v1Client` declaration

**What**: Add a one-line `// confirmed: backend MapGroup is /api/v1/systems/{systemId} (PackageEndpoints.cs:17)` comment above each `v1Client` declaration. Add `// confirmed: backend DashboardEndpoints.cs exports routes under /api/dashboard` above the `downloadExportUrl` function.

**Why**: Prevents future developers from misidentifying these as bugs and "correcting" them to `/api/dashboard`, which would break the package/SAR/SAP flows.

**Verify**: No compilation or lint errors.

---

## Testing tasks

### T010 — [P1] Integration test: multipart chat stream with attachment [#141]

**File**: `tests/Ato.Copilot.Tests.Integration/Chat/ChatStreamAttachmentTests.cs` (new file)

**What**: Write a `WebApplicationFactory`-based integration test that:
1. POSTs a `multipart/form-data` body to `/mcp/chat/stream` with `message=test` and a 1 KB `text/plain` fixture.
2. Asserts HTTP 200.
3. Asserts the SSE stream contains at least one `data:` event.

### T011 — [P1] Integration test: attachment MIME validation rejection [#141]

**File**: `tests/Ato.Copilot.Tests.Integration/Chat/ChatStreamAttachmentTests.cs`

**What**: Add a test that POSTs an `image/png` file; asserts HTTP 400 with code `UNSUPPORTED_ATTACHMENT_TYPE`.

### T012 — [P1] Unit test: `ChatInput` passes attachments to `onSend` [#141]

**File**: `src/Ato.Copilot.Dashboard/src/components/chat/ChatInput.test.tsx` (new or extend existing)

**What**: Render `<ChatInput onSend={mockFn} ...>`, simulate file selection, simulate Send click, assert `mockFn` called with `(string, File[])`.
