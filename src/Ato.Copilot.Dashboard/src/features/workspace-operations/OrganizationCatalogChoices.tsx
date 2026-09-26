import { useState } from 'react';
import { LockKeyhole, Plus, Search, X } from 'lucide-react';
import * as api from './api';
import type { OrganizationCapability, OrganizationComponentDraft, SupportingComponent } from './types';
import { inputClass, secondaryButtonClass, Status, useRemote, Pager } from './workspaceUi';

export const componentTypes = ['Person', 'Place', 'Thing', 'Policy'] as const;
export const emptyComponent: OrganizationComponentDraft = { name: '', componentType: 'Thing', description: '', owner: '' };

export function OrganizationRecordChoices({ tenantId, source, recordType, selected, multiple = false, onSelect }: {
  tenantId: string; source?: 'local' | 'provider'; recordType: 'capability' | 'component';
  selected: string[]; multiple?: boolean; onSelect: (record: OrganizationCapability) => void;
}) {
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const state = useRemote(signal => api.listOrganizationCapabilities(tenantId, {
    grouping: recordType, source, search: search.trim() || undefined, page, pageSize: 25,
  }, signal), [tenantId, source, recordType, search, page]);
  return <div className="space-y-3">
    <label className="relative block"><span className="sr-only">Search {recordType === 'component' ? 'organization-wide components' : 'capabilities'}</span>
      <Search size={14} aria-hidden="true" className="absolute left-3 top-3 text-slate-400" />
      <input className={`${inputClass} w-full pl-9`} placeholder={source === 'provider' ? 'Search provider library…' : 'Search organization library…'}
        value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} />
    </label>
    <Status loading={state.loading} error={state.error} retry={state.retry} />
    {state.data?.aggregateState && state.data.aggregateState !== 'Available' &&
      <p role="alert">The catalog returned partial results: {state.data.aggregateState}</p>}
    {state.data?.items.map(item => {
      const key = `${item.source}:${item.recordId.toLowerCase()}`;
      return <label key={key} className={`flex cursor-pointer gap-3 rounded-md border p-3 text-sm focus-within:ring-2 focus-within:ring-indigo-300 ${
        selected.includes(key) ? 'border-indigo-400 bg-indigo-50/70 dark:bg-indigo-950' : 'border-slate-200 bg-white dark:border-gray-700 dark:bg-gray-900'}`}>
        <input type={multiple ? 'checkbox' : 'radio'} name={multiple ? undefined : 'organization-record'}
          checked={selected.includes(key)} onChange={() => onSelect(item)} className="mt-1 self-start accent-indigo-600" />
        <span className="min-w-0"><span className="flex flex-wrap items-center gap-2 text-xs font-semibold">{item.name}
          <span className="rounded-full border border-indigo-200 px-2 py-0.5 text-[10px] font-normal text-indigo-700 dark:border-indigo-800 dark:text-indigo-300">{item.recordType === 'component' ? 'Component' : 'Capability'}</span></span>
          <span className="mt-1 block text-[11px] text-slate-500">{item.sourceName || (item.source === 'provider' ? 'CSP source' : 'Organization')} · {item.availability} · {item.category}</span>
          {!!item.supportingComponents?.length && <span className="mt-1 block text-[11px] text-slate-500">Includes: {item.supportingComponents.map(component => component.name).join(' · ')}</span>}
        </span>
      </label>;
    })}
    {!state.loading && !state.error && state.data?.items.length === 0 &&
      <p className="text-sm text-slate-500">No eligible {recordType === 'component' ? 'components' : 'capabilities'} match this search.</p>}
    {state.data && state.data.total > state.data.pageSize && <Pager {...state.data} onPage={setPage} />}
  </div>;
}

export function OrganizationSupportingComponents({ tenantId, selected, drafts, retained, editing, onEditingChange, onSelect, onRemove, onStage, onRemoveDraft }: {
  tenantId: string; selected: OrganizationCapability[]; drafts: OrganizationComponentDraft[]; retained: SupportingComponent[];
  editing: boolean; onEditingChange: (editing: boolean) => void;
  onSelect: (record: OrganizationCapability) => void; onRemove: (record: OrganizationCapability) => void;
  onStage: (draft: OrganizationComponentDraft) => void; onRemoveDraft: (index: number) => void;
}) {
  const [open, setOpen] = useState(false);
  const chipClass = 'inline-flex max-w-full items-center gap-1 rounded-full border border-indigo-100 bg-indigo-50 px-2 py-1 text-[10px] text-indigo-800 dark:border-indigo-900 dark:bg-indigo-950 dark:text-indigo-200';
  return <section aria-label="Supporting components" className="space-y-2">
    <h4 className="text-xs font-semibold">Supporting components <span className="font-normal text-slate-400">(optional)</span></h4>
    <div className="flex flex-wrap items-center gap-1.5">
      {retained.map(item => <span key={`${item.source}:${item.id}`} className={chipClass}>
        <LockKeyhole size={11} aria-hidden="true" /><span className="break-words">{item.name} · {item.componentType}</span>
      </span>)}
      {selected.map(item => <span key={`${item.source}:${item.recordId}`} className={chipClass}>
        <span className="break-words">{item.name} · {item.category}</span>
        <button type="button" aria-label={`Remove ${item.name}`} className="rounded-full p-0.5 hover:bg-indigo-200" onClick={() => onRemove(item)}><X size={11} aria-hidden="true" /></button>
      </span>)}
      {drafts.map((draft, index) => <span key={index} className={chipClass}>
        <span className="break-words">{draft.name} · {draft.componentType}</span>
        <button type="button" aria-label={`Remove ${draft.name}`} className="rounded-full p-0.5 hover:bg-indigo-200" onClick={() => onRemoveDraft(index)}><X size={11} aria-hidden="true" /></button>
      </span>)}
      <button type="button" disabled={editing} className="inline-flex items-center gap-1 rounded border border-indigo-200 px-2 py-1 text-[11px] text-indigo-600 hover:bg-indigo-50 disabled:cursor-not-allowed disabled:opacity-50 dark:border-indigo-800 dark:text-indigo-300" onClick={() => setOpen(value => !value)}>
        <Plus size={12} aria-hidden="true" />{open ? 'Done selecting components' : 'Add or link component'}
      </button>
    </div>
    {!!retained.length && <p className="text-[11px] text-slate-500">Existing source components are retained. Provider components remain read-only.</p>}
    {open && <div className="space-y-3 rounded-md border border-slate-200 bg-slate-50/60 p-3 dark:border-gray-700 dark:bg-gray-950">
      <OrganizationRecordChoices tenantId={tenantId} recordType="component" multiple
        selected={selected.map(item => `${item.source}:${item.recordId.toLowerCase()}`)} onSelect={onSelect} />
      <StageOrganizationComponent onStage={onStage} onEditingChange={onEditingChange} />
    </div>}
  </section>;
}

export function OrganizationComponentFields({ value, onChange, inline = false }: {
  value: OrganizationComponentDraft; onChange: (value: OrganizationComponentDraft) => void; inline?: boolean;
}) {
  return <div className="grid gap-3 sm:grid-cols-2">
    <label className="grid gap-1 text-sm">{inline ? 'Component name' : 'Name'}
      <input className={inputClass} required maxLength={200} value={value.name}
        onChange={event => onChange({ ...value, name: event.target.value })} />
    </label>
    <label className="grid gap-1 text-sm">Component type
      <select className={inputClass} value={value.componentType} onChange={event => {
        const componentType = componentTypes.find(type => type === event.target.value);
        if (componentType) onChange({ ...value, componentType });
      }}>{componentTypes.map(type => <option key={type}>{type}</option>)}</select>
    </label>
    <label className="grid gap-1 text-sm sm:col-span-2">{inline ? 'Component description' : 'Description'}
      <textarea className={inputClass} required rows={3} maxLength={2000} value={value.description}
        onChange={event => onChange({ ...value, description: event.target.value })} />
    </label>
    {inline && <label className="grid gap-1 text-sm sm:col-span-2">Component owner
      <input className={inputClass} maxLength={200} value={value.owner}
        placeholder="Defaults to the organization owner" onChange={event => onChange({ ...value, owner: event.target.value })} />
    </label>}
  </div>;
}

export function StageOrganizationComponent({ onStage, onEditingChange }: {
  onStage: (value: OrganizationComponentDraft) => void; onEditingChange: (editing: boolean) => void;
}) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState(emptyComponent);
  if (!open) return <button type="button" className={secondaryButtonClass} onClick={() => { setOpen(true); onEditingChange(true); }}>Add new component</button>;
  return <fieldset className="space-y-3 rounded-lg border border-slate-200 p-4 dark:border-gray-700">
    <legend className="px-2 text-sm font-semibold">New organization component</legend>
    <OrganizationComponentFields value={draft} onChange={setDraft} inline />
    <p className="text-xs text-slate-500">Staged only. Nothing is created until you save the organization changes.</p>
    <div className="flex gap-2">
      <button type="button" className={secondaryButtonClass} disabled={!draft.name.trim() || !draft.description.trim()} onClick={() => {
        onStage({ ...draft, name: draft.name.trim(), description: draft.description.trim(), owner: draft.owner.trim() });
        setDraft(emptyComponent); setOpen(false); onEditingChange(false);
      }}>Stage component</button>
      <button type="button" className={secondaryButtonClass} onClick={() => { setDraft(emptyComponent); setOpen(false); onEditingChange(false); }}>Discard component</button>
    </div>
  </fieldset>;
}
