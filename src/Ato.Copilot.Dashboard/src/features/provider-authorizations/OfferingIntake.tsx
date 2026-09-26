import { useEffect, useRef, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { Pager, Status, inputClass, secondaryButtonClass, surfaceClass, useRemote, warningClass } from '../workspace-operations/workspaceUi';
import { PackageUpload } from '../package-imports/PackageUpload';
import { PackageReceiptCard, packageIsProcessing } from '../package-imports/PackageReceipts';
import { getPackageStatus } from '../package-imports/api';
import { PackageImportError } from '../package-imports/request';
import * as api from './api';
import { CitationFields, compact, Field, Lines, MutationForm, ScopeFields } from './forms';
import type { BoundaryInput, BoundaryRevision, Cloud, Offering, PackageReceipt, PackageVersion } from './types';

export function OfferingCreate({ onCreated, expanded = false, suggestedName = '' }: { onCreated: (offering: Offering) => void; expanded?: boolean; suggestedName?: string }) {
  const [name, setName] = useState(suggestedName);
  const [description, setDescription] = useState('');
  const [environments, setEnvironments] = useState<Cloud[]>([]);
  return <details open={expanded || undefined} className="group rounded-lg border border-dashed border-slate-300 bg-white px-4 py-3 open:border-solid open:bg-slate-50 dark:border-gray-600 dark:bg-gray-900 dark:open:bg-gray-800"><summary className="cursor-pointer rounded text-sm font-medium text-indigo-700 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-indigo-500 dark:text-indigo-300">Create an offering</summary>
    <div className="mt-4"><MutationForm label="Create offering" submitDisabled={!name.trim() || !environments.length}
      submit={async key => onCreated(await api.createOffering({ name: name.trim(), description, environments }, key))} onSaved={() => setName('')}>
      <Field label="Offering name" value={name} onChange={setName} required />
      <Field label="Description" value={description} onChange={setDescription} multiline />
      <fieldset className="space-y-2"><legend className="text-sm font-medium">Environments</legend>
        {(['AzureCloud', 'AzureUSGovernment'] as const).map(cloud => <label key={cloud} className="mr-4 inline-flex items-center gap-2 text-sm">
          <input type="checkbox" checked={environments.includes(cloud)} onChange={event => setEnvironments(previous => event.target.checked ? [...previous, cloud] : previous.filter(value => value !== cloud))} />
          {cloud === 'AzureCloud' ? 'Azure Commercial' : 'Azure Government'}
        </label>)}
      </fieldset>
      <p className="text-xs text-slate-600">Creating an offering does not authorize workloads, allocate hosting or publish capabilities.</p>
    </MutationForm></div>
  </details>;
}

export function BoundaryEditor({ offering, predecessor, suggestion, onSaved, onPendingChange }: {
  offering: Offering; predecessor?: BoundaryRevision; suggestion?: BoundaryInput; onSaved: (boundary: BoundaryRevision) => void;
  onPendingChange?: (pending: boolean) => void;
}) {
  const [value, setValue] = useState<BoundaryInput>(() => predecessor ? {
    name: predecessor.name, scopeStatement: predecessor.scopeStatement, services: predecessor.services,
    componentSnapshotIds: predecessor.componentSnapshotIds, includedScopes: predecessor.includedScopes, exclusions: predecessor.exclusions,
    providerResponsibilities: predecessor.providerResponsibilities, customerResponsibilities: predecessor.customerResponsibilities, citations: predecessor.citations,
  } : suggestion ?? { name: '', scopeStatement: '', services: [], componentSnapshotIds: [], includedScopes: [], exclusions: [], providerResponsibilities: [], customerResponsibilities: [], citations: [] });
  const update = <K extends keyof BoundaryInput>(key: K, next: BoundaryInput[K]) => setValue(previous => ({ ...previous, [key]: next }));
  return <MutationForm label="Save boundary revision" submitDisabled={!value.name.trim() || !value.scopeStatement.trim()} onSaved={() => undefined} onPendingChange={onPendingChange}
    submit={async key => onSaved(await api.createBoundary(offering.offeringId, {
      ...value, services: compact(value.services), componentSnapshotIds: compact(value.componentSnapshotIds),
      providerResponsibilities: compact(value.providerResponsibilities), customerResponsibilities: compact(value.customerResponsibilities),
      expectedOfferingRevision: offering.revision, predecessorRevisionId: predecessor?.boundaryRevisionId ?? null,
    }, key))}>
    <Field label="Boundary name" value={value.name} onChange={text => update('name', text)} required />
    <Field label="Explicit scope statement" value={value.scopeStatement} onChange={text => update('scopeStatement', text)} required multiline maxLength={8000} />
    <p className="text-sm text-slate-600">An empty resource list is not universal coverage. Record only supported scope; omitted or uncertain coverage remains undetermined.</p>
    <Lines label="Services" values={value.services} onChange={text => update('services', text)} />
    <Lines label="Immutable component snapshot IDs" values={value.componentSnapshotIds} onChange={text => update('componentSnapshotIds', text)} />
    <ScopeFields label="Included Azure scopes" value={value.includedScopes} onChange={scopes => update('includedScopes', scopes)} />
    <fieldset className="space-y-2 rounded border p-3"><legend>Explicit exclusions</legend>
      {value.exclusions.map((exclusion, index) => <div key={index} className="space-y-2 border-t pt-2">
        <Field label={`Excluded scope description ${index + 1}`} required value={exclusion.description} onChange={text => update('exclusions', value.exclusions.map((item, row) => row === index ? { ...item, description: text } : item))} />
        <Field label={`Exclusion rationale ${index + 1}`} required value={exclusion.rationale} onChange={text => update('exclusions', value.exclusions.map((item, row) => row === index ? { ...item, rationale: text } : item))} />
        <ScopeFields label={`Excluded Azure scope ${index + 1} (optional)`} maxItems={1} value={exclusion.scope ? [exclusion.scope] : []} onChange={scopes =>
          update('exclusions', value.exclusions.map((item, row) => row === index ? { ...item, scope: scopes[0] ?? null } : item))} />
        <button type="button" className={secondaryButtonClass} onClick={() => update('exclusions', value.exclusions.filter((_, row) => row !== index))}>Remove exclusion {index + 1}</button>
      </div>)}
      <button type="button" className={secondaryButtonClass} disabled={value.exclusions.length >= 100}
        onClick={() => update('exclusions', [...value.exclusions, { scope: null, description: '', rationale: '' }])}>Add exclusion</button>
    </fieldset>
    <Lines label="Provider responsibilities" values={value.providerResponsibilities} onChange={text => update('providerResponsibilities', text)} />
    <Lines label="Customer responsibilities" values={value.customerResponsibilities} onChange={text => update('customerResponsibilities', text)} />
    <CitationFields value={value.citations} onChange={citations => update('citations', citations)} />
  </MutationForm>;
}

export function OfferingPicker({ selected, onSelect, disabled = false }: { selected: string; onSelect: (id: string) => void; disabled?: boolean }) {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const remote = useRemote(signal => api.listOfferings(page, search, signal), [page, search]);
  return <div className="space-y-3">
    <Field label="Find offering" value={search} onChange={text => { setSearch(text); setPage(1); }} />
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    <label className="grid gap-1 text-sm">Offering<select className={inputClass} value={selected} disabled={disabled || remote.loading || !!remote.error}
      onChange={event => onSelect(event.target.value)}><option value="">Choose an offering</option>
      {selected && !remote.data?.items.some(item => item.offeringId === selected) && <option value={selected}>Selected offering ({selected})</option>}
      {remote.data?.items.map(item => <option key={item.offeringId} value={item.offeringId}>{item.name}</option>)}
    </select></label>
    {remote.data && remote.data.total > remote.data.pageSize && <Pager {...remote.data} onPage={setPage} />}
  </div>;
}

function PersistedReceipt({ receipt }: { receipt: PackageReceipt }) {
  const remote = useRemote(signal => getPackageStatus(receipt.package.packageId, signal), [receipt.package.packageId]);
  useEffect(() => {
    if (!remote.loading && !remote.error && packageIsProcessing(remote.data ?? receipt.package)) {
      const timer = window.setTimeout(remote.retry, 5000);
      return () => window.clearTimeout(timer);
    }
  }, [remote.loading, remote.error, remote.data, remote.retry, receipt.package]);
  return <div className="space-y-3">
    <PackageReceiptCard item={remote.data ?? receipt.package} />
    <Status error={remote.error} retry={remote.retry} />
    <Link className="text-sm font-semibold text-indigo-700 underline"
      to={api.authorizationHref(receipt.packageVersion.offeringId, `packages/${receipt.package.packageId}`)}>Continue review in Authorizations</Link>
    <p className="text-sm text-slate-600">Receipt is saved. You may continue while processing; inventory review and publication are separate.</p>
  </div>;
}

export function OfferingIntake({ initialOfferingId = '', existingPackageId, previousVersion, onPendingChange, onReceived, suggestedName, suggestedBoundary }: {
  initialOfferingId?: string; existingPackageId?: string; onPendingChange?: (pending: boolean) => void;
  previousVersion?: PackageVersion; suggestedName?: string; suggestedBoundary?: BoundaryInput;
  onReceived?: (receipt: PackageReceipt) => void;
}) {
  const [offeringId, setOfferingId] = useState(initialOfferingId);
  const [boundaryId, setBoundaryId] = useState('');
  const [boundaryPage, setBoundaryPage] = useState(1);
  const [receipt, setReceipt] = useState<PackageReceipt | null>(null);
  const [pending, setPending] = useState(false);
  const [name, setName] = useState('');
  const [refresh, setRefresh] = useState(0);
  const offering = useRemote(signal => offeringId ? api.getOffering(offeringId, signal) : Promise.resolve(null), [offeringId, refresh]);
  const boundaries = useRemote(signal => offeringId ? api.listBoundaries(offeringId, boundaryPage, signal) : Promise.resolve(null), [offeringId, boundaryPage, refresh]);
  const existing = useRemote(signal => existingPackageId ? api.getAssociatedPackage(existingPackageId, signal) : Promise.resolve(null), [existingPackageId, refresh]);
  const intent = useRef<Parameters<typeof api.uploadPackage> | null>(null);
  const pendingChange = (value: boolean) => { setPending(value); onPendingChange?.(value); };
  const select = (id: string) => { setOfferingId(id); setBoundaryId(''); setReceipt(null); setBoundaryPage(1); };
  const boundarySaved = (saved: BoundaryRevision) => { setBoundaryId(saved.boundaryRevisionId); setRefresh(value => value + 1); };
  return <section className="space-y-5" aria-label="Authorization package preparation">
    <p className={warningClass}>{existingPackageId ? 'Confirm the offering and exact boundary for these retained sources.' : 'Select the offering and its exact boundary before receipt.'} Source-stated authorization is not verified authority, inherited workload coverage or a new mission ATO.</p>
    {previousVersion && <p className="break-all text-sm">Preparing a successor to version {previousVersion.version} in retained series {previousVersion.seriesId}. Previous sources and review history will not be replaced.</p>}
    <fieldset disabled={pending} className="space-y-4">
      {!initialOfferingId && <><OfferingPicker selected={offeringId} onSelect={select} disabled={pending} />
        <OfferingCreate suggestedName={suggestedName} expanded={!!suggestedName} onCreated={saved => { select(saved.offeringId); setRefresh(value => value + 1); }} /></>}
      <Status loading={!!offeringId && offering.loading} error={offering.error} retry={offering.retry} />
      {offering.data && <>
        <h2 className="text-lg font-semibold">{offering.data.name}</h2>
        <Status loading={boundaries.loading} error={boundaries.error} retry={boundaries.retry} />
        {boundaries.data && <>
          <label className="grid gap-1 text-sm">Boundary revision<select className={inputClass} value={boundaryId} onChange={event => setBoundaryId(event.target.value)}>
            <option value="">Choose an exact boundary revision</option>
            {boundaryId && !boundaries.data.items.some(item => item.boundaryRevisionId === boundaryId) && <option value={boundaryId}>Selected revision ({boundaryId})</option>}
            {boundaries.data.items.map(item => <option key={item.boundaryRevisionId} value={item.boundaryRevisionId}>{item.name} · v{item.version}</option>)}
          </select></label>
          {!boundaries.data.items.length && <p>No boundary revisions recorded.</p>}
          {boundaries.data.total > boundaries.data.pageSize && <Pager {...boundaries.data} onPage={setBoundaryPage} />}
          <details open={!boundaries.data.total} className={`${surfaceClass} p-4`}><summary className="cursor-pointer text-sm font-semibold">Prepare an explicit boundary</summary>
            <div className="mt-4"><BoundaryEditor key={`${offeringId}:${offering.data.revision}`} offering={offering.data} suggestion={suggestedBoundary} onSaved={boundarySaved} /></div>
          </details>
        </>}
        {!existingPackageId && <Field label="Package name" value={name} onChange={setName} required />}
      </>}
    </fieldset>
    {existingPackageId && <Status loading={existing.loading} error={existing.error} retry={existing.retry} />}
    {offering.data && boundaryId && !offering.loading && !offering.error && !boundaries.error && <>
      {existingPackageId && existing.data
        ? <MutationForm label="Associate retained package" disabled={!!existing.data.association} onPendingChange={pendingChange}
          onSaved={() => { pendingChange(false); setRefresh(value => value + 1); }} submit={async key => {
          if (!existing.data || !offering.data) throw new Error('Reload the offering and package before association.');
          const saved = await api.associatePackage(existingPackageId, { expectedPackageRevision: existing.data.revision,
            offeringId, expectedOfferingRevision: offering.data.revision, boundaryRevisionId: boundaryId }, key);
          setReceipt(saved); onReceived?.(saved);
        }}><p>Associate the retained package without re-uploading or discarding original source bytes. Pending approvals are invalidated.</p></MutationForm>
        : !existingPackageId && <PackageUpload disabled={!name.trim()} onPendingChange={pendingChange} upload={async (files, key) => {
          if (!offering.data) throw new Error('Offering context is unavailable.');
          intent.current ??= [offeringId, { name: name.trim(), boundaryRevisionId: boundaryId, expectedOfferingRevision: offering.data.revision,
            ...(previousVersion ? { seriesId: previousVersion.seriesId, previousVersionId: previousVersion.packageVersionId } : {}) },
          files, `${key}:${offeringId}:${boundaryId}${previousVersion ? `:${previousVersion.packageVersionId}` : ''}`];
          try {
            const saved = await api.uploadPackage(...intent.current);
            intent.current = null; setReceipt(saved); setRefresh(value => value + 1); onReceived?.(saved);
          } catch (reason) {
            if (reason instanceof PackageImportError && [400, 401, 403, 413, 422].includes(reason.status ?? 0)) intent.current = null;
            throw reason;
          }
        }} />}
    </>}
    {receipt && <PersistedReceipt receipt={receipt} />}
  </section>;
}
