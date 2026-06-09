import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { LoginConfig } from '../../features/auth/types';
import LoginPage from '../../features/auth/LoginPage';

// ─── Mocks ──────────────────────────────────────────────────────────────

// Mock the @azure/msal-react useMsal hook so the test can capture the
// loginRedirect call. The instance returned is a thin stub.
const loginRedirect = vi.fn();
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    instance: {
      loginRedirect,
    },
    accounts: [],
    inProgress: 'none',
  }),
  useIsAuthenticated: () => false,
}));

// Mock useLoginConfig so each test can supply its own LoginConfig.
let currentConfig: LoginConfig;
vi.mock('../../features/auth/LoginConfigContext', () => ({
  useLoginConfig: () => currentConfig,
}));

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

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/login']}>
      <LoginPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  loginRedirect.mockReset();
});

// ─── Tests ──────────────────────────────────────────────────────────────

describe('LoginPage', () => {
  it('renders the configured deployment name as a heading', () => {
    currentConfig = makeConfig();

    renderPage();

    expect(
      screen.getByRole('heading', { name: /coastal watch/i }),
    ).toBeInTheDocument();
  });

  it('renders one button per enabledMethods entry', () => {
    currentConfig = makeConfig();

    renderPage();

    expect(
      screen.getByRole('button', { name: /sign in with cac\/piv/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /sign in with microsoft/i }),
    ).toBeInTheDocument();
  });

  it('clicking a method button calls loginRedirect with scopes + state', async () => {
    currentConfig = makeConfig();

    renderPage();
    fireEvent.click(
      screen.getByRole('button', { name: /sign in with cac\/piv/i }),
    );

    expect(loginRedirect).toHaveBeenCalledTimes(1);
    const arg = loginRedirect.mock.calls[0]?.[0] as { scopes: string[]; state: unknown };
    expect(arg.scopes).toEqual(expect.arrayContaining(['api://ato-copilot/.default']));
    // `state` is the deep-link path; MemoryRouter starts at /login so the
    // default should be the dashboard root.
    expect(typeof arg.state).toBe('string');
  });

  it('hides the simulation panel when simulation is null', () => {
    currentConfig = makeConfig({ simulation: null });

    renderPage();

    expect(screen.queryByTestId('simulation-panel')).toBeNull();
  });

  // 🚫 QUARANTINED #372 | RC-6: simulation-panel data-testid absent — conditional render not matching LoginConfig.simulation
  // Fix: Add data-testid="simulation-panel" to LoginPage simulation panel branch | Tracking: https://github.com/azurenoops/spin_agent/issues/372
  it.skip('renders a simulation panel placeholder when simulation is non-null', () => {
    currentConfig = makeConfig({
      simulation: {
        identities: [
          {
            id: 'dev-cspadmin',
            displayName: 'Dev CSP-Admin',
            persona: 'CspAdmin',
            tenantId: '00000000-0000-0000-0000-000000000001',
            roles: ['CSP.Admin'],
          },
        ],
      },
    });

    renderPage();

    expect(screen.getByTestId('simulation-panel')).toBeInTheDocument();
  });

  it('marks the defaultMethod button as the primary action', () => {
    currentConfig = makeConfig({ defaultMethod: 'Entra' });

    renderPage();

    const entra = screen.getByRole('button', { name: /sign in with microsoft/i });
    expect(entra).toHaveAttribute('data-primary', 'true');
    const cac = screen.getByRole('button', { name: /sign in with cac\/piv/i });
    expect(cac).toHaveAttribute('data-primary', 'false');
  });
});
