// @vitest-environment node
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync, copyFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';

const repository = resolve(import.meta.dirname, '../../../../..');
const fixtures: string[] = [];

function fixture() {
  const root = mkdtempSync(join(tmpdir(), 'onboarding-agent-context-'));
  fixtures.push(root);
  for (const path of [
    '.specify/scripts/bash/update-agent-context.sh',
    '.specify/scripts/bash/common.sh',
    '.specify/templates/agent-file-template.md',
  ]) {
    mkdirSync(dirname(join(root, path)), { recursive: true });
    copyFileSync(join(repository, path), join(root, path));
  }
  mkdirSync(join(root, 'specs/078-fixture'), { recursive: true });
  writeFileSync(join(root, 'specs/078-fixture/plan.md'), [
    '**Language/Version**: C#',
    '**Primary Dependencies**: React',
    '**Storage**: SQLite',
    '**Project Type**: Web',
  ].join('\n'));
  mkdirSync(join(root, '.github'));
  const target = join(root, '.github/copilot-instructions.md');
  writeFileSync(target, [
    '# Fixture',
    '## Active Technologies',
    '- C# + React (078-fixture)',
    '- SQLite (078-fixture)',
    '',
    '## Recent Changes',
    '- Previous feature',
    '',
    '<!-- MANUAL ADDITIONS START -->',
    'Keep this manually maintained guidance.',
    '<!-- MANUAL ADDITIONS END -->',
    '',
  ].join('\n'));
  return { root, target };
}

afterEach(() => {
  for (const root of fixtures.splice(0)) rmSync(root, { recursive: true });
});

describe('required agent context generation', () => {
  it('retains full plan values and does not treat leading-dash entries as grep options', () => {
    // Arrange
    const { root, target } = fixture();

    // Act
    const result = spawnSync('bash', ['.specify/scripts/bash/update-agent-context.sh', 'copilot'], {
      cwd: root,
      env: { ...process.env, SPECIFY_FEATURE: '078-fixture' },
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    const generated = readFileSync(target, 'utf8');
    const technologies = generated.split('## Active Technologies\n')[1]!.split('## Recent Changes')[0]!;

    // Assert
    expect(result.error).toBeUndefined();
    expect(result.status).toBe(0);
    expect.soft(result.stderr).toBe('');
    expect.soft(result.stdout).toContain('Found framework: React\n');
    expect.soft(technologies.match(/^- C# \+ React \(078-fixture\)$/gm)).toHaveLength(1);
    expect.soft(technologies.match(/^- SQLite \(078-fixture\)$/gm)).toHaveLength(1);
    expect.soft(technologies).not.toContain('Reac (');
    expect(generated).toContain('<!-- MANUAL ADDITIONS START -->\nKeep this manually maintained guidance.\n<!-- MANUAL ADDITIONS END -->');
  });
});
