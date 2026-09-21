import { useCallback, useEffect, useRef, useState } from 'react';
import { getAssessmentReadiness, type AssessmentReadiness } from '../api/assessments';
import { assessmentError, type AssessmentError } from '../utils/assessmentErrors';

interface ReadinessState {
  systemId: string;
  result: AssessmentReadiness | null;
  error: AssessmentError | null;
  loading: boolean;
}

export function useAssessmentReadiness(systemId: string) {
  const [state, setState] = useState<ReadinessState>({ systemId, result: null, error: null, loading: true });
  const generation = useRef(0);

  const refresh = useCallback(async () => {
    const request = ++generation.current;
    setState({ systemId, result: null, error: null, loading: true });
    try {
      const result = await getAssessmentReadiness(systemId);
      if (request !== generation.current) return;
      if (result.systemId !== systemId) throw new Error('Unable to verify readiness for this system.');
      setState({ systemId, result, error: null, loading: false });
    } catch (error: unknown) {
      if (request !== generation.current) return;
      setState({ systemId, result: null, error: assessmentError(error, 'Unable to verify Azure assessment readiness.'), loading: false });
    }
  }, [systemId]);

  useEffect(() => {
    void refresh();
    return () => { generation.current += 1; };
  }, [refresh]);

  const block = useCallback((error: unknown) => {
    generation.current += 1;
    setState({ systemId, result: null, error: assessmentError(error, 'Assessment failed. Verify Azure readiness and retry.'), loading: false });
  }, [systemId]);

  const current = state.systemId === systemId;
  return {
    result: current ? state.result : null,
    error: current ? state.error : null,
    loading: !current || state.loading,
    isReady: current && !state.loading && !state.error && state.result?.isReady === true,
    configurationUrl: `/systems/${encodeURIComponent(systemId)}/profile/EnvironmentAndDeployment#azure-assessment-environment`,
    refresh,
    block,
  };
}
