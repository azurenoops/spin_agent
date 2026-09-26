import { useEffect, useRef, useState } from 'react';
import { Plus } from 'lucide-react';
import * as api from './api';
import { buttonClass, inputClass, message, secondaryButtonClass, Status, useRemote } from './workspaceUi';
import { ComponentIcon, StateBadge } from './CapabilityPresentation';
import type { ComponentType } from '../../types/dashboard';
import type { OrganizationCapabilityDetail } from './types';

export function SetupSystemPicker({ onSelect, onCancel }: { onSelect: (systemId: string) => void; onCancel: () => void }) {
  const [cursor, setCursor] = useState<string>();
  const [selected, setSelected] = useState('');
  const state = useRemote(signal => api.listSetupSystems({ pageSize: 50, cursor }, signal), [cursor]);
  return <div className="mx-auto max-w-2xl space-y-5">
    <h2 className="text-lg font-semibold">Choose a system</h2>
    <p className="text-sm text-slate-500">Capabilities are applied within a system. Your setup permission will be checked before any changes can be applied.</p>
    <Status loading={state.loading} error={state.error} retry={state.retry} />
    <label className="grid gap-2 text-sm font-medium">Apply to
      <select className={inputClass} value={selected} onChange={event => setSelected(event.target.value)}>
        <option value="">Select a system</option>
        {state.data?.items.map(system => <option key={system.systemId} value={system.systemId}>{system.name}{system.acronym ? ` · ${system.acronym}` : ''}</option>)}
      </select>
    </label>
    {state.data?.items.length === 0 && <p className="text-sm text-slate-500">No readable systems are available. Register a system or request access before setup.</p>}
    {state.data?.nextCursor && <button className={secondaryButtonClass} onClick={() => { setCursor(state.data?.nextCursor ?? undefined); setSelected(''); }}>More systems</button>}
    {cursor && <button className={secondaryButtonClass} onClick={() => { setCursor(undefined); setSelected(''); }}>First page</button>}
    <div className="flex justify-between border-t border-slate-200 pt-5">
      <button type="button" className={secondaryButtonClass} onClick={onCancel}>Cancel</button>
      <button className={buttonClass} disabled={!selected || state.loading || !!state.error} onClick={() => onSelect(selected)}>Continue →</button>
    </div>
  </div>;
}

export function CapabilityChoices({ tenantId, source, selected, search, onSelect }: {
  tenantId: string; source: string; selected: string; search: string; onSelect: (id: string) => void;
}) {
  const [page, setPage] = useState(1);
  const state = useRemote(signal => api.listOrganizationCapabilities(tenantId, {
    grouping: 'capability', source, page, pageSize: 25, search: search || undefined,
  }, signal), [tenantId, source, page, search]);
  return <fieldset className="space-y-3">
    <legend className="sr-only">Capability choices</legend>
    <Status loading={state.loading} error={state.error} retry={state.retry} />
    {state.data?.items.map(item => <label key={item.recordId}
      className={`flex cursor-pointer items-center gap-3 rounded-lg border bg-white p-4 dark:bg-gray-900 ${selected.toLowerCase() === item.recordId.toLowerCase() ? 'border-indigo-400 ring-1 ring-indigo-100' : 'border-slate-200 dark:border-gray-700'}`}>
      <input type="radio" name="capability-choice" checked={selected.toLowerCase() === item.recordId.toLowerCase()} onChange={() => onSelect(item.recordId)} className="accent-indigo-600" />
      <div><p className="text-sm font-medium">{item.name}</p>
        <p className="mt-1 text-xs text-slate-500">{item.sourceName || (source === 'provider' ? 'Provider offering' : 'Organization capability')}</p>
        <div className="mt-2"><StateBadge tone="indigo">{item.controlCount == null ? item.category : `${item.controlCount} proposed control mappings`}</StateBadge></div></div>
    </label>)}
    {!state.loading && !state.error && state.data?.items.length === 0 && <p className="text-sm text-slate-500">No eligible capabilities match this source and search.</p>}
    {selected && state.data && !state.data.items.some(item => item.recordId.toLowerCase() === selected.toLowerCase()) &&
      <p className="text-xs text-slate-500">A previously selected capability is retained outside this page. It will be validated when preparing setup.</p>}
    {state.data && state.data.total > 25 && <div className="flex items-center justify-between">
      <button className={secondaryButtonClass} disabled={page === 1} onClick={() => setPage(value => value - 1)}>Previous capabilities</button>
      <span className="text-xs text-slate-500">Page {page}</span>
      <button className={secondaryButtonClass} disabled={page * 25 >= state.data.total} onClick={() => setPage(value => value + 1)}>More capabilities</button>
    </div>}
  </fieldset>;
}

export function SetupComponentChoices({ systemId, selected, onChange, capability, onBusyChange }: {
  systemId: string; selected: string[]; onChange: (ids: string[]) => void;
  capability: OrganizationCapabilityDetail | null;
  onBusyChange: (busy: boolean) => void;
}) {
  const [cursor, setCursor] = useState<string>();
  const [generation, setGeneration] = useState(0);
  const state = useRemote(signal => api.getSetupComponents(systemId, { pageSize: 50, cursor }, signal),
    [systemId, cursor, generation]);
  const [form, setForm] = useState({ name: '', componentType: 'Thing' as ComponentType, description: '' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const controller = useRef<AbortController | null>(null);
  useEffect(() => () => { controller.current?.abort(); onBusyChange(false); }, [systemId, onBusyChange]);
  const create = async () => {
    if (!form.name.trim()) { setError('Component name is required.'); return; }
    if (busy) return;
    controller.current?.abort();
    const request = new AbortController();
    controller.current = request;
    setBusy(true); onBusyChange(true); setError(null);
    try {
      const component = await api.createSetupComponent(systemId, { ...form, name: form.name.trim(), status: 'Planned' }, request.signal);
      if (request.signal.aborted) return;
      onChange([...new Set([...selected, component.id])]);
      setForm({ name: '', componentType: 'Thing', description: '' });
      setGeneration(value => value + 1);
    } catch (reason) { if (!request.signal.aborted) setError(message(reason)); }
    finally { if (!request.signal.aborted) { setBusy(false); onBusyChange(false); } }
  };
  return <fieldset disabled={busy} className="space-y-3"><legend className="sr-only">Supporting component options</legend>
    {capability?.supportingComponents?.filter(item => item.source === 'provider').map(item => <div key={item.id}
      className="flex items-center gap-3 rounded-lg border border-slate-200 bg-slate-50 p-4 dark:border-gray-700 dark:bg-gray-900">
      <input type="checkbox" checked disabled aria-label={`${item.name} provider source`} /><ComponentIcon type={item.componentType} />
      <div className="flex-1"><p className="text-sm font-medium">{item.name}</p><p className="mt-1 text-xs text-slate-500">Provider source · read-only</p></div>
      <StateBadge>{item.componentType}</StateBadge>
    </div>)}
    <Status loading={state.loading} error={state.error} retry={state.retry} />
    {state.data?.items.map(item => <label key={item.id} className="flex cursor-pointer items-center gap-3 rounded-lg border border-slate-200 bg-white p-4 dark:border-gray-700 dark:bg-gray-900">
      <input type="checkbox" checked={selected.includes(item.id)} className="accent-indigo-600"
        onChange={event => onChange(event.target.checked ? [...new Set([...selected, item.id])] : selected.filter(id => id !== item.id))} />
      <ComponentIcon type={item.componentType} /><div className="min-w-0 flex-1"><p className="text-sm font-medium">{item.name}</p>
        <p className="mt-1 text-xs text-slate-500">Organization · existing component</p></div><StateBadge>{item.componentType}</StateBadge>
    </label>)}
    {state.data?.items.length === 0 && <p className="text-sm text-slate-500">No local components are registered in this system yet.</p>}
    <p className="text-xs text-slate-500">{selected.length} local component{selected.length === 1 ? '' : 's'} selected across pages.</p>
    <div className="flex gap-2">{cursor && <button className={secondaryButtonClass} onClick={() => setCursor(undefined)}>First page</button>}
      {state.data?.nextCursor && <button className={secondaryButtonClass} onClick={() => setCursor(state.data?.nextCursor ?? undefined)}>More components</button>}</div>
    <details className="pt-2 text-sm"><summary className="cursor-pointer text-indigo-700 dark:text-indigo-300">+ Add a component inline</summary>
      <div className="mt-3 space-y-3 rounded-lg border border-slate-200 p-4 dark:border-gray-700">
        <p className="text-xs text-slate-500">This creates a reusable component immediately. Its capability link is saved when setup is applied.</p>
        <label className="grid gap-1">Component name<input className={inputClass} value={form.name} onChange={event => setForm(value => ({ ...value, name: event.target.value }))} /></label>
        <label className="grid gap-1">Component type<select className={inputClass} value={form.componentType}
          onChange={event => setForm(value => ({ ...value, componentType: event.target.value as ComponentType }))}>
          {['Person', 'Place', 'Thing', 'Policy'].map(type => <option key={type}>{type}</option>)}
        </select></label>
        <label className="grid gap-1">Component description<textarea className={inputClass} value={form.description}
          onChange={event => setForm(value => ({ ...value, description: event.target.value }))} /></label>
        {error && <p role="alert" className="text-red-700">{error}</p>}
        <button className={`${secondaryButtonClass} inline-flex items-center gap-2`} disabled={busy} onClick={() => void create()}>
          <Plus size={15} aria-hidden="true" />{busy ? 'Creating component…' : 'Create and select component'}
        </button>
      </div>
    </details>
  </fieldset>;
}
