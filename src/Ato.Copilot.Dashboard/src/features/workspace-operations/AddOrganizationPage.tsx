import { useCallback, useEffect, useRef, useState } from 'react';
import SetupFrame, { SetupGuidance, SetupPanel } from '../onboarding/shared/SetupFrame';
import * as onboarding from './organizationOnboardingApi';
import { Link, useLocation, useNavigate } from '../workspaces/workspaceNavigation';
import * as api from './api';
import type { OrganizationCreationRequest } from './types';
import { buttonClass, errorClass, message, secondaryButtonClass, warningClass } from './workspaceUi';
import {
  AdministratorInputs, administratorIntent, emptyAdministrator, emptyOrganization, SetupField,
  SetupInfo, setupCard, SetupSummary, validateAdministrator, validateOrganization,
  organizationFieldLimits, type FieldErrors,
} from './OrganizationSetupPresentation';

function draftFingerprint(values: onboarding.OrganizationDraftValues): string {
  function normalize(value: unknown): unknown {
    if (value == null) return undefined;
    if (typeof value === 'string') return value.trim() || undefined;
    if (typeof value === 'object' && !Array.isArray(value)) {
      const entries = Object.entries(value).sort(([first], [second]) => first.localeCompare(second))
        .map(([key, entry]) => [key, normalize(entry)] as const).filter(([, entry]) => entry !== undefined);
      return entries.length ? Object.fromEntries(entries) : undefined;
    }
    return value;
  }
  return JSON.stringify(normalize(values));
}

export default function AddOrganizationPage() {
  const navigate = useNavigate();
  const location = useLocation();
  const queryKey = new URLSearchParams(location.search).get('key');
  const requestedDraft = new URLSearchParams(location.search).get('draft');
  const [draft, setDraft] = useState<onboarding.OrganizationOnboardingDraft | null>(null);
  const draftId = useRef(requestedDraft ?? crypto.randomUUID());
  const localDraftId = useRef(requestedDraft ? null : draftId.current);
  const previousDraftRoute = useRef(requestedDraft);
  const hydrationNavigation = useRef(navigate);
  hydrationNavigation.current = navigate;
  const [existingTenant, setExistingTenant] = useState<string | null>(null);
  const [existingSummary, setExistingSummary] = useState<onboarding.OrganizationSetupSummary | null>(null);
  const [existingSearch, setExistingSearch] = useState('');
  const [existingOptions, setExistingOptions] = useState<{ id: string; displayName: string }[]>([]);
  const [reuseAdmin, setReuseAdmin] = useState(false);
  const [offline, setOffline] = useState(false);
  const [manual, setManual] = useState(false);
  const [draftUncertain, setDraftUncertain] = useState(false);
  const [draftConflict, setDraftConflict] = useState(false);
  const [serverVersion, setServerVersion] = useState<onboarding.OrganizationOnboardingDraft | null>(null);
  const attemptedDraft = useRef<{ id: string; expectedRevision: number; fingerprint: string } | null>(null);
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
  const draftValues = (): onboarding.OrganizationDraftValues => ({
    ...organization, organizationChoice: existingTenant ? 'existing' : 'create', existingTenantId: existingTenant,
    administratorChoice: reuseAdmin ? 'existing' : enroll ? 'other' : 'deferred',
    administrator: enroll && !reuseAdmin ? {
      directoryTenantId: administrator.directoryTenantId || null, objectId: administrator.objectId || null,
      ...(newPerson ? { newPerson: { displayName: administrator.displayName, email: administrator.email } } : { personId: administrator.personId || null }),
    } : null,
  });
  const adoptDraft = useCallback((value: onboarding.OrganizationOnboardingDraft) => {
    draftId.current = value.draftId; setDraft(value); key.current = value.creationKey;
    const values = value.values;
    setOrganization({ displayName: values.displayName ?? '', legalEntityName: values.legalEntityName ?? '',
      primaryPocName: values.primaryPocName ?? '', primaryPocEmail: values.primaryPocEmail ?? '' });
    setExistingTenant(values.existingTenantId ?? null);
    setReuseAdmin(values.administratorChoice === 'existing'); setEnroll(values.administratorChoice === 'other');
    setStep(value.currentStep === 'review' ? 3 : value.currentStep === 'administrator' ? 2 : 1);
    setAdministrator({ directoryTenantId: values.administrator?.directoryTenantId ?? '', objectId: values.administrator?.objectId ?? '',
      personId: values.administrator?.personId ?? '', displayName: values.administrator?.newPerson?.displayName ?? '', email: values.administrator?.newPerson?.email ?? '' });
    setNewPerson(!values.administrator || !!values.administrator.newPerson);
    setOffline(false); setManual(false); setDraftUncertain(false); setDraftConflict(false); setServerVersion(null);
    setErrors({}); setError(null);
  }, []);
  useEffect(() => {
    const previous = previousDraftRoute.current;
    previousDraftRoute.current = requestedDraft;
    if (!requestedDraft) {
      if (previous) {
        pending.current = false;
        draftId.current = crypto.randomUUID();
        localDraftId.current = draftId.current;
        key.current = crypto.randomUUID();
        setDraft(null); setOrganization(emptyOrganization); setAdministrator(emptyAdministrator);
        setExistingTenant(null); setReuseAdmin(false); setEnroll(true); setNewPerson(true);
        setStep(1); setOffline(false); setManual(false); setDraftUncertain(false);
        setDraftConflict(false); setServerVersion(null); attemptedDraft.current = null;
        setError(null); setErrors({}); setBusy(false);
      }
      return;
    }
    if (localDraftId.current === requestedDraft) return;
    const controller = new AbortController();
    pending.current = false;
    draftId.current = requestedDraft;
    setDraft(null); setOrganization(emptyOrganization); setAdministrator(emptyAdministrator);
    setExistingTenant(null); setReuseAdmin(false); setEnroll(true); setNewPerson(true);
    setStep(1); setOffline(false); setManual(false); setDraftUncertain(false);
    setDraftConflict(false); setServerVersion(null); attemptedDraft.current = null;
    setError(null); setErrors({}); setBusy(true);
    onboarding.getOrganizationDraft(requestedDraft, controller.signal).then(value => {
      if (controller.signal.aborted) return;
      if (value.draftId !== requestedDraft) throw new Error('The saved draft does not match the requested organization setup.');
      if (value.state === 'Confirmed') { hydrationNavigation.current(value.resumeUrl, { replace: true }); return; }
      if (value.state === 'Discarded') throw new Error('This draft was discarded. Return to setup to choose another record.');
      adoptDraft(value);
    }).catch(reason => { if (!controller.signal.aborted) setError(message(reason)); })
      .finally(() => { if (!controller.signal.aborted) setBusy(false); });
    return () => controller.abort();
  }, [requestedDraft, adoptDraft]);
  useEffect(() => {
    if (!existingTenant) { setExistingSummary(null); return; }
    const controller = new AbortController();
    onboarding.getOrganizationSetupSummary(existingTenant, null, controller.signal)
      .then(value => { if (!controller.signal.aborted) setExistingSummary(value); })
      .catch(reason => { if (!controller.signal.aborted) setError(message(reason)); });
    return () => controller.abort();
  }, [existingTenant]);
  const saveLater = async () => {
    if (pending.current || busy || recovery || draftUncertain) return;
    const owner = draftId.current;
    const values = draftValues();
    attemptedDraft.current = { id: owner, expectedRevision: draft?.revision ?? 0, fingerprint: draftFingerprint(values) };
    setDraftConflict(false); setServerVersion(null);
    pending.current = true; setBusy(true); setError(null);
    navigate(`/organizations/new?draft=${owner}`, { replace: true });
    try {
      const saved = await onboarding.saveOrganizationDraft(owner, values,
        step === 1 ? 'details' : step === 2 ? 'administrator' : 'review', draft?.revision ?? 0);
      if (!alive.current || draftId.current !== owner) return;
      if (saved.draftId !== owner) throw new Error('The saved response does not match this draft.');
      setDraft(saved);
      navigate('/setup/resume');
    } catch (reason) { if (alive.current && draftId.current === owner) {
      setError(message(reason));
      setDraftConflict(reason instanceof api.WorkspaceOperationError && reason.status === 409);
      setDraftUncertain(!(reason instanceof api.WorkspaceOperationError && [400, 422].includes(reason.status ?? 0)));
    } }
    finally { if (alive.current && draftId.current === owner) { pending.current = false; setBusy(false); } }
  };
  const confirmSaved = async () => {
    if (pending.current || busy || draftUncertain) return;
    const owner = draftId.current;
    const values = draftValues();
    attemptedDraft.current = { id: owner, expectedRevision: draft?.revision ?? 0, fingerprint: draftFingerprint(values) };
    setDraftConflict(false); setServerVersion(null);
    pending.current = true; setBusy(true); setError(null);
    navigate(`/organizations/new?draft=${owner}`, { replace: true });
    try {
      const saved = await onboarding.saveOrganizationDraft(owner, values, 'review', draft?.revision ?? 0);
      if (!alive.current || draftId.current !== owner) return;
      if (saved.draftId !== owner) throw new Error('The saved response does not match this draft.');
      setDraft(saved);
      const value = await onboarding.confirmOrganizationDraft(saved.draftId, saved.revision);
      if (!alive.current || draftId.current !== owner) return;
      if (value.draftId !== owner) throw new Error('The confirmation response does not match this draft.');
      navigate(value.resumeUrl, { replace: true, state: { autoResume: enroll && !reuseAdmin } });
    } catch (reason) {
      if (alive.current && draftId.current === owner) {
        setError(`Cannot confirm organization outcome: ${message(reason)}. Reopen this saved draft before retrying.`);
        setDraftUncertain(true);
        setDraftConflict(reason instanceof api.WorkspaceOperationError && reason.status === 409);
        navigate(`/organizations/new?draft=${draftId.current}`, { replace: true });
      }
    } finally { if (alive.current && draftId.current === owner) { pending.current = false; setBusy(false); } }
  };
  const reconcileDraft = async () => {
    if (busy) return;
    const owner = draftId.current;
    setBusy(true); setError(null);
    try {
      const saved = await onboarding.getOrganizationDraft(owner);
      if (!alive.current || draftId.current !== owner) return;
      if (saved.draftId !== owner) throw new Error('The saved response does not match this draft.');
      const attempt = attemptedDraft.current;
      const exactAttempt = attempt?.id === owner && !draftConflict
        && draftFingerprint(saved.values) === attempt.fingerprint
        && draftFingerprint(draftValues()) === attempt.fingerprint;
      if (saved.state !== 'Discarded' && (!exactAttempt
        || saved.state === 'Draft' && saved.revision !== attempt!.expectedRevision + 1)) {
        setServerVersion(saved); setDraftConflict(true);
        setError('Saved data changed. Review the server version before replacing your retained local edits; no newer revision has been adopted.');
      } else if (saved.state === 'Confirmed') navigate(saved.resumeUrl, { replace: true });
      else if (saved.state === 'Draft') {
        setDraft(saved); setDraftUncertain(false); key.current = saved.creationKey;
      } else setError('This draft was discarded. It cannot be edited or confirmed.');
    } catch (reason) { if (alive.current && draftId.current === owner) {
      if (reason instanceof api.WorkspaceOperationError && reason.status === 404) {
        if (!draft && attemptedDraft.current?.expectedRevision === 0 && !draftConflict) setDraftUncertain(false);
        setError('No saved draft was found. Retry Save with this same retained draft identity; no new request identity will be generated.');
      } else setError(`Saved outcome is not yet confirmed: ${message(reason)}`);
    } }
    finally { if (alive.current && draftId.current === owner) setBusy(false); }
  };
  const searchExisting = async () => {
    if (busy) return;
    setBusy(true); setError(null);
    try {
      const result = await api.listOrganizations({ page: 1, pageSize: 25, search: existingSearch });
      if (alive.current) setExistingOptions(result.items);
    } catch (reason) { if (alive.current) setError(message(reason)); }
    finally { if (alive.current) setBusy(false); }
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
    const nextErrors = step === 1 ? validateOrganization(organization) : enroll && !reuseAdmin ? validateAdministrator(administrator, newPerson) : {};
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
  return <SetupFrame journey="Organization" title={offline ? 'Directory lookup is unavailable' : step === 1 ? 'Set up an organization' : step === 2 ? 'Choose the organization administrator' : 'Review organization setup'}
    description="Create or reuse the organization once. Review administrator access separately."
    steps={[{ id: '1', label: 'Organization details' }, { id: '2', label: 'Administrator access' }, { id: '3', label: 'Review setup' }]}
    currentStep={String(step)} onStepChange={!recovery ? id => edit(Number(id)) : undefined}
    onSaveLater={!recovery ? () => void saveLater() : undefined} busy={busy} error={error}
    onBack={!recovery && step > 1 ? () => edit(step - 1) : undefined}
    primaryAction={!recovery && !offline ? { label: step === 1 ? 'Continue' : step === 2 ? 'Review setup' : existingTenant ? 'Confirm organization reuse' : 'Create organization',
      onClick: () => { if (step < 3) advance(); else if (draft || existingTenant) void confirmSaved(); else void create(request); },
      disabled: busy || draftUncertain || !!existingTenant && step === 3 && (!existingSummary || enroll && existingSummary.liveAccess.state === 'Available')
        || !!requestedDraft && !draft } : undefined}
    guidance={<><SetupGuidance title="No duplicate registration">Choose an authorized organization by its record, not by a matching name.</SetupGuidance>
      <SetupGuidance title="Primary contact vs administrator">Contact information is descriptive. No membership, invitation or system role is granted by a draft save.</SetupGuidance></>}
    saveStatus={draft ? `Server draft saved · revision ${draft.revision}` : 'Unsaved until explicit save or confirmation'}>
    <div className="space-y-5 text-slate-800 dark:text-gray-100">
      {draftUncertain && <SetupPanel title="Confirm the saved outcome">
        <p className={warningClass}>Your edits and draft identity are retained. Read the server result before another save or creation.</p>
        <button type="button" disabled={busy} className={secondaryButtonClass} onClick={() => void reconcileDraft()}>Check saved draft</button>
        {serverVersion && <div className="mt-4 space-y-3">
          <p>Server revision {serverVersion.revision}: {serverVersion.values.displayName || 'Organization draft'}</p>
          <p>Primary contact: {serverVersion.values.primaryPocName || 'Not recorded'}</p>
          <p className="text-sm">Using the saved server version replaces the local form and step. Copy any unsaved edits you still need before continuing.</p>
          <button type="button" disabled={busy} className={secondaryButtonClass} onClick={() => {
            if (serverVersion.state === 'Confirmed') navigate(serverVersion.resumeUrl, { replace: true });
            else adoptDraft(serverVersion);
          }}>Use saved server version</button>
        </div>}
      </SetupPanel>}
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
      </section> : offline ? <SetupPanel title="Continue with an explicit choice">
        <p className={warningClass}>Lookup results are unavailable. Manual identity details are not directory verification.</p>
        {existingSummary?.liveAccess.state === 'Available' && <button className={secondaryButtonClass} onClick={() => { setReuseAdmin(true); setEnroll(false); setOffline(false); }}>Use the existing administrator</button>}
        <button className={secondaryButtonClass} onClick={() => { setManual(true); setOffline(false); }}>Enter identity details manually</button>
        <button className={secondaryButtonClass} onClick={() => { setEnroll(false); setReuseAdmin(false); setOffline(false); }}>Complete enrollment later</button>
        <button className={secondaryButtonClass} onClick={() => setOffline(false)}>Return to administrator setup</button>
      </SetupPanel> : <form ref={form} noValidate onSubmit={event => {
        event.preventDefault();
        if (step < 3) advance(); else if (draft || existingTenant) void confirmSaved(); else void create(request);
      }} className="space-y-5">
        <fieldset disabled={busy} className="space-y-5">
        <div className="space-y-5">
          <div className="min-w-0 space-y-4">
            {step !== 1 && <h2 ref={heading} tabIndex={-1} className="text-xl font-semibold">{step === 2 ? 'Initial administrator' : 'Confirm the stated outcomes'}</h2>}
            {step === 1 && <><section className={`${setupCard} grid gap-4 sm:grid-cols-2`}>
              <h2 className="font-semibold sm:col-span-2">Organization identity</h2>
              {([
                ['displayName', 'Organization name'], ['legalEntityName', 'Legal entity name (optional)'],
                ['primaryPocName', 'Primary contact name (optional)'], ['primaryPocEmail', 'Primary contact email (optional)'],
              ] as const).map(([field, label]) => <SetupField key={field} label={label} value={organization[field]}
                type={field === 'primaryPocEmail' ? 'email' : 'text'} error={errors[field]} maxLength={organizationFieldLimits[field]}
                onChange={value => {
                  setOrganization(current => ({ ...current, [field]: value }));
                  setErrors(current => { const next = { ...current }; delete next[field]; return next; });
                }} />)}
              <p className="text-xs text-slate-500 sm:col-span-2">Organization name is required. Contact information does not invite a user or grant access.</p>
            </section><SetupPanel title="Use an existing organization">
              <SetupField label="Find an authorized organization" value={existingSearch} onChange={setExistingSearch} />
              <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => void searchExisting()}>Search organizations</button>
              {existingOptions.map(item => <button type="button" className={`${secondaryButtonClass} block mt-2`} key={item.id}
                onClick={() => { setExistingTenant(item.id); setOrganization(current => ({ ...current, displayName: item.displayName })); }}>{item.displayName} · Use existing organization</button>)}
              {existingTenant && <p role="status">Existing organization selected. Its record will not be recreated. <button type="button" className="underline" onClick={() => { setExistingTenant(null); setReuseAdmin(false); }}>Create a different organization</button></p>}
            </SetupPanel></>}
            {step === 2 && <section className={`${setupCard} space-y-5`}>
              <p className="rounded bg-indigo-50 p-3 text-sm text-indigo-950 dark:bg-indigo-950 dark:text-indigo-100">Organization: {organization.displayName}</p>
              <fieldset className="grid gap-3 text-sm sm:grid-cols-2"><legend className="mb-3 font-semibold">Administrator enrollment</legend>
                {existingSummary?.liveAccess.state === 'Available' && <label><input type="radio" name="administrator-enrollment" checked={reuseAdmin}
                  onChange={() => { setReuseAdmin(true); setEnroll(false); }} />Use the existing administrator</label>}
                <label className="flex items-center gap-3 rounded-xl border border-slate-200 p-4 has-[:checked]:border-indigo-500 has-[:checked]:bg-indigo-50 dark:border-slate-700 dark:has-[:checked]:bg-indigo-950"><input type="radio" name="administrator-enrollment" checked={enroll && !reuseAdmin} onChange={() => { setEnroll(true); setReuseAdmin(false); setErrors({}); }} />Enroll administrator now</label>
                <label className="flex items-center gap-3 rounded-xl border border-slate-200 p-4 has-[:checked]:border-indigo-500 has-[:checked]:bg-indigo-50 dark:border-slate-700 dark:has-[:checked]:bg-indigo-950"><input type="radio" name="administrator-enrollment" checked={!enroll && !reuseAdmin} onChange={() => { setEnroll(false); setReuseAdmin(false); setErrors({}); }} />Complete enrollment later</label>
              </fieldset>
              {existingSummary?.liveAccess.state === 'Available' && <SetupInfo>Current administrator: {existingSummary.liveAccess.administrators.items.map(item => item.displayName).join(', ')}. Selecting another administrator requires the existing role-management workflow; initial enrollment does not replace them.</SetupInfo>}
              {enroll && !reuseAdmin && <AdministratorInputs fields={administrator} newPerson={newPerson} initialMode={manual ? 'manual' : undefined} onNewPerson={value => { setNewPerson(value); setErrors({}); }}
                errors={errors} onChange={(field, value) => {
                  setAdministrator(current => ({ ...current, [field]: value }));
                  setErrors(current => { const next = { ...current }; delete next[field]; return next; });
                }} />}
              <button type="button" className={secondaryButtonClass} onClick={() => setOffline(true)}>Directory lookup is unavailable</button>
            </section>}
            {step === 3 && <>
              <SetupSummary request={request} />
              {existingTenant && <SetupInfo>Reuse the existing organization. {reuseAdmin ? 'Use the recorded active administrator without additional grants.' : enroll ? 'A separate enrollment request will be validated.' : 'Additional enrollment is deferred.'}</SetupInfo>}
              <div className="flex flex-wrap gap-3">
                <button type="button" disabled={busy} className={secondaryButtonClass} onClick={() => edit(1)}>Edit organization details</button>
                <button type="button" disabled={busy} className={secondaryButtonClass} onClick={() => edit(2)}>Edit administrator</button>
              </div>
              <section className={setupCard}><h3 className="mb-3 font-semibold">What will be created</h3>
                <ol className="list-inside list-decimal space-y-3 text-sm">
                  <li>{existingTenant ? 'Reuse the selected organization record' : 'Organization record and resumable setup record'}</li>
                  {enroll && <>{newPerson && <li>Organization-local Person record</li>}<li>Explicit organization membership</li><li>Organization Administrator assignment</li></>}
                </ol>
                {!enroll && <p className="mt-4 text-sm">Administrator and membership enrollment will remain pending.</p>}
                <p className="mt-4 text-xs text-slate-500">These are intended writes, not completed operations.</p>
              </section>
            </>}
          </div>
        </div>
        {Object.keys(errors).length > 0 && <p role="alert" className={errorClass}>Correct the highlighted fields before continuing.</p>}
        {busy && <p role="status">Saving organization creation. Do not submit another request.</p>}
        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-200 pt-5">
          <div className="flex gap-3">
            <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => navigate('/organizations')}>Cancel</button></div>
        </div>
        </fieldset>
      </form>}
    </div>
  </SetupFrame>;
}
