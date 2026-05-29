import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type {
  LoginConfig,
  SimulationPanelDescriptor,
} from '../../features/auth/types';

// ─── Mocks ──────────────────────────────────────────────────────────────

// Hoist axios spies so the vi.mock factory can capture them safely.
const { post } = vi.hoisted(() => ({ post: vi.fn() }));

vi.mock('axios', () => {
  const stub = { post };
  return { default: stub, post };
});

// Spy on window.location.assign so we can assert the success path navigates
// to the dashboard root (RequireAuth then probes /me, sees the simulation
// cookies, and promotes the user to authenticated). assign — not reload —
// because the success flow must LEAVE /login, not re-bootstrap it.
const { assign } = vi.hoisted(() => ({ assign: vi.fn() }));
Object.defineProperty(window, 'location', {
  configurable: true,
  value: { ...window.location, assign },
});

// Re-assignable LoginConfig — each test sets the simulation descriptor.
let currentConfig: LoginConfig;
vi.mock('../../features/auth/LoginConfigContext', () => ({
  useLoginConfig: () => currentConfig,
}));

import SimulationPanel from '../../features/auth/SimulationPanel';

function makeConfig(simulation: SimulationPanelDescriptor | null): LoginConfig {
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
    simulation,
    msal: {
      clientId: 'cid',
      authority: 'https://login.microsoftonline.us/tid',
      redirectUri: 'http://localhost/login/callback',
      postLogoutRedirectUri: 'http://localhost/login',
    },
  };
}

const SAMPLE_SIM: SimulationPanelDescriptor = {
  identities: [
    {
      id: 'dev-cspadmin',
      displayName: 'Dev CSP-Admin',
      persona: 'CspAdmin',
      tenantId: '00000000-0000-0000-0000-000000000001',
      roles: ['CSP.Admin'],
    },
    {
      id: 'dev-isso',
      displayName: 'Dev ISSO',
      persona: 'ISSO',
      tenantId: '00000000-0000-0000-0000-000000000001',
      roles: ['ISSO'],
    },
  ],
};

beforeEach(() => {
  post.mockReset();
  assign.mockReset();
});

describe('SimulationPanel', () => {
  // ─── Layer 2 of the 3-layer simulation gate ────────────────────────
  // Per research.md § R-Summary item 4, the SPA route guard MUST refuse
  // to mount the panel when `useLoginConfig().simulation` is null — even
  // if a malicious payload tries to force-mount via props. This is
  // defense-in-depth on top of the server-side Layer 1 (login-config
  // omits the descriptor in non-Development).

  it('renders nothing when simulation descriptor is null', () => {
    // Arrange
    currentConfig = makeConfig(null);

    // Act
    const { container } = render(<SimulationPanel />);

    // Assert
    expect(container.firstChild).toBeNull();
    expect(screen.queryByTestId('simulation-panel')).toBeNull();
  });

  it('renders nothing even when force=true is passed AND simulation is null', () => {
    // Arrange — defense-in-depth: the production caller never passes
    // `force`; the prop exists only so unit tests can prove the guard
    // cannot be bypassed.
    currentConfig = makeConfig(null);

    // Act
    const { container } = render(<SimulationPanel force />);

    // Assert — the `force` prop MUST NOT bypass the route guard.
    expect(container.firstChild).toBeNull();
    expect(screen.queryByTestId('simulation-panel')).toBeNull();
  });

  // ─── Render path ───────────────────────────────────────────────────

  it('renders one button per identity when simulation descriptor is populated', () => {
    // Arrange
    currentConfig = makeConfig(SAMPLE_SIM);

    // Act
    render(<SimulationPanel />);

    // Assert
    expect(screen.getByTestId('simulation-panel')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /dev csp-admin/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /dev isso/i })).toBeInTheDocument();
  });

  it('clicking an identity POSTs to /api/auth/simulate with the identity id', async () => {
    // Arrange
    currentConfig = makeConfig(SAMPLE_SIM);
    post.mockResolvedValueOnce({ status: 204 });

    // Act
    render(<SimulationPanel />);
    fireEvent.click(screen.getByRole('button', { name: /dev csp-admin/i }));

    // Assert — POST URL carries identityId; body intentionally null per § 5.2.
    await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
    const [url] = post.mock.calls[0]!;
    expect(url).toContain('/api/auth/simulate');
    expect(url).toContain('identityId=dev-cspadmin');
  });

  it('on 204 success navigates to dashboard root so RequireAuth picks up the simulation session', async () => {
    // Arrange
    currentConfig = makeConfig(SAMPLE_SIM);
    post.mockResolvedValueOnce({ status: 204 });

    // Act
    render(<SimulationPanel />);
    fireEvent.click(screen.getByRole('button', { name: /dev isso/i }));

    // Assert
    await waitFor(() => expect(assign).toHaveBeenCalledTimes(1));
    expect(assign).toHaveBeenCalledWith('/');
  });

  it('on network failure surfaces an error message and does not navigate', async () => {
    // Arrange
    currentConfig = makeConfig(SAMPLE_SIM);
    post.mockRejectedValueOnce(new Error('boom'));

    // Act
    render(<SimulationPanel />);
    fireEvent.click(screen.getByRole('button', { name: /dev csp-admin/i }));

    // Assert
    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent(/could not start simulated session/i),
    );
    expect(assign).not.toHaveBeenCalled();
  });
});
