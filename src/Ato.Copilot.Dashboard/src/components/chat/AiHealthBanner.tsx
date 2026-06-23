import { useState, useCallback, useEffect } from 'react';
import { acquireBearer } from '../../features/auth/msalInstance';

/**
 * T272: AI provider health indicator + degraded-mode banner.
 *
 * Performs a lightweight health-check against the MCP chat endpoint on:
 *  1. Panel open
 *  2. Before each send (triggered externally)
 *
 * Renders a non-blocking banner when the provider is unreachable.
 * A "Retry" button re-checks and dismisses on success.
 * Deduplication: only one banner shown at a time.
 */

type HealthStatus = 'unknown' | 'checking' | 'healthy' | 'degraded';

interface AiHealthBannerProps {
  /** Increment this counter each time a check should be triggered (panel open, before send). */
  triggerCheck: number;
  /** Called after the check completes (either way). */
  onCheckComplete?: (healthy: boolean) => void;
}

export function useAiHealthCheck() {
  const [status, setStatus] = useState<HealthStatus>('unknown');

  const check = useCallback(async (): Promise<boolean> => {
    setStatus('checking');

    // fix(#521): Acquire the bearer token in its own try-catch so that an MSAL
    // initialisation failure on cold page load is NOT treated as a provider
    // failure.  Auth-not-ready ≠ AI provider unreachable.
    let token: string | null = null;
    try {
      token = await acquireBearer();
    } catch {
      // MSAL not ready — auth failure is not a provider failure.
      // Treat as unknown so the banner is not shown on cold page load.
      setStatus('unknown');
      return true;
    }

    // fix/425: The MCP server exposes health at /health (root), not /mcp/health.
    // nginx proxies /api/health → ${MCP_BASE_URL}/health so the dashboard can
    // reach it without CORS issues. VITE_MCP_BASE_URL is only used in dev/vscode
    // where the server is accessed directly; the nginx proxy is used in staging/prod.
    const attempt = async (): Promise<boolean> => {
      try {
        const baseUrl = import.meta.env.VITE_MCP_BASE_URL || '';
        const url = `${baseUrl}/api/health`;
        const headers: Record<string, string> = { Accept: 'application/json' };
        if (token) headers['Authorization'] = `Bearer ${token}`;
        const resp = await fetch(url, { method: 'GET', headers, signal: AbortSignal.timeout(5000) });
        if (resp.ok) { setStatus('healthy'); return true; }
        if (resp.status === 401 || resp.status === 403) {
          // Auth issue, not provider issue — MSAL token may not be cached yet.
          // Don't show the degraded banner; treat as unknown and let the user
          // retry once the session is fully authenticated.
          setStatus('unknown');
          return true;
        }
        setStatus('degraded');
        return false;
      } catch (err: unknown) {
        // fix(#526): AbortError / TimeoutError means the request was cancelled
        // during navigation or by AbortSignal.timeout() — this is NOT a genuine
        // provider failure. Setting 'degraded' here was firing the banner even
        // when the API returned HTTP 200 on the very next load.
        if (err instanceof DOMException && (err.name === 'AbortError' || err.name === 'TimeoutError')) {
          setStatus('unknown'); // navigation cancelled the request — not a real failure
          return true;
        }
        setStatus('degraded');
        return false;
      }
    };

    const firstAttempt = await attempt();
    if (!firstAttempt) {
      // Retry once after 2 s to avoid transient startup false-positives.
      await new Promise<void>((resolve) => setTimeout(resolve, 2000));
      return await attempt();
    }
    return firstAttempt;
  }, []);

  return { status, check };
}

export default function AiHealthBanner({ triggerCheck, onCheckComplete }: AiHealthBannerProps) {
  const { status, check } = useAiHealthCheck();
  const [dismissed, setDismissed] = useState(false);

  useEffect(() => {
    if (triggerCheck === 0) return;
    void check().then((healthy) => {
      if (healthy) setDismissed(false);
      onCheckComplete?.(healthy);
    });
  }, [triggerCheck]); // eslint-disable-line react-hooks/exhaustive-deps

  if (status === 'healthy' || status === 'unknown' || status === 'checking' || dismissed) {
    return null;
  }

  return (
    <div
      role="alert"
      aria-live="assertive"
      className="flex items-center justify-between gap-3 bg-amber-50 border-b border-amber-200 px-4 py-2 text-sm text-amber-800"
    >
      <div className="flex items-center gap-2">
        <svg className="h-4 w-4 shrink-0 text-amber-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126zM12 15.75h.007v.008H12v-.008z" />
        </svg>
        <span>AI provider is currently unreachable. Responses may be delayed or unavailable.</span>
      </div>
      <div className="flex items-center gap-2 shrink-0">
        <button
          onClick={() => {
            void check().then((healthy) => {
              if (healthy) setDismissed(true);
            });
          }}
          className="rounded px-2 py-1 text-xs font-medium bg-amber-100 hover:bg-amber-200 text-amber-800 transition-colors"
        >
          {'Retry'}
        </button>
        <button
          onClick={() => setDismissed(true)}
          aria-label="Dismiss"
          className="rounded p-1 hover:bg-amber-100 text-amber-600 transition-colors"
        >
          <svg className="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
          </svg>
        </button>
      </div>
    </div>
  );
}
