# Editor Bridge Events Catalog

Schema version: **1.1.0**  
Transport: VS Code webview `postMessage` / `onDidReceiveMessage`  
Source of truth: `extensions/vscode/src/bridge/editorBridgeEvents.ts`  
Guard: `extensions/vscode/src/bridge/editorBridgeGuard.ts`

---

## Overview

The editor bridge carries typed messages between the VS Code webview (editor side)
and the extension host (host side). All messages are validated at the host boundary
by `isEditorToHostEvent()` before dispatch. Malformed payloads are rejected with a
console warning; the bridge does not crash on bad input.

**Direction key**

| Symbol | Meaning |
|--------|---------|
| `editor→host` | Webview `vscode.postMessage()` → host `onDidReceiveMessage` |
| `host→editor` | Host `panel.webview.postMessage()` → webview `window.addEventListener('message')` |

---

## Editor → Host Events

### `drillDown`

Requests detailed compliance information for a control.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `command` | `"drillDown"` | yes | Discriminant |
| `controlId` | `string` | yes | Control identifier (e.g. `AC-2`) |
| `conversationId` | `string` | no | Active conversation for context |

---

### `applyFix`

Requests a remediation script diff preview.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `command` | `"applyFix"` | yes | Discriminant |
| `findingId` | `string` | yes | Finding identifier |
| `title` | `string` | yes | Human-readable finding title |
| `remediationScript` | `string` | no | Bicep/script to preview |

---

### `confirmRemediation`

Confirms and applies a remediation to cloud resources.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `command` | `"confirmRemediation"` | yes | Discriminant |
| `findingId` | `string` | yes | Finding identifier |
| `controlId` | `string` | yes | Control identifier |
| `conversationId` | `string` | no | Active conversation |

---

### `updateStatus`

Updates the lifecycle status of a finding.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `command` | `"updateStatus"` | yes | Discriminant |
| `findingId` | `string` | yes | Finding identifier |
| `newStatus` | `string` | yes | Target status (`acknowledged`, `remediated`, `verified`) |
| `conversationId` | `string` | no | Active conversation |

---

### `checkPim`

Initiates a Privileged Identity Management (PIM) pre-flight check.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `command` | `"checkPim"` | yes | Discriminant |
| `conversationId` | `string` | no | Active conversation |

---

### `openCitationSourcePanel` *(v1.1.0, #2343)*

**Direction:** `editor→host`

Requests the host shell open or focus the source-evidence panel for a specific
inline citation. This is the canonical bridge signal for citation → source-panel
navigation; it replaces ad-hoc callbacks and hardcoded panel access.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `command` | `"openCitationSourcePanel"` | yes | Discriminant |
| `citationId` | `string` | yes | Stable citation identifier |
| `sourceId` | `string` | no | Pre-resolved source ID; host resolves from `citationId` when absent |
| `anchor` | `{ blockId: string; offset?: number }` | no | Deep-link position hint within the source document |
| `origin` | `'click' \| 'keyboard' \| 'programmatic'` | yes | Interaction type — used for UX and analytics |
| `requestId` | `string` | yes | Caller-generated UUID for correlation and future ack |
| `meta` | `Record<string, unknown>` | no | Reserved forward-compatibility bag (v1 additive only) |

**Host-side contract:**

- Resolves `sourceId` from `citationId` when `sourceId` is absent.
- If neither resolves to a known source, shows a graceful empty-state notification
  (not an error dialog).
- Debounces rapid repeat emits for the same `citationId` within 300 ms (guards
  against double-click and keyboard repeat events).

**Editor-side contract:**

- Always generates a fresh `requestId` (`crypto.randomUUID()`) per emit.
- Sets `origin` to `'click'` for pointer interactions and `'keyboard'` for Enter/Space.
- A delegated listener on `document` fires for any element with `[data-citation-id]`.

**Usage example:**

```typescript
// Editor side (inside getScript() or a separate citation component):
openCitationSourcePanel('cite-ac2-1', 'click', 'source-nist-ac2');

// Resulting postMessage payload:
{
  command: 'openCitationSourcePanel',
  citationId: 'cite-ac2-1',
  sourceId: 'source-nist-ac2',
  origin: 'click',
  requestId: 'f47ac10b-58cc-4372-a567-0e02b2c3d479',
}

// HTML element that triggers the delegated listener:
<span
  data-citation-id="cite-ac2-1"
  data-source-id="source-nist-ac2"
  tabindex="0"
  role="button"
  aria-label="View source for citation AC-2.1"
>[1]</span>
```

---

## Host → Editor Events

### `drillDownResult`

Returns detailed control information to the webview for inline display.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `command` | `"drillDownResult"` | yes | Discriminant |
| `controlId` | `string` | yes | Control the result is for |
| `data` | `unknown` | no | Structured result data |
| `response` | `string` | no | Human-readable response text |

---

### `progressUpdate`

Reports progress percentage and label during long-running operations.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `command` | `"progressUpdate"` | yes | Discriminant |
| `percentage` | `number` | yes | Progress 0–100 |
| `label` | `string` | no | Human-readable progress description |

---

## Versioning policy

- The union is **additive-only**. New event types are added as new discriminated
  union members; existing members are never modified.
- Breaking changes require a major schema version bump and a migration plan.
- The `meta` bag on `OpenCitationSourcePanelEvent` provides a forward-compatibility
  escape hatch for minor additive extensions without a version bump.
