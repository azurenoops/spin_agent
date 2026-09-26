import { useEffect, useRef, useState } from 'react';
import { downloadEvidence } from '../../../api/evidence';
import { generateQueuedProposal, getProposalById, type NarrativeProposal } from '../../../api/narrativeLibrary';
import { Link, useNavigate } from '../../workspaces/workspaceNavigation';
import SetupDialog from '../SetupDialog';
import { StateBadge, workspaceCard } from '../CapabilityPresentation';
import { buttonClass, errorClass, inputClass, message, secondaryButtonClass, Status, useRemote, warningClass } from '../workspaceUi';
import type { SystemCapabilityDetail } from './systemCapabilityTypes';
import { reviewSystemCapabilityNarrative } from './systemCapabilityApi';
import { boundedRequest } from './systemCapabilityRequests';
import { generateScopedSystemCapabilityProposal } from './systemCapabilityNarrativeRequests';

type Narrative = SystemCapabilityDetail['narratives'][number];
type Proposal = Narrative['proposals'][number];

export default function SystemCapabilityNarratives({ tenantId, systemId, detail, onChanged }: {
  tenantId: string; systemId: string; detail: SystemCapabilityDetail; onChanged: () => void;
}) {
  const navigate = useNavigate();
  const [selected, setSelected] = useState<{ narrative: Narrative; proposal: Proposal } | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const working = useRef(false);
  const active = useRef(true);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  async function perform(action: () => Promise<void>) {
    if (working.current) return;
    working.current = true; setBusy(true); setError(null);
    try { await action(); }
    catch (reason) { if (active.current) setError(message(reason)); }
    finally { working.current = false; if (active.current) setBusy(false); }
  }
  const download = (evidence: SystemCapabilityDetail['evidence'][number]) => {
    void perform(async () => {
      const blob = await boundedRequest(() => downloadEvidence(systemId, evidence.id));
      if (!active.current) return;
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url; link.download = evidence.fileName; document.body.appendChild(link);
      link.click(); link.remove(); URL.revokeObjectURL(url);
    });
  };
  const generate = (narrative: Narrative, queued?: Proposal) => {
    if (!detail.permissions.canAuthorNarratives || !narrative.canGenerate) return;
    void perform(async () => {
      const proposal = await boundedRequest(() => queued
        ? generateQueuedProposal(systemId, queued.id, queued.revision)
        : generateScopedSystemCapabilityProposal(tenantId, systemId, detail.item.source, detail.item.recordId,
          { controlId: narrative.controlId, narrativeType: narrative.narrativeType, expectedVersion: narrative.currentVersion, sourceRevision: detail.item.sourceRevision }));
      if (!active.current) return;
      if (proposal.controlId !== narrative.controlId || proposal.narrativeType !== narrative.narrativeType)
        throw new Error('The returned proposal does not match this control and narrative type. Refresh the narrative workspace.');
      navigate(`/systems/${encodeURIComponent(systemId)}/narratives/review?proposal=${encodeURIComponent(proposal.id)}`);
    });
  };
  return <div className="space-y-5">
    <p className={warningClass}>Approved narratives are preserved until an authorized proposal is accepted. Policy and technical freshness are evaluated independently.</p>
    {error && <p role="alert" className={errorClass}>{error}</p>}
    <section className={`${workspaceCard} space-y-4`}><h2 className="text-lg font-semibold">Evidence references</h2>
      {detail.evidence.length ? <div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead><tr>
        <th className="p-2">Filename</th><th className="p-2">Owner / source</th><th className="p-2">State / control</th><th className="p-2">Action</th>
      </tr></thead><tbody className="divide-y divide-slate-200 dark:divide-gray-700">{detail.evidence.map(evidence => <tr key={evidence.id}>
        <td className="p-2">{evidence.fileName}</td><td className="p-2">{evidence.owner}<br />{evidence.source}</td>
        <td className="p-2">{evidence.state}<br />{evidence.controlId ?? 'Capability-scoped'} · {evidence.narrativeType}</td>
        <td className="p-2"><button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => download(evidence)}>Open reference</button></td>
      </tr>)}</tbody></table></div> : <p className="text-sm text-slate-500 dark:text-gray-400">No authorized evidence references are linked to this capability or its mapped control implementations.</p>}
      {detail.permissions.canManageEvidence
        ? <Link className={secondaryButtonClass} to={`/systems/${encodeURIComponent(systemId)}/evidence`}>Link evidence in the system repository</Link>
        : <p className="text-sm text-slate-500 dark:text-gray-400">Evidence management permission is required to link additional evidence.</p>}
      <p className="text-xs text-slate-500 dark:text-gray-400">Reference availability does not verify implementation. Protected downloads recheck access; storage locations and bearer URLs are never shown.</p>
    </section>
    <section className="space-y-4"><h2 className="text-lg font-semibold">Narrative review</h2>
      {!detail.narratives.length && <p className={workspaceCard}>No scoped narrative records are available.</p>}
      <div className="grid gap-4 xl:grid-cols-2">{detail.narratives.map(narrative => <article key={`${narrative.controlId}:${narrative.narrativeType}`} className={`${workspaceCard} space-y-3`}>
        <header className="flex flex-wrap items-center justify-between gap-2"><h3 className="font-semibold">{narrative.controlId} · {narrative.narrativeType} narrative</h3>
          <StateBadge tone={narrative.approvedContent !== null ? 'green' : 'neutral'}>{narrative.approvedContent !== null ? 'Approved content' : 'No approved content'}</StateBadge></header>
        <p className="text-sm">Freshness: {narrative.freshness} · Current version: {narrative.currentVersion} · {narrative.approvalStatus}</p>
        <section aria-label={`Approved ${narrative.narrativeType} content for ${narrative.controlId}`} className="rounded border border-slate-200 p-3 text-sm dark:border-gray-700">
          <h4 className="mb-2 font-medium">Approved content</h4><p className="whitespace-pre-wrap">{narrative.approvedContent ?? 'No approved snapshot is recorded.'}</p>
        </section>
        {narrative.currentContent !== narrative.approvedContent && <details className="text-sm"><summary className="cursor-pointer">Current working content (not an approval)</summary>
          <p className="mt-2 whitespace-pre-wrap">{narrative.currentContent ?? 'No working content.'}</p></details>}
        {narrative.proposals.map(proposal => <div key={proposal.id} className="space-y-2 rounded border border-slate-200 p-3 text-sm dark:border-gray-700">
          <p>Proposal: {proposal.status} · Revision {proposal.revision}{proposal.isStale ? ' · Stale' : ''}</p>
          <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => setSelected({ narrative, proposal })}>View {narrative.narrativeType} proposal for {narrative.controlId}</button>
          {['PendingGeneration', 'GenerationFailed'].includes(proposal.status) && <button type="button" className={buttonClass}
            disabled={busy || !detail.permissions.canAuthorNarratives || !narrative.canGenerate}
            onClick={() => generate(narrative, proposal)}>Generate queued {narrative.narrativeType} proposal for {narrative.controlId}</button>}
          {(!detail.permissions.canReviewNarratives || !proposal.canReview) && <p className="text-xs text-slate-500 dark:text-gray-400">Review is not permitted for this actor or proposal. Responsibility-review permission is a separate grant.</p>}
        </div>)}
        <button type="button" className={buttonClass} disabled={busy || !detail.permissions.canAuthorNarratives || !narrative.canGenerate}
          onClick={() => generate(narrative)}>Generate {narrative.narrativeType} proposal for {narrative.controlId}</button>
        {narrative.blockedReason && <p className="text-sm text-amber-800 dark:text-amber-200">{narrative.blockedReason}</p>}
        {!detail.permissions.canAuthorNarratives && <p className="text-sm text-slate-500 dark:text-gray-400">Narrative authoring permission is required.</p>}
      </article>)}</div>
    </section>
    <p className="break-all rounded border border-slate-200 p-3 text-xs dark:border-gray-700">Source / provenance: System {systemId} · Capability {detail.item.name} ({detail.item.source}:{detail.item.recordId}) · Revision {detail.item.sourceRevision} · Controls {detail.item.controlIds.join(', ') || 'None recorded'}</p>
    {selected && <ProposalReview key={`${selected.proposal.id}:${selected.proposal.revision}`} tenantId={tenantId} systemId={systemId} detail={detail}
      narrative={selected.narrative} proposal={selected.proposal} onClose={() => setSelected(null)} onChanged={() => { setSelected(null); onChanged(); }} />}
  </div>;
}

function ProposalReview({ tenantId, systemId, detail, narrative, proposal, onClose, onChanged }: {
  tenantId: string; systemId: string; detail: SystemCapabilityDetail; narrative: Narrative; proposal: Proposal; onClose: () => void; onChanged: () => void;
}) {
  const [note, setNote] = useState('');
  const [reviewed, setReviewed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [invalidated, setInvalidated] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const active = useRef(true);
  const inFlight = useRef(false);
  useEffect(() => { active.current = true; return () => { active.current = false; }; }, []);
  const content = useRemote(async signal => {
    if (proposal.source !== detail.item.source || proposal.recordId !== detail.item.recordId)
      throw new Error('The proposal source does not match this capability.');
    const next = await boundedRequest(() => getProposalById(systemId, proposal.id), signal);
    if (!next || next.controlId !== narrative.controlId || next.narrativeType !== narrative.narrativeType
      || next.revision !== proposal.revision || next.id !== proposal.id)
      throw new Error('The proposal changed or does not match this system, control and narrative type. Refresh the capability.');
    return next;
  }, [systemId, detail.item.source, detail.item.recordId, proposal.id, proposal.revision, narrative.controlId, narrative.narrativeType]);
  const permitted = detail.permissions.canReviewNarratives && proposal.canReview && content.data?.canReview === true
    && content.data.status === 'Draft' && !proposal.isStale && !content.data.isStale && !invalidated;
  async function decide(decision: 'Approve' | 'RequestRevision') {
    if (!permitted || busy || inFlight.current || !reviewed || decision === 'RequestRevision' && !note.trim()) return;
    inFlight.current = true; setBusy(true); setError(null);
    try {
      await boundedRequest(() => reviewSystemCapabilityNarrative(tenantId, systemId, detail.item.source, detail.item.recordId, proposal.id,
        { expectedRevision: proposal.revision, decision, note: note.trim() }));
      if (active.current) onChanged();
    } catch (reason) {
      if (active.current) { setInvalidated(true); setReviewed(false); setError(`${message(reason)} Refresh the proposal before another decision.`); }
    } finally { inFlight.current = false; if (active.current) setBusy(false); }
  }
  return <SetupDialog busy={busy} onClose={onClose} title={`Review ${narrative.controlId} ${narrative.narrativeType} proposal`}
    description={`System ${systemId} · ${detail.item.name} · Exact proposal revision ${proposal.revision}`}>
    <div className="space-y-4"><Status loading={content.loading} error={content.error} retry={content.retry} />
      {error && <p role="alert" className={errorClass}>{error}</p>}
      {content.data && <ProposalContent content={content.data} />}
      {!permitted && !content.loading && <p className={warningClass}>This proposal cannot be accepted here. Current reviewer permission, a non-stale Draft and the exact source revision are required; authors cannot approve their own work.</p>}
      <label className="flex gap-2 text-sm"><input type="checkbox" checked={reviewed} disabled={!permitted || busy}
        onChange={event => setReviewed(event.target.checked)} />I reviewed the source, proposed content and dependency findings.</label>
      <label className="grid gap-1 text-sm">Review note<textarea className={inputClass} value={note} maxLength={2000} disabled={!permitted || busy}
        onChange={event => setNote(event.target.value)} /></label>
      <div className="flex flex-wrap gap-3"><button type="button" className={secondaryButtonClass} disabled={!permitted || busy || !reviewed || !note.trim()}
        onClick={() => { void decide('RequestRevision'); }}>Return for revision</button>
        <button type="button" className={buttonClass} disabled={!permitted || busy || !reviewed} onClick={() => { void decide('Approve'); }}>Accept proposal</button></div>
      <p className="text-xs text-slate-500 dark:text-gray-400">Approved historical content remains preserved. Acceptance changes only the authorized narrative version, not implementation or an authorization decision.</p>
    </div>
  </SetupDialog>;
}

function ProposalContent({ content }: { content: NarrativeProposal }) {
  return <>
    <div className="grid gap-4 md:grid-cols-2"><section><h3 className="font-semibold">Previous version {content.baseVersion}</h3>
      <p className="mt-2 whitespace-pre-wrap text-sm">{content.beforeContent}</p></section>
      <section><h3 className="font-semibold">Proposed content</h3><p className="mt-2 whitespace-pre-wrap text-sm">{content.proposedContent}</p></section></div>
    {[...content.conflicts, ...content.missingEvidence].map((finding, index) => <p key={index} className={warningClass}>{finding}</p>)}
    <details className="text-sm"><summary className="cursor-pointer">Persisted proposal provenance</summary><pre className="mt-2 whitespace-pre-wrap break-all text-xs">{JSON.stringify(content.provenance, null, 2)}</pre></details>
  </>;
}
