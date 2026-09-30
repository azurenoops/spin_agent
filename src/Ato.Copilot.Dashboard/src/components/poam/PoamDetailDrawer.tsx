import { useState } from 'react';
import { usePoamDetail } from '../../hooks/usePoam';
import { linkComponents, unlinkComponents } from '../../api/poam';
import { SeverityBadge, StatusBadge } from './PoamTable';
import ComponentPicker from './ComponentPicker';
import PoamLifecycleActions from './PoamLifecycleActions';
import AsyncErrorState from '../AsyncErrorState';
import { useSystemMutationPermission } from '../permissions/useSystemMutationPermission';
import SetupDialog from '../../features/workspace-operations/SetupDialog';
import { Link } from '../../features/workspaces/workspaceNavigation';
import { systemPanel, systemPrimaryAction, systemSecondaryAction } from '../../features/systems/SystemTaskPresentation';
import PoamLinkedWork from './PoamLinkedWork';
import { poamErrorMessage } from '../../utils/poamErrors';
import { useDateFormatter } from '../../hooks/useDateFormatter';

interface PoamDetailDrawerProps {
  poamId: string;
  systemId?: string;
  onClose: () => void;
}

export default function PoamDetailDrawer({ poamId, systemId, onClose }: PoamDetailDrawerProps) {
  const { formatCalendarDate, formatDateTime } = useDateFormatter();
  const { data: detail, loading, error, refresh } = usePoamDetail(poamId);
  const canManage = useSystemMutationPermission(detail?.systemId, 'canManageRemediation');
  const [tab, setTab] = useState<'Overview' | 'Linked work' | 'Evidence & history'>('Overview');
  const [busy, setBusy] = useState(false);
  const [mutationError, setMutationError] = useState<string | null>(null);
  const [showComponents, setShowComponents] = useState(false);
  const [componentIds, setComponentIds] = useState<string[]>([]);
  const scopeMismatch = detail && systemId && detail.systemId !== systemId;
  const mutate = async (action: () => Promise<unknown>) => {
    if (!canManage || busy) return;
    setBusy(true); setMutationError(null);
    try { await action(); refresh(); setShowComponents(false); setComponentIds([]); }
    catch (failure) { setMutationError(poamErrorMessage(failure)); }
    finally { setBusy(false); }
  };
  return <SetupDialog placement="right" busy={busy} onClose={onClose}
    title={detail && !scopeMismatch ? detail.weakness : 'POA&M details'}
    description={detail && !scopeMismatch ? `${detail.controlId} · ${detail.systemName}` : 'Loading the selected remediation commitment.'}>
    {scopeMismatch ? <p role="alert">This commitment does not belong to the current system.</p>
      : <>
        {error && <AsyncErrorState title="Unable to load POA&M details." onRetry={refresh} />}
        {loading && !detail && <p role="status">Loading commitment…</p>}
        {detail && <>
          <div className="mb-4 flex flex-wrap gap-2"><StatusBadge status={detail.status} /><SeverityBadge severity={detail.catSeverity} /></div>
          <nav aria-label="POA&M detail sections" className="mb-5 flex flex-wrap gap-4 border-b border-slate-200 dark:border-slate-700">
            {(['Overview', 'Linked work', 'Evidence & history'] as const).map(value => <button key={value} aria-pressed={tab === value}
              className={`border-b-2 pb-3 text-xs font-medium ${tab === value ? 'border-indigo-600 text-indigo-700 dark:text-indigo-300' : 'border-transparent text-slate-500 dark:text-slate-300'}`}
              disabled={busy} onClick={() => setTab(value)}>{value}</button>)}
          </nav>
          {mutationError && <p role="alert" className="mb-4 text-sm text-red-700 dark:text-red-300">{mutationError}</p>}
          <div className="space-y-4">
            {tab === 'Overview' && <>
              <section className={systemPanel}><h3 className="mb-3 text-sm font-semibold">Remediation commitment</h3>
                <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-3 text-sm">
                  <dt className="text-slate-500 dark:text-slate-400">Owner</dt><dd className="break-words">{detail.poc}{detail.pocEmail && <p className="text-xs">{detail.pocEmail}</p>}</dd>
                  <dt className="text-slate-500 dark:text-slate-400">Due date</dt><dd>{formatCalendarDate(detail.scheduledCompletionDate)}</dd>
                  <dt className="text-slate-500 dark:text-slate-400">Source</dt><dd>{detail.weaknessSource}</dd>
                  {detail.resourcesRequired && <><dt>Resources</dt><dd>{detail.resourcesRequired}</dd></>}
                  {detail.costEstimate != null && <><dt>Cost estimate</dt><dd>${detail.costEstimate.toLocaleString()}</dd></>}
                </dl>{detail.comments && <p className="mt-4 whitespace-pre-wrap text-sm">{detail.comments}</p>}
              </section>
              <section className={systemPanel}><h3 className="text-sm font-semibold">Milestones ({detail.milestones.filter(item => item.completedDate).length}/{detail.milestones.length})</h3>
                {detail.milestones.length === 0 ? <p className="mt-3 text-sm text-slate-500 dark:text-slate-300">No milestones recorded.</p>
                  : <ol className="mt-3 space-y-3">{[...detail.milestones].sort((a, b) => a.sequence - b.sequence).map(item => <li key={item.id} className="rounded-lg border border-slate-200 p-3 text-sm dark:border-slate-700">
                    <p className="font-medium">{item.sequence}. {item.description}</p><p className="mt-1 text-xs text-slate-500 dark:text-slate-300">Target: {formatCalendarDate(item.targetDate)} · {item.completedDate ? 'Completed' : item.isOverdue ? 'Overdue' : 'Incomplete'}</p>
                  </li>)}</ol>}
              </section>
              {(detail.status === 'Ongoing' || detail.status === 'Delayed') && <PoamLifecycleActions detail={detail} onStatusChanged={refresh} onBusyChange={setBusy} />}
            </>}
            {tab === 'Linked work' && <>
              <PoamLinkedWork systemId={detail.systemId} poamId={poamId} controlId={detail.controlId} weakness={detail.weakness} canManage={canManage} onChanged={refresh} onBusyChange={setBusy} />
              <details className={systemPanel}><summary className="cursor-pointer text-sm font-semibold">Affected components ({detail.components?.length ?? 0})</summary>
                <ul className="mt-3 space-y-2">{detail.components?.map(component => <li key={component.id} className="flex items-center justify-between gap-2 text-sm">{component.name}
                  <button className="text-xs text-red-700 disabled:opacity-50 dark:text-red-300" disabled={!canManage || busy} onClick={() => void mutate(() => unlinkComponents(poamId, { componentIds: [component.id] }))}>Unlink {component.name}</button></li>)}</ul>
                <button className={`${systemSecondaryAction} mt-3`} disabled={!canManage || busy} onClick={() => setShowComponents(!showComponents)}>{showComponents ? 'Cancel linking' : 'Link components'}</button>
                {showComponents && <div className="mt-3 space-y-3"><ComponentPicker systemId={detail.systemId} selectedIds={componentIds} onChange={setComponentIds} disabled={busy} />
                  <button className={systemPrimaryAction} disabled={!canManage || !componentIds.length || busy} onClick={() => void mutate(() => linkComponents(poamId, { componentIds }))}>Link selected components</button></div>}
              </details>
            </>}
            {tab === 'Evidence & history' && <>
              <section className={systemPanel}><h3 className="text-sm font-semibold">Verification evidence</h3>
                <p className="mt-2 text-sm text-slate-500 dark:text-slate-300">Completed tasks and closed tickets do not automatically close this commitment.</p>
                <Link className={`${systemSecondaryAction} mt-3`} to={`/systems/${detail.systemId}/evidence`}>Review supporting evidence</Link>
              </section>
              {(detail.ticketSync || detail.externalTicketRef) && <section className={systemPanel}><h3 className="text-sm font-semibold">Legacy POA&amp;M ticket reference</h3>
                <p className="mt-2 text-sm">{detail.ticketSync?.externalTicketId ?? detail.externalTicketRef}</p>
                {detail.ticketSync && <><p className="mt-1 text-xs">{detail.ticketSync.syncStatus} · Last sync {formatDateTime(detail.ticketSync.lastSyncAt)}</p>
                  {detail.ticketSync.lastSyncError && <p className="mt-2 text-xs text-red-700 dark:text-red-300">{detail.ticketSync.lastSyncError}</p>}</>}
                <p className="mt-2 text-xs text-slate-500 dark:text-slate-300">Retained legacy reference. New external ticket links belong to remediation tasks and do not verify or close this commitment.</p>
              </section>}
              <section className={systemPanel}><h3 className="text-sm font-semibold">Commitment history</h3>
                {detail.history?.length ? <ol className="mt-3 space-y-3">{detail.history.map(item => <li key={item.id} className="rounded-lg border border-slate-200 p-3 text-sm dark:border-slate-700">
                  <p className="font-medium">{item.eventType}</p><p className="mt-1 text-xs text-slate-500 dark:text-slate-300">{new Date(item.timestamp).toLocaleString()} · {item.actingUserName}</p>
                  {(item.oldValue || item.newValue) && <p className="mt-2 break-words text-xs">{item.oldValue ?? 'Not recorded'} → {item.newValue ?? 'Not recorded'}</p>}
                  {item.details && <p className="mt-2 whitespace-pre-wrap text-xs">{item.details}</p>}
                  {item.cascadeOrigin && <p className="mt-1 text-xs">Cascade origin: {item.cascadeOrigin}</p>}
                </li>)}</ol> : <p className="mt-2 text-sm text-slate-500 dark:text-slate-300">No history recorded.</p>}
              </section>
            </>}
          </div>
        </>}
      </>}
  </SetupDialog>;
}
