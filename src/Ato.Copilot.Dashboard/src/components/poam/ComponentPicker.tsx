import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import { getComponents } from '../../api/components';
import type { SystemComponentDto } from '../../types/dashboard';

interface ComponentPickerProps {
  selectedIds: string[];
  onChange: (ids: string[]) => void;
  disabled?: boolean;
}

export default function ComponentPicker({ selectedIds, onChange, disabled }: ComponentPickerProps) {
  const { id: systemId } = useParams<{ id: string }>();
  const [components, setComponents] = useState<SystemComponentDto[]>([]);
  const [search, setSearch] = useState('');
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);

  const fetchComponents = useCallback(async () => {
    if (!systemId) return;
    setLoading(true);
    try {
      const result = await getComponents(systemId, { search: search || undefined, pageSize: 100 });
      setComponents(result.items);
    } catch {
      // silently handle — user sees empty list
    } finally {
      setLoading(false);
    }
  }, [systemId, search]);

  useEffect(() => {
    if (open) fetchComponents();
  }, [open, fetchComponents]);

  const toggle = (id: string) => {
    if (disabled) return;
    const next = selectedIds.includes(id)
      ? selectedIds.filter(x => x !== id)
      : [...selectedIds, id];
    onChange(next);
  };

  const selectedComponents = components.filter(c => selectedIds.includes(c.id));
  const unselectedComponents = components.filter(c => !selectedIds.includes(c.id));

  return (
    <div className="relative">
      <label className="mb-1 block text-xs font-medium text-gray-500">
        Components ({selectedIds.length} selected)
      </label>

      {/* Selected badges */}
      {selectedIds.length > 0 && (
        <div className="mb-2 flex flex-wrap gap-1">
          {selectedComponents.map(c => (
            <span key={c.id} className="inline-flex items-center gap-1 rounded-full bg-indigo-50 px-2 py-0.5 text-xs text-indigo-700">
              {c.name}
              {!disabled && (
                <button type="button" onClick={() => toggle(c.id)} className="text-indigo-400 hover:text-indigo-600">
                  &times;
                </button>
              )}
            </span>
          ))}
        </div>
      )}

      {/* Toggle dropdown */}
      <button
        type="button"
        disabled={disabled}
        onClick={() => setOpen(o => !o)}
        className="w-full rounded-lg border px-3 py-2 text-left text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-50"
      >
        {open ? 'Close picker' : 'Select components...'}
      </button>

      {open && (
        <div className="absolute z-20 mt-1 max-h-48 w-full overflow-y-auto rounded-lg border bg-white shadow-lg">
          <div className="sticky top-0 bg-white p-2">
            <input
              type="text"
              placeholder="Search components..."
              className="w-full rounded border px-2 py-1 text-xs"
              value={search}
              onChange={e => setSearch(e.target.value)}
            />
          </div>
          {loading && <p className="p-2 text-xs text-gray-400">Loading...</p>}
          {!loading && unselectedComponents.length === 0 && (
            <p className="p-2 text-xs text-gray-400">No components found</p>
          )}
          {unselectedComponents.map(c => (
            <button
              key={c.id}
              type="button"
              onClick={() => toggle(c.id)}
              className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-indigo-50"
            >
              <span className="truncate">{c.name}</span>
              <span className="ml-auto text-xs text-gray-400">{c.componentType}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
