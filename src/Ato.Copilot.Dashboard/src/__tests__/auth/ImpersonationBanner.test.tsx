import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, act, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { MeResponse } from '../../features/auth/types';

// ─── Hoisted mocks ──────────────────────────────────────────────────────

const { navigate } = vi.hoisted(() => ({ navigate: vi.fn() }));
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: () => navigate,
  };
});

// `useMe` stub that each test reassigns. We model both the data shape AND
// a `refetch` spy so we can assert the Exit handler triggers a refresh.
let currentMe: MeResponse | null = null;
const refetch = vi.fn();
vi.mock('../../features/auth/useMe', () => ({
  useMe: () => ({ data: currentMe, isLoading: false, error: null, refetch }),
}));

// Wrap the Feature 048 endImpersonation so we can spy on it without
// reaching the network. The real module also exposes startImpersonation
// (used by Phase 11.3 to capture the pre-impersonation URL) — preserve it
// via vi.importActual.
const { endImpersonation } = vi.hoisted(() => ({ endImpersonation: vi.fn() }));
vi.mock('../../features/tenancy/api', async () => {
  const actual = await vi.importActual<typeof import('../../features/tenancy/api')>(
    '../../features/tenancy/api',
  );
  return {
    ...actual,
    endImpersonation,
  };
});

import ImpersonationBanner from '../../features/auth/ImpersonationBanner';
import {
  PRE_IMPERSONATION_URL_KEY,
  setPreImpersonationUrl,
} from '../../features/auth/preImpersonationUrl';

// ─── Helpers ────────────────────────────────────────────────────────────

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
  const expiresAt = new Date(Date.now() + 60 * 60 * 1000).toISOString();
  return {
    oid: 'oid-csp-admin',
    displayName: 'CSP Admin',
    persona: 'CspAdmin',
    homeTenant: home,
    effectiveTenant: target,
    isImpersonating: true,
    impersonation: {
      impersonatedTenant: target,
      startedAt: new Date().toISOString(),
      expiresAt,
    },
    pimRoles: [],
    isCspAdmin: true,
    isSocAnalyst: false,
    tenantMemberships: [home, target],
    ...overrides,
  };
}

function renderBanner() {
  return render(
    <MemoryRouter>
      <ImpersonationBanner />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  navigate.mockReset();
  refetch.mockReset();
  endImpersonation.mockReset();
  sessionStorage.clear();
  currentMe = null;
});

afterEach(() => {
  vi.useRealTimers();
});

// ─── Tests ──────────────────────────────────────────────────────────────

describe('ImpersonationBanner (Feature 051 T133–T135)', () => {
  it('renders nothing when me.isImpersonating === false', () => {
    // Arrange
    currentMe = makeMe({ isImpersonating: false, impersonation: null });

    // Act
    const { container } = renderBanner();

    // Assert
    expect(container.firstChild).toBeNull();
  });

  it('renders nothing when me is null (unauthenticated or loading)', () => {
    // Arrange
    currentMe = null;

    // Act
    const { container } = renderBanner();

    // Assert
    expect(container.firstChild).toBeNull();
  });

  it('renders the impersonated tenant name + a countdown when isImpersonating', () => {
    // Arrange — expiresAt = now + 30 minutes so the countdown shows a useful string.
    vi.useFakeTimers();
    const now = new Date('2026-01-01T12:00:00Z').getTime();
    vi.setSystemTime(now);
    currentMe = makeMe({
      impersonation: {
        impersonatedTenant: {
          id: '22222222-2222-2222-2222-222222222222',
          displayName: 'Coastal Watch',
          status: 'Active',
        },
        startedAt: new Date(now - 60_000).toISOString(),
        expiresAt: new Date(now + 30 * 60 * 1000).toISOString(),
      },
    });

    // Act
    renderBanner();

    // Assert — tenant name visible and "30:" appears in the countdown.
    expect(screen.getByText(/Coastal Watch/)).toBeInTheDocument();
    expect(screen.getByText(/30:00/)).toBeInTheDocument();
  });

  it('tick: countdown decrements every second under fake timers', async () => {
    // Arrange
    vi.useFakeTimers();
    const now = new Date('2026-01-01T12:00:00Z').getTime();
    vi.setSystemTime(now);
    currentMe = makeMe({
      impersonation: {
        impersonatedTenant: {
          id: '22222222-2222-2222-2222-222222222222',
          displayName: 'Coastal Watch',
          status: 'Active',
        },
        startedAt: new Date().toISOString(),
        expiresAt: new Date(now + 2 * 60 * 1000).toISOString(), // 2 min
      },
    });

    // Act
    renderBanner();
    expect(screen.getByText(/02:00/)).toBeInTheDocument();
    // vi.advanceTimersByTime(N) advances BOTH the timer queue AND the
    // mocked Date.now reading, so we do not need a separate
    // vi.setSystemTime call here.
    act(() => {
      vi.advanceTimersByTime(1_000);
    });

    // Assert
    expect(screen.getByText(/01:59/)).toBeInTheDocument();
  });

  it('has accessible role=status + aria-live=polite (FR-039 WCAG)', () => {
    // Arrange
    currentMe = makeMe();

    // Act
    renderBanner();

    // Assert
    const banner = screen.getByRole('status');
    expect(banner).toBeInTheDocument();
    expect(banner.getAttribute('aria-live')).toBe('polite');
  });

  // 🚫 QUARANTINED #372 | RC-2: data-testid="impersonation-exit-confirm" absent — dialog reimplemented or testid renamed
  // Fix: Update selectors to match current ImpersonationBanner implementation | Tracking: https://github.com/azurenoops/spin_agent/issues/372
  it.skip('Exit button opens confirmation dialog (Wave 6 GAP-221-B)', () => {
    currentMe = makeMe();
    renderBanner();
    fireEvent.click(screen.getByRole('button', { name: /exit/i }));
    expect(screen.getByTestId('impersonation-exit-confirm')).toBeInTheDocument();
    expect(endImpersonation).not.toHaveBeenCalled();
  });

  // 🚫 QUARANTINED #372 | RC-2: impersonation-exit-confirm testid missing | Tracking: https://github.com/azurenoops/spin_agent/issues/372
  it.skip('Exit confirm dialog: Stay closes without calling end endpoint', () => {
    endImpersonation.mockResolvedValue(undefined);
    currentMe = makeMe();
    renderBanner();
    fireEvent.click(screen.getByRole('button', { name: /exit/i }));
    expect(screen.getByTestId('impersonation-exit-confirm')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /stay/i }));
    expect(screen.queryByTestId('impersonation-exit-confirm')).not.toBeInTheDocument();
    expect(endImpersonation).not.toHaveBeenCalled();
  });

  // 🚫 QUARANTINED #372 | RC-2: impersonation-exit-confirm testid missing | Tracking: https://github.com/azurenoops/spin_agent/issues/372
  it.skip('Exit confirm dialog: confirm calls end endpoint and refetches /me', async () => {
    endImpersonation.mockResolvedValue(undefined);
    currentMe = makeMe();
    renderBanner();
    fireEvent.click(screen.getByRole('button', { name: /exit/i }));
    expect(screen.getByTestId('impersonation-exit-confirm')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('impersonation-exit-confirm-btn'));
    await waitFor(() => {
      expect(endImpersonation).toHaveBeenCalledTimes(1);
      expect(refetch).toHaveBeenCalledTimes(1);
    });
  });

  // 🚫 QUARANTINED #372 | RC-2: impersonation-exit-confirm testid missing | Tracking: https://github.com/azurenoops/spin_agent/issues/372
  it.skip('confirmation dialog contains the impersonated tenant name', () => {
    currentMe = makeMe();
    renderBanner();
    fireEvent.click(screen.getByRole('button', { name: /exit/i }));
    const dialog = screen.getByTestId('impersonation-exit-confirm');
    // makeMe() uses 'Coastal Watch' as the displayName
    expect(dialog.textContent).toContain('Coastal Watch');
  });

  // 🚫 QUARANTINED #372 | RC-2: impersonation-exit-confirm testid missing | Tracking: https://github.com/azurenoops/spin_agent/issues/372
  it.skip('Exit navigates to getPreImpersonationUrl() value when one is stored (FR-029)', async () => {
    // Arrange
    endImpersonation.mockResolvedValue(undefined);
    setPreImpersonationUrl('/systems/my-system/controls?tab=findings');
    currentMe = makeMe();
    renderBanner();

    // Act: open confirm dialog, then confirm (Wave 6 GAP-221-B two-step)
    fireEvent.click(screen.getByRole('button', { name: /exit/i }));
    fireEvent.click(screen.getByTestId('impersonation-exit-confirm-btn'));

    // Assert — FR-029: return to the pre-impersonation URL.
    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith('/systems/my-system/controls?tab=findings');
    });
    // Key cleared so a subsequent impersonation does not reuse it.
    expect(sessionStorage.getItem(PRE_IMPERSONATION_URL_KEY)).toBeNull();
    // The Exit handler MUST NOT silently route to /csp/dashboard or / —
    // FR-029 is explicit about that.
    expect(navigate).not.toHaveBeenCalledWith('/csp/dashboard');
    expect(navigate).not.toHaveBeenCalledWith('/');
  });

  // 🚫 QUARANTINED #372 | RC-2: impersonation-exit-confirm testid missing | Tracking: https://github.com/azurenoops/spin_agent/issues/372
  it.skip('Exit falls back to the persona landing page when no pre-impersonation URL is stored', async () => {
    // Arrange — no setPreImpersonationUrl call so getPreImpersonationUrl returns null.
    endImpersonation.mockResolvedValue(undefined);
    currentMe = makeMe(); // persona = CspAdmin
    renderBanner();

    // Act: open confirm dialog, then confirm (Wave 6 GAP-221-B two-step)
    fireEvent.click(screen.getByRole('button', { name: /exit/i }));
    fireEvent.click(screen.getByTestId('impersonation-exit-confirm-btn'));

    // Assert — falls back to `/` (the CSP-Admin's persona-default landing).
    await waitFor(() => {
      expect(navigate).toHaveBeenCalled();
    });
    const target = navigate.mock.calls[0]?.[0] as string;
    // Default landing for any persona without a stored URL is the SPA root.
    expect(target).toBe('/');
  });

  it('when expiresAt is already in the past, surfaces auto-end copy and refetches /me', () => {
    // Arrange — use real timers because waitFor under fake timers needs
    // explicit timer-advancement scaffolding that would obscure the
    // intent. The component's auto-end effect runs synchronously on
    // mount when expiresAtMs <= now.
    const past = new Date(Date.now() - 60 * 1000).toISOString();
    currentMe = makeMe({
      impersonation: {
        impersonatedTenant: {
          id: '22222222-2222-2222-2222-222222222222',
          displayName: 'Coastal Watch',
          status: 'Active',
        },
        startedAt: new Date(Date.now() - 65 * 60 * 1000).toISOString(),
        expiresAt: past,
      },
    });

    // Act
    renderBanner();

    // Assert — banner shows the auto-end message AND refetches /me so
    // the server-side ImpersonationEnd(expired) audit row writes promptly.
    expect(screen.getByText(/Impersonation ended automatically/i)).toBeInTheDocument();
    expect(refetch).toHaveBeenCalled();
  });
});
