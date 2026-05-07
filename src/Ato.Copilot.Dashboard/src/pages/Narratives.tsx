import { useState, useCallback, useMemo, useRef, useEffect, Fragment } from 'react';
import { useParams } from 'react-router-dom';
import { usePolling } from '../hooks/usePolling';
import { useSettings } from '../hooks/useSettings';
import { getNarratives, bulkUpdateNarratives, saveNarrative, regenerateNarrative, getAvailableControls, createNarrative } from '../api/narratives';
import { getBusinessContext } from '../api/businessContext';
import type { NarrativeListItem, AvailableControl } from '../api/narratives';
import type { BusinessContextDraftResponse } from '../types/dashboard';
import EvidenceSection from '../components/EvidenceSection';

// ─── Helpers ────────────────────────────────────────────────────────────────

function StatusBadge({ status, variant }: { status: string; variant?: 'green' | 'amber' | 'red' | 'blue' | 'gray' }) {
  const colors = {
    green: 'bg-green-100 text-green-700',
    amber: 'bg-amber-100 text-amber-700',
    red: 'bg-red-100 text-red-700',
    blue: 'bg-indigo-100 text-indigo-700',
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

type ControlActivity = {
  name: string;
  description: string;
};

function parseControlActivities(narrative: string | null | undefined): ControlActivity[] {
  if (!narrative) return [];

  const normalized = narrative.replace(/\r/g, '');
  const pattern = /Activity Name:\s*(.+?)(?:\n|\s+)Activity Description:\s*([\s\S]*?)(?=(?:\n\s*(?:-\s*)?Activity Name:)|\n\n|$)/gi;
  const activities: ControlActivity[] = [];

  let match: RegExpExecArray | null;
  while ((match = pattern.exec(normalized)) !== null) {
    const name = (match[1] ?? '').trim();
    const description = (match[2] ?? '').replace(/^[-:\s]+/, '').trim();
    if (!name && !description) continue;
    activities.push({ name, description });
  }

  return activities;
}

const FAMILIES = ['All', 'AC', 'AU', 'AT', 'CA', 'CM', 'CP', 'IA', 'IR', 'MA', 'MP', 'PE', 'PL', 'PM', 'PS', 'RA', 'SA', 'SC', 'SI', 'SR'];
const STATUSES = ['All', 'Implemented', 'PartiallyImplemented', 'Planned', 'NotApplicable'];
const IMPL_STATUSES = ['Planned', 'Implemented', 'PartiallyImplemented', 'NotApplicable'];

// ─── Add Narrative Dialog ───────────────────────────────────────────────────

function AddNarrativeDialog({
  systemId,
  onClose,
  onCreated,
}: {
  systemId: string;
  onClose: () => void;
  onCreated: () => void;
}) {
  const [controlSearch, setControlSearch] = useState('');
  const [controls, setControls] = useState<AvailableControl[]>([]);
  const [loadingControls, setLoadingControls] = useState(false);
  const [selectedControl, setSelectedControl] = useState<AvailableControl | null>(null);
  const [narrative, setNarrative] = useState('');
  const [implStatus, setImplStatus] = useState('Planned');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState('');
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Load controls on mount and when search changes
  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setLoadingControls(true);
      getAvailableControls(systemId, controlSearch || undefined)
        .then(setControls)
        .catch(() => setControls([]))
        .finally(() => setLoadingControls(false));
    }, 300);
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, [systemId, controlSearch]);

  const handleSubmit = async () => {
    if (!selectedControl) return;
    setSubmitting(true);
    setError('');
    try {
      await createNarrative(systemId, {
        controlId: selectedControl.id,
        narrative: narrative || undefined,
        implementationStatus: implStatus,
      });
      onCreated();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Failed to create narrative';
      setError(msg);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-lg rounded-lg bg-white shadow-xl">
        <div className="border-b border-gray-200 px-6 py-4">
          <h3 className="text-lg font-semibold text-gray-900">Add Narrative</h3>
          <p className="mt-1 text-sm text-gray-500">Create a new control implementation narrative.</p>
        </div>
        <div className="space-y-4 px-6 py-4">
          {/* Control Picker */}
          {!selectedControl ? (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Select Control *</label>
              <input
                type="text"
                value={controlSearch}
                onChange={e => setControlSearch(e.target.value)}
                placeholder="Search controls (e.g. ac-2, audit)..."
                className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
              <div className="mt-2 max-h-48 overflow-y-auto rounded-md border border-gray-200 bg-gray-50">
                {loadingControls ? (
                  <p className="px-3 py-2 text-sm text-gray-400">Loading...</p>
                ) : controls.length === 0 ? (
                  <p className="px-3 py-2 text-sm text-gray-400 italic">No available controls found</p>
                ) : (
                  controls.map(c => (
                    <button
                      key={c.id}
                      type="button"
                      onClick={() => setSelectedControl(c)}
                      className="flex w-full items-center justify-between px-3 py-2 text-left text-sm hover:bg-indigo-50"
                    >
                      <span className="font-medium text-gray-900">{c.id}</span>
                      <span className="ml-2 truncate text-gray-500">{c.title}</span>
                    </button>
                  ))
                )}
              </div>
            </div>
          ) : (
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Control</label>
              <div className="flex items-center gap-2 rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-sm">
                <span className="font-medium text-gray-900">{selectedControl.id}</span>
                <span className="text-gray-500">{selectedControl.title}</span>
                <button
                  type="button"
                  onClick={() => setSelectedControl(null)}
                  className="ml-auto text-gray-400 hover:text-gray-600"
                >
                  ✕
                </button>
              </div>
            </div>
          )}

          {/* Status */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Implementation Status</label>
            <select
              value={implStatus}
              onChange={e => setImplStatus(e.target.value)}
              className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm"
            >
              {IMPL_STATUSES.map(s => <option key={s} value={s}>{s}</option>)}
            </select>
          </div>

          {/* Narrative Text */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Narrative</label>
            <textarea
              value={narrative}
              onChange={e => setNarrative(e.target.value)}
              rows={5}
              placeholder="Enter the implementation narrative text (optional — can be added later)..."
              className="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>

          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>
        <div className="flex justify-end gap-2 border-t border-gray-200 px-6 py-4">
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={handleSubmit}
            disabled={!selectedControl || submitting}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
          >
            {submitting ? 'Creating...' : 'Create'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ─── Component ──────────────────────────────────────────────────────────────

export default function Narratives() {
  const { id: systemId } = useParams<{ id: string }>();
  const { settings } = useSettings();
  const [familyFilter, setFamilyFilter] = useState('All');
  const [statusFilter, setStatusFilter] = useState('All');
  const [search, setSearch] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [bulkStatus, setBulkStatus] = useState('');
  const [bulkApproval, setBulkApproval] = useState('');
  const [updating, setUpdating] = useState(false);
  const [editedNarratives, setEditedNarratives] = useState<Record<string, string>>({});
  const [savingIds, setSavingIds] = useState<Set<string>>(new Set());
  const [regeneratingIds, setRegeneratingIds] = useState<Set<string>>(new Set());
  const [savedIds, setSavedIds] = useState<Set<string>>(new Set());
  const savedTimers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});
  const [showAddDialog, setShowAddDialog] = useState(false);
  const [regenError, setRegenError] = useState('');
  const [businessContextCache, setBusinessContextCache] = useState<Record<string, BusinessContextDraftResponse | null>>({});

  const buildConfiguredSourceUrls = () => {
    if (!settings?.sharePointSiteUrl || !settings?.sourceDocuments) {
      return [];
    }
    const base = settings.sharePointSiteUrl.trim().replace(/\/+$/, '');
    const lines = settings.sourceDocuments
      .split(/\r?\n|,/) 
      .map(x => x.trim())
      .filter(Boolean);

    if (lines.length === 0) return [] as string[];

    return lines
      .map((line) => {
        if (/^https?:\/\//i.test(line)) return line;
        if (!base) return null;
        return `${base}/${line.replace(/^\/+/, '')}`;
      })
      .filter((x): x is string => Boolean(x));
  };

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
      else {
        next.add(id);
        // Fetch business context on expand if not cached
        const item = items.find(n => n.id === id);
        if (item && systemId && !(item.controlId in businessContextCache)) {
          getBusinessContext(systemId, item.controlId)
            .then(bc => setBusinessContextCache(c => ({ ...c, [item.controlId]: bc })))
            .catch(() => setBusinessContextCache(c => ({ ...c, [item.controlId]: null })));
        }
      }
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

  const handleNarrativeBlur = async (controlId: string, text: string, original: string | null) => {
    if (text === (original ?? '')) return; // No change
    if (!systemId) return;
    setSavingIds(prev => new Set([...prev, controlId]));
    try {
      await saveNarrative(systemId, controlId, text);
      setSavedIds(prev => new Set([...prev, controlId]));
      // Clear "Saved" indicator after 2s
      if (savedTimers.current[controlId]) clearTimeout(savedTimers.current[controlId]);
      savedTimers.current[controlId] = setTimeout(() => {
        setSavedIds(prev => { const next = new Set(prev); next.delete(controlId); return next; });
      }, 2000);
      refresh();
    } finally {
      setSavingIds(prev => { const next = new Set(prev); next.delete(controlId); return next; });
    }
  };

  const handleRegenerate = async (controlId: string) => {
    if (!systemId) return;
    setRegeneratingIds(prev => new Set([...prev, controlId]));
    setRegenError('');
    try {
      const sourceUrls = buildConfiguredSourceUrls();
      const newNarrative = await regenerateNarrative(
        systemId,
        controlId,
        sourceUrls.length > 0 ? { sourceUrls } : undefined,
      );
      if (newNarrative) {
        setEditedNarratives(prev => ({ ...prev, [controlId]: newNarrative }));
      }
      refresh();
    } catch (err: unknown) {
      const resp = (err as { response?: { status?: number; data?: { error?: string; errorCode?: string } } })?.response;
      if (resp?.status === 503) {
        setRegenError(`Regeneration failed for ${controlId}: AI service is not configured.`);
      } else if (resp?.data?.errorCode === 'NO_CAPABILITY') {
        setRegenError(`Regeneration failed for ${controlId}: No security capability is linked to this control. Assign a capability first.`);
      } else {
        const msg = resp?.data?.error || (err instanceof Error ? err.message : 'Unknown error');
        setRegenError(`Regeneration failed for ${controlId}: ${msg}`);
      }
    } finally {
      setRegeneratingIds(prev => { const next = new Set(prev); next.delete(controlId); return next; });
    }
  };

  if (!systemId) return null;

  return (
    <div className="space-y-6">
        {/* Header */}
        <div className="flex items-start justify-between">
          <div>
            <h2 className="text-xl font-bold text-gray-900">Control Narratives</h2>
            <p className="mt-1 text-sm text-gray-500">View and manage control implementation narratives for this system.</p>
          </div>
          <button
            onClick={() => setShowAddDialog(true)}
            className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700"
          >
            + Add Narrative
          </button>
        </div>

        {showAddDialog && (
          <AddNarrativeDialog
            systemId={systemId!}
            onClose={() => setShowAddDialog(false)}
            onCreated={() => { setShowAddDialog(false); refresh(); }}
          />
        )}

        {/* Document source indicator */}
        {(() => {
          const configuredSources = buildConfiguredSourceUrls();
          if (configuredSources.length > 0) {
            return (
              <div className="flex items-center gap-2 rounded-lg border border-teal-200 bg-teal-50 px-4 py-2.5 text-sm text-teal-800">
                <svg className="h-4 w-4 flex-shrink-0 text-teal-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                </svg>
                <span>
                  <span className="font-medium">Document sources active</span> — Regenerate uses{' '}
                  <span className="font-medium">{configuredSources.length} configured source{configuredSources.length !== 1 ? 's' : ''}</span> from SharePoint to ground narratives in your policy documents.
                </span>
                <span
                  title={configuredSources.join('\n')}
                  className="ml-auto cursor-default rounded bg-teal-100 px-2 py-0.5 text-xs font-medium text-teal-700"
                >
                  {configuredSources.length} source{configuredSources.length !== 1 ? 's' : ''}
                </span>
              </div>
            );
          }
          return (
            <div className="flex items-center gap-2 rounded-lg border border-gray-200 bg-gray-50 px-4 py-2.5 text-sm text-gray-500">
              <svg className="h-4 w-4 flex-shrink-0 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
              </svg>
              <span>
                No document sources configured — Regenerate uses security capability data.{' '}
                Configure SharePoint sources in{' '}
                <span className="font-medium text-gray-700">Settings → Documents To Consume</span>{' '}
                to generate narratives from your policy documents instead.
              </span>
            </div>
          );
        })()}

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
                <div className="bg-indigo-400 transition-all" style={{ width: `${(stats.planned / stats.total) * 100}%` }} />
              </div>
            </div>
            <div className="mt-2 flex items-center gap-4 text-xs text-gray-500">
              <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-green-500" /> Implemented ({stats.implemented})</span>
              <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-amber-400" /> Partial ({stats.partial})</span>
              <span className="flex items-center gap-1"><span className="h-2 w-2 rounded-full bg-indigo-400" /> Planned ({stats.planned})</span>
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
            <p className="mt-1 text-2xl font-bold text-indigo-600">{stats.approved}</p>
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
            className="rounded-md border border-gray-300 px-3 py-1 text-sm shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
          />
        </div>

        {/* Bulk actions toolbar */}
        {selected.size > 0 && (
          <div className="flex items-center gap-3 rounded-lg border border-indigo-200 bg-indigo-50 p-3">
            <span className="text-sm font-medium text-indigo-700">{selected.size} selected</span>
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
              className="rounded-md bg-indigo-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
            >
              {updating ? 'Updating...' : 'Apply'}
            </button>
            <button type="button" onClick={() => setSelected(new Set())} className="text-sm text-gray-500 hover:text-gray-700">Clear</button>
          </div>
        )}

        {/* Loading / error */}
        {loading && !narratives && (
          <div className="flex items-center justify-center py-16">
            <div className="h-8 w-8 animate-spin rounded-full border-4 border-indigo-500 border-t-transparent" />
          </div>
        )}
        {error && (
          <div className="rounded-md bg-red-50 p-4 text-sm text-red-700">{String(error)}</div>
        )}
        {regenError && (
          <div className="flex items-center justify-between rounded-md bg-amber-50 border border-amber-200 p-4 text-sm text-amber-700">
            <span>{regenError}</span>
            <button onClick={() => setRegenError('')} className="text-amber-500 hover:text-amber-700 text-xs font-medium ml-4">Dismiss</button>
          </div>
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
                    <tr className={`hover:bg-gray-50 ${selected.has(n.controlId) ? 'bg-indigo-50' : ''}`}>
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
                          {(() => {
                            const narrativeValue = editedNarratives[n.controlId] ?? n.narrative ?? '';
                            const activities = parseControlActivities(narrativeValue);
                            return (
                          <div className="space-y-2">
                            <div className="flex items-center justify-between text-xs text-gray-500">
                              <div className="flex items-center gap-4">
                                <span>Authored: {formatDate(n.authoredAt)}</span>
                                <span>Version: {n.version}</span>
                                {n.aiSuggested && <span className="text-purple-600 font-medium">AI-generated narrative</span>}
                                {savingIds.has(n.controlId) && <span className="text-indigo-600 font-medium">Saving…</span>}
                                {savedIds.has(n.controlId) && !savingIds.has(n.controlId) && <span className="text-green-600 font-medium">Saved ✓</span>}
                              </div>
                              <button
                                className="inline-flex items-center gap-1 rounded bg-purple-600 px-3 py-1 text-xs font-medium text-white hover:bg-purple-700 disabled:opacity-50"
                                disabled={regeneratingIds.has(n.controlId)}
                                onClick={() => handleRegenerate(n.controlId)}
                              >
                                {regeneratingIds.has(n.controlId) ? 'Regenerating…' : 'Regenerate'}
                              </button>
                            </div>
                            <textarea
                              className="w-full rounded-md border border-gray-200 bg-white p-4 text-sm text-gray-700 min-h-[120px] resize-y focus:border-indigo-400 focus:ring-1 focus:ring-indigo-400"
                              value={narrativeValue}
                              onChange={e => setEditedNarratives(prev => ({ ...prev, [n.controlId]: e.target.value }))}
                              onBlur={e => handleNarrativeBlur(n.controlId, e.target.value, n.narrative)}
                            />
                            {activities.length > 0 && (
                              <div className="rounded-lg border border-indigo-200 bg-indigo-50 p-3">
                                <p className="text-xs font-semibold uppercase tracking-wide text-indigo-700">Control Activities</p>
                                <div className="mt-2 space-y-2">
                                  {activities.map((activity, idx) => (
                                    <div key={`${n.controlId}-activity-${idx}`} className="rounded border border-indigo-100 bg-white p-2">
                                      <p className="text-sm font-medium text-gray-900">{activity.name || 'Unnamed Activity'}</p>
                                      {activity.description && (
                                        <p className="mt-1 text-sm text-gray-700 whitespace-pre-wrap">{activity.description}</p>
                                      )}
                                    </div>
                                  ))}
                                </div>
                              </div>
                            )}
                            {systemId && (
                              <EvidenceSection
                                systemId={systemId}
                                controlId={n.controlId}
                                controlImplementationId={n.id}
                              />
                            )}
                            {/* Business Context Side Panel (T048/T049) */}
                            {(() => {
                              const bc = businessContextCache[n.controlId];
                              if (bc) {
                                return (
                                  <div className="mt-3 rounded-lg border border-indigo-200 bg-indigo-50 p-4 space-y-2">
                                    <div className="flex items-center justify-between">
                                      <span className="text-xs font-semibold text-indigo-700">Mission Owner Business Context</span>
                                      <StatusBadge status={bc.governanceStatus} variant={approvalVariant(bc.governanceStatus)} />
                                    </div>
                                    <p className="text-sm text-gray-700 whitespace-pre-wrap">{bc.content}</p>
                                    <div className="flex items-center gap-3 text-xs text-gray-500">
                                      <span>By {bc.authoredBy}</span>
                                      <span>{formatDate(bc.authoredAt)}</span>
                                    </div>
                                    {settings.role === 'ISSO' && (
                                      <button
                                        type="button"
                                        className="text-xs text-indigo-600 font-medium hover:underline"
                                        onClick={() => setEditedNarratives(prev => ({
                                          ...prev,
                                          [n.controlId]: (prev[n.controlId] ?? n.narrative ?? '') + '\n\n' + bc.content,
                                        }))}
                                      >
                                        Copy to Narrative
                                      </button>
                                    )}
                                  </div>
                                );
                              }
                              if (bc === null) {
                                return (
                                  <p className="mt-3 text-xs text-gray-400 italic">
                                    Awaiting business context from Mission Owner
                                  </p>
                                );
                              }
                              return null;
                            })()}
                          </div>
                            );
                          })()}
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
  );
}
