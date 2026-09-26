import { useCallback, useEffect, useState } from 'react';
import { inputClass, Pager, secondaryButtonClass, Status, surfaceClass, useRemote } from '../workspace-operations/workspaceUi';
import { PackageImportError } from '../package-imports/request';
import { MutationForm } from './forms';
import { getImpactOption, listImpactOptions, type ImpactOption, type ImpactOptionKind } from './changeImpactApi';
import type { ImpactInput, Offering } from './types';

export interface ImpactSource {
  kind: 'Package' | 'Capability' | 'Boundary' | 'HostingScope';
  id: string;
  boundaryRevisionId?: string;
}
type ChangeKind = 'Component' | 'Capability' | 'Boundary' | 'HostingScope';
type ChangeChoice = { kind: ChangeKind; option: ImpactOption };
type Context = Record<'Boundary' | 'HostingScope' | 'Authorization' | 'Package', ImpactOption[]>;
const isChangeKind = (kind: string): kind is ChangeKind =>
  kind === 'Component' || kind === 'Capability' || kind === 'Boundary' || kind === 'HostingScope';
const optionLabel = (item: ImpactOption) => `${item.name} · ${item.version}`;

function NamedOptions({ offeringId, kind, label, value, onChange, multiple = true, changesOnly = false, onAvailability }: {
  offeringId: string; kind: ImpactOptionKind; label: string; value: ImpactOption[];
  onChange: (value: ImpactOption[]) => void; multiple?: boolean; changesOnly?: boolean;
  onAvailability: (label: string, unavailable: boolean) => void;
}) {
  const [page, setPage] = useState(1);
  const remote = useRemote(signal => listImpactOptions(offeringId, kind, page, signal), [offeringId, kind, page]);
  useEffect(() => { onAvailability(label, remote.loading || !!remote.error); },
    [label, remote.loading, remote.error, onAvailability]);
  useEffect(() => () => onAvailability(label, false), [label, onAvailability]);
  return <fieldset aria-label={label} className="min-w-0 space-y-3 rounded-lg border border-slate-200 p-4 dark:border-gray-700">
    <legend className="px-1 font-semibold">{label}</legend>
    <Status loading={remote.loading} error={remote.error ? `${label} unavailable. ${remote.error}` : null} retry={remote.retry} />
    {value.length > 0 && <ul aria-label={`Selected ${label.toLowerCase()}`} className="space-y-2 text-sm">
      {value.map(item => <li key={item.id} className="flex flex-wrap items-start justify-between gap-2">
        <span className="min-w-0 break-words">{optionLabel(item)}</span>
        <button type="button" className="text-indigo-700 underline dark:text-indigo-300"
          onClick={() => onChange(value.filter(selected => selected.id !== item.id))}>Remove {item.name}</button>
      </li>)}
    </ul>}
    {remote.data && <>
      {!remote.data.items.length && <p className="text-sm text-slate-600 dark:text-slate-300">No {label.toLowerCase()} available. Return to the source task to record the missing information.</p>}
      <div className="space-y-3">{remote.data.items.map(item => <label key={item.id} className="flex items-start gap-3 text-sm">
        <input type={multiple ? 'checkbox' : 'radio'} name={label} className="mt-1"
          checked={value.some(selected => selected.id === item.id)} disabled={changesOnly && !item.change}
          onChange={event => onChange(event.target.checked
            ? multiple ? [...value.filter(selected => selected.id !== item.id), item] : [item]
            : value.filter(selected => selected.id !== item.id))} />
        <span className="min-w-0 break-words"><span className="font-medium">{optionLabel(item)}</span>
          <span className="mt-1 block text-slate-600 dark:text-slate-300">{item.summary}</span>
          {changesOnly && !item.change && <span className="block">No reviewable saved change is available.</span>}
        </span>
      </label>)}</div>
      {remote.data.total > remote.data.pageSize && <Pager {...remote.data} onPage={setPage} />}
    </>}
  </fieldset>;
}

export function ImpactContextEditor({ offering, initialContext, source, disabled, onChanged, onPendingChange, onPreview, onReload }: {
  offering: Offering; initialContext?: ImpactInput | null; source?: ImpactSource; disabled: boolean;
  onChanged: () => void; onPendingChange: (pending: boolean) => void;
  onPreview: (input: ImpactInput, key: string) => Promise<void>;
  onReload: (input: ImpactInput) => void;
}) {
  const [context, setContext] = useState<Context>({ Boundary: [], HostingScope: [], Authorization: [], Package: [] });
  const [changes, setChanges] = useState<ChangeChoice[]>([]);
  const [kind, setKind] = useState<ChangeKind>(source && isChangeKind(source.kind) ? source.kind : 'Capability');
  const [confirmed, setConfirmed] = useState(false);
  const [unavailable, setUnavailable] = useState<Record<string, boolean>>({});
  const availability = useCallback((label: string, value: boolean) => {
    setUnavailable(previous => previous[label] === value ? previous : { ...previous, [label]: value });
  }, []);
  const initial = useRemote(async signal => {
    const requests: { kind: ImpactOptionKind; id: string; change: boolean }[] = [];
    const boundaryId = initialContext?.boundaryRevisionId ?? source?.boundaryRevisionId
      ?? (source?.kind === 'Boundary' ? source.id : offering.currentBoundaryRevisionId);
    if (boundaryId) requests.push({ kind: 'Boundary', id: boundaryId, change: false });
    const hostingId = initialContext?.hostingScopeRevisionId ?? (source?.kind === 'HostingScope' ? source.id : undefined);
    if (hostingId) requests.push({ kind: 'HostingScope', id: hostingId, change: false });
    for (const id of initialContext?.authorizationRevisionIds ?? []) requests.push({ kind: 'Authorization', id, change: false });
    for (const id of initialContext?.packageVersionIds ?? []) requests.push({ kind: 'Package', id, change: false });
    for (const change of initialContext?.changes ?? []) {
      if (!isChangeKind(change.kind)) throw new PackageImportError('This historical change type cannot be restored. Start a new review from the source task.', 422);
      requests.push({ kind: change.kind, id: change.recordId, change: true });
    }
    if (source && !requests.some(item => item.kind === source.kind && item.id === source.id && item.change === isChangeKind(source.kind)))
      requests.push({ kind: source.kind, id: source.id, change: isChangeKind(source.kind) });
    return Promise.all(requests.map(async request => ({
      ...request, option: await getImpactOption(offering.offeringId, request.kind, request.id, signal),
    })));
  }, [offering.offeringId, initialContext, source]);
  useEffect(() => {
    if (!initial.data) return;
    const next: Context = { Boundary: [], HostingScope: [], Authorization: [], Package: [] };
    const nextChanges: ChangeChoice[] = [];
    for (const item of initial.data) {
      if (item.change && isChangeKind(item.kind)) nextChanges.push({ kind: item.kind, option: item.option });
      if (!item.change) {
        if (item.kind === 'Boundary' || item.kind === 'HostingScope' || item.kind === 'Authorization' || item.kind === 'Package')
          next[item.kind].push(item.option);
      }
    }
    setContext(next); setChanges(nextChanges); setConfirmed(false);
  }, [initial.data]);
  const edit = () => { setConfirmed(false); onChanged(); };
  const contextChange = (key: keyof Context, value: ImpactOption[]) => { edit(); setContext(previous => ({ ...previous, [key]: value })); };
  const selectedBoundary = context.Boundary[0];
  const selectedChanges = changes.map(item => item.option.change).filter(item => item !== null);
  const currentInput: ImpactInput | null = selectedBoundary && selectedChanges.length === changes.length ? {
    expectedOfferingRevision: offering.revision, boundaryRevisionId: selectedBoundary.id,
    ...(context.HostingScope[0] ? { hostingScopeRevisionId: context.HostingScope[0].id } : {}),
    authorizationRevisionIds: context.Authorization.map(item => item.id),
    packageVersionIds: context.Package.map(item => item.id), changes: selectedChanges,
  } : null;
  const complete = !initial.loading && !initial.error && !Object.values(unavailable).some(Boolean)
    && context.Boundary.length === 1 && context.HostingScope.length <= 1
    && context.Authorization.length <= 100 && context.Package.length <= 100 && changes.length > 0 && changes.length <= 100
    && changes.every(item => item.option.change !== null) && confirmed;
  return <section aria-label="Select proposed change" className={`${surfaceClass} min-w-0 space-y-4 p-5`}>
    <h3 className="text-lg font-semibold">Select the change and its supporting versions</h3>
    <p className="text-sm text-slate-600 dark:text-slate-300">Choose named, saved records. SPIN carries their exact versions into the review; you do not need to copy identifiers or hashes.</p>
    <Status loading={initial.loading} error={initial.error ? `Selected source unavailable. ${initial.error}` : null} retry={initial.retry} />
    {initialContext && <p className="text-sm">The previous selection is shown for review. Saved capability/component versions may have changed; confirm the displayed versions before reassessing.</p>}
    {source?.kind === 'Package' && <p className="text-sm">The revised package is selected as source context. Select the saved capability, component or boundary changes it supports. Raw package claims do not by themselves establish affected coverage.</p>}
    <MutationForm label="Assess impact" submitDisabled={!complete} disabled={disabled || initial.loading || !!initial.error}
      onPendingChange={onPendingChange} onSaved={() => undefined} submit={async key => {
        if (!complete || !currentInput) throw new PackageImportError('Review the named versions and resolve unavailable selections before assessing impact.', 422);
        await onPreview(currentInput, key);
      }}>
      <label className="grid gap-1 text-sm">Change type<select className={inputClass} value={kind} onChange={event => {
        if (isChangeKind(event.target.value)) setKind(event.target.value);
      }}>
        <option value="Capability">Security capability</option><option value="Component">Component</option>
        <option value="Boundary">Authorization boundary</option><option value="HostingScope">Hosting scope</option>
      </select></label>
      <NamedOptions key={kind} offeringId={offering.offeringId} kind={kind} label="Proposed changes" changesOnly
        value={changes.filter(item => item.kind === kind).map(item => item.option)} onAvailability={availability}
        onChange={value => { edit(); setChanges(previous => [...previous.filter(item => item.kind !== kind), ...value.map(option => ({ kind, option }))]); }} />
      {changes.some(item => item.kind !== kind) && <ul className="text-sm">{changes.filter(item => item.kind !== kind).map(item =>
        <li key={`${item.kind}:${item.option.id}`}>Also selected: {optionLabel(item.option)} ({item.kind})</li>)}</ul>}
      <NamedOptions offeringId={offering.offeringId} kind="Boundary" label="Boundary versions" multiple={false}
        value={context.Boundary} onChange={value => contextChange('Boundary', value)} onAvailability={availability} />
      <NamedOptions offeringId={offering.offeringId} kind="Authorization" label="Authorization records"
        value={context.Authorization} onChange={value => contextChange('Authorization', value)} onAvailability={availability} />
      <p className="text-sm text-slate-600 dark:text-slate-300">If no authorization is recorded, coverage remains unestablished. The impact service checks the selected context and reports publication blockers.</p>
      <NamedOptions offeringId={offering.offeringId} kind="Package" label="Source package versions"
        value={context.Package} onChange={value => contextChange('Package', value)} onAvailability={availability} />
      <details><summary className="cursor-pointer text-sm font-medium">Hosting context (optional)</summary>
        <NamedOptions offeringId={offering.offeringId} kind="HostingScope" label="Hosting scope versions" multiple={false}
          value={context.HostingScope} onChange={value => contextChange('HostingScope', value)} onAvailability={availability} />
      </details>
      <button type="button" className={secondaryButtonClass} disabled={!currentInput}
        onClick={() => { if (currentInput) onReload(currentInput); }}>Refresh selected versions</button>
      <label className="flex items-start gap-2 text-sm"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />
        I reviewed the selected changes and supporting versions.</label>
    </MutationForm>
  </section>;
}
