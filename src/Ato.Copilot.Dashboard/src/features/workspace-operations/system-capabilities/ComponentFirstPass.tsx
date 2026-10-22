import { useEffect, useRef, useState } from 'react';
import {
  getResponsibilityDraft, prepareResponsibilityDraft, type DraftSource, type ResponsibilityDraftContext,
} from '../../../api/responsibilityDrafts';
import { errorClass, inputClass, message, secondaryButtonClass, useRemote, warningClass } from '../workspaceUi';
import type { SystemCapabilityDetail } from './systemCapabilityTypes';

function sourceTexts(content: string): string[] {
  const strings = (value: unknown): string[] => typeof value === 'string' ? [value]
    : Array.isArray(value) ? value.flatMap(strings)
    : value && typeof value === 'object' ? Object.entries(value).flatMap(([key, field]) => {
      const objective = key.match(/^(RPO|RTO)(Hours?|Minutes?|Days?)$/i);
      if (objective && (typeof field === 'number' && Number.isFinite(field) || typeof field === 'string' && /^\d+(?:\.\d+)?$/.test(field)))
        return [`${objective[1]} ${field} ${objective[2]}`];
      if (/^(RPO|RTO|RecoveryPointObjective|RecoveryTimeObjective)$/i.test(key) && typeof field === 'string')
        return [`${key} ${field}`];
      return strings(field);
    }) : [];
  try { return strings(JSON.parse(content)); }
  catch (reason) {
    if (reason instanceof SyntaxError) return [content];
    throw reason;
  }
}
function recoveryTargets(sources: DraftSource[]) {
  return sources.filter(source => source.origin === 'From provider source').flatMap(source =>
    sourceTexts(source.content).flatMap(text => [...text.matchAll(/\b(RPO|RTO|recovery point objective|recovery time objective)\b(?:\s*\((?:RPO|RTO)\))?\s*(?:target|objective)?\s*(?:of|is|:|=)?\s*(\d+(?:\.\d+)?)\s*-?\s*(hours?|hrs?|h|days?|d|minutes?|mins?|m)\b/gi)].map(match => ({
      kind: /RPO|point/i.test(match[1] ?? '') ? 'RPO' : 'RTO',
      value: `${match[2]} ${/^h/i.test(match[3] ?? '') ? Number(match[2]) === 1 ? 'hour' : 'hours'
        : /^d/i.test(match[3] ?? '') ? Number(match[2]) === 1 ? 'day' : 'days' : Number(match[2]) === 1 ? 'minute' : 'minutes'}`, source, quote: match[0],
    }))));
}

export default function ComponentFirstPass(props: {
  data: SystemCapabilityDetail; systemId: string; canUse: boolean; currentUsage: string;
  onBusyChange: (busy: boolean) => void; onUse: (wording: string, reference: { draftId: string; revision: number }) => void;
}) {
  const controls = props.data.item.controlIds;
  const [controlId, setControlId] = useState(controls[0] ?? '');
  if (!controls.length) return <p className="text-gray-600 dark:text-gray-300">A baseline control and supporting capability are required for source-pinned AI preparation. No system usage has been inferred.</p>;
  return <FirstPass key={`${props.systemId}:${controlId}`} {...props} controlId={controlId}
    onControl={setControlId} controls={controls} />;
}

function FirstPass({ data, systemId, controlId, controls, onControl, canUse, currentUsage, onBusyChange, onUse }: {
  data: SystemCapabilityDetail; systemId: string; controlId: string; controls: string[]; onControl: (value: string) => void;
  canUse: boolean; currentUsage: string; onBusyChange: (busy: boolean) => void;
  onUse: (wording: string, reference: { draftId: string; revision: number }) => void;
}) {
  const [scopeId, setScopeId] = useState<string | null>(null);
  const remote = useRemote(signal => getResponsibilityDraft(systemId, controlId, scopeId, signal), [systemId, controlId, scopeId]);
  const [prepared, setPrepared] = useState<ResponsibilityDraftContext | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [replace, setReplace] = useState(false);
  const pending = useRef<AbortController | null>(null);
  useEffect(() => {
    setPrepared(null); setReplace(false); setError('');
  }, [scopeId]);
  useEffect(() => () => { pending.current?.abort(); onBusyChange(false); }, [onBusyChange]);
  const context = prepared ?? remote.data;
  const draft = context?.draft;
  const proposal = draft?.suggestion.values.scope.origin === 'AI proposed' ? draft.suggestion.values.scope
    : draft?.suggestion.values.customer;
  const sources = context?.sources ?? [];
  const targets = scopeId ? recoveryTargets(sources) : [];
  async function prepare() {
    if (!context?.canPrepare || busy || pending.current || remote.loading) return;
    const controller = new AbortController();
    pending.current = controller; setBusy(true); onBusyChange(true); setError(''); setReplace(false);
    try {
      const next = await prepareResponsibilityDraft(systemId, controlId, scopeId, draft?.revision ?? 0, true, controller.signal);
      if (!controller.signal.aborted) setPrepared(next);
    } catch (reason) {
      if (!controller.signal.aborted) setError(message(reason));
    } finally {
      pending.current = null;
      if (!controller.signal.aborted) { setBusy(false); onBusyChange(false); }
    }
  }
  const failed = error || remote.error || (draft?.generationState === 'Failed'
    ? draft.generationError || 'AI preparation failed. Source facts remain available; your wording is unchanged.' : '');
  const usable = !!proposal?.value.trim() && canUse && !busy && !remote.loading && !failed && !draft?.isStale
    && (!currentUsage.trim() || replace);
  return <section aria-label="Component first pass" className="min-w-0 space-y-3 rounded border border-indigo-200 p-4 text-sm dark:border-indigo-800">
    <h3 className="font-semibold">Prepare a first pass</h3>
    <p>Use selected published provider facts, or system records and attached evidence when no provider scope is selected. Suggestions do not verify deployment, protected workloads or successful recovery.</p>
    <label className="grid gap-1">Control context<select className={inputClass} value={controlId} disabled={busy}
      onChange={event => onControl(event.target.value)}>{controls.map(control => <option key={control} value={control}>{control}</option>)}</select></label>
    {remote.loading && <p role="status">Loading source context…</p>}
    {failed && <p role="alert" className={errorClass}>{failed}</p>}
    {remote.error && <button type="button" className={secondaryButtonClass} onClick={remote.retry}>Retry source context</button>}
    {context && <>
      <label className="grid gap-1">Provider scope<select className={`${inputClass} w-full min-w-0`} value={scopeId ?? ''} disabled={busy}
        onChange={event => { setPrepared(null); setScopeId(event.target.value || null); }}>
        <option value="">System records only — no provider scope selected</option>
        {context.scopes.map(scope => <option key={scope.id} value={scope.id}>{scope.name}</option>)}
      </select></label>
      {context.scopes.find(scope => scope.id === scopeId)?.reviewRequired && <p className={warningClass}>The selected provider scope requires applicability review.</p>}
      {scopeId && <section aria-label="Published provider facts" className="space-y-2">
        <h4 className="font-semibold">From the selected provider source</h4>
        {(['provider', 'allocation', 'providerDuties', 'customer', 'scope', 'exclusions'] as const)
          .filter(field => context.sourceValues[field].origin === 'From provider source' && context.sourceValues[field].value)
          .map(field => <details key={field}><summary className="cursor-pointer">{field === 'allocation' ? 'Published responsibility split' : field === 'providerDuties' ? 'Provider duties' : field === 'customer' ? 'Customer duties' : field}</summary>
            <p className="whitespace-pre-wrap break-words">{context.sourceValues[field].value}</p><p className="text-xs">{context.sourceValues[field].explanation}</p></details>)}
        {targets.length > 0 ? <section aria-label="Provider recovery targets" className="space-y-2">
          <h4 className="font-semibold">Selected provider source recovery targets</h4><p>Results not verified</p>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">{targets.map((target, index) => <div key={`${target.source.id}:${index}`} className="rounded bg-gray-50 p-3 dark:bg-gray-800">
            <strong className="block text-xl">{target.value}</strong>
            <p>{target.kind === 'RPO' ? 'Maximum data loss target (RPO)' : 'Time to restore target (RTO)'}</p>
            <p className="text-xs">{target.source.title}: {target.quote}</p>
          </div>)}</div>
        </section> : <p>No explicit RPO or RTO target is available in the selected source.</p>}
      </section>}
      <button type="button" className={secondaryButtonClass} disabled={busy || !context.canPrepare || remote.loading}
        onClick={() => { void prepare(); }}>{busy ? 'Preparing…' : 'Prepare first pass'}</button>
      {!context.canPrepare && <p>Preparation is read-only for your current permissions.</p>}
      {proposal?.value && <section aria-label="Proposed system usage" className="space-y-2">
        <h4 className="font-semibold">Proposed usage wording</h4>
        <p>{proposal.origin} · review before use</p><p className="whitespace-pre-wrap">{proposal.value}</p>
        <p className="text-xs">{proposal.explanation}</p><p className="break-all text-xs">Source references: {proposal.sourceIds.join(', ') || 'Not recorded'} · {proposal.sourceHash}</p>
        {currentUsage.trim() && <><p className="whitespace-pre-wrap">Your current wording: {currentUsage}</p>
          <label className="flex gap-2"><input type="checkbox" checked={replace} onChange={event => setReplace(event.target.checked)} />Replace my current wording with this proposal</label></>}
        <button type="button" className={secondaryButtonClass} disabled={!usable} onClick={() => {
          if (usable && proposal && draft) { onUse(proposal.value, { draftId: draft.id, revision: draft.revision }); setReplace(false); }
        }}>Use proposed wording</button>
        <p>This copies wording only. It does not choose a system area, save scope or accept responsibility.</p>
      </section>}
      {draft?.isStale && <p className={warningClass}>The saved proposal has stale sources. Refresh and compare; your corrections remain unchanged.</p>}
      {[...new Set([...context.questions, ...context.conflicts, ...(draft?.suggestion.questions ?? []), ...(draft?.suggestion.conflicts ?? [])])]
        .map(question => <p key={question} className="text-amber-900 dark:text-amber-200">{question}</p>)}
      <details><summary className="cursor-pointer font-medium text-indigo-700 dark:text-indigo-300">Published source and proposal provenance</summary>
        <div className="mt-2 space-y-3">{sources.map(source => <details key={`${source.id}:${source.version}`}>
          <summary className="cursor-pointer">{source.title} · {source.origin}</summary>
          <p className="break-all text-xs">{source.version}</p><pre className="whitespace-pre-wrap break-words text-xs">{source.content}</pre>
          {source.href && <a className="text-indigo-700 underline dark:text-indigo-300" href={source.href}>Open source record</a>}
        </details>)}</div>
        <p>Complete source is read-only. Accepted responsibilities and provider releases are not replaced by this proposal.</p>
        {data.item.source === 'provider' && <p>The component library description and the selected scope&apos;s pinned published source may differ. Review their versions separately.</p>}
      </details>
    </>}
  </section>;
}
