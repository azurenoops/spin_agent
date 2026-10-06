import { useEffect, useRef, useState } from 'react';
import { explainOverviewGroup, overviewError, type OverviewExplanation, type OverviewWorkGroup, type OverviewAiMode } from '../../api/systemOverview';
import { getResponsibilityDraft, type ResponsibilityDraftContext } from '../../api/responsibilityDrafts';
import { Link } from '../workspaces/workspaceNavigation';
import { inputClass } from '../workspace-operations/workspaceUi';
import { systemPanel, systemSecondaryAction } from './SystemTaskPresentation';

export interface OverviewAiEdit { content: string; edited: boolean; proposal: OverviewExplanation | null; scopeId: string | null; explainedScopeId?: string | null; mode?: OverviewAiMode }
export default function OverviewAiHelp({ systemId, runId, group, edits, onClose }: {
  systemId: string; runId: string; group: OverviewWorkGroup; edits: Map<string, OverviewAiEdit>; onClose: () => void;
}) {
  const cached = edits.get(`${systemId}:${runId}:${group.id}`);
  const [content, setContent] = useState(cached?.content ?? '');
  const [proposal, setProposal] = useState<OverviewExplanation | null>(cached?.proposal ?? null);
  const [scopeId, setScopeId] = useState<string | null>(cached?.scopeId ?? null);
  const [explainedScopeId, setExplainedScopeId] = useState<string | null>(cached?.explainedScopeId ?? null);
  const [mode, setMode] = useState<OverviewAiMode>(cached?.mode ?? 'Explain');
  const [context, setContext] = useState<ResponsibilityDraftContext | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [contextError, setContextError] = useState('');
  const edited = useRef(cached?.edited ?? false);
  const [suggestion, setSuggestion] = useState<OverviewExplanation | null>(null);
  const pending = useRef<AbortController | null>(null);
  const key = `${systemId}:${runId}:${group.id}`;
  const control = group.controls[0] ?? null;
  useEffect(() => {
    edits.set(key, { content, proposal, edited: edited.current, scopeId, explainedScopeId, mode });
  }, [edits, key, content, proposal, scopeId, explainedScopeId, mode]);
  useEffect(() => {
    if (!control) return;
    const controller = new AbortController();
    setContextError('');
    getResponsibilityDraft(systemId, control, scopeId, controller.signal).then(value => {
      if (!controller.signal.aborted) setContext(value);
    }).catch(reason => { if (!controller.signal.aborted) setContextError(overviewError(reason)); });
    return () => controller.abort();
  }, [systemId, control, scopeId]);
  useEffect(() => () => pending.current?.abort(), []);
  useEffect(() => { void explain(); }, []);
  async function explain(nextMode: OverviewAiMode = mode) {
    if (pending.current) return;
    const controller = new AbortController();
    pending.current = controller; setBusy(true); setError('');
    try {
      const result = await explainOverviewGroup(systemId, runId, { groupId: group.id, controlId: control, scopeId,
        ...(nextMode === 'Explain' ? {} : { mode: nextMode }) }, controller.signal);
      if (controller.signal.aborted) return;
      if (edited.current) setSuggestion(result);
      else { setContent(result.content); setProposal(result); setSuggestion(null); }
      setExplainedScopeId(scopeId);
    } catch (reason) {
      if (!controller.signal.aborted) setError(overviewError(reason));
    } finally {
      pending.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }
  return <section aria-label="AI first-pass help" className={`${systemPanel} space-y-3`}>
    <div className="flex flex-wrap items-center justify-between gap-3"><h2 className="font-semibold">AI first pass: {group.title}</h2>
      <button type="button" className={systemSecondaryAction} disabled={busy} onClick={onClose}>Close AI help</button></div>
    <p className="text-xs">Explanation and suggested documentation focus only. No phase, source, accepted responsibility, document or evidence record is changed.</p>
    <div className="flex flex-wrap gap-2">
      <button type="button" className={systemSecondaryAction} disabled={busy || !!contextError}
        onClick={() => { setMode('SuggestNextAction'); void explain('SuggestNextAction'); }}>Suggest documentation focus with AI</button>
      {control && <>
        <button type="button" className={systemSecondaryAction} disabled={busy || !!contextError}
          onClick={() => { setMode('MapRequirements'); void explain('MapRequirements'); }}>Map existing text with AI</button>
        <button type="button" className={systemSecondaryAction} disabled={busy || !!contextError}
          onClick={() => { setMode('DraftResponses'); void explain('DraftResponses'); }}>Prepare draft responses with AI</button>
      </>}
    </div>
    {(mode === 'MapRequirements' || mode === 'DraftResponses') && <p className="text-xs">Mappings and responses are source-qualified suggestions only. Unknown catalog statements or missing evidence must remain questions; use the existing requirement workflow for reviewed application.</p>}
    {control && <label className="grid gap-1 text-sm">Provider scope for the first pass
      <select className={`${inputClass} min-w-0 w-full`} disabled={busy || !context} value={scopeId ?? ''}
        onChange={event => { setScopeId(event.target.value || null); setContext(null); }}>
        <option value="">System records and evidence — no provider scope selected</option>
        {context?.scopes.map(scope => <option key={scope.id} value={scope.id}>{scope.name}</option>)}
      </select></label>}
    {contextError && <p role="alert" className="text-sm text-red-800 dark:text-red-200">Source scope unavailable: {contextError}</p>}
    {context && scopeId && <section aria-label="Published provider facts" className="space-y-2 text-sm">
      <h3 className="font-semibold">From the selected provider source — not accepted responsibility</h3>
      {(['allocation', 'provider', 'providerDuties', 'customer', 'scope', 'exclusions'] as const)
        .filter(field => context.sourceValues[field].origin === 'From provider source' && context.sourceValues[field].value)
        .map(field => <div key={field}>
          <h4 className="text-xs font-semibold">{field === 'providerDuties' ? 'Provider duties' : field === 'customer' ? 'Customer duties'
            : field === 'allocation' ? 'Published responsibility split' : field === 'provider' ? 'Provider source'
              : field === 'scope' ? 'Published scope' : 'Published exclusions'}</h4>
          <p className="whitespace-pre-wrap">{context.sourceValues[field].value}</p>
          <p className="text-xs">{context.sourceValues[field].explanation}</p>
          <details className="text-xs"><summary className="cursor-pointer">Published fact provenance</summary>
            <p className="break-all">{context.sourceValues[field].sourceIds.join(', ')} · {context.sourceValues[field].sourceHash}</p>
          </details>
        </div>)}
    </section>}
    {busy && <p role="status" className="text-sm">Preparing a source-supported explanation…</p>}
    {error && <p role="alert" className="text-sm text-red-800 dark:text-red-200">{error}</p>}
    {proposal && <>
      <h3 className="text-sm font-semibold">AI proposed — review required</h3>
      {scopeId !== explainedScopeId && <p className="text-xs text-amber-900 dark:text-amber-200">Selected source changed. Refresh the explanation; previous wording and your corrections are retained.</p>}
      <p className="whitespace-pre-wrap text-sm">{proposal.content}</p>
      <label className="grid gap-1 text-sm">Your working explanation
        <textarea className={`${inputClass} min-h-28`} value={content} maxLength={8000}
          onChange={event => { edited.current = true; setContent(event.target.value); }} /></label>
      <p className="text-xs">Your corrections stay in this page&apos;s working view. Save any actual responses through the existing authorized workflow.</p>
      {proposal.questions.map(question => <p key={question} className="text-sm text-amber-900 dark:text-amber-200">{question}</p>)}
      <details><summary className="cursor-pointer text-sm text-indigo-700 dark:text-indigo-300">First-pass sources and versions</summary>
        <p className="break-all text-xs">Explanation-context hash: {proposal.sourceHash}</p>
        {proposal.sources.map(source => <details key={`${source.id}:${source.version}`} className="mt-3 text-xs">
          <summary className="cursor-pointer">{source.title} · {source.origin}</summary><p className="break-all">{source.version}</p>
          <pre className="whitespace-pre-wrap break-words">{source.content}</pre>
          {source.href && <Link className="underline" to={source.href}>Open source record</Link>}
        </details>)}
      </details>
    </>}
    {suggestion && <section aria-label="Compare refreshed explanation" className="space-y-2 rounded border border-slate-200 p-3 dark:border-slate-700">
      <h3 className="text-sm font-semibold">Your corrections have not been replaced</h3>
      <p className="whitespace-pre-wrap text-sm">{suggestion.content}</p>
      <button type="button" className={systemSecondaryAction} onClick={() => {
        setContent(suggestion.content); setProposal(suggestion); setSuggestion(null); edited.current = false;
      }}>Use refreshed explanation</button>
    </section>}
    <button type="button" className={systemSecondaryAction} disabled={busy || !!contextError}
      onClick={() => { void explain(); }}>{proposal ? 'Refresh explanation' : 'Retry AI first pass'}</button>
    {control && <Link className={systemSecondaryAction}
      to={`/systems/${encodeURIComponent(systemId)}/narratives?control=${encodeURIComponent(control)}&statement=policy`}>
      Map existing text and prepare requirement responses
    </Link>}
    <Link className={systemSecondaryAction} to={`/systems/${encodeURIComponent(systemId)}/inheritance/subscriptions?control=${encodeURIComponent(control ?? '')}`}>
      Prepare source-pinned responsibilities
    </Link>
    <p className="text-xs">Published provider duties remain source facts, not AI-assessed coverage or accepted inheritance. Missing facts remain questions; source review and human acceptance stay in their existing workflows.</p>
  </section>;
}
