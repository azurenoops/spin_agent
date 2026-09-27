import { useState } from 'react';
import { inputClass, Pager, Status, useRemote } from '../workspace-operations/workspaceUi';
import { Field, MutationForm } from './forms';
import * as api from './evidenceSharingApi';
import type { FindingEvidence } from './types';

export function EvidenceSharingControls({ evidence }: { evidence: FindingEvidence }) {
  const [targetPage, setTargetPage] = useState(1);
  const [historyPage, setHistoryPage] = useState(1);
  const [assignmentId, setAssignmentId] = useState('');
  const [summary, setSummary] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [saved, setSaved] = useState('');
  const [busy, setBusy] = useState(false);
  const [revoking, setRevoking] = useState(false);
  const targets = useRemote(signal => api.listShareTargets(evidence.offeringId, targetPage, signal), [evidence.offeringId, targetPage]);
  const history = useRemote(signal => api.listEvidenceShares(evidence.offeringId, evidence.evidenceId, assignmentId, historyPage, signal),
  [evidence.offeringId, evidence.evidenceId, assignmentId, historyPage]);
  const target = targets.data?.items.find(x => x.assignmentId === assignmentId);
  const latest = assignmentId && historyPage === 1 ? history.data?.items[0] : undefined;
  const blocked = targets.loading || !!targets.error || history.loading || !!history.error || !target || !evidence.evidenceRevision || historyPage !== 1;
  const refresh = () => { history.retry(); targets.retry(); setConfirmed(false); };
  return <section className="mt-4 space-y-3 rounded border border-indigo-200 bg-indigo-50/30 p-4" aria-label="Explicit mission evidence sharing">
    <h4 className="font-semibold">Approve a customer-facing summary</h4>
    <p className="text-sm">Private by default. Share only the reviewed summary of this retained artifact with a named eligible mission system. Private attachments remain restricted.</p>
    {saved && <p role="status">{saved}</p>}
    <Status loading={targets.loading || history.loading} error={targets.error ?? history.error} retry={refresh} />
    {historyPage !== 1 && <p className="text-sm">Return to the first history page before approving a replacement summary.</p>}
    <MutationForm label={latest ? 'Approve replacement summary' : 'Approve summary access'} disabled={blocked || revoking}
      onPendingChange={setBusy} submitDisabled={!summary.trim() || !confirmed}
      submit={key => api.approveEvidenceShare(evidence.offeringId, evidence.evidenceId, {
        assignmentId: target!.assignmentId, expectedAssignmentRevision: target!.assignmentRevision,
        expectedEvidenceRevision: evidence.evidenceRevision!, summary: summary.trim(),
        previousVersionId: latest?.shareId ?? null, version: (latest?.version ?? 0) + 1,
      }, key)} onSaved={() => { setSaved('Provider approval saved. Only this summary is available to the named mission.'); refresh(); }}>
      <Field label="Customer-facing summary" value={summary} onChange={text => { setSummary(text); setConfirmed(false); }} multiline required maxLength={8000} />
      <label className="flex gap-2 text-sm"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />
        I approve only this summary for the selected mission; private source attachments remain restricted.</label>
    </MutationForm>
    <fieldset disabled={busy || revoking} className="space-y-2">
      <label className="grid gap-1 text-sm">Named mission system<select className={inputClass} value={assignmentId}
        onChange={event => { setAssignmentId(event.target.value); setHistoryPage(1); setConfirmed(false); setSaved(''); }}>
        <option value="">Select an eligible associated or adopted system</option>
        {targets.data?.items.map(item => <option key={item.assignmentId} value={item.assignmentId}>{item.systemName} · {item.systemId} · tenant {item.targetTenantId}</option>)}
      </select></label>
      {targets.data && <Pager {...targets.data} onPage={page => { setTargetPage(page); setAssignmentId(''); setHistoryPage(1); setConfirmed(false); }} />}
      {targets.data?.total === 0 && <p>No eligible associated/adopted system is available. An offering label alone never grants access.</p>}
    </fieldset>
    {!!history.data?.items.length && !history.loading && !history.error && <div className="space-y-2 border-t pt-3">
      <h4 className="font-semibold">Retained sharing history ({history.data.total})</h4>
      {history.data.items.map(item => <article key={item.shareId} className="space-y-2 border-t py-2">
        <p className="text-sm">Version {item.version} · {item.revokedAt ? 'Revoked / replaced' : 'Approved summary only'} · System {item.systemId} · Tenant {item.targetTenantId}</p>
        <p className="whitespace-pre-wrap text-sm">{item.summary}</p>
        <p className="break-all text-xs">Retained summary SHA-256: {item.contentHash}</p>
        {!item.revokedAt && <RevokeShare key={item.shareId + item.revision} share={item} disabled={busy}
          onPendingChange={setRevoking}
          onSaved={() => { setRevoking(false); setSaved('Summary access revoked. The retained approval remains in provider history.'); refresh(); }} />}
      </article>)}
      <fieldset disabled={busy || revoking}><Pager {...history.data} onPage={page => { setHistoryPage(page); setConfirmed(false); }} /></fieldset>
    </div>}
  </section>;
}

function RevokeShare({ share, disabled, onSaved, onPendingChange }: {
  share: api.EvidenceShare; disabled: boolean; onSaved: () => void; onPendingChange: (value: boolean) => void;
}) {
  const [rationale, setRationale] = useState('');
  return <MutationForm label="Revoke summary access" disabled={disabled} submitDisabled={!rationale.trim()} onPendingChange={onPendingChange}
    submit={key => api.revokeEvidenceShare(share.offeringId, share.shareId, { expectedRevision: share.revision, rationale }, key)} onSaved={onSaved}>
    <Field label="Revocation rationale" value={rationale} onChange={setRationale} required />
  </MutationForm>;
}
