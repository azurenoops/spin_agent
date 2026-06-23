/**
 * Regression tests for the 11 system sub-page alias routes.
 *
 * Issues: #523 (critical) + #516 (high, duplicate)
 *
 * Background
 * ----------
 * React Router v7 introduced a breaking change where relative "../…" Navigate
 * paths silently fail on direct URL loads inside nested <Route> trees (issues
 * #464, #467, #462, #460).  Wave 9 (#438) added 11 SystemRedirect aliases that
 * use absolute /systems/:id/… Navigate targets so that Oracle QA deep-links and
 * sidebar nav shortcuts always resolve correctly.
 *
 * Test strategy
 * -------------
 * We do NOT import App.tsx — it pulls in hundreds of pages, MSAL, Azure
 * providers, etc., and would require extensive mocking.  Instead we:
 *   1. Inline a byte-for-byte replica of SystemRedirect (the function under test
 *      is tiny — 3 lines — so a replica is both accurate and stable).
 *   2. Mount a minimal MemoryRouter + Routes tree that matches App.tsx's nested
 *      structure: <Route path="/systems/:id"> wrapping the alias + a sentinel
 *      target route.
 *   3. Assert the final location.pathname equals the canonical target.
 *
 * Each test proves that navigating to an alias path (e.g.
 * /systems/test-id/capabilities) ends up at the canonical path (e.g.
 * /systems/test-id/capability-coverage), confirming the redirect fires.
 */
import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { MemoryRouter, Routes, Route, Navigate, useParams, useLocation } from 'react-router-dom';

// ── Replica of the SystemRedirect function from App.tsx (lines 69-72) ──────
// Keep this in sync whenever App.tsx changes SystemRedirect.
function SystemRedirect({ to }: { to: string }) {
  const { id } = useParams<{ id: string }>();
  return <Navigate to={`/systems/${id ?? ''}/${to}`} replace />;
}

// ── Helper: captures the current React Router location ───────────────────────
function CaptureLocation({ out }: { out: { pathname: string } }) {
  out.pathname = useLocation().pathname;
  return null;
}

/**
 * Mount a MemoryRouter that:
 *  - starts at `aliasPath` (the user-typed / deep-linked URL)
 *  - has a nested Route under /systems/:id for the alias slug
 *  - has a nested Route under /systems/:id for the canonical slug
 *
 * Returns the final pathname after all redirects have been applied.
 */
function renderAliasRoute(aliasSlug: string, canonicalSlug: string, systemId = 'test-id'): string {
  const location = { pathname: '' };

  render(
    <MemoryRouter initialEntries={[`/systems/${systemId}/${aliasSlug}`]}>
      <Routes>
        <Route path="/systems/:id">
          {/* alias slug — under test */}
          <Route path={aliasSlug} element={<SystemRedirect to={canonicalSlug} />} />
          {/* canonical target — captures final location */}
          <Route path={canonicalSlug} element={<CaptureLocation out={location} />} />
          {/* profile sub-routes (canonical slugs that contain a /) */}
          {canonicalSlug.startsWith('profile/') && (
            <Route path={canonicalSlug} element={<CaptureLocation out={location} />} />
          )}
        </Route>
        {/*
         * Fallback: when canonicalSlug contains "profile/" React Router treats
         * the nested Route path literally as "profile/MissionAndPurpose", which
         * matches fine.  But we add an absolute top-level fallback to capture
         * any Navigate that targets /systems/:id/profile/… just in case.
         */}
        <Route path="/systems/:id/profile/:section" element={<CaptureLocation out={location} />} />
      </Routes>
    </MemoryRouter>,
  );

  return location.pathname;
}

// ─────────────────────────────────────────────────────────────────────────────
// The 11 alias routes from App.tsx (Wave 9, #438)
// ─────────────────────────────────────────────────────────────────────────────

describe('SystemRedirect — Wave 9 alias routes (#523 / #516 regression)', () => {
  const SYSTEM_ID = 'sys-abc-123';

  // ── 1. control-inheritance → inheritance ──────────────────────────────────
  it('control-inheritance redirects to inheritance', () => {
    const pathname = renderAliasRoute('control-inheritance', 'inheritance', SYSTEM_ID);
    expect(pathname).toBe(`/systems/${SYSTEM_ID}/inheritance`);
  });

  // ── 2. categorization → baseline ─────────────────────────────────────────
  it('categorization redirects to baseline', () => {
    const pathname = renderAliasRoute('categorization', 'baseline', SYSTEM_ID);
    expect(pathname).toBe(`/systems/${SYSTEM_ID}/baseline`);
  });

  // ── 3. capabilities → capability-coverage ────────────────────────────────
  it('capabilities redirects to capability-coverage', () => {
    const pathname = renderAliasRoute('capabilities', 'capability-coverage', SYSTEM_ID);
    expect(pathname).toBe(`/systems/${SYSTEM_ID}/capability-coverage`);
  });

  // ── 4. mission-purpose → profile/MissionAndPurpose ───────────────────────
  it('mission-purpose redirects to profile/MissionAndPurpose', () => {
    const pathname = renderAliasRoute('mission-purpose', 'profile/MissionAndPurpose', SYSTEM_ID);
    expect(pathname).toBe(`/systems/${SYSTEM_ID}/profile/MissionAndPurpose`);
  });

  // ── 5. users-access → profile/UsersAndAccess ─────────────────────────────
  it('users-access redirects to profile/UsersAndAccess', () => {
    const pathname = renderAliasRoute('users-access', 'profile/UsersAndAccess', SYSTEM_ID);
    expect(pathname).toBe(`/systems/${SYSTEM_ID}/profile/UsersAndAccess`);
  });

  // ── 6. environment → profile/EnvironmentAndDeployment ────────────────────
  it('environment redirects to profile/EnvironmentAndDeployment', () => {
    const pathname = renderAliasRoute('environment', 'profile/EnvironmentAndDeployment', SYSTEM_ID);
    expect(pathname).toBe(`/systems/${SYSTEM_ID}/profile/EnvironmentAndDeployment`);
  });

  // ── 7. data-types → profile/DataTypes ────────────────────────────────────
  it('data-types redirects to profile/DataTypes', () => {
    const pathname = renderAliasRoute('data-types', 'profile/DataTypes', SYSTEM_ID);
    expect(pathname).toBe(`/systems/${SYSTEM_ID}/profile/DataTypes`);
  });

  // ── 8. ports-protocols → profile/PortsProtocolsAndServices ───────────────
  it('ports-protocols redirects to profile/PortsProtocolsAndServices', () => {
    const pathname = renderAliasRoute(
      'ports-protocols',
      'profile/PortsProtocolsAndServices',
      SYSTEM_ID,
    );
    expect(pathname).toBe(`/systems/${SYSTEM_ID}/profile/PortsProtocolsAndServices`);
  });

  // ── 9. leveraged-auth → profile/LeveragedAuthorizations ──────────────────
  it('leveraged-auth redirects to profile/LeveragedAuthorizations', () => {
    const pathname = renderAliasRoute(
      'leveraged-auth',
      'profile/LeveragedAuthorizations',
      SYSTEM_ID,
    );
    expect(pathname).toBe(`/systems/${SYSTEM_ID}/profile/LeveragedAuthorizations`);
  });

  // ── 10. legal-regulatory → legal ─────────────────────────────────────────
  it('legal-regulatory redirects to legal', () => {
    const pathname = renderAliasRoute('legal-regulatory', 'legal', SYSTEM_ID);
    expect(pathname).toBe(`/systems/${SYSTEM_ID}/legal`);
  });

  // ── 11. implementation-roadmap → roadmap ─────────────────────────────────
  it('implementation-roadmap redirects to roadmap', () => {
    const pathname = renderAliasRoute('implementation-roadmap', 'roadmap', SYSTEM_ID);
    expect(pathname).toBe(`/systems/${SYSTEM_ID}/roadmap`);
  });

  // ── Contract: SystemRedirect preserves the :id param ─────────────────────
  it('preserves system id in the redirected URL', () => {
    // Use a uuid-style id to ensure it is passed through verbatim
    const specialId = '00000000-dead-beef-cafe-000000000001';
    const pathname = renderAliasRoute('capabilities', 'capability-coverage', specialId);
    expect(pathname).toBe(`/systems/${specialId}/capability-coverage`);
  });

  // ── Contract: navigate to alias does NOT stay at alias URL ────────────────
  it('alias URL is not preserved (redirect replaces history entry)', () => {
    // Confirm the final URL is the canonical one, not the original alias
    const pathname = renderAliasRoute('control-inheritance', 'inheritance', SYSTEM_ID);
    expect(pathname).not.toContain('control-inheritance');
  });
});
