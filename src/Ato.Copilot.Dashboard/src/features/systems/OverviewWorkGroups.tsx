import { useEffect, useRef, useState } from 'react';
import type { OverviewWork, OverviewWorkGroup } from '../../api/systemOverview';
import { getOverviewWork, overviewError } from '../../api/systemOverview';
import { Link } from '../workspaces/workspaceNavigation';
import { systemPanel, systemPrimaryAction, systemSecondaryAction } from './SystemTaskPresentation';
import { packageSourceHref } from './packageReadinessNavigation';
import { moveTabFocus } from '../workspace-operations/workspaceUi';

export default function OverviewWorkGroups({ systemId, work, mine, offset, expanded, allFindings, search,
  onOwner, onOffset, onExpanded, onAllFindings, onAi }: {
  systemId: string; work: OverviewWork | null; mine: boolean; offset: number; expanded: string[];
  allFindings: boolean; search: string; onOwner: (mine: boolean) => void; onOffset: (offset: number) => void;
  onExpanded: (id: string, open: boolean) => void; onAllFindings: () => void; onAi: (group: OverviewWorkGroup) => void;
}) {
  const recommended = work?.groups.items.find(group => group.id === work.recommendedGroupId && group.priorityReason && group.action.canView);
  return <section aria-label="Team documentation work" className={`${systemPanel} space-y-4`}>
    <h2 className="text-lg font-semibold">What to work on next</h2>
    <div role="tablist" aria-label="Task ownership" onKeyDown={moveTabFocus} className="flex flex-wrap gap-3 border-b border-slate-200 dark:border-slate-700">
      {[{ mine: false, name: 'All system work' }, { mine: true, name: 'Assigned to me' }].map(tab =>
        <button key={tab.name} type="button" role="tab" aria-selected={mine === tab.mine} tabIndex={mine === tab.mine ? 0 : -1}
          className={`min-h-11 border-b-2 px-2 text-sm ${mine === tab.mine ? 'border-indigo-600 font-semibold text-indigo-700 dark:text-indigo-300' : 'border-transparent'}`}
          onClick={() => onOwner(tab.mine)}>{tab.name}</button>)}
    </div>
    {work && !work.findingsAvailable && <p role="status">Individual findings were not retained for this older check. Check again to obtain grouped work; aggregate checks are not finding counts.</p>}
    {recommended && <section aria-label="Next recommended action" className="space-y-2 rounded-lg bg-indigo-50 p-4 dark:bg-indigo-950">
      <h3 className="font-semibold">Start with {recommended.title.toLowerCase()}</h3>
      <p className="text-xs font-semibold">Rule-based priority — not an AI recommendation</p>
      <p className="text-sm">{recommended.priorityReason}</p>
      <p className="text-xs">{recommended.owner?.displayName ?? 'Owner not provided'} · Supports {recommended.documents.join(', ') || 'Documentation context not provided'}</p>
      <Link className={systemPrimaryAction} to={packageSourceHref(systemId, recommended.action.path!, search, 'overview')!}>Review next prerequisite</Link>
      <button type="button" className={`${systemSecondaryAction} ml-0 sm:ml-2`} onClick={() => onAi(recommended)}>Explain next action with AI</button>
    </section>}
    {work?.findingsAvailable && <>
      <div><h3 className="font-semibold">Grouped package gaps</h3>
        <p className="text-xs text-slate-600 dark:text-slate-300">{work.groups.totalCount} work {work.groups.totalCount === 1 ? 'group' : 'groups'}</p>
        <p className="mt-1 text-xs text-slate-600 dark:text-slate-300">A work group may address several findings. Every returned finding remains individually inspectable.</p>
      </div>
      {!work.groups.totalCount && <div className="space-y-2">
        <p>{mine ? 'No actions are currently assigned to you.' : 'No grouped findings were returned by this check.'}</p>
        {mine && <><p className="text-sm">System-wide gaps may still remain. Personal work uses explicit recorded assignments, not role assumptions.</p>
          <button type="button" className={systemSecondaryAction} onClick={() => onOwner(false)}>View all system work</button></>}
      </div>}
      {work.groups.items.map(group => <Group key={`${work.runId}:${group.id}`} systemId={systemId} runId={work.runId}
        initial={group} open={allFindings || expanded.includes(group.id)} search={search}
        onOpen={value => onExpanded(group.id, value)} onAi={() => onAi(group)} />)}
      {work.groups.totalCount > 0 && <div className="flex flex-wrap items-center justify-between gap-3">
        <span className="text-xs">Work groups {offset + 1}–{Math.min(offset + work.groups.items.length, work.groups.totalCount)} of {work.groups.totalCount}</span>
        <div className="flex gap-2">
          <button type="button" className={systemSecondaryAction} disabled={offset === 0} onClick={() => onOffset(Math.max(0, offset - 10))}>Previous work groups</button>
          <button type="button" className={systemSecondaryAction} disabled={offset + 10 >= work.groups.totalCount} onClick={() => onOffset(offset + 10)}>Next work groups</button>
        </div>
      </div>}
      <button type="button" className="min-h-11 text-sm font-semibold text-indigo-700 dark:text-indigo-300" onClick={onAllFindings}>
        {allFindings ? 'Collapse finding details' : 'View all findings'}
      </button>
      {allFindings && <p className="text-xs text-slate-600 dark:text-slate-300">All finding details on this page are open. Use group and finding pagination to inspect the complete result.</p>}
    </>}
  </section>;
}

function Group({ systemId, runId, initial, open, onOpen, search, onAi }: {
  systemId: string; runId: string; initial: OverviewWorkGroup; open: boolean; onOpen: (open: boolean) => void; search: string; onAi: () => void;
}) {
  const [group, setGroup] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const pending = useRef<AbortController | null>(null);
  useEffect(() => { setGroup(initial); }, [initial]);
  useEffect(() => () => pending.current?.abort(), []);
  const href = group.action.canView && group.action.path ? packageSourceHref(systemId, group.action.path, search, 'overview') : null;
  async function page(offset: number) {
    if (pending.current) return;
    const controller = new AbortController();
    pending.current = controller; setBusy(true); setError('');
    try {
      const result = await getOverviewWork(systemId, runId, { groupId: group.id, findingOffset: offset, limit: 10, offset: 0 }, controller.signal);
      const refreshed = result.groups.items.find(item => item.id === group.id);
      if (!refreshed) throw new Error('The saved finding group could not be verified.');
      if (!controller.signal.aborted) setGroup(refreshed);
    } catch (reason) {
      if (!controller.signal.aborted) setError(overviewError(reason));
    } finally {
      pending.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }
  return <details open={open} className="border-b border-slate-200 py-3 dark:border-slate-700"
    onToggle={event => { if (event.currentTarget.open !== open) onOpen(event.currentTarget.open); }}>
    <summary className="cursor-pointer text-sm font-semibold focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-600">
      <span>{group.title}</span><span className="ml-2 text-xs font-normal">{group.total} findings · {group.blocking} blocking · {group.warnings} warnings</span>
    </summary>
    <div className="mt-3 min-w-0 space-y-3 text-sm">
      <p>{group.owner?.displayName ?? 'Owner not provided'}</p>
      {!group.owner && <p className="text-xs text-slate-600 dark:text-slate-300">No verified current owner was provided for these saved findings.</p>}
      <p className="text-xs">Affected controls: {group.controls.join(', ') || 'Not provided'} · RMF context: {group.rmfPhases.join(' / ') || 'Not provided'}</p>
      <p className="text-xs">Supports: {group.documents.join(', ') || 'Document mapping not provided'}</p>
      {href ? <Link className={systemPrimaryAction} aria-label={`Open ${group.title}`} to={href}>Open existing workflow</Link>
        : <p>{group.action.reason || 'A verified source workflow is unavailable.'}</p>}
      <button type="button" className={systemSecondaryAction} onClick={onAi}>Explain this group with AI</button>
      {error && <p role="alert" className="text-red-800 dark:text-red-200">{error}</p>}
      <ul className="divide-y divide-slate-200 dark:divide-slate-700">{group.findings.items.map(finding => <li key={finding.id} className="space-y-1 py-3">
        <p className="text-xs font-semibold">{finding.severity === 'Error' ? 'Blocking requirement' : 'Warning'}</p>
        <p>{finding.description}</p>{finding.remediation && <p className="text-xs text-slate-600 dark:text-slate-300">{finding.remediation}</p>}
        <details className="text-xs"><summary className="cursor-pointer text-indigo-700 dark:text-indigo-300">Technical identifiers</summary>
          <dl className="mt-2 space-y-1 break-all"><dt>Finding</dt><dd>{finding.id}</dd>
            {finding.controlId && <><dt>Control</dt><dd>{finding.controlId}</dd></>}
            {finding.recordId && <><dt>Requirement or record</dt><dd>{finding.recordId}</dd></>}
            <dt>Source category</dt><dd>{finding.category}</dd></dl></details>
      </li>)}</ul>
      {group.total > 20 && <div className="flex flex-wrap items-center gap-2">
        <span className="text-xs">Findings {group.findings.offset + 1}–{group.findings.offset + group.findings.items.length} of {group.total}</span>
        <button type="button" className={systemSecondaryAction} disabled={busy || group.findings.offset === 0}
          onClick={() => { void page(Math.max(0, group.findings.offset - 20)); }}>Previous findings</button>
        <button type="button" className={systemSecondaryAction} disabled={busy || group.findings.offset + 20 >= group.total}
          onClick={() => { void page(group.findings.offset + 20); }}>Next findings</button>
      </div>}
    </div>
  </details>;
}
