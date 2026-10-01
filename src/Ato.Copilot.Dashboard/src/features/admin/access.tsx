import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import axios from 'axios';

export type WorkspaceKind = 'Administration' | 'System';
export type AccessScopeKind = 'Organization' | 'Provider' | 'Platform' | 'System';

export interface AccessBadge {
  label: string;
  source: string;
}

export interface EffectiveAccessDestination {
  id: string;
  workspace: WorkspaceKind;
  scopeKind: AccessScopeKind;
  scopeId: string;
  displayName: string;
  actions: string[];
  badges: AccessBadge[];
  availability: 'Available' | 'SetupRequired' | 'Unavailable';
}

export interface EffectiveAccess {
  version: string;
  generatedAt: string;
  subject: {
    objectId: string;
    displayName: string;
    tenantId: string;
    isCspAdmin: boolean;
  };
  defaultDestinationId: string | null;
  destinations: EffectiveAccessDestination[];
}

interface AccessContextValue {
  access: EffectiveAccess | null;
  selectedDestination: EffectiveAccessDestination | null;
  isLoading: boolean;
  error: Error | null;
  selectDestination: (destinationId: string) => Promise<boolean>;
  reload: () => void;
}

const AccessContext = createContext<AccessContextValue | null>(null);
const rememberedDestinationKey = 'spin.admin.remembered-destination';

export function canAccessAction(
  destination: EffectiveAccessDestination | null,
  action: string,
): boolean {
  return destination?.actions.includes(action) ?? false;
}

export function resolveRememberedDestination(
  destinationId: string | null,
  destinations: EffectiveAccessDestination[],
): EffectiveAccessDestination | null {
  if (!destinationId) return null;
  return destinations.find((destination) => destination.id === destinationId) ?? null;
}

function initialDestination(access: EffectiveAccess): EffectiveAccessDestination | null {
  const remembered = resolveRememberedDestination(
    window.localStorage.getItem(rememberedDestinationKey),
    access.destinations,
  );
  if (remembered) return remembered;

  return resolveRememberedDestination(access.defaultDestinationId, access.destinations)
    ?? access.destinations.find((destination) =>
      destination.scopeId === access.subject.tenantId)
    ?? (access.destinations.length === 1 ? access.destinations[0]! : null);
}

export function EffectiveAccessProvider({ children }: { children: ReactNode }) {
  const [access, setAccess] = useState<EffectiveAccess | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [isLoading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);
  const [refresh, setRefresh] = useState(0);
  const requestGeneration = useRef(0);

  const reload = useCallback(() => setRefresh((value) => value + 1), []);

  useEffect(() => {
    const controller = new AbortController();
    const generation = ++requestGeneration.current;
    setAccess(null);
    setSelectedId(null);
    setLoading(true);
    setError(null);

    void axios.get('/api/auth/effective-access', { signal: controller.signal })
      .then((response) => {
        if (generation !== requestGeneration.current) return;
        const envelope = response.data as { status?: string; data?: EffectiveAccess };
        if (envelope.status !== 'success' || !envelope.data) {
          throw new Error('Unexpected effective-access response.');
        }
        const nextAccess = envelope.data;
        const destination = initialDestination(nextAccess);
        setAccess(nextAccess);
        setSelectedId(destination?.id ?? null);
      })
      .catch((reason: unknown) => {
        if (controller.signal.aborted || generation !== requestGeneration.current) return;
        setError(reason instanceof Error ? reason : new Error(String(reason)));
      })
      .finally(() => {
        if (!controller.signal.aborted && generation === requestGeneration.current) {
          setLoading(false);
        }
      });

    return () => {
      controller.abort();
      requestGeneration.current += 1;
    };
  }, [refresh]);

  useEffect(() => {
    const handleTenantChange = () => reload();
    window.addEventListener('ato:tenant-changed', handleTenantChange);
    return () => window.removeEventListener('ato:tenant-changed', handleTenantChange);
  }, [reload]);

  useEffect(() => {
    const handleRevocation = () => {
      requestGeneration.current += 1;
      setAccess(null);
      setSelectedId(null);
      setLoading(true);
      window.localStorage.removeItem(rememberedDestinationKey);
      reload();
    };
    window.addEventListener('spin:access-revoked', handleRevocation);
    return () => window.removeEventListener('spin:access-revoked', handleRevocation);
  }, [reload]);

  const selectDestination = useCallback(async (destinationId: string) => {
    const destination = access?.destinations.find((item) => item.id === destinationId);
    if (!destination) return false;

    requestGeneration.current += 1;
    setSelectedId(destinationId);
    window.localStorage.setItem(rememberedDestinationKey, destinationId);
    window.dispatchEvent(new CustomEvent('spin:workspace-changed', {
      detail: { destinationId },
    }));
    return true;
  }, [access]);

  const selectedDestination = useMemo(
    () => resolveRememberedDestination(selectedId, access?.destinations ?? []),
    [access?.destinations, selectedId],
  );

  const value = useMemo<AccessContextValue>(() => ({
    access,
    selectedDestination,
    isLoading,
    error,
    selectDestination,
    reload,
  }), [access, selectedDestination, isLoading, error, selectDestination, reload]);

  return <AccessContext.Provider value={value}>{children}</AccessContext.Provider>;
}

export function useEffectiveAccess(): AccessContextValue {
  const value = useContext(AccessContext);
  if (!value) {
    throw new Error('useEffectiveAccess must be used within EffectiveAccessProvider.');
  }
  return value;
}

declare global {
  interface WindowEventMap {
    'spin:workspace-changed': CustomEvent<{ destinationId: string }>;
    'spin:access-revoked': CustomEvent;
  }
}
