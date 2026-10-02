import type { useResponsibilityFirstPass } from './useResponsibilityFirstPass';
import { buttonClass, errorClass, secondaryButtonClass, warningClass } from '../features/workspace-operations/workspaceUi';
import { Link } from '../features/workspaces/workspaceNavigation';
import { responsibilityFields } from '../api/responsibilityDrafts';

export default function ResponsibilityFirstPass({ state, compact = false, hideFailure = false }: { state: ReturnType<typeof useResponsibilityFirstPass>; compact?: boolean; hideFailure?: boolean }) {
  const { context, saved, busy, loading } = state;
  const failure = state.error || (saved?.generationState === 'Failed' ? saved.generationError : null);
  const questions = [...new Set([...context?.questions ?? [], ...saved?.suggestion.questions ?? []])];
  const conflicts = [...new Set([...context?.conflicts ?? [], ...saved?.suggestion.conflicts ?? []])];
  return <section aria-label="Prepared first pass" className={compact ? 'review-first-pass space-y-3' : 'space-y-3 rounded border border-indigo-200 p-3 text-sm dark:border-indigo-800'}>
    {!compact && <h3 className="font-semibold">Prepared first pass</h3>}
    {loading && <p role="status">Loading saved draft and authorized sources… You can continue editing.</p>}
    {busy && <p role="status">{state.automatic ? 'Preparing duties from the recorded environment or saving your edits…' : 'Preparing or saving the proposed draft…'} Accepted responsibility is unchanged.</p>}
    {failure && !hideFailure && <p role="alert" className={errorClass}>{failure}</p>}
    {failure && <button type="button" className={secondaryButtonClass} disabled={busy || loading} onClick={state.reload}>Reload saved draft</button>}
    {context && <>
      {context.environmentScopeIssue && <p role="alert" className={warningClass}>{context.environmentScopeIssue}</p>}
      <details open={compact ? undefined : true}>
        <summary className={compact ? 'cursor-pointer' : 'sr-only'}>{state.automatic ? 'Environment context and sources' : 'Source selection and draft preparation'}</summary>
        <div className="mt-2 space-y-2">{state.automatic ? <>
          <p>Environment context: {context.scopes.find(scope => scope.id === state.scopeId)?.name
            ?? (context.environmentScopeIssue ? 'System and environment records across recorded contributions' : 'System records; no applicable provider scope recorded')}</p>
          <Link className="underline" to={`/systems/${encodeURIComponent(context.systemId)}/profile/EnvironmentAndDeployment`}>Inspect recorded environment</Link>
        </> : <label className="grid gap-1">Provider scope
        <select className="rounded border bg-white p-2 dark:bg-gray-900" disabled={busy}
          value={state.scopeId ?? ''} onChange={event => state.setScopeId(event.target.value || null)}>
          <option value="">System records only — no provider scope selected</option>
          {context.scopes.map(scope => <option key={scope.id} value={scope.id}>{scope.name}</option>)}
        </select>
      </label>}
      <p>{saved ? `${saved.status} · draft revision ${saved.revision}` : 'Source-backed first pass · not yet saved'}</p>
      {saved?.generatedAt && <p>AI prepared {new Date(saved.generatedAt).toLocaleString()}. Reviewed allocation is separate.</p>}
      {saved?.generationState === 'NotRequested' && <p>Prepared from source records; AI has not been used.</p>}
        </div>
      </details>
      {saved?.isStale && <p className={warningClass}>Relevant sources changed. Refresh and compare before confirmation.</p>}
      {conflicts.map(text => <p key={text} className="text-amber-900 dark:text-amber-200">{text}</p>)}
      {questions.length > 0 && (compact ? <details>
        <summary className="cursor-pointer">{questions.length} applicability questions need attention</summary>
        {questions.map(text => <p key={text} className="mt-2 text-amber-900 dark:text-amber-200">{text}</p>)}
      </details> : questions.map(text => <p key={text} className="text-amber-900 dark:text-amber-200">{text}</p>))}
      <div className="flex flex-wrap gap-2">
        {(!state.automatic || saved || failure) && <button type="button" className={secondaryButtonClass}
          disabled={busy || !context.canPrepare}
          onClick={() => { void state.prepare(); }}>{failure ? 'Retry preparation' : saved ? 'Refresh suggestion' : 'Prepare first pass'}</button>}
        {failure && <button type="button" className={secondaryButtonClass} disabled={busy || !context.canPrepare}
          onClick={() => { void state.prepare(false); }}>Use source records without AI</button>}
      </div>
      {saved?.status === 'ComparisonRequired' && <section aria-label="Compare refreshed suggestion" className="space-y-2 rounded border p-3">
        <h4 className="font-semibold">Compare before applying</h4>
        <p>Your corrections have not been replaced.</p>
        {responsibilityFields.filter(key => saved.suggestion.values[key].value !== state.values[key])
          .map(key => <details key={key}>
            <summary className="cursor-pointer">{key}: changed suggestion</summary>
            <p className="whitespace-pre-wrap">Your current draft: {state.values[key] || 'Not recorded'}</p>
            <p className="whitespace-pre-wrap">Suggested: {saved.suggestion.values[key].value || 'Needs confirmation'}</p>
            <p>{saved.suggestion.values[key].origin} · {saved.suggestion.values[key].explanation}</p>
          </details>)}
        <div className="flex flex-wrap gap-2">
          <button type="button" className={buttonClass} disabled={busy || !context.canPrepare}
            onClick={() => { void state.save(true, true); }}>Apply refreshed suggestion</button>
          <button type="button" className={secondaryButtonClass} disabled={busy || !context.canPrepare}
            onClick={() => { void state.save(true); }}>Keep my edits after comparison</button>
        </div>
      </section>}
      <details><summary className="cursor-pointer font-medium">First-pass sources & provenance</summary>
        {(saved?.sources ?? context.sources).map(source => <details key={`${source.id}:${source.version}`} className="mt-2">
          <summary className="cursor-pointer">{source.title} · {source.origin}</summary>
          {source.href && <Link className="underline" to={source.href}>Open existing record</Link>}
          <p className="break-all text-xs">Version fingerprint: {source.version}</p>
          <pre className="whitespace-pre-wrap break-words text-xs">{source.content}</pre>
        </details>)}
      </details>
      {saved && <details><summary className="cursor-pointer">Draft and review history (latest 20)</summary>
        <ul>{saved.history.map(event => <li key={event.id}>{event.action} · {event.actor} · {new Date(event.at).toLocaleString()} · revision {event.revision}</li>)}</ul>
      </details>}
    </>}
  </section>;
}
