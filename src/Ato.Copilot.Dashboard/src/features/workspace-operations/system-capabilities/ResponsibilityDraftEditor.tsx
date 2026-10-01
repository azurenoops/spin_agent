import { useEffect, useRef, useState } from 'react';
import {
  isResponsibilityType,
} from '../../../api/capabilityResponsibilities';
import {
  responsibilityFields, type ResponsibilityField, type ResponsibilityValue, type ResponsibilityValues,
} from '../../../api/responsibilityDrafts';
import ResponsibilityFirstPass from '../../../components/ResponsibilityFirstPass';
import { useResponsibilityFirstPass } from '../../../components/useResponsibilityFirstPass';
import { buttonClass, inputClass, secondaryButtonClass, warningClass } from '../workspaceUi';
import type { SystemCapabilitySource } from './systemCapabilityTypes';

interface CachedEdit {
  values: ResponsibilityValues;
  edited: Set<ResponsibilityField>;
  scopeId: string | null;
}
export type ResponsibilityDraftEdits = Map<string, CachedEdit>;
interface Props {
  tenantId: string; systemId: string; source: SystemCapabilitySource; capabilityId: string; controlId: string;
  edits: ResponsibilityDraftEdits; onChanged: () => void;
}
const emptyValues = (): ResponsibilityValues => ({
  allocation: 'NeedsConfirmation', provider: '', providerDuties: '', customer: '',
  scope: '', exclusions: '', source: '', basis: '', information: '',
});

export default function ResponsibilityDraftEditor(props: Props) {
  return <Editor key={`${props.tenantId}:${props.systemId}:${props.source}:${props.capabilityId}:${props.controlId}`} {...props} />;
}

function Editor({ tenantId, systemId, source, capabilityId, controlId, edits, onChanged }: Props) {
  const key = `${tenantId}:${systemId}:${source}:${capabilityId}:${controlId}`;
  const initial = useRef(edits.get(key));
  const [values, setValues] = useState<ResponsibilityValues>(() => initial.current?.values ?? emptyValues());
  const current = useRef(values);
  current.current = values;
  const edited = useRef(initial.current?.edited ?? new Set<ResponsibilityField>());
  const scope = useRef(initial.current?.scopeId ?? null);
  const [origins, setOrigins] = useState<Partial<Record<ResponsibilityField, ResponsibilityValue>>>({});
  const [notice, setNotice] = useState('');
  const [reviewing, setReviewing] = useState(false);
  const [coverage, setCoverage] = useState(false);
  const [duties, setDuties] = useState(false);
  const [notes, setNotes] = useState('');
  const cache = (next: ResponsibilityValues) => {
    current.current = next;
    edits.set(key, { values: next, edited: edited.current, scopeId: scope.current });
    setValues(next);
  };
  const prepared = useResponsibilityFirstPass(systemId, controlId, true, values, (suggested, force) => {
    const next = { ...current.current };
    const nextOrigins: Partial<Record<ResponsibilityField, ResponsibilityValue>> = {};
    for (const field of responsibilityFields) {
      nextOrigins[field] = suggested[field];
      if (!force && edited.current.has(field)) continue;
      next[field] = suggested[field].value;
      if (suggested[field].userEdited) edited.current.add(field);
      else edited.current.delete(field);
    }
    cache(next); setOrigins(nextOrigins); setCoverage(false); setDuties(false); setReviewing(false);
  });
  useEffect(() => {
    if (initial.current?.scopeId) prepared.setScopeId(initial.current.scopeId);
    // Restore only this identity-keyed editor's recorded selection.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  const setScopeId: typeof prepared.setScopeId = value => {
    const next = typeof value === 'function' ? value(scope.current) : value;
    scope.current = next; cache(current.current); prepared.setScopeId(next);
    setCoverage(false); setDuties(false); setReviewing(false); setNotice('');
  };
  const state = { ...prepared, setScopeId };
  const change = (field: ResponsibilityField, value: string) => {
    edited.current.add(field); cache({ ...current.current, [field]: value });
    setCoverage(false); setDuties(false); setReviewing(false); setNotice('');
  };
  const field = (name: ResponsibilityField, label: string, limit = 1200) => <label className="grid gap-1 text-sm">
    {label}
    {origins[name] && <span className="text-xs text-indigo-700 dark:text-indigo-300">
      {origins[name]!.origin}{edited.current.has(name) ? ' · your correction' : ''}
    </span>}
    <textarea aria-label={label} className={`${inputClass} w-full min-w-0`} maxLength={limit}
      value={values[name]} onChange={event => change(name, event.target.value)} />
  </label>;
  const providerAllocation = values.allocation === 'Shared' || values.allocation === 'Inherited';
  const valid = isResponsibilityType(values.allocation) && !!values.basis.trim()
    && (!providerAllocation || !!values.provider.trim() && !!values.providerDuties.trim())
    && (values.allocation === 'Inherited' ? !!values.scope.trim() && !!values.exclusions.trim() && !!values.source.trim()
      : !!values.customer.trim());
  const canSave = prepared.context?.canPrepare === true && !prepared.busy && !prepared.loading;
  async function save() {
    setNotice('');
    if (await prepared.save()) { setNotice('Draft saved. Responsibility acceptance remains separate.'); onChanged(); }
  }
  async function confirm() {
    if (!prepared.canConfirm || !valid || !reviewing || !coverage || !duties || !notes.trim()) return;
    setNotice('');
    if (await prepared.confirm(notes.trim())) {
      setNotice('Responsibility accepted through the existing approval lifecycle. Narrative approval and authorization remain separate.');
      setReviewing(false); onChanged();
    }
  }
  return <section aria-label={`Prepared draft for ${controlId}`} className="min-w-0 space-y-3 text-sm">
    <h3 className="font-semibold">Prepared responsibility · {controlId}</h3>
    <p>Saving a draft does not accept inheritance, satisfy a control, approve narratives, submit eMASS or authorize the system.</p>
    {notice && <p role="status">{notice}</p>}
    <ResponsibilityFirstPass state={state} />
    <fieldset disabled={!canSave} className="min-w-0 space-y-3">
      <legend className="font-semibold">Correct the prepared responsibility</legend>
      <label className="grid gap-1">Responsibility split draft
        <select className={inputClass} value={values.allocation} onChange={event => change('allocation', event.target.value)}>
          <option value="NeedsConfirmation">Responsibility not confirmed</option>
          <option value="Shared">Provider and my team</option><option value="Inherited">Provider covers the control</option>
          <option value="Customer">My team implements the control</option>
        </select>
      </label>
      {field('provider', 'Provider / source')}
      {field('providerDuties', 'Provider responsibility draft')}
      {field('customer', 'Customer responsibility draft', 2000)}
      {field('basis', 'Basis for this allocation')}
      <details><summary className="cursor-pointer font-medium">Scope, exclusions, supporting source and missing information</summary>
        <div className="mt-3 space-y-3">{field('scope', 'Applicable scope')}{field('exclusions', 'Exclusions')}
          {field('source', 'Supporting source')}{field('information', 'Missing information')}</div>
      </details>
    </fieldset>
    <div className="flex flex-wrap gap-2">
      <button type="button" className={buttonClass} disabled={!canSave} onClick={() => { void save(); }}>Save draft</button>
      <button type="button" className={secondaryButtonClass} disabled={!prepared.canConfirm || !valid}
        onClick={() => { setReviewing(true); setCoverage(false); setDuties(false); }}>Review saved responsibility</button>
    </div>
    {reviewing && <section aria-label="Confirm reviewed responsibility" className="space-y-3 rounded border p-3">
      <p>Check the saved fields and exact source versions. This uses the upstream responsibility approval lifecycle.</p>
      <label className="flex gap-2"><input type="checkbox" checked={coverage} onChange={event => setCoverage(event.target.checked)} />Provider coverage or system applicability verified</label>
      <label className="flex gap-2"><input type="checkbox" checked={duties} onChange={event => setDuties(event.target.checked)} />Customer duties reviewed</label>
      <label className="grid gap-1">Review notes<textarea aria-label="Review notes" className={inputClass} maxLength={2000}
        value={notes} onChange={event => setNotes(event.target.value)} /></label>
      <button type="button" className={buttonClass} disabled={!prepared.canConfirm || !coverage || !duties || !notes.trim()}
        onClick={() => { void confirm(); }}>Confirm {controlId} responsibility</button>
    </section>}
    {prepared.context && !prepared.context.canPrepare && <p className={warningClass}>Read-only: an assigned ISSM or ISSO is required to save or confirm this system&apos;s responsibility.</p>}
  </section>;
}
