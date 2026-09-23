import { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../workspaces/WorkspaceBoundary';
import { ArrowRight, LockKeyhole } from 'lucide-react';
import SetupDialog from './SetupDialog';
import { OrganizationComponentFields, OrganizationRecordChoices, OrganizationSupportingComponents, emptyComponent } from './OrganizationCatalogChoices';
import { OrganizationDialogSteps, OrganizationInfo, OrganizationRecordType, OrganizationReviewSummary, OrganizationSourceCards } from './OrganizationDialogPresentation';
import { buttonClass, errorClass, inputClass, message, secondaryButtonClass, Status, useQueryState, useRemote, warningClass } from './workspaceUi';
import * as api from './api';
import type {
  InlineLocalCapability, OrganizationCapability, OrganizationCatalogAddition,
  OrganizationCatalogAdditionResult, OrganizationComponentDraft,
} from './types';

const families = ['AC', 'AT', 'AU', 'CA', 'CM', 'CP', 'IA', 'IR', 'MA', 'MP', 'PE', 'PL', 'PM', 'PS', 'PT', 'RA', 'SA', 'SC', 'SI', 'SR'];
const implementationStatuses = ['Planned', 'InProgress', 'Implemented', 'Deprecated'];
const emptyCapability: InlineLocalCapability = {
  name: '', provider: 'Organization', category: 'AC', description: '', implementationStatus: 'Planned', owner: '',
};
const recordKey = (record: { source: string; recordId: string }) => `${record.source}:${record.recordId.toLowerCase()}`;

export default function OrganizationCatalogDialog({ tenantId }: { tenantId: string }) {
  const session = useWorkspaceSession();
  const { params, set } = useQueryState();
  const location = useLocation();
  const navigate = useNavigate();
  const seed = params.get('setupRecord') ?? params.get('record') ?? '';
  const [source, setSource] = useState<'local' | 'provider'>((params.get('setupSource') ?? params.get('source')) === 'provider' ? 'provider' : 'local');
  const [recordType, setRecordType] = useState<'capability' | 'component'>((params.get('setupRecordType') ?? params.get('recordType') ?? params.get('grouping')) === 'component' ? 'component' : 'capability');
  const [reuse, setReuse] = useState(!!seed);
  const [recordId, setRecordId] = useState(seed);
  const [capability, setCapability] = useState(emptyCapability);
  const [component, setComponent] = useState(emptyComponent);
  const [components, setComponents] = useState<OrganizationCapability[]>([]);
  const [newComponents, setNewComponents] = useState<OrganizationComponentDraft[]>([]);
  const [editingComponent, setEditingComponent] = useState(false);
  const [contribution, setContribution] = useState('');
  const [owner, setOwner] = useState('');
  const [step, setStep] = useState(1);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [canEditFailure, setCanEditFailure] = useState(false);
  const [result, setResult] = useState<OrganizationCatalogAdditionResult | null>(null);
  const attempt = useRef<OrganizationCatalogAddition | null>(null);
  const saving = useRef(false);
  const mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; }; }, []);

  const access = useRemote(signal => api.getOrganizationCatalogAccess(tenantId, signal), [tenantId]);
  const existing = source === 'provider' || reuse;
  const selected = useRemote(signal => existing && recordId
    ? api.getOrganizationCapability(tenantId, source, recordId, undefined, signal, recordType)
    : Promise.resolve(null), [tenantId, source, recordId, recordType, existing]);
  useEffect(() => {
    setContribution(selected.data?.capability.organizationContribution ?? '');
    setOwner(selected.data?.capability.organizationOwner ?? '');
  }, [selected.data]);

  const name = existing ? selected.data?.capability.name : recordType === 'capability' ? capability.name.trim() : component.name.trim();
  const ready = existing ? !!selected.data && !selected.loading && !selected.error
    : !!name && !!(recordType === 'capability' ? capability.description.trim() : component.description.trim());
  const close = () => {
    if (saving.current) return;
    if (location.pathname === '/security-capabilities/setup') navigate('/security-capabilities', { replace: true });
    else set({ dialog: null, step: null, stage: null, setupSource: null, setupRecord: null, setupRecordType: null, operation: null });
  };
  const resetSelection = () => {
    setRecordId(''); setComponents([]); setNewComponents([]); setContribution(''); setOwner(''); setError(null); setEditingComponent(false);
  };
  const request = (): OrganizationCatalogAddition => ({
    idempotencyKey: crypto.randomUUID(), source, recordType,
    ...(existing ? { recordId } : recordType === 'capability'
      ? { capability: { ...capability, name: capability.name.trim(), description: capability.description.trim(), owner: owner.trim() } }
      : { component: { ...component, name: component.name.trim(), description: component.description.trim(), owner: owner.trim() } }),
    components: recordType === 'capability' ? components.map(({ source: componentSource, recordId: componentId }) => ({ source: componentSource, recordId: componentId })) : [],
    newComponents: recordType === 'capability' ? newComponents.map(draft => ({ ...draft, owner: draft.owner || owner.trim() })) : [],
    organizationContribution: contribution.trim(), owner: owner.trim(),
  });
  const save = async () => {
    if (saving.current || !access.data?.canManageCatalog) return;
    attempt.current ??= request();
    saving.current = true; setBusy(true); setError(null); setCanEditFailure(false);
    try {
      const saved = await api.addOrganizationCatalogRecord(tenantId, attempt.current);
      if (mounted.current) { setResult(saved); setStep(4); }
    } catch (reason) {
      if (mounted.current) {
        setError(message(reason));
        const status = typeof reason === 'object' && reason !== null && 'status' in reason ? reason.status : null;
        setCanEditFailure(status === 400 || status === 404 || status === 422);
      }
    } finally {
      saving.current = false;
      if (mounted.current) setBusy(false);
    }
  };

  const supportingComponents = recordType === 'capability' && <OrganizationSupportingComponents key={`${source}:${reuse}`} tenantId={tenantId}
    editing={editingComponent} onEditingChange={setEditingComponent}
    selected={components} drafts={newComponents} retained={selected.data?.supportingComponents ?? []}
    onSelect={item => setComponents(current => current.some(entry => recordKey(entry) === recordKey(item))
      ? current.filter(entry => recordKey(entry) !== recordKey(item)) : [...current, item])}
    onRemove={item => setComponents(current => current.filter(entry => recordKey(entry) !== recordKey(item)))}
    onStage={draft => setNewComponents(current => [...current, draft])}
    onRemoveDraft={index => setNewComponents(current => current.filter((_, position) => position !== index))} />;
  const reviewSummary = <OrganizationReviewSummary source={source} name={name} owner={owner}
    componentNames={[...(selected.data?.supportingComponents ?? []), ...components, ...newComponents].map(item => item.name)} />;

  return <SetupDialog busy={busy} onClose={close} organizationName={session?.workspace.displayName ?? tenantId} expanded={step === 2 || step === 3}>
    <Status loading={access.loading} error={access.error} retry={access.retry} />
    {access.data && !access.data.canManageCatalog && <p role="alert" className={warningClass}>Organization catalog management permission is required. Request access from your organization administrator.</p>}
    {error && <p role="alert" className={`${errorClass} mb-4`}>{error}</p>}
    {access.data?.canManageCatalog && <>
      {step < 4 && <OrganizationDialogSteps step={step} />}
      {step === 1 && <form className="space-y-3" onSubmit={event => { event.preventDefault(); if (ready && !editingComponent) setStep(2); }}>
        <OrganizationSourceCards source={source} onChange={value => { setSource(value); resetSelection(); }} />
        {source === 'provider' && <h3 className="pt-1 text-xs font-semibold">Browse offerings available to your organization</h3>}
        <OrganizationRecordType value={recordType} onChange={value => { setRecordType(value); resetSelection(); }} />
        {source === 'local' && <fieldset className="flex flex-wrap gap-3 text-[11px] text-slate-500"><legend className="sr-only">Organization record action</legend>
          <label className="flex gap-2"><input type="radio" name="record-action" checked={!reuse} onChange={() => { setReuse(false); resetSelection(); }} />Create new organization record</label>
          <label className="flex gap-2"><input type="radio" name="record-action" checked={reuse} onChange={() => { setReuse(true); resetSelection(); }} />Use existing organization record</label>
        </fieldset>}
        {existing ? <>
          <OrganizationRecordChoices key={`${source}:${recordType}`} tenantId={tenantId} source={source} recordType={recordType}
            selected={[`${source}:${recordId.toLowerCase()}`]} onSelect={item => setRecordId(item.recordId)} />
          {recordId && <><Status loading={selected.loading} error={selected.error} retry={selected.retry} />
            {selected.data && <p className="text-xs">Selected: <strong>{selected.data.capability.name}</strong></p>}</>}
        </> : recordType === 'component'
          ? <OrganizationComponentFields value={component} onChange={setComponent} />
          : <div className="grid gap-3 sm:grid-cols-2">
            <label className="grid gap-1 text-xs font-medium sm:col-span-2">Name<input className={inputClass} required maxLength={200} value={capability.name} onChange={event => setCapability({ ...capability, name: event.target.value })} /></label>
            <label className="grid gap-1 text-xs font-medium sm:col-span-2">Description<textarea required rows={2} maxLength={8000} className={inputClass} value={capability.description} onChange={event => setCapability({ ...capability, description: event.target.value })} /></label>
            <label className="grid gap-1 text-xs font-medium">Control family<select className={inputClass} value={capability.category} onChange={event => setCapability({ ...capability, category: event.target.value })}>{families.map(family => <option key={family}>{family}</option>)}</select></label>
            <label className="grid gap-1 text-xs font-medium">Implementation status<select className={inputClass} value={capability.implementationStatus} onChange={event => setCapability({ ...capability, implementationStatus: event.target.value })}>{implementationStatuses.map(status => <option key={status}>{status}</option>)}</select></label>
          </div>}
        {source === 'local' && <label className="grid items-center gap-2 text-xs font-medium sm:grid-cols-[auto_1fr]">Organization owner
          <input maxLength={200} className={inputClass} value={owner} placeholder="Organization security team" onChange={event => setOwner(event.target.value)} />
        </label>}
        {supportingComponents}
        {source === 'provider' ? <div className="flex gap-2 rounded-md border border-blue-100 bg-blue-50 p-3 text-blue-900 dark:border-blue-900 dark:bg-blue-950 dark:text-blue-100">
          <LockKeyhole size={16} className="shrink-0" aria-hidden="true" /><div><p className="text-xs font-semibold">Provider-managed · Read-only</p>
            <p className="mt-1 text-[11px]">Only published CSP offerings are eligible. Keep the source reference and included contributors. Organization contribution is stored separately.</p></div>
        </div> : <OrganizationInfo>Organization-wide library: create or reuse capabilities and components. Nothing is assigned to a system here.</OrganizationInfo>}
        <div className="flex justify-end gap-2 border-t border-slate-100 pt-3 dark:border-gray-700">
          <button type="button" className={secondaryButtonClass} onClick={close}>Cancel</button>
          <button type="submit" className={`${buttonClass} inline-flex items-center gap-2`} disabled={!ready || editingComponent}>Continue <ArrowRight size={14} aria-hidden="true" /></button>
        </div>
      </form>}
      {step === 2 && <form className="space-y-4" onSubmit={event => { event.preventDefault(); if (contribution.trim() && owner.trim() && !editingComponent) setStep(3); }}>
        <div className="grid gap-4 sm:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
        <div className="min-w-0 space-y-3">
        <h3 className="text-sm font-semibold">Define the organization contribution</h3>
        <p className="text-xs text-slate-500">{name}</p>
        <label className="grid gap-1 text-xs font-medium">Organization contribution
          <textarea required rows={4} maxLength={2000} className={inputClass} value={contribution}
            placeholder="Describe what your organization provides, operates, or maintains."
            onChange={event => setContribution(event.target.value)} />
        </label>
        <label className="grid gap-1 text-xs font-medium">Organization owner
          <input required maxLength={200} className={inputClass} value={owner} onChange={event => setOwner(event.target.value)} />
        </label>
        {supportingComponents}
        <OrganizationInfo>Describe what the organization provides, operates, or maintains. System-specific responsibilities are completed inside each system.</OrganizationInfo>
        </div>{reviewSummary}</div>
        <div className="flex flex-wrap justify-end gap-2 border-t border-slate-100 pt-3 dark:border-gray-700">
          <button type="button" className={secondaryButtonClass} disabled={editingComponent} onClick={() => setStep(1)}>Back</button>
          <button type="button" className={secondaryButtonClass} onClick={close}>Cancel</button>
          <button type="submit" className={buttonClass} disabled={!contribution.trim() || !owner.trim() || editingComponent}>Review changes</button>
        </div>
      </form>}
      {step === 3 && <section className="space-y-4">
        <h3 className="text-sm font-semibold">Review organization changes</h3>
        <div className="grid gap-4 sm:grid-cols-[minmax(0,1.5fr)_minmax(0,1fr)]">
        <dl className="min-w-0 space-y-3 break-words rounded-md border border-slate-200 p-4 text-xs leading-5 dark:border-gray-700">
          <div><dt className="text-slate-500">Organization offering</dt><dd className="font-medium">{name}</dd></div>
          <div><dt className="text-slate-500">Action</dt><dd>{source === 'provider' ? 'Adopt CSP offering for organization-wide use' : existing ? 'Reuse organization record' : `Create organization ${recordType}`}</dd></div>
          <div><dt className="text-slate-500">Organization contribution</dt><dd className="whitespace-pre-wrap">{contribution}</dd></div>
          <div><dt className="text-slate-500">Organization owner</dt><dd>{owner}</dd></div>
          {recordType === 'capability' && <div><dt className="text-slate-500">Additional components</dt><dd>{[...components, ...newComponents].map(item => item.name).join(', ') || 'None'}</dd></div>}
        </dl>{reviewSummary}</div>
        <OrganizationInfo>This saves reusable organization records and your contribution. No systems, subscriptions, control confirmations, or narratives are changed.</OrganizationInfo>
        {attempt.current && error && !canEditFailure && <p className="text-sm text-slate-500">Retry uses the same save request to avoid duplicate records.</p>}
        <div className="flex flex-wrap justify-end gap-2 border-t border-slate-100 pt-3 dark:border-gray-700">
          <button type="button" className={secondaryButtonClass} disabled={busy || (!!attempt.current && !canEditFailure)} onClick={() => {
            attempt.current = null; setError(null); setStep(2);
          }}>Back</button>
          <button type="button" className={secondaryButtonClass} disabled={busy} onClick={close}>Cancel</button>
          <button type="button" className={buttonClass} disabled={busy} onClick={() => void save()}>{busy ? 'Saving…' : error ? 'Retry save' : 'Save to organization'}</button>
        </div>
      </section>}
      {step === 4 && result && <section className="space-y-4">
        <h3 className="text-lg font-semibold">Added to organization</h3>
        <p role="status"><strong>{result.name}</strong> is saved in the organization library. It has not been assigned to any system.</p>
        <div className="flex flex-wrap justify-between gap-3 border-t border-slate-200 pt-4">
          <button type="button" className={secondaryButtonClass} onClick={close}>Done</button>
          <Link className={buttonClass} to={`/security-capabilities/${result.source}/${encodeURIComponent(result.recordId)}?recordType=${result.recordType}`}>View {result.recordType}</Link>
        </div>
      </section>}
    </>}
    {!access.data?.canManageCatalog && <button type="button" className={`${secondaryButtonClass} mt-4`} onClick={close}>Cancel</button>}
  </SetupDialog>;
}
