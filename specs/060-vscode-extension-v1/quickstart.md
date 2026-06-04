# Quickstart — 060 VS Code Extension v1.0

## Prerequisites

- Node ≥ 20, npm ≥ 10
- VS Code ≥ 1.90.0
- `vsce` CLI: `npm install -g @vscode/vsce`
- For icon conversion: ImageMagick (`convert`) or `sharp` npm package

## Install dependencies

```bash
cd extensions/vscode
npm install
```

## Compile the extension

```bash
npm run compile
# Output: dist/src/, dist/test/
```

## Run tests

```bash
# Headless (CI-safe, requires xvfb on Linux)
xvfb-run -a npm test

# Developer machine (launches Extension Development Host)
npm test
```

All 17 suites should report green. If any fail, check:
1. `dist/src/` exists (compile step ran).
2. `dist/test/runTests.js` exists.
3. VS Code binary is available (installed via `@vscode/test-electron`).

## Generate the icon PNG

```bash
# Using ImageMagick
convert -background none media/icon.svg -resize 128x128 media/icon.png

# Using sharp (if ImageMagick unavailable)
node -e "require('sharp')('media/icon.svg').resize(128,128).png().toFile('media/icon.png', (e,i) => console.log(e||i))"
```

## Package for Marketplace

```bash
# Produces ato-copilot-vscode-0.1.0.vsix
vsce package --no-dependencies

# Inspect contents (no warnings should appear)
vsce ls
```

## Install locally from VSIX

```bash
code --install-extension ato-copilot-vscode-0.1.0.vsix
```

## Test the extension end-to-end

1. Press `F5` in VS Code (or install the VSIX) to launch Extension Development Host.
2. Open GitHub Copilot Chat (`Ctrl+Shift+I` / `Cmd+Shift+I`).
3. Type `@ato /compliance list controls for AC-2`.
4. Run `Ctrl+Shift+P` → `ATO Copilot: Check API Health` — verify success message.
5. Run `Ctrl+Shift+P` → `ATO Copilot: Configure Connection` — verify settings UI opens.

## Publish to Marketplace (release engineers only)

```bash
# Requires VSCE_TOKEN env var (PAT from marketplace.visualstudio.com)
vsce login ato-copilot
vsce publish
```
