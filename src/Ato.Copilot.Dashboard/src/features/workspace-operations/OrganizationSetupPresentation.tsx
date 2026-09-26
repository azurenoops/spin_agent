import { useState, useId, type ReactNode } from 'react';
import { Check, Info, UserRound, ShieldCheck } from 'lucide-react';
import type { InitialAdministrator, OrganizationCreationRequest, ProvisioningResult } from './types';
import { inputClass, secondaryButtonClass } from './workspaceUi';
import { EntraUserPicker } from './EntraUserPicker';

export const setupCard = 'min-w-0 rounded-lg border border-slate-200 bg-white p-5 shadow-sm dark:border-gray-700 dark:bg-gray-900';
export const emptyOrganization = { displayName: '', legalEntityName: '', primaryPocName: '', primaryPocEmail: '' };
export const organizationFieldLimits = { displayName: 200, legalEntityName: 300, primaryPocName: 200, primaryPocEmail: 254 };
export const emptyAdministrator = { directoryTenantId: '', objectId: '', personId: '', displayName: '', email: '' };
export type AdministratorFields = typeof emptyAdministrator;
export type FieldErrors = Record<string, string>;
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const email = /^[^\s@]+@[^\s@]+$/;

export function validateOrganization(fields: typeof emptyOrganization): FieldErrors {
  const errors: FieldErrors = {};
  if (!fields.displayName.trim()) errors.displayName = 'Enter an organization name.';
  for (const field of ['displayName', 'legalEntityName', 'primaryPocName', 'primaryPocEmail'] as const) {
    if (fields[field].trim().length > organizationFieldLimits[field]) errors[field] = `Use ${organizationFieldLimits[field]} characters or fewer.`;
  }
  if (fields.primaryPocEmail.trim() && !email.test(fields.primaryPocEmail.trim())) errors.primaryPocEmail = 'Enter a valid contact email.';
  return errors;
}

export function validateAdministrator(fields: AdministratorFields, newPerson: boolean): FieldErrors {
  const errors: FieldErrors = {};
  if (!guid.test(fields.directoryTenantId.trim())) errors.directoryTenantId = 'Enter a valid directory tenant ID.';
  if (!guid.test(fields.objectId.trim())) errors.objectId = 'Enter a valid user object ID.';
  if (newPerson) {
    if (!fields.displayName.trim() || fields.displayName.trim().length > 256) errors.displayName = 'Enter an administrator name of 1 to 256 characters.';
    if (!email.test(fields.email.trim()) || fields.email.trim().length > 320) errors.email = 'Enter a valid administrator email of up to 320 characters.';
  } else if (!guid.test(fields.personId.trim())) errors.personId = 'Enter a valid Person record ID.';
  return errors;
}

export function administratorIntent(fields: AdministratorFields, newPerson: boolean): InitialAdministrator {
  return { directoryTenantId: fields.directoryTenantId.trim(), objectId: fields.objectId.trim(),
    ...(newPerson ? { newPerson: { displayName: fields.displayName.trim(), email: fields.email.trim() } } : { personId: fields.personId.trim() }) };
}

export function enrollmentComplete(result: ProvisioningResult) {
  return result.tenantState === 'Completed' && result.administratorState === 'Completed'
    && result.membershipState === 'Completed'
    && (result.personState === 'Completed' || result.personState === 'NotRequested'
      || (!result.personState && !result.initialAdministrator?.newPerson));
}

export function organizationSetupLabel(state?: string) {
  return ({ Completed: 'Setup complete', Pending: 'Enrollment pending',
    Failed: 'Enrollment needs attention', NotStarted: 'Enrollment not started' } as Record<string, string>)[state ?? '']
    ?? 'Setup status unavailable';
}

export function SetupSteps({ step }: { step: number }) {
  return <nav aria-label="Organization setup progress"><ol className="flex flex-wrap gap-3 text-xs">
    {['Organization', 'Administrator', 'Review', 'Setup status'].map((name, index) =>
      <li key={name} aria-current={step === index + 1 ? 'step' : undefined} className="flex min-w-16 flex-col items-center gap-1.5">
        <span className={`grid h-7 w-7 place-items-center rounded-full border ${step === index + 1 ? 'bg-white font-bold text-indigo-700' : 'border-current'}`}>
          {index + 1 < step ? <Check size={14} aria-label="Visited" /> : index + 1}
        </span>{name}
      </li>)}
  </ol></nav>;
}

export function SetupInfo({ children }: { children: ReactNode }) {
  return <section className="flex items-start gap-3 rounded-lg border border-indigo-100 bg-indigo-50 p-4 text-sm leading-relaxed text-indigo-950 dark:border-indigo-900 dark:bg-indigo-950 dark:text-indigo-100">
    <Info size={18} className="mt-0.5 shrink-0" aria-hidden="true" /><div>{children}</div>
  </section>;
}

export function SetupField({ label, value, onChange, error, type = 'text', maxLength }: {
  label: string; value: string; onChange: (value: string) => void; error?: string; type?: string; maxLength?: number;
}) {
  const id = useId();
  return <div className="grid gap-1.5 text-sm"><label htmlFor={id} className="font-medium">{label}</label>
    <input id={id} className={`${inputClass} w-full min-w-0`} value={value} type={type} maxLength={maxLength}
      aria-invalid={!!error} aria-describedby={error ? `${id}-error` : undefined} onChange={event => onChange(event.target.value)} />
    {error && <p id={`${id}-error`} className="text-xs text-red-700 dark:text-red-300">{error}</p>}
  </div>;
}

export function AdministratorInputs({ fields, newPerson, onNewPerson, onChange, errors }: {
  fields: AdministratorFields; newPerson: boolean; onNewPerson: (value: boolean) => void;
  onChange: (key: keyof AdministratorFields, value: string) => void; errors: FieldErrors;
}) {
  const group = useId();
  const [mode, setMode] = useState<'directory' | 'manual'>(() => fields.objectId ? 'manual' : 'directory');
  const [selected, setSelected] = useState(false);
  return <div className="space-y-5">
    <div className="flex flex-wrap gap-2" role="group" aria-label="Administrator identity source">
      <button type="button" aria-pressed={mode === 'directory'} className={`${secondaryButtonClass} ${mode === 'directory' ? 'bg-indigo-50 text-indigo-700 ring-1 ring-indigo-300 dark:bg-indigo-950' : ''}`} onClick={() => setMode('directory')}>Find in Entra</button>
      <button type="button" aria-pressed={mode === 'manual'} className={secondaryButtonClass} onClick={() => { setMode('manual'); setSelected(false); }}>Enter manually</button>
    </div>
    {mode === 'directory' && !selected && Object.keys(errors).length > 0 && <p role="alert" className="text-sm text-red-700 dark:text-red-300">Select a person from Entra, enter their details manually, or complete enrollment later.</p>}
    {mode === 'directory' && !selected && <EntraUserPicker onSelect={user => {
      onChange('directoryTenantId', user.directoryTenantId); onChange('objectId', user.objectId);
      onChange('displayName', user.displayName); onChange('email', user.email);
      onNewPerson(true); setSelected(true);
    }} />}
    {mode === 'directory' && selected && <div className="space-y-4 rounded-xl border border-indigo-200 bg-indigo-50/50 p-5 dark:border-indigo-800 dark:bg-indigo-950/40">
      <div className="flex items-start gap-3"><span className="rounded-full bg-indigo-100 p-3 text-indigo-700"><UserRound size={24} /></span><div className="min-w-0 flex-1"><p className="text-xs font-medium uppercase tracking-wide text-indigo-700 dark:text-indigo-300">Selected administrator</p><h3 className="mt-1 break-words text-lg font-semibold">{fields.displayName}</h3><p className="break-all text-sm text-slate-600 dark:text-slate-300">{fields.email}</p></div></div>
      <p className="flex gap-2 text-sm"><ShieldCheck size={18} className="shrink-0 text-indigo-600" />Organization Administrator · assigned after confirmation</p>
      <button type="button" className={secondaryButtonClass} onClick={() => { setSelected(false); for (const key of ['directoryTenantId', 'objectId', 'displayName', 'email'] as const) onChange(key, ''); }}>Choose another person</button>
    </div>}
    {(mode === 'manual' || selected) && <div className="space-y-4 border-t border-slate-100 pt-4 dark:border-slate-800">
    {mode === 'manual' && <p className="text-xs text-slate-500">Manual identity details are not checked against Entra.</p>}
    <fieldset className="space-y-2 text-sm"><legend className="mb-2 font-semibold">Organization-local Person</legend>
      <label className="flex gap-2"><input type="radio" name={group} checked={newPerson} onChange={() => onNewPerson(true)} />Create a Person record for this administrator</label>
      <label className="flex gap-2"><input type="radio" name={group} checked={!newPerson} onChange={() => onNewPerson(false)} />Use an existing Person record</label>
    </fieldset>
    <details open={mode === 'manual' || Object.keys(errors).length > 0} className="space-y-3"><summary className="cursor-pointer text-sm font-medium">Identity details</summary>
    <SetupField label="Directory tenant ID" value={fields.directoryTenantId} onChange={value => onChange('directoryTenantId', value)} error={errors.directoryTenantId} />
    <SetupField label="User object ID" value={fields.objectId} onChange={value => onChange('objectId', value)} error={errors.objectId} />
    {newPerson ? <>
      <SetupField label="Administrator name" value={fields.displayName} onChange={value => onChange('displayName', value)} error={errors.displayName} maxLength={256} />
      <SetupField label="Administrator email" value={fields.email} onChange={value => onChange('email', value)} error={errors.email} type="email" maxLength={320} />
      <p className="text-xs text-slate-500">A new local Person record will be saved after the organization is created. Its record ID will be assigned by the server.</p>
    </> : <>
      <SetupField label="Person record ID" value={fields.personId} onChange={value => onChange('personId', value)} error={errors.personId} />
      <p className="text-xs text-slate-500">The Person must belong to this organization. For a new organization, create a Person record instead.</p>
    </>}
    </details>
    <p className="text-xs text-slate-500">Confirm the identity details before continuing. No invitation is sent.</p>
    </div>}
  </div>;
}

export function SetupSummary({ request }: { request: OrganizationCreationRequest }) {
  const administrator = request.initialAdministrator;
  return <div className="grid gap-4 md:grid-cols-2">
    <section className={setupCard}><h3 className="mb-4 font-semibold">Organization details</h3>
      <dl className="space-y-3 text-sm">{[
        ['Organization name', request.displayName], ['Legal entity name', request.legalEntityName],
        ['Primary contact name', request.primaryPocName], ['Primary contact email', request.primaryPocEmail],
      ].map(([label, value]) => <div key={label}><dt className="text-xs text-slate-500">{label}</dt><dd className="break-words">{value || 'Not provided'}</dd></div>)}</dl>
    </section>
    <section className={setupCard}><h3 className="mb-4 font-semibold">Initial administrator</h3>
      {!administrator ? <p className="text-sm">Enrollment deferred</p> : <dl className="space-y-3 text-sm">
        {[
          ['Directory tenant ID', administrator.directoryTenantId], ['User object ID', administrator.objectId],
          ['Person record ID', administrator.personId ?? 'Assigned after Person creation'],
          ...(administrator.newPerson ? [['Administrator name', administrator.newPerson.displayName], ['Administrator email', administrator.newPerson.email]] : []),
        ].map(([label, value]) => <div key={label}><dt className="text-xs text-slate-500">{label}</dt><dd className="break-all">{value}</dd></div>)}
        <div><dt className="text-xs text-slate-500">Identity verification</dt><dd>Not verified by this flow</dd></div>
      </dl>}
    </section>
  </div>;
}
