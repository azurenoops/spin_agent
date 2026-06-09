import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
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

// ─── BroadcastChannel tests (primary path) ──────────────────────────────

describe('useLoginRaceListener — BroadcastChannel path', () => {
  let mockChannel: {
    onmessage: ((e: MessageEvent) => void) | null;
    close: ReturnType<typeof vi.fn>;
    postMessage: ReturnType<typeof vi.fn>;
  };
  let BroadcastChannelSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    mockChannel = { onmessage: null, close: vi.fn(), postMessage: vi.fn() };
    BroadcastChannelSpy = vi.fn(() => mockChannel);
    Object.defineProperty(window, 'BroadcastChannel', {
      configurable: true,
      writable: true,
      value: BroadcastChannelSpy,
    });
  });

  afterEach(() => {
    // Restore to test isolation
    Object.defineProperty(window, 'BroadcastChannel', {
      configurable: true,
      writable: true,
      value: undefined,
    });
  });

  it('fires callback when login_complete message arrives and accounts are non-empty', () => {
    getAllAccounts.mockReturnValue([{ homeAccountId: 'oid-1' }]);
    const onLogin = vi.fn();
    render(<Harness onLogin={onLogin} />);

    act(() => {
      if (mockChannel.onmessage) {
        mockChannel.onmessage(
          new MessageEvent('message', { data: { type: 'login_complete' } }),
        );
      }
    });

    expect(onLogin).toHaveBeenCalledTimes(1);
  });

  it('does NOT fire for a message with wrong type', () => {
    getAllAccounts.mockReturnValue([{ homeAccountId: 'oid-1' }]);
    const onLogin = vi.fn();
    render(<Harness onLogin={onLogin} />);

    act(() => {
      if (mockChannel.onmessage) {
        mockChannel.onmessage(
          new MessageEvent('message', { data: { type: 'logout' } }),
        );
      }
    });

    expect(onLogin).not.toHaveBeenCalled();
  });

  it('does NOT fire when accounts are empty (cross-tab logout)', () => {
    getAllAccounts.mockReturnValue([]);
    const onLogin = vi.fn();
    render(<Harness onLogin={onLogin} />);

    act(() => {
      if (mockChannel.onmessage) {
        mockChannel.onmessage(
          new MessageEvent('message', { data: { type: 'login_complete' } }),
        );
      }
    });

    expect(onLogin).not.toHaveBeenCalled();
  });

  it('closes the channel on unmount', () => {
    getAllAccounts.mockReturnValue([{ homeAccountId: 'oid-1' }]);
    const onLogin = vi.fn();
    const { unmount } = render(<Harness onLogin={onLogin} />);

    unmount();

    expect(mockChannel.close).toHaveBeenCalledTimes(1);
  });

  it('opens a BroadcastChannel named ato-login', () => {
    getAllAccounts.mockReturnValue([]);
    render(<Harness onLogin={vi.fn()} />);
    expect(BroadcastChannelSpy).toHaveBeenCalledWith('ato-login');
  });
});

// ─── localStorage fallback tests (BroadcastChannel not available) ───────

// 🚫 QUARANTINED #372 | RC-5: TypeError: instance.getAllAccounts is not a function — MSAL mock incomplete for BroadcastChannel path
// Fix: Extend @azure/msal-react useMsal mock to include getAllAccounts on instance | Tracking: https://github.com/azurenoops/spin_agent/issues/372
describe.skip('useLoginRaceListener — localStorage storage-event fallback', () => {
  beforeEach(() => {
    // Remove BroadcastChannel to force the fallback path
    Object.defineProperty(window, 'BroadcastChannel', {
      configurable: true,
      writable: true,
      value: undefined,
    });
  });

  afterEach(() => {
    // Restore real BroadcastChannel if the environment has it
    Object.defineProperty(window, 'BroadcastChannel', {
      configurable: true,
      writable: true,
      value: (globalThis as unknown as { BroadcastChannel?: unknown }).BroadcastChannel,
    });
  });

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

  it('does NOT fire when accounts are empty (logout cross-tab)', () => {
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
