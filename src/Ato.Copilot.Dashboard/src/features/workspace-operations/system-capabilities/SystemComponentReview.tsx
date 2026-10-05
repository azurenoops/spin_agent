import { useEffect, useId, useRef, useState } from 'react';
import { Link } from '../../workspaces/workspaceNavigation';
import {
  getApprovedSystemDesign, getSystemDesign, saveComponentScope,
  type ComponentScopeUse, type SystemDesignGraph,
} from '../../../api/systemDesign';
import { buttonClass, errorClass, inputClass, message, moveTabFocus, secondaryButtonClass, Status, useRemote, warningClass } from '../workspaceUi';
import { getSystemCapability, getSystemComponentPlacements } from './systemCapabilityApi';
import type { SystemCapabilityDetail } from './systemCapabilityTypes';
import SystemComponentPlacements from './SystemComponentPlacements';
import ComponentFirstPass from './ComponentFirstPass';

const linkClass = 'font-medium text-indigo-700 underline-offset-2 hover:underline dark:text-indigo-300';
const sameScope = (left: ComponentScopeUse, right: ComponentScopeUse) =>
  left.source === right.source && left.componentId === right.componentId && left.sourceRevision === right.sourceRevision
  && left.decision === right.decision && left.boundaryId === right.boundaryId && left.usage === right.usage
  && JSON.stringify(left.wordingBasis ?? null) === JSON.stringify(right.wordingBasis ?? null);

export default function SystemComponentReview({ data, tenantId, systemId, onBusyChange, onChanged }: {
  data: SystemCapabilityDetail; tenantId: string; systemId: string;
  onBusyChange: (busy: boolean) => void; onChanged: () => void;
}) {
  const record = data.item;
  const id = useId();
  const [section, setSection] = useState('overview');
  const [graph, setGraph] = useState<SystemDesignGraph | null>(null);
  const [decision, setDecision] = useState<ComponentScopeUse['decision']>('NeedsConfirmation');
  const [area, setArea] = useState('');
  const [usage, setUsage] = useState('');
  const [wording, setWording] = useState<{ draftId: string; revision: number } | null>(null);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [busy, setBusy] = useState(false);
  const [preparing, setPreparing] = useState(false);
  const [placementBusy, setPlacementBusy] = useState(false);
  const [invalidated, setInvalidated] = useState(false);
  const [showPlacements, setShowPlacements] = useState(false);
  const edited = useRef(false);
  const pending = useRef<AbortController | null>(null);
  const remote = useRemote(async signal => {
    const [working, approved, placements, detail] = await Promise.all([
      getSystemDesign(systemId, signal), getApprovedSystemDesign(systemId, signal),
      getSystemComponentPlacements(tenantId, systemId, { source: record.source, recordType: 'component', recordId: record.recordId }, signal),
      getSystemCapability(tenantId, systemId, { source: record.source, recordType: 'component', recordId: record.recordId }, signal),
    ]);
    if (working.systemId !== systemId || working.tenantId.toLowerCase() !== tenantId.toLowerCase()
      || approved && (approved.graph.systemId !== systemId || approved.graph.tenantId.toLowerCase() !== tenantId.toLowerCase()))
      throw new Error('The saved scope does not match this system and workspace.');
    if (detail.item.sourceRevision !== placements.sourceRevision)
      throw new Error('The component source changed while loading. Reload current source before saving; entered data is retained.');
    return { working, approved, placements, detail };
  }, [tenantId, systemId, record.source, record.recordId]);
  const shown = remote.data?.detail ?? data;
  const item = shown.item;
  useEffect(() => {
    if (!remote.data) return;
    setGraph(remote.data.working);
    const scope = remote.data.working.componentScopes?.find(value => value.source === item.source && value.componentId === item.recordId);
    if (!edited.current) {
      setDecision(scope?.decision ?? 'NeedsConfirmation'); setArea(scope?.boundaryId ?? ''); setUsage(scope?.usage ?? '');
      setWording(scope?.wordingBasis ? { draftId: scope.wordingBasis.draftId, revision: scope.wordingBasis.revision } : null);
    }
  }, [remote.data, item.source, item.recordId]);
  useEffect(() => () => pending.current?.abort(), []);
  useEffect(() => {
    onBusyChange(busy || preparing || placementBusy);
    return () => onBusyChange(false);
  }, [busy, preparing, placementBusy, onBusyChange]);
  const current = graph?.componentScopes?.find(value => value.source === item.source && value.componentId === item.recordId);
  const baseline = remote.data?.approved?.graph.componentScopes?.find(value => value.source === item.source && value.componentId === item.recordId);
  const reviewed = !!current && !!baseline && sameScope(current, baseline);
  const stale = current && current.sourceRevision !== item.sourceRevision || graph?.sourcesStale;
  const locked = busy || preparing || placementBusy || remote.loading || !!remote.error || invalidated || !graph?.actions.canEdit;
  const title = !current ? 'System scope not recorded' : reviewed
    ? current.decision === 'Excluded' ? 'Excluded from system scope' : 'System scope reviewed'
    : current.decision === 'NeedsConfirmation' ? 'Scope draft needs confirmation' : 'Scope draft ready for review';
  const change = () => { edited.current = true; setNotice(''); };
  async function save() {
    if (locked || !graph || pending.current) return;
    setError(''); setNotice('');
    if (decision === 'Included' && !remote.data?.placements.boundaries.some(boundary => boundary.id === area)) {
      setError('Choose the system area this service supports.'); return;
    }
    if (decision === 'Included' && !item.isAvailable) {
      setError('The source is unavailable for new included use. Record exclusion or a confirmation question instead.'); return;
    }
    const controller = new AbortController();
    pending.current = controller; setBusy(true);
    try {
      const saved = await saveComponentScope(systemId, {
        expectedRevision: graph.revision, source: item.source, componentId: item.recordId,
        sourceRevision: item.sourceRevision, decision, boundaryId: decision === 'Included' ? area : null, usage,
        ...(wording ? { wordingDraftId: wording.draftId, wordingDraftRevision: wording.revision } : {}),
      }, controller.signal);
      if (controller.signal.aborted) return;
      setGraph(saved); edited.current = false;
      setNotice('Scope draft saved. Provider source, placements and reviewed baseline are unchanged.');
      onChanged();
    } catch (reason) {
      if (!controller.signal.aborted) { setError(message(reason)); setInvalidated(true); }
    } finally {
      pending.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }
  const backup = /\bbackup\b|\bbackups\b/i.test(`${item.name} ${item.description} ${item.capabilities.map(value => value.name).join(' ')}`);
  const demo = /synthetic|demonstration|\bdemo\b/i.test(item.description);
  const description = item.description.trim();
  const summary = description ? description.split(/(?<=[.!?])\s/).find(sentence => !/^(synthetic|demonstration|demo)\b/i.test(sentence))
    ?? description : 'No source-supported description is available.';
  const serviceLabel = description.match(/\bRecovery Services vault\b/i)?.[0];
  const tabs = [{ id: 'overview', name: 'Overview' }, { id: 'scope', name: 'System scope' }, { id: 'evidence', name: 'Evidence' }];
  const systemBase = `/systems/${encodeURIComponent(systemId)}`;
  return <div className="min-w-0 space-y-5 break-words text-sm">
    <header className="space-y-2">
      {demo && <span className="inline-flex rounded bg-amber-50 px-2 py-1 text-xs text-amber-900 dark:bg-amber-950 dark:text-amber-200">Synthetic demonstration source</span>}
      <h2 className="text-xl font-semibold">{item.name}</h2>
      <p className="text-gray-600 dark:text-gray-300">{serviceLabel ? `${serviceLabel} · ` : ''}{item.subType || 'Subtype not recorded'}</p>
      <p>Managed by {item.sourceName}</p>
    </header>
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {!item.isAvailable && <p className={warningClass}>The component source is no longer available for new use. Historical scope and source records remain retained.</p>}
    {graph && !remote.loading && !remote.error && <section aria-label="Next action" className="space-y-2 rounded-lg bg-indigo-50 p-4 dark:bg-indigo-950">
      <h3 className="font-semibold">{title}</h3>
      <p>{!current ? 'Confirm which part of your system uses this service.' : current.decision === 'Excluded'
        ? reviewed ? 'Reviewed exclusion: this service does not count toward in-scope coverage.' : 'Proposed exclusion. Review is required before changing the reviewed baseline.'
        : current.decision === 'NeedsConfirmation' ? 'The scope question remains open. Your notes are saved.'
        : `${reviewed ? 'Reviewed' : 'Proposed'} use: ${current.boundaryName}. ${reviewed ? 'Evidence and responsibilities remain separate.' : 'Review is still required.'}`}</p>
      {section !== 'scope' && <button type="button" className={buttonClass} onClick={() => setSection('scope')}>Review system scope</button>}
      {stale && <p className={warningClass}>Source changes affect this draft or review. Compare current source information; reviewed records are retained.</p>}
    </section>}
    <div role="tablist" aria-label="Component sections" className="flex flex-wrap border-b border-gray-200 dark:border-gray-700" onKeyDown={moveTabFocus}>
      {tabs.map(tab => <button key={tab.id} id={`${id}-${tab.id}-tab`} type="button" role="tab"
        aria-selected={section === tab.id} aria-controls={`${id}-${tab.id}-panel`} tabIndex={section === tab.id ? 0 : -1}
        onClick={() => setSection(tab.id)} className={`border-b-2 px-3 py-3 focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-600 ${section === tab.id ? 'border-indigo-600 font-semibold text-indigo-700 dark:text-indigo-300' : 'border-transparent'}`}>{tab.name}</button>)}
    </div>
    {notice && <p role="status" className="rounded bg-green-50 p-3 text-green-900 dark:bg-green-950 dark:text-green-200">{notice}</p>}
    <div role="tabpanel" id={`${id}-${section}-panel`} aria-labelledby={`${id}-${section}-tab`} tabIndex={0} className="min-w-0 space-y-4">
      {section === 'overview' && <>
        <section className="space-y-2"><h3 className="font-semibold">What this service does</h3><p>{summary.length > 280 ? `${summary.slice(0, 277)}...` : summary}</p></section>
        <section className="space-y-2"><h3 className="font-semibold">Supports</h3>
          {item.capabilities.length ? <ul className="space-y-2">{item.capabilities.map(capability => <li key={`${capability.source}:${capability.recordId}`}>
            <Link className={linkClass} to={`${systemBase}/security-capabilities/${capability.source}/${encodeURIComponent(capability.recordId)}`}>{capability.name}</Link>
          </li>)}</ul> : <p>No applied capability links this component.</p>}
        </section>
        <p className="text-gray-600 dark:text-gray-300">Provider targets are source statements, not measured results. Select a provider scope in the first pass to inspect its published facts.</p>
        <details><summary className={`cursor-pointer ${linkClass}`}>Provider source and technical details</summary>
          <div className="mt-3 space-y-3">
            <p>Source is read-only here. System-use drafts do not change provider authorship.</p>
            <p className="whitespace-pre-wrap">{item.description || 'Source description unavailable.'}</p>
            <dl className="space-y-1"><dt>Component classification</dt><dd>{item.componentType || 'Not recorded'} · {item.subType || 'Not recorded'}</dd>
              <dt>Component identifier</dt><dd className="break-all">{item.recordId}</dd><dt>Source revision</dt><dd className="break-all">{item.sourceRevision}</dd></dl>
            <Link className={linkClass} to={`/security-capabilities/${item.source}/${encodeURIComponent(item.recordId)}?recordType=component`}>Open complete source in library</Link>
          </div>
        </details>
      </>}
      {section === 'scope' && <>
        <h3 className="font-semibold">How does your system use {item.name}?</h3>
        <p>Record service use, not provider infrastructure containment.</p>
        {graph && !graph.actions.canEdit && <p className={warningClass}>Read-only: editable System design permission is required. An approved design must first have a derived draft; an under-review design must be withdrawn by an authorized editor.</p>}
        <form className="min-w-0 space-y-4" onSubmit={event => { event.preventDefault(); void save(); }}>
          <fieldset disabled={locked} className="space-y-2"><legend className="font-semibold">Scope decision</legend>
            {([{ value: 'Included', label: 'Used by this system' }, { value: 'Excluded', label: 'Not used by this system' },
              { value: 'NeedsConfirmation', label: 'Needs confirmation' }] as const).map(choice => <label key={choice.value} className="flex items-start gap-3 border-b border-gray-200 py-3 dark:border-gray-700">
                <input type="radio" name={`${id}-decision`} value={choice.value} checked={decision === choice.value}
                  onChange={() => { change(); setDecision(choice.value); }} /><span>{choice.label}</span></label>)}
          </fieldset>
          {decision === 'Included' && <label className="grid gap-1">System area supported
            <select className={`${inputClass} min-w-0 w-full`} disabled={locked} value={area} onChange={event => { change(); setArea(event.target.value); }}>
              <option value="">Choose a recorded system area</option>{remote.data?.placements.boundaries.map(boundary => <option key={boundary.id} value={boundary.id}>{boundary.name}</option>)}
            </select>
            {remote.data && !remote.data.placements.boundaries.length && <span>No system areas are recorded. Use the existing boundary workflow to record one.</span>}
          </label>}
          <label className="grid gap-1">How it is used
            <textarea aria-label="How it is used" className={`${inputClass} min-h-24 w-full min-w-0`} maxLength={2000} readOnly={locked} value={usage}
              onChange={event => { change(); setUsage(event.target.value); }} />
          </label>
          {wording && <p className="text-gray-600 dark:text-gray-300">Wording is based on a prepared responsibility proposal. Your corrections are retained for review.</p>}
          {current?.wordingBasis && <details><summary className={`cursor-pointer ${linkClass}`}>Saved wording provenance</summary>
            <div className="mt-2 space-y-2">
              <p>{current.wordingBasis.origin}{current.wordingBasis.userEdited ? ' · user corrected' : ''}</p>
              <p className="whitespace-pre-wrap">Original proposal: {current.wordingBasis.originalWording}</p>
              <p className="break-all text-xs">Proposal {current.wordingBasis.draftId} · revision {current.wordingBasis.revision} · source hash {current.wordingBasis.sourceHash}</p>
              <p className="text-xs">Source references: {current.wordingBasis.sourceIds.join(', ')}</p>
            </div>
          </details>}
          {edited.current && current && (current.usage !== usage || current.decision !== decision || (current.boundaryId ?? '') !== (decision === 'Included' ? area : '')) &&
            <details><summary className={`cursor-pointer ${linkClass}`}>Compare saved scope with your entries</summary>
              <div className="mt-2 space-y-2"><p>Saved decision: {current.decision} · {current.boundaryName || 'No area recorded'}</p>
                <p className="whitespace-pre-wrap">{current.usage || 'No saved usage description.'}</p>
                <p>Your entries above have not been replaced. Review this difference before saving a new draft.</p></div>
            </details>}
          <p>Provider infrastructure is not moved into your boundary by recording service use. Scope does not confirm responsibility allocation, inheritance, control implementation or authorization.</p>
          {error && <p role="alert" className={errorClass}>{error}</p>}
          {invalidated && <div className={warningClass}><p>Your entries are preserved. Reload saved state before retrying; compare any concurrent changes.</p>
            <button type="button" className={`${secondaryButtonClass} mt-2`} onClick={() => { setInvalidated(false); remote.retry(); }}>Reload saved scope</button></div>}
          <button type="submit" className={buttonClass} disabled={locked}>{busy ? 'Saving scope draft…' : 'Save scope draft'}</button>
        </form>
        <Link className={linkClass} to={`${systemBase}/profile/SystemDesign`}>Review scope draft</Link>
        <p className="text-gray-600 dark:text-gray-300">Scope drafts use the existing independent System design review. The reviewed baseline is preserved.</p>
        <details onToggle={event => setShowPlacements(event.currentTarget.open)}><summary className={`cursor-pointer ${linkClass}`}>Existing infrastructure placements</summary>
          <div className="mt-3 space-y-3"><p>This separate workflow changes canonical boundary assignments immediately, not the service-use draft.</p>
            {showPlacements && <SystemComponentPlacements tenantId={tenantId} systemId={systemId} source={item.source} componentId={item.recordId}
              onBusyChange={setPlacementBusy} onChanged={text => { setNotice(text); onChanged(); remote.retry(); }} />}
            <Link className={linkClass} to={`${systemBase}/boundaries`}>Open system boundaries</Link></div>
        </details>
      </>}
      {section === 'evidence' && <>
        <h3 className="font-semibold">Available evidence</h3>
        {shown.evidence.length ? <><p>Linked to the component&apos;s controls; applicability and sufficiency still require review.</p>
          <ul className="space-y-2">{shown.evidence.map(evidence => <li key={evidence.id}>
            <a className={linkClass} href={evidence.openUrl}>{evidence.fileName}</a><p className="text-xs">{evidence.controlId || 'Control not recorded'} · {evidence.narrativeType} · {evidence.state}</p>
          </li>)}</ul></> : <p>Evidence unavailable for this component.</p>}
        <h3 className="font-semibold">Documentation to review</h3>
        <ul className="divide-y divide-gray-200 dark:divide-gray-700">
          {(backup ? [
            ['Protected workloads', 'Identify actual workloads and data covered by this service; provider prose is not a protected-item inventory.'],
            ['Configuration and retention records', 'Attach current backup policies, retention and configuration records.'],
            ['Restore results', 'Attach dated restore exercise results. Successful recovery is not verified here.'],
            ['Responsibility review', 'Confirm provider and system-owner duties through the existing responsibility workflow.'],
          ] : [['System applicability', 'Document actual system use and applicable configuration.'], ['Responsibility review', 'Review the provider and system-owner duties.']]).map(([name, text]) =>
            <li key={name} className="space-y-1 py-3"><h4 className="font-medium">{name}</h4><p>{text}</p></li>)}
        </ul>
        <div className="flex flex-wrap gap-3"><Link className={linkClass} to={`${systemBase}/evidence`}>Open evidence repository</Link>
          <Link className={linkClass} to={shown.responsibilityReviewUrl}>Review responsibilities</Link></div>
        <p className="text-gray-600 dark:text-gray-300">File availability does not verify the target, control implementation or authorization.</p>
      </>}
    </div>
    <div hidden={section === 'evidence'}>
      <ComponentFirstPass data={shown} systemId={systemId} canUse={!locked} onBusyChange={setPreparing}
        onUse={(text, reference) => { change(); setUsage(text); setWording(reference); setSection('scope'); }} currentUsage={usage} />
    </div>
  </div>;
}
