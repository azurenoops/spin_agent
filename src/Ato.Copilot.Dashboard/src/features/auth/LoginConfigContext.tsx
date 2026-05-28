import { createContext, useContext, type ReactNode } from 'react';
import type { LoginConfig } from './types';

/**
 * Feature 051 § 4.3 — provides the bootstrap-fetched `LoginConfig` to the
 * whole authenticated app. Throws (NOT returns undefined) if the hook is
 * used outside a `LoginConfigProvider` so the failure mode is loud during
 * development instead of producing a downstream null-ref.
 */
const LoginConfigContext = createContext<LoginConfig | null>(null);

export interface LoginConfigProviderProps {
  value: LoginConfig;
  children: ReactNode;
}

export function LoginConfigProvider({ value, children }: LoginConfigProviderProps) {
  return (
    <LoginConfigContext.Provider value={value}>
      {children}
    </LoginConfigContext.Provider>
  );
}

export function useLoginConfig(): LoginConfig {
  const ctx = useContext(LoginConfigContext);
  if (ctx === null) {
    throw new Error(
      'useLoginConfig must be used within a <LoginConfigProvider>. ' +
        'Wire it in `main.tsx` after fetching GET /api/auth/login-config.',
    );
  }
  return ctx;
}
