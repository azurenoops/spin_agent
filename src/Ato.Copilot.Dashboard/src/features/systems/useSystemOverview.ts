import { useCallback, useEffect, useRef, useState } from 'react';
import { getPackageReadinessWorkspace, validatePackageReadiness, type PackageReadinessWorkspace } from '../../api/packageReadiness';
import { overviewError } from '../../api/systemOverview';

export function useSystemOverview(systemId: string) {
  const [workspace, setWorkspace] = useState<PackageReadinessWorkspace | null>(null);
  const [loading, setLoading] = useState(true);
  const [checking, setChecking] = useState(false);
  const [error, setError] = useState('');
  const [revision, setRevision] = useState(0);
  const pending = useRef<AbortController | null>(null);
  const read = useRef<AbortController | null>(null);
  const load = useCallback(async () => {
    read.current?.abort();
    const controller = new AbortController();
    read.current = controller; setLoading(true); setError('');
    try {
      const result = await getPackageReadinessWorkspace(systemId, { purpose: 'InitialSubmission' }, controller.signal);
      if (!controller.signal.aborted) { setWorkspace(result); setRevision(value => value + 1); }
    } catch (reason) {
      if (!controller.signal.aborted) setError(overviewError(reason));
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, [systemId]);
  useEffect(() => {
    void load();
    return () => { read.current?.abort(); pending.current?.abort(); };
  }, [load]);
  const check = useCallback(async () => {
    if (pending.current) return;
    const controller = new AbortController();
    pending.current = controller; setChecking(true); setError('');
    try {
      const response = await validatePackageReadiness(systemId, { purpose: 'InitialSubmission' }, controller.signal);
      if (controller.signal.aborted) return;
      const refreshed = await getPackageReadinessWorkspace(systemId, { purpose: 'InitialSubmission' }, controller.signal);
      if (controller.signal.aborted) return;
      const prior = workspace?.latestRun?.outcome === 'Ready' || workspace?.latestRun?.outcome === 'Blocked'
        ? workspace.latestRun : workspace?.lastSuccessfulRun;
      const last = response.run.outcome === 'Ready' || response.run.outcome === 'Blocked' ? response.run
        : refreshed.lastSuccessfulRun ?? prior ?? null;
      setWorkspace({ ...refreshed, latestRun: response.run, lastSuccessfulRun: last });
      setRevision(value => value + 1);
      if (response.run.outcome === 'Failed' || response.run.outcome === 'SourceChanged')
        setError(response.run.failure?.message ?? 'Sources changed during evaluation. Previous successful results are retained; check again.');
    } catch (reason) {
      if (!controller.signal.aborted) setError(overviewError(reason));
    } finally {
      pending.current = null;
      if (!controller.signal.aborted) setChecking(false);
    }
  }, [systemId, workspace]);
  const latest = workspace?.latestRun;
  const successful = latest?.outcome === 'Ready' || latest?.outcome === 'Blocked' ? latest : workspace?.lastSuccessfulRun ?? null;
  return { workspace, setWorkspace, loading, checking, error, revision, load, check, successful };
}
