import { useCallback, useState } from 'react';
import { usePolling } from './usePolling';
import {
  listPoamItems,
  getPoamDetail,
  getPoamMetrics,
  createPoamItem,
  updatePoamItem,
  updatePoamStatus,
} from '../api/poam';
import type {
  PaginatedPoamResponse,
  PoamDetail,
  PoamMetrics,
  PoamListQuery,
  CreatePoamRequest,
  UpdatePoamStatusRequest,
  UpdatePoamStatusResponse,
} from '../types/poam';

// ─── Data Hooks ─────────────────────────────────────────────────────────────

/** Fetches paginated POA&M list for a system with polling. */
export function usePoamList(systemId: string | undefined, query?: PoamListQuery) {
  const fetcher = useCallback(
    () => (systemId ? listPoamItems(systemId, query) : Promise.resolve(null)),
    [systemId, query?.page, query?.pageSize, query?.sortBy, query?.sortDirection,
     query?.status, query?.catSeverity, query?.overdue, query?.componentId, query?.search],
  );
  return usePolling<PaginatedPoamResponse | null>(fetcher);
}

/** Fetches a single POA&M detail by ID. */
export function usePoamDetail(poamId: string | undefined) {
  const fetcher = useCallback(
    () => (poamId ? getPoamDetail(poamId) : Promise.resolve(null)),
    [poamId],
  );
  return usePolling<PoamDetail | null>(fetcher);
}

/** Fetches POA&M metrics for a system (summary cards). */
export function usePoamMetrics(systemId: string | undefined) {
  const fetcher = useCallback(
    () => (systemId ? getPoamMetrics(systemId) : Promise.resolve(null)),
    [systemId],
  );
  return usePolling<PoamMetrics | null>(fetcher);
}

// ─── Mutation Hooks ─────────────────────────────────────────────────────────

/** Creates a POA&M item. Returns the mutation function and state. */
export function useCreatePoam() {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const create = useCallback(async (systemId: string, request: CreatePoamRequest) => {
    setLoading(true);
    setError(null);
    try {
      const result = await createPoamItem(systemId, request);
      return result;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(msg);
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  return { create, loading, error };
}

/** Updates a POA&M item. */
export function useUpdatePoam() {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const update = useCallback(async (poamId: string, request: Record<string, unknown>) => {
    setLoading(true);
    setError(null);
    try {
      const result = await updatePoamItem(poamId, request);
      return result;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(msg);
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  return { update, loading, error };
}

/** Updates POA&M status (lifecycle transition). */
export function useUpdatePoamStatus() {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const updateStatus = useCallback(async (
    poamId: string,
    request: UpdatePoamStatusRequest,
  ): Promise<UpdatePoamStatusResponse> => {
    setLoading(true);
    setError(null);
    try {
      const result = await updatePoamStatus(poamId, request);
      return result;
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(msg);
      throw err;
    } finally {
      setLoading(false);
    }
  }, []);

  return { updateStatus, loading, error };
}
