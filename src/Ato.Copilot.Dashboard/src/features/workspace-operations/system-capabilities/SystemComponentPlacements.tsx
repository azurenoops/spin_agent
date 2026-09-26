import { useEffect, useRef, useState } from 'react';
import { buttonClass, errorClass, inputClass, message, secondaryButtonClass, Status, useRemote, warningClass } from '../workspaceUi';
import * as api from './systemCapabilityApi';
import { boundedRequest } from './systemCapabilityRequests';
import type { SystemCapabilitySource } from './systemCapabilityTypes';

export default function SystemComponentPlacements({ tenantId, systemId, source, componentId, onBusyChange, onChanged }: {
  tenantId: string; systemId: string; source: SystemCapabilitySource; componentId: string;
  onBusyChange: (busy: boolean) => void; onChanged: (notice: string) => void;
}) {
  const key = { source, recordType: 'component' as const, recordId: componentId };
  const remote = useRemote(signal => boundedRequest(inner => api.getSystemComponentPlacements(tenantId, systemId, key, inner), signal),
    [tenantId, systemId, source, componentId]);
  const [boundaryId, setBoundaryId] = useState('');
  const [removingId, setRemovingId] = useState<string | null>(null);
  const [acknowledged, setAcknowledged] = useState(false);
  const [busy, setBusy] = useState(false);
  const [invalidated, setInvalidated] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const pending = useRef<AbortController | null>(null);
  const active = useRef(true);
  useEffect(() => {
    active.current = true;
    return () => { active.current = false; pending.current?.abort(); onBusyChange(false); };
  }, [onBusyChange]);
  const options = remote.data;
  const choices = options?.boundaries.filter(boundary => !options.placements.some(placement => placement.boundaryId === boundary.id)) ?? [];
  const selectedBoundary = choices.find(boundary => boundary.id === boundaryId);
  const removing = options?.placements.find(placement => placement.id === removingId);
  const locked = busy || invalidated || remote.loading || !!remote.error;
  async function save(action: (signal: AbortSignal) => Promise<unknown>, notice: string) {
    if (pending.current || locked) return;
    const controller = new AbortController();
    pending.current = controller; setBusy(true); onBusyChange(true); setError(null);
    try {
      await boundedRequest(action, controller.signal);
      if (active.current) onChanged(notice);
    } catch (reason) {
      if (active.current) { setError(message(reason)); setInvalidated(true); setAcknowledged(false); }
    } finally {
      pending.current = null;
      if (active.current) { setBusy(false); onBusyChange(false); }
    }
  }
  return <div className="space-y-4">
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {error && <p role="alert" className={errorClass}>{error}</p>}
    {invalidated && <div className={warningClass}><p>The reviewed state is no longer safe to reuse. Refresh saved placements before any further change.</p>
      <button type="button" className={`${secondaryButtonClass} mt-3`} onClick={() => {
        setError(null); setInvalidated(false); setBoundaryId(''); setRemovingId(null); setAcknowledged(false); remote.retry();
      }}>Refresh placements</button></div>}
    {options && <>
      <ul className="space-y-3">{options.placements.map(placement => <li key={placement.id} className="rounded border border-gray-200 p-3 dark:border-gray-700">
        <p className="font-medium">{placement.boundaryName ?? (placement.state === 'SystemWide' ? 'System-wide' : 'Unassigned')}
          {placement.state === 'Excluded' && ' (Excluded)'}</p>
        {placement.canUnassign ? <button type="button" className={`${secondaryButtonClass} mt-2`} disabled={locked}
          onClick={() => { setRemovingId(placement.id); setAcknowledged(false); }}>Remove from {placement.boundaryName}</button>
          : <p className="mt-2 text-sm text-gray-600 dark:text-gray-300">{placement.unassignBlockedReason ?? 'This assignment cannot be removed in the boundary editor.'}</p>}
      </li>)}</ul>
      {removing && <section aria-label="Confirm boundary placement removal" className={`${warningClass} space-y-3`}>
        <p>Remove only the component&apos;s placement on {removing.boundaryName}? Its source, capabilities, other placements and approved narratives are retained.</p>
        <label className="flex gap-2 text-sm"><input type="checkbox" checked={acknowledged} disabled={locked}
          onChange={event => setAcknowledged(event.target.checked)} />I reviewed removal of only this boundary placement.</label>
        <div className="flex flex-wrap gap-2">
          <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => setRemovingId(null)}>Cancel removal</button>
          <button type="button" className={buttonClass} disabled={locked || !acknowledged || !removing.canUnassign}
            onClick={() => { if (!acknowledged || !removing.canUnassign) return; void save(
              signal => api.unassignSystemComponentBoundary(tenantId, systemId, key, removing.id,
                { sourceRevision: options.sourceRevision, relationshipRevision: options.relationshipRevision, placementRevision: removing.revision }, signal),
              `Removed from ${removing.boundaryName}. Other placements are unchanged.`); }}>Confirm placement removal</button>
        </div>
      </section>}
      {options.canAssignBoundary ? <form className="space-y-3" onSubmit={event => {
        event.preventDefault();
        if (!selectedBoundary) return;
        void save(signal => api.assignSystemComponentBoundary(tenantId, systemId, key,
          { boundaryId: selectedBoundary.id, sourceRevision: options.sourceRevision, relationshipRevision: options.relationshipRevision }, signal),
        `Assigned to ${selectedBoundary.name}.`);
      }}>
        <label className="grid gap-1 text-sm">Boundary for this component
          <select className={`${inputClass} min-w-0 w-full`} value={boundaryId} disabled={locked || !choices.length}
            onChange={event => setBoundaryId(event.target.value)}><option value="">Choose a boundary</option>
            {choices.map(boundary => <option key={boundary.id} value={boundary.id}>{boundary.name}</option>)}</select></label>
        {!choices.length && <p className="text-sm text-gray-600 dark:text-gray-300">No additional authorized boundaries are available.</p>}
        <button type="submit" className={buttonClass} disabled={locked || !selectedBoundary}>Assign to boundary</button>
      </form> : <p className="text-sm text-gray-600 dark:text-gray-300">{options.assignBlockedReason ?? 'No boundary-placement permission is available.'}</p>}
    </>}
  </div>;
}
