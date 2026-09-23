import { Cloud, FileText } from 'lucide-react';
import { Link } from '../workspaces/workspaceNavigation';
import { StateBadge, workspaceCard } from './CapabilityPresentation';
import { secondaryButtonClass, Status, useRemote, Pager } from './workspaceUi';
import * as api from './api';
import type { ProviderCatalogItem, ProviderSourceArtifact, WorkingRevision } from './types';
import { useState } from 'react';

export function ProviderVersions({ item, working }: { item?: ProviderCatalogItem | null; working?: WorkingRevision | null }) {
  return <dl className="space-y-4 text-sm">
    <div><dt className="mb-1 text-xs text-slate-500">Current subscriber version</dt><dd><PublishedVersion item={item} /></dd></div>
    <div><dt className="mb-1 text-xs text-slate-500">Working revision</dt><dd>
      <WorkingVersion item={item} working={working} />
    </dd></div>
    <div><dt className="mb-1 text-xs text-slate-500">Publication authority</dt><dd>Provider approval of the exact revision and impact preview is required.</dd></div>
  </dl>;
}

export function WorkingVersion({ item, working }: { item?: ProviderCatalogItem | null; working?: WorkingRevision | null }) {
  const revision = working?.revision ?? item?.workingRevision;
  const approval = working?.approvalState ?? item?.workingApprovalState;
  const pending = revision != null && revision !== item?.releasedRevision;
  return <StateBadge tone={pending ? approval === 'Approved' ? 'green' : 'amber' : 'neutral'}>
    {pending ? `v${revision} · ${approval === 'Approved' ? 'Approved' : 'Needs review'}`
      : item?.releasedRevision ? 'No pending revision' : 'No saved working revision'}
  </StateBadge>;
}

export function PublishedVersion({ item }: { item?: ProviderCatalogItem | null }) {
  return <StateBadge tone={item?.releasedRevision ? 'green' : 'neutral'}>
    {item?.releasedRevision ? `v${item.releasedRevision} · Published` : 'Not published'}</StateBadge>;
}

export function ProviderOfferingSummary() {
  const [page, setPage] = useState(1);
  const state = useRemote(signal => api.getProviderCatalogOverview(page, signal), [page]);
  const [open, setOpen] = useState(false);
  return <section aria-label="Provider offering" className={workspaceCard}>
    <Status loading={state.loading} error={state.error} retry={state.retry} />
    {state.data && <>
      <div className="flex flex-wrap items-center gap-5">
        <span className="rounded-lg bg-indigo-50 p-3 text-indigo-600 dark:bg-indigo-950"><Cloud size={22} aria-hidden="true" /></span>
        <div className="min-w-0 flex-1"><h2 className="text-lg font-semibold">{state.data.providerName ?? 'Provider'} offering</h2>
          <p className="mt-1 text-xs text-slate-500">Provider-owned source content</p></div>
        <dl className="flex flex-wrap gap-6 text-xs">
          <div><dt className="text-slate-500">Source package</dt><dd className="mt-1 font-medium">{state.data.sourceArtifacts.total ? `${state.data.sourceArtifacts.total} source artifacts` : 'Not recorded'}</dd></div>
          <div><dt className="text-slate-500">Authorization record</dt><dd className="mt-1 font-medium">Not recorded · separate from publication</dd></div>
        </dl>
        <button type="button" className={secondaryButtonClass} onClick={() => setOpen(value => !value)} aria-expanded={open}>View source package</button>
      </div>
      {open && <div className="mt-5 border-t border-slate-200 pt-4 dark:border-gray-700">
        <p className="mb-3 text-xs text-slate-500">Source-package provenance; these references do not establish verified evidence or authorization.</p>
        <ProviderArtifacts items={state.data.sourceArtifacts.items} />
        {state.data.sourceArtifacts.total > state.data.sourceArtifacts.pageSize && <Pager {...state.data.sourceArtifacts} onPage={setPage} />}
      </div>}
    </>}
  </section>;
}

export function ProviderArtifacts({ items }: { items: ProviderSourceArtifact[] }) {
  return items.length ? <ul className="divide-y divide-slate-200 dark:divide-gray-700">{items.map((item, index) =>
    <li key={`${item.componentId}:${index}`} className="flex gap-3 py-3 text-sm">
      <FileText size={18} className="mt-0.5 shrink-0 text-indigo-600" aria-hidden="true" />
      <div className="min-w-0"><p className="break-words font-medium">{item.sourceFileName ?? item.componentName}</p><p className="mt-1 break-words text-xs text-slate-500">{item.sourceFormat} · {item.sourceReference ?? 'Reference not recorded'}</p></div>
    </li>)}</ul> : <p className="text-sm text-slate-500">No source artifacts recorded.</p>;
}

export function ProviderComponentPicker({ selected, onSelect, multiple = true }: { selected: string[]; onSelect: (item: ProviderCatalogItem) => void; multiple?: boolean }) {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const state = useRemote(signal => api.listProviderCatalog({ grouping: 'component', search, page, pageSize: 25 }, signal), [page, search]);
  return <div className="space-y-3">
    <label className="grid gap-1 text-xs">Search provider components<input value={search} className="rounded border border-slate-300 bg-white p-2 text-sm dark:border-gray-600 dark:bg-gray-900"
      onChange={event => { setSearch(event.target.value); setPage(1); }} /></label>
    <Status loading={state.loading} error={state.error} retry={state.retry} />
    {state.data?.aggregateState && state.data.aggregateState !== 'Available' && <p role="alert" className="text-sm text-amber-800 dark:text-amber-200">Partial component results: {state.data.aggregateState}</p>}
    {state.data?.items.map(item => <label key={item.componentId} className="flex items-start gap-3 rounded border border-slate-200 p-3 text-sm dark:border-gray-700">
      <input type={multiple ? 'checkbox' : 'radio'} name={multiple ? undefined : 'provider-component'} checked={selected.some(id => id.toLowerCase() === item.componentId.toLowerCase())} className="mt-1 accent-indigo-600" onChange={() => onSelect(item)} />
      <span>{item.name}<span className="mt-1 block text-xs text-slate-500">{item.componentType} · {item.lifecycle}</span></span>
    </label>)}
    {state.data?.items.length === 0 && <p className="text-sm text-slate-500">No provider components match this search. <Link className="text-indigo-700 underline" to="/components">Manage provider components</Link></p>}
    {state.data && state.data.total > state.data.pageSize && <Pager {...state.data} onPage={setPage} />}
  </div>;
}
