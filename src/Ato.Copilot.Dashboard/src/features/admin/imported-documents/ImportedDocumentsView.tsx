import { useEffect, useMemo, useState } from 'react';
import axios from 'axios';

interface ImportedArtifactRow {
  id: string;
  tenantId: string;
  kind: string | number;
  label: string;
  versionTag: string | null;
  createdAt: string;
  updatedAt: string;
  dependentsCount: number;
  staleDependentsCount: number;
  statusLabel: string | null;
}

interface DependencyRow {
  id: string;
  sourceArtifactType: string;
  sourceArtifactId: string;
  sourceVersionTag: string;
  dependentType: string;
  dependentId: string;
  derivedAt: string;
  isStale: boolean;
  staleSince: string | null;
  staleReason: string | null;
  lastReRunJobId: string | null;
}

const KIND_LABELS: Record<string, string> = {
  Template: 'Org Template',
  EmassImportSession: 'eMASS Import',
  SspPdfImportSession: 'SSP PDF',
  NarrativeSeedDocument: 'Narrative Seed',
};

const onboardingApi = axios.create({ baseURL: '/api/onboarding' });

/**
 * Cross-kind imports management view (T132 / FR-093). Paginated table with
 * filter chips (Template / eMASS / SSP PDF / Narrative Seed); per-row dependents
 * drawer + admin re-run trigger.
 */
export default function ImportedDocumentsView() {
  const [filter, setFilter] = useState<string>('');
  const [page, setPage] = useState(1);
  const [pageSize] = useState(50);
  const [rows, setRows] = useState<ImportedArtifactRow[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [openDeps, setOpenDeps] = useState<{ id: string; rows: DependencyRow[] } | null>(null);
  const [busy, setBusy] = useState(false);

  async function refresh() {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
      if (filter) params.set('kind', filter);
      const { data } = await onboardingApi.get<{ ok: boolean; data: { items: ImportedArtifactRow[]; total: number } }>(`/imports?${params}`);
      setRows(data.data.items);
      setTotal(data.data.total);
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Failed to load imports.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void refresh();
  }, [filter, page]);

  async function openDependencies(row: ImportedArtifactRow) {
    setBusy(true);
    try {
      const { data } = await onboardingApi.get<{ ok: boolean; data: DependencyRow[] }>(`/imports/${row.id}/dependencies`);
      setOpenDeps({ id: row.id, rows: data.data });
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Failed to load dependencies.');
    } finally {
      setBusy(false);
    }
  }

  async function rerunDependency(depId: string) {
    setBusy(true);
    try {
      await onboardingApi.post(`/dependencies/${depId}/rerun`);
      if (openDeps) {
        const { data } = await onboardingApi.get<{ ok: boolean; data: DependencyRow[] }>(`/imports/${openDeps.id}/dependencies`);
        setOpenDeps({ id: openDeps.id, rows: data.data });
      }
      await refresh();
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Re-run failed.');
    } finally {
      setBusy(false);
    }
  }

  const totalPages = useMemo(() => Math.max(1, Math.ceil(total / pageSize)), [total, pageSize]);

  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Imported Documents</h1>
        <div className="flex gap-2">
          {[
            { v: '', label: 'All' },
            { v: 'Template', label: 'Templates' },
            { v: 'EmassImportSession', label: 'eMASS' },
            { v: 'SspPdfImportSession', label: 'SSP PDFs' },
            { v: 'NarrativeSeedDocument', label: 'Narrative Seeds' },
          ].map(c => (
            <button
              key={c.v}
              onClick={() => { setFilter(c.v); setPage(1); }}
              className={`rounded px-3 py-1 text-sm ${filter === c.v ? 'bg-indigo-600 text-white' : 'bg-gray-100 text-gray-700'}`}
            >
              {c.label}
            </button>
          ))}
        </div>
      </div>

      {error && <div className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{error}</div>}

      <table className="w-full text-sm">
        <thead className="bg-gray-50">
          <tr>
            <th className="text-left px-3 py-2">Kind</th>
            <th className="text-left px-3 py-2">Label</th>
            <th className="text-left px-3 py-2">Status</th>
            <th className="text-left px-3 py-2">Updated</th>
            <th className="text-left px-3 py-2">Dependents</th>
            <th className="text-left px-3 py-2">Actions</th>
          </tr>
        </thead>
        <tbody>
          {loading && <tr><td colSpan={6} className="px-3 py-4 text-center text-gray-500">Loading…</td></tr>}
          {!loading && rows.length === 0 && (
            <tr><td colSpan={6} className="px-3 py-4 text-center text-gray-500">No artifacts.</td></tr>
          )}
          {rows.map(r => {
            const kindLabel = KIND_LABELS[String(r.kind)] ?? String(r.kind);
            return (
              <tr key={r.id} className="border-t">
                <td className="px-3 py-2">{kindLabel}</td>
                <td className="px-3 py-2">{r.label}</td>
                <td className="px-3 py-2">{r.statusLabel ?? '—'}</td>
                <td className="px-3 py-2">{new Date(r.updatedAt).toLocaleString()}</td>
                <td className="px-3 py-2">
                  {r.dependentsCount}
                  {r.staleDependentsCount > 0 && (
                    <span className="ml-1 rounded bg-amber-100 px-1 text-amber-800 text-xs">
                      {r.staleDependentsCount} stale
                    </span>
                  )}
                </td>
                <td className="px-3 py-2">
                  <button
                    onClick={() => openDependencies(r)}
                    className="text-indigo-600 hover:underline"
                  >
                    View dependents
                  </button>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>

      <div className="flex items-center justify-between text-sm text-gray-600">
        <div>{total} artifacts · page {page} / {totalPages}</div>
        <div className="flex gap-2">
          <button disabled={page <= 1} onClick={() => setPage(p => p - 1)} className="rounded border px-2 py-1 disabled:opacity-50">Prev</button>
          <button disabled={page >= totalPages} onClick={() => setPage(p => p + 1)} className="rounded border px-2 py-1 disabled:opacity-50">Next</button>
        </div>
      </div>

      {openDeps && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/30" onClick={() => setOpenDeps(null)}>
          <div className="w-full max-w-3xl rounded bg-white p-6 shadow" onClick={e => e.stopPropagation()}>
            <div className="flex items-center justify-between mb-3">
              <h2 className="text-lg font-semibold">Dependents</h2>
              <button onClick={() => setOpenDeps(null)} className="text-gray-500 hover:text-gray-800">✕</button>
            </div>
            {openDeps.rows.length === 0 ? (
              <div className="text-sm text-gray-500">No downstream dependents.</div>
            ) : (
              <table className="w-full text-sm">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="text-left px-2 py-1">Type</th>
                    <th className="text-left px-2 py-1">Stale</th>
                    <th className="text-left px-2 py-1">Reason</th>
                    <th className="text-left px-2 py-1">Action</th>
                  </tr>
                </thead>
                <tbody>
                  {openDeps.rows.map(d => (
                    <tr key={d.id} className="border-t">
                      <td className="px-2 py-1">{d.dependentType}</td>
                      <td className="px-2 py-1">{d.isStale ? 'Yes' : 'No'}</td>
                      <td className="px-2 py-1">{d.staleReason ?? '—'}</td>
                      <td className="px-2 py-1">
                        <button
                          disabled={busy || !d.isStale}
                          onClick={() => rerunDependency(d.id)}
                          className="rounded bg-indigo-600 px-2 py-1 text-white text-xs disabled:opacity-50"
                        >
                          Re-run
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
