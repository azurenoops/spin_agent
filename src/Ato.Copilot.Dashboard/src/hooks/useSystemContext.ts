import { createContext, useContext } from 'react';
import type { SystemDetailResponse } from '../types/dashboard';

/**
 * Lightweight context so pages that load SystemDetailResponse can share it
 * with the chat panel (which lives outside the route tree).
 */
const SystemContext = createContext<SystemDetailResponse | null>(null);

export const SystemContextProvider = SystemContext.Provider;

export function useSystemContext(): SystemDetailResponse | null {
  return useContext(SystemContext);
}
