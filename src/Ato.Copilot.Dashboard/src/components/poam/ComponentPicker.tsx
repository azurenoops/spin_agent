import { useState, useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { getComponents } from '../../api/components';
import type { SystemComponentDto } from '../../types/dashboard';

interface ComponentPickerProps {
  systemId?: string;
  selectedIds: string[];
  onChange: (ids: string[]) => void;
  disabled?: boolean;
}

export default function ComponentPicker({ systemId: requestedSystem, selectedIds, onChange, disabled }: ComponentPickerProps) {
  const { id: routeSystem } = useParams<{ id: string }>();
  const systemId = requestedSystem ?? routeSystem;
  const [components, setComponents] = useState<SystemComponentDto[]>([]);
  const [search, setSearch] = useState('');
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    if (!open) return;
    let active = true;
    setComponents([]); setError(null);
    if (!systemId) { setError('Select a system before linking components.'); return; }
    setLoading(true);
    getComponents(systemId, { search: search || undefined, pageSize: 100 }).then(result => {
      if (active) setComponents(result.items);
    }).catch(failure => {
      if (active) setError(failure instanceof Error ? failure.message : 'Unable to load components.');
    }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [open, systemId, search, retry]);

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
                <button type="button" aria-label={`Remove ${c.name}`} onClick={() => toggle(c.id)} className="text-indigo-400 hover:text-indigo-600">
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
        className="w-full rounded-lg border px-3 py-2 text-left text-sm text-gray-700 hover:bg-gray-50 disabled:opacity-50 dark:border-slate-600 dark:text-slate-200 dark:hover:bg-slate-800"
      >
        {open ? 'Close picker' : 'Select components...'}
      </button>

      {open && (
        <div className="mt-1 max-h-48 w-full overflow-y-auto rounded-lg border bg-white dark:border-slate-600 dark:bg-slate-900">
          <div className="sticky top-0 bg-white p-2 dark:bg-slate-900">
            <input
              aria-label="Search components"
              type="text"
              placeholder="Search components..."
              className="w-full rounded border px-2 py-1 text-xs"
              value={search}
              onChange={e => setSearch(e.target.value)}
            />
          </div>
          {loading && <p className="p-2 text-xs text-gray-400">Loading...</p>}
          {error && <div className="p-2"><p role="alert" className="text-xs text-red-700 dark:text-red-300">{error}</p><button type="button" onClick={() => setRetry(value => value + 1)} className="mt-2 text-xs underline">Retry components</button></div>}
          {!error && !loading && unselectedComponents.length === 0 && (
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
