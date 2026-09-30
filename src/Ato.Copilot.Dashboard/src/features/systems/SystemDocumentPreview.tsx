import { useEffect, useRef, useState } from 'react';
import { Link, useParams, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import { getSspPreview, getAdditionalDocumentPreview, retainSspPreview, type SspPreview, type AdditionalDocumentPreview, type AdditionalDocumentType, type DocumentSourceReference, type DocumentRecordReference } from '../../api/exports';
import { Status, useRemote, moveTabFocus } from '../workspace-operations/workspaceUi';
import WorkspacePageHeader from '../../components/layout/WorkspacePageHeader';
import ExportSspDialog from '../../components/ExportSspDialog';
import { systemProfileTasks } from './systemProfileTasks';
import './sspDocument.css';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

type DocumentType = 'ssp' | AdditionalDocumentType;
const documentTypes: Record<DocumentType, { label: string; title: string; model: string; sourcePath: string; sourceLabel: string }> = {
  ssp: { label: 'SSP', title: 'System Security Plan', model: 'system-security-plan', sourcePath: 'profile/EnvironmentAndDeployment', sourceLabel: 'Review source mapping' },
  sap: { label: 'SAP', title: 'Security Assessment Plan', model: 'security-assessment-plan', sourcePath: 'assessments?tab=plan', sourceLabel: 'Review assessment plan' },
  sar: { label: 'SAR', title: 'Security Assessment Report', model: 'security-assessment-report', sourcePath: 'assessments', sourceLabel: 'Review assessment work' },
  poam: { label: 'POA&M', title: 'Plan of Action and Milestones', model: 'poam-register', sourcePath: 'poam', sourceLabel: 'Review POA&M register' },
};

function readDocument(preview: SspPreview | AdditionalDocumentPreview, documentType: DocumentType = 'ssp') {
  const definition = documentTypes[documentType];
  const document: unknown = JSON.parse(preview.content);
  const body = isRecord(document) ? document[definition.model] : null;
  if (!isRecord(body)) throw new Error(documentType === 'ssp'
    ? 'The generated response does not contain an OSCAL system security plan.'
    : `The generated response does not contain the expected ${definition.model} document.`);
  const metadata = isRecord(body.metadata) ? body.metadata : {};
  const characteristics = isRecord(body['system-characteristics']) ? body['system-characteristics'] : {};
  const properties = Array.isArray(characteristics.props) ? characteristics.props : [];
  const profiles = properties.flatMap((property: unknown) => {
    if (!isRecord(property) || !['working-profile', 'approved-profile'].includes(String(property.name))) return [];
    if (typeof property.value !== 'string') throw new Error('The generated profile contribution has no readable source payload.');
    const payload: unknown = JSON.parse(property.value);
    if (!isRecord(payload) || typeof payload.sectionType !== 'string') throw new Error('The generated profile contribution is invalid.');
    const content = readProfileContent(payload.content ?? payload.Content, property.name === 'approved-profile');
    const fields = 'scalarContent' in content ? readProfileContent(content.scalarContent, true) : content;
    const collections = { UsersAndAccess: 'userCategories', DataTypes: 'dataTypeEntries',
      PortsProtocolsAndServices: 'ppsEntries', LeveragedAuthorizations: 'leveragedAuthorizations' };
    for (const key of Object.values(collections)) {
      const rows = content[key];
      if (Array.isArray(rows) && (rows.length || key === collections[payload.sectionType as keyof typeof collections])) fields[key] = rows;
    }
    return [{ sectionType: payload.sectionType, fields, working: property.name === 'working-profile',
      status: typeof payload.governanceStatus === 'string' ? payload.governanceStatus : null,
      reviewScope: typeof payload.reviewScope === 'string' ? payload.reviewScope : null }];
  });
  return {
    available: true as const,
    preview,
    body,
    metadata,
    envelope: isRecord(document) ? Object.fromEntries(Object.entries(document).filter(([key]) => key !== definition.model)) : {},
    profiles,
    title: typeof metadata.title === 'string' ? metadata.title : typeof body.title === 'string' ? body.title : definition.title,
    systemName: 'systemName' in preview ? preview.systemName
      : typeof characteristics['system-name'] === 'string' ? characteristics['system-name'] : 'Not recorded',
    documentStatus: 'documentStatus' in preview ? preview.documentStatus : null,
    sourceRecords: 'sourceRecords' in preview ? preview.sourceRecords : undefined,
  };
}

function readProfileContent(value: unknown, allowLegacyText = false): Record<string, unknown> {
  if (value === null || value === undefined || value === '') return {};
  let parsed: unknown = value;
  if (typeof value === 'string') {
    try { parsed = JSON.parse(value); }
    catch (error) {
      if (allowLegacyText && error instanceof SyntaxError) return { 'Unstructured saved text': value };
      throw error;
    }
  }
  if (allowLegacyText && !isRecord(parsed)) return { 'Unstructured saved text': value };
  if (!isRecord(parsed)) throw new Error('The generated profile contribution is not a structured record.');
  return { ...parsed };
}

function fieldLabel(name: string) {
  return name.replace(/[-_]/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, letter => letter.toUpperCase());
}

const documentSectionNames: Record<string, string> = {
  uuid: 'Document identity', metadata: 'Document metadata', 'import-profile': 'Control baseline',
  'system-characteristics': 'System characteristics & boundaries',
  'system-implementation': 'System implementation & inventory',
  'control-implementation': 'Control implementation',
  'back-matter': 'References & back matter',
  'import-ssp': 'System security plan reference', 'import-ap': 'Assessment plan reference',
  'local-definitions': 'Document definitions', 'reviewed-controls': 'Assessment scope',
  'assessment-subjects': 'Assessment subjects', 'assessment-assets': 'Assessment assets',
  tasks: 'Assessment activities', results: 'Assessment results',
  observations: 'Observations', risks: 'Risks', findings: 'Findings', 'poam-items': 'Plan of action and milestones',
  controlEntries: 'Assessment control scope', teamMembers: 'Assessment team', sections: 'Report sections',
  content: 'Saved plan content', scopeNotes: 'Assessment scope notes', assessmentApproach: 'Assessment approach',
  rulesOfEngagement: 'Rules of engagement', findingsBySeverity: 'Findings by severity',
  findingsByFamily: 'Findings by control family', items: 'POA&M items',
};

function sectionLabel(key: string) {
  return Object.prototype.hasOwnProperty.call(documentSectionNames, key) ? documentSectionNames[key] : fieldLabel(key);
}

function recordLabel(value: unknown) {
  if (!isRecord(value)) return 'Value';
  const label = ['title', 'name', 'control-id', 'controlId', 'weaknessName', 'sectionType', 'uuid', 'id'].map(key => value[key])
    .find((item): item is string => typeof item === 'string' && item.length > 0);
  return label ? label.slice(0, 160) : 'Record';
}

function knownProfileContribution(value: string | null) {
  return value && Object.prototype.hasOwnProperty.call(systemProfileTasks, value) ? value as keyof typeof systemProfileTasks : null;
}

function GeneratedFields({ value, complete = false, expanded = false, path = '' }: {
  value: unknown; complete?: boolean; expanded?: boolean; path?: string;
}) {
  const childPath = (key: string | number) => `${path}/${String(key).replace(/~/g, '~0').replace(/\//g, '~1')}`;
  if (complete && (value === null || typeof value !== 'object')) {
    const text = value === null || value === '' ? 'Not recorded' : String(value);
    const field = <span data-document-value-path={path} data-ssp-value-path={path} className="whitespace-pre-wrap break-words">{text}</span>;
    return typeof value === 'string' && value.length > 2000
      ? <details open={expanded}><summary className="cursor-pointer text-xs text-indigo-700">Long text ({value.length.toLocaleString()} characters)</summary>
        <div className="mt-2 min-w-0 text-xs">{field}</div></details> : field;
  }
  if (value === null || value === undefined || value === '') return <span className="text-slate-500">Not recorded</span>;
  if (Array.isArray(value)) return value.length ? <div className="space-y-3">{value.map((item, index) =>
    complete && value.length > 12
      ? <details key={index} open={expanded} className="min-w-0 rounded border border-slate-200 p-3 dark:border-slate-700">
        <summary className="cursor-pointer break-words text-sm font-medium">
          {index + 1}. {recordLabel(item)}
        </summary>
        <div className="mt-3"><GeneratedFields value={item} complete expanded={expanded} path={childPath(index)} /></div>
      </details>
      : <div key={index} className="min-w-0 rounded border border-slate-200 p-3 dark:border-slate-700">
        <GeneratedFields value={item} complete={complete} expanded={expanded} path={childPath(index)} />
      </div>)}</div>
    : <span className="text-slate-500">No records in this generated contribution.</span>;
  if (isRecord(value) && !Object.keys(value).length) return <span className="text-slate-500">No fields in this generated record.</span>;
  if (isRecord(value)) return <dl className="grid gap-3 sm:grid-cols-2">{Object.entries(value)
    .filter(([key]) => complete || !['id', 'tenantId', 'systemProfileSectionId', 'sortOrder', 'approvedSnapshotId', 'canSubmit', 'canWithdraw', 'canReview'].includes(key))
    .map(([key, item]) => <div key={key} className={`min-w-0 ${isRecord(item) || Array.isArray(item) || typeof item === 'string' && item.length > 100 ? 'sm:col-span-2' : ''}`}>
      <dt className="mb-1 text-xs font-medium text-slate-500">{fieldLabel(key)}</dt>
      <dd className="whitespace-pre-wrap break-words text-sm"><GeneratedFields value={item} complete={complete} expanded={expanded} path={childPath(key)} /></dd>
    </div>)}</dl>;
  return <>{typeof value === 'boolean' ? value ? 'Yes' : 'No' : String(value)}</>;
}

export default function SystemDocumentPreview() {
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const navigate = useNavigate();
  const params = new URLSearchParams(location.search);
  const selected = params.get('document') ?? 'ssp';
  const documentType = Object.prototype.hasOwnProperty.call(documentTypes, selected) ? selected as DocumentType : null;
  const contribution = documentType === 'ssp' ? params.get('contribution') : null;
  if (!id) return <p role="alert">A system is required to preview its documents.</p>;
  const navigation = <nav role="tablist" aria-label="Document types" className="system-section-tabs" onKeyDown={moveTabFocus}>
    {(Object.keys(documentTypes) as DocumentType[]).map(type => <button key={type} type="button" role="tab"
      id={`document-tab-${type}`} aria-controls={`document-panel-${type}`} aria-selected={documentType === type}
      tabIndex={documentType === type ? 0 : -1}
      className={`shrink-0 border-b-2 px-1 py-3 text-sm ${documentType === type ? 'border-indigo-600 font-semibold text-indigo-700' : 'border-transparent text-slate-500'}`}
      onClick={() => {
        if (type === documentType) return;
        const next = new URLSearchParams(location.search);
        if (type === 'ssp') next.delete('document'); else { next.set('document', type); next.delete('contribution'); }
        navigate(`${location.pathname}${next.toString() ? `?${next}` : ''}`);
      }}>{documentTypes[type].label}</button>)}
  </nav>;
  const definition = documentType ? documentTypes[documentType] : null;
  const knownContribution = knownProfileContribution(contribution);
  return <div className="ssp-preview-root space-y-5">
    <WorkspacePageHeader title="Preview the generated documents"
      description={definition ? `Inspect the ${definition.title} and its source diagnostics without changing assessment or authorization records.` : 'Select SSP, SAP, SAR or POA&M.'}
      actions={definition && <Link to={`/systems/${encodeURIComponent(id)}/${knownContribution ? `profile/${knownContribution}` : definition.sourcePath}`}
        className="self-start rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-medium text-white">{definition.sourceLabel}</Link>} />
    {navigation}
    {documentType
      ? <PreviewContent key={`${id}:${documentType}:${contribution ?? ''}`} systemId={id} documentType={documentType} contribution={contribution} />
      : <p role="alert">Unsupported document type. Choose a document above.</p>}
  </div>;
}

function PreviewContent({ systemId, documentType, contribution }: { systemId: string; documentType: DocumentType; contribution: string | null }) {
  const definition = documentTypes[documentType];
  const [view, setView] = useState<'document' | 'source'>('document');
  const [retained, setRetained] = useState<ReturnType<typeof readDocument> | null>(null);
  const [retaining, setRetaining] = useState(false);
  const [retainError, setRetainError] = useState<string | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [expanded, setExpanded] = useState(true);
  const requestKey = useRef<string | null>(null);
  const request = useRef<AbortController | null>(null);
  useEffect(() => () => request.current?.abort(), []);
  const data = useRemote(async signal => {
    if (documentType === 'ssp') return readDocument(await getSspPreview(systemId, signal));
    const preview = await getAdditionalDocumentPreview(systemId, documentType, signal);
    return preview.available ? readDocument(preview, documentType) : preview;
  }, [systemId, documentType, contribution]);
  const current = retained ?? (!data.loading && !data.error && data.data?.available === true ? data.data : null);
  const unavailable = !data.loading && !data.error && data.data?.available === false ? data.data : null;
  const base = `/systems/${encodeURIComponent(systemId)}`;
  const knownContribution = knownProfileContribution(contribution);
  const profiles = current?.profiles ?? [];
  const baseDocumentPath = `/${definition.model}`;
  const sections: { key: string; title: string; value: unknown; path: string }[] = [];
  if (current) {
    const details: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(current.body)) {
      if (documentType !== 'ssp' && !Array.isArray(value) && !isRecord(value)
        && !['content', 'scopeNotes', 'assessmentApproach', 'rulesOfEngagement', 'findingsBySeverity', 'findingsByFamily'].includes(key)) {
        details[key] = value;
      } else {
        sections.push({ key, title: sectionLabel(key) ?? fieldLabel(key), value,
          path: `${baseDocumentPath}/${key.replace(/~/g, '~0').replace(/\//g, '~1')}` });
      }
    }
    if (Object.keys(details).length) sections.unshift({ key: 'document-details', title: 'Document details', value: details, path: baseDocumentPath });
  }
  const reviewOnly = retained?.preview.canGenerate === false || retained?.preview.sourceManifest?.previewOnly === true;
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
  return <div role="tabpanel" id={`document-panel-${documentType}`} aria-labelledby={`document-tab-${documentType}`} className="space-y-5">
    <p className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900">
      {documentType === 'ssp'
        ? 'Current working data preview — not an approved baseline, immutable package, or submission-readiness decision. Save edits before previewing.'
        : 'Read-only preview of current saved records. Viewing this document does not create, finalize or approve an assessment or submission.'}
      {' '}Validate the selected package purpose before final generation.
    </p>
    <Status loading={!retained && data.loading} error={!retained ? data.error : null} retry={data.retry} />
    {unavailable && <section role="status" className="rounded-lg border border-slate-200 bg-white p-6 dark:border-slate-700 dark:bg-slate-900">
      <h2 className="text-lg font-semibold">{definition.label} is not available yet</h2>
      <p className="mt-3">{unavailable.message}</p>
      <p className="mt-2 text-xs text-slate-500">{unavailable.reasonCode}</p>
      <p className="mt-3 text-sm text-slate-500">Use the source-workflow link above to prepare the document. Opening this preview does not create or finalize it.</p>
      <button type="button" onClick={data.retry} className="mt-4 text-sm text-indigo-700 underline">Refresh availability</button>
    </section>}
    {retainError && <p role="alert" className="rounded border border-red-200 bg-red-50 p-3 text-sm text-red-800">{retainError} Retry retains the original request identity.</p>}
    {current && <>
      <div className="flex flex-wrap items-center gap-3" aria-label="Document representation">
        <button type="button" aria-pressed={view === 'document'} onClick={() => setView('document')}
          className={`rounded-lg border px-3 py-2 text-sm ${view === 'document' ? 'border-indigo-300 bg-indigo-50 text-indigo-800' : 'border-slate-200 bg-white text-slate-700'}`}>{definition.label} document</button>
        <button type="button" aria-pressed={view === 'source'} onClick={() => setView('source')}
          className={`rounded-lg border px-3 py-2 text-sm ${view === 'source' ? 'border-indigo-300 bg-indigo-50 text-indigo-800' : 'border-slate-200 bg-white text-slate-700'}`}>{documentType === 'ssp' ? 'OSCAL source' : 'JSON source'}</button>
        {!retained && <button type="button" disabled={retaining} onClick={data.retry} className="text-sm text-indigo-700 underline dark:text-indigo-300">Refresh preview</button>}
        {documentType === 'ssp' && !retained && <button type="button" disabled={retaining} onClick={() => void retain()} className="rounded-lg bg-indigo-600 px-3 py-2 text-sm text-white disabled:opacity-50">
          {retaining ? 'Retaining preview…' : 'Retain generated preview'}
        </button>}
      </div>
      {retained && <section className="space-y-3 rounded-lg border border-indigo-200 bg-indigo-50 p-4 text-sm text-indigo-900">
        <p>{reviewOnly
          ? 'The server retained this exact working preview for review. Create a new preview after saving source changes; this snapshot will not update automatically.'
          : 'The server retained this exact generated document. Inspect the returned content and source versions before exporting. This does not approve working data.'}</p>
        <label className="flex items-start gap-2"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} className="mt-1" />
          I reviewed this exact retained preview and its source diagnostics.</label>
        <button type="button" disabled={!confirmed || reviewOnly || retained.preview.sourceGaps.length > 0} onClick={() => setExporting(true)}
          className="rounded-lg bg-indigo-600 px-3 py-2 font-medium text-white disabled:opacity-50">Export retained OSCAL</button>
        {!reviewOnly && retained.preview.sourceGaps.length > 0 && <p>Resolve source diagnostics and create a new preview before final export.</p>}
        {reviewOnly && <p>Working previews are review-only. Generate a separate approved-source export after completing the required reviews.</p>}
        <button type="button" onClick={() => {
          setRetained(null); setConfirmed(false); requestKey.current = null; setRetainError(null); data.retry();
        }} className="ml-4 underline">Create a new preview</button>
      </section>}
      <div className="grid min-w-0 gap-6 xl:grid-cols-[minmax(0,2fr)_minmax(260px,1fr)]">
        <article className={view === 'document' ? 'ssp-paper' : 'min-w-0 rounded-lg border border-slate-200 bg-white p-6 dark:border-slate-700 dark:bg-slate-900'}>
          {view === 'document' ? <>
            <section className="ssp-cover" aria-label={`${definition.label} cover page`}>
              <p className="ssp-kicker">{definition.title}</p>
              <h2>{current.title}</h2>
              <p className="ssp-cover-system">{current.systemName}</p>
              <p className="text-sm">Version: {typeof current.metadata.version === 'string' ? current.metadata.version : 'Not recorded'}</p>
              <p className="ssp-draft-notice">{documentType === 'ssp' ? 'WORKING DRAFT' : 'READ-ONLY PREVIEW'}<br />This preview is not a signed or approved submission package and does not establish authorization.</p>
              <p className="text-xs">{documentType === 'ssp' ? 'FedRAMP-inspired presentation' : 'Formal document presentation'} · SPIN generated content<br />
                {documentType === 'ssp' ? 'Not an official FedRAMP template or approval.' : 'Not a signed assessment or authorization decision.'}</p>
            </section>
            <section className="ssp-front-matter" aria-label="Document control">
              <h3 className="mb-4">Document control</h3>
              <table aria-label="Document control" className="ssp-control-table"><tbody>
                {[
                  ['System / service', current.systemName],
                  ['SPIN system ID', systemId],
                  ['Document identifier', typeof current.body.uuid === 'string' ? current.body.uuid : typeof current.body.id === 'string' ? current.body.id : 'Not recorded'],
                  ['Document version', typeof current.metadata.version === 'string' ? current.metadata.version : 'Not recorded'],
                  ...(current.documentStatus ? [['Recorded document state', current.documentStatus]] : []),
                  ['Generated (UTC)', current.preview.generatedAt],
                  ['Source state', 'Saved working data — review-only preview'],
                ].map(([label, value]) => <tr key={label}><th scope="row">{label}</th><td>{value}</td></tr>)}
              </tbody></table>
              <h4 className="mb-2 mt-6 font-semibold">Recorded revision history</h4>
              {Array.isArray(current.metadata.revisions) && current.metadata.revisions.length
                ? <GeneratedFields value={current.metadata.revisions} />
                : <p className="text-xs">No revision history was supplied in the generated document. No history or signatures have been inferred.</p>}
            </section>
            <p className="mt-4 text-sm text-slate-600 dark:text-slate-300">
              All generated {definition.label} sections are included below. {documentType === 'ssp' && 'A contribution link highlights its source without hiding the rest of the document.'}
            </p>
            <section className="ssp-front-matter">
            <h3>Table of contents</h3>
            <nav aria-label={`${definition.label} document sections`} className="ssp-toc">
              {profiles.length > 0 && <a href="#ssp-profile-contributions">Profile contributions</a>}
              {Object.keys(current.envelope).length > 0 && <a className="underline" href="#ssp-envelope">Document envelope</a>}
              {sections.map((section, index) => <a key={section.key} href={`#${documentType}-section-${index}`}>{index + 1}. {section.title}</a>)}
            </nav>
            </section>
            {contribution && !profiles.some(profile => profile.sectionType === contribution) && <p role="alert" className="mt-5 rounded border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900">
              No {knownContribution ? systemProfileTasks[knownContribution].title : contribution} contribution was returned in this generated SSP.
              Review the source diagnostics; this is not evidence that the saved record is included.
            </p>}
            {profiles.length > 0 && <h3 id="ssp-profile-contributions" className="mb-4 text-lg font-semibold">Profile contributions</h3>}
            {profiles.map((profile, index) => <section key={`${profile.sectionType}:${index}`} data-highlighted-contribution={profile.sectionType === contribution ? 'true' : undefined}
              className={`ssp-profile-contribution my-5 border p-4 ${profile.sectionType === contribution ? 'border-indigo-400 bg-indigo-50/30 dark:border-indigo-500' : 'border-slate-200 dark:border-slate-700'}`}>
              <h3 className="mb-2 text-lg font-semibold">{Object.prototype.hasOwnProperty.call(systemProfileTasks, profile.sectionType)
                ? systemProfileTasks[profile.sectionType as keyof typeof systemProfileTasks].title : fieldLabel(profile.sectionType)}</h3>
              <p className="mb-4 text-xs text-slate-500">{profile.working ? 'Saved working source — not an approved export' : 'Approved source'}
                {profile.status ? ` · ${profile.reviewScope === 'AccessContext' ? 'Access context' : 'Section'}: ${profile.status}` : ''}
              </p>
              <GeneratedFields value={profile.fields} />
            </section>)}
            <div className="my-6 flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 pt-5">
              <h3 className="text-lg font-semibold">Complete generated {definition.label}</h3>
              <button type="button" className="text-sm text-indigo-700 underline dark:text-indigo-300"
                onClick={() => setExpanded(value => !value)}>{expanded ? 'Collapse long records' : 'Expand all records'}</button>
            </div>
            {Object.keys(current.envelope).length > 0 && <section id="ssp-envelope" aria-label="Document envelope" className="ssp-generated-section min-w-0">
              <h3 className="mb-4 text-lg font-semibold">Document envelope</h3>
              <GeneratedFields value={current.envelope} complete expanded={expanded} />
            </section>}
            {sections.map((section, index) => <section key={section.key} id={`${documentType}-section-${index}`} aria-label={section.title}
              className="ssp-generated-section min-w-0">
              <h3 className="mb-4">{index + 1}. {section.title}</h3>
              <GeneratedFields value={section.value} complete expanded={expanded} path={section.path} />
            </section>)}
            <p className="ssp-document-footer">SPIN · {documentType === 'ssp' ? 'Working' : 'Read-only'} {definition.title} preview<br />
              This view reads every {definition.label} section without assembling a separate export. Long records can be expanded individually or together; the {documentType === 'ssp' ? 'OSCAL' : 'JSON'} source tab preserves the exact response.
              Section organization follows the {documentType === 'ssp' ? 'generated OSCAL' : 'canonical saved-record'} document, not a claim of FedRAMP template conformity.
            </p>
          </> : <pre aria-label={documentType === 'ssp' ? 'Generated OSCAL JSON' : 'Document JSON'} className="max-h-[65vh] overflow-auto whitespace-pre-wrap break-all text-xs leading-6">{current.preview.content}</pre>}
        </article>
        <aside className="min-w-0 space-y-6">
          <section className="border-l-2 border-indigo-200 pl-4">
            <h2 className="mb-3 text-sm font-semibold">Source diagnostics</h2>
            {current.preview.sourceGaps.length ? <ul className="space-y-3 text-sm text-amber-800 dark:text-amber-300">
              {current.preview.sourceGaps.map((gap, index) => <li key={`${gap.code}:${index}`}><p>{gap.message}</p><small>{gap.code}</small></li>)}
            </ul> : <p className="text-sm text-slate-500">No source gaps were reported by this generator. This does not establish package readiness.</p>}
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
            <SourceReferences title="Profile sources" items={current.preview.sourceManifest.profiles} />
            <SourceReferences title="Provider sources" items={current.preview.sourceManifest.providerSources} />
            <SourceReferences title="Narratives" items={current.preview.sourceManifest.narratives} />
            <p className="mt-3 text-xs text-slate-500">Other sources: {current.preview.sourceManifest.otherSources}</p>
          </section>}
          {current.sourceRecords && <section className="border-l-2 border-indigo-200 pl-4">
            <h2 className="mb-3 text-sm font-semibold">Document source versions</h2>
            <SourceReferences title="Document sources" items={current.sourceRecords} />
          </section>}
          <nav aria-label="Document next actions" className="flex flex-col gap-3 text-sm text-indigo-700 dark:text-indigo-300">
            <Link to={`${base}/documents?purpose=InitialSubmission`}>Review package readiness</Link>
            <Link to={`${base}/inheritance/subscriptions`}>Review responsibilities</Link>
            <Link to={`${base}/evidence`}>Review evidence</Link>
            <Link to={`${base}/documents?tab=exports&purpose=InitialSubmission`}>Generate an export package</Link>
          </nav>
        </aside>
      </div>
    </>}
    {exporting && retained?.preview.previewId && <ExportSspDialog systemId={systemId}
      sourcePreviewId={retained.preview.previewId} sourceContentHash={retained.preview.contentHash}
      onClose={() => setExporting(false)} />}
  </div>;
}

function SourceReferences({ title, items }: { title: string; items: readonly (DocumentSourceReference | DocumentRecordReference)[] }) {
  return <details className="mt-3 text-xs"><summary className="cursor-pointer font-medium">{title} ({items.length})</summary>
    <ul className="mt-2 space-y-3">{items.map((item, index) => <li key={`${item.recordId}:${item.versionId}:${index}`} className="break-all">
      <p>{item.kind}: {item.recordId}</p><p>Version: {item.versionId ?? 'Not recorded'}</p><p>SHA-256: {item.contentHash}</p>
    </li>)}</ul>
  </details>;
}
