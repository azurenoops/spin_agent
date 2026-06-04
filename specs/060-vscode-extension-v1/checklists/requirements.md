# Requirements Checklist — 060 VS Code Extension v1.0

## Functional Requirements

### Tests (US1)
- [ ] FR-060-01: All 17 `.test.ts` files in `src/` compile without TypeScript errors.
- [ ] FR-060-02: `npm test` exits 0 in the local developer environment.
- [ ] FR-060-03: `npm test` exits 0 in the CI `vscode-extension-compile` job.
- [ ] FR-060-04: All 17 test suites are reported in test output; none are skipped.
- [ ] FR-060-05: No test file is deleted as a fix strategy; failing tests are repaired.

### Marketplace Packaging (US2)
- [ ] FR-060-06: `media/icon.png` exists and is ≥ 128×128 pixels.
- [ ] FR-060-07: `package.json` sets `"icon": "media/icon.png"`.
- [ ] FR-060-08: `CHANGELOG.md` exists with a `## [0.1.0]` section listing shipped features.
- [ ] FR-060-09: `package.json` sets `"publisher": "ato-copilot"` matching a claimed Marketplace account.
- [ ] FR-060-10: `package.json` includes `"license"` and `"repository"` fields.
- [ ] FR-060-11: `vsce package --no-dependencies` exits 0 with no warnings.
- [ ] FR-060-12: The `.vsix` artifact is uploaded as a CI artifact in the extension CI job.

### Command Coverage (US3)
- [ ] FR-060-13: All nine command IDs from `contributes.commands` are registered via `vscode.commands.registerCommand`.
- [ ] FR-060-14: `ato.checkHealth` has a test asserting `mcpClient.getHealth()` is called.
- [ ] FR-060-15: `ato.configure` has a test asserting the settings UI is opened.
- [ ] FR-060-16: `ato.signIn` has a test asserting the MSAL device-code flow is initiated.
- [ ] FR-060-17: `ato.signOut` has a test asserting the cached token is cleared.
- [ ] FR-060-18: `ato.switchTenant` has a test asserting tenant selection flow.
- [ ] FR-060-19: `ato.analyzeCurrentFile` has a test asserting editor document is read and MCP called.
- [ ] FR-060-20: `ato.analyzeWorkspace` has a test asserting workspace file enumeration.
- [ ] FR-060-21: `ato.followUpSuggestion` has a test asserting chat panel is opened.
- [ ] FR-060-22: `ato.requestFalsePositive` has a test asserting editor selection is read.

### Configurable MCP URL (US4)
- [ ] FR-060-23: `mcpClient.ts` reads `ato-copilot.apiUrl` per-request (not cached across configuration changes).
- [ ] FR-060-24: Extension registers `vscode.workspace.onDidChangeConfiguration` to reset MCP base URL on `ato-copilot.*` changes.
- [ ] FR-060-25: `mcpClient.test.ts` test verifies a URL change takes effect on the next request without reload.
- [ ] FR-060-26: README documents all six settings with purpose, type, default, and example values.

## Non-Functional Requirements

- [ ] NFR-060-01: No new TypeScript compiler errors introduced.
- [ ] NFR-060-02: No new ESLint warnings in CI.
- [ ] NFR-060-03: All 17 test suites complete within 120 seconds in CI.
- [ ] NFR-060-04: VSIX package size is < 5 MB (use `--no-dependencies` to exclude `node_modules`).
- [ ] NFR-060-05: Icon PNG is committed at exactly 128×128 (not upscaled from smaller source).
