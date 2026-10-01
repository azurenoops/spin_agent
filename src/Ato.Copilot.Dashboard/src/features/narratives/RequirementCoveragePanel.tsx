import { useEffect, useState } from 'react';
import {
  acceptEnhancement, getRequirementCoverage,
  proposeEnhancement, reviewRequirementResponses, saveRequirementResponses, returnEnhancement,
  type RequirementCoverageDetail, type RequirementResponse,
} from '../../api/requirementCoverage';
import { listEvidence } from '../../api/evidence';
import type { EvidenceArtifactDto } from '../../types/evidence';
import { Link } from '../workspaces/workspaceNavigation';

interface Props {
  systemId: string; controlId: string; kind: 'policy' | 'technical';
  onNavigate: (controlId: string) => void; onChanged: () => void;
  onDirtyChange?: (dirty: boolean) => void;
}
function errorMessage(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (error && typeof error === 'object' && 'error' in error && typeof error.error === 'string') return error.error;
  return 'Requirement coverage could not be updated. Reload and verify your system assignment.';
}

export default function RequirementCoveragePanel({ systemId, controlId, kind, onNavigate, onChanged, onDirtyChange }: Props) {
  const [data, setData] = useState<RequirementCoverageDetail | null>(null);
  const [responses, setResponses] = useState<RequirementResponse[]>([]);
  const [parameters, setParameters] = useState<Record<string, string>>({});
  const [evidence, setEvidence] = useState<EvidenceArtifactDto[]>([]);
  const [evidenceSearch, setEvidenceSearch] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [revision, setRevision] = useState(0);
  const [enhancement, setEnhancement] = useState('');
  const [rationale, setRationale] = useState('');
  const [draft, setDraft] = useState('');
  const [reviewNotes, setReviewNotes] = useState<Record<string, string>>({});
  const responseKind = kind === 'policy' ? 'Policy' : 'Technical';
  const hasChanges = dirty || Boolean(rationale || draft || Object.values(reviewNotes).some(Boolean));
  useEffect(() => { onDirtyChange?.(hasChanges); }, [hasChanges, onDirtyChange]);
  useEffect(() => {
    const warn = (event: BeforeUnloadEvent) => { if (hasChanges) event.preventDefault(); };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [hasChanges]);

  const apply = (value: RequirementCoverageDetail) => {
    setData(value); setResponses(value.requirements.flatMap(item => item.responses));
    setParameters(value.parameterValues); setDirty(false);
  };
  useEffect(() => {
    const controller = new AbortController();
    setData(null); setError('');
    getRequirementCoverage(systemId, controlId, controller.signal)
      .then(value => { if (!controller.signal.aborted) apply(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(errorMessage(reason)); });
    return () => controller.abort();
  }, [systemId, controlId, revision]);
  useEffect(() => {
    if (!data || data.framework || hasChanges) return;
    const timer = window.setTimeout(() => setRevision(value => value + 1), 10_000);
    return () => window.clearTimeout(timer);
  }, [data, hasChanges]);

  const run = async (operation: () => Promise<void>) => {
    setBusy(true); setError('');
    try { await operation(); }
    catch (reason) { setError(errorMessage(reason)); }
    finally { setBusy(false); }
  };
  const navigate = (id: string) => {
    if (!hasChanges || window.confirm('Discard unsaved requirement responses?')) onNavigate(id);
  };
  const update = (id: string, patch: Partial<RequirementResponse>) => {
    setDirty(true);
    setResponses(previous => {
      const current = previous.find(r => r.statementId === id && r.kind === responseKind)
        ?? { statementId: id, kind: responseKind, response: '', evidence: [] };
      return [...previous.filter(r => !(r.statementId === id && r.kind === responseKind)), { ...current, ...patch }];
    });
  };
  const reload = async () => {
    apply(await getRequirementCoverage(systemId, controlId)); onChanged();
  };

  return <section className="cnw-statement-card cnw-requirements" aria-label="Requirements and enhancements">
    <h3>Requirements &amp; enhancements</h3>
    {error && <div role="alert" className="cnw-error">{error}
      <button type="button" onClick={() => setRevision(value => value + 1)}>Reload coverage</button></div>}
    {!data && !error && <p role="status">Loading authoritative requirements...</p>}
    {data && <>
      <small>{data.framework ? `${data.framework} · ${data.catalogVersion}` : 'Automatic catalog association'}</small>
      {data.sourceUri && <small>Source: {data.sourceUri}</small>}
      {data.parent && <button type="button" className="cnw-next-action" onClick={() => navigate(data.parent!.controlId)}>
        Parent control: {data.parent.controlId} · {data.parent.title}
      </button>}
      {data.gaps.length > 0 && <ul className="cnw-blocked">{data.gaps.map(gap => <li key={gap}>{gap}</li>)}</ul>}
      {!data.framework && <p className="cnw-action-note">
        SPIN links the source automatically using this system&apos;s recorded baseline framework.
        No catalog selection or reconciliation rationale is required here.{' '}
        <Link className="underline" to="/controls">Open catalog source management</Link>
      </p>}
      {data.requirements.map(requirement => {
        const response = responses.find(r => r.statementId === requirement.id && r.kind === responseKind);
        return <section key={requirement.id} className="cnw-requirement">
          <h4>{controlId} · {requirement.label ?? requirement.id}</h4>
          <p>{requirement.text}</p>
          <small>Source statement: {requirement.id}</small>
          <p className="cnw-action-note">{requirement.responseState === 'Missing' ? 'Response needed'
            : requirement.reviewed ? 'Reviewed coverage' : 'Draft response — review needed'}
            {requirement.evidenceGap && ' · Supporting evidence needed'}</p>
          {data.canAuthor && data.narrativeVersion !== null
            ? <label>{responseKind} response for {requirement.label ?? requirement.id}
              <textarea aria-label={`${responseKind} response for ${requirement.label ?? requirement.id}`}
                value={response?.response ?? ''} maxLength={8000} rows={3}
                onChange={event => update(requirement.id, { response: event.target.value })} />
            </label>
            : <p>{response?.response || `No ${kind} requirement response recorded.`}</p>}
          {response?.evidence.map(pin => <div key={pin.artifactId}>
            <small>Evidence: {evidence.find(item => item.id === pin.artifactId)?.fileName ?? pin.artifactId} · {pin.contentHash}</small>
            {data.canAuthor && <button type="button" onClick={() => update(requirement.id, {
              evidence: response.evidence.filter(item => item.artifactId !== pin.artifactId),
            })}>Remove evidence association</button>}
          </div>)}
          {data.canAuthor && evidence.length > 0 && <label>Supporting evidence for {requirement.label ?? requirement.id}
            <select value="" onChange={event => {
              const artifact = evidence.find(item => item.id === event.target.value);
              if (artifact && !response?.evidence.some(pin => pin.artifactId === artifact.id))
                update(requirement.id, { evidence: [...(response?.evidence ?? []), { artifactId: artifact.id, contentHash: artifact.contentHash }] });
            }}>
              <option value="">Select an evidence artifact</option>
              {evidence.map(item => <option key={item.id} value={item.id}>{item.fileName}</option>)}
            </select>
          </label>}
        </section>;
      })}
      {data.parameters.map(parameter => <div key={parameter.id}>
        <details><summary>Parameter source: {parameter.id}</summary><p>{parameter.definition}</p></details>
        {data.canAuthor ? <label>Recorded value for {parameter.id}
          <input value={parameters[parameter.id] ?? ''} maxLength={2000} onChange={event => {
            setDirty(true); setParameters(previous => ({ ...previous, [parameter.id]: event.target.value }));
          }} />
        </label> : <p>{parameters[parameter.id] || 'Organization-defined value not recorded.'}</p>}
      </div>)}
      {data.canAuthor && data.narrativeVersion !== null && data.requirements.length > 0 && <>
        <label>Find supporting evidence
          <input value={evidenceSearch} onChange={event => setEvidenceSearch(event.target.value)} />
        </label>
        <button type="button" disabled={busy} onClick={() => run(async () => {
          const page = await listEvidence({ systemId, page: 1, pageSize: 50, search: evidenceSearch || undefined });
          setEvidence(page.items);
        })}>Find evidence</button>
        <small>Up to 50 results. Refine the search to locate another artifact; attachment alone is not reviewed coverage.</small>
        <button type="button" disabled={busy || !dirty} onClick={() => run(async () => {
          const saved = await saveRequirementResponses(systemId, controlId, {
            expectedVersion: data.narrativeVersion!,
            responses: responses.filter(response => response.response.trim()),
            parameters: Object.fromEntries(Object.entries(parameters).filter(([, value]) => value.trim())),
          });
          apply(saved); onChanged();
        })}>Save requirement responses</button>
      </>}
      {data.canReview && data.narrativeVersion !== null && <button type="button" disabled={busy || dirty || data.gaps.length > 0}
        onClick={() => run(async () => { apply(await reviewRequirementResponses(systemId, controlId, data.narrativeVersion!)); onChanged(); })}>
        Review requirement coverage
      </button>}
      {data.enhancements.map(item => <div key={item.controlId} className="cnw-requirement">
        {item.selected || item.hasNarrative ? <button type="button" className="cnw-next-action" onClick={() => navigate(item.controlId)}>
          {item.controlId} · {item.title}
        </button> : <strong>{item.controlId} · {item.title}</strong>}
        <small>{item.selected ? `Selected · ${item.hasNarrative ? 'Separate narrative available' : 'narrative missing'}`
          : `Catalog available · Not selected${item.hasNarrative ? ' · Existing narrative needs reconciliation' : ''}`}</small>
        {!item.selected && data.canAuthor && <button type="button" onClick={() => setEnhancement(item.controlId)}>Propose enhancement {item.controlId}</button>}
      </div>)}
      {enhancement && data.canAuthor && <fieldset disabled={busy}>
        <legend>Propose {enhancement}</legend>
        <label>Required rationale<textarea value={rationale} maxLength={2000} onChange={event => setRationale(event.target.value)} /></label>
        <label>Separate {kind} draft<textarea value={draft} maxLength={8000} onChange={event => setDraft(event.target.value)} /></label>
        <p>Selection stays unchanged until a different authorized reviewer accepts. Narrative approval remains separate.</p>
        <button type="button" disabled={!rationale.trim() || !draft.trim()} onClick={() => run(async () => {
          await proposeEnhancement(systemId, { parentControlId: controlId, controlId: enhancement,
            expectedBaselineRevision: data.baselineRevision, rationale,
            policyDraft: kind === 'policy' ? draft : null, technicalDraft: kind === 'technical' ? draft : null });
          setEnhancement(''); setRationale(''); setDraft(''); await reload();
        })}>Submit enhancement proposal</button>
      </fieldset>}
      {data.proposals.map(proposal => <section key={proposal.id} className="cnw-requirement">
        <h4>{proposal.controlId} · {proposal.status} selection</h4>
        <p>{proposal.rationale}</p>
        <p>Policy draft: {proposal.policyDraft || 'Not recorded'}</p>
        <p>Technical draft: {proposal.technicalDraft || 'Not recorded'}</p>
        <small>Proposed by {proposal.createdBy} · {proposal.createdAt}</small>
        {proposal.reviewedBy && <small>Reviewed by {proposal.reviewedBy} · {proposal.reviewedAt ?? 'Review time not recorded'}</small>}
        {proposal.reviewNote && <p>Review note: {proposal.reviewNote}</p>}
        {proposal.canAccept && <button type="button" disabled={busy || dirty} onClick={() => run(async () => {
          await acceptEnhancement(systemId, proposal.id, proposal.revision); await reload();
        })}>Accept {proposal.controlId} selection (keep narrative Draft)</button>}
        {proposal.canAccept && <>
          <label>Revision note for {proposal.controlId}<textarea maxLength={2000} value={reviewNotes[proposal.id] ?? ''}
            onChange={event => setReviewNotes(previous => ({ ...previous, [proposal.id]: event.target.value }))} /></label>
          <button type="button" disabled={busy || dirty || !reviewNotes[proposal.id]?.trim()} onClick={() => run(async () => {
            await returnEnhancement(systemId, proposal.id, proposal.revision, reviewNotes[proposal.id]!); await reload();
          })}>Request revision for {proposal.controlId}</button>
        </>}
      </section>)}
      <small>Reviewed documentation coverage is not a control effectiveness finding or authorization decision.</small>
    </>}
  </section>;
}
