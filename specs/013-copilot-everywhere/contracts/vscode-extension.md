# VS Code Extension Contract: Copilot Everywhere — GitHub Copilot Chat Participant

**Feature**: 013-copilot-everywhere  
**Date**: 2026-02-26  
**Status**: Complete

---

## Extension Identity

| Field | Value |
|-------|-------|
| Publisher | `ato-copilot` |
| Extension ID | `ato-copilot.ato-copilot-vscode` |
| Display Name | Security Posture Intelligence Navigator |
| VS Code Engine | `^1.90.0` |
| Activation | `onChatParticipant:ato` |

---

## Chat Participant: `@ato`

**Registration**:
```typescript
const participant = vscode.chat.createChatParticipant("ato", handler);
participant.iconPath = vscode.Uri.joinPath(context.extensionUri, "media", "icon.png");
participant.isSticky = true;
```

### Slash Commands

| Command | Agent Mapping | Description |
|---------|---------------|-------------|
| `/compliance` | `ComplianceAgent` | Run compliance assessments, query controls, remediation |
| `/knowledge` | `KnowledgeBaseAgent` | Query ATO knowledge base, best practices, documentation |
| `/config` | `ConfigurationAgent` | Manage Security Posture Intelligence Navigator configuration settings |

### Request → MCP Server Mapping

The participant handler translates `ChatRequest` into an MCP `/mcp/chat` POST:

```typescript
// Request payload to POST /mcp/chat
interface McpChatRequest {
  conversationId: string;          // Generated per VS Code session
  message: string;                 // request.prompt
  conversationHistory: Array<{     // Rebuilt from context.history
    role: "user" | "assistant";
    content: string;
  }>;
  context: {
    source: "vscode-copilot";
    platform: "VSCode";
    targetAgent?: string;          // From slash command mapping
    metadata: {
      routingHint?: string;        // Same as targetAgent
      fileName?: string;           // Active editor file (if any)
      language?: string;           // Active editor language
    };
  };
}
```

### Response Rendering

```typescript
// Response from MCP Server
interface McpChatResponse {
  response: string;                // Markdown content
  agentUsed?: string;              // Agent attribution
  intentType?: string;             // Response classification
  templates?: Array<{              // Generated IaC templates
    name: string;
    type: string;                  // "bicep" | "terraform" | "kubernetes" | etc.
    content: string;
    language: string;
  }>;
  requiresFollowUp?: boolean;
  followUpPrompt?: string;
}
```

- Markdown content → `stream.markdown(response.response)`
- Agent attribution → `stream.markdown(`\n\n*Processed by: ${response.agentUsed}*`)`
- Templates → rendered as fenced code blocks with type-specific icons and "Save" buttons via `stream.button()`

---

## Commands

### `ato.checkHealth` (FR-029, FR-034)

Check MCP Server health status.

| Property | Value |
|----------|-------|
| Title | Security Posture Intelligence Navigator: Check API Health |
| Category | Security Posture Intelligence Navigator |

**Behavior**:
- `GET {apiUrl}/health` with configured timeout
- Success → `vscode.window.showInformationMessage("Security Posture Intelligence Navigator API is healthy")`
- Failure → `vscode.window.showWarningMessage("Security Posture Intelligence Navigator API is unreachable", "Configure Connection")`
- On activation: runs silently as background check (FR-034)

### `ato.configure` (FR-029, FR-032)

Open Security Posture Intelligence Navigator settings.

| Property | Value |
|----------|-------|
| Title | Security Posture Intelligence Navigator: Configure Connection |
| Category | Security Posture Intelligence Navigator |

**Behavior**: Opens VS Code settings filtered to `@ext:ato-copilot.ato-copilot-vscode`

### `ato.analyzeCurrentFile` (FR-029, FR-030)

Analyze current editor file for compliance.

| Property | Value |
|----------|-------|
| Title | Security Posture Intelligence Navigator: Analyze Current File for Compliance |
| Category | Security Posture Intelligence Navigator |

**Request to MCP Server**:
```typescript
{
  conversationId: generateId(),
  message: `Analyze the following ${language} file for NIST 800-53 compliance issues:\n\nFile: ${fileName}\n\n\`\`\`${language}\n${fileContent}\n\`\`\`\n\nReturn findings as JSON with fields: controlId, title, severity (high/medium/low), description, recommendation.`,
  context: {
    source: "vscode-copilot",
    platform: "VSCode",
    metadata: { fileName, language, analysisType: "file" }
  }
}
```

**Response**: Opens side-by-side webview panel with findings grouped by severity:
- High (red badge) → Medium (orange badge) → Low (green badge)
- Each finding shows: control ID, title, description, recommendation

### `ato.analyzeWorkspace` (FR-029, FR-031)

Analyze workspace IaC files for compliance.

| Property | Value |
|----------|-------|
| Title | Security Posture Intelligence Navigator: Analyze Workspace for Compliance |
| Category | Security Posture Intelligence Navigator |

**File patterns**: `**/*.bicep`, `**/*.tf`, `**/*.yaml`, `**/*.yml`, `**/*.json`  
**Excludes**: `**/node_modules/**`, `**/.git/**`, `**/bin/**`, `**/obj/**`

**Behavior**: Scans matching files, sends each to `/mcp/chat` with structured prompt, aggregates findings into a workspace-level webview panel.

---

## Settings (FR-032)

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `ato-copilot.apiUrl` | `string` | `"http://localhost:3001"` | MCP Server base URL |
| `ato-copilot.apiKey` | `string` | `""` | API key for authentication (if required) |
| `ato-copilot.timeout` | `number` | `30000` | Request timeout in milliseconds |
| `ato-copilot.enableLogging` | `boolean` | `false` | Enable debug logging to output channel |

---

## Error Handling (FR-033)

| Error Code | User Message | Action Button |
|------------|-------------|---------------|
| `ECONNREFUSED` | "Cannot connect to Security Posture Intelligence Navigator API at {url}" | "Configure Connection" |
| `ETIMEDOUT` | "Security Posture Intelligence Navigator API request timed out" | "Configure Connection" |
| HTTP 401 | "Security Posture Intelligence Navigator API authentication failed" | "Configure Connection" |
| HTTP 500 | "Security Posture Intelligence Navigator API encountered an error" | "Retry" |
| Other | "An unexpected error occurred: {message}" | "Configure Connection" |

---

## Export Service (FR-035)

Supports exporting compliance analysis results.

### Export Formats

| Format | File Extension | Description |
|--------|---------------|-------------|
| Markdown | `.md` | Title, timestamp, summary table, detailed findings |
| JSON | `.json` | Machine-readable findings array |
| HTML | `.html` | Styled page with severity-colored badges |

### Export Actions

| Action | Behavior |
|--------|----------|
| Save to File | `vscode.window.showSaveDialog` → write file |
| Copy to Clipboard | `vscode.env.clipboard.writeText` |
| Preview in Editor | `vscode.workspace.openTextDocument` + `showTextDocument` |

---

## Workspace Service (FR-036)

Template save operations.

### Type-Based Folder Mapping

| Template Type | Folder | Icon |
|--------------|--------|------|
| `bicep` | `bicep/` | `$(file-code)` |
| `terraform` | `terraform/` | `$(file-code)` |
| `kubernetes` | `kubernetes/` | `$(server-environment)` |
| `powershell` | `scripts/` | `$(terminal)` |
| `arm` | `arm/` | `$(json)` |
| Other | `templates/` | `$(file)` |

**Icon rendering**: Icons use VS Code [Codicons](https://code.visualstudio.com/api/references/icons-in-labels) via `$(icon-name)` syntax in Markdown content rendered by `stream.markdown()`. The "Save" button uses `stream.button()` which triggers the `ato.saveTemplate` internal command handler (registered in `extension.ts`) to invoke `WorkspaceService.saveTemplate()`.

### Conflict Resolution (US5, Scenario 5)

When target file exists: prompt with `Overwrite`, `Cancel`, `Save As New` options.
