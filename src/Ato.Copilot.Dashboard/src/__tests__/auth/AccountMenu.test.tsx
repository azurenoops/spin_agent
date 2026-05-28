import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, fireEvent, render, screen, within } from '@testing-library/react';
import type { MeResponse, PimRoleAssignment } from '../../features/auth/types';

// ─── Hoisted mocks ──────────────────────────────────────────────────────

const { postMock, logoutRedirectMock, purgeMock } = vi.hoisted(() => ({
  postMock: vi.fn(),
  logoutRedirectMock: vi.fn(),
  purgeMock: vi.fn(),
}));

vi.mock('axios', () => ({
  default: { post: postMock },
  post: postMock,
}));

vi.mock('../../features/auth/msalInstance', () => ({
  getMsalInstance: () => ({ logoutRedirect: logoutRedirectMock }),
}));

vi.mock('../../features/auth/useIdleFormStateBackup', () => ({
  purgeUnsavedChanges: purgeMock,
}));

// `useMe` stub — each test re-assigns `currentMe` BEFORE render. The
// AccountMenu pulls the rich `MeResponse` (persona, homeTenant, pimRoles)
// from this hook; the `oid` + `displayName` props are MSAL-bootstrap
// fallbacks used before /me resolves.
let currentMe: MeResponse | null = null;
vi.mock('../../features/auth/useMe', () => ({
  useMe: () => ({ data: currentMe, isLoading: false, error: null, refetch: vi.fn() }),
}));

import AccountMenu from '../../features/auth/AccountMenu';

// ─── Fixtures ───────────────────────────────────────────────────────────

function makeMe(overrides: Partial<MeResponse> = {}): MeResponse {
  const home = {
    id: '11111111-1111-1111-1111-111111111111',
    displayName: 'Acme Home',
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
    tenantMemberships: [home],
    ...overrides,
  };
}

function pimRole(name: string, msFromNow: number): PimRoleAssignment {
  return {
    name,
    expiresAt: new Date(Date.now() + msFromNow).toISOString(),
  };
}

function openMenu() {
  fireEvent.click(screen.getByRole('button', { name: /account menu/i }));
}

beforeEach(() => {
  currentMe = makeMe();
  postMock.mockReset();
  postMock.mockResolvedValue({ status: 204, data: { status: 'success' } });
  logoutRedirectMock.mockReset();
  logoutRedirectMock.mockResolvedValue(undefined);
  purgeMock.mockReset();
});

afterEach(() => {
  vi.useRealTimers();
});

// ─── Tests ──────────────────────────────────────────────────────────────

describe('AccountMenu', () => {
  describe('rendering', () => {
    it('shows displayName, persona, and homeTenant.displayName once the menu is opened', () => {
      // Arrange
      currentMe = makeMe({
        displayName: 'Jane Doe',
        persona: 'SystemOwner',
        homeTenant: {
          id: 'h1',
          displayName: 'Acme Home',
          status: 'Active',
        },
      });
      render(<AccountMenu oid="oid-jane" />);

      // Act
      openMenu();

      // Assert — all three fields rendered inside the menu region.
      const menu = screen.getByRole('menu');
      expect(within(menu).getByText('Jane Doe')).toBeInTheDocument();
      expect(within(menu).getByText(/SystemOwner/)).toBeInTheDocument();
      expect(within(menu).getByText('Acme Home')).toBeInTheDocument();
    });

    it('renders each active PIM role with a human-readable countdown', () => {
      // Arrange — 23 minutes and 1h 5m respectively.
      currentMe = makeMe({
        pimRoles: [
          pimRole('Compliance Officer', 23 * 60_000),
          pimRole('System Owner', 65 * 60_000),
        ],
      });
      render(<AccountMenu oid="oid-jane" />);

      // Act
      openMenu();

      // Assert — countdown formatted in minutes (<60m) and h+m (>=60m).
      const menu = screen.getByRole('menu');
      expect(within(menu).getByText('Compliance Officer')).toBeInTheDocument();
      expect(within(menu).getByText(/expires in 23m/i)).toBeInTheDocument();
      expect(within(menu).getByText('System Owner')).toBeInTheDocument();
      expect(within(menu).getByText(/expires in 1h 5m/i)).toBeInTheDocument();
    });

    it('auto-hides PIM rows whose expiresAt is in the past', () => {
      // Arrange
      currentMe = makeMe({
        pimRoles: [
          pimRole('Expired Role', -60_000), // 1 min ago
          pimRole('Active Role', 30 * 60_000), // 30 min from now
        ],
      });
      render(<AccountMenu oid="oid-jane" />);

      // Act
      openMenu();

      // Assert
      const menu = screen.getByRole('menu');
      expect(within(menu).queryByText('Expired Role')).not.toBeInTheDocument();
      expect(within(menu).getByText('Active Role')).toBeInTheDocument();
    });
  });

  describe('sign-out (Phase 4 regression guard)', () => {
    it('calls purgeUnsavedChanges(oid) BEFORE msalInstance.logoutRedirect', async () => {
      // Arrange — instrument call order across the two side-effects.
      const callOrder: string[] = [];
      purgeMock.mockImplementation(() => {
        callOrder.push('purge');
      });
      logoutRedirectMock.mockImplementation(async () => {
        callOrder.push('logout');
      });
      currentMe = makeMe({ oid: 'oid-42' });
      render(<AccountMenu oid="oid-42" />);

      // Act
      openMenu();
      await act(async () => {
        fireEvent.click(screen.getByTestId('account-menu-sign-out'));
      });

      // Assert
      expect(purgeMock).toHaveBeenCalledWith('oid-42');
      expect(postMock).toHaveBeenCalledWith('/api/auth/signout');
      expect(logoutRedirectMock).toHaveBeenCalledTimes(1);
      expect(callOrder).toEqual(['purge', 'logout']);
    });
  });

  describe('ARIA & keyboard (FR-031)', () => {
    it('trigger has accessible label "Account menu"', () => {
      // Arrange + Act
      render(<AccountMenu oid="oid-jane" />);

      // Assert — RTL accessible-name lookup uses aria-label.
      expect(
        screen.getByRole('button', { name: /account menu/i }),
      ).toBeInTheDocument();
    });

    it('aria-expanded toggles when the menu opens and closes', () => {
      // Arrange
      render(<AccountMenu oid="oid-jane" />);
      const trigger = screen.getByRole('button', { name: /account menu/i });

      // Assert — initial closed state.
      expect(trigger).toHaveAttribute('aria-expanded', 'false');

      // Act — open.
      fireEvent.click(trigger);

      // Assert — open.
      expect(trigger).toHaveAttribute('aria-expanded', 'true');

      // Act — close.
      fireEvent.click(trigger);

      // Assert — closed.
      expect(trigger).toHaveAttribute('aria-expanded', 'false');
    });

    it('Escape closes the menu and returns focus to the trigger', () => {
      // Arrange
      render(<AccountMenu oid="oid-jane" />);
      const trigger = screen.getByRole('button', { name: /account menu/i });
      fireEvent.click(trigger);

      // Sanity: the menu rendered.
      const menu = screen.getByRole('menu');
      expect(menu).toBeInTheDocument();

      // Act
      fireEvent.keyDown(menu, { key: 'Escape' });

      // Assert
      expect(screen.queryByRole('menu')).not.toBeInTheDocument();
      expect(document.activeElement).toBe(trigger);
    });

    it('exposes role="menu" with at least one role="menuitem" child', () => {
      // Arrange + Act
      render(<AccountMenu oid="oid-jane" />);
      openMenu();

      // Assert
      expect(screen.getByRole('menu')).toBeInTheDocument();
      expect(screen.getAllByRole('menuitem').length).toBeGreaterThanOrEqual(1);
    });

    it('Tab from the last menuitem cycles to the first; Shift+Tab from the first cycles to the last', () => {
      // Arrange — provide a PIM role so we have at least two focusable
      // items (PIM role row + Sign Out) for a meaningful trap.
      currentMe = makeMe({
        pimRoles: [pimRole('Compliance Officer', 30 * 60_000)],
      });
      render(<AccountMenu oid="oid-jane" />);
      openMenu();

      const items = screen.getAllByRole('menuitem');
      expect(items.length).toBeGreaterThanOrEqual(2);
      const first = items[0]!;
      const last = items[items.length - 1]!;

      // Act — focus last, Tab forward.
      last.focus();
      fireEvent.keyDown(last, { key: 'Tab' });

      // Assert — focus wraps to first.
      expect(document.activeElement).toBe(first);

      // Act — focus first, Shift+Tab backward.
      first.focus();
      fireEvent.keyDown(first, { key: 'Tab', shiftKey: true });

      // Assert — focus wraps to last.
      expect(document.activeElement).toBe(last);
    });

    it('renders an aria-live="polite" status region announcing the active PIM role expiry', () => {
      // Arrange — single active role so the live region copy is deterministic.
      currentMe = makeMe({
        pimRoles: [pimRole('Compliance Officer', 23 * 60_000)],
      });
      render(<AccountMenu oid="oid-jane" />);

      // Assert — live region is always present (even when menu is closed)
      // so a screen reader announces the initial state on render.
      const live = screen.getByRole('status');
      expect(live).toHaveAttribute('aria-live', 'polite');
      expect(live.textContent ?? '').toMatch(/Compliance Officer/);
      expect(live.textContent ?? '').toMatch(/23m/);
    });
  });
});
