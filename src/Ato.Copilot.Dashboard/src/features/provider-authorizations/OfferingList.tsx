import { useState } from 'react';
import { ArrowRight, Cloud, FileUp, Info, Layers3, MapPin, Search, Shield, CheckCircle2, CircleDashed } from 'lucide-react';
import { Link } from '../workspaces/workspaceNavigation';
import { Pager, secondaryButtonClass, Status, useRemote } from '../workspace-operations/workspaceUi';
import * as api from './api';
import type { Offering } from './types';

const focus = 'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 focus-visible:ring-offset-2 dark:focus-visible:ring-offset-gray-900';
const panel = 'rounded-xl border border-slate-200 bg-white dark:border-gray-700 dark:bg-gray-900';
const environments = { AzureCloud: 'Azure Commercial', AzureUSGovernment: 'Azure Government' };

function ScopeIndicator({ label, recorded }: { label: string; recorded: boolean }) {
  const Icon = recorded ? CheckCircle2 : CircleDashed;
  return <div className="flex items-start gap-2.5">
    <Icon size={17} aria-hidden="true" className={`mt-0.5 shrink-0 ${recorded ? 'text-emerald-600 dark:text-emerald-400' : 'text-slate-400'}`} />
    <div><dt className="text-xs text-slate-500 dark:text-gray-400">{label}</dt>
      <dd className="mt-1 text-sm font-medium text-slate-700 dark:text-gray-200">{recorded ? 'Recorded revision' : 'Not recorded'}</dd></div>
  </div>;
}

function OfferingCard({ item }: { item: Offering }) {
  const boundaryNeeded = !item.currentBoundaryRevisionId;
  const hostingNeeded = !item.currentHostingScopeRevisionId;
  const needsSetup = boundaryNeeded || hostingNeeded;
  const nextLabel = boundaryNeeded ? 'Define boundary' : 'Define hosting scope';
  return <article aria-label={item.name} className={`${panel} overflow-hidden shadow-sm transition-shadow hover:shadow-md`}>
    <div className="p-5 sm:p-6">
      <div className="flex items-start gap-4">
        <span className="rounded-xl bg-indigo-50 p-3 text-indigo-600 dark:bg-indigo-950 dark:text-indigo-300"><Cloud size={24} aria-hidden="true" /></span>
        <div className="min-w-0 flex-1"><div className="flex flex-wrap items-center gap-3">
          <h3 className="min-w-0 break-words text-lg font-semibold"><Link className={`rounded text-slate-900 hover:text-indigo-700 dark:text-white dark:hover:text-indigo-300 ${focus}`} to={api.authorizationHref(item.offeringId)}>{item.name}</Link></h3>
          <span aria-label={`Offering lifecycle: ${item.lifecycle}`} className="rounded-full border border-slate-200 bg-slate-50 px-2.5 py-0.5 text-xs font-medium text-slate-600 dark:border-gray-600 dark:bg-gray-800 dark:text-gray-300">{item.lifecycle}</span>
        </div><p className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-500 dark:text-gray-400">
          <span>{item.environments.map(cloud => environments[cloud]).join(' · ') || 'Environment not recorded'}</span><span>Revision {item.revision}</span>
        </p></div>
      </div>
      {item.description && <p className="mt-4 break-words text-sm leading-relaxed text-slate-600 dark:text-gray-300">{item.description}</p>}
      <dl className="mt-6 grid gap-4 rounded-lg bg-slate-50 p-4 sm:grid-cols-2 dark:bg-gray-800/60">
        <ScopeIndicator label="Authorization boundary" recorded={!boundaryNeeded} />
        <ScopeIndicator label="Azure hosting scope" recorded={!hostingNeeded} />
      </dl>
      {needsSetup && <div className="mt-4 flex flex-wrap items-center justify-between gap-3 text-xs">
        <span className="text-slate-500 dark:text-gray-400">{boundaryNeeded ? 'Start by defining what this offering covers.' : 'Connect the offering to its Azure hosting scope.'}</span>
        <Link to={api.authorizationHref(item.offeringId, boundaryNeeded ? 'boundary' : 'inherited-coverage')} aria-label={`${nextLabel} for ${item.name}`} className={`inline-flex items-center gap-1 rounded font-semibold text-indigo-700 dark:text-indigo-300 ${focus}`}>{nextLabel}<ArrowRight size={14} aria-hidden="true" /></Link>
      </div>}
    </div>
    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 px-5 py-4 sm:px-6 dark:border-gray-700">
      <Link aria-label={`Add package to this offering: ${item.name}`} className={`inline-flex items-center gap-2 rounded-lg border border-slate-200 px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 dark:border-gray-600 dark:text-gray-200 dark:hover:bg-gray-800 ${focus}`} to={api.authorizationHref(item.offeringId, 'import')}><FileUp size={16} aria-hidden="true" />Add package to this offering</Link>
      <Link aria-label={`Manage ${item.name}`} className={`inline-flex items-center gap-2 rounded text-sm font-semibold text-indigo-700 dark:text-indigo-300 ${focus}`} to={api.authorizationHref(item.offeringId)}>Manage offering<ArrowRight size={16} aria-hidden="true" /></Link>
    </div>
  </article>;
}

function WorkflowGuide() {
  return <aside aria-label="Authorization workflow" className={`${panel} self-start p-5 sm:p-6`}>
    <p className="text-xs font-semibold uppercase tracking-wider text-indigo-600 dark:text-indigo-300">From source to coverage</p>
    <h2 className="mt-2 text-base font-semibold text-slate-900 dark:text-white">Build a traceable offering</h2>
    <ol className="mt-5 space-y-6">{[
      [MapPin, 'Define the boundary', 'Record the services, resources, and responsibilities in scope.'],
      [FileUp, 'Import your package', 'Connect the existing decision and supporting documents.'],
      [Layers3, 'Review the coverage', 'Review extracted components and capabilities before publication.'],
    ].map(([Icon, title, description], index) => {
      const StepIcon = Icon as typeof MapPin;
      return <li key={String(title)} className="flex gap-3"><span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-slate-100 text-slate-500 dark:bg-gray-800 dark:text-gray-400"><StepIcon size={16} aria-hidden="true" /></span>
        <div><h3 className="text-sm font-semibold text-slate-700 dark:text-gray-200"><span className="sr-only">Step {index + 1}: </span>{String(title)}</h3><p className="mt-1 text-xs leading-5 text-slate-500 dark:text-gray-400">{String(description)}</p></div></li>;
    })}</ol>
    <p className="mt-6 border-t border-slate-200 pt-4 text-xs leading-5 text-slate-500 dark:border-gray-700 dark:text-gray-400">Published capabilities and mission-system authorizations remain separate from the provider offering.</p>
  </aside>;
}

export function OfferingList() {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const remote = useRemote(signal => api.listOfferings(page, search, signal), [page, search]);
  const clearSearch = () => { setSearch(''); setPage(1); };
  return <div className="space-y-6">
    <div className="flex items-start gap-2.5 rounded-lg bg-slate-50 px-4 py-3 text-xs leading-5 text-slate-600 dark:bg-gray-800/60 dark:text-gray-300">
      <Info size={16} aria-hidden="true" className="mt-0.5 shrink-0 text-slate-400" />
      <p>Record existing authorization decisions and their scope. SPIN does not issue or independently verify these external decisions.</p>
    </div>
    <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_minmax(220px,0.265fr)]">
      <section aria-label="Provider offerings" className="min-w-0 space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-4">
          <div><h2 className="text-lg font-semibold text-slate-900 dark:text-white">Your offerings</h2><p className="mt-1 text-sm text-slate-500 dark:text-gray-400">Manage each offering’s boundary and source packages.</p></div>
          {remote.data && <span className="rounded-full bg-indigo-50 px-3 py-1 text-xs font-medium text-indigo-700 dark:bg-indigo-950 dark:text-indigo-300">{remote.data.total} {remote.data.total === 1 ? 'offering' : 'offerings'}{search ? ' found' : ''}</span>}
        </div>
        <div className="relative"><Search size={18} aria-hidden="true" className="pointer-events-none absolute left-3.5 top-3 text-slate-400" />
          <label htmlFor="offering-search" className="sr-only">Search offerings</label>
          <input id="offering-search" type="search" value={search} placeholder="Search by offering name…" onChange={event => { setSearch(event.target.value); setPage(1); }} className={`w-full rounded-lg border border-slate-200 bg-white py-2.5 pl-11 pr-4 text-sm text-slate-900 placeholder:text-slate-400 dark:border-gray-700 dark:bg-gray-900 dark:text-white ${focus}`} />
        </div>
        <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
        {remote.data && <>
          {!remote.data.items.length && <div className={`${panel} px-6 py-10 text-center`}><Shield size={30} aria-hidden="true" className="mx-auto mb-3 text-indigo-400" /><h3 className="font-semibold text-slate-900 dark:text-white">{search ? 'No matching offerings' : 'Create your first offering'}</h3><p className="mx-auto mt-2 max-w-md text-sm text-slate-500 dark:text-gray-400">{search ? 'Try another name or clear your search to see all offerings.' : 'Start with an offering, define its boundary, then connect an existing authorization package.'}</p>{search && <button type="button" className={`${secondaryButtonClass} mt-4 ${focus}`} onClick={clearSearch}>Clear search</button>}</div>}
          <div className="space-y-4">{remote.data.items.map(item => <OfferingCard key={item.offeringId} item={item} />)}</div>
          {remote.data.total > remote.data.pageSize && <Pager {...remote.data} onPage={setPage} />}
        </>}
      </section>
      <WorkflowGuide />
    </div>
  </div>;
}
