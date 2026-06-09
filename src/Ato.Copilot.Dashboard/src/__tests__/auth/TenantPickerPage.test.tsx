import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { MeResponse, TenantSummary } from '../../features/auth/types';

// ─── Mocks ──────────────────────────────────────────────────────────────

// Hoist spies so the vi.mock factory can capture them safely.
const { post, get } = vi.hoisted(() => ({
  post: vi.fn(),
  get: vi.fn(),
}));

vi.mock('axios', () => {
  const stub = { post, get };
  return {
    default: stub,
    post,
    get,
  };
});

const { navigate } = vi.hoisted(() => ({ navigate: vi.fn() }));
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: () => navigate,
  };
});

// Default `useMe` mock — each test re-assigns `currentMe`.
let currentMe: MeResponse;
vi.mock('../../features/auth/useMe', () => ({
  useMe: () => ({ data: currentMe, isLoading: false, error: null }),
}));

import TenantPickerPage from '../../features/auth/TenantPickerPage';

function tenant(id: string, displayName: string, status: TenantSummary['status']): TenantSummary {
  return { id, displayName, status };
}

function baseMe(overrides: Partial<MeResponse> = {}): MeResponse {
  const home = tenant('11111111-1111-1111-1111-111111111111', 'Coastal Watch', 'Active');
  return {
    oid: 'oid-1',
    displayName: 'Jane',
    persona: 'SystemOwner',
    homeTenant: home,
    effectiveTenant: home,
    isImpersonating: false,
    impersonation: null,
    pimRoles: [],
    isCspAdmin: false,
    isSocAnalyst: false,
    tenantMemberships: [home],
    ...overrides,
  };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <TenantPickerPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  post.mockReset();
  get.mockReset();
  navigate.mockReset();
});

// ─── Tests ──────────────────────────────────────────────────────────────

describe('TenantPickerPage', () => {
  it('renders one row per tenant with status badge', () => {
    currentMe = baseMe({
      isCspAdmin: true,
      tenantMemberships: [
        tenant('a', 'Active Org', 'Active'),
        tenant('s', 'Suspended Org', 'Suspended'),
      ],
    });

    renderPage();

    expect(screen.getByText('Active Org')).toBeInTheDocument();
    expect(screen.getByText('Suspended Org')).toBeInTheDocument();
    // Status badges visible in the row markup.
    expect(screen.getAllByText(/active/i).length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText(/suspended/i).length).toBeGreaterThanOrEqual(1);
  });

  it('hides Disabled rows for non-CSP-Admin users', () => {
    currentMe = baseMe({
      isCspAdmin: false,
      tenantMemberships: [
        tenant('a', 'Active Org', 'Active'),
        tenant('d', 'Disabled Org', 'Disabled'),
      ],
    });

    renderPage();

    expect(screen.getByText('Active Org')).toBeInTheDocument();
    expect(screen.queryByText('Disabled Org')).not.toBeInTheDocument();
  });

  it('renders Disabled rows grayed-out (disabled clickability) for CSP-Admin', () => {
    currentMe = baseMe({
      isCspAdmin: true,
      tenantMemberships: [
        tenant('a', 'Active Org', 'Active'),
        tenant('d', 'Disabled Org', 'Disabled'),
      ],
    });

    renderPage();

    // Disabled row is rendered but its button is disabled.
    const disabledBtn = screen.getByRole('button', { name: /disabled org/i });
    expect(disabledBtn).toBeDisabled();
  });

  it('renders the "Remember on this device" checkbox below the list', () => {
    currentMe = baseMe({
      tenantMemberships: [tenant('a', 'Active Org', 'Active')],
    });

    renderPage();

    expect(
      screen.getByRole('checkbox', { name: /remember.*device/i }),
    ).toBeInTheDocument();
  });

  it('POSTs /api/auth/select-tenant on row click with tenantId + remember=false by default', async () => {
    currentMe = baseMe({
      tenantMemberships: [tenant('tenant-a', 'Active Org', 'Active')],
    });
    post.mockResolvedValueOnce({ status: 204 });

    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /active org/i }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(post).toHaveBeenCalledWith(
      '/api/auth/select-tenant',
      { tenantId: 'tenant-a', remember: false },
    );
  });

  it('POSTs with remember=true when the checkbox is checked', async () => {
    currentMe = baseMe({
      tenantMemberships: [tenant('tenant-a', 'Active Org', 'Active')],
    });
    post.mockResolvedValueOnce({ status: 204 });

    renderPage();

    fireEvent.click(screen.getByRole('checkbox', { name: /remember.*device/i }));
    fireEvent.click(screen.getByRole('button', { name: /active org/i }));

    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    expect(post).toHaveBeenCalledWith(
      '/api/auth/select-tenant',
      { tenantId: 'tenant-a', remember: true },
    );
  });

  it('CSP-Admin sees an "All Tenants (CSP view)" row that navigates to /csp/dashboard WITHOUT calling /select-tenant (FR-011)', async () => {
    currentMe = baseMe({
      isCspAdmin: true,
      tenantMemberships: [tenant('a', 'Active Org', 'Active')],
    });

    renderPage();

    const cspViewBtn = screen.getByRole('button', { name: /all tenants.*csp view/i });
    fireEvent.click(cspViewBtn);

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith('/', expect.anything()); // fix #361;
    });
    expect(post).not.toHaveBeenCalled();
  });

  it('non-CSP-Admin does NOT see the "All Tenants (CSP view)" row', () => {
    currentMe = baseMe({
      isCspAdmin: false,
      tenantMemberships: [tenant('a', 'Active Org', 'Active')],
    });

    renderPage();

    expect(screen.queryByText(/all tenants.*csp view/i)).not.toBeInTheDocument();
  });

  it('navigates to "/" on successful POST', async () => {
    currentMe = baseMe({
      tenantMemberships: [tenant('a', 'Active Org', 'Active')],
    });
    post.mockResolvedValueOnce({ status: 204 });

    renderPage();

    fireEvent.click(screen.getByRole('button', { name: /active org/i }));

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith('/', expect.anything());
    });
  });

  it('regression #361: CSP-Admin All Tenants does NOT navigate to /csp/dashboard (retired route)', async () => {
    currentMe = baseMe({ isCspAdmin: true, tenantMemberships: [tenant('a', 'Active Org', 'Active')] });
    renderPage();
    fireEvent.click(screen.getByRole('button', { name: /all tenants.*csp view/i }));
    await waitFor(() => expect(navigate).toHaveBeenCalled());
    const targets = (navigate.mock.calls as Array<[string, ...unknown[]]>).map(([p]) => p);
    expect(targets).not.toContain('/csp/dashboard');
  });
});
