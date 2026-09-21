import type { ReactNode } from 'react';
import { MeContext, useMeRequest } from './useMe';

export function MeProvider({ contextKey, enabled = true, children }: {
  contextKey: string;
  enabled?: boolean;
  children: ReactNode;
}) {
  const value = useMeRequest(enabled, contextKey);
  return <MeContext.Provider value={value}>{children}</MeContext.Provider>;
}
