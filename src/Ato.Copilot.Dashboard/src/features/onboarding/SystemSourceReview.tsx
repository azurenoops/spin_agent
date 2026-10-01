import { useEffect, useRef, useState } from 'react';
import SetupFrame, { SetupGuidance, SetupPanel } from './shared/SetupFrame';
import { Link, useNavigate } from '../workspaces/workspaceNavigation';
import { workspaceErrorMessage } from '../workspaces/api';
import {
  applySystemSource, getSystemSetup, getSystemSource, previewSystemSource,
  type SystemSetupView, type SystemSourceApply, type SystemSourcePreview, type SystemSourceReceipt,
} from './systemSetupApi';

export default function SystemSourceReview({ tenantId, systemId, kind, receiptId }: {
  tenantId: string; systemId: string; kind: string; receiptId: string;
}) {
  const navigate = useNavigate();
  const [source, setSource] = useState<SystemSourceReceipt>();
  const [target, setTarget] = useState<SystemSetupView>();
  const [preview, setPreview] = useState<SystemSourcePreview>();
  const [decisions, setDecisions] = useState<Record<string, 'keepCurrent' | 'applyProposed'>>({});
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const alive = useRef(true);
  const pending = useRef<{ key: string; data: SystemSourceApply } | undefined>(undefined);
  useEffect(() => {
    alive.current = true;
    void Promise.all([getSystemSource(tenantId, systemId, kind, receiptId), getSystemSetup(tenantId, systemId)])
      .then(([receipt, system]) => { if (alive.current) { setSource(receipt); setTarget(system); } })
      .catch(reason => { if (alive.current) setError(workspaceErrorMessage(reason)); })
      .finally(() => { if (alive.current) setBusy(false); });
    return () => { alive.current = false; };
  }, [tenantId, systemId, kind, receiptId]);
  async function review() {
    if (!source || !target?.canManage || busy) return;
    setBusy(true); setError(null);
    try {
      const result = await previewSystemSource(tenantId, systemId, source);
      if (!alive.current) return;
      setPreview(result); setConfirmed(false);
      setDecisions(Object.fromEntries(result.fields.filter(field => field.supported).map(field => [field.field, 'keepCurrent'])));
    } catch (reason) { if (alive.current) setError(workspaceErrorMessage(reason)); }
    finally { if (alive.current) setBusy(false); }
  }
  async function apply() {
    if (!source || !preview || !confirmed || !target?.canManage || busy) return;
    const intent = pending.current ?? { key: crypto.randomUUID(), data: {
      expectedSourceRevision: preview.sourceRevision, expectedSystemRevision: preview.identityRevision,
      previewHash: preview.previewHash, decisions: Object.entries(decisions).map(([field, decision]) => ({ field, decision })),
    } };
    pending.current = intent; setBusy(true); setError(null);
    try {
      const receipt = await applySystemSource(tenantId, systemId, source, intent.data, intent.key);
      if (!alive.current) return;
      setSource(receipt); pending.current = undefined; setPreview(undefined); setConfirmed(false);
    } catch (reason) {
      if (!alive.current) return;
      setError(workspaceErrorMessage(reason));
      const status = reason && typeof reason === 'object' && 'status' in reason ? reason.status : undefined;
      if (status === 409 || status === 400) { pending.current = undefined; setPreview(undefined); setConfirmed(false); }
    } finally { if (alive.current) setBusy(false); }
  }
  const applied = source?.reviewState === 'applied';
  const path = `/systems/${encodeURIComponent(systemId)}`;
  return <SetupFrame journey="System" currentStep="source-review" steps={[]}
    title="Review source-derived identity" description="Review the retained original and exact target. Nothing is applied until you confirm field decisions."
    busy={busy} error={error} onBack={() => navigate(`${path}/setup`)}
    primaryAction={applied ? { label: 'Open document previews', onClick: () => navigate(`${path}/documents`) }
      : preview ? { label: pending.current ? 'Retry original decisions' : 'Apply reviewed decisions',
        onClick: () => void apply(), disabled: !confirmed || !target?.canManage }
      : { label: 'Review proposed fields', onClick: () => void review(), disabled: !target?.canManage || source?.analysisState !== 'parsed' }}
    guidance={<SetupGuidance title="Preserve reviewed records">Only supported identity fields are proposed. Approved document baselines, narratives, controls, roles and authorization decisions are not overwritten. Applying a source review does not approve the SSP.</SetupGuidance>}>
    <SetupPanel title="Exact system and retained source">
      <p>{target?.displayName}</p><p className="mt-2">{source?.fileName}</p>
      <p className="mt-2 break-all font-mono text-xs">{source?.sha256}</p>
      <p className="mt-2 text-sm">Receipt: {source?.receiptState} · Analysis: {source?.analysisState} · Review: {source?.reviewState}</p>
      {source?.error && <p role="alert" className="mt-3 text-red-800">{source.error}</p>}
      {applied && <p role="status" className="mt-3 rounded bg-green-50 p-3">Source decisions recorded. Original bytes and field-level review provenance are retained. Document approval and authorization remain separate.</p>}
    </SetupPanel>
    {preview && <SetupPanel title="Field-by-field review"><fieldset disabled={busy || Boolean(pending.current)} className="space-y-4">
      {preview.fields.map(field => <div key={field.field} className="rounded border p-3">
        <p className="font-medium">{field.field}</p><p className="text-sm">Current: {field.currentValue || 'Not recorded'}</p>
        <p className="text-sm">Proposed: {field.proposedValue || 'Not extracted'}</p>
        {field.supported ? <label className="mt-2 block text-sm">Decision for {field.field}<select className="ml-3 rounded border p-2"
          value={decisions[field.field]} onChange={event => { setDecisions(previous => ({ ...previous,
            [field.field]: event.target.value as 'keepCurrent' | 'applyProposed' })); setConfirmed(false); }}>
          <option value="keepCurrent">Keep current</option><option value="applyProposed">Apply proposed</option>
        </select></label> : <p className="text-sm text-amber-800">Not supported for application; retained for later documentation review.</p>}
      </div>)}
      <label className="flex gap-3"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />I reviewed this source and the exact target fields.</label>
    </fieldset></SetupPanel>}
    <Link className="text-purple-700 underline" to={`${path}/setup`}>Return to saved system setup</Link>
  </SetupFrame>;
}
