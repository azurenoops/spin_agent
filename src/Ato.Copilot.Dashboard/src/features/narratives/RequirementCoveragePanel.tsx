import { useEffect, useRef, useState } from 'react';
import {
  acceptEnhancement, getRequirementCoverage,
  proposeEnhancement, reviewRequirementResponses, saveRequirementResponses, returnEnhancement,
  type RequirementCoverageDetail, type RequirementResponse,
  generateRequirementFirstPass, type RequirementFirstPass,
} from '../../api/requirementCoverage';
import { listEvidence } from '../../api/evidence';
import type { EvidenceArtifactDto } from '../../types/evidence';
import { Link } from '../workspaces/workspaceNavigation';
import { parameterName, parameterPresentation, readableRequirement } from './requirementPresentation';

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
  const [firstPass, setFirstPass] = useState<RequirementFirstPass | null>(null);
  const [firstPassError, setFirstPassError] = useState('');
  const [firstPassBusy, setFirstPassBusy] = useState(false);
  const [firstPassAttempts, setFirstPassAttempts] = useState<Record<string, number>>({});
  const [firstPassTokens, setFirstPassTokens] = useState<Record<string, string>>({});
  const [firstPassMessage, setFirstPassMessage] = useState('');
  const requested = useRef(new Set<string>());
  const responseKind: RequirementResponse['kind'] = kind === 'policy' ? 'Policy' : 'Technical';
  const firstPassKey = `${systemId}:${controlId}:${responseKind}`;
  const firstPassAttempt = firstPassAttempts[firstPassKey] ?? 0;
  const hasChanges = dirty || Boolean(rationale || draft || Object.values(reviewNotes).some(Boolean));
  useEffect(() => { onDirtyChange?.(hasChanges); }, [hasChanges, onDirtyChange]);
  useEffect(() => {
    const warn = (event: BeforeUnloadEvent) => { if (hasChanges) event.preventDefault(); };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [hasChanges]);

  const apply = (value: RequirementCoverageDetail) => {
    setData(value); setResponses(value.requirements.flatMap(item => item.responses));
    setParameters(value.parameterValues); setDirty(false); setFirstPass(null); setFirstPassTokens({}); setFirstPassBusy(false); setFirstPassMessage('');
  };
  useEffect(() => {
    const controller = new AbortController();
    setData(null); setError(''); setFirstPass(null); setFirstPassError(''); setFirstPassBusy(false); setFirstPassTokens({});
    getRequirementCoverage(systemId, controlId, controller.signal)
      .then(value => { if (!controller.signal.aborted) apply(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(errorMessage(reason)); });
    return () => controller.abort();
  }, [systemId, controlId, revision]);
  useEffect(() => {
    setFirstPass(null); setFirstPassError(''); setFirstPassMessage(''); setFirstPassBusy(false);
  }, [systemId, controlId, responseKind]);
  useEffect(() => {
    if (!data || data.systemId !== systemId || data.controlId !== controlId || !data.canAuthor || data.narrativeVersion === null
      || !data.framework || data.requirements.length === 0) return;
    const missing = data.requirements.some(r => !r.responses.some(response => response.kind === responseKind && response.response.trim()))
      || data.parameters.some(p => !data.parameterValues[p.id]?.trim());
    const assisted = data.firstPass?.kind === responseKind || data.firstPasses?.some(p => p.kind === responseKind);
    if (firstPassAttempt === 0 && (!missing || assisted || hasChanges)) return;
    const key = `${systemId}:${controlId}:${responseKind}:${data.narrativeVersion}:${data.baselineRevision}:${firstPassAttempt}`;
    if (requested.current.has(key)) return;
    requested.current.add(key);
    const controller = new AbortController();
    let complete = false;
    setFirstPassBusy(true); setFirstPassError('');
    generateRequirementFirstPass(systemId, controlId, { expectedVersion: data.narrativeVersion,
      expectedBaselineRevision: data.baselineRevision, kind: responseKind }, controller.signal)
      .then(value => {
        if (controller.signal.aborted) return;
        if (value.systemId !== systemId || value.controlId !== controlId || value.kind !== responseKind || value.expectedVersion !== data.narrativeVersion
          || value.responses.some(r => !data.requirements.some(q => q.id === r.statementId))
          || value.parameters.some(p => !data.parameters.some(q => q.id === p.parameterId)))
          throw new Error('AI suggestions do not match this control’s source requirements. No suggestions were applied.');
        setFirstPass(value);
      })
      .catch(reason => { if (!controller.signal.aborted) setFirstPassError(errorMessage(reason)); })
      .finally(() => { complete = true; if (!controller.signal.aborted) setFirstPassBusy(false); });
    return () => { controller.abort(); if (!complete) requested.current.delete(key); };
    // Typing does not cancel an in-flight preview; applying it still preserves all nonempty fields.
  }, [data, systemId, controlId, responseKind, firstPassAttempt]);
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
  const useFirstPass = () => {
    if (!firstPass || !data?.canAuthor || firstPassBusy || firstPass.expectedVersion !== data.narrativeVersion || firstPass.kind !== responseKind) {
      setFirstPassError('This suggestion is no longer available for the current control and statement type. Prepare a new first pass.');
      return;
    }
    const additions = firstPass.responses.filter(draft => !responses.some(r => r.statementId === draft.statementId
      && r.kind === responseKind && r.response.trim())).map((draft): RequirementResponse => ({
        statementId: draft.statementId, kind: responseKind, response: draft.response,
        evidence: responses.find(r => r.statementId === draft.statementId && r.kind === responseKind)?.evidence ?? [],
      }));
    const values = firstPass.parameters.filter(draft => !parameters[draft.parameterId]?.trim());
    if (!additions.length && !values.length) {
      setFirstPassMessage('No empty fields were available. Your existing answers were preserved.');
      return;
    }
    setResponses(previous => [...previous.filter(r => !additions.some(a => a.statementId === r.statementId && a.kind === r.kind)), ...additions]);
    setParameters(previous => ({ ...previous, ...Object.fromEntries(values.map(p => [p.parameterId, p.value])) }));
    setFirstPassTokens(previous => ({ ...previous, [responseKind]: firstPass.token })); setDirty(true);
    setFirstPassMessage(`Added ${additions.length} response and ${values.length} parameter suggestions locally. Save and review are still separate.`);
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
      {data.canAuthor && data.narrativeVersion !== null && data.requirements.length > 0 && <section aria-label="AI requirement first pass" className="cnw-first-pass">
        <h4>First pass from your system records</h4>
        <p className="cnw-action-note">AI prepares suggestions from saved system information. Review the basis and remaining questions; nothing is saved, approved or marked satisfied automatically.</p>
        {firstPassBusy && <p role="status">Preparing requirement responses from recorded system information…</p>}
        {firstPassError && <p role="alert" className="cnw-error">{firstPassError} Your entered text is unchanged; manual drafting is still available.</p>}
        {firstPassMessage && <p role="status">{firstPassMessage}</p>}
        <button type="button" disabled={firstPassBusy || busy || !data.framework} onClick={() => setFirstPassAttempts(previous =>
          ({ ...previous, [firstPassKey]: (previous[firstPassKey] ?? 0) + 1 }))}>
          {firstPassError ? 'Retry first pass' : firstPass ? 'Refresh first pass from records' : 'Prepare AI first pass'}
        </button>
        {firstPass && <>
          {firstPass.questions.length > 0 && <div><h5>Information still needed</h5><ul>{firstPass.questions.map((q, i) => <li key={i}>{q}</li>)}</ul></div>}
          {firstPass.conflicts.length > 0 && <div><h5>Sources to reconcile</h5><ul>{firstPass.conflicts.map((q, i) => <li key={i}>{q}</li>)}</ul></div>}
          <button type="button" disabled={firstPassBusy || busy || !firstPass.responses.length && !firstPass.parameters.length}
            onClick={useFirstPass}>Use first pass in empty fields</button>
          <details><summary>Sources used for this first pass</summary><ul>{firstPass.sources.map(s => <li key={s.id}>
            {s.title} · {s.reviewState} · version {s.version}<small>Source: {s.id} · SHA-256: {s.contentHash}</small>
          </li>)}</ul></details>
        </>}
      </section>}
      {!data.framework && <p className="cnw-action-note">
        SPIN links the source automatically using this system&apos;s recorded baseline framework.
        No catalog selection or reconciliation rationale is required here.{' '}
        <Link className="underline" to="/controls">Open catalog source management</Link>
      </p>}
      {data.requirements.map(requirement => {
        const response = responses.find(r => r.statementId === requirement.id && r.kind === responseKind);
        return <section key={requirement.id} className="cnw-requirement">
          <h4>{controlId} · {requirement.label ?? requirement.id}</h4>
          <p>{readableRequirement(requirement.text, data.parameters, parameters)}</p>
          <details><summary>View original requirement source</summary><small>Source statement: {requirement.id}</small>
            {readableRequirement(requirement.text, data.parameters, parameters) !== requirement.text && <p>{requirement.text}</p>}
          </details>
          {firstPass?.responses.filter(r => r.statementId === requirement.id).map(r => <div key={r.statementId} className="cnw-first-pass-suggestion">
            <strong>AI-proposed {kind} response — needs review</strong><p>{r.response}</p>
            <details><summary>Why this was suggested</summary><p>{r.explanation}</p><ul>{r.sourceIds.map(id =>
              <li key={id}>{firstPass.sources.find(s => s.id === id)?.title ?? id}</li>)}</ul></details>
          </div>)}
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
      {data.parameters.map((parameter, index) => <div key={parameter.id}>
        <h4>{parameterName(parameter, index)}</h4>
        {parameterPresentation(parameter, index).warning && <p className="cnw-action-note">{parameterPresentation(parameter, index).warning}</p>}
        <details><summary>Parameter source: {parameter.id}</summary><p>{parameter.definition}</p></details>
        {firstPass?.parameters.filter(p => p.parameterId === parameter.id).map(p => <div key={p.parameterId} className="cnw-first-pass-suggestion">
          <strong>AI-proposed value — needs review</strong><p>{p.value}</p><p>{p.explanation}</p>
          <small>Basis: {p.sourceIds.map(id => firstPass.sources.find(s => s.id === id)?.title ?? id).join(', ')}</small>
        </div>)}
        {data.canAuthor ? <label>Recorded value: {parameterName(parameter, index)} (parameter {index + 1})
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
          const proofs = Object.values(firstPassTokens);
          const saved = await saveRequirementResponses(systemId, controlId, {
            expectedVersion: data.narrativeVersion!,
            responses: responses.filter(response => response.response.trim()),
            parameters: Object.fromEntries(Object.entries(parameters).filter(([, value]) => value.trim())),
            ...(proofs.length === 1 ? { firstPassToken: proofs[0]! } : proofs.length > 1 ? { firstPassTokens: proofs } : {}),
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
