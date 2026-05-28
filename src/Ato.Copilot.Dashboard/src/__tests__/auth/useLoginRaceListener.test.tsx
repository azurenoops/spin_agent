import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, act } from '@testing-library/react';
import { useLoginRaceListener } from '../../features/auth/useLoginRaceListener';

// ─── Mocks ──────────────────────────────────────────────────────────────

const getAllAccounts = vi.fn();
vi.mock('@azure/msal-react', () => ({
  useMsal: () => ({
    instance: { getAllAccounts },
  }),
}));

// Tiny harness component that mounts the hook.
function Harness({ onLogin }: { onLogin: () => void }) {
  useLoginRaceListener({ onLoginCompletedInAnotherTab: onLogin });
  return null;
}

beforeEach(() => {
  getAllAccounts.mockReset();
});

// ─── Tests ──────────────────────────────────────────────────────────────

describe('useLoginRaceListener', () => {
  it('fires onLoginCompletedInAnotherTab when a msal account key writes and accounts are non-empty', () => {
    getAllAccounts.mockReturnValue([{ homeAccountId: 'oid-1' }]);
    const onLogin = vi.fn();
    render(<Harness onLogin={onLogin} />);

    act(() => {
      window.dispatchEvent(
        new StorageEvent('storage', {
          key: 'msal.account.keys.0',
          newValue: '[]',
        }),
      );
    });

    expect(onLogin).toHaveBeenCalledTimes(1);
  });

  it('does NOT fire for storage events on unrelated keys', () => {
    getAllAccounts.mockReturnValue([{ homeAccountId: 'oid-1' }]);
    const onLogin = vi.fn();
    render(<Harness onLogin={onLogin} />);

    act(() => {
      window.dispatchEvent(
        new StorageEvent('storage', {
          key: 'unrelated_key',
          newValue: 'whatever',
        }),
      );
    });

    expect(onLogin).not.toHaveBeenCalled();
  });

  it('does NOT fire when the storage event has no accounts in the cache (logout cross-tab)', () => {
    getAllAccounts.mockReturnValue([]);
    const onLogin = vi.fn();
    render(<Harness onLogin={onLogin} />);

    act(() => {
      window.dispatchEvent(
        new StorageEvent('storage', {
          key: 'msal.account.keys.0',
          newValue: null,
        }),
      );
    });

    expect(onLogin).not.toHaveBeenCalled();
  });

  it('does NOT fire when storage event has no key (clear())', () => {
    getAllAccounts.mockReturnValue([{ homeAccountId: 'oid-1' }]);
    const onLogin = vi.fn();
    render(<Harness onLogin={onLogin} />);

    act(() => {
      window.dispatchEvent(
        new StorageEvent('storage', {
          key: null,
          newValue: null,
        }),
      );
    });

    expect(onLogin).not.toHaveBeenCalled();
  });

  it('unregisters the storage listener on unmount', () => {
    getAllAccounts.mockReturnValue([{ homeAccountId: 'oid-1' }]);
    const onLogin = vi.fn();
    const { unmount } = render(<Harness onLogin={onLogin} />);

    unmount();

    act(() => {
      window.dispatchEvent(
        new StorageEvent('storage', {
          key: 'msal.account.keys.0',
          newValue: '[]',
        }),
      );
    });

    expect(onLogin).not.toHaveBeenCalled();
  });
});
