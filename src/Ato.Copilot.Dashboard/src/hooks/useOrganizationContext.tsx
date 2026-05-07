import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import {
  onboarding,
  type OrganizationContextDto,
} from '../features/onboarding/api/onboardingApi';

interface OrganizationContextValue {
  context: OrganizationContextDto | null;
  /**
   * Convenience: prefers `subOrganization` when filled out, falls back to
   * `organizationName`, and finally `null` when no context has been recorded
   * yet (pre-onboarding).
   */
  displayName: string | null;
  loading: boolean;
  error: Error | null;
  /** Force a refetch (useful after onboarding step 1 saves). */
  refresh: () => Promise<void>;
}

const OrganizationContext = createContext<OrganizationContextValue | undefined>(
  undefined,
);

interface ProviderProps {
  children: ReactNode;
}

/**
 * App-level provider that loads `/api/onboarding/organization-context` once and
 * exposes the result through {@link useOrganizationContext}. Pages render
 * `displayName` (sub-org if filled out, otherwise org name) for branding so the
 * tenant identity stays visible across the portal.
 *
 * Errors are tolerated silently — when the user has no context yet (pre-onboarding)
 * or the endpoint denies access, callers see `displayName === null` and can render
 * a fallback.
 */
export function OrganizationContextProvider({ children }: ProviderProps) {
  const [context, setContext] = useState<OrganizationContextDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const next = await onboarding.getOrganizationContext();
      setContext(next);
    } catch (e) {
      // Auth-forbidden or NotFound → leave null; the portal still renders.
      setError(e as Error);
      setContext(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const value = useMemo<OrganizationContextValue>(() => {
    const sub = context?.subOrganization?.trim();
    const org = context?.organizationName?.trim();
    const displayName = sub && sub.length > 0 ? sub : org && org.length > 0 ? org : null;
    return { context, displayName, loading, error, refresh: load };
  }, [context, loading, error, load]);

  return (
    <OrganizationContext.Provider value={value}>
      {children}
    </OrganizationContext.Provider>
  );
}

export function useOrganizationContext(): OrganizationContextValue {
  const ctx = useContext(OrganizationContext);
  if (!ctx) {
    throw new Error(
      'useOrganizationContext must be used inside <OrganizationContextProvider>',
    );
  }
  return ctx;
}
