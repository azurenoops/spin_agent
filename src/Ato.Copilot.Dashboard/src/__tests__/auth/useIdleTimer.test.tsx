import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, act } from '@testing-library/react';
import { useIdleTimer } from '../../features/auth/useIdleTimer';

// ─── Mocks ──────────────────────────────────────────────────────────────

const postMock = vi.fn<(url: string, body?: unknown) => Promise<{ status: number }>>(
  async () => ({ status: 204 }),
);
vi.mock('axios', async () => {
  const actual = await vi.importActual<typeof import('axios')>('axios');
  return {
    ...actual,
    default: {
      ...actual.default,
      post: (url: string, body?: unknown) => postMock(url, body),
    },
  };
});

const logoutRedirectMock = vi.fn<(opts?: { postLogoutRedirectUri?: string }) => Promise<void>>(
  async () => undefined,
);
vi.mock('../../features/auth/msalInstance', () => ({
  getMsalInstance: () => ({
    logoutRedirect: logoutRedirectMock,
  }),
}));

// ─── Helpers ────────────────────────────────────────────────────────────

function Harness({ timeoutMinutes }: { timeoutMinutes: number }) {
  useIdleTimer(timeoutMinutes);
  return null;
}

function listenForIdleWarning(): { count: number; cleanup: () => void } {
  let count = 0;
  const handler = () => {
    count += 1;
  };
  window.addEventListener('ato:idle-warning', handler as EventListener);
  return {
    get count() {
      return count;
    },
    cleanup: () => window.removeEventListener('ato:idle-warning', handler as EventListener),
  };
}

// ─── Tests ──────────────────────────────────────────────────────────────

describe('useIdleTimer', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    postMock.mockClear();
    logoutRedirectMock.mockClear();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('fires POST /api/auth/signout {reason:idle_timeout} after timeoutMinutes of inactivity', async () => {
    // Arrange — 1-minute timeout for fast tests.
    render(<Harness timeoutMinutes={1} />);

    // Act — advance just past 1 minute.
    await act(async () => {
      vi.advanceTimersByTime(60_000 + 50);
      // Drain microtasks queued by the timer callback.
      await Promise.resolve();
    });

    // Assert
    expect(postMock).toHaveBeenCalledTimes(1);
    const args = postMock.mock.calls[0]!;
    expect(args[0]).toBe('/api/auth/signout');
    expect(args[1]).toEqual({ reason: 'idle_timeout' });
    expect(logoutRedirectMock).toHaveBeenCalledTimes(1);
    const logoutArg = logoutRedirectMock.mock.calls[0]?.[0] as
      | { postLogoutRedirectUri?: string }
      | undefined;
    expect(logoutArg?.postLogoutRedirectUri).toContain('/login?reason=idle_timeout');
  });

  it.each(['mousemove', 'keydown', 'touchstart', 'click'] as const)(
    'resets the timer on a %s event',
    (eventName) => {
      // Arrange
      render(<Harness timeoutMinutes={1} />);

      // Act — advance halfway, fire the event, then advance another
      // half. If the timer was reset the sign-out request MUST NOT fire.
      act(() => {
        vi.advanceTimersByTime(30_000);
        window.dispatchEvent(new Event(eventName));
        vi.advanceTimersByTime(30_000);
      });

      // Assert
      expect(postMock).not.toHaveBeenCalled();
    },
  );

  it("resets the timer on a non-silent-renewal 'ato:user-input' event", () => {
    // Arrange
    render(<Harness timeoutMinutes={1} />);

    // Act
    act(() => {
      vi.advanceTimersByTime(30_000);
      window.dispatchEvent(
        new CustomEvent('ato:user-input', { detail: { source: 'api-success' } }),
      );
      vi.advanceTimersByTime(30_000);
    });

    // Assert — fewer than 60s combined since last reset ⇒ no sign-out.
    expect(postMock).not.toHaveBeenCalled();
  });

  it("does NOT reset the timer on an 'ato:user-input' event tagged source=silent-renewal", async () => {
    // Arrange
    render(<Harness timeoutMinutes={1} />);

    // Act — fire a silent-renewal user-input halfway through and then
    // let the timer run out. The renewal MUST NOT reset the timer
    // (FR-007a / research.md § R10).
    await act(async () => {
      vi.advanceTimersByTime(30_000);
      window.dispatchEvent(
        new CustomEvent('ato:user-input', { detail: { source: 'silent-renewal' } }),
      );
      vi.advanceTimersByTime(30_000 + 50);
      await Promise.resolve();
    });

    // Assert
    expect(postMock).toHaveBeenCalledTimes(1);
  });

  it("fires 'ato:idle-warning' 60 seconds before idle expiry", () => {
    // Arrange — 2-minute timeout so the warning fires at the 60s mark.
    const warnings = listenForIdleWarning();
    render(<Harness timeoutMinutes={2} />);

    // Act — advance just past the warning threshold (timeout - 60s).
    act(() => {
      vi.advanceTimersByTime(60_000 + 50);
    });

    // Assert
    expect(warnings.count).toBe(1);
    warnings.cleanup();
  });
});
