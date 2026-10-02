import { Link } from '../../workspaces/workspaceNavigation';
import { moveTabFocus, secondaryButtonClass } from '../workspaceUi';
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
  const gaps = item.components.filter(component => !component.scopeDecision && !inScope(component.placements)
    || component.scopeDecision === 'NeedsConfirmation').length;
  const pending = data.controls.filter(control => responsibilityNeedsReview(control.reviewState)).length;
  const draftContexts = useRemote(async signal => {
    const eligible = data.controls.filter(control => !['MissingBaseline', 'OutsideBaseline', 'Inactive'].includes(control.reviewState));
    return Promise.all(eligible.map(control => getResponsibilityDraft(systemId, control.controlId, null, signal,
      { useEnvironment: true, capabilityId: item.source === 'provider' ? item.recordId : undefined })));
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
    <section aria-label="Next step" className="review-next">
      <h3>{pending || draftReviews ? 'Your next step: review responsibilities'
        : gaps ? 'Your next step: assign system locations' : 'Review evidence and system applicability'}</h3>
      <p className="review-muted">{pending} {pending === 1 ? 'responsibility needs' : 'responsibilities need'} review. Check source-backed duties and correct the system&apos;s first pass.</p>
      {draftContexts.loading ? <p role="status">Checking saved responsibility drafts…</p> : draftContexts.error
        ? <p role="alert">Draft counts are unavailable: {draftContexts.error} <button type="button" className="underline" onClick={draftContexts.retry}>Refresh draft counts</button></p>
        : <p className="text-xs review-muted">{draftReviews} saved {draftReviews === 1 ? 'draft needs' : 'drafts need'} review · {controlsWithoutDraft} {controlsWithoutDraft === 1 ? 'control without a saved draft' : 'controls without saved drafts'}</p>}
      <button type="button" className="review-primary" onClick={() => setSection(pending || draftReviews ? 'responsibilities' : gaps ? 'scope' : 'responsibilities')}>
        {pending || draftReviews ? 'Review first responsibility →' : gaps ? 'Assign locations' : 'Inspect responsibilities'}
      </button>
      {gaps > 0 && <p className="text-xs review-muted">Also needed: {gaps} scope {gaps === 1 ? 'gap' : 'gaps'}. Assign component locations in this system.</p>}
    </section>
    <div role="tablist" aria-label="Capability review sections" onKeyDown={moveTabFocus} className="review-tabs">
      {sections.map(tab => <button key={tab.id} type="button" role="tab" id={`applied-review-tab-${tab.id}`}
        aria-controls={`applied-review-panel-${tab.id}`} aria-selected={section === tab.id} tabIndex={section === tab.id ? 0 : -1}
        aria-label={tab.label} onClick={() => setSection(tab.id)}>
        {tab.label}
        {tab.id === 'scope' && <span className="review-count" aria-hidden="true">{gaps} gaps</span>}
        {tab.id === 'responsibilities' && <span className="review-count" aria-hidden="true">
          {draftContexts.loading ? 'Checking…' : draftContexts.error ? 'Unavailable' : `${draftReviews} ${draftReviews === 1 ? 'draft' : 'drafts'}`}
        </span>}
      </button>)}
    </div>
    <div role="tabpanel" id={`applied-review-panel-${section}`} aria-labelledby={`applied-review-tab-${section}`} tabIndex={0}>
      {section === 'overview' && <section className={sectionClass}>
        <h3 className="font-semibold">What this capability provides</h3>
        <p className="whitespace-pre-wrap">{item.description
          ? item.description.length > 240 ? `${item.description.slice(0, 237)}...` : item.description
          : 'No source description is recorded. Ask the source owner to supply one.'}</p>
        <p className="text-xs review-muted">{item.source === 'provider' ? 'Provider source' : 'Organization source'} · {item.sourceName}</p>
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
        <div className="review-task-row">
          <div><p className="font-medium">Where it applies</p><p>{item.components.length} components · {gaps} scope links need attention</p></div>
          <button type="button" className="review-secondary" onClick={() => setSection('scope')}>Review scope</button>
        </div>
        <div className="review-task-row">
          <div><p className="font-medium">Who does what</p><p>{data.controls.length} mapped controls · {draftContexts.loading ? 'Checking saved drafts' : draftContexts.error ? 'Draft counts unavailable' : `${draftReviews} saved ${draftReviews === 1 ? 'draft needs' : 'drafts need'} review`}</p></div>
          <button type="button" className="review-secondary" onClick={() => setSection('responsibilities')}>Review duties</button>
        </div>
        <p className="review-warning-strip">A provider mapping is not an accepted allocation. Verify the responsibility split and system scope before relying on inheritance.</p>
      </section>}
      {section === 'scope' && <section className={sectionClass}>
        <h3 className="font-semibold">Contributing components and recorded locations</h3>
        <p>A location places a component in system scope. It does not verify deployment, monitoring access or provider coverage.</p>
        {item.components.length ? <ul className="divide-y divide-gray-200 dark:divide-gray-700">
          {item.components.map(component => {
            const excluded = component.scopeDecision === 'Excluded';
            const assigned = component.scopeDecision === 'Included' || !excluded && inScope(component.placements);
            return <li key={`${component.source}:${component.recordId}`} className="space-y-2 py-3">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <p className="min-w-0 font-medium">{component.name}</p>
                {data.permissions.canManage && item.isApplied
                  ? <button type="button" className={secondaryButtonClass} aria-label={`${assigned ? 'Change' : 'Assign'} location for ${component.name}`}
                    onClick={() => onManageComponent(component.recordId, component.source)}>{assigned ? 'Change' : 'Assign location'}</button>
                  : <span className="text-xs text-gray-500">Read-only</span>}
              </div>
              {excluded ? <p>Reviewed service-use exclusion: not counted toward in-scope coverage.</p>
                : component.reviewedScope?.decision === 'Included' ? <p>{component.reviewedScope.boundaryName} · reviewed service use, not infrastructure containment.</p>
                : <ul className="space-y-1">{component.placements.map((placement, index) => <li key={placement.id || index}>
                {placement.state === 'SystemWide' ? 'System-wide' : placement.state === 'Unassigned' ? 'No location assigned'
                  : `${placement.boundaryName ?? 'Unnamed boundary'}${placement.state === 'Excluded' ? ' (Excluded)' : ''}`}
              </li>)}</ul>}
              {!assigned && !excluded && <p className="text-amber-800 dark:text-amber-200">No in-scope system location is recorded. Assign an existing system boundary or ask an authorized system manager.</p>}
            </li>;
          })}
        </ul> : <p>No contributing components are recorded. Link components through the authorized capability setup workflow.</p>}
      </section>}
      {section === 'responsibilities' && <section className={sectionClass}>
        <h3 className="font-semibold">Review prepared responsibility drafts</h3>
        <p className="text-xs review-muted">Choose a mapped control. Draft preparation and saved allocation are separate.</p>
        <div className="review-controls" aria-label="Mapped controls">{data.controls.map(control =>
          <button type="button" key={control.controlId} aria-label={control.controlId} aria-pressed={controlId === control.controlId}
            className="review-secondary" onClick={() => setControlId(control.controlId)}>
            {control.controlId}
            <span className="block text-xs font-normal">{responsibilityLabel(control.reviewState)}</span>
          </button>)}</div>
        {selected ? <>
          <ResponsibilityDraftEditor tenantId={tenantId} systemId={systemId} source={item.source}
            capabilityId={item.recordId} controlId={controlId} edits={edits} onChanged={() => { draftContexts.retry(); onChanged(); }} />
          <details><summary>Recorded mapping and duties</summary><dl className="mt-3 space-y-2">
            <div><dt className="font-medium">Provider source information</dt><dd className="whitespace-pre-wrap">{selected.providerCoverage || 'No control-specific provider information is recorded.'}</dd></div>
            <div><dt className="font-medium">Recorded customer responsibility</dt><dd className="whitespace-pre-wrap">{selected.organizationDuty || 'Customer duties are not recorded. Review the source and system records before confirming.'}</dd></div>
          </dl></details>
          <div className="flex flex-wrap gap-2">
            <Link className="review-secondary" to={`${base}?tab=coverage${context}`}>Review {controlId} responsibility</Link>
            <Link className="review-secondary" to={`${base}?tab=evidence${context}`}>Review evidence and narratives</Link>
          </div>
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
    <details className="text-xs"><summary className="cursor-pointer">Source ownership and full capability review</summary>
      <div className="mt-3 space-y-3">
      {item.source === 'provider' && <p>Provider-authored source content remains read-only. Authorized changes affect only this system&apos;s application and responsibility records.</p>}
      <Link className="text-indigo-700 underline dark:text-indigo-300" to={base}>Open full capability review</Link>
      </div>
    </details>
  </div>;
}
