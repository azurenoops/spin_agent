import { useId, type ReactNode } from 'react';
import { Check, Info } from 'lucide-react';
import type { InitialAdministrator, OrganizationCreationRequest, ProvisioningResult } from './types';
import { inputClass } from './workspaceUi';

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
  return <div className="space-y-4">
    <fieldset className="space-y-2 text-sm"><legend className="mb-2 font-semibold">Organization-local Person</legend>
      <label className="flex gap-2"><input type="radio" name={group} checked={newPerson} onChange={() => onNewPerson(true)} />Create a Person record for this administrator</label>
      <label className="flex gap-2"><input type="radio" name={group} checked={!newPerson} onChange={() => onNewPerson(false)} />Use an existing Person record</label>
    </fieldset>
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
    <p className="text-xs text-slate-500">Directory identifiers are entered manually and are not verified here. No invitation is sent.</p>
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
