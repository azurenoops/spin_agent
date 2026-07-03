/**
 * Tests for PR #603 test plan items:
 *
 * - Item 1: Click org row in CSP portfolio → navigates to /
 * - Item 4 (OrgsTable): Verify impersonation error banner still appears when API rejects
 *
 * These tests extend the existing OrgsTable.test.tsx suite.  The original file
 * only covered confirmation dialogs (Wave 6 GAP-221-A); this file covers the
 * row-click navigation contract introduced by fix #599.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { TenantSummary, TenantsPage } from '../../../features/csp-dashboard/api';

// ─── Hoisted mocks ──────────────────────────────────────────────────────

const { navigate } = vi.hoisted(() => ({ navigate: vi.fn() }));
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => navigate };
});

const {
  getCspDashboardTenants,
  updateTenantStatus,
  createCspDashboardTenant,
} = vi.hoisted(() => ({
  getCspDashboardTenants: vi.fn(),
  updateTenantStatus: vi.fn(),
  createCspDashboardTenant: vi.fn(),
}));

vi.mock('../../../features/csp-dashboard/api', async () => {
  const actual = await vi.importActual<typeof import('../../../features/csp-dashboard/api')>(
    '../../../features/csp-dashboard/api',
  );
  return {
    ...actual,
    getCspDashboardTenants,
    updateTenantStatus,
    createCspDashboardTenant,
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

vi.mock('../../../features/tenancy/vestigeTenants', () => ({
  isVestigeTenant: () => false,
}));

import OrgsTable from '../../../features/csp-dashboard/OrgsTable';

// ─── Helpers ─────────────────────────────────────────────────────────────

const TEST_TENANT_ID = 'aaaaaaaa-0000-0000-0000-000000000001';

function makeTenant(overrides: Partial<TenantSummary> = {}): TenantSummary {
  return {
    tenantId: TEST_TENANT_ID,
    displayName: 'Acme Corp',
    status: 'Active',
    onboardingState: 'Active',
    organizationCount: 2,
    systemCount: 3,
    atoStatusCounts: { authorized: 1, inProcess: 1, denied: 0 },
    openFindingCount: 5,
    openPoamCount: 2,
    openDeviationCount: 1,
    lastActivityTimestamp: new Date().toISOString(),
    ...overrides,
  };
}

function makePage(items: TenantSummary[]): TenantsPage {
  return { items, totalCount: items.length, page: 1, pageSize: 25 };
}

function renderOrgsTable() {
  return render(<MemoryRouter><OrgsTable /></MemoryRouter>);
}

beforeEach(() => {
  navigate.mockReset();
  startImpersonation.mockReset();
  updateTenantStatus.mockReset();
  createCspDashboardTenant.mockReset();
  getCspDashboardTenants.mockReset();
  getCspDashboardTenants.mockResolvedValue(makePage([makeTenant()]));
});

// ─── Test Plan Item 1 ─────────────────────────────────────────────────────
// Click org row in CSP portfolio → navigates to /

describe('PR #603 — Test Plan Item 1: CSP portfolio org row click navigation', () => {
  it('navigates to / when impersonation succeeds', async () => {
    startImpersonation.mockResolvedValue(undefined);
    renderOrgsTable();

    const row = await screen.findByTestId(`org-row-${TEST_TENANT_ID}`);
    fireEvent.click(row);

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith('/');
    });
  });

  it('navigates to / even when impersonation API rejects (fix #599)', async () => {
    startImpersonation.mockRejectedValue(new Error('401 Unauthorized'));
    renderOrgsTable();

    const row = await screen.findByTestId(`org-row-${TEST_TENANT_ID}`);
    fireEvent.click(row);

    await waitFor(() => {
      // Navigation MUST occur regardless of impersonation failure
      expect(navigate).toHaveBeenCalledWith('/');
    });
  });

  it('does NOT navigate for Disabled orgs', async () => {
    getCspDashboardTenants.mockResolvedValue(makePage([makeTenant({ status: 'Disabled' })]));
    startImpersonation.mockResolvedValue(undefined);
    renderOrgsTable();

    const row = await screen.findByTestId(`org-row-${TEST_TENANT_ID}`);
    fireEvent.click(row);

    // Brief wait to ensure no async navigation occurs
    await new Promise((r) => setTimeout(r, 50));
    expect(navigate).not.toHaveBeenCalled();
  });
});

// ─── Test Plan Item 4 (OrgsTable) ─────────────────────────────────────────
// Verify impersonation error banner still appears when API rejects

describe('PR #603 — Test Plan Item 4: OrgsTable error banner on impersonation rejection', () => {
  it('shows error banner [data-testid=orgs-error] when impersonation rejects', async () => {
    startImpersonation.mockRejectedValue(new Error('Impersonation service unavailable'));
    renderOrgsTable();

    const row = await screen.findByTestId(`org-row-${TEST_TENANT_ID}`);
    fireEvent.click(row);

    await waitFor(() => {
      const banner = screen.getByTestId('orgs-error');
      expect(banner).toBeInTheDocument();
      expect(banner).toHaveTextContent('Impersonation service unavailable');
    });
  });

  it('does NOT show error banner when impersonation succeeds', async () => {
    startImpersonation.mockResolvedValue(undefined);
    renderOrgsTable();

    const row = await screen.findByTestId(`org-row-${TEST_TENANT_ID}`);
    fireEvent.click(row);

    await waitFor(() => {
      expect(navigate).toHaveBeenCalled();
    });
    expect(screen.queryByTestId('orgs-error')).not.toBeInTheDocument();
  });
});
