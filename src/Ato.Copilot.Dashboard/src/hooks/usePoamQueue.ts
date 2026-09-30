import { useCallback } from 'react';
import { getPoamWorkspace } from '../api/poamWorkspace';
import type { PoamListQuery } from '../types/poam';
import { projectPoamQueue } from '../components/poam/poamQueueProjection';
import { poamErrorMessage } from '../utils/poamErrors';
import { usePoamRead } from './usePoamRead';

export function usePoamQueue(systemId: string, query: PoamListQuery) {
  const fetcher = useCallback((signal: AbortSignal) => getPoamWorkspace(systemId, signal), [systemId]);
  const result = usePoamRead(fetcher);
  try {
    return { ...result, data: result.data ? projectPoamQueue(result.data, query) : null };
  } catch (error) {
    return { ...result, data: null, error: new Error(poamErrorMessage(error)) };
  }
}
