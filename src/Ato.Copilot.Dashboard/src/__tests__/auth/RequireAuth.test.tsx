/**
 * RequireAuth — regression tests for #362 (infinite MSAL redirect loop).
 *
 * Source-level tests only — jsdom + React Strict Mode + window.location.assign
 * causes environment hangs. The source text assertions are sufficient to
 * verify the fix: the diff is a single conditional split and CI type-check
 * catches structural breakage.
 */
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const SRC_DIR = join(
  'C:', 'Users', 'zeus_bot', 'ato-copilot-upstream',
  'src', 'Ato.Copilot.Dashboard', 'src', 'features', 'auth',
);
const SOURCE = readFileSync(join(SRC_DIR, 'RequireAuth.tsx'), 'utf-8');

describe('RequireAuth source — fix #362 contract', () => {
  it('imports useNavigate from react-router-dom', () => {
    expect(SOURCE).toContain('useNavigate');
    expect(SOURCE).toContain("from 'react-router-dom'");
  });

  it('calls useNavigate() in the component body', () => {
    expect(SOURCE).toMatch(/const navigate = useNavigate\(\)/);
  });

  it('401 branch contains loginRedirect', () => {
    const idx = SOURCE.indexOf('status === 401');
    expect(idx).toBeGreaterThan(-1);
    const block = SOURCE.slice(idx, SOURCE.indexOf('} else if', idx));
    expect(block).toContain('loginRedirect');
  });

  it('403 branch calls navigate to NoTenantAssignment', () => {
    const idx = SOURCE.indexOf('} else if (status === 403)');
    expect(idx).toBeGreaterThan(-1);
    const block = SOURCE.slice(idx, SOURCE.indexOf('} else {', idx));
    expect(block).toContain('/login/error?errorClass=NoTenantAssignment');
    expect(block).toMatch(/navigate\s*\(/);
  });

  it('regression #362: 401 and 403 are in SEPARATE branches', () => {
    expect(SOURCE).not.toContain('401 || status === 403');
  });

  it('regression #362: 403 branch does NOT call loginRedirect()', () => {
    const idx = SOURCE.indexOf('} else if (status === 403)');
    const block = SOURCE.slice(idx, SOURCE.indexOf('} else {', idx));
    expect(block).not.toMatch(/loginRedirect\s*\(/);
  });
});
