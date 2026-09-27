import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from '../workspaces/workspaceNavigation';
import { getSspPreview, retainSspPreview, type SspPreview, type DocumentSourceReference } from '../../api/exports';
import { Status, useRemote } from '../workspace-operations/workspaceUi';
import WorkspacePageHeader from '../../components/layout/WorkspacePageHeader';
import ExportSspDialog from '../../components/ExportSspDialog';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readDocument(preview: SspPreview) {
  const document: unknown = JSON.parse(preview.content);
  const ssp = isRecord(document) ? document['system-security-plan'] : null;
  if (!isRecord(ssp)) throw new Error('The generated response does not contain an OSCAL system security plan.');
  const metadata = isRecord(ssp.metadata) ? ssp.metadata : {};
  const characteristics = isRecord(ssp['system-characteristics']) ? ssp['system-characteristics'] : {};
  return {
    preview,
    title: typeof metadata.title === 'string' ? metadata.title : 'System Security Plan',
    systemName: typeof characteristics['system-name'] === 'string' ? characteristics['system-name'] : 'Not recorded',
    description: typeof characteristics.description === 'string' ? characteristics.description : 'No system description was generated.',
  };
}

export default function SystemDocumentPreview() {
  const { id } = useParams<{ id: string }>();
  if (!id) return <p role="alert">A system is required to preview its documents.</p>;
  return <PreviewContent key={id} systemId={id} />;
}

function PreviewContent({ systemId }: { systemId: string }) {
  const [view, setView] = useState<'document' | 'source'>('document');
  const [retained, setRetained] = useState<ReturnType<typeof readDocument> | null>(null);
  const [retaining, setRetaining] = useState(false);
  const [retainError, setRetainError] = useState<string | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [exporting, setExporting] = useState(false);
  const requestKey = useRef<string | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);
  const data = useRemote(async signal => readDocument(await getSspPreview(systemId, signal)), [systemId]);
  const current = retained ?? data.data;
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const retain = async () => {
    if (request.current) return;
    const controller = new AbortController();
    request.current = controller;
    requestKey.current ??= crypto.randomUUID();
    setRetaining(true);
    setRetainError(null);
    setConfirmed(false);
    try {
      const result = readDocument(await retainSspPreview(systemId, requestKey.current, controller.signal));
      if (!controller.signal.aborted) setRetained(result);
    } catch (error) {
      if (!controller.signal.aborted) setRetainError(error instanceof Error ? error.message : 'Unable to retain the preview.');
    } finally {
      if (!controller.signal.aborted) { request.current = null; setRetaining(false); }
    }
  };
  return <div className="space-y-5">
    <WorkspacePageHeader title="Preview the generated documents"
      description="Inspect generated SSP content alongside the source diagnostics returned by the document service."
      actions={<Link to={`${base}/profile/EnvironmentAndDeployment`} className="self-start rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-medium text-white">Review source mapping</Link>} />
    <p className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900">
      Current working data preview — not an approved baseline, immutable package, or submission-readiness decision.
      Validate the selected package purpose before final generation.
    </p>
    <Status loading={!retained && data.loading} error={!retained ? data.error : null} retry={data.retry} />
    {retainError && <p role="alert" className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{retainError} Retry retains the original request identity.</p>}
    {current && <>
      <div className="flex flex-wrap items-center gap-3" aria-label="Document representation">
        <button type="button" aria-pressed={view === 'document'} onClick={() => setView('document')}
          className={`rounded-lg border px-3 py-2 text-sm ${view === 'document' ? 'border-indigo-300 bg-indigo-50 text-indigo-800' : 'border-slate-200 bg-white text-slate-700'}`}>Document summary</button>
        <button type="button" aria-pressed={view === 'source'} onClick={() => setView('source')}
          className={`rounded-lg border px-3 py-2 text-sm ${view === 'source' ? 'border-indigo-300 bg-indigo-50 text-indigo-800' : 'border-slate-200 bg-white text-slate-700'}`}>OSCAL source</button>
        {!retained && <button type="button" disabled={retaining} onClick={data.retry} className="text-sm text-indigo-700 underline dark:text-indigo-300">Refresh preview</button>}
        {!retained && <button type="button" disabled={retaining} onClick={() => void retain()} className="rounded-lg bg-indigo-600 px-3 py-2 text-sm text-white disabled:opacity-50">
          {retaining ? 'Retaining preview…' : 'Retain generated preview'}
        </button>}
      </div>
      {retained && <section className="space-y-3 rounded-lg border border-indigo-200 bg-indigo-50 p-4 text-sm text-indigo-900">
        <p>The server retained this exact generated document. Inspect the returned content and source versions before exporting. This does not approve working data.</p>
        <label className="flex items-start gap-2"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} className="mt-1" />
          I reviewed this exact retained preview and its source diagnostics.</label>
        <button type="button" disabled={!confirmed || retained.preview.sourceGaps.length > 0} onClick={() => setExporting(true)}
          className="rounded-lg bg-indigo-600 px-3 py-2 font-medium text-white disabled:opacity-50">Export retained OSCAL</button>
        {retained.preview.sourceGaps.length > 0 && <p>Resolve source diagnostics and create a new preview before final export.</p>}
        <button type="button" onClick={() => {
          setRetained(null); setConfirmed(false); requestKey.current = null; setRetainError(null); data.retry();
        }} className="ml-4 underline">Create a new preview</button>
      </section>}
      <div className="grid min-w-0 gap-6 xl:grid-cols-[minmax(0,2fr)_minmax(260px,1fr)]">
        <article className="min-w-0 rounded-lg border border-slate-200 bg-white p-6 dark:border-slate-700 dark:bg-slate-900">
          {view === 'document' ? <>
            <p className="mb-2 text-xs uppercase tracking-wider text-slate-500">Generated SSP preview</p>
            <h2 className="text-xl font-semibold">{current.title}</h2>
            <p className="mt-2 text-sm text-slate-500">{current.systemName}</p>
            <hr className="my-6 border-slate-200 dark:border-slate-700" />
            <h3 className="mb-3 font-semibold">System description</h3>
            <p className="whitespace-pre-wrap break-words text-sm leading-7">{current.description}</p>
            <p className="mt-6 text-xs text-slate-500">The OSCAL source view contains the complete generated content, including implementation and provider references. This summary displays fields from that response; it does not assemble a separate export.</p>
          </> : <pre aria-label="Generated OSCAL JSON" className="max-h-[65vh] overflow-auto whitespace-pre-wrap break-all text-xs leading-6">{current.preview.content}</pre>}
        </article>
        <aside className="min-w-0 space-y-6">
          <section className="border-l-2 border-indigo-200 pl-4">
            <h2 className="mb-3 text-sm font-semibold">Source diagnostics</h2>
            {current.preview.sourceGaps.length ? <ul className="space-y-3 text-sm text-amber-800 dark:text-amber-300">
              {current.preview.sourceGaps.map((gap, index) => <li key={`${gap.code}:${index}`}><p>{gap.message}</p><small>{gap.code}</small></li>)}
            </ul> : <p className="text-sm text-slate-500">No provider source gaps were reported by this generator. This does not establish package readiness.</p>}
          </section>
          <section className="border-l-2 border-indigo-200 pl-4">
            <h2 className="mb-3 text-sm font-semibold">Preview provenance</h2>
            <dl className="space-y-3 text-xs">
              <div><dt className="text-slate-500">Generated</dt><dd>{current.preview.generatedAt}</dd></div>
              <div><dt className="text-slate-500">Content SHA-256</dt><dd className="break-all">{current.preview.contentHash}</dd></div>
              {current.preview.previewId && <div><dt className="text-slate-500">Retained preview</dt><dd className="break-all">{current.preview.previewId}</dd></div>}
              <div><dt className="text-slate-500">Source state</dt><dd>Current working data</dd></div>
            </dl>
          </section>
          {current.preview.sourceManifest && <section className="border-l-2 border-indigo-200 pl-4">
            <h2 className="mb-3 text-sm font-semibold">Source versions</h2>
            <SourceReferences title="Approved profiles" items={current.preview.sourceManifest.profiles} />
            <SourceReferences title="Provider sources" items={current.preview.sourceManifest.providerSources} />
            <SourceReferences title="Narratives" items={current.preview.sourceManifest.narratives} />
            <p className="mt-3 text-xs text-slate-500">Other sources: {current.preview.sourceManifest.otherSources}</p>
          </section>}
          <nav aria-label="Document next actions" className="flex flex-col gap-3 text-sm text-indigo-700 dark:text-indigo-300">
            <Link to={`${base}/documents`}>Review package readiness</Link>
            <Link to={`${base}/inheritance/subscriptions`}>Review responsibilities</Link>
            <Link to={`${base}/evidence`}>Review evidence</Link>
            <Link to={`${base}/documents?tab=exports`}>Generate an export package</Link>
          </nav>
        </aside>
      </div>
    </>}
    {exporting && retained?.preview.previewId && <ExportSspDialog systemId={systemId}
      sourcePreviewId={retained.preview.previewId} sourceContentHash={retained.preview.contentHash}
      onClose={() => setExporting(false)} />}
  </div>;
}

function SourceReferences({ title, items }: { title: string; items: DocumentSourceReference[] }) {
  return <details className="mt-3 text-xs"><summary className="cursor-pointer font-medium">{title} ({items.length})</summary>
    <ul className="mt-2 space-y-3">{items.map((item, index) => <li key={`${item.recordId}:${item.versionId}:${index}`} className="break-all">
      <p>{item.kind}: {item.recordId}</p><p>Version: {item.versionId}</p><p>SHA-256: {item.contentHash}</p>
    </li>)}</ul>
  </details>;
}
