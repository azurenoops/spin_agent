import { useState, useEffect } from 'react';
import { getCapabilities, getCapabilityMappings, createCapabilityMappings } from '../api/capabilities';
import type { SecurityCapabilityDto, CapabilityMappingRole } from '../types/dashboard';

interface Props {
  systemId: string;
  existingCapabilityIds: string[];
  onClose: () => void;
  onAdded: () => void;
}

const ROLES: { value: CapabilityMappingRole; label: string; description: string }[] = [
  { value: 'Primary', label: 'Primary', description: 'This system is the primary implementer' },
  { value: 'Supporting', label: 'Supporting', description: 'Provides supporting implementation' },
  { value: 'Shared', label: 'Shared', description: 'Shared responsibility across systems' },
];

export default function AddCapabilityDialog({ systemId, existingCapabilityIds, onClose, onAdded }: Props) {
  const [capabilities, setCapabilities] = useState<SecurityCapabilityDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [role, setRole] = useState<CapabilityMappingRole>('Primary');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const result = await getCapabilities({ pageSize: 200 });
        if (!cancelled) {
          setCapabilities(result.items.filter((c) => !existingCapabilityIds.includes(c.id)));
        }
      } catch {
        if (!cancelled) setError('Failed to load capabilities');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [existingCapabilityIds]);

  const filtered = capabilities.filter(
    (c) =>
      !search ||
      c.name.toLowerCase().includes(search.toLowerCase()) ||
      c.provider.toLowerCase().includes(search.toLowerCase()) ||
      c.category.toLowerCase().includes(search.toLowerCase()),
  );

  const selected = capabilities.find((c) => c.id === selectedId);

  const handleAdd = async () => {
    if (!selectedId) return;
    setSaving(true);
    setError(null);
    try {
      // Get the capability's existing control mappings
      const mappingData = await getCapabilityMappings(selectedId);
      const controls = mappingData.mappings.map((m) => m.controlId);

      if (controls.length === 0) {
        setError('This capability has no control mappings. Map controls to the capability first in the Component Library.');
        setSaving(false);
        return;
      }

      // Deduplicate controls
      const uniqueControls = [...new Set(controls)];

      // Create system-scoped mappings
      await createCapabilityMappings(selectedId, {
        mappings: uniqueControls.map((controlId) => ({
          controlId,
          role,
          registeredSystemId: systemId,
        })),
      });

      onAdded();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to add capability';
      setError(msg);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center">
      <div className="fixed inset-0 bg-black/40" onClick={onClose} />
      <div className="relative w-full max-w-2xl rounded-lg bg-white shadow-xl mx-4 max-h-[80vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <div>
            <h3 className="text-lg font-semibold text-gray-900">Add Capability</h3>
            <p className="text-sm text-gray-500">Link an organizational capability to this system</p>
          </div>
          <button type="button" onClick={onClose} className="text-gray-400 hover:text-gray-500">
            <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
          {error && (
            <div className="rounded-md bg-red-50 border border-red-200 p-3">
              <p className="text-sm text-red-700">{error}</p>
            </div>
          )}

          {/* Search */}
          <input
            type="text"
            placeholder="Search capabilities by name, provider, or category..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:ring-1 focus:ring-indigo-500"
          />

          {/* Role selector */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">Mapping Role</label>
            <div className="flex gap-3">
              {ROLES.map((r) => (
                <button
                  key={r.value}
                  type="button"
                  onClick={() => setRole(r.value)}
                  className={`flex-1 rounded-md border px-3 py-2 text-sm transition ${
                    role === r.value
                      ? 'border-indigo-500 bg-indigo-50 text-indigo-700 ring-1 ring-indigo-500'
                      : 'border-gray-200 bg-white text-gray-700 hover:border-gray-300'
                  }`}
                >
                  <div className="font-medium">{r.label}</div>
                  <div className="text-xs opacity-70 mt-0.5">{r.description}</div>
                </button>
              ))}
            </div>
          </div>

          {/* Capability list */}
          {loading ? (
            <p className="text-sm text-gray-500 text-center py-8">Loading capabilities...</p>
          ) : filtered.length === 0 ? (
            <div className="rounded-lg border-2 border-dashed border-gray-300 p-8 text-center">
              <p className="text-sm text-gray-500">
                {capabilities.length === 0
                  ? 'All capabilities are already linked to this system.'
                  : 'No capabilities match your search.'}
              </p>
            </div>
          ) : (
            <div className="space-y-2 max-h-64 overflow-y-auto">
              {filtered.map((cap) => (
                <button
                  key={cap.id}
                  type="button"
                  onClick={() => setSelectedId(cap.id === selectedId ? null : cap.id)}
                  className={`w-full rounded-md border px-4 py-3 text-left transition ${
                    cap.id === selectedId
                      ? 'border-indigo-500 bg-indigo-50 ring-1 ring-indigo-500'
                      : 'border-gray-200 bg-white hover:border-gray-300 hover:bg-gray-50'
                  }`}
                >
                  <div className="flex items-center justify-between">
                    <div className="min-w-0">
                      <div className="font-medium text-gray-900 truncate">{cap.name}</div>
                      <div className="text-xs text-gray-500 mt-0.5">
                        {cap.provider} · {cap.categoryName} · {cap.mappedControlCount} controls ·{' '}
                        {cap.systemsUsingCount} system{cap.systemsUsingCount !== 1 ? 's' : ''}
                      </div>
                    </div>
                    <span
                      className={`ml-3 inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium ${
                        cap.implementationStatus === 'Implemented'
                          ? 'bg-green-100 text-green-700'
                          : cap.implementationStatus === 'InProgress'
                            ? 'bg-indigo-100 text-indigo-700'
                            : 'bg-gray-100 text-gray-600'
                      }`}
                    >
                      {cap.implementationStatus}
                    </span>
                  </div>
                  {cap.description && (
                    <p className="text-xs text-gray-400 mt-1 line-clamp-2">{cap.description}</p>
                  )}
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t border-gray-200 px-6 py-4">
          <p className="text-xs text-gray-500">
            {selected
              ? `Will map ${selected.mappedControlCount} control${selected.mappedControlCount !== 1 ? 's' : ''} as ${role}`
              : 'Select a capability to add'}
          </p>
          <div className="flex gap-3">
            <button
              type="button"
              onClick={onClose}
              className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={handleAdd}
              disabled={!selectedId || saving}
              className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {saving ? 'Adding...' : 'Add Capability'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
