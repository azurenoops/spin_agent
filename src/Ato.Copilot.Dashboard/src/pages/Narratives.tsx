import { useState, useCallback, useMemo, Fragment } from 'react';
import { useParams, Link } from 'react-router-dom';
import PageLayout from '../components/layout/PageLayout';
import { usePolling } from '../hooks/usePolling';
import { getNarratives, bulkUpdateNarratives } from '../api/narratives';
import type { NarrativeListItem } from '../api/narratives';

// ─── Helpers ────────────────────────────────────────────────────────────────

function StatusBadge({ status, variant }: { status: string; variant?: 'green' | 'amber' | 'red' | 'blue' | 'gray' }) {
  const colors = {
    green: 'bg-green-100 text-green-700',
    amber: 'bg-amber-100 text-amber-700',
    red: 'bg-red-100 text-red-700',
    blue: 'bg-blue-100 text-blue-700',
    gray: 'bg-gray-100 text-gray-500',
  };
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${colors[variant ?? 'gray']}`}>
      {status}
    </span>
  );
}

function implVariant(status: string): 'green' | 'amber' | 'blue' | 'gray' {
  if (status === 'Implemented') return 'green';
  if (status === 'PartiallyImplemented') return 'amber';
  if (status === 'Planned') return 'blue';
  return 'gray';
}

function approvalVariant(status: string): 'green' | 'amber' | 'red' | 'blue' | 'gray' {
  if (status === 'Approved') return 'green';
  if (status === 'UnderReview') return 'blue';
  if (status === 'Draft') return 'amber';
  if (status === 'NeedsRevision') return 'red';
  return 'gray';
}

function formatDate(dt: string | null | undefined): string {
  if (!dt) return '—';
  return new Date(dt).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
}

const FAMILIES = ['All', 'AC', 'AU', 'AT', 'CA', 'CM', 'CP', 'IA', 'IR', 'MA', 'MP', 'PE', 'PL', 'PM', 'PS', 'RA', 'SA', 'SC', 'SI', 'SR'];
const STATUSES = ['All', 'Implemented', 'PartiallyImplemented', 'Planned', 'NotApplicable'];

// ─── Component ──────────────────────────────────────────────────────────────

export default function Narratives() {
  const { id: systemId } = useParams<{ id: string }>();
  const [familyFilter, setFamilyFilter] = useState('All');
  const [statusFilter, setStatusFilter] = useState('All');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [bulkStatus, setBulkStatus] = useState('');
  const [bulkApproval, setBulkApproval] = useState('');
  const [updating, setUpdating] = useState(false);

  const fetchNarratives = useCallback(() => {
    if (!systemId) return Promise.resolve([]);
    const params: Record<string, string> = {};
    if (familyFilter !== 'All') params.family = familyFilter;
    if (statusFilter !== 'All') params.status = statusFilter;
    if (search.trim()) params.search = search.trim();
    return getNarratives(systemId, params);
  }, [systemId, familyFilter, statusFilter, search]);

  const { data: narratives, loading, error, refresh } = usePolling<NarrativeListItem[]>(fetchNarratives, 30_000);

  const items = narratives ?? [];

  // Group by family
  const familyCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const n of items) {
      counts[n.family] = (counts[n.family] ?? 0) + 1;
    }
    return counts;
  }, [items]);

  // Progress stats
  const stats = useMemo(() => {
    const total = items.length;
    const implemented = items.filter(n => n.implementationStatus === 'Implemented').length;
    const partial = items.filter(n => n.implementationStatus === 'PartiallyImplemented').length;
    const planned = items.filter(n => n.implementationStatus === 'Planned').length;
    const approved = items.filter(n => n.approvalStatus === 'Approved').length;
    const ai = items.filter(n => n.aiSuggested).length;
    return { total, implemented, partial, planned, approved, ai };
  }, [items]);

  const toggleSelect = (controlId: string) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(controlId)) next.delete(controlId);
      else next.add(controlId);
      return next;
    });
  };

  const toggleSelectAll = () => {
    if (selected.size === items.length) {
      setSelected(new Set());
    } else {
      setSelected(new Set(items.map(n => n.controlId)));
    }
  };

  const toggleExpand = (id: string) => {
    setExpanded(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleBulkUpdate = async () => {
    if (!systemId || selected.size === 0) return;
    setUpdating(true);
    try {
      await bulkUpdateNarratives(systemId, {
        controlIds: Array.from(selected),
        implementationStatus: bulkStatus || undefined,
        approvalStatus: bulkApproval || undefined,
      });
      setSelected(new Set());
      setBulkStatus('');
      setBulkApproval('');
      refresh();
    } finally {
      setUpdating(false);
    }
  };

  if (!systemId) return null;

  return (
    <PageLayout title="Narratives">
      <div className="space-y-6 p-6">
        {/* Breadcrumb */}
        <nav className="text-sm text-gray-500">
          <Link to="/" className="hover:text-blue-600">Portfolio</Link>
          <span className="mx-1">/</span>
          <Link to={`/systems/${systemId}`} className="hover:text-blue-600">System</Link>
          <span className="mx-1">/</span>
          <span className="text-gray-900 font-medium">Narratives</span>
        </nav>

        {/* Header */}
        <div>
          <h2 className="text-xl font-bold text-gray-900">Control Narratives</h2>
          <p className="mt-1 text-sm text-gray-500">View and manage control implementation narratives for this system.</p>
        </div>

        {/* Progress bar */}
        {stats.total > 0 && (
          <div className="rounded-lg border border-gray-200 bg-white p-4">
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm font-medium text-gray-700">Implementation Progress</span>
              <span className="text-sm text-gray-500">{stats.implemented} of {stats.total} implemented</span>
            </div>
            <div className="h-3 w-full overflow-hidden rounded-full bg-gray-200">
              <div className="flex h-full">
                <div className="bg-green-500 transition-all" style={{ width: `${(stats.implemented / stats.total) * 100}%` }} />
                <div className="bg-amber-400 transition-all" style={{ width: `${(stats.partial / stats.total) * 100}%` }} />
                <div className="bg-blue-400 transition-all" style={{ width: `${(stats.planned / stats.total) * 100}%` }} />
              </div>
            </div>
            <div className="mt-2 flex items-center gap-4 text-xs text-gray-500">
              <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-green-500" /> Implemented ({stats.implemented})</span>
              <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-amber-400" /> Partial ({stats.partial})</span>
              <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-blue-400" /> Planned ({stats.planned})</span>
              <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-gray-300" /> Approved ({stats.approved})</span>
              {stats.ai > 0 && <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-purple-400" /> AI-generated ({stats.ai})</span>}
            </div>
          </div>
        )}

        {/* Summary cards */}
        <div className="grid grid-cols-5 gap-4">
          <div className="rounded-lg border border-gray-200 bg-white p-4 text-center">
            <p className="text-xs font-medium text-gray-500 uppercase">Total</p>
            <p className="mt-1 text-2xl font-bold text-gray-900">{stats.total}</p>
          </div>
          <div className="rounded-lg border border-gray-200 bg-white p-4 text-center">
            <p className="text-xs font-medium text-gray-500 uppercase">Implemented</p>
            <p className="mt-1 text-2xl font-bold text-green-600">{stats.implemented}</p>
          </div>
          <div className="rounded-lg border border-gray-200 bg-white p-4 text-center">
            <p className="text-xs font-medium text-gray-500 uppercase">Partial</p>
            <p className="mt-1 text-2xl font-bold text-amber-600">{stats.partial}</p>
          </div>
          <div className="rounded-lg border border-gray-200 bg-white p-4 text-center">
            <p className="text-xs font-medium text-gray-500 uppercase">Approved</p>
            <p className="mt-1 text-2xl font-bold text-blue-600">{stats.approved}</p>
          </div>
          <div className="rounded-lg border border-gray-200 bg-white p-4 text-center">
            <p className="text-xs font-medium text-gray-500 uppercase">AI Suggested</p>
            <p className="mt-1 text-2xl font-bold text-purple-600">{stats.ai}</p>
          </div>
        </div>

        {/* Filters */}
        <div className="flex flex-wrap items-center gap-3">
          <div className="flex items-center gap-2">
            <label className="text-xs font-medium text-gray-500">Family:</label>
            <select value={familyFilter} onChange={e => setFamilyFilter(e.target.value)} className="rounded-md border border-gray-300 px-2 py-1 text-sm">
              {FAMILIES.map(f => <option key={f} value={f}>{f}{f !== 'All' && familyCounts[f] ? ` (${familyCounts[f]})` : ''}</option>)}
            </select>
          </div>
          <div className="flex items-center gap-2">
            <label className="text-xs font-medium text-gray-500">Status:</label>
            <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)} className="rounded-md border border-gray-300 px-2 py-1 text-sm">
              {STATUSES.map(s => <option key={s} value={s}>{s}</option>)}
            </select>
          </div>
          <input
            type="text"
            placeholder="Search controls..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="rounded-md border border-gray-300 px-3 py-1 text-sm shadow-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
          />
        </div>

        {/* Bulk actions toolbar */}
        {selected.size > 0 && (
          <div className="flex items-center gap-3 rounded-lg border border-blue-200 bg-blue-50 p-3">
            <span className="text-sm font-medium text-blue-700">{selected.size} selected</span>
            <select value={bulkStatus} onChange={e => setBulkStatus(e.target.value)} className="rounded-md border border-gray-300 px-2 py-1 text-sm">
              <option value="">Set status...</option>
              <option value="Implemented">Implemented</option>
              <option value="PartiallyImplemented">Partially Implemented</option>
              <option value="Planned">Planned</option>
              <option value="NotApplicable">Not Applicable</option>
            </select>
            <select value={bulkApproval} onChange={e => setBulkApproval(e.target.value)} className="rounded-md border border-gray-300 px-2 py-1 text-sm">
              <option value="">Set approval...</option>
              <option value="Draft">Draft</option>
              <option value="UnderReview">Under Review</option>
              <option value="Approved">Approved</option>
              <option value="NeedsRevision">Needs Revision</option>
            </select>
            <button
              type="button"
              disabled={updating || (!bulkStatus && !bulkApproval)}
              onClick={handleBulkUpdate}
              className="rounded-md bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {updating ? 'Updating...' : 'Apply'}
            </button>
            <button type="button" onClick={() => setSelected(new Set())} className="text-sm text-gray-500 hover:text-gray-700">Clear</button>
          </div>
        )}

        {/* Loading / error */}
        {loading && !narratives && (
          <div className="flex items-center justify-center py-16">
            <div className="h-8 w-8 animate-spin rounded-full border-4 border-blue-500 border-t-transparent" />
          </div>
        )}
        {error && (
          <div className="rounded-md bg-red-50 p-4 text-sm text-red-700">{String(error)}</div>
        )}

        {/* Table */}
        {narratives && (
          <div className="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
            <table className="min-w-full divide-y divide-gray-200 text-sm">
              <thead className="bg-gray-50">
                <tr>
                  <th className="px-3 py-3 w-8">
                    <input
                      type="checkbox"
                      checked={selected.size === items.length && items.length > 0}
                      onChange={toggleSelectAll}
                      className="rounded border-gray-300"
                    />
                  </th>
                  <th className="px-3 py-3 text-left font-medium text-gray-500">Control</th>
                  <th className="px-3 py-3 text-left font-medium text-gray-500">Family</th>
                  <th className="px-3 py-3 text-left font-medium text-gray-500">Status</th>
                  <th className="px-3 py-3 text-left font-medium text-gray-500">Approval</th>
                  <th className="px-3 py-3 text-left font-medium text-gray-500">Author</th>
                  <th className="px-3 py-3 text-center font-medium text-gray-500">Ver</th>
                  <th className="px-3 py-3 text-center font-medium text-gray-500">AI</th>
                  <th className="px-3 py-3 w-8" />
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {items.length === 0 ? (
                  <tr>
                    <td colSpan={9} className="px-4 py-12 text-center text-gray-400">
                      No narratives found. Select a baseline to auto-populate controls.
                    </td>
                  </tr>
                ) : items.map((n) => (
                  <Fragment key={n.id}>
                    <tr className={`hover:bg-gray-50 ${selected.has(n.controlId) ? 'bg-blue-50' : ''}`}>
                      <td className="px-3 py-3">
                        <input
                          type="checkbox"
                          checked={selected.has(n.controlId)}
                          onChange={() => toggleSelect(n.controlId)}
                          className="rounded border-gray-300"
                        />
                      </td>
                      <td className="px-3 py-3 font-medium text-gray-900">{n.controlId}</td>
                      <td className="px-3 py-3 text-gray-600">{n.family}</td>
                      <td className="px-3 py-3"><StatusBadge status={n.implementationStatus} variant={implVariant(n.implementationStatus)} /></td>
                      <td className="px-3 py-3"><StatusBadge status={n.approvalStatus} variant={approvalVariant(n.approvalStatus)} /></td>
                      <td className="px-3 py-3 text-gray-500">{n.authoredBy ?? '—'}</td>
                      <td className="px-3 py-3 text-center text-gray-500">{n.version}</td>
                      <td className="px-3 py-3 text-center">
                        {n.aiSuggested ? (
                          <span className="inline-flex items-center rounded-full bg-purple-100 px-2 py-0.5 text-xs font-medium text-purple-700" title="AI-generated narrative">AI</span>
                        ) : n.isAutoPopulated ? (
                          <span className="inline-flex items-center rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-500" title="Auto-populated">Auto</span>
                        ) : null}
                      </td>
                      <td className="px-3 py-3">
                        <button type="button" onClick={() => toggleExpand(n.id)} className="text-gray-400 hover:text-gray-600" title="Expand">
                          <svg className={`h-4 w-4 transition-transform ${expanded.has(n.id) ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                            <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
                          </svg>
                        </button>
                      </td>
                    </tr>
                    {expanded.has(n.id) && (
                      <tr className="bg-gray-50">
                        <td colSpan={9} className="px-6 py-4">
                          <div className="space-y-2">
                            <div className="flex items-center gap-4 text-xs text-gray-500">
                              <span>Authored: {formatDate(n.authoredAt)}</span>
                              <span>Version: {n.version}</span>
                              {n.aiSuggested && <span className="text-purple-600 font-medium">AI-generated narrative</span>}
                            </div>
                            <div className="rounded-md border border-gray-200 bg-white p-4 text-sm text-gray-700 whitespace-pre-wrap">
                              {n.narrative || <span className="italic text-gray-400">No narrative content.</span>}
                            </div>
                          </div>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </PageLayout>
  );
}
