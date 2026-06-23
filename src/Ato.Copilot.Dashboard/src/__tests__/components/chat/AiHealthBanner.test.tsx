import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';

// ── Hoist mocks BEFORE imports that import the real module ────────────────
// acquireBearer lives in msalInstance; the health check calls it before fetch.
// We replace it with a no-op that always resolves to '' so MSAL is not needed.
vi.mock('../../../features/auth/msalInstance', () => ({
  acquireBearer: vi.fn().mockResolvedValue(''),
  getMsalInstance: vi.fn(),
  setMsalInstance: vi.fn(),
  DEFAULT_API_SCOPES: ['api://ato-copilot/.default'],
}));

import { useAiHealthCheck } from '../../../components/chat/AiHealthBanner';
import { acquireBearer } from '../../../features/auth/msalInstance';

// ── Helpers ───────────────────────────────────────────────────────────────

function makeAbortError(): DOMException {
  return new DOMException('The user aborted a request.', 'AbortError');
}

function makeTimeoutError(): DOMException {
  return new DOMException('Signal timed out.', 'TimeoutError');
}

function makeNetworkError(): TypeError {
  return new TypeError('Failed to fetch');
}

// ── Tests ─────────────────────────────────────────────────────────────────

describe('useAiHealthCheck — fix #526: AbortError / TimeoutError must NOT trigger degraded', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('sets status to "healthy" when fetch returns HTTP 200', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200 });

    const { result } = renderHook(() => useAiHealthCheck());
    expect(result.current.status).toBe('unknown');

    let healthy: boolean | undefined;
    await act(async () => {
      healthy = await result.current.check();
    });

    expect(healthy).toBe(true);
    expect(result.current.status).toBe('healthy');
  });

  it('sets status to "degraded" on a genuine network error (TypeError)', async () => {
    globalThis.fetch = vi.fn().mockRejectedValue(makeNetworkError());

    const { result } = renderHook(() => useAiHealthCheck());

    let healthy: boolean | undefined;
    await act(async () => {
      healthy = await result.current.check();
    });

    expect(healthy).toBe(false);
    expect(result.current.status).toBe('degraded');
  });

  it('sets status to "unknown" (NOT "degraded") when fetch throws AbortError (fix #526)', async () => {
    globalThis.fetch = vi.fn().mockRejectedValue(makeAbortError());

    const { result } = renderHook(() => useAiHealthCheck());

    let healthy: boolean | undefined;
    await act(async () => {
      healthy = await result.current.check();
    });

    // AbortError means navigation cancelled the request — not a real failure.
    expect(result.current.status).toBe('unknown');
    // Returns true so callers don't treat this as a health failure.
    expect(healthy).toBe(true);
  });

  it('sets status to "unknown" (NOT "degraded") when fetch throws TimeoutError (fix #526)', async () => {
    globalThis.fetch = vi.fn().mockRejectedValue(makeTimeoutError());

    const { result } = renderHook(() => useAiHealthCheck());

    let healthy: boolean | undefined;
    await act(async () => {
      healthy = await result.current.check();
    });

    expect(result.current.status).toBe('unknown');
    expect(healthy).toBe(true);
  });

  it('sets status to "unknown" on HTTP 401 (auth not yet resolved, not a provider failure)', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: false, status: 401 });

    const { result } = renderHook(() => useAiHealthCheck());

    let healthy: boolean | undefined;
    await act(async () => {
      healthy = await result.current.check();
    });

    expect(result.current.status).toBe('unknown');
    expect(healthy).toBe(true);
  });

  it('sets status to "degraded" on HTTP 503 (real provider failure)', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: false, status: 503 });

    const { result } = renderHook(() => useAiHealthCheck());

    let healthy: boolean | undefined;
    await act(async () => {
      healthy = await result.current.check();
    });

    expect(healthy).toBe(false);
    expect(result.current.status).toBe('degraded');
  });
});

// ── Tests for fix #521: MSAL cold-load false-positive ────────────────────

describe('useAiHealthCheck — fix #521: MSAL token acquisition failure must NOT show degraded banner', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('sets status to "unknown" (NOT "degraded") when acquireBearer throws (MSAL not yet initialised)', async () => {
    // Simulate cold page load: MSAL is not initialised yet — acquireBearer throws.
    vi.mocked(acquireBearer).mockRejectedValueOnce(
      new Error('No MSAL instance set. Call setMsalInstance() first.'),
    );
    // fetch should NOT be called at all — stub it to verify.
    globalThis.fetch = vi.fn();

    const { result } = renderHook(() => useAiHealthCheck());

    let healthy: boolean | undefined;
    await act(async () => {
      healthy = await result.current.check();
    });

    // Auth-not-ready must NOT be reported as a provider failure.
    expect(result.current.status).toBe('unknown');
    expect(healthy).toBe(true);
    // Health endpoint must not have been called (no token, no request).
    expect(globalThis.fetch).not.toHaveBeenCalled();
  });

  it('sets status to "healthy" when acquireBearer succeeds and fetch returns HTTP 200', async () => {
    vi.mocked(acquireBearer).mockResolvedValueOnce('valid-token');
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200 });

    const { result } = renderHook(() => useAiHealthCheck());

    let healthy: boolean | undefined;
    await act(async () => {
      healthy = await result.current.check();
    });

    expect(healthy).toBe(true);
    expect(result.current.status).toBe('healthy');
  });
});

