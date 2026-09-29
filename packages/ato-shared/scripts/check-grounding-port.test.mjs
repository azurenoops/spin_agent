import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, copyFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

function scan(path, source) {
  const root = mkdtempSync(join(tmpdir(), 'grounding-port-test-'));
  try {
    const script = join(root, 'packages/ato-shared/scripts/check-grounding-port.mjs');
    mkdirSync(dirname(script), { recursive: true });
    copyFileSync(fileURLToPath(new URL('./check-grounding-port.mjs', import.meta.url)), script);
    const file = join(root, path);
    mkdirSync(dirname(file), { recursive: true });
    writeFileSync(file, source);
    const result = spawnSync(process.execPath, [script], { encoding: 'utf8' });
    assert.ifError(result.error);
    return { code: result.status, text: result.stdout + result.stderr };
  } finally {
    rmSync(root, { recursive: true });
  }
}

const browserFixture = 'src/Ato.Copilot.Dashboard/e2e/isolated/provider-presentation.local.ts';

test('permits only screenshot comparison page setup in the isolated browser fixture', () => {
  // Arrange / Act
  const result = scan(browserFixture, 'await comparison.setContent("<html>Synthetic screenshot comparison</html>");');
  // Assert
  assert.equal(result.code, 0, result.text);
});

test('still rejects production setContent without grounding', () => {
  // Arrange / Act
  const result = scan('src/Ato.Copilot.Dashboard/src/pages/Claims.ts', 'editor.setContent("An unsupported claim");');
  // Assert
  assert.equal(result.code, 1);
  assert.match(result.text, /\[Rule A\]/);
});

test('does not exempt other fixture mutation APIs', () => {
  // Arrange / Act
  const result = scan(browserFixture, 'document.insertContent("An unsupported claim");');
  // Assert
  assert.equal(result.code, 1);
  assert.match(result.text, /\[Rule A\]/);
});

test('does not exempt document setContent in the same fixture', () => {
  // Arrange / Act
  const result = scan(browserFixture, 'editor.setContent("An unsupported claim");');
  // Assert
  assert.equal(result.code, 1);
  assert.match(result.text, /\[Rule A\]/);
});

test('still checks evidence sentinels and bare claim nodes in the browser fixture', () => {
  // Arrange / Act
  const result = scan(browserFixture, 'comparison.setContent("<html/>"); const value = { evidenceSpan: [0, 0], spanRef: "x", agentOrigin: "agent" };');
  // Assert
  assert.equal(result.code, 1);
  assert.match(result.text, /\[Rule B\]/);
  assert.match(result.text, /\[Rule D\]/);
});

test('continues to accept actual grounded production content', () => {
  // Arrange / Act
  const result = scan('src/Editor.ts', 'GroundingPort.bind(claim, evidence); editor.insertContent(claim);');
  // Assert
  assert.equal(result.code, 0, result.text);
});
