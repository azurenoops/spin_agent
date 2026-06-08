import { useState, useEffect, useCallback } from 'react';
import apiClient from '../api/client';

interface OverlayDoc {
  id: string;
  type: string;
  title: string;
  controlId: string;
  content: string;
  sourceReference?: string;
  isActive: boolean;
  createdBy: string;
  createdAt: string;
}

export default function KnowledgeBaseManagementPage() {
  const [docs, setDocs] = useState<OverlayDoc[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filterType, setFilterType] = useState('');
  const [filterControl, setFilterControl] = useState('');
  const [selected, setSelected] = useState<OverlayDoc | null>(null);

  const fetchDocs = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const params = new URLSearchParams();
      if (filterType) params.set('type', filterType);
      if (filterControl) params.set('controlId', filterControl);
      const res = await apiClient.get<OverlayDoc[]>(
        `/api/v1/admin/overlay-documents${params.size ? '?' + params.toString() : ''}`
      );
      setDocs(res.data);
    } catch (e: unknown) {
      setError((e as Error).message ?? 'Failed to load overlay documents');
    } finally {
      setLoading(false);
    }
  }, [filterType, filterControl]);

  useEffect(() => { void fetchDocs(); }, [fetchDocs]);

  const overlayTypes = ['CNSSI-1253', 'SECNAVINST', 'OPNAVINST', 'DoD-8140', 'DoD-8570'];

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-gray-900">Knowledge Base Management</h1>
          <p className="mt-1 text-sm text-gray-500">
            Manage NIST 800-53 overlay documents: Navy Instructions, DoD Directives, CNSSI-1253 NSS overlays.
          </p>
        </div>
      </div>

      {/* Filters */}
      <div className="flex flex-wrap gap-3">
        <select
          value={filterType}
          onChange={e => setFilterType(e.target.value)}
          className="rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
        >
          <option value="">All Types</option>
          {overlayTypes.map(t => <option key={t} value={t}>{t}</option>)}
        </select>
        <input
          type="text"
          placeholder="Filter by control ID (e.g. AC-1)"
          value={filterControl}
          onChange={e => setFilterControl(e.target.value.toUpperCase())}
          className="rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500 w-56"
        />
        <button
          onClick={() => void fetchDocs()}
          className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
        >
          Apply
        </button>
      </div>

      {/* Error */}
      {error && (
        <div className="rounded-lg bg-red-50 border border-red-200 p-3 text-sm text-red-700">{error}</div>
      )}

      {/* Table */}
      {loading ? (
        <div className="text-sm text-gray-500">Loading overlay documents…</div>
      ) : docs.length === 0 ? (
        <div className="rounded-lg border border-dashed border-gray-300 p-8 text-center">
          <p className="text-sm text-gray-400">No overlay documents found. Add command-specific guidance above.</p>
        </div>
      ) : (
        <div className="overflow-hidden rounded-xl border border-gray-200 shadow-sm">
          <table className="min-w-full divide-y divide-gray-200 text-sm">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Control</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Type</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Title</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Source</th>
                <th className="px-4 py-3 text-left font-medium text-gray-600">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100 bg-white">
              {docs.map(doc => (
                <tr
                  key={doc.id}
                  onClick={() => setSelected(doc)}
                  className="cursor-pointer hover:bg-indigo-50 transition-colors"
                >
                  <td className="px-4 py-3 font-mono text-indigo-700">{doc.controlId}</td>
                  <td className="px-4 py-3">
                    <span className="inline-flex items-center rounded-full bg-blue-100 px-2 py-0.5 text-xs font-medium text-blue-800">
                      {doc.type}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-gray-900">{doc.title}</td>
                  <td className="px-4 py-3 text-gray-500 text-xs">{doc.sourceReference ?? '—'}</td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${
                      doc.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-600'
                    }`}>
                      {doc.isActive ? 'Active' : 'Inactive'}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Detail drawer */}
      {selected && (
        <div className="fixed inset-0 z-40 bg-black/30" onClick={() => setSelected(null)}>
          <aside
            className="absolute right-0 top-0 h-full w-full max-w-lg bg-white shadow-2xl overflow-y-auto"
            onClick={e => e.stopPropagation()}
          >
            <div className="flex items-center justify-between border-b border-gray-200 px-5 py-4">
              <h2 className="text-base font-semibold text-gray-900">{selected.title}</h2>
              <button onClick={() => setSelected(null)} className="text-gray-400 hover:text-gray-600">
                ×
              </button>
            </div>
            <div className="p-5 space-y-4">
              <div className="flex gap-3 text-sm">
                <span className="font-mono text-indigo-700 bg-indigo-50 px-2 py-1 rounded">{selected.controlId}</span>
                <span className="bg-blue-100 text-blue-800 px-2 py-1 rounded text-xs font-medium">{selected.type}</span>
              </div>
              {selected.sourceReference && (
                <p className="text-xs text-gray-500"><strong>Source:</strong> {selected.sourceReference}</p>
              )}
              <div className="rounded-lg bg-gray-50 border border-gray-200 p-4">
                <p className="text-sm text-gray-700 whitespace-pre-wrap">{selected.content}</p>
              </div>
              <p className="text-xs text-gray-400">Added by {selected.createdBy} · {new Date(selected.createdAt).toLocaleDateString()}</p>
            </div>
          </aside>
        </div>
      )}
    </div>
  );
}
