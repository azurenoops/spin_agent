# Quickstart: API Mismatch Fixes (Epic 052)

**Feature**: 052-api-mismatch-fixes
**Spec**: [../spec.md](../spec.md)

This guide walks a developer through verifying the bug, applying the fix, and running the tests from a clean checkout.

---

## Prerequisites

- Node ≥ 18, pnpm ≥ 8 (dashboard)
- .NET 8 SDK (backend)
- Repo cloned and dependencies installed (`pnpm install`, `dotnet restore`)

---

## Step 1 — Reproduce Issue #141 (optional verification)

1. Start the backend:
   ```bash
   cd src/Ato.Copilot.Mcp
   dotnet run
   ```
2. Start the dashboard:
   ```bash
   cd src/Ato.Copilot.Dashboard
   pnpm dev
   ```
3. Open `http://localhost:5173`, navigate to any chat panel.
4. Click the paperclip icon, attach any text file.
5. Type a message and press Enter.
6. Observe: the file indicator disappears; the backend receives only the text. In the browser DevTools Network tab you will see a JSON request body — no file is present. **This is the bug.**

---

## Step 2 — Apply the frontend fix (T001 + T002)

### T001 — `ChatInput.tsx`

Open `src/Ato.Copilot.Dashboard/src/components/chat/ChatInput.tsx`.

Locate `handleSend` (around line 29). Change:

```typescript
// BEFORE
onSend(trimmed);
```

```typescript
// AFTER
const files = attachments.map((a) => a.file);
onSend(trimmed, files.length > 0 ? files : undefined);
```

### T002 — Parent component (e.g. `ChatPanel.tsx`)

Find the component that renders `<ChatInput onSend={...}>`. Update the `onSend` handler:

```typescript
const handleSend = useCallback(async (content: string, files?: File[]) => {
  let body: BodyInit;
  let headers: Record<string, string> = {};

  if (files && files.length > 0) {
    const form = new FormData();
    form.append('message', content);
    if (conversationId) form.append('conversationId', conversationId);
    files.forEach((f) => form.append('attachment[]', f));
    body = form;
    // Do NOT set Content-Type — browser sets it with the boundary automatically
  } else {
    body = JSON.stringify({ message: content, conversationId });
    headers['Content-Type'] = 'application/json';
  }

  const response = await fetch('/mcp/chat/stream', {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${localStorage.getItem('auth_token') ?? ''}`,
      ...headers,
    },
    body,
  });
  // ... existing SSE parsing unchanged
}, [conversationId]);
```

---

## Step 3 — Apply the backend fix (T004 + T005 + T006)

### T004 — Extend `ChatRequest`

Open `src/Ato.Copilot.Mcp/Server/McpHttpBridge.cs`. Find `class ChatRequest` (~line 420). Add:

```csharp
[System.Text.Json.Serialization.JsonIgnore]
public IFormFileCollection? Attachments { get; set; }
```

### T005 — Update `HandleChatStreamRequestAsync`

Find `private async Task<IResult> HandleChatStreamRequestAsync(HttpContext context)` (~line 181). Replace the deserialization block at the top of the method with:

```csharp
ChatRequest? chatRequest;

if (context.Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true)
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    chatRequest = new ChatRequest
    {
        Message      = form["message"].FirstOrDefault() ?? string.Empty,
        ConversationId = form["conversationId"].FirstOrDefault(),
        Attachments  = form.Files.Count > 0 ? form.Files : null,
    };
}
else
{
    chatRequest = await JsonSerializer.DeserializeAsync<ChatRequest>(
        context.Request.Body, _jsonOptions, context.RequestAborted);
}
```

### T006 — Add MIME validation

Immediately after the binding block, before the `if (chatRequest == null ...)` check, add:

```csharp
var attachmentError = ValidateAttachments(chatRequest?.Attachments);
if (attachmentError != null) return attachmentError;
```

Add the static helper to the class:

```csharp
private static readonly HashSet<string> _allowedMimeTypes =
    new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "text/plain", "text/csv",
        "application/json", "application/xml"
    };

private static IResult? ValidateAttachments(IFormFileCollection? files)
{
    if (files is null || files.Count == 0) return null;

    var rejected = files
        .Where(f => f.Length == 0 || !_allowedMimeTypes.Contains(f.ContentType))
        .Select(f => f.FileName)
        .ToList();

    if (rejected.Count == 0) return null;

    return Results.BadRequest(new
    {
        ok = false,
        code = "UNSUPPORTED_ATTACHMENT_TYPE",
        message = "One or more attachments have unsupported types or are empty.",
        rejectedFiles = rejected,
        elapsedMs = 0
    });
}
```

---

## Step 4 — Run tests

### Backend

```bash
cd tests/Ato.Copilot.Tests.Integration
dotnet test --filter "Category=Chat"
```

If `ChatStreamAttachmentTests.cs` does not yet exist, create it from the template in `tasks.md` T010.

### Frontend

```bash
cd src/Ato.Copilot.Dashboard
pnpm test --run ChatInput
```

---

## Step 5 — Manual smoke test

1. Restart backend and dashboard.
2. Attach a PDF file in the chat input.
3. Send the message.
4. In DevTools Network, confirm the request to `/mcp/chat/stream` shows `Content-Type: multipart/form-data` and the file appears as a form part.
5. Confirm the AI response references or acknowledges the attachment.

---

## Step 6 — Add OSCAL URL JSDoc (T008)

Open `src/Ato.Copilot.Dashboard/src/api/exports.ts`. Add the following JSDoc above each `oscal*Url` function (lines ~91, ~95, ~99):

```typescript
/**
 * Returns the browser-navigation URL for the OSCAL POAM export.
 * @remarks
 * Returns a browser-navigation URL. Do NOT call this via fetch() or Axios —
 * the auth interceptor is not applied. Use only as `<a href>` or `window.open()` target.
 */
export function oscalPoamUrl(systemId: string): string {
  return `/api/v1/systems/${systemId}/exports/oscal-poam`;
}
```

Repeat for `oscalAssessmentResultsUrl` and `oscalSapUrl`.

---

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| `form["message"]` returns empty string | `FormData.append('message', ...)` key mismatch | Confirm the frontend uses key `message` (lowercase) |
| `context.Request.ReadFormAsync()` throws `InvalidOperationException` | Request body already consumed by middleware | Ensure `EnableBuffering()` is not consuming the body before the endpoint; the stream endpoint is a delegate and does not go through model binding, so this should not occur |
| TypeScript error: `Property 'file' does not exist on type 'FileAttachment'` | `FileAttachment` type definition differs from assumed | Check `src/types/chat.ts` — the property may be named differently (e.g. `rawFile`) |
| `UNSUPPORTED_ATTACHMENT_TYPE` for a plaintext file | Browser is sending `text/plain; charset=utf-8` | Extend `_allowedMimeTypes` to include `text/plain; charset=utf-8` variants, or strip the charset before comparison |
