# Extension Manifest Contract — 060 VS Code Extension v1.0

This document is the authoritative contract for `extensions/vscode/package.json`
at the v1.0 release. All fields marked **v1.0 required** must be set before
`vsce package` is run.

---

## Identity

| Field | Type | v1.0 value | Notes |
|-------|------|-----------|-------|
| `name` | string | `ato-copilot-vscode` | npm package name; must be unique on Open VSX / Marketplace |
| `displayName` | string | `ATO Copilot` | Shown in Extensions sidebar |
| `version` | string | `0.1.0` | SemVer; bump before publish |
| `publisher` | string | `ato-copilot` | **Must be a claimed Marketplace account** |
| `description` | string | (existing) | < 100 chars, no markdown |
| `icon` | string | `media/icon.png` | **v1.0 required** — 128×128 PNG |
| `license` | string | `MIT` | **v1.0 required** |
| `repository` | object | `{ "type": "git", "url": "https://github.com/..." }` | **v1.0 required** |
| `engines.vscode` | string | `^1.90.0` | Minimum VS Code version |
| `main` | string | `./dist/src/extension.js` | Compiled entry point |

---

## Activation Events

```json
"activationEvents": [
  "onChatParticipant:ato"
]
```

The extension activates when the `@ato` chat participant is first invoked.
All nine palette commands are registered in the `activate()` function regardless
of activation event.

---

## Chat Participants

```json
"chatParticipants": [
  {
    "id": "ato",
    "fullName": "ATO Copilot",
    "name": "ato",
    "description": "Ask about ATO compliance, NIST 800-53 controls, and infrastructure-as-code",
    "isSticky": true,
    "iconPath": "media/icon.svg",
    "commands": [
      { "name": "compliance", "description": "Run compliance assessments, query controls, remediation" },
      { "name": "knowledge",  "description": "Query ATO knowledge base, best practices, documentation" },
      { "name": "config",     "description": "Manage ATO Copilot configuration settings" }
    ]
  }
]
```

---

## Commands

All nine commands must be registered via `vscode.commands.registerCommand` in
`extension.ts` `activate()`. Each must have a test (see
`checklists/requirements.md` FR-060-13 through FR-060-22).

| `command` | `title` | Handler module | Menu |
|-----------|---------|---------------|------|
| `ato.checkHealth` | ATO Copilot: Check API Health | `commands/health.ts` | — |
| `ato.configure` | ATO Copilot: Configure Connection | `commands/configure.ts` | — |
| `ato.signIn` | ATO Copilot: Sign In | `commands/signIn.ts` | — |
| `ato.signOut` | ATO Copilot: Sign Out | `commands/signOut.ts` | — |
| `ato.switchTenant` | ATO Copilot: Switch Tenant | `commands/switchTenant.ts` | — |
| `ato.analyzeCurrentFile` | ATO Copilot: Analyze Current File for Compliance | `commands/analyzeFile.ts` | — |
| `ato.analyzeWorkspace` | ATO Copilot: Analyze Workspace for Compliance | `commands/analyzeWorkspace.ts` | — |
| `ato.followUpSuggestion` | ATO Copilot: Follow-Up Suggestion | `commands/followUpSuggestion.ts` | — |
| `ato.requestFalsePositive` | ATO Copilot: Request False Positive | `commands/requestFalsePositive.ts` | `editor/context` when `editorTextFocus` |

---

## Configuration Settings

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `ato-copilot.apiUrl` | `string` | `http://localhost:3001` | MCP Server base URL. Examples: `https://ato-copilot-staging.example.com`, `https://ato.example.com` |
| `ato-copilot.apiKey` | `string` | `""` | API key for authentication (if required). Stored in SecretStorage when non-empty. |
| `ato-copilot.timeout` | `number` | `30000` | Request timeout in milliseconds. Range: 1000–300000. |
| `ato-copilot.enableLogging` | `boolean` | `false` | Enable debug logging to the "ATO Copilot" Output channel. |
| `ato-copilot.tenantId` | `string` | `""` | Home tenant id (GUID). Forwarded as `X-Tenant-Id` on MCP requests. |
| `ato-copilot.impersonatedTenantId` | `string` | `""` | CSP-Admin impersonated tenant id (GUID). Forwarded as `X-Impersonated-Tenant-Id`. |

---

## Context Menu

```json
"menus": {
  "editor/context": [
    {
      "command": "ato.requestFalsePositive",
      "when": "editorTextFocus",
      "group": "ATO Copilot"
    }
  ]
}
```

---

## Scripts

| Script | Command | Purpose |
|--------|---------|---------|
| `compile` | `tsc -p ./` | Full build to `dist/` |
| `watch` | `tsc -watch -p ./` | Incremental build |
| `lint` | `eslint src --ext ts` | Lint source (not tests) |
| `pretest` | `npm run compile` | Compile before running tests |
| `test` | `node ./dist/test/runTests.js` | Run mocha suite |
| `vscode:prepublish` | `npm run compile` | Build before packaging |
