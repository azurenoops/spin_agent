# Tasks — 060 VS Code Extension v1.0

## Milestone 1 — Make tests green (US1, P1)

### T01 — Audit all 17 test files for compilation errors
**Work**:
- `npm run compile` with full output; collect every TS error from `.test.ts` files.
- Categorise errors: wrong import path, missing mock type, changed API signature.
- Output: a triage table (file → error → fix action).

**Estimate**: 2 pts

---

### T02 — Fix test compilation errors
**Work**: Apply fixes identified in T01. No test should be deleted; every
failing assertion should be fixed at the implementation level or the assertion
updated with an explanatory comment.

Common expected fixes:
- Update sinon stub types for `vscode.window.showInformationMessage`.
- Align `mcpClient.test.ts` mock shape with current `McpClient` interface.
- Fix import paths broken by any directory restructure.

**Estimate**: 3 pts

---

### T03 — Verify test runner wiring (from spec 053)
**Work**:
- Confirm `dist/test/runTests.js` exists after compile.
- Confirm `test/runTests.ts` (or equivalent) uses `@vscode/test-electron` or a
  headless runner compatible with CI (no display server required).
- If spec 053 is not yet merged, apply the minimal runner setup here:
  - Add `"test:headless": "node ./dist/src/runTests.js"` script.
  - Use `@vscode/test-electron` with `extensionDevelopmentPath` set.
- Add `npm test` step to CI `vscode-extension-compile` job.

**Estimate**: 2 pts (0 if spec 053 already merged)

---

### T04 — Run full test suite; fix remaining failures
**Work**: `npm test` in CI; triage any runtime failures (e.g., missing VS Code
API stubs at runtime). Fix each failure; do not skip.

**Estimate**: 3 pts

---

## Milestone 2 — Marketplace packaging (US2, P1)

### T05 — Generate `media/icon.png`
**Work**:
- Convert existing `media/icon.svg` to `media/icon.png` at 128×128 using
  `sharp`, `inkscape --export-png`, or a one-time script.
- Commit the PNG; add `"icon": "media/icon.png"` to `package.json`.

**Estimate**: 1 pt

---

### T06 — Create `CHANGELOG.md`
**Work**:
- Create `CHANGELOG.md` following
  [Keep a Changelog](https://keepachangelog.com/) format.
- Minimum content:
  ```md
  ## [0.1.0] — 2026-06-03
  ### Added
  - @ato chat participant with /compliance, /knowledge, /config slash commands
  - Analyze current file and workspace commands
  - Azure MSAL sign-in (device code flow)
  - IaC diagnostics and code actions for Bicep, Terraform, YAML
  - RMF panel and analysis panel
  - Export results as Markdown, JSON, or HTML
  ```

**Estimate**: 1 pt

---

### T07 — Claim publisher and verify `package.json`
**Work**:
- Register `ato-copilot` publisher account at marketplace.visualstudio.com
  (one-time human action; document in `CONTRIBUTING.md`).
- Verify `package.json` fields: `name`, `displayName`, `publisher`, `icon`,
  `repository`, `license` (add MIT `LICENSE` file if absent).

**Estimate**: 1 pt

---

### T08 — Add `vsce package` to CI
**Work**:
- Add `npx vsce package --no-dependencies -o ato-copilot.vsix` step to
  `vscode-extension-compile` CI job (after compile).
- Upload `ato-copilot.vsix` as a CI artifact.
- Assert step exits 0 (packaging failure = CI failure).

**Estimate**: 1 pt

---

## Milestone 3 — Command test coverage (US3, P2)

### T09 — Audit command registration and test gaps
**Work**: For each of the nine command IDs, verify:
1. `registerCommand('ato.X', handler)` call exists.
2. A test file exercises the handler.

Output a gap table; create stub test files for commands with no coverage.

**Estimate**: 1 pt

---

### T10 — Add tests for `ato.followUpSuggestion`
**Work**: Create `src/followUpSuggestion.test.ts` (or add to
`analyzeCommands.test.ts`). Stub `vscode.commands.executeCommand` for
`workbench.action.chat.open`; assert it is called with the expected query.

**Estimate**: 2 pts

---

### T11 — Add tests for `ato.requestFalsePositive`
**Work**: Add tests verifying the handler reads `editor.selection` text and
posts to the MCP false-positive endpoint (stub `mcpClient`).

**Estimate**: 2 pts

---

### T12 — Add tests for `ato.checkHealth` and `ato.configure`
**Work**: `ato.checkHealth` test asserts `mcpClient.getHealth()` called and
result shown via `showInformationMessage`. `ato.configure` test asserts
`vscode.commands.executeCommand('workbench.action.openSettings', ...)` called.

**Estimate**: 2 pts

---

## Milestone 4 — Configurable MCP URL (US4, P2)

### T13 — Add `onDidChangeConfiguration` listener
**File**: `src/extension.ts`
**Work**:
- Register `vscode.workspace.onDidChangeConfiguration(e => { if (e.affectsConfiguration('ato-copilot')) { mcpClient.resetBaseUrl(); } })`.
- Add `resetBaseUrl()` method to `McpClient` class.

**Estimate**: 1 pt

---

### T14 — Test config change propagation in `mcpClient.test.ts`
**Work**:
- Add test: stub config to return URL A, make request, assert URL A used.
- Change stub to URL B, call `resetBaseUrl()`, make request, assert URL B used.

**Estimate**: 2 pts

---

### T15 — Update README settings table
**Work**: Add staging and prod example values to README `## Configuration` section.
Document each setting key with its purpose and example.

**Estimate**: 1 pt

---

## Summary

| Task | US | Points | Priority |
|------|----|--------|----------|
| T01 Audit test compilation | US1 | 2 | P1 |
| T02 Fix test errors | US1 | 3 | P1 |
| T03 Runner wiring | US1 | 2 | P1 |
| T04 Runtime failures | US1 | 3 | P1 |
| T05 icon.png | US2 | 1 | P1 |
| T06 CHANGELOG | US2 | 1 | P1 |
| T07 Publisher + package.json | US2 | 1 | P1 |
| T08 vsce in CI | US2 | 1 | P1 |
| T09 Command gap audit | US3 | 1 | P2 |
| T10 followUpSuggestion test | US3 | 2 | P2 |
| T11 requestFalsePositive test | US3 | 2 | P2 |
| T12 checkHealth + configure test | US3 | 2 | P2 |
| T13 onDidChangeConfiguration | US4 | 1 | P2 |
| T14 Config change test | US4 | 2 | P2 |
| T15 README settings | US4 | 1 | P2 |
| **Total** | | **25** | |
