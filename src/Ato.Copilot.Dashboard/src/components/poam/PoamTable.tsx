import type { PoamListItem, PoamListQuery } from '../../types/poam';
import { systemSecondaryAction } from '../../features/systems/SystemTaskPresentation';
import { useDateFormatter } from '../../hooks/useDateFormatter';

export function SeverityBadge({ severity }: { severity: string }) {
  const value = severity.replace(/^Cat/, '');
  const colors: Record<string, string> = {
    I: 'bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-200',
    II: 'bg-amber-50 text-amber-700 dark:bg-amber-950 dark:text-amber-200',
    III: 'bg-indigo-50 text-indigo-700 dark:bg-indigo-950 dark:text-indigo-200',
  };
  return <span className={`inline-flex rounded px-2 py-0.5 text-xs font-medium ${colors[value] ?? 'bg-slate-100 text-slate-700'}`}>CAT {value}</span>;
}

export function StatusBadge({ status }: { status: string }) {
  const colors: Record<string, string> = {
    Ongoing: 'bg-blue-50 text-blue-700 dark:bg-blue-950 dark:text-blue-200',
    Completed: 'bg-emerald-50 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-200',
    Delayed: 'bg-red-50 text-red-700 dark:bg-red-950 dark:text-red-200',
    RiskAccepted: 'bg-purple-50 text-purple-700 dark:bg-purple-950 dark:text-purple-200',
    ReadyToVerify: 'bg-teal-50 text-teal-700 dark:bg-teal-950 dark:text-teal-200',
  };
  return <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${colors[status] ?? 'bg-slate-100 text-slate-700'}`}>{status === 'ReadyToVerify' ? 'Ready to verify' : status}</span>;
}

interface PoamTableProps {
  items: PoamListItem[];
  totalItems: number;
  query: PoamListQuery;
  loading: boolean;
  onQueryChange: (updater: (prev: PoamListQuery) => PoamListQuery) => void;
  onRowClick: (item: PoamListItem) => void;
}

export default function PoamTable({ items, totalItems, query, loading, onQueryChange, onRowClick }: PoamTableProps) {
  const { formatCalendarDate } = useDateFormatter();
  const filtered = Boolean(query.search || query.status || query.catSeverity || query.overdue || query.componentId || query.view && query.view !== 'all');
  if (!loading && totalItems === 0 && !filtered) return <p className="rounded-lg border border-dashed border-slate-300 p-8 text-center text-sm dark:border-slate-600">No POA&amp;M items recorded</p>;
  const field = 'max-w-full rounded-md border border-slate-300 bg-white px-3 py-2 text-sm dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100';
  const page = query.page ?? 1;
  const pageSize = query.pageSize ?? 25;
  return <section aria-label="POA&M queue" aria-busy={loading} className="overflow-hidden rounded-[10px] border border-[#dfe4ed] bg-white dark:border-slate-700 dark:bg-slate-900">
    <div className="flex flex-wrap items-center gap-2 border-b border-slate-200 p-3 dark:border-slate-700">
      <input type="search" role="textbox" aria-label="Search POA&M items" placeholder="Search weaknesses, owners, controls…" className={`${field} min-w-0 flex-1`}
        value={query.search ?? ''} onChange={e => onQueryChange(q => ({ ...q, search: e.target.value || undefined, page: 1 }))} />
      <select aria-label="Status" className={field} value={query.status ?? ''} onChange={e => onQueryChange(q => ({ ...q, status: e.target.value || undefined, page: 1 }))}>
        <option value="">All statuses</option><option value="Ongoing">Ongoing</option><option value="Completed">Completed</option><option value="Delayed">Delayed</option><option value="RiskAccepted">Risk accepted</option>
      </select>
      <select aria-label="Severity" className={field} value={query.catSeverity ?? ''} onChange={e => onQueryChange(q => ({ ...q, catSeverity: e.target.value || undefined, page: 1 }))}>
        <option value="">All severities</option><option value="CatI">CAT I</option><option value="CatII">CAT II</option><option value="CatIII">CAT III</option>
      </select>
      <label className="flex items-center gap-2 text-xs"><input type="checkbox" checked={query.overdue ?? false} onChange={e => onQueryChange(q => ({ ...q, overdue: e.target.checked || undefined, page: 1 }))} />Overdue only</label>
    </div>
    {loading && items.length === 0 ? <p role="status" className="p-8 text-center text-sm">Loading POA&amp;M items…</p>
      : items.length === 0 ? <p className="p-8 text-center text-sm">No matching POA&amp;M items</p>
        : <div className="overflow-x-auto"><table className="w-full text-left text-sm">
          <thead className="bg-slate-50 text-xs text-slate-500 dark:bg-slate-800 dark:text-slate-300"><tr>
            {['Weakness', 'Owner', 'Next milestone', 'Due', 'Status'].map(label => <th scope="col" key={label} className="px-4 py-3 font-medium">{label}</th>)}
          </tr></thead>
          <tbody className="divide-y divide-slate-100 dark:divide-slate-700">{items.map(item => <tr key={item.id} className="hover:bg-indigo-50/50 dark:hover:bg-slate-800">
            <td className="min-w-52 max-w-md px-4 py-3"><button className="text-left font-semibold text-indigo-700 hover:underline dark:text-indigo-300" onClick={() => onRowClick(item)}>{item.weakness}</button>
              <div className="mt-1 flex flex-wrap items-center gap-2 text-xs"><span className="rounded bg-indigo-50 px-1.5 text-indigo-700 dark:bg-indigo-950 dark:text-indigo-200">{item.controlId}</span><SeverityBadge severity={item.catSeverity} /></div></td>
            <td className="px-4 py-3 text-slate-600 dark:text-slate-300">{item.poc || 'Unassigned'}</td>
            <td className="min-w-40 px-4 py-3 text-xs">{item.nextMilestone
              ? <><p>{item.nextMilestone.description}</p><p className="mt-1 text-slate-500 dark:text-slate-300">{formatCalendarDate(item.nextMilestone.targetDate)}</p>{item.nextMilestone.isOverdue && <p className="mt-1 text-red-700 dark:text-red-300">Milestone overdue</p>}</>
              : <span className="text-slate-500 dark:text-slate-300">{item.nextMilestone === null ? item.milestoneProgress.total ? 'All milestones complete' : 'No milestones recorded' : `${item.milestoneProgress.completed}/${item.milestoneProgress.total} complete`}</span>}</td>
            <td className="whitespace-nowrap px-4 py-3 text-xs">{formatCalendarDate(item.dueDate)}{item.isOverdue && <p className="mt-1 text-red-700 dark:text-red-300">Overdue</p>}</td>
            <td className="px-4 py-3"><StatusBadge status={item.readyToVerify ? 'ReadyToVerify' : item.status} /></td>
          </tr>)}</tbody>
        </table></div>}
    {totalItems > 0 && <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 p-3 text-xs dark:border-slate-700">
      <span>Showing {(page - 1) * pageSize + 1}–{Math.min(page * pageSize, totalItems)} of {totalItems}</span>
      <div className="flex items-center gap-2">
        <select aria-label="Items per page" className={field} value={pageSize} onChange={e => onQueryChange(q => ({ ...q, pageSize: Number(e.target.value), page: 1 }))}><option value={25}>25</option><option value={50}>50</option><option value={100}>100</option></select>
        <button disabled={page === 1 || loading} className={systemSecondaryAction} onClick={() => onQueryChange(q => ({ ...q, page: page - 1 }))}>Prev</button>
        <button disabled={page * pageSize >= totalItems || loading} className={systemSecondaryAction} onClick={() => onQueryChange(q => ({ ...q, page: page + 1 }))}>Next</button>
      </div>
    </div>}
  </section>;
}
