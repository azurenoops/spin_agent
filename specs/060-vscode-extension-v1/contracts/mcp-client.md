# MCP Client Contract — 060 VS Code Extension v1.0

This document describes how the VS Code extension calls the ATO Copilot MCP
server. The implementation lives in `extensions/vscode/src/services/mcpClient.ts`.

---

## Base URL resolution

The MCP server URL is **not hardcoded**. It is read from VS Code settings on
every request:

```typescript
import * as vscode from 'vscode';

function getBaseUrl(): string {
  return vscode.workspace
    .getConfiguration('ato-copilot')
    .get<string>('apiUrl', 'http://localhost:3001');
}
```

This means changing `ato-copilot.apiUrl` in VS Code settings takes effect on the
next API call without an extension reload. The extension registers an
`onDidChangeConfiguration` listener to invalidate any internal cache:

```typescript
vscode.workspace.onDidChangeConfiguration(e => {
  if (e.affectsConfiguration('ato-copilot')) {
    mcpClient.resetBaseUrl();
  }
});
```

---

## HTTP headers forwarded on every request

| Header | Source | Purpose |
|--------|--------|---------|
| `Authorization` | `Bearer <accessToken>` from SecretStorage | Azure MSAL token |
| `X-Tenant-Id` | `ato-copilot.tenantId` setting | Home tenant scope |
| `X-Impersonated-Tenant-Id` | `ato-copilot.impersonatedTenantId` setting | CSP-Admin impersonation (omitted if empty) |
| `Content-Type` | `application/json` | All POST/PUT requests |

---

## Endpoints called by each command

### `ato.checkHealth`

```
GET {apiUrl}/health
```

**Response**:
```json
{ "status": "healthy", "version": "1.0.0" }
```

On success: `vscode.window.showInformationMessage('ATO Copilot: API is healthy ✓')`
On error: `vscode.window.showErrorMessage('ATO Copilot: API unreachable — {message}')`

---

### `ato.analyzeCurrentFile`

```
POST {apiUrl}/api/compliance/analyze-file
Content-Type: application/json

{
  "content": "<full text of active editor document>",
  "fileName": "<basename of editor file>",
  "language": "<VS Code languageId>"
}
```

**Response**: `ComplianceAnalysisResult` (see `mcpClient.ts` for type).

---

### `ato.analyzeWorkspace`

```
POST {apiUrl}/api/compliance/analyze-workspace
Content-Type: application/json

{
  "files": [
    { "path": "relative/path.tf", "content": "..." },
    ...
  ]
}
```

**Supported file extensions**: `.bicep`, `.tf`, `.yaml`, `.yml`, `.json`.

---

### `ato.requestFalsePositive`

```
POST {apiUrl}/api/compliance/false-positive
Content-Type: application/json

{
  "findingId": "<uuid from active diagnostic>",
  "selectedText": "<editor selection text>",
  "reason": "<user-provided reason string>"
}
```

---

### `@ato` chat participant (all slash commands)

The chat participant uses the MCP HTTP SSE endpoint for streaming responses:

```
POST {apiUrl}/api/chat/stream
Content-Type: application/json
Accept: text/event-stream

{
  "command": "compliance" | "knowledge" | "config",
  "message": "<user chat message>",
  "context": { ... }  // VS Code chat context
}
```

SSE events:
```
data: {"type":"token","content":"..."}
data: {"type":"done"}
data: {"type":"error","message":"..."}
```

Implemented in `src/services/sseClient.ts`.

---

## Timeout

All requests use `ato-copilot.timeout` (default 30 000 ms) as the axios timeout.
SSE connections use the same timeout for the initial connection; streaming
continues until the `done` event regardless.

---

## Error handling

| HTTP status | Extension behaviour |
|-------------|-------------------|
| `401` | Prompt user to run `ATO Copilot: Sign In` |
| `403` | Show `showErrorMessage` with role information from response body |
| `429` | Show `showWarningMessage` with retry-after hint |
| `5xx` | Show `showErrorMessage` with status code and message |
| Network error | Show `showErrorMessage('ATO Copilot: Cannot reach server at {apiUrl}')` |

---

## Testing the MCP client

`src/mcpClient.test.ts` uses sinon to stub axios:

```typescript
import sinon from 'sinon';
import axios from 'axios';

let getStub: sinon.SinonStub;

beforeEach(() => {
  getStub = sinon.stub(axios, 'get');
});

afterEach(() => {
  sinon.restore();
});

it('reads apiUrl from settings per request', async () => {
  const cfgStub = sinon.stub(vscode.workspace, 'getConfiguration')
    .returns({ get: (k: string) => k === 'apiUrl' ? 'http://test:9999' : undefined } as any);

  getStub.resolves({ data: { status: 'healthy' } });

  await mcpClient.getHealth();

  sinon.assert.calledWith(getStub, sinon.match(/http:\/\/test:9999/));
  cfgStub.restore();
});
```
