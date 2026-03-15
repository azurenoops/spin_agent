import { useState, useEffect } from 'react';
import type { CapabilityMappingDto } from '../../types/dashboard';
import { getCapabilityMappings, createCapabilityMappings } from '../../api/capabilities';

const narrativeStatusBadge: Record<string, string> = {
  Populated: 'bg-green-100 text-green-800',
  Empty: 'bg-gray-100 text-gray-600',
  Customized: 'bg-purple-100 text-purple-800',
};

interface MappingPanelProps {
  capabilityId: string;
}

export function MappingPanel({ capabilityId }: MappingPanelProps) {
  const [mappings, setMappings] = useState<CapabilityMappingDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [showAdd, setShowAdd] = useState(false);
  const [controlId, setControlId] = useState('');
  const [role, setRole] = useState<'Primary' | 'Supporting' | 'Shared'>('Primary');
  const [adding, setAdding] = useState(false);
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

  useEffect(() => { fetchMappings(); }, [capabilityId]);

  const handleAdd = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!controlId.trim()) return;
    setAdding(true);
    setError(null);
    try {
      await createCapabilityMappings(capabilityId, {
        mappings: [{ controlId: controlId.trim().toUpperCase(), role }],
      });
      setControlId('');
      setShowAdd(false);
      await fetchMappings();
    } catch (err: any) {
      setError(err?.response?.data?.error ?? 'Failed to create mapping');
    } finally {
      setAdding(false);
    }
  };

  // Group by family
  const groupedMappings = mappings.reduce<Record<string, CapabilityMappingDto[]>>((acc, m) => {
    const family = m.controlFamily ?? 'Other';
    (acc[family] ??= []).push(m);
    return acc;
  }, {});

  if (loading) return <div className="text-sm text-gray-400 py-2">Loading mappings...</div>;

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h4 className="text-sm font-semibold text-gray-700">Control Mappings ({mappings.length})</h4>
        <button
          onClick={() => setShowAdd(!showAdd)}
          className="text-xs text-blue-600 hover:text-blue-800"
        >
          {showAdd ? 'Cancel' : '+ Add Mapping'}
        </button>
      </div>

      {error && <div className="text-xs text-red-600 bg-red-50 p-2 rounded">{error}</div>}

      {showAdd && (
        <form onSubmit={handleAdd} className="flex gap-2 items-end">
          <div className="flex-1">
            <label className="block text-xs text-gray-500 mb-1">Control ID</label>
            <input
              type="text"
              value={controlId}
              onChange={(e) => setControlId(e.target.value)}
              placeholder="e.g., AC-2"
              className="w-full border rounded px-2 py-1.5 text-sm"
            />
          </div>
          <div>
            <label className="block text-xs text-gray-500 mb-1">Role</label>
            <select
              value={role}
              onChange={(e) => setRole(e.target.value as 'Primary' | 'Supporting' | 'Shared')}
              className="border rounded px-2 py-1.5 text-sm"
            >
              <option value="Primary">Primary</option>
              <option value="Supporting">Supporting</option>
              <option value="Shared">Shared</option>
            </select>
          </div>
          <button
            type="submit"
            disabled={adding || !controlId.trim()}
            className="px-3 py-1.5 text-sm bg-blue-600 text-white rounded hover:bg-blue-700 disabled:opacity-50"
          >
            {adding ? '...' : 'Add'}
          </button>
        </form>
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
                {items
                  .sort((a, b) => a.controlId.localeCompare(b.controlId))
                  .map((m) => (
                    <div
                      key={m.id}
                      className="flex items-center justify-between px-3 py-2 bg-gray-50 rounded text-sm"
                    >
                      <div className="flex items-center gap-2">
                        <span className="font-mono text-xs font-medium">{m.controlId}</span>
                        <span className="text-gray-500 truncate max-w-xs">{m.controlTitle}</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs text-gray-400">{m.role}</span>
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
