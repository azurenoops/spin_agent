import { useEffect, useRef, useState } from 'react';
import { buttonClass, errorClass, message, secondaryButtonClass, Status, surfaceClass, useRemote, warningClass } from '../workspace-operations/workspaceUi';
import { enrichPackage, getAnalysisOperation } from './api';
import { PackageImportError } from './request';
import type { PackageStatus } from './types';

interface Intent { key: string; revision: number; operationId: string | null }
function readIntent(storageKey: string): { intent: Intent | null; error: string | null } {
  try {
    const text = sessionStorage.getItem(storageKey);
    if (!text) return { intent: null, error: null };
    const value: unknown = JSON.parse(text);
    if (!value || typeof value !== 'object' || !('key' in value) || typeof value.key !== 'string'
      || !('revision' in value) || typeof value.revision !== 'number' || !Number.isSafeInteger(value.revision)
      || !('operationId' in value) || (value.operationId !== null && typeof value.operationId !== 'string')) {
      throw new Error('Stored enrichment identity is invalid; do not start a replacement analysis.');
    }
    return { intent: { key: value.key, revision: value.revision, operationId: value.operationId }, error: null };
  } catch (reason) { return { intent: null, error: `Unable to recover enrichment identity: ${message(reason)}` }; }
}
export function PackageEnrichment({ status, disabled, onPendingChange, onChanged }: {
  status: PackageStatus; disabled: boolean; onPendingChange: (pending: boolean) => void; onChanged: () => void;
}) {
  const storageKey = `ato-package-enrichment:${status.packageId}`;
  const [saved, setSaved] = useState(() => readIntent(storageKey));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const lock = useRef(false);
  const operationId = saved.intent?.operationId;
  const operation = useRemote(signal => operationId ? getAnalysisOperation(status.packageId, operationId, signal) : Promise.resolve(null),
    [status.packageId, operationId]);
  const terminal = !!operation.data && ['Completed', 'Succeeded', 'Failed', 'Cancelled'].includes(operation.data.state);
  const completed = !!operation.data && ['Completed', 'Succeeded'].includes(operation.data.state);
  const pending = busy || (!!saved.intent && (!operationId || !terminal))
    || (completed && status.analysisProfileVersion !== operation.data?.targetAnalysisProfileVersion);
  useEffect(() => { onPendingChange(pending); }, [pending, onPendingChange]);
  useEffect(() => {
    if (!operationId || terminal || operation.loading || operation.error) return;
    const timer = window.setTimeout(operation.retry, 5000);
    return () => window.clearTimeout(timer);
  }, [operationId, terminal, operation.loading, operation.error, operation.retry]);
  useEffect(() => {
    if (!busy && (!saved.intent || operationId)) return;
    const warn = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ''; };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [busy, saved.intent, operationId]);
  const run = async () => {
    if (lock.current || disabled || saved.error) return;
    lock.current = true; setBusy(true); setError(null);
    const intent = saved.intent && !terminal ? saved.intent : { key: crypto.randomUUID(), revision: status.revision, operationId: null };
    try {
      sessionStorage.setItem(storageKey, JSON.stringify(intent));
      setSaved({ intent, error: null });
      const result = await enrichPackage(status.packageId, { expectedRevision: intent.revision, targetAnalysisProfileVersion: 2 }, intent.key);
      if (!result.operationId || result.packageId !== status.packageId || result.targetAnalysisProfileVersion !== 2) {
        throw new Error('No matching durable enrichment receipt was returned. Recover the same operation.');
      }
      const confirmed = { ...intent, operationId: result.operationId };
      setSaved({ intent: confirmed, error: null });
      sessionStorage.setItem(storageKey, JSON.stringify(confirmed));
    } catch (reason) {
      if (reason instanceof PackageImportError && [400, 401, 403, 404, 409, 422].includes(reason.status ?? 0)) {
        try { sessionStorage.removeItem(storageKey); setSaved({ intent: null, error: null }); }
        catch (storageError) { setSaved(current => ({ ...current, error: message(storageError) })); }
      }
      setError(`${message(reason)} Original source bytes and reviewed revisions are not replaced. Recover the existing receipt before starting another analysis.`);
    } finally { lock.current = false; setBusy(false); }
  };
  return <section aria-label="Analysis profile" className={`${surfaceClass} space-y-3 p-4`}>
    <h2 className="font-semibold">Authorization claim analysis</h2>
    <p className="text-sm">Stored analysis profile: {status.analysisProfileVersion ?? 'Not reported'}.</p>
    <p className={warningClass}>Profile 1 does not establish authorization-decision, boundary, finding or POA&amp;M family coverage. Explicit enrichment uses retained sources, preserves reviewed proposals and invalidates affected approval; it never issues authority or publishes records.</p>
    {saved.error && <p role="alert" className={errorClass}>{saved.error}</p>}
    {error && <p role="alert" className={errorClass}>{error}</p>}
    <Status loading={!!operationId && operation.loading} error={operation.error} retry={operation.retry} />
    {operation.data && <div className="space-y-2 text-sm">
      <p>Enrichment operation: {operation.data.state}</p><p className="break-all">Operation {operation.data.operationId} · Profile {operation.data.sourceProfileVersion} → {operation.data.targetAnalysisProfileVersion}</p>
      {operation.data.message && <p className={warningClass}>{operation.data.errorCode}: {operation.data.message}</p>}
      <button type="button" className={secondaryButtonClass} onClick={operation.retry}>Refresh enrichment status</button>
      {terminal && <button type="button" className={`${secondaryButtonClass} ml-2`} onClick={onChanged}>Refresh package after analysis</button>}
    </div>}
    {((!!saved.intent && !operationId) || (status.analysisProfileVersion !== 2 && (!operationId || terminal && !completed))) && <button type="button" className={buttonClass}
      disabled={disabled || busy || !!saved.error} onClick={() => void run()}>
      {busy ? 'Requesting enrichment...' : saved.intent && !terminal ? 'Recover enrichment receipt' : 'Analyze additional authorization claim families'}
    </button>}
  </section>;
}
