# Research — 060 VS Code Extension v1.0

## Test infrastructure gap

The `vscode-extension-compile` CI job (`.github/workflows/ci.yml` lines 49-72)
runs only:
```yaml
- name: Compile extension
  run: npm run compile
```

There is no `npm test` step. The 17 `.test.ts` files in `src/` are compiled but
never executed. This means any regression since the test files were written is
undetected by CI.

`package.json` defines:
```json
"test": "node ./dist/test/runTests.js"
```

The runner file `dist/test/runTests.js` is compiled from `test/runTests.ts`
(if it exists). If that source file is missing, the test step will fail with
"cannot find module". Spec 053 / Issue #137 addresses the runner wiring.

## Publisher registration process

`vsce` (the VS Code extension CLI) enforces that the `publisher` value in
`package.json` matches an account registered at
`https://marketplace.visualstudio.com/manage`. Steps:

1. Log in to marketplace.visualstudio.com with the org Microsoft account.
2. Create a Personal Access Token (PAT) with Marketplace → Publish scope.
3. `vsce login ato-copilot` using the PAT.
4. `vsce package` then `vsce publish`.

The PAT must be stored as a GitHub Actions secret (`VSCE_TOKEN`) for automated
publishing.

## Icon requirements

`vsce` validates:
- Icon path exists.
- Icon is PNG (not SVG — SVG is rejected by the Marketplace renderer).
- Minimum 128×128 pixels.

`media/icon.svg` exists. Conversion to PNG can be done with:
```bash
# Option A: sharp (Node)
node -e "require('sharp')('media/icon.svg').resize(128,128).png().toFile('media/icon.png')"

# Option B: ImageMagick
convert -background none media/icon.svg -resize 128x128 media/icon.png
```

## MCP client architecture

`src/services/mcpClient.ts` uses `axios`. The base URL is read from
`vscode.workspace.getConfiguration('ato-copilot').get('apiUrl')` at call time.
This means changing the setting takes effect on the next API call without
requiring extension reload — a good pattern to verify via test.

The `tenantId` and `impersonatedTenantId` settings are forwarded as HTTP headers
(`X-Tenant-Id`, `X-Impersonated-Tenant-Id`) on every MCP request per the
Feature 048 contract.

## Mocha runner compatibility with headless CI

`@vscode/test-electron` launches a real VS Code process with a hidden window
for testing. This works in CI only if `xvfb` (X virtual framebuffer) is
available. On GitHub Actions `ubuntu-latest` runners, `xvfb-run` is available.

The CI test step should use:
```yaml
- name: Run extension tests
  run: xvfb-run -a npm test
  working-directory: extensions/vscode
```

Alternatively, tests that do not need the VS Code runtime can be run with
plain `mocha` by mocking the `vscode` module (the `test/mocks/vscode.ts` pattern).
Inspecting the existing test files will determine which approach is required.

## `vsce` packaging warnings

Running `vsce package` on the current state will warn:
- `WARNING Extension 'ato-copilot-vscode' has no icon.` → fixed by T05.
- `WARNING No CHANGELOG.md found.` → fixed by T06.
- Potential warning on missing `repository` field in package.json → fixed by T07.
