import { useState, useEffect, useCallback } from 'react';
import { listComponents } from '../../api/components';
import type { OrgComponentDto } from '../../api/components';
import { linkComponentCapabilities, unlinkComponentCapability } from '../../api/capabilities';

interface ComponentPickerModalProps {
  open: boolean;
  capabilityId: string;
  capabilityName: string;
  linkedComponentIds: string[];
  onClose: () => void;
  onSave: () => void;
}

export default function ComponentPickerModal({
  open, capabilityId, capabilityName, linkedComponentIds, onClose, onSave,
}: ComponentPickerModalProps) {
  const [components, setComponents] = useState<OrgComponentDto[]>([]);
  const [search, setSearch] = useState('');
  const [typeFilter, setTypeFilter] = useState('');
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (open) {
      setSelected(new Set(linkedComponentIds));
      setSearch('');
      setTypeFilter('');
    }
  }, [open, linkedComponentIds]);

  const fetchComponents = useCallback(async () => {
    setLoading(true);
    try {
      const res = await listComponents({ search: search || undefined, type: typeFilter || undefined, pageSize: 200 });
      setComponents(res.items);
    } catch {
      setComponents([]);
    }
    setLoading(false);
  }, [search, typeFilter]);

  useEffect(() => {
    if (open) fetchComponents();
  }, [open, fetchComponents]);

  const toggleComponent = (id: string) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const handleSave = async () => {
    setSaving(true);
    try {
      // Compute adds and removes
      const currentSet = new Set(linkedComponentIds);
      const toAdd = [...selected].filter(id => !currentSet.has(id));
      const toRemove = linkedComponentIds.filter(id => !selected.has(id));

      // For each component to link, call the link endpoint
      for (const componentId of toAdd) {
        await linkComponentCapabilities(componentId, { capabilityIds: [capabilityId] });
      }
      // For each component to unlink
      for (const componentId of toRemove) {
        await unlinkComponentCapability(componentId, capabilityId);
      }

      onSave();
      onClose();
    } catch {
      // silently handled
    }
    setSaving(false);
  };

  if (!open) return null;

  const hasChanges = (() => {
    const orig = new Set(linkedComponentIds);
    if (selected.size !== orig.size) return true;
    for (const id of selected) if (!orig.has(id)) return true;
    return false;
  })();

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
      <div className="w-full max-w-lg rounded-xl bg-white shadow-2xl">
        <div className="flex items-center justify-between border-b px-6 py-4">
          <div>
            <h2 className="text-lg font-semibold text-gray-900">Link Components</h2>
            <p className="text-sm text-gray-500 mt-0.5">{capabilityName}</p>
          </div>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600 text-xl">&times;</button>
        </div>

        <div className="px-6 py-3 border-b flex gap-2">
          <input
            type="text"
            placeholder="Search components..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="flex-1 border rounded px-3 py-1.5 text-sm focus:ring-2 focus:ring-indigo-300 focus:outline-none"
          />
          <select
            value={typeFilter}
            onChange={e => setTypeFilter(e.target.value)}
            className="border rounded px-2 py-1.5 text-sm"
          >
            <option value="">All Types</option>
            <option value="Thing">Thing</option>
            <option value="Person">Person</option>
            <option value="Place">Place</option>
            <option value="Policy">Policy</option>
          </select>
        </div>

        <div className="max-h-[50vh] overflow-y-auto px-6 py-3">
          {loading ? (
            <p className="text-sm text-gray-400 py-4 text-center">Loading components...</p>
          ) : components.length === 0 ? (
            <p className="text-sm text-gray-400 py-4 text-center">No components found</p>
          ) : (
            <div className="space-y-1">
              {components.map(c => {
                const isLinked = linkedComponentIds.includes(c.id);
                const isSelected = selected.has(c.id);
                return (
                  <label
                    key={c.id}
                    className={`flex items-center gap-3 rounded-md px-3 py-2 cursor-pointer hover:bg-gray-50 ${isSelected ? 'bg-indigo-50 border border-indigo-200' : ''}`}
                  >
                    <input
                      type="checkbox"
                      checked={isSelected}
                      onChange={() => toggleComponent(c.id)}
                      className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
                    />
                    <div className="flex-1 min-w-0">
                      <div className="text-sm font-medium text-gray-900 truncate">{c.name}</div>
                      <div className="text-xs text-gray-500">
                        {c.componentType}
                        {c.subType && ` · ${c.subType}`}
                        {isLinked && <span className="ml-1 text-indigo-600">(currently linked)</span>}
                      </div>
                    </div>
                  </label>
                );
              })}
            </div>
          )}
        </div>

        <div className="flex items-center justify-between border-t px-6 py-3">
          <span className="text-sm text-gray-500">{selected.size} selected</span>
          <div className="flex gap-3">
            <button onClick={onClose} className="rounded-md border px-4 py-2 text-sm text-gray-600 hover:bg-gray-50">Cancel</button>
            <button
              onClick={handleSave}
              disabled={!hasChanges || saving}
              className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
            >
              {saving ? 'Saving...' : 'Save Links'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
