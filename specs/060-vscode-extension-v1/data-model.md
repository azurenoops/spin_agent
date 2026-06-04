# Data Model — 060 VS Code Extension v1.0

## No Database Entities

The VS Code extension is a pure client-side application. It has no database,
no backend persistence, and no new API endpoints. This document describes the
**extension manifest schema** and **VS Code settings schema** as the canonical
data model for this epic.

---

## Extension Manifest (`package.json`)

The manifest is the authoritative contract between the extension and the VS Code
host. All fields below are currently set in `extensions/vscode/package.json`.

### Top-level identity fields

| Field | Type | Current value | v1.0 requirement |
|-------|------|--------------|-----------------|
| `name` | `string` | `ato-copilot-vscode` | unchanged |
| `displayName` | `string` | `ATO Copilot` | unchanged |
| `version` | `string` | `0.1.0` | unchanged for initial release |
| `publisher` | `string` | `ato-copilot` | must be a **claimed** Marketplace account |
| `description` | `string` | set | unchanged |
| `icon` | `string` | _(missing)_ | **`"media/icon.png"`** (128×128 PNG) |
| `engines.vscode` | `string` | `^1.90.0` | unchanged |
| `main` | `string` | `./dist/src/extension.js` | unchanged |
| `license` | `string` | _(missing)_ | **`"MIT"`** |
| `repository` | `object` | _(missing)_ | **`{ type: "git", url: "..." }`** |

### `contributes.chatParticipants[]`

| Field | Value |
|-------|-------|
| `id` | `"ato"` |
| `fullName` | `"ATO Copilot"` |
| `name` | `"ato"` (used as `@ato` in chat) |
| `isSticky` | `true` |
| `iconPath` | `"media/icon.svg"` |
| slash commands | `/compliance`, `/knowledge`, `/config` |

### `contributes.commands[]`

See `contracts/extension-manifest.md` for the full table.

### `contributes.configuration` (settings schema)

See VS Code Settings Schema section below.

---

## VS Code Settings Schema

Settings are accessed via `vscode.workspace.getConfiguration('ato-copilot')`.

```jsonc
{
  "ato-copilot.apiUrl": {
    "type": "string",
    "default": "http://localhost:3001",
    "description": "MCP Server base URL"
    // Examples: "https://ato-copilot-staging.example.com"
    //           "https://ato-copilot.example.com"
  },
  "ato-copilot.apiKey": {
    "type": "string",
    "default": "",
    "description": "API key for authentication (if required by the MCP server)"
    // Stored in VS Code SecretStorage when non-empty.
  },
  "ato-copilot.timeout": {
    "type": "number",
    "default": 30000,
    "description": "Request timeout in milliseconds",
    "minimum": 1000,
    "maximum": 300000
  },
  "ato-copilot.enableLogging": {
    "type": "boolean",
    "default": false,
    "description": "Enable debug logging to the ATO Copilot output channel"
  },
  "ato-copilot.tenantId": {
    "type": "string",
    "default": "",
    "description": "Caller home tenant id (Guid). Forwarded on outbound MCP requests."
  },
  "ato-copilot.impersonatedTenantId": {
    "type": "string",
    "default": "",
    "description": "Optional CSP-Admin impersonated tenant id (Guid)."
  }
}
```

### TypeScript type for settings

```typescript
// src/services/config.ts
export interface AtoExtensionConfig {
  apiUrl: string;
  apiKey: string;
  timeout: number;
  enableLogging: boolean;
  tenantId: string;
  impersonatedTenantId: string;
}

export function getConfig(): AtoExtensionConfig {
  const cfg = vscode.workspace.getConfiguration('ato-copilot');
  return {
    apiUrl:                cfg.get<string>('apiUrl', 'http://localhost:3001'),
    apiKey:                cfg.get<string>('apiKey', ''),
    timeout:               cfg.get<number>('timeout', 30_000),
    enableLogging:         cfg.get<boolean>('enableLogging', false),
    tenantId:              cfg.get<string>('tenantId', ''),
    impersonatedTenantId:  cfg.get<string>('impersonatedTenantId', ''),
  };
}
```

---

## SecretStorage schema

Auth tokens cached by `src/auth/secretStorage.ts`:

| Key | Value |
|-----|-------|
| `ato-copilot.accessToken` | Azure MSAL access token JWT |
| `ato-copilot.refreshToken` | MSAL refresh token |

These are written by `ato.signIn` and cleared by `ato.signOut`. They are
**not** part of `contributes.configuration` — they live exclusively in
`vscode.ExtensionContext.secrets`.
