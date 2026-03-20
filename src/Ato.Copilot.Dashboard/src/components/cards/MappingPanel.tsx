import { useState, useEffect } from 'react';
import type { CapabilityMappingDto, BoundaryDefinitionDto } from '../../types/dashboard';
import { getCapabilityMappings, createCapabilityMappings } from '../../api/capabilities';
import { fetchBoundaryDefinitions } from '../../api/boundaries';
import apiClient from '../../api/client';

const narrativeStatusBadge: Record<string, string> = {
  Populated: 'bg-green-100 text-green-800',
  Empty: 'bg-gray-100 text-gray-600',
  Customized: 'bg-purple-100 text-purple-800',
};

interface MappingPanelProps {
  capabilityId: string;
  systemId?: string;
}

export function MappingPanel({ capabilityId, systemId }: MappingPanelProps) {
  const [mappings, setMappings] = useState<CapabilityMappingDto[]>([]);
  const [boundaries, setBoundaries] = useState<BoundaryDefinitionDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [showPicker, setShowPicker] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const fetchMappings = async () => {
    try {
      const data = await getCapabilityMappings(capabilityId);
      setMappings(data.mappings);
    } catch {
      setError('Failed to load mappings');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchMappings();
    if (systemId) {
      fetchBoundaryDefinitions(systemId).then(setBoundaries).catch(() => {});
    }
  }, [capabilityId, systemId]);

  const handleAddControls = async (selectedControls: { controlId: string; role: 'Primary' | 'Supporting' | 'Shared'; boundaryDefinitionId?: string }[]) => {
    setError(null);
    try {
      const result = await createCapabilityMappings(capabilityId, {
        mappings: selectedControls,
      });
      if (result.warnings?.length) {
        setError(result.warnings.map((w) => w.message).join('; '));
      }
      if (result.created > 0) {
        await fetchMappings();
      }
    } catch (err: any) {
      setError(err?.response?.data?.error ?? 'Failed to create mapping');
    }
  };

  // Group by family, then deduplicate by controlId within each family
  const groupedMappings = mappings.reduce<Record<string, CapabilityMappingDto[]>>((acc, m) => {
    const family = m.controlFamily ?? 'Other';
    (acc[family] ??= []).push(m);
    return acc;
  }, {});

  // Deduplicate: merge rows with the same controlId into one representative row
  const deduped = (items: CapabilityMappingDto[]) => {
    const byControl = new Map<string, CapabilityMappingDto[]>();
    for (const m of items) {
      const key = m.controlId.toLowerCase();
      (byControl.get(key) ?? (() => { const a: CapabilityMappingDto[] = []; byControl.set(key, a); return a; })()).push(m);
    }
    return Array.from(byControl.values()).map(group => ({
      representative: group[0]!,
      systemCount: new Set(group.map(m => m.registeredSystemId).filter(Boolean)).size,
      allIds: group.map(m => m.id),
    }));
  };

  // Unique control count for the header
  const uniqueControlCount = new Set(mappings.map(m => m.controlId.toLowerCase())).size;

  if (loading) return <div className="text-sm text-gray-400 py-2">Loading mappings...</div>;

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h4 className="text-sm font-semibold text-gray-700">Control Mappings ({uniqueControlCount})</h4>
        <button
          onClick={() => setShowPicker(true)}
          className="text-xs text-blue-600 hover:text-blue-800"
        >
          + Add Mapping
        </button>
      </div>

      {error && <div className="text-xs text-red-600 bg-red-50 p-2 rounded">{error}</div>}

      {showPicker && (
        <ControlPickerDialog
          existingControlIds={mappings.map(m => m.controlId.toUpperCase())}
          baselineLevel=""
          boundaries={boundaries}
          onAdd={async (selected) => {
            await handleAddControls(selected);
            setShowPicker(false);
          }}
          onClose={() => setShowPicker(false)}
        />
      )}

      {mappings.length === 0 ? (
        <p className="text-sm text-gray-400 italic">No control mappings yet</p>
      ) : (
        Object.entries(groupedMappings)
          .sort(([a], [b]) => a.localeCompare(b))
          .map(([family, items]) => (
            <div key={family}>
              <h5 className="text-xs font-medium text-gray-500 uppercase tracking-wide mb-1">{family}</h5>
              <div className="space-y-1">
                {deduped(items)
                  .sort((a, b) => a.representative.controlId.localeCompare(b.representative.controlId))
                  .map(({ representative: m, systemCount }) => (
                    <div
                      key={m.controlId}
                      className="flex items-center justify-between px-3 py-2 bg-gray-50 rounded text-sm"
                    >
                      <div className="flex items-center gap-2">
                        <span className="font-mono text-xs font-medium">{m.controlId}</span>
                        <span className="text-gray-500 truncate max-w-xs">{m.controlTitle}</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs text-gray-400">{m.role}</span>
                        <span className="text-xs px-1.5 py-0.5 rounded bg-blue-50 text-blue-700">
                          {systemCount > 1 ? `${systemCount} Systems` : m.registeredSystemName ?? m.boundaryDefinitionName ?? 'All Systems'}
                        </span>
                        <span className={`text-xs px-1.5 py-0.5 rounded ${narrativeStatusBadge[m.narrativeStatus] ?? 'bg-gray-100 text-gray-600'}`}>
                          {m.narrativeStatus}
                        </span>
                      </div>
                    </div>
                  ))}
              </div>
            </div>
          ))
      )}
    </div>
  );
}

// ─── Control Picker Dialog ──────────────────────────────────────────────────

interface PickerControl {
  id: string;
  family: string;
  familyName: string;
  title: string;
  type: string;
}

interface ControlPickerDialogProps {
  existingControlIds: string[];
  baselineLevel: string;
  boundaries: BoundaryDefinitionDto[];
  onAdd: (controls: { controlId: string; role: 'Primary' | 'Supporting' | 'Shared'; boundaryDefinitionId?: string }[]) => Promise<void>;
  onClose: () => void;
}

function ControlPickerDialog({ existingControlIds, baselineLevel, boundaries, onAdd, onClose }: ControlPickerDialogProps) {
  const [controls, setControls] = useState<PickerControl[]>([]);
  const [search, setSearch] = useState('');
  const [familyFilter, setFamilyFilter] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [role, setRole] = useState<'Primary' | 'Supporting' | 'Shared'>('Primary');
  const [boundaryId, setBoundaryId] = useState('');
  const [loading, setLoading] = useState(true);
  const [adding, setAdding] = useState(false);

  useEffect(() => {
    apiClient.get('/controls', { params: { pageSize: 2000 } })
      .then(res => setControls(res.data.items))
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  const filtered = controls.filter(c => {
    if (existingControlIds.includes(c.id.toUpperCase())) return false;
    if (search && !c.id.toLowerCase().includes(search.toLowerCase()) && !c.title.toLowerCase().includes(search.toLowerCase())) return false;
    if (familyFilter && c.family !== familyFilter) return false;
    return true;
  });

  const families = [...new Set(controls.map(c => c.family))].sort();

  const handleToggle = (id: string) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  const handleSubmit = async () => {
    if (selected.size === 0) return;
    setAdding(true);
    await onAdd(
      [...selected].map(controlId => ({
        controlId,
        role,
        boundaryDefinitionId: boundaryId || undefined,
      })),
    );
    setAdding(false);
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center" onClick={onClose}>
      <div className="absolute inset-0 bg-black/30" />
      <div
        className="relative w-full max-w-3xl max-h-[80vh] bg-white rounded-xl shadow-xl flex flex-col"
        onClick={e => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between border-b px-6 py-4">
          <div>
            <h3 className="text-lg font-bold text-gray-900">Select Controls</h3>
            <p className="text-sm text-gray-500">
              Choose controls from the {baselineLevel} baseline to map to this capability
            </p>
          </div>
          <button onClick={onClose} className="rounded p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-600">
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Filters */}
        <div className="flex items-center gap-3 border-b px-6 py-3">
          <div className="relative flex-1">
            <svg className="absolute left-2.5 top-2 h-4 w-4 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            <input
              type="text"
              placeholder="Search controls..."
              value={search}
              onChange={e => setSearch(e.target.value)}
              className="w-full rounded-md border border-gray-300 py-1.5 pl-8 pr-3 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
              autoFocus
            />
          </div>
          <select
            value={familyFilter}
            onChange={e => setFamilyFilter(e.target.value)}
            className="rounded-md border border-gray-300 py-1.5 px-3 text-sm"
          >
            <option value="">All families</option>
            {families.map(f => (
              <option key={f} value={f}>{f}</option>
            ))}
          </select>
          <div className="flex items-center gap-2">
            <select
              value={role}
              onChange={e => setRole(e.target.value as 'Primary' | 'Supporting' | 'Shared')}
              className="rounded-md border border-gray-300 py-1.5 px-3 text-sm"
            >
              <option value="Primary">Primary</option>
              <option value="Supporting">Supporting</option>
              <option value="Shared">Shared</option>
            </select>
            {boundaries.length > 0 && (
              <select
                value={boundaryId}
                onChange={e => setBoundaryId(e.target.value)}
                className="rounded-md border border-gray-300 py-1.5 px-3 text-sm"
              >
                <option value="">All Systems</option>
                {boundaries.map(b => (
                  <option key={b.id} value={b.id}>{b.name}</option>
                ))}
              </select>
            )}
          </div>
        </div>

        {/* Control list */}
        <div className="flex-1 overflow-y-auto px-6 py-3">
          {loading ? (
            <div className="text-center text-gray-400 py-8">Loading controls...</div>
          ) : filtered.length === 0 ? (
            <div className="text-center text-gray-400 py-8">No matching controls found</div>
          ) : (
            <div className="space-y-1">
              {filtered.slice(0, 200).map(c => (
                <label
                  key={c.id}
                  className={`flex items-center gap-3 rounded px-3 py-2 cursor-pointer transition-colors ${
                    selected.has(c.id) ? 'bg-blue-50 border border-blue-200' : 'hover:bg-gray-50 border border-transparent'
                  }`}
                >
                  <input
                    type="checkbox"
                    checked={selected.has(c.id)}
                    onChange={() => handleToggle(c.id)}
                    className="h-4 w-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                  />
                  <span className="font-mono text-xs font-semibold text-gray-800 w-20 flex-shrink-0">{c.id}</span>
                  <span className="text-sm text-gray-600 truncate">{c.title}</span>
                  <span className="ml-auto text-xs text-gray-400 flex-shrink-0">{c.type}</span>
                </label>
              ))}
              {filtered.length > 200 && (
                <p className="text-xs text-gray-400 text-center py-2">
                  Showing first 200 of {filtered.length} controls. Use search to narrow results.
                </p>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t px-6 py-4">
          <span className="text-sm text-gray-500">
            {selected.size} control{selected.size !== 1 ? 's' : ''} selected
          </span>
          <div className="flex gap-2">
            <button
              onClick={onClose}
              className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              onClick={handleSubmit}
              disabled={selected.size === 0 || adding}
              className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {adding ? 'Adding...' : `Add ${selected.size} Control${selected.size !== 1 ? 's' : ''}`}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
