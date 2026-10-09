import { useEffect, useRef, useState } from 'react';
import { Link } from '../workspaces/workspaceNavigation';
import { getProviderCapability } from '../workspace-operations/api';
import { Pager, Status, useRemote } from '../workspace-operations/workspaceUi';
import { getPackageCandidates } from '../package-imports/api';
import { stateLabel } from '../package-imports/PackageReceipts';
import * as api from './api';
import type { Offering, OfferingBoundaryCapability } from './types';
import { ProviderBadge, ProviderPanel } from './ProviderPresentation';
import { readAllPages } from './providerReadModels';
import SetupDialog from '../workspace-operations/SetupDialog';
import type { PackageCandidate, PackageCitation } from '../package-imports/types';

export function OfferingCapabilities({ offering }: { offering: Offering }) {
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState('all');
  const [sort, setSort] = useState('asc');
  const [selected, setSelected] = useState<OfferingBoundaryCapability | null>(null);
  const selectedTrigger = useRef<HTMLButtonElement | null>(null);
  useEffect(() => {
    if (!selected && selectedTrigger.current) {
      if (selectedTrigger.current.isConnected) selectedTrigger.current.focus();
      selectedTrigger.current = null;
    }
  }, [selected]);
  const [openingProvenance, setOpeningProvenance] = useState<string | null>(null);
  const remote = useRemote(async signal => {
    const first = await api.getBoundaryOverview(offering.offeringId, 1, 1, signal);
    const items = await readAllPages(async next => {
      if (next === 1) return first.capabilities;
      const current = await api.getBoundaryOverview(offering.offeringId, next, 1, signal);
      if (current.offeringRevision !== first.offeringRevision)
        throw new Error('Offering changed while capability pages were loading. Retry the complete list before relying on this baseline.');
      return current.capabilities;
    }, signal);
    const sources = new Map<string, Promise<PackageCandidate[]>>();
    const records = await Promise.all(items.map(async item => {
      try {
        return { item, detail: await readCapabilityDetails(item, signal, sources), error: null };
      } catch (error) {
        signal.throwIfAborted();
        return { item, detail: null, error: error instanceof Error ? error.message : 'Implementation details unavailable.' };
      }
    }));
    return { ...first.capabilities, offeringRevision: first.offeringRevision, records };
  }, [offering.offeringId, offering.revision]);
  const query = search.trim().toLocaleLowerCase();
  const filtered = remote.data?.records.filter(({ item, detail }) =>
    (filter === 'all' || (filter === 'published' ? item.publicationState === 'Published' : ['NeedsReview', 'AwaitingReview'].includes(item.reviewState)))
    && (!query || [item.name, detail?.description, ...(detail?.controls ?? [])].some(value => value?.toLocaleLowerCase().includes(query))))
    .sort((a, b) => (sort === 'desc' ? -1 : 1) * a.item.name.localeCompare(b.item.name)) ?? [];
  const pageSize = 5;
  const currentPage = Math.min(page, Math.max(1, Math.ceil(filtered.length / pageSize)));
  const published = remote.data?.records.filter(record => record.item.publicationState === 'Published') ?? [];
  const revisions = [...new Set(published.flatMap(({ item }) => item.releaseRevision == null ? [] : [item.releaseRevision]))].sort((a, b) => a - b);
  const releaseLabel = !remote.data?.published ? 'No published release'
    : published.some(({ item }) => item.releaseRevision == null) || !revisions.length ? 'Release version not reported'
      : `Live: ${revisions.length === 1 ? 'revision' : 'revisions'} ${revisions.join(', ')}`;
  const selectedRecord = selected && remote.data?.records.find(record =>
    record.item.capabilityId === selected.capabilityId && record.item.candidateId === selected.candidateId && record.item.packageId === selected.packageId);
  const dutyGroups = new Map<string, string[]>();
  for (const [control, duty] of Object.entries(selectedRecord?.detail?.duties ?? {})) {
    dutyGroups.set(duty, [...(dutyGroups.get(duty) ?? []), control]);
  }
  return <div className="space-y-[22px]">
    <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
    {remote.data && !remote.loading && !remote.error && <>
      {remote.data.offeringRevision !== offering.revision && <p role="alert">
        Offering context changed since the identity loaded. <Link className="provider-text" to={api.authorizationHref(offering.offeringId)}>Reopen the offering overview</Link> before editing. Published release references remain explicit.
      </p>}
      <div className="provider-banner provider-banner-release"><div>
        <strong>{remote.data.published} capabilities available · {remote.data.awaitingReview} awaiting review</strong>
        <p>Mission Owners use the published release until they review a replacement.</p>
      </div><ProviderBadge tone={remote.data.published ? 'success' : 'neutral'}>{releaseLabel}</ProviderBadge></div>
      <ProviderPanel title="Service implementations">
        <div className="provider-toolbar mb-4 flex flex-wrap items-center justify-between gap-3">
          <input type="text" aria-label="Search capabilities" placeholder="Search capabilities or controls"
            className="max-w-full rounded-md border border-slate-300 bg-white px-3 py-3 text-sm sm:w-[380px]"
            value={search} onChange={event => { setSearch(event.target.value); setPage(1); }} />
          <ProviderBadge>{remote.data.awaitingReview} awaiting review</ProviderBadge>
          <label className="grid gap-1 text-xs">Release state<select className="rounded border border-slate-300 p-2 text-sm" value={filter}
            onChange={event => { setFilter(event.target.value); setPage(1); }}>
            <option value="all">All records</option><option value="published">Published</option><option value="pending">Awaiting review</option>
          </select></label>
          <label className="grid gap-1 text-xs">Sort by name<select className="rounded border border-slate-300 p-2 text-sm" value={sort}
            onChange={event => { setSort(event.target.value); setPage(1); }}>
            <option value="asc">A → Z</option><option value="desc">Z → A</option>
          </select></label>
        </div>
        {query && remote.data.records.some(record => record.error) && <div className="mb-3 text-sm">
          <p role="status">Search is incomplete while implementation details are unavailable. Retry the details before relying on control matches.</p>
          <button className="provider-secondary mt-2" onClick={remote.retry}>Retry capability search</button>
        </div>}
        {!remote.data.total ? <p>No capability records linked to this offering. Review retained source proposals before publication.</p>
          : <div className="provider-table-wrap relative"><table className="provider-table offering-capability-table table-fixed w-full" aria-label="Service implementations">
            <colgroup><col style={{ width: '50%' }} /><col style={{ width: '28%' }} /><col style={{ width: '22%' }} /></colgroup>
            <thead><tr><th>Capability</th><th>Source control references</th><th>Release state</th></tr></thead>
            <tbody>{filtered.slice((currentPage - 1) * pageSize, currentPage * pageSize).map(record =>
              <CapabilityRow key={`${record.item.capabilityId}:${record.item.candidateId}`} offeringId={offering.offeringId} {...record} retry={remote.retry}
                triggerRef={element => {
                  if (element && selected?.capabilityId === record.item.capabilityId && selected.candidateId === record.item.candidateId
                    && selected.packageId === record.item.packageId) selectedTrigger.current = element;
                }}
                onOpen={element => { selectedTrigger.current = element; setOpeningProvenance(record.detail?.provenance ?? null); setSelected(record.item); }} />)}
              {!filtered.length && <tr><td colSpan={3}>{remote.data.records.some(record => record.error) ? 'No matches in the available metadata.' : 'No capabilities match this search.'}</td></tr>}
            </tbody>
          </table></div>}
        <p role="status" className="mt-3 text-xs text-slate-500">{filtered.length} matching records · Page {currentPage} of {Math.max(1, Math.ceil(filtered.length / pageSize))}</p>
        {filtered.length > pageSize && <Pager page={currentPage} pageSize={pageSize} total={filtered.length} onPage={setPage} />}
        <p className="offering-context-note mt-4 rounded bg-indigo-50 p-3 text-xs">Descriptions and source duty maps are retained context, not an immutable release payload. The capability workflow keeps working-revision review separate from published release references.</p>
      </ProviderPanel>
    </>}
    <div className="grid gap-[22px] md:grid-cols-2">
      <ProviderPanel title="Components support capabilities"><p>A service or product is the implementation building block. Each capability explains the protection delivered and the evidence that supports it.</p>
        <Link className="provider-secondary mt-3" to={api.authorizationHref(offering.offeringId, 'inherited-coverage')}>View supporting components</Link>
      </ProviderPanel>
      <ProviderPanel title="Keep customer work visible"><p>Shared controls include specific Mission Owner tasks and evidence. Applying a capability does not mark those tasks complete.</p>
        <Link className="mt-4 inline-block text-sm font-semibold text-indigo-700" to={api.authorizationHref(offering.offeringId, 'inherited-coverage?task=responsibilities')}>Review mission responsibilities</Link>
      </ProviderPanel>
    </div>
    {selected && <SetupDialog title={selected.name} description="Read-only capability and retained source context."
      className="provider-workspace offering-implementation-drawer" placement="right" busy={false} onClose={() => setSelected(null)}
      headerContent={<>
        <p className="offering-implementation-eyebrow">Capability detail · retained context</p>
        <h2 aria-hidden="true" className="offering-implementation-title">{selected.name}</h2>
        <p className="offering-implementation-offering">{offering.name}</p>
      </>}
      footer={<div className="offering-implementation-actions">
        {selected.capabilityId && <Link className="provider-primary" to={`/security-capabilities/${encodeURIComponent(selected.capabilityId)}`}>Open capability workflow</Link>}
        <button type="button" className="provider-secondary" onClick={() => setSelected(null)}>Close details</button>
      </div>}>
      <div className="offering-implementation-body">
        <Status loading={remote.loading} error={remote.error} retry={remote.retry} />
        {openingProvenance && selectedRecord?.detail && openingProvenance !== selectedRecord.detail.provenance && <p role="alert">
          Source version changed since this drawer opened. Current source context is shown below; the retained release reference and selected mission baseline have not been replaced.
        </p>}
        {selectedRecord && JSON.stringify(selectedRecord.item) !== JSON.stringify(selected) && <p role="alert">
          Linked source or release context changed since this drawer opened. Close and reopen the capability to inspect the current record. No selected mission baseline has been replaced.
        </p>}
        <ProviderBadge tone={selected.publicationState === 'Published' ? 'success' : 'attention'}>
          {stateLabel(selected.publicationState)} record{selected.releaseRevision != null ? ` · revision ${selected.releaseRevision}` : ' · Release version not reported'}
        </ProviderBadge>
        <section aria-label="Service implementation">
          <h3>Service implementation</h3>
          <p className="offering-implementation-statement">{selectedRecord?.detail?.description || 'Implementation summary unavailable.'}</p>
          <p className="offering-implementation-note">Retained source or catalog context, not an immutable release payload. Source proposal duties do not establish accepted mission responsibilities.</p>
          {selectedRecord?.error && <><p role="alert">{selectedRecord.error}</p><button className="provider-secondary" onClick={remote.retry}>Retry implementation details</button></>}
        </section>
        <section aria-label="Control-duty references">
          <h3>Control-duty references</h3>
          {selectedRecord?.detail?.controls.length ? <ul className="offering-implementation-chips" aria-label="Source control references">
            {selectedRecord.detail.controls.map(control => <li key={control}>{control}</li>)}
          </ul> : <p>Source control references not reported.</p>}
        </section>
        <section aria-label="Who does what?">
          <h3>Who does what?</h3>
          {dutyGroups.size ? <div className="offering-implementation-duties">{[...dutyGroups].map(([duty, controls]) =>
            <div role="group" aria-label={duty || 'Duty not reported'} key={duty}>
              <p className="offering-implementation-duty">{duty || 'Duty not reported'}</p>
              <ul className="offering-implementation-chips" aria-label="Controls with this source duty">{controls.map(control => <li key={control}>{control}</li>)}</ul>
            </div>)}</div> : <p>Pinned published duties are not supplied by this listing API. Open the capability workflow for working-revision and source-duty review; neither is mission acceptance.</p>}
          {!!dutyGroups.size && <p className="offering-implementation-muted">These are source duty values, not complete provider/customer statements or mission acceptance. Review the retained source for assigned responsibilities.</p>}
        </section>
        <section aria-label="Scope & applicability">
          <h3>Scope & applicability</h3>
          <dl><div><dt>Boundary context</dt><dd>{!selected.boundaryRevisionId ? 'Not recorded' : selected.boundaryRevisionId === offering.currentBoundaryRevisionId ? 'Current recorded boundary' : 'Different boundary version — review applicability before use.'}</dd></div></dl>
          <p className="offering-implementation-muted">Service scope and exclusions are recorded separately. A published capability does not establish accepted inheritance or a mission authorization decision.</p>
          <Link className="provider-text" to={api.authorizationHref(offering.offeringId, 'boundary')}>View scope & duties</Link>
        </section>
        <section aria-label="Evidence & provenance">
          <h3>Evidence & provenance</h3>
          <p>{selectedRecord?.detail?.provenance || 'Source version and provenance are not reported.'}</p>
          {selected.packageId && selected.candidateId && <Link className="provider-text" to={api.authorizationHref(offering.offeringId, `packages/${encodeURIComponent(selected.packageId)}/candidates/${encodeURIComponent(selected.candidateId)}`)}>Review retained source</Link>}
          {!!selectedRecord?.detail?.citations.length ? <details><summary>Retained source citations</summary>
            <ul className="offering-implementation-citations">{selectedRecord.detail.citations.map((citation, index) => <li key={index}>
              <p className="font-semibold">{citation.archivePath} · {citation.locator}</p>
              <p>{citation.quote}</p>
              <p>Artifact: {citation.artifactId}</p>
            </li>)}</ul>
          </details> : <p className="offering-implementation-muted">No retained source citations supplied by this read model. Evidence and review remain in the capability workflow.</p>}
        </section>
        <details><summary>Source and release identities</summary>
          <dl><div><dt>Published capability release</dt><dd>{selected.releaseRevision != null ? `Revision ${selected.releaseRevision}` : 'Release version not reported'}</dd></div>
            <div><dt>Offering identity</dt><dd>{offering.lifecycle} · revision {offering.revision}</dd></div></dl>
          <p>
          Capability: {selected.capabilityId || 'Not reported'}<br />Release: {selected.releaseId || 'Not reported'}<br />
          Boundary: {selected.boundaryRevisionId || 'Not recorded'}<br />Package: {selected.packageId || 'Not reported'}<br />Candidate: {selected.candidateId || 'Not reported'}
        </p></details>
      </div>
    </SetupDialog>}
  </div>;
}

interface CapabilityDetails { description: string; controls: string[]; duties: Record<string, string> | null; provenance: string; citations: PackageCitation[] }

async function readCapabilityDetails(item: OfferingBoundaryCapability, signal: AbortSignal, sources: Map<string, Promise<PackageCandidate[]>>): Promise<CapabilityDetails | null> {
  if (item.packageId && item.candidateId) {
    if (!sources.has(item.packageId)) sources.set(item.packageId,
      readAllPages(page => getPackageCandidates(item.packageId!, { page, pageSize: 25 }, signal), signal));
    const records = await sources.get(item.packageId)!;
    const record = records.find(candidate => candidate.candidateId === item.candidateId);
    if (!record) throw new Error('The retained source proposal could not be found.');
    return { description: record.description, controls: Object.keys(record.controlDuties), duties: record.controlDuties,
      provenance: `Retained source proposal revision ${record.revision} · ${stateLabel(record.reviewState)}. Proposal revision is not capability release revision.`,
      citations: record.citations };
  }
  if (item.capabilityId) {
    const record = await getProviderCapability(item.capabilityId, signal);
    return { description: record.capability.description, controls: record.mappedControlIds, duties: null,
      provenance: `Current catalog context · working revision ${record.capability.workingRevision ?? 'not reported'} · ${record.capability.sourceFormat || 'source format not reported'}${record.capability.sourceReference ? ` · ${record.capability.sourceReference}` : ''}. Catalog metadata is not the pinned release snapshot.`,
      citations: [] };
  }
  return null;
}

function CapabilityRow({ item, detail, error, retry, onOpen, triggerRef }: {
  offeringId: string; item: OfferingBoundaryCapability; detail: CapabilityDetails | null; error: string | null; retry: () => void;
  onOpen: (element: HTMLButtonElement) => void; triggerRef: (element: HTMLButtonElement | null) => void;
}) {
  return <tr>
    <td data-label="Capability"><button type="button" ref={triggerRef} className="provider-text text-left" onClick={event => onOpen(event.currentTarget)}>{item.name}</button><small><span className="line-clamp-2">{detail?.description || 'Implementation summary unavailable'}</span></small>
      {error && <><p role="alert">{error}</p><button className="mt-2 text-xs text-indigo-700 underline" onClick={retry}>Retry implementation details</button></>}
    </td>
    <td data-label="Source control references">{detail ? detail.controls.length ? detail.controls.map((control, index) =>
      <span key={control}>{index > 0 && ', '}<span className="inline-block whitespace-nowrap">{control}</span></span>) : 'No mappings recorded' : 'Not reported'}</td>
    <td data-label="Release state"><ProviderBadge tone={item.publicationState === 'Published' ? 'success' : 'attention'}>{stateLabel(item.publicationState)}</ProviderBadge>
      {(item.releaseId || item.releaseRevision != null) ? <details className="mt-1"><summary>Exact release</summary>
        {item.reviewState !== item.publicationState && <small>{stateLabel(item.reviewState)}</small>}
        {item.releaseRevision != null && <small>Revision {item.releaseRevision}</small>}
        {item.releaseId && <small>{item.releaseId}</small>}
      </details> : item.reviewState !== item.publicationState && <small>{stateLabel(item.reviewState)}</small>}
    </td>
  </tr>;
}
