# Feature Specification: VS Code Extension v1.0 — Marketplace + Commands + Tests

**Feature Branch**: `060-vscode-extension-v1`
**Created**: 2026-06-03
**Status**: Draft
**GitHub Issue**: #128
**Depends on**: Issue #137 / Spec 053 (CI wiring for VS Code tests — tests must
be *runnable* in CI before this spec can make them *green*)

## Background

The ATO Copilot VS Code extension (`extensions/vscode/`) is functionally rich —
it ships a GitHub Copilot Chat participant (`@ato`), nine palette commands, IaC
diagnostics, code actions, an analysis panel, PIM flow, and Azure MSAL auth.
However, three blockers prevent it from reaching the VS Code Marketplace:

1. **Tests never run in CI.** The `vscode-extension-compile` CI job only
   compiles (`npm run compile`). The 16 mocha test files in `src/` (co-located
   with source, not in a `test/suite/` directory) are never executed. Spec 053 /
   Issue #137 wires the runner; this spec makes the tests green.

2. **Publisher field is a placeholder.** `package.json` sets
   `"publisher": "ato-copilot"` — a value that must be claimed on the VS Code
   Marketplace before `vsce publish` can succeed. The `CHANGELOG.md`, icon, and
   packaging script also need verification.

3. **Commands lack uniform test coverage.** The nine `contributes.commands`
   entries each have implementations in `src/commands/`, but some tests are stubs
   or test only the happy path without mocking the MCP client.

### Verified state of the code

**`package.json` summary**

| Field | Value |
|-------|-------|
| `name` | `ato-copilot-vscode` |
| `displayName` | `ATO Copilot` |
| `version` | `0.1.0` |
| `publisher` | `ato-copilot` (must be claimed on Marketplace) |
| `engines.vscode` | `^1.90.0` |
| `main` | `./dist/src/extension.js` |
| `activationEvents` | `["onChatParticipant:ato"]` |

**Registered commands** (from `contributes.commands`):

| Command ID | Title |
|-----------|-------|
| `ato.checkHealth` | ATO Copilot: Check API Health |
| `ato.configure` | ATO Copilot: Configure Connection |
| `ato.signIn` | ATO Copilot: Sign In |
| `ato.signOut` | ATO Copilot: Sign Out |
| `ato.switchTenant` | ATO Copilot: Switch Tenant |
| `ato.analyzeCurrentFile` | ATO Copilot: Analyze Current File for Compliance |
| `ato.analyzeWorkspace` | ATO Copilot: Analyze Workspace for Compliance |
| `ato.followUpSuggestion` | ATO Copilot: Follow-Up Suggestion |
| `ato.requestFalsePositive` | ATO Copilot: Request False Positive |

**Configuration settings** (from `contributes.configuration`):

| Key | Type | Default |
|-----|------|---------|
| `ato-copilot.apiUrl` | `string` | `http://localhost:3001` |
| `ato-copilot.apiKey` | `string` | `""` |
| `ato-copilot.timeout` | `number` | `30000` |
| `ato-copilot.enableLogging` | `boolean` | `false` |
| `ato-copilot.tenantId` | `string` | `""` |
| `ato-copilot.impersonatedTenantId` | `string` | `""` |

**Source structure** (directories under `src/`):
`auth/`, `codeActions/`, `commands/`, `diagnostics/`, `panels/`, `services/`,
`webview/` + `extension.ts`, `participant.ts`

**Test files** (co-located in `src/`, not in `test/suite/`):
`analysisPanel.test.ts`, `analysisPanelActions.test.ts`,
`analysisPanelGrouping.test.ts`, `analyzeCommands.test.ts`,
`complianceFinding.test.ts`, `exportWorkspace.test.ts`, `iacCodeActions.test.ts`,
`iacDiagnostics.test.ts`, `mcpClient.test.ts`, `participant.test.ts`,
`participantAttribution.test.ts`, `participantComplianceSummary.test.ts`,
`participantEnrichment.test.ts`, `participantStreaming.test.ts`,
`pimFlow.test.ts`, `rmfPanel.test.ts`, `sseClient.test.ts`

**CI gap**: `vscode-extension-compile` job runs `npm run compile` only. No
`npm test` step exists. Spec 053 / Issue #137 adds the runner step; this spec's
US1 ensures all 17 test files pass once wired.

**MCP client**: `src/services/mcpClient.ts` — uses `axios` with base URL read
from `vscode.workspace.getConfiguration('ato-copilot').get('apiUrl')` at call
time (configurable, not hardcoded).

## Clarifications

- **Q: Should the publisher be `ato-copilot` or `nous-research`?**
  **A:** Register `ato-copilot` publisher on marketplace.visualstudio.com. If
  the org decides to publish under `nous-research`, the package.json value and
  the VSIX display name must change together. This spec uses `ato-copilot` as
  the target.

- **Q: Is `CHANGELOG.md` missing or just incomplete?**
  **A:** A placeholder `CHANGELOG.md` must be created with at least a `## 0.1.0`
  entry listing the features shipped. `vsce` rejects packages with no changelog.

- **Q: Does the extension need an icon PNG for Marketplace listing?**
  **A:** `media/icon.svg` exists. `vsce` requires a PNG ≥ 128×128. Add
  `media/icon.png` (128×128) and set `"icon": "media/icon.png"` in `package.json`.

- **Q: Should `ato.followUpSuggestion` and `ato.requestFalsePositive` remain as
  commands, or are they internal participant actions?**
  **A:** Keep as registered commands; add tests that verify the command handlers
  are registered and do not throw on invocation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — All mocha tests pass in CI (Priority: P1)

**As a** maintainer
**I want** all 17 VS Code mocha test files to pass in CI
**So that** regressions in the extension are caught automatically before merge.

**Why this priority**: Tests exist but never run. Any breaking change since the
test files were written is currently invisible. Making tests green in CI is
the foundational quality gate for everything else in this epic.

**Independent Test**: `npm test` in `extensions/vscode/` exits 0 with all
17 suites reporting. Delivers standalone value: CI now catches regressions.

**Acceptance**
- Every `.test.ts` file in `src/` compiles without error.
- `npm test` exits 0 on developer machines and in the CI job added by spec 053.
- Test runner output lists each suite by file name; no suite is skipped.
- Failing tests are fixed, not deleted (if a test asserts wrong behavior, fix
  the implementation or the assertion with a comment explaining the change).

### User Story 2 — Marketplace-publishable package (Priority: P1)

**As a** release engineer
**I want** `vsce package` to produce a valid `.vsix` without warnings
**So that** the extension can be submitted to the VS Code Marketplace.

**Why this priority**: Extension value is locked to internal developer machines
until it ships on the Marketplace. The packaging blockers (icon, changelog,
publisher) are each one-line fixes that unlock distribution to the broader ATO
practitioner community.

**Independent Test**: Run `vsce package --no-dependencies` in `extensions/vscode/`;
assert exit code 0 and the output `.vsix` exists; inspect the manifest with
`vsce ls` and assert no warnings about missing icon, changelog, or publisher.

**Acceptance**
- `"icon": "media/icon.png"` set in `package.json`.
- `media/icon.png` exists (128×128 PNG, generated from existing SVG).
- `CHANGELOG.md` exists with at minimum a `## [0.1.0]` section.
- `publisher` field in `package.json` is `"ato-copilot"` (matching a claimed
  Marketplace publisher account).
- `vsce package` exits 0 with no warnings.
- `.vsix` artifact is uploaded as a CI artifact in the `vscode-extension-compile`
  job (or a new `vscode-extension-package` job).

### User Story 3 — All commands tested (Priority: P2)

**As a** developer
**I want** every command in `contributes.commands` to have an implementation
and a test
**So that** users never invoke a command and see "command not found" or a JS
exception.

**Why this priority**: P2 — the commands are implemented but test coverage is
uneven. Some test files focus on participant behavior; commands like
`ato.followUpSuggestion` and `ato.requestFalsePositive` lack dedicated tests.

**Independent Test**: For each command ID, assert: (a) a `registerCommand` call
exists in `extension.ts` or a commands/ module, and (b) a test file exercises
the handler. `npm test` passes.

**Acceptance**
- All nine command IDs are registered via `vscode.commands.registerCommand`.
- Each command has at least one test that: invokes the handler, asserts no
  exception thrown, and asserts the expected VS Code API calls were made
  (using sinon stubs for `vscode.window.*`, `vscode.commands.*`).
- `ato.requestFalsePositive` test verifies it reads editor selection text.
- `ato.followUpSuggestion` test verifies it opens a chat panel.

### User Story 4 — Configurable MCP server URL (Priority: P2)

**As a** developer using a non-default MCP server deployment
**I want** the extension to read its MCP server URL from VS Code settings
**So that** I can point it at staging, prod, or a self-hosted instance without
editing source code.

**Why this priority**: P2 — `mcpClient.ts` already reads from
`ato-copilot.apiUrl` at call time. This story validates the contract, adds a
settings-change listener, and ensures tests cover the config path.

**Independent Test**: In `mcpClient.test.ts`, stub
`vscode.workspace.getConfiguration` to return `http://custom:9999`; assert the
axios request targets `http://custom:9999/...`. Change stub mid-test; assert next
call uses updated URL.

**Acceptance**
- `mcpClient.ts` reads `ato-copilot.apiUrl` per-request (already implemented;
  verify by test).
- Extension registers a `vscode.workspace.onDidChangeConfiguration` listener
  that invalidates any cached base URL when `ato-copilot.*` settings change.
- `ato-copilot.apiUrl` default `http://localhost:3001` matches the default MCP
  server dev port.
- Settings documented in README with example values for staging and prod.
