/**
 * Tests for PR #603 test plan items:
 *
 * - Item 2: Click system row in /systems CSP view → navigates to /systems/{id}
 * - Item 4: Verify impersonation error banner still appears when API rejects
 *
 * Items 1 and 3 are covered in OrgsTable.test.tsx and SystemSummaryRow.test.tsx
 * respectively (see audit comment on PR #603).
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { SystemRow, SystemsPage } from '../../../features/csp-dashboard/api';

// ─── Hoisted mocks ──────────────────────────────────────────────────────

// PageLayout depends on ChatPanelProvider which requires full app context.
// Mock it to a passthrough so we can test CspSystemsPage in isolation.
vi.mock('../../../components/layout/PageLayout', () => ({
  default: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

// PageHero is a simple presentational component — stub it out too.
vi.mock('../../../components/layout/PageHero', () => ({
  default: () => null,
}));

const { navigate } = vi.hoisted(() => ({ navigate: vi.fn() }));
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => navigate };
});

const { getCspDashboardSystems } = vi.hoisted(() => ({
  getCspDashboardSystems: vi.fn(),
}));
vi.mock('../../../features/csp-dashboard/api', async () => {
  const actual = await vi.importActual<typeof import('../../../features/csp-dashboard/api')>(
    '../../../features/csp-dashboard/api',
  );
  return {
    ...actual,
    getCspDashboardSystems,
    isUnavailable: (r: unknown) => r === null,
  };
});

const { startImpersonation } = vi.hoisted(() => ({ startImpersonation: vi.fn() }));
vi.mock('../../../features/tenancy/api', async () => {
  const actual = await vi.importActual<typeof import('../../../features/tenancy/api')>(
    '../../../features/tenancy/api',
  );
  return { ...actual, startImpersonation };
});

import CspSystemsPage from '../../../features/csp-dashboard/CspSystemsPage';

// ─── Helpers ─────────────────────────────────────────────────────────────

const SYSTEM_ID = 'sys-00000000-0000-0000-0000-000000000001';
const TENANT_ID = 'ten-00000000-0000-0000-0000-000000000001';

function makeSystem(overrides: Partial<SystemRow> = {}): SystemRow {
  return {
    systemId: SYSTEM_ID,
    name: 'Test System Alpha',
    acronym: 'TSA',
    tenantId: TENANT_ID,
    orgDisplayName: 'Acme Corp',
    impactLevel: 'Moderate',
    currentRmfPhase: 'Assess',
    complianceScore: 87.3,
    atoExpirationDate: null,
    atoStatus: 'None',
    atoDaysRemaining: null,
    atoSeverity: 'none',
    openPoamCount: 2,
    overduePoamCount: 0,
    ...overrides,
  };
}

function makePage(items: SystemRow[]): SystemsPage {
  return { items, totalCount: items.length, page: 1, pageSize: 50 };
}

function renderCspSystemsPage() {
  return render(
    <MemoryRouter>
      <CspSystemsPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  navigate.mockReset();
  startImpersonation.mockReset();
  getCspDashboardSystems.mockReset();
  getCspDashboardSystems.mockResolvedValue(makePage([makeSystem()]));
});

// ─── Test Plan Item 2 ─────────────────────────────────────────────────────
// Click system row in /systems CSP view → navigates to /systems/{id}

describe('PR #603 — Test Plan Item 2: CSP /systems row click navigation', () => {
  it('navigates to /systems/{systemId} when impersonation succeeds', async () => {
    startImpersonation.mockResolvedValue(undefined);
    renderCspSystemsPage();

    const row = await screen.findByTestId(`csp-system-row-${SYSTEM_ID}`);
    fireEvent.click(row);

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith(`/systems/${encodeURIComponent(SYSTEM_ID)}`);
    });
  });

  it('navigates to /systems/{systemId} even when impersonation API rejects (fix #595/#599)', async () => {
    startImpersonation.mockRejectedValue(new Error('403 Forbidden'));
    renderCspSystemsPage();

    const row = await screen.findByTestId(`csp-system-row-${SYSTEM_ID}`);
    fireEvent.click(row);

    await waitFor(() => {
      // Navigation MUST occur regardless of impersonation failure
      expect(navigate).toHaveBeenCalledWith(`/systems/${encodeURIComponent(SYSTEM_ID)}`);
    });
  });
});

// ─── Test Plan Item 4 ─────────────────────────────────────────────────────
// Verify impersonation error banner still appears when API rejects

describe('PR #603 — Test Plan Item 4: error banner on impersonation rejection', () => {
  it('shows error banner when impersonation API returns an error', async () => {
    startImpersonation.mockRejectedValue(new Error('Impersonation denied by server'));
    renderCspSystemsPage();

    const row = await screen.findByTestId(`csp-system-row-${SYSTEM_ID}`);
    fireEvent.click(row);

    await waitFor(() => {
      const banner = screen.getByTestId('csp-systems-error');
      expect(banner).toBeInTheDocument();
      expect(banner).toHaveTextContent('Impersonation denied by server');
    });
  });

  it('does NOT show error banner when impersonation succeeds', async () => {
    startImpersonation.mockResolvedValue(undefined);
    renderCspSystemsPage();

    const row = await screen.findByTestId(`csp-system-row-${SYSTEM_ID}`);
    fireEvent.click(row);

    await waitFor(() => {
      expect(navigate).toHaveBeenCalled();
    });
    expect(screen.queryByTestId('csp-systems-error')).not.toBeInTheDocument();
  });
});
