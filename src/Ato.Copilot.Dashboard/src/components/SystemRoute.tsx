import { useState, useCallback, useEffect, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import { SystemContextProvider } from '../hooks/useSystemContext';
import { usePolling } from '../hooks/usePolling';
import { getSystemDetail } from '../api/systemDetail';
import type { SystemDetailResponse } from '../types/dashboard';

/** Extract systemId from any /systems/:id path. */
function extractSystemId(pathname: string): string | null {
  const match = pathname.match(/^\/systems\/([^/]+)/);
  return match?.[1] ?? null;
}

/**
 * Top-level provider that watches the URL and auto-fetches
 * SystemDetailResponse whenever a /systems/:id route is active.
 * Provides the data to ALL children — including ChatPanel.
 */
export default function SystemDataProvider({ children }: { children: ReactNode }) {
  const location = useLocation();
  const systemId = extractSystemId(location.pathname);
  const [detail, setDetail] = useState<SystemDetailResponse | null>(null);

  // Clear when navigating away from a system page
  useEffect(() => {
    if (!systemId) setDetail(null);
  }, [systemId]);

  const fetchDetail = useCallback(async () => {
    if (!systemId) return;
    try {
      const d = await getSystemDetail(systemId);
      setDetail(d);
    } catch {
      // Non-critical: chat just won't have phase context
    }
  }, [systemId]);

  usePolling(fetchDetail, 60000, !!systemId);

  return (
    <SystemContextProvider value={detail}>
      {children}
    </SystemContextProvider>
  );
}
