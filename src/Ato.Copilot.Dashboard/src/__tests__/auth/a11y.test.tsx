import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, act, fireEvent, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
// vitest-axe@0.1.0 ships a broken `extend-expect` entry (the dist file
// is empty). Wire the matcher manually from its `matchers` entry below.
import { axe } from 'vitest-axe';
import { toHaveNoViolations } from 'vitest-axe/matchers';
import type { LoginConfig, MeResponse } from '../../features/auth/types';

expect.extend({ toHaveNoViolations });

// ─── Hoisted mocks shared across every component-under-test ─────────────

const { loginRedirect, logoutRedirect, postMock, getMock, navigate, purgeMock } =
  vi.hoisted(() => ({
    loginRedirect: vi.fn(),
    logoutRedirect: vi.fn(),
    postMock: vi.fn(),
    getMock: vi.fn(),
    navigate: vi.fn(),
    purgeMock: vi.fn(),
  }));

vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    instance: { loginRedirect, logoutRedirect },
    accounts: [],
    inProgress: 'none',
  }),
  useIsAuthenticated: () => false,
}));

vi.mock('axios', () => {
  // ImpersonationBanner imports `endImpersonation` from
  // features/tenancy/api which calls `axios.create(...)` then registers
  // request/response interceptors at module load. The stub must
  // therefore expose a `create` factory that returns a client mirroring
  // the surface the tenancy client touches (interceptors.request.use /
  // interceptors.response.use). The interceptor registrations are no-ops
  // so the stub call chains succeed.
  const client = {
    post: postMock,
    get: getMock,
    interceptors: {
      request: { use: vi.fn() },
      response: { use: vi.fn() },
    },
  };
  const stub = {
    post: postMock,
    get: getMock,
    create: () => client,
  };
  return { default: stub, post: postMock, get: getMock, create: stub.create };
});

vi.mock('../../features/auth/msalInstance', () => ({
  getMsalInstance: () => ({ logoutRedirect }),
  // features/tenancy/api.ts imports DEFAULT_API_SCOPES — stub it here so
  // the module loads cleanly during the a11y test bootstrap.
  DEFAULT_API_SCOPES: ['api://ato/.default'],
}));

vi.mock('../../features/auth/useIdleFormStateBackup', () => ({
  purgeUnsavedChanges: purgeMock,
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>(
    'react-router-dom',
  );
  return {
    ...actual,
    useNavigate: () => navigate,
  };
});

// LoginConfig stub — re-assignable per test (default uses the
// makeConfig() helper below).
let currentConfig: LoginConfig;
vi.mock('../../features/auth/LoginConfigContext', () => ({
  useLoginConfig: () => currentConfig,
}));

// /me stub — re-assignable per test.
let currentMe: MeResponse | null = null;
vi.mock('../../features/auth/useMe', () => ({
  useMe: () => ({
    data: currentMe,
    isLoading: false,
    error: null,
    refetch: vi.fn(),
  }),
}));

// End-impersonation API stub used by ImpersonationBanner.
vi.mock('../../features/tenancy/api', async () => {
  const actual = await vi.importActual<typeof import('../../features/tenancy/api')>(
    '../../features/tenancy/api',
  );
  return { ...actual, endImpersonation: vi.fn() };
});

// ─── Imports under test — AFTER mocks so vi.mock hoists correctly ───────

import LoginPage from '../../features/auth/LoginPage';
import TenantPickerPage from '../../features/auth/TenantPickerPage';
import AccountMenu from '../../features/auth/AccountMenu';
import ImpersonationBanner from '../../features/auth/ImpersonationBanner';
import LoginErrorPage from '../../features/auth/LoginErrorPage';
import IdleWarningModal from '../../features/auth/IdleWarningModal';
import RestoreUnsavedChangesPrompt from '../../features/auth/RestoreUnsavedChangesPrompt';

// ─── Fixtures ───────────────────────────────────────────────────────────

function makeConfig(overrides: Partial<LoginConfig> = {}): LoginConfig {
  return {
    branding: {
      deploymentName: 'Coastal Watch — ATO Copilot',
      logoUrl: null,
      supportEmail: 'support@coastal-watch.gov',
    },
    defaultMethod: 'Cac',
    enabledMethods: [
      { id: 'Cac', displayName: 'Sign in with CAC/PIV' },
      { id: 'Entra', displayName: 'Sign in with Microsoft' },
    ],
    cloud: 'AzureUSGovernment',
    idleTimeoutMinutes: 30,
    rememberTenantCookieDays: 30,
    simulation: null,
    msal: {
      clientId: 'cid',
      authority: 'https://login.microsoftonline.us/tid',
      redirectUri: 'http://localhost/login/callback',
      postLogoutRedirectUri: 'http://localhost/login',
    },
    ...overrides,
  };
}

function makeMe(overrides: Partial<MeResponse> = {}): MeResponse {
  const home = {
    id: '11111111-1111-1111-1111-111111111111',
    displayName: 'Acme Home',
    status: 'Active' as const,
  };
  const target = {
    id: '22222222-2222-2222-2222-222222222222',
    displayName: 'Coastal Watch',
    status: 'Active' as const,
  };
  return {
    oid: 'oid-jane',
    displayName: 'Jane Doe',
    persona: 'SystemOwner',
    homeTenant: home,
    effectiveTenant: home,
    isImpersonating: false,
    impersonation: null,
    pimRoles: [],
    isCspAdmin: false,
    isSocAnalyst: false,
    tenantMemberships: [home, target],
    ...overrides,
  };
}

beforeEach(() => {
  currentConfig = makeConfig();
  currentMe = makeMe();
  loginRedirect.mockReset();
  logoutRedirect.mockReset();
  postMock.mockReset();
  postMock.mockResolvedValue({ status: 204, data: { status: 'success' } });
  getMock.mockReset();
  getMock.mockResolvedValue({ status: 200, data: { status: 'success', data: {} } });
  navigate.mockReset();
  purgeMock.mockReset();
  localStorage.clear();
  sessionStorage.clear();
});

afterEach(() => {
  localStorage.clear();
  sessionStorage.clear();
});

// ─── The axe runtime config (WCAG 2.1 AA) ───────────────────────────────

/**
 * Per Feature 051 FR-039 + analysis C7: assert zero violations of every
 * WCAG 2 / 2.1 level-A and AA rule plus the ARIA + colour-contrast
 * category buckets. The rule selectors below are axe-core's standard
 * tag names.
 */
const AXE_OPTIONS = {
  runOnly: {
    type: 'tag' as const,
    values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'cat.aria', 'cat.color'],
  },
};

// ─── The eight surfaces — one a11y assertion per surface ────────────────

describe('Feature 051 — WCAG 2.1 AA accessibility (FR-039 / T144a)', () => {
  it('LoginPage has no axe violations', async () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/login']}>
        <LoginPage />
      </MemoryRouter>,
    );

    const results = await axe(container, AXE_OPTIONS);
    expect(results).toHaveNoViolations();
  });

  it('TenantPickerPage has no axe violations (multi-tenant user)', async () => {
    currentMe = makeMe(); // Two memberships → picker renders the choices.

    const { container } = render(
      <MemoryRouter initialEntries={['/select-tenant']}>
        <TenantPickerPage />
      </MemoryRouter>,
    );

    const results = await axe(container, AXE_OPTIONS);
    expect(results).toHaveNoViolations();
  });

  it('AccountMenu (open state) has no axe violations', async () => {
    currentMe = makeMe({
      pimRoles: [
        {
          name: 'ISSO',
          expiresAt: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
        },
      ],
    });

    const { container } = render(
      <MemoryRouter>
        <AccountMenu oid="oid-jane" displayName="Jane Doe" />
      </MemoryRouter>,
    );

    // Open the menu so axe sees the expanded surface.
    fireEvent.click(screen.getByRole('button', { name: /account menu/i }));

    const results = await axe(container, AXE_OPTIONS);
    expect(results).toHaveNoViolations();
  });

  it('ImpersonationBanner has no axe violations (CSP-Admin impersonating)', async () => {
    const target = {
      id: '22222222-2222-2222-2222-222222222222',
      displayName: 'Coastal Watch',
      status: 'Active' as const,
    };
    currentMe = makeMe({
      isCspAdmin: true,
      isImpersonating: true,
      effectiveTenant: target,
      impersonation: {
        impersonatedTenant: target,
        startedAt: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
        expiresAt: new Date(Date.now() + 55 * 60 * 1000).toISOString(),
      },
    });

    const { container } = render(
      <MemoryRouter>
        <ImpersonationBanner />
      </MemoryRouter>,
    );

    const results = await axe(container, AXE_OPTIONS);
    expect(results).toHaveNoViolations();
  });

  it('LoginErrorPage (ClockSkew + correlationId) has no axe violations', async () => {
    const { container } = render(
      <MemoryRouter
        initialEntries={['/login/error?errorClass=ClockSkew&correlationId=abc-123']}
      >
        <LoginErrorPage />
      </MemoryRouter>,
    );

    const results = await axe(container, AXE_OPTIONS);
    expect(results).toHaveNoViolations();
  });

  it('IdleWarningModal (alertdialog open) has no axe violations', async () => {
    const { container } = render(<IdleWarningModal />);

    act(() => {
      window.dispatchEvent(
        new CustomEvent('ato:idle-warning', { detail: { secondsUntilSignOut: 60 } }),
      );
    });

    const results = await axe(container, AXE_OPTIONS);
    expect(results).toHaveNoViolations();
  });

  it('RestoreUnsavedChangesPrompt (visible snapshot) has no axe violations', async () => {
    localStorage.setItem(
      'ato.unsavedChanges.user-a.intake-step-2',
      JSON.stringify({
        savedAt: '2026-05-28T12:00:00Z',
        data: { title: 'Draft' },
      }),
    );

    const { container } = render(<RestoreUnsavedChangesPrompt oid="user-a" />);

    const results = await axe(container, AXE_OPTIONS);
    expect(results).toHaveNoViolations();
  });
});
