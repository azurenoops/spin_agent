import { useEffect, useRef, useState } from 'react';
import PageHero from '../../components/layout/PageHero';
import PageLayout from '../../components/layout/PageLayout';
import { Link, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import * as api from './api';
import type { OrganizationCreationRequest } from './types';
import { buttonClass, errorClass, message, secondaryButtonClass, warningClass } from './workspaceUi';
import {
  AdministratorInputs, administratorIntent, emptyAdministrator, emptyOrganization, SetupField,
  SetupInfo, setupCard, SetupSteps, SetupSummary, validateAdministrator, validateOrganization,
  organizationFieldLimits, type FieldErrors,
} from './OrganizationSetupPresentation';

export default function AddOrganizationPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const queryKey = new URLSearchParams(location.search).get('key');
  const key = useRef(queryKey ?? crypto.randomUUID());
  const [step, setStep] = useState(1);
  const [organization, setOrganization] = useState(emptyOrganization);
  const [administrator, setAdministrator] = useState(emptyAdministrator);
  const [enroll, setEnroll] = useState(true);
  const [newPerson, setNewPerson] = useState(true);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [recovery, setRecovery] = useState(!!queryKey);
  const [missing, setMissing] = useState(false);
  const [confirmed, setConfirmed] = useState<OrganizationCreationRequest | null>(null);
  const confirmedRef = useRef<OrganizationCreationRequest | null>(null);
  const pending = useRef(false);
  const alive = useRef(true);
  const generation = useRef(0);
  const heading = useRef<HTMLHeadingElement>(null);
  const form = useRef<HTMLFormElement>(null);
  const request: OrganizationCreationRequest = {
    displayName: organization.displayName.trim(),
    ...(organization.legalEntityName.trim() ? { legalEntityName: organization.legalEntityName.trim() } : {}),
    ...(organization.primaryPocName.trim() ? { primaryPocName: organization.primaryPocName.trim() } : {}),
    ...(organization.primaryPocEmail.trim() ? { primaryPocEmail: organization.primaryPocEmail.trim() } : {}),
    ...(enroll ? { initialAdministrator: administratorIntent(administrator, newPerson) } : {}),
  };
  useEffect(() => { alive.current = true; return () => { alive.current = false; generation.current++; }; }, []);
  useEffect(() => { heading.current?.focus(); }, [step, recovery]);
  useEffect(() => {
    if (Object.keys(errors).length) form.current?.querySelector<HTMLInputElement>('[aria-invalid="true"]')?.focus();
  }, [errors]);
  useEffect(() => {
    if (!queryKey || (pending.current && key.current === queryKey)) return;
    if (confirmedRef.current && key.current === queryKey) return;
    key.current = queryKey;
    const controller = new AbortController();
    const current = ++generation.current;
    setRecovery(true); setBusy(true); setError(null); setMissing(false);
    api.getOrganizationCreation(queryKey, controller.signal).then(value => {
      if (controller.signal.aborted || current !== generation.current) return;
      if (value) navigate(`/organizations/${value.tenantId}/provisioning?key=${encodeURIComponent(queryKey)}`, { replace: true });
      else setMissing(true);
    }).catch(reason => {
      if (!controller.signal.aborted && current === generation.current) setError(message(reason));
    }).finally(() => {
      if (!controller.signal.aborted && current === generation.current) setBusy(false);
    });
    return () => controller.abort();
  }, [queryKey, navigate]);

  const advance = () => {
    const nextErrors = step === 1 ? validateOrganization(organization) : enroll ? validateAdministrator(administrator, newPerson) : {};
    setErrors(nextErrors);
    if (!Object.keys(nextErrors).length) { setStep(value => value + 1); setError(null); }
  };
  const create = async (body: OrganizationCreationRequest) => {
    if (pending.current) return;
    pending.current = true;
    const current = ++generation.current;
    confirmedRef.current = body;
    setConfirmed(body); setBusy(true); setError(null);
    navigate(`/organizations/new?key=${encodeURIComponent(key.current)}`, { replace: true });
    try {
      const value = await api.createOrganization(body, key.current);
      if (!alive.current || current !== generation.current) return;
      navigate(`/organizations/${value.tenantId}/provisioning?key=${encodeURIComponent(key.current)}`,
        { replace: true, state: { autoResume: !!body.initialAdministrator } });
    } catch (reason) {
      if (!alive.current || current !== generation.current) return;
      setError(message(reason));
      if (reason instanceof api.WorkspaceOperationError && [400, 422].includes(reason.status ?? 0)) {
        confirmedRef.current = null; setConfirmed(null);
      } else { setRecovery(true); setMissing(false); }
    } finally {
      if (alive.current && current === generation.current) { pending.current = false; setBusy(false); }
    }
  };
  const recover = async () => {
    if (pending.current) return;
    pending.current = true; setBusy(true); setError(null); setMissing(false);
    const current = ++generation.current;
    try {
      const value = await api.getOrganizationCreation(key.current);
      if (!alive.current || current !== generation.current) return;
      if (value) navigate(`/organizations/${value.tenantId}/provisioning?key=${encodeURIComponent(key.current)}`, { replace: true });
      else setMissing(true);
    } catch (reason) {
      if (alive.current && current === generation.current) setError(message(reason));
    } finally {
      if (alive.current && current === generation.current) { pending.current = false; setBusy(false); }
    }
  };
  const edit = (value: number) => { setErrors({}); setError(null); setStep(value); };
  return <PageLayout title="Add organization">
    <PageHero eyebrow="Organizations / Add organization" title="Add organization"
      description="Create the organization once. Enrollment can be resumed."
      actions={<SetupSteps step={recovery ? 4 : step} />} />
    <div className="space-y-5 text-slate-800 dark:text-gray-100">
      {error && <p role="alert" className={errorClass}>{error}</p>}
      {recovery ? <section className="space-y-5">
        <h2 ref={heading} tabIndex={-1} className="text-xl font-semibold">Recover organization creation</h2>
        <p role="status" className={warningClass}>{busy ? 'Checking saved organization setup...' : missing
          ? 'No saved organization was found for this request yet. Check again before starting another creation.'
          : 'The creation outcome is uncertain. Check its saved status before retrying; the same request identity is retained.'}</p>
        {confirmed && <SetupSummary request={confirmed} />}
        {!confirmed && <SetupInfo>Pre-submission form input was not saved. This recovery key checks only confirmed server work.</SetupInfo>}
        <div className="flex flex-wrap gap-3">
          <button className={buttonClass} disabled={busy} onClick={() => void recover()}>Check creation status</button>
          {missing && confirmed && <button className={secondaryButtonClass} disabled={busy} onClick={() => void create(confirmed)}>Retry creation</button>}
          <Link className={secondaryButtonClass} to="/organizations">Back to organizations</Link>
        </div>
      </section> : <form ref={form} noValidate onSubmit={event => {
        event.preventDefault();
        if (step < 3) advance(); else void create(request);
      }} className="space-y-5">
        <div className="grid items-start gap-5 lg:grid-cols-[minmax(0,1fr)_320px]">
          <div className="min-w-0 space-y-4">
            <h2 ref={heading} tabIndex={-1} className="text-xl font-semibold">{step === 1 ? 'Organization details' : step === 2 ? 'Initial administrator' : 'Review organization setup'}</h2>
            {step === 1 && <section className={`${setupCard} space-y-4`}>
              {([
                ['displayName', 'Organization name'], ['legalEntityName', 'Legal entity name (optional)'],
                ['primaryPocName', 'Primary contact name (optional)'], ['primaryPocEmail', 'Primary contact email (optional)'],
              ] as const).map(([field, label]) => <SetupField key={field} label={label} value={organization[field]}
                type={field === 'primaryPocEmail' ? 'email' : 'text'} error={errors[field]} maxLength={organizationFieldLimits[field]}
                onChange={value => {
                  setOrganization(current => ({ ...current, [field]: value }));
                  setErrors(current => { const next = { ...current }; delete next[field]; return next; });
                }} />)}
              <p className="text-xs text-slate-500">Organization name is required. Contact information does not invite a user or grant access.</p>
            </section>}
            {step === 2 && <section className={`${setupCard} space-y-5`}>
              <p className="rounded bg-indigo-50 p-3 text-sm text-indigo-950 dark:bg-indigo-950 dark:text-indigo-100">Organization: {organization.displayName}</p>
              <fieldset className="grid gap-3 text-sm sm:grid-cols-2"><legend className="mb-3 font-semibold">Administrator enrollment</legend>
                <label className="flex items-center gap-3 rounded-xl border border-slate-200 p-4 has-[:checked]:border-indigo-500 has-[:checked]:bg-indigo-50 dark:border-slate-700 dark:has-[:checked]:bg-indigo-950"><input type="radio" name="administrator-enrollment" checked={enroll} onChange={() => { setEnroll(true); setErrors({}); }} />Enroll administrator now</label>
                <label className="flex items-center gap-3 rounded-xl border border-slate-200 p-4 has-[:checked]:border-indigo-500 has-[:checked]:bg-indigo-50 dark:border-slate-700 dark:has-[:checked]:bg-indigo-950"><input type="radio" name="administrator-enrollment" checked={!enroll} onChange={() => { setEnroll(false); setErrors({}); }} />Complete enrollment later</label>
              </fieldset>
              {enroll && <AdministratorInputs fields={administrator} newPerson={newPerson} onNewPerson={value => { setNewPerson(value); setErrors({}); }}
                errors={errors} onChange={(field, value) => {
                  setAdministrator(current => ({ ...current, [field]: value }));
                  setErrors(current => { const next = { ...current }; delete next[field]; return next; });
                }} />}
            </section>}
            {step === 3 && <>
              <SetupSummary request={request} />
              <div className="flex flex-wrap gap-3">
                <button type="button" disabled={busy} className={secondaryButtonClass} onClick={() => edit(1)}>Edit organization details</button>
                <button type="button" disabled={busy} className={secondaryButtonClass} onClick={() => edit(2)}>Edit administrator</button>
              </div>
              <section className={setupCard}><h3 className="mb-3 font-semibold">What will be created</h3>
                <ol className="list-inside list-decimal space-y-3 text-sm">
                  <li>Organization record and resumable setup record</li>
                  {enroll && <>{newPerson && <li>Organization-local Person record</li>}<li>Explicit organization membership</li><li>Organization Administrator assignment</li></>}
                </ol>
                {!enroll && <p className="mt-4 text-sm">Administrator and membership enrollment will remain pending.</p>}
                <p className="mt-4 text-xs text-slate-500">These are intended writes, not completed operations.</p>
              </section>
            </>}
          </div>
          <aside className="space-y-4">
            <SetupInfo><strong>Create the organization once.</strong><p className="mt-2">Nothing is saved before you confirm. After creation, saved setup work can be resumed from Organizations.</p></SetupInfo>
            <SetupInfo><strong>Organization Administrator</strong><p className="mt-2">Manages this organization’s membership. System and RMF responsibilities are assigned separately.</p></SetupInfo>
            <SetupInfo><strong>Choose the right person.</strong><p className="mt-2">Find a user in your connected Entra directory. Review their identity before granting access; your primary contact is not selected automatically.</p></SetupInfo>
          </aside>
        </div>
        {Object.keys(errors).length > 0 && <p role="alert" className={errorClass}>Correct the highlighted fields before continuing.</p>}
        {busy && <p role="status">Saving organization creation. Do not submit another request.</p>}
        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 pt-5">
          <div className="flex gap-3">{step > 1 && <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => edit(step - 1)}>Back</button>}
            <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => navigate('/organizations')}>Cancel</button></div>
          <button type="submit" className={buttonClass} disabled={busy}>{step === 1 ? 'Continue' : step === 2 ? 'Review setup' : 'Create organization'}</button>
        </div>
      </form>}
    </div>
  </PageLayout>;
}
