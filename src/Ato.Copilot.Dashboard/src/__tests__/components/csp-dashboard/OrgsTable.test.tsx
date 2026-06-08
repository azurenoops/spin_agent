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

// ─── Helpers ────────────────────────────────────────────────────────────

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

// ─── Tests ──────────────────────────────────────────────────────────────

describe('OrgsTable — confirmation dialogs (Wave 6 GAP-221-A)', () => {
  // ── Button visibility by status ────────────────────────────────────────

  it('renders Suspend button for Active org', async () => {
    getCspDashboardTenants.mockResolvedValue(makePage([makeTenant({ status: 'Active' })]));
    renderOrgsTable();
    await waitFor(() => {
      expect(screen.getByTestId(`org-suspend-${TEST_TENANT_ID}`)).toBeInTheDocument();
    });
  });

  it('renders Disable button for Active org', async () => {
    getCspDashboardTenants.mockResolvedValue(makePage([makeTenant({ status: 'Active' })]));
    renderOrgsTable();
    await waitFor(() => {
      expect(screen.getByTestId(`org-disable-${TEST_TENANT_ID}`)).toBeInTheDocument();
    });
  });

  it('renders Reinstate but NOT Suspend for Suspended org', async () => {
    getCspDashboardTenants.mockResolvedValue(makePage([makeTenant({ status: 'Suspended' })]));
    renderOrgsTable();
    await waitFor(() => {
      expect(screen.getByTestId(`org-reinstate-${TEST_TENANT_ID}`)).toBeInTheDocument();
      expect(screen.queryByTestId(`org-suspend-${TEST_TENANT_ID}`)).not.toBeInTheDocument();
    });
  });

  it('renders Reinstate for Disabled org', async () => {
    getCspDashboardTenants.mockResolvedValue(makePage([makeTenant({ status: 'Disabled' })]));
    renderOrgsTable();
    await waitFor(() => {
      expect(screen.getByTestId(`org-reinstate-${TEST_TENANT_ID}`)).toBeInTheDocument();
    });
  });

  // ── Dialog appearance ─────────────────────────────────────────────────

  it('clicking Suspend opens a dialog with Suspend heading', async () => {
    getCspDashboardTenants.mockResolvedValue(makePage([makeTenant({ status: 'Active' })]));
    renderOrgsTable();
    await waitFor(() => {
      expect(screen.getByTestId(`org-suspend-${TEST_TENANT_ID}`)).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId(`org-suspend-${TEST_TENANT_ID}`));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByRole('dialog').textContent).toMatch(/suspend/i);
    expect(updateTenantStatus).not.toHaveBeenCalled();
  });

  it('clicking Disable opens a dialog with Disable heading', async () => {
    getCspDashboardTenants.mockResolvedValue(makePage([makeTenant({ status: 'Active' })]));
    renderOrgsTable();
    await waitFor(() => {
      expect(screen.getByTestId(`org-disable-${TEST_TENANT_ID}`)).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId(`org-disable-${TEST_TENANT_ID}`));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByRole('dialog').textContent).toMatch(/disable/i);
    expect(updateTenantStatus).not.toHaveBeenCalled();
  });

  it('clicking Reinstate opens a dialog with Reinstate heading', async () => {
    getCspDashboardTenants.mockResolvedValue(makePage([makeTenant({ status: 'Suspended' })]));
    renderOrgsTable();
    await waitFor(() => {
      expect(screen.getByTestId(`org-reinstate-${TEST_TENANT_ID}`)).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId(`org-reinstate-${TEST_TENANT_ID}`));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByRole('dialog').textContent).toMatch(/reinstate/i);
    expect(updateTenantStatus).not.toHaveBeenCalled();
  });

  // ── Cancel behavior ───────────────────────────────────────────────────

  it('Cancel closes the dialog without calling updateTenantStatus', async () => {
    getCspDashboardTenants.mockResolvedValue(makePage([makeTenant({ status: 'Active' })]));
    renderOrgsTable();
    await waitFor(() => {
      expect(screen.getByTestId(`org-suspend-${TEST_TENANT_ID}`)).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId(`org-suspend-${TEST_TENANT_ID}`));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /cancel/i }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(updateTenantStatus).not.toHaveBeenCalled();
  });

  // ── Confirm behavior ──────────────────────────────────────────────────

  it('status-action-confirm calls updateTenantStatus with correct arguments', async () => {
    getCspDashboardTenants.mockResolvedValue(makePage([makeTenant({ status: 'Active' })]));
    updateTenantStatus.mockResolvedValue(undefined);
    getCspDashboardTenants
      .mockResolvedValueOnce(makePage([makeTenant({ status: 'Active' })]))
      .mockResolvedValue(makePage([makeTenant({ status: 'Suspended' })]));

    renderOrgsTable();
    await waitFor(() => {
      expect(screen.getByTestId(`org-suspend-${TEST_TENANT_ID}`)).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId(`org-suspend-${TEST_TENANT_ID}`));
    fireEvent.click(screen.getByTestId('status-action-confirm'));

    await waitFor(() => {
      expect(updateTenantStatus).toHaveBeenCalledWith(TEST_TENANT_ID, 'Suspended', undefined);
    });
  });

  // ── Dialog copy ───────────────────────────────────────────────────────

  it('confirmation dialog shows the org display name', async () => {
    getCspDashboardTenants.mockResolvedValue(
      makePage([makeTenant({ status: 'Active', displayName: 'Pentagon Logistics' })]),
    );
    renderOrgsTable();
    await waitFor(() => {
      expect(screen.getByTestId(`org-suspend-${TEST_TENANT_ID}`)).toBeInTheDocument();
    });
    fireEvent.click(screen.getByTestId(`org-suspend-${TEST_TENANT_ID}`));
    expect(screen.getByRole('dialog').textContent).toContain('Pentagon Logistics');
  });
});
