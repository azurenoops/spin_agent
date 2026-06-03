# Data Model: API Mismatch Fixes (Epic 052)

**Feature**: 052-api-mismatch-fixes
**Spec**: [../spec.md](../spec.md)

> **No new database entities are introduced by this epic.**
>
> This document records the API surface corrections only — changes to request models,
> wire formats, and TypeScript types. No EF Core migrations are required.

---

## 1. Backend model changes

### 1.1 `ChatRequest` — `McpHttpBridge.cs`

**Location**: `src/Ato.Copilot.Mcp/Server/McpHttpBridge.cs` (line ~420)

**Current shape**:

```csharp
public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public Dictionary<string, object>? Context { get; set; }
    public List<ChatMessage>? ConversationHistory { get; set; }
    public string? Action { get; set; }
    public Dictionary<string, object>? ActionContext { get; set; }
}
```

**Corrected shape** (additive — no breaking changes):

```csharp
public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? ConversationId { get; set; }
    public Dictionary<string, object>? Context { get; set; }
    public List<ChatMessage>? ConversationHistory { get; set; }
    public string? Action { get; set; }
    public Dictionary<string, object>? ActionContext { get; set; }

    // ── NEW (Epic 052 / Issue #141) ──────────────────────────────────────
    /// <summary>
    /// Files attached by the user. Populated only when the request uses
    /// multipart/form-data encoding; null for application/json requests.
    /// </summary>
    public IFormFileCollection? Attachments { get; set; }
}
```

**Constraints**:
- `Attachments` is **nullable**. The property MUST NOT be included in JSON serialization output (mark `[JsonIgnore]` if the model is ever serialized to a response body, though it currently is not).
- When `Content-Type` is `application/json`, `Attachments` is always `null`. No behavior change for existing callers.
- When `Content-Type` is `multipart/form-data`, each `attachment[]` form part is collected into `IFormFileCollection` by the ASP.NET Core form pipeline. Any other named form part that matches an existing `ChatRequest` property is bound by name.

---

### 1.2 `ProcessChatRequestAsync` signature — `McpServer.cs`

**Location**: `src/Ato.Copilot.Mcp/Server/McpServer.cs` (line ~101)

**Current signature**:

```csharp
public async Task<McpChatResponse> ProcessChatRequestAsync(
    string message,
    string? conversationId = null,
    Dictionary<string, object>? context = null,
    List<(string Role, string Content)>? history = null,
    string? action = null,
    Dictionary<string, object>? actionContext = null)
```

**Corrected signature** (additive):

```csharp
public async Task<McpChatResponse> ProcessChatRequestAsync(
    string message,
    string? conversationId = null,
    Dictionary<string, object>? context = null,
    List<(string Role, string Content)>? history = null,
    string? action = null,
    Dictionary<string, object>? actionContext = null,
    IEnumerable<IFormFile>? attachments = null)   // ← NEW (Issue #141)
```

**Attachment serialization** (internal contract, not a wire format):

When `attachments` is non-null and non-empty, each file is read into a transient in-memory structure before agent invocation:

```csharp
// Internal — not persisted, not returned to client
record AttachmentContext(string FileName, string MimeType, string Base64Content);
```

These are appended to the agent's system prompt as structured context blocks. No persistence occurs. The `AttachmentContext` type is internal to `McpServer.cs`.

---

## 2. Wire format changes

### 2.1 `/mcp/chat/stream` — content-type duality

| Scenario | `Content-Type` | Body format | Status |
|----------|---------------|-------------|--------|
| Text-only message (existing) | `application/json` | JSON object matching `ChatRequest` | Unchanged |
| Message + attachments (new) | `multipart/form-data` | Named parts — see § 2.1.1 | **New** |

#### 2.1.1 Multipart part names

| Part name | Type | Required | Notes |
|-----------|------|----------|-------|
| `message` | string | yes | Plain text message content |
| `conversationId` | string | no | Existing conversation identifier |
| `context` | string (JSON) | no | Serialized `Dictionary<string, object>` |
| `action` | string | no | Action routing identifier |
| `actionContext` | string (JSON) | no | Serialized `Dictionary<string, object>` |
| `attachment[]` | file | no | Repeat for each file; name MUST be `attachment[]` |

#### 2.1.2 Attachment MIME allowlist

| MIME type | Description |
|-----------|-------------|
| `application/pdf` | PDF documents (SSP, policy docs) |
| `text/plain` | Plain text evidence |
| `text/csv` | CSV evidence or data tables |
| `application/json` | JSON configuration or schema evidence |
| `application/xml` | XML (OSCAL, SCAP) evidence |

Any other MIME type → HTTP 400 `UNSUPPORTED_ATTACHMENT_TYPE`.

---

## 3. Frontend type changes

> See [contracts/frontend-types.md](contracts/frontend-types.md) for full TypeScript interface diffs.

### 3.1 Summary of type surface changes

| File | Change | Issue |
|------|--------|-------|
| `ChatInput.tsx` — `handleSend` | Pass `attachments.map(a => a.file)` as second arg | #141 |
| `ChatInput.tsx` — no type change needed | `onSend: (content: string, attachments?: File[])` already correct | #141 |
| `exports.ts` — `oscal*Url` functions | Add `@remarks` JSDoc (no type change) | #145 |

---

## 4. Route surface — confirmed matches (no changes)

The table below is the authoritative route audit. All entries are **confirmed matches**; no route changes are made.

| Route | Frontend source | Backend source | Verdict |
|-------|----------------|---------------|---------|
| `GET /api/dashboard/systems/{id}/exports/{eid}/download` | `exports.ts:85` (`apiClient.defaults.baseURL`) | `DashboardEndpoints.cs:5252` (`/api/dashboard` group + `/systems/{systemId}/exports/{exportId:guid}/download`) | ✅ MATCH |
| `GET /api/v1/systems/{id}/exports/oscal-poam` | `exports.ts:92` (hardcoded string) | `PackageEndpoints.cs:231` (`/api/v1/systems/{systemId}` group + `/exports/oscal-poam`) | ✅ MATCH |
| `GET /api/v1/systems/{id}/exports/oscal-assessment-results` | `exports.ts:96` | `PackageEndpoints.cs:241` | ✅ MATCH |
| `GET /api/v1/systems/{id}/exports/oscal-sap` | `exports.ts:100` | `PackageEndpoints.cs:251` | ✅ MATCH |
| `POST /api/v1/systems/{id}/packages` | `package.ts:124` (`v1Client`) | `PackageEndpoints.cs:289` | ✅ MATCH |
| `GET /api/v1/systems/{id}/packages/{pid}` | `package.ts:134` | `PackageEndpoints.cs:332` | ✅ MATCH |
| `GET /api/v1/systems/{id}/packages` | `package.ts:144` | `PackageEndpoints.cs:314` | ✅ MATCH |
| `GET /api/v1/systems/{id}/packages/{pid}/download` | `package.ts:152` (hardcoded) | `PackageEndpoints.cs:386` | ✅ MATCH |
| `POST /api/v1/systems/{id}/packages/validate` | `package.ts:158` | `PackageEndpoints.cs:263` | ✅ MATCH |
| `POST /api/v1/systems/{id}/sar` | `sar.ts:71` (`v1Client`) | `PackageEndpoints.cs:22` | ✅ MATCH |
| `GET /api/v1/systems/{id}/sar` | `sar.ts:76` | `PackageEndpoints.cs:40` | ✅ MATCH |
| `GET /api/v1/systems/{id}/sar/{sarId}` | `sar.ts:81` | `PackageEndpoints.cs:52` | ✅ MATCH |
| `PUT /api/v1/systems/{id}/sar/{sarId}/sections/{type}` | `sar.ts:86` | `PackageEndpoints.cs:65` | ✅ MATCH |
| `POST /api/v1/systems/{id}/sar/{sarId}/submit` | `sar.ts:93` | `PackageEndpoints.cs:96` | ✅ MATCH |
| `POST /api/v1/systems/{id}/sar/{sarId}/review` | `sar.ts:98` | `PackageEndpoints.cs:114` | ✅ MATCH |
| `GET /api/v1/systems/{id}/sar/{sarId}/export` | `sar.ts:104` | `PackageEndpoints.cs:133` | ✅ MATCH |
| `POST /api/v1/systems/{id}/sap` | `sap.ts:55` (`v1Client`) | `PackageEndpoints.cs:155` | ✅ MATCH |
| `GET /api/v1/systems/{id}/sap` | `sap.ts:60` | `PackageEndpoints.cs:173` | ✅ MATCH |
| `POST /api/v1/systems/{id}/sap/{sapId}/finalize` | `sap.ts:64` | `PackageEndpoints.cs:185` | ✅ MATCH |
| `GET /api/dashboard/templates` | `exports.ts:108` (`apiClient`) | `DashboardEndpoints.cs:5341` (`/api/dashboard` group + `/templates`) | ✅ MATCH |
| `POST /api/dashboard/templates` | `exports.ts:127` | `DashboardEndpoints.cs:5282` | ✅ MATCH |
| `DELETE /api/dashboard/templates/{id}` | `exports.ts:133` | `DashboardEndpoints.cs:5356` | ✅ MATCH |
| `PUT /api/dashboard/templates/{id}` | `exports.ts:142` | `DashboardEndpoints.cs:5373` | ✅ MATCH |
