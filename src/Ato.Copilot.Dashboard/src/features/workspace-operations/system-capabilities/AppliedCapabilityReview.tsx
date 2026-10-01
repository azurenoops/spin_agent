import { Link } from '../../workspaces/workspaceNavigation';
import { buttonClass, moveTabFocus, secondaryButtonClass } from '../workspaceUi';
import type { SystemCapabilityDetail, SystemCapabilityPlacement, SystemCapabilitySource } from './systemCapabilityTypes';
import { responsibilityLabel, responsibilityNeedsReview } from './responsibilityPresentation';
import ResponsibilityDraftEditor, { type ResponsibilityDraftEdits } from './ResponsibilityDraftEditor';
import { getResponsibilityDraft } from '../../../api/responsibilityDrafts';
import { useRemote } from '../workspaceUi';

const inScope = (placements: SystemCapabilityPlacement[]) =>
  placements.some(placement => placement.state === 'InScope' || placement.state === 'SystemWide');
const sectionClass = 'min-w-0 space-y-3 text-sm';

export interface AppliedReviewNavigation { section: string; controlId: string }
export default function AppliedCapabilityReview({ data, tenantId, systemId, onManageComponent, edits, navigation, onNavigate, onChanged }: {
  data: SystemCapabilityDetail; tenantId: string; systemId: string; edits: ResponsibilityDraftEdits;
  navigation: AppliedReviewNavigation; onNavigate: (state: AppliedReviewNavigation) => void; onChanged: () => void;
  onManageComponent: (id: string, source: SystemCapabilitySource) => void;
}) {
  const section = navigation.section;
  const setSection = (value: string) => onNavigate({ ...navigation, section: value });
  const controlId = data.controls.some(control => control.controlId === navigation.controlId) ? navigation.controlId
    : data.controls.find(control => responsibilityNeedsReview(control.reviewState))?.controlId ?? data.controls[0]?.controlId ?? '';
  const setControlId = (value: string) => onNavigate({ ...navigation, controlId: value });
  const item = data.item;
  const selected = data.controls.find(control => control.controlId === controlId);
  const gaps = item.components.filter(component => !inScope(component.placements)).length;
  const pending = data.controls.filter(control => responsibilityNeedsReview(control.reviewState)).length;
  const draftContexts = useRemote(async signal => {
    const eligible = data.controls.filter(control => !['MissingBaseline', 'OutsideBaseline', 'Inactive'].includes(control.reviewState));
    const contexts = await Promise.all(eligible.map(async control => {
      const initial = await getResponsibilityDraft(systemId, control.controlId, null, signal);
      return [initial, ...await Promise.all(initial.scopes.map(scope =>
        getResponsibilityDraft(systemId, control.controlId, scope.id, signal)))];
    }));
    return contexts.flat();
  }, [tenantId, systemId, item.source, item.recordId, item.sourceRevision, data.controls.map(control => control.controlId).join(',')]);
  const drafts = draftContexts.data?.flatMap(context => context.draft ? [{ ...context.draft, controlId: context.controlId }] : []) ?? [];
  const draftReviews = drafts.filter(draft => draft.status !== 'Accepted' || draft.isStale).length;
  const controlsWithoutDraft = data.controls.length - new Set(drafts.map(draft => draft.controlId)).size;
  const base = `/systems/${encodeURIComponent(systemId)}/security-capabilities/${item.source}/${encodeURIComponent(item.recordId)}`;
  const context = `&control=${encodeURIComponent(controlId)}`;
  const sections = [
    { id: 'overview', label: 'Overview' },
    { id: 'scope', label: 'Where it applies' },
    { id: 'responsibilities', label: 'Responsibilities' },
  ];
  return <div className="min-w-0 space-y-5 break-words">
    <header className="space-y-2">
      <h2 className="text-xl font-semibold">{item.name}</h2>
      <p className="text-sm text-gray-600 dark:text-gray-300">Provided by {item.sourceName} · {item.source === 'provider' ? 'Provider source' : 'Organization source'}</p>
    </header>
    <section aria-label="Next step" className="space-y-3 rounded-lg bg-indigo-50 p-4 text-sm dark:bg-indigo-950">
      <h3 className="font-semibold">{gaps ? 'Your next step: assign system locations'
        : pending ? 'Your next step: review responsibilities' : 'Review evidence and system applicability'}</h3>
      <p>{gaps} scope {gaps === 1 ? 'gap' : 'gaps'} · {pending} {pending === 1 ? 'responsibility needs' : 'responsibilities need'} review</p>
      {draftContexts.loading ? <p role="status">Checking saved responsibility drafts…</p> : draftContexts.error
        ? <p role="alert">Draft counts are unavailable: {draftContexts.error} <button type="button" className="underline" onClick={draftContexts.retry}>Refresh draft counts</button></p>
        : <p>{draftReviews} saved {draftReviews === 1 ? 'draft needs' : 'drafts need'} review · {controlsWithoutDraft} controls without saved drafts</p>}
      <button type="button" className={buttonClass} onClick={() => setSection(gaps ? 'scope' : 'responsibilities')}>
        {gaps ? 'Assign locations' : pending ? 'Review first responsibility' : 'Inspect responsibilities'}
      </button>
    </section>
    <div role="tablist" aria-label="Capability review sections" onKeyDown={moveTabFocus} className="flex flex-wrap gap-1 border-b border-gray-200 dark:border-gray-700">
      {sections.map(tab => <button key={tab.id} type="button" role="tab" id={`applied-review-tab-${tab.id}`}
        aria-controls={`applied-review-panel-${tab.id}`} aria-selected={section === tab.id} tabIndex={section === tab.id ? 0 : -1}
        onClick={() => setSection(tab.id)} className={`border-b-2 px-2 py-3 text-sm focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-600 ${section === tab.id ? 'border-indigo-600 font-semibold text-indigo-700 dark:text-indigo-300' : 'border-transparent'}`}>
        {tab.label}
      </button>)}
    </div>
    <div role="tabpanel" id={`applied-review-panel-${section}`} aria-labelledby={`applied-review-tab-${section}`} tabIndex={0}>
      {section === 'overview' && <section className={sectionClass}>
        <h3 className="font-semibold">What it provides</h3>
        <p className="whitespace-pre-wrap">{item.description
          ? item.description.length > 240 ? `${item.description.slice(0, 237)}...` : item.description
          : 'No source description is recorded. Ask the source owner to supply one.'}</p>
        <p>Responsibility confirmation is separate from narrative approval, control satisfaction and authorization.</p>
        <details><summary className="cursor-pointer font-medium text-indigo-700 dark:text-indigo-300">Source statement, version, evidence and limitations</summary>
          <div className="mt-3 space-y-3">
            <p className="whitespace-pre-wrap">{item.description || 'No source statement recorded.'}</p>
            <p className="break-all text-xs">Source version: {item.sourceRevision}</p>
            <p>Linked evidence: {data.evidence.length}. Evidence and narratives are reviewed in the selected system/control context.</p>
            <p>Component association does not prove deployed configuration, monitoring connectivity or verified provider coverage.</p>
            <p>Inspect recorded provider scopes, exclusions and source versions in the selected control&apos;s prepared first pass. Choosing no provider scope uses system records without assuming inheritance.</p>
            <p>Draft review and confirmed responsibility counts are separate. Reviewed drafts do not establish accepted inheritance.</p>
            <Link className="text-indigo-700 underline dark:text-indigo-300" to={`${base}?tab=evidence${context}`}>Inspect source evidence and narratives</Link>
          </div>
        </details>
      </section>}
      {section === 'scope' && <section className={sectionClass}>
        <h3 className="font-semibold">Contributing components and recorded locations</h3>
        <p>A location places a component in system scope. It does not verify deployment, monitoring access or provider coverage.</p>
        {item.components.length ? <ul className="divide-y divide-gray-200 dark:divide-gray-700">
          {item.components.map(component => {
            const assigned = inScope(component.placements);
            return <li key={`${component.source}:${component.recordId}`} className="space-y-2 py-3">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <p className="min-w-0 font-medium">{component.name}</p>
                {data.permissions.canManage && item.isApplied
                  ? <button type="button" className={secondaryButtonClass} aria-label={`${assigned ? 'Change' : 'Assign'} location for ${component.name}`}
                    onClick={() => onManageComponent(component.recordId, component.source)}>{assigned ? 'Change' : 'Assign location'}</button>
                  : <span className="text-xs text-gray-500">Read-only</span>}
              </div>
              <ul className="space-y-1">{component.placements.map((placement, index) => <li key={placement.id || index}>
                {placement.state === 'SystemWide' ? 'System-wide' : placement.state === 'Unassigned' ? 'No location assigned'
                  : `${placement.boundaryName ?? 'Unnamed boundary'}${placement.state === 'Excluded' ? ' (Excluded)' : ''}`}
              </li>)}</ul>
              {!assigned && <p className="text-amber-800 dark:text-amber-200">No in-scope system location is recorded. Assign an existing system boundary or ask an authorized system manager.</p>}
            </li>;
          })}
        </ul> : <p>No contributing components are recorded. Link components through the authorized capability setup workflow.</p>}
      </section>}
      {section === 'responsibilities' && <section className={sectionClass}>
        <h3 className="font-semibold">Review a mapped control</h3>
        <div className="flex flex-wrap gap-2" aria-label="Mapped controls">{data.controls.map(control =>
          <button type="button" key={control.controlId} aria-label={control.controlId} aria-pressed={controlId === control.controlId}
            className={controlId === control.controlId ? buttonClass : secondaryButtonClass} onClick={() => setControlId(control.controlId)}>
            {control.controlId}
            <span className="block text-xs font-normal">{responsibilityLabel(control.reviewState)}</span>
          </button>)}</div>
        {selected ? <>
          <p className="font-medium">{responsibilityLabel(selected.reviewState)}</p>
          <ResponsibilityDraftEditor tenantId={tenantId} systemId={systemId} source={item.source}
            capabilityId={item.recordId} controlId={controlId} edits={edits} onChanged={onChanged} />
          <dl className="space-y-2">
            <div><dt className="font-medium">Provider source information</dt><dd className="whitespace-pre-wrap">{selected.providerCoverage || 'No control-specific provider information is recorded.'}</dd></div>
            <div><dt className="font-medium">Recorded customer responsibility</dt><dd className="whitespace-pre-wrap">{selected.organizationDuty || 'Customer duties are not recorded. Review the source and system records before confirming.'}</dd></div>
          </dl>
          <Link className={buttonClass} to={`${base}?tab=coverage${context}`}>Review {controlId} responsibility</Link>
          <Link className={`${secondaryButtonClass} ml-0 sm:ml-2`} to={`${base}?tab=evidence${context}`}>Review evidence and narratives</Link>
          <details><summary className="cursor-pointer text-indigo-700 dark:text-indigo-300">Source and technical details</summary>
            <dl className="mt-3 space-y-2 text-xs">
              <div><dt>Available source version</dt><dd className="break-all">{selected.availableSourceRevision}</dd></div>
              <div><dt>Confirmed source version</dt><dd className="break-all">{selected.confirmedSourceRevision ?? 'Not confirmed'}</dd></div>
              <div><dt>Effective allocation</dt><dd>{selected.allocation ?? 'Not designated'}</dd></div>
            </dl>
          </details>
        </> : <p>No mapped controls are recorded. Select a baseline and verify source mappings in the full capability review.</p>}
      </section>}
    </div>
    <footer className="space-y-3 border-t border-gray-200 pt-4 text-xs dark:border-gray-700">
      {item.source === 'provider' && <p>Provider-authored source content remains read-only. Authorized changes affect only this system&apos;s application and responsibility records.</p>}
      <Link className="text-indigo-700 underline dark:text-indigo-300" to={base}>Open full capability review</Link>
    </footer>
  </div>;
}
