# Plan — 060 VS Code Extension v1.0

## Delivery order

```
Week 1 — Tests green (P1)
├── T01  Audit 17 test files for errors
├── T02  Fix compilation errors
├── T03  Verify/fix runner wiring (spec 053 dependency)
└── T04  Fix runtime test failures

Week 1 — Marketplace packaging (P1, parallelisable with tests)
├── T05  Generate media/icon.png
├── T06  Create CHANGELOG.md
├── T07  Claim publisher + fix package.json fields
└── T08  Add vsce package step to CI

Week 2 — Command coverage (P2)
├── T09  Gap audit
├── T10  followUpSuggestion test
├── T11  requestFalsePositive test
└── T12  checkHealth + configure tests

Week 2 — Config change propagation (P2)
├── T13  onDidChangeConfiguration listener
├── T14  Config change test
└── T15  README settings update
```

## Dependency on spec 053

Spec 053 / Issue #137 adds `npm test` to the CI job. If 053 is not merged when
work on 060 begins, T03 covers the minimal runner wiring needed to unblock test
execution. The 060 branch should be rebased on 053 once that branch merges to
avoid duplicate CI changes.

## Branch strategy

`feat/060-vscode-extension-v1` off `main`. Commit structure:

1. `fix(vscode): resolve test compilation errors`
2. `fix(vscode): fix runtime test failures — N/17 pass`
3. `feat(vscode): add icon.png, CHANGELOG.md, publisher fields for Marketplace`
4. `ci(vscode): add vsce package step and test run to CI`
5. `test(vscode): add missing command handler tests`
6. `feat(vscode): propagate config changes via onDidChangeConfiguration`

## Definition of Done

- [ ] `npm test` exits 0 in CI with all 17 suites reported.
- [ ] `vsce package --no-dependencies` exits 0 with no warnings.
- [ ] `ato-copilot.vsix` artifact uploaded in CI.
- [ ] All nine command IDs have `registerCommand` + at least one test.
- [ ] `mcpClient.ts` reads base URL per-request; test proves config change
      takes effect without extension reload.
- [ ] `CHANGELOG.md` exists with `## [0.1.0]` entry.
- [ ] `media/icon.png` (128×128) committed.
- [ ] No new TypeScript compiler errors.
- [ ] No new ESLint warnings.
