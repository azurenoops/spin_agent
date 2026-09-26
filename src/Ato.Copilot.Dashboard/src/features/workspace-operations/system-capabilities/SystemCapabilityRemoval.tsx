import { useEffect, useRef, useState } from 'react';
import SetupDialog from '../SetupDialog';
import { buttonClass, errorClass, message, secondaryButtonClass, Status, useQueryState, warningClass } from '../workspaceUi';
import { Link } from '../../workspaces/workspaceNavigation';
import * as api from './systemCapabilityApi';
import { boundedRequest, sameScope } from './systemCapabilityRequests';
import { OperationPlan } from './SystemCapabilitySetup';

type Detail = Awaited<ReturnType<typeof api.getSystemCapability>>;
type Operation = Awaited<ReturnType<typeof api.getSystemCapabilityOperation>>;

export default function SystemCapabilityRemoval({ tenantId, systemId, systemName, detail, onClose, onRefresh }: {
  tenantId: string; systemId: string; systemName: string; detail: Detail; onClose: () => void; onRefresh: () => void;
}) {
  const { params, set } = useQueryState();
  const [operation, setOperation] = useState<Operation | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const [uncertain, setUncertain] = useState(false);
  const [stale, setStale] = useState(false);
  const inFlight = useRef(false);
  const active = useRef(true);
  const controller = useRef<AbortController | null>(null);
  const key = useRef(params.get('removalKey') ?? crypto.randomUUID());
  const operationId = params.get('operationId');
  const item = detail.item;
  const completed = operation?.state === 'Completed';
  const valid = (value: Operation) => sameScope(value, tenantId, systemId, 'Removal')
    && value.selections.length === 1 && value.selections[0]?.source === item.source
    && value.selections[0]?.recordId === item.recordId
    && (value.state === 'Completed' || value.selections[0]?.sourceRevision === item.sourceRevision);
  const accept = (value: Operation) => {
    if (!valid(value)) throw Object.assign(new Error('The removal preview does not match this system, source record or source revision.'), { status: 409 });
    setOperation(value); setUncertain(false); setAcknowledged(false);
  };
  async function perform(action: (signal: AbortSignal) => Promise<void>) {
    if (inFlight.current && !controller.current?.signal.aborted) return;
    inFlight.current = true; setBusy(true); setError(null);
    const request = new AbortController(); controller.current = request;
    try { await action(request.signal); }
    catch (reason) {
      if (!active.current || request.signal.aborted) return;
      setError(message(reason));
      const status = (reason as { status?: number }).status;
      if (status === 409 || status === 403 || status === 404) setStale(true);
    } finally {
      if (controller.current === request) {
        inFlight.current = false;
        if (active.current) setBusy(false);
      }
    }
  }
  const load = async (signal: AbortSignal) => {
    if (operationId) {
      const next = await boundedRequest(inner => api.getSystemCapabilityOperation(tenantId, systemId, operationId, inner), signal);
      if (!signal.aborted) accept(next);
    } else {
      set({ removalKey: key.current });
      const result = await boundedRequest(() => api.prepareSystemCapabilityRemoval(tenantId, systemId, item.source, item.recordId,
        { idempotencyKey: key.current, sourceRevision: item.sourceRevision, relationshipRevision: detail.relationshipRevision }), signal);
      if (!signal.aborted) { accept(result.operation); set({ operationId: result.operation.operationId, removalKey: null }); }
    }
  };
  useEffect(() => {
    active.current = true;
    if (detail.permissions.canManage) void perform(load);
    return () => { active.current = false; controller.current?.abort(); };
  }, [tenantId, systemId, item.source, item.recordId]);
  const complete = () => {
    if (!detail.permissions.canManage || !operation || !acknowledged || uncertain || stale || completed || operation.state === 'InProgress') return;
    void perform(async signal => {
      setUncertain(true);
      try {
        const next = await boundedRequest(() => api.completeSystemCapabilityOperation(tenantId, systemId, operation.operationId,
          { expectedRevision: operation.revision }), signal);
        if (!signal.aborted) accept(next);
      } catch (reason) {
        if (signal.aborted) return;
        try {
          const next = await boundedRequest(inner => api.getSystemCapabilityOperation(tenantId, systemId, operation.operationId, inner), signal);
          if (!signal.aborted) accept(next);
        } catch (refreshError) {
          throw new Error(`${message(reason)} Unable to refresh saved removal outcomes: ${message(refreshError)}`);
        }
        throw reason;
      }
    });
  };
  return <SetupDialog busy={busy} onClose={onClose} title="Remove from this system"
    description={`${item.name} · ${systemName} · ${item.source === 'provider' ? 'Provider unsubscribe' : 'Organization capability unlink'}`}>
    <div className="space-y-4">
      <p className="font-semibold">{completed ? `Removed ${item.name} from ${systemName}` : `Remove ${item.name} from ${systemName}?`}</p>
      <p className="text-sm">{item.source === 'provider' ? 'This removes only this system’s provider subscription.' : 'This unlinks only the organization capability from this system.'}</p>
      <p className="text-sm">Source: {item.sourceName}</p>
      <details className="text-xs"><summary className="cursor-pointer">Removal revision details</summary>
        <p className="mt-2 break-all">Source revision: {item.sourceRevision}<br />Relationship revision: {detail.relationshipRevision}</p>
      </details>
      <div className={warningClass}><p>Shared library records, unrelated capability links, other systems and approved historical narratives are retained.</p>
        <p className="mt-2">Component placements are retained. Review or remove individual placements separately in Boundaries; an authorization decision is never changed here.</p></div>
      {!detail.permissions.canManage && <p role="alert" className={errorClass}>Current system management permission is required for removal.</p>}
      <Status loading={busy && !operation} />
      {error && <p role="alert" className={errorClass}>{error}</p>}
      {operation && <OperationPlan operation={operation} records={[item]} systemName={systemName} />}
      {operation?.lastError && <p className={warningClass}>{operation.lastError}</p>}
      {stale && <p className={warningClass}>Access or source state changed. Refresh the capability before preparing another removal.</p>}
      {!completed && operation && !stale && !uncertain && <label className="flex gap-2 text-sm">
        <input type="checkbox" checked={acknowledged} disabled={busy || !detail.permissions.canManage}
          onChange={event => setAcknowledged(event.target.checked)} />I reviewed this exact removal and the retained records.
      </label>}
      <div className="flex flex-wrap justify-end gap-3">
        <button type="button" className={secondaryButtonClass} disabled={busy} onClick={onClose}>Cancel</button>
        {stale ? <button type="button" className={secondaryButtonClass} disabled={busy} onClick={onRefresh}>Refresh capability</button>
          : completed ? <Link className={buttonClass} to={`/systems/${encodeURIComponent(systemId)}/security-capabilities`}>View system capabilities</Link>
            : operation && !uncertain && operation.state !== 'InProgress'
              ? <button type="button" className="rounded-md bg-red-700 px-3 py-2 text-sm font-semibold text-white disabled:opacity-50"
                disabled={busy || !acknowledged || !detail.permissions.canManage} onClick={complete}>
                {operation.state === 'Partial' ? 'Retry unfinished removal' : item.source === 'provider' ? 'Unsubscribe from this system' : 'Unlink from this system'}</button>
              : <button type="button" className={secondaryButtonClass} disabled={busy || !detail.permissions.canManage}
                onClick={() => { void perform(load); }}>{operation || operationId ? 'Refresh saved outcomes' : 'Retry removal preview'}</button>}
      </div>
      <p className="text-xs text-slate-500 dark:text-gray-400">Cancelling only closes this dialog. It does not undo saved changes. The operation URL recovers the same persisted plan and outcomes.</p>
    </div>
  </SetupDialog>;
}
