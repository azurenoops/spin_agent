/**
 * RequireAuth — regression tests for #362 (infinite MSAL redirect loop).
 *
 * Structural source assertions using ?raw import (Vite/Vitest raw asset import).
 * This avoids Node.js fs/path imports which are not in the dashboard tsconfig lib.
 */
import { describe, it, expect } from 'vitest';
// ?raw import: Vite transforms the file into its source text string.
// Vitest uses the same Vite transform pipeline so this works in tests.
import SOURCE from '../../features/auth/RequireAuth.tsx?raw';

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
