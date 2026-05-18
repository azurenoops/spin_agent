import { useEffect, useState } from 'react';
import { onImpersonationChanged, readImpersonation } from '../features/tenancy/api';

/**
 * Reactive wrapper around `readImpersonation()`. Returns `true` while a CSP-Admin
 * has an active tenant impersonation cookie + sessionStorage mirror, `false`
 * otherwise. Re-renders when impersonation starts or ends (subscribes to the
 * `ato:impersonation-changed` event dispatched from `features/tenancy/api.ts`).
 */
export function useImpersonationActive(): boolean {
  const [active, setActive] = useState<boolean>(() => readImpersonation() !== null);

  useEffect(() => {
    return onImpersonationChanged(() => {
      setActive(readImpersonation() !== null);
    });
  }, []);

  return active;
}
