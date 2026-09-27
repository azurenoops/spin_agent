import { useEffect, useState } from 'react';
import { listBoundaryComponents } from '../../api/boundaries';
import type { BoundaryComponentDto } from '../../types/dashboard';
import { systemPanel, systemSecondaryAction } from './SystemTaskPresentation';

export default function SystemBoundaryInventory({ systemId, boundaries, onReview }: {
  systemId: string;
  boundaries: { id: string; name: string; isPrimary: boolean; boundaryType: string }[];
  onReview: (boundaryId: string) => void;
}) {
  const [choice, setChoice] = useState('');
  const selected = boundaries.find(item => item.id === choice) ?? boundaries.find(item => item.isPrimary) ?? boundaries[0];
  const [items, setItems] = useState<BoundaryComponentDto[]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    if (!selected) return;
    let current = true;
    setLoading(true); setError(null); setItems([]);
    void listBoundaryComponents(systemId, selected.id, { page, pageSize: 25 }).then(result => {
      if (!current) return;
      setItems(result.items); setTotal(result.totalCount);
    }).catch(reason => { if (current) setError(reason instanceof Error ? reason.message : 'Recorded boundary components are unavailable.'); })
      .finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [systemId, selected?.id, page, attempt]);
  if (!selected) return null;
  return <section className={systemPanel} aria-label="Recorded boundary inventory">
    <div className="mb-5 flex flex-wrap items-end justify-between gap-3">
      <label className="block text-sm font-medium">Recorded boundary
        <select className="mt-2 block w-full rounded-md border border-slate-300 bg-white px-3 py-2 dark:border-slate-600 dark:bg-slate-900"
          value={selected.id} onChange={event => { setChoice(event.target.value); setPage(1); }}>
          {boundaries.map(item => <option key={item.id} value={item.id}>{item.name} ({item.boundaryType})</option>)}
        </select>
      </label>
      <button type="button" className={systemSecondaryAction} onClick={() => onReview(selected.id)}>Review selected boundary</button>
    </div>
    <h2 className="mb-3 text-lg font-semibold">Components and scope</h2>
    {loading && <p role="status" className="text-sm text-slate-500">Loading recorded component placements…</p>}
    {error && <div className="space-y-2 text-sm text-amber-900"><p role="alert">{error}</p>
      <button type="button" className="underline" onClick={() => setAttempt(value => value + 1)}>Retry component placements</button>
    </div>}
    {!loading && !error && (items.length ? <div className="overflow-x-auto">
      <table className="w-full text-left text-sm"><thead className="bg-slate-50 dark:bg-slate-800"><tr>
        {['Component', 'Type', 'Source', 'Scope'].map(label => <th key={label} scope="col" className="px-3 py-3 font-medium">{label}</th>)}
      </tr></thead><tbody className="divide-y divide-slate-100 dark:divide-slate-700">{items.map(item => <tr key={item.assignmentId}>
        <td className="px-3 py-3 font-medium">{item.componentName}</td><td className="px-3 py-3">{item.componentType}</td>
        <td className="px-3 py-3">{item.inheritanceProvider ?? item.source}</td>
        <td className="px-3 py-3"><span className="font-medium">{item.isInScope ? 'Included' : 'Excluded'}</span>
          {!item.isInScope && item.exclusionRationale && <p className="mt-1 text-xs text-slate-500">{item.exclusionRationale}</p>}</td>
      </tr>)}</tbody></table>
    </div> : <p className="text-sm text-slate-500">No component assignments are recorded in this boundary.</p>)}
    {total > 25 && <nav aria-label="Boundary inventory pages" className="mt-4 flex items-center justify-between gap-3 text-sm">
      <button type="button" disabled={page <= 1 || loading} className={systemSecondaryAction} onClick={() => setPage(value => value - 1)}>Previous components</button>
      <span>{page} / {Math.ceil(total / 25)}</span>
      <button type="button" disabled={page * 25 >= total || loading} className={systemSecondaryAction} onClick={() => setPage(value => value + 1)}>Next components</button>
    </nav>}
    <p className="mt-4 rounded-lg border border-indigo-100 bg-indigo-50 p-4 text-sm text-indigo-900">
      Changes to included resources should identify affected inventory, assessment scope and monitoring rules. Viewing this register does not acquire an edit lock or change a placement.
    </p>
  </section>;
}
