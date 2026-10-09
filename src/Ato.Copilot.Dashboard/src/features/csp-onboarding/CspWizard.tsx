import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useSearchParams } from '../workspaces/workspaceNavigation';
import SetupFrame, { SetupGuidance, SetupPanel } from '../onboarding/shared/SetupFrame';
import { Field } from '../provider-authorizations/forms';
import { OfferingPicker } from '../provider-authorizations/OfferingIntake';
import { getOffering } from '../provider-authorizations/api';
import { PackageReceiptCard } from '../package-imports/PackageReceipts';
import { preparePackageUpload } from '../package-imports/uploadIdentity';
import { validatePackageFiles, PACKAGE_FILE_ACCEPT } from '../package-imports/validation';
import { PackageImportError } from '../package-imports/request';
import * as api from './providerSetupApi';
import type { ProviderScreen, SetupDraft, SetupState, UploadIntent } from './providerSetupApi';

const steps = [
  { id: 'p-details', label: 'Confirm provider identity' }, { id: 'p-access', label: 'Confirm access and contacts' },
  { id: 'p-offering', label: 'Add the first offering' }, { id: 'p-authorization', label: 'Choose authorization starting point' },
  { id: 'p-sources', label: 'Add available records' }, { id: 'p-review', label: 'Review and finish setup' },
];
const titles: Record<ProviderScreen, string> = {
  'p-details': 'Confirm provider identity', 'p-access': 'Confirm access and contacts', 'p-offering': 'Add the first offering',
  'p-authorization': 'Choose authorization starting point', 'p-sources': 'Add available records',
  'p-uncertain': 'Check the package receipt', 'p-review': 'Review and finish setup',
  'p-ready': 'Provider setup complete',
};
const descriptions: Record<ProviderScreen, string> = {
  'p-details': 'Confirm the operating organization, provider name, DoD component, timezone, and service contact.',
  'p-access': 'Review actual administrator access and record operational and security contacts separately from role grants.',
  'p-offering': 'Create or select the first offering and optionally associate it with a service portfolio.',
  'p-authorization': 'Record onboarding intent and optionally submit an eMASS package for canonical intake and analysis.',
  'p-sources': 'Optionally retain supporting material, reference an existing package, or explicitly defer records.',
  'p-uncertain': 'The upload response was interrupted. Confirm the existing request before trying again.',
  'p-review': 'Review saved facts, unresolved work, and explicit non-claims before completing onboarding.',
  'p-ready': 'Review the onboarding summary, then open the provider workspace when you are ready.',
};
const button = 'rounded-lg border border-slate-300 px-4 py-2 text-sm text-indigo-700 disabled:opacity-50';
const deferral = (reason: string) => ({ reason, ownerRole: 'CSP.Admin' as const });
const offeringEnvironments = [
  { id: 'AzureGovernment', label: 'Azure Government', canonical: 'AzureUSGovernment' },
  { id: 'AzureCommercial', label: 'Azure Commercial', canonical: 'AzureCloud' },
  { id: 'AwsGovCloud', label: 'AWS GovCloud', canonical: 'ManualService' },
  { id: 'Microsoft365DoD', label: 'Microsoft 365 DoD', canonical: 'Microsoft365DoD' },
  { id: 'Other', label: 'Other service environment', canonical: 'ManualService' },
] as const;

function emptyDraft(state: SetupState): SetupDraft {
  return {
    currentScreen: 'p-details',
    details: { displayName: state.profile.identity?.displayName ?? '', legalEntityName: state.profile.identity?.legalEntityName ?? '',
      serviceContactName: '', serviceContactEmail: state.profile.supportContact?.primarySupportEmail ?? '',
      dodComponent: '', timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC',
      legacyClassificationDefault: state.profile.classification?.defaultClassificationFloor ?? 'Unclassified',
      confirmLegacyClassificationDefault: !!state.profile.classification },
    operationalContact: { choice: 'Unspecified' }, securityContact: { choice: 'Unspecified' },
    firstOffering: { choice: 'Unspecified' }, authorization: { choice: 'Unspecified' },
    sources: { choice: 'Unspecified', intentIds: [] },
  };
}

function normalizeDraft(value: SetupDraft): SetupDraft {
  return {
    ...value,
    currentScreen: value.currentScreen === ('p-ready' as ProviderScreen) ? 'p-ready' : value.currentScreen,
    details: {
      ...value.details,
      dodComponent: value.details.dodComponent ?? '',
      timeZoneId: value.details.timeZoneId ?? Intl.DateTimeFormat().resolvedOptions().timeZone ?? 'UTC',
    },
    operationalContact: value.operationalContact ?? { choice: 'Unspecified' },
    firstOffering: { ...value.firstOffering, portfolioName: value.firstOffering.portfolioName ?? '' },
    authorization: value.authorization ?? { choice: 'Unspecified' },
  };
}

export default function CspWizard() {
  const navigate = useNavigate();
  const [search] = useSearchParams();
  const [state, setState] = useState<SetupState | null>(null);
  const [draft, setDraft] = useState<SetupDraft | null>(null);
  const [screen, setScreen] = useState<ProviderScreen>('p-details');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [saveStatus, setSaveStatus] = useState('');
  const [projectionUnavailable, setProjectionUnavailable] = useState(false);
  const [files, setFiles] = useState<File[]>([]);
  const [packageName, setPackageName] = useState('');
  const [classification, setClassification] = useState('');
  const [synthetic, setSynthetic] = useState(false);
  const [markings, setMarkings] = useState<string[]>([]);
  const [confirmed, setConfirmed] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const lock = useRef(false);
  const command = useRef<{ signature: string; key: string } | null>(null);
  const commandRetry = useRef<(() => Promise<api.CommandResult>) | null>(null);
  const intentPending = useRef<UploadIntent | null>(null);
  const preparationRetry = useRef<{ revision: number; input: api.UploadIntentInput } | null>(null);
  const hydrate = (next: SetupState, restore = false) => {
    setState(next);
    setProjectionUnavailable(false);
    const fields = normalizeDraft(next.draft?.fields ?? emptyDraft(next));
    setDraft(fields);
    if (restore) {
      setScreen(fields.currentScreen ?? 'p-details');
      setPackageName(fields.sources.selection?.packageName ?? '');
    }
  };
  useEffect(() => {
    const controller = new AbortController();
    api.getSetup(controller.signal).then(next => {
      if (controller.signal.aborted) return;
      hydrate(next, true);
      if (search.get('screen') === 'p-sources') setScreen('p-sources');
      const pending = next.uploadIntents?.find(item => item.intentId === search.get('intentId'));
      if (pending) { intentPending.current = pending; setScreen('p-uncertain'); }
    }).catch(reason => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : 'Provider setup is unavailable.'); });
    return () => controller.abort();
  }, [navigate, search]);

  const act = async (work: () => Promise<void>) => {
    if (lock.current) return;
    lock.current = true; setBusy(true); setError(null);
    try { await work(); }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'The operation is unavailable; keep the saved request.'); }
    finally { lock.current = false; setBusy(false); }
  };
  const keyFor = (signature: string) => {
    if (command.current && command.current.signature !== signature)
      throw new Error('The previous command outcome is uncertain. Reload the saved setup before changing the request.');
    command.current ??= { signature, key: crypto.randomUUID() };
    return command.current.key;
  };
  const result = (saved: api.CommandResult): SetupState => {
    if (saved.outcome !== 'Committed') throw new Error('No committed setup outcome was returned. Reconcile the same request.');
    command.current = null;
    commandRetry.current = null;
    if (saved.current.projectionState !== 'Available' || !saved.current.state) {
      setProjectionUnavailable(true);
      setSaveStatus('Saved; current status unavailable. Reload before continuing.');
      throw new Error(saved.current.error?.message ?? 'Saved; current status unavailable. Do not repeat the mutation.');
    }
    hydrate(saved.current.state);
    setSaveStatus('Saved to the server.');
    return saved.current.state;
  };
  const execute = async (signature: string, send: (key: string) => Promise<api.CommandResult>) => {
    const key = keyFor(signature);
    commandRetry.current ??= () => send(key);
    try { return result(await commandRetry.current()); }
    catch (reason) {
      if (reason instanceof PackageImportError && [400, 401, 403, 409, 422].includes(reason.status ?? 0)) {
        command.current = null; commandRetry.current = null;
      }
      throw reason;
    }
  };
  const save = async (fields: SetupDraft): Promise<SetupState> => {
    if (projectionUnavailable) throw new Error('The previous command is saved. Reload current setup before making another change.');
    if (!state) throw new Error('Reload provider setup first.');
    const value = { ...fields, currentScreen: screen };
    if (files.length && !intentPending.current && !state.uploadIntents?.some(item => !item.receipt)) {
      const validation = validatePackageFiles(files);
      if (validation.length) throw new Error(validation.join(' '));
      const prepared = await preparePackageUpload(files);
      value.sources = { choice: fields.sources.intentIds.length ? 'Intents' : 'Selected', intentIds: fields.sources.intentIds,
        selection: { packageName: packageName.trim() || files[0]!.name, files: prepared.manifest,
          offeringHintId: fields.firstOffering.offeringId ?? null, context: null } };
    }
    const signature = JSON.stringify(['SaveDraft', state.draft?.revision ?? 0, value]);
    return execute(signature, key => api.saveDraft(state.draft?.revision ?? 0, value, key));
  };
  const saveLater = () => void act(async () => {
    if (!draft) return;
    await save(draft);
    navigate('/setup/resume');
  });
  const change = <K extends keyof SetupDraft>(field: K, value: SetupDraft[K]) => {
    setDraft(previous => previous ? { ...previous, [field]: value } : previous);
    setSaveStatus('Unsaved changes');
  };
  const go = (id: string) => { setScreen(id as ProviderScreen); setError(null); };
  const unresolved = state?.uploadIntents?.filter(item => !item.receipt) ?? [];
  const pending = intentPending.current ?? unresolved[0];
  const checkReceipt = async () => {
    if (!pending) { setSaveStatus('No unresolved upload intent is recorded.'); return; }
    const reconciliation = await api.reconcileReceipt(pending);
    const next = await api.getSetup();
    hydrate(next);
    if (reconciliation.outcome === 'Confirmed') {
      const retained = next.uploadIntents.find(item => item.intentId === pending.intentId);
      if (!reconciliation.receipt?.packageId || retained?.receipt?.packageId !== reconciliation.receipt.packageId)
        throw new Error('The reconciliation response does not match the retained request receipt. Keep the original intent and check again.');
      intentPending.current = null; setFiles([]); setScreen('p-sources'); setSaveStatus('Receipt confirmed. Review and publication remain separate.');
    } else {
      setScreen('p-uncertain'); setSaveStatus('No receipt is observed yet. This does not prove the earlier upload failed. Reselect the same files if retrying.');
    }
  };
  const upload = () => void act(async () => {
    if (!draft || !state) return;
    if (!state.handling.uploadsPermitted) throw new Error('Deployment handling policy is unknown or disallows uploads.');
    const validation = validatePackageFiles(files);
    if (validation.length) throw new Error(validation.join(' '));
    const prepared = await preparePackageUpload(files);
    let intent = pending;
    if (intent) {
      const recovered = await api.reconcileReceipt(intent);
      if (recovered.outcome === 'Confirmed') { await checkReceipt(); return; }
      if (JSON.stringify(intent.input.files) !== JSON.stringify(prepared.manifest))
        throw new Error('The selected files do not match the saved source manifest. Reselect the original files; no new package was sent.');
    } else {
      const saved = preparationRetry.current ? state : await save(draft);
      preparationRetry.current ??= { revision: saved.draft!.revision, input: {
        intentId: crypto.randomUUID(), schemaVersion: 1, packageName: packageName.trim() || files[0]!.name,
        entryPoint: 'Onboarding',
        associationMode: 'Unassociated', offeringHintId: saved.draft?.committedOfferingId ?? draft.firstOffering.offeringId ?? null,
        context: null, files: prepared.manifest, handlingPolicyVersion: saved.handling.version!,
        declaredContent: { classification, markings, containsOnlySyntheticData: synthetic },
      } };
      const preparation = preparationRetry.current;
      try { intent = await api.prepareUpload(preparation.revision, preparation.input); }
      catch (reason) {
        // No bytes have been sent. Recover the server-held attempt rather than allocating another key.
        const recovered = await api.getSetup();
        hydrate(recovered);
        const retained = recovered.uploadIntents.find(item => item.intentId === preparation.input.intentId);
        if (retained) { intentPending.current = retained; preparationRetry.current = null; setScreen('p-uncertain'); }
        throw reason;
      }
      preparationRetry.current = null;
      intentPending.current = intent;
      hydrate(await api.getSetup());
    }
    setScreen('p-uncertain');
    const receipt = await api.uploadSource(intent, prepared.files, state.profile.onboardingState === 'Active');
    if (!receipt?.packageId || !receipt.operationId) throw new Error('Receipt remains unknown. Check the existing request before retrying.');
    const refreshed = await api.getSetup();
    const retained = refreshed.uploadIntents.find(item => item.intentId === intent.intentId);
    hydrate(refreshed);
    if (!retained?.receipt || retained.receipt.packageId !== receipt.packageId)
      throw new Error('The upload response does not match the retained request receipt. Check the existing receipt before retrying.');
    intentPending.current = null; setFiles([]);
    setScreen('p-sources'); setSaveStatus('Receipt confirmed; analysis and publication have their own status.');
  });
  const next = () => void act(async () => {
    if (!draft || !state) return;
    if (screen === 'p-ready') { navigate('/setup'); return; }
    if (screen === 'p-uncertain') { await checkReceipt(); return; }
    if (screen === 'p-authorization' && draft.authorization.choice === 'Unspecified')
      throw new Error('Choose an authorization starting point.');
    const saved = await save(draft);
    if (screen === 'p-review') {
      if (!confirmed) throw new Error('Confirm the provider setup.');
      if (unresolved.length && !acknowledged) throw new Error('Acknowledge unfinished source receipt recovery.');
      const signature = JSON.stringify(['Complete', saved.draft!.revision, saved.profileRevision, unresolved.map(item => item.intentId)]);
      await execute(signature, key => api.completeSetup(saved.draft!.revision, saved.profileRevision!, unresolved.map(item => item.intentId), key));
      setScreen('p-ready'); return;
    }
    const section = screen === 'p-details' ? 'Details' : screen === 'p-access' ? 'Contacts'
      : screen === 'p-offering' ? 'FirstOffering' : screen === 'p-authorization' ? 'AuthorizationStartingPoint' : null;
    if (section && !(section === 'Details' && saved.profile.onboardingState === 'Active')) {
      const signature = JSON.stringify([section, saved.draft!.revision, saved.profileRevision]);
      await execute(signature, key => api.commitSetup(saved.draft!.revision, saved.profileRevision!, section, key, draft.firstOffering.expectedRevision));
    }
    if (screen === 'p-sources' && !['Deferred', 'Intents', 'ExistingPackage'].includes(draft.sources.choice))
      throw new Error('Add a source package or explicitly choose Skip sources for now.');
    go(steps[Math.min(steps.findIndex(item => item.id === screen) + 1, steps.length - 1)]!.id);
  });

  if (!state || !draft) return <SetupFrame journey="Provider" title="Provider setup" description="Loading authorized provider setup."
    currentStep="p-details" steps={steps} error={error} busy={!error}>
    <p>{error ? 'Setup could not be read. No saved state is inferred.' : 'Loading provider setup…'}</p>
    {error && <button className={button} onClick={() => void act(async () => hydrate(await api.getSetup(), true))}>Retry</button>}
  </SetupFrame>;
  const details = draft.details;
  return <SetupFrame journey="Provider" title={titles[screen]} description={descriptions[screen]}
    currentStep={screen === 'p-uncertain' ? 'p-sources' : screen} steps={steps} onStepChange={go}
    onSaveLater={saveLater} onChoosePath={() => navigate('/setup')} busy={busy} error={error} saveStatus={saveStatus}
    onBack={() => go(steps[Math.max(0, steps.findIndex(item => item.id === screen) - 1)]!.id)}
    primaryAction={{ label: screen === 'p-review' ? 'Finish provider setup' : screen === 'p-ready' ? 'Return to setup overview'
      : screen === 'p-uncertain' ? 'Check existing receipt' : screen === 'p-sources' ? 'Continue to setup review' : 'Save & continue',
      onClick: next, disabled: projectionUnavailable || screen === 'p-review' && !confirmed }}
    footerHelp="Saved records, source processing, human review and publication remain separate."
    guidance={<><SetupGuidance title="Use existing provider identity">Reuse this provider and its retained receipts. Adding a service never creates a new provider.</SetupGuidance>
      <SetupGuidance title="Nothing is auto-published">Source proposals remain private until explicit review, exact approval and publication. Setup grants no customer-system access.</SetupGuidance>
      <SetupGuidance title="Onboarding-only scope">This flow stops at setup confirmation. It does not open a provider or offering workspace.</SetupGuidance></>}>
    {commandRetry.current && error && <button className={button} onClick={() => void act(async () => {
      if (commandRetry.current) result(await commandRetry.current());
    })}>Retry same saved request</button>}
    {error && <button className={button} onClick={() => void act(async () => {
      const next = await api.getSetup(); command.current = null; commandRetry.current = null; hydrate(next, true);
    })}>Reload saved setup</button>}
    {screen === 'p-details' && <>
      <SetupPanel title="Provider details"><fieldset disabled={state.profile.onboardingState === 'Active'} className="grid gap-5 sm:grid-cols-2">
        <Field label="Provider display name" value={details.displayName} maxLength={64} onChange={value => change('details', { ...details, displayName: value })} />
        <Field label="Operating organization" value={details.legalEntityName} maxLength={256} onChange={value => change('details', { ...details, legalEntityName: value })} />
        <Field label="Service contact" value={details.serviceContactName ?? ''} onChange={value => change('details', { ...details, serviceContactName: value })} />
        <Field label="Contact email" value={details.serviceContactEmail} type="email" onChange={value => change('details', { ...details, serviceContactEmail: value })} />
        <label className="grid gap-1 text-sm">DoD component<select className="rounded border p-2" value={details.dodComponent ?? ''}
          onChange={event => change('details', { ...details, dodComponent: event.target.value })}>
          <option value="">Choose a DoD component</option>
          {['Department of the Navy', 'Department of the Army', 'Department of the Air Force', 'Fourth Estate'].map(value => <option key={value}>{value}</option>)}
        </select></label>
        <Field label="Timezone (IANA, for example America/New_York)" value={details.timeZoneId ?? ''}
          onChange={value => change('details', { ...details, timeZoneId: value })} />
      </fieldset>{state.profile.onboardingState === 'Active' && <p className="mt-3 text-sm">Committed provider identity is finalized. Source work remains available.</p>}</SetupPanel>
      <SetupPanel title="Deployment handling limit">
        <p>{state.handling.environmentLabel ?? 'Configured handling environment unavailable'}</p>
        <p className="mt-2">{state.handling.state === 'Known' ? `Permitted declared classifications: ${state.handling.allowedClassifications.join(', ')}`
          : 'Deployment handling policy is unknown or expired. Uploads are unavailable.'}</p>
        <p className="mt-3 text-sm">A service impact level or classification default does not change the deployment’s approved handling limits.</p>
        {state.profile.onboardingState !== 'Active' && <details className="mt-4"><summary>Existing deployment default</summary>
          <label className="mt-3 block text-sm">Legacy classification default<select className="ml-3 rounded border p-2"
            value={details.legacyClassificationDefault ?? 'Unclassified'} onChange={event => change('details', { ...details, legacyClassificationDefault: event.target.value })}>
            {['Unclassified', 'CUI', 'Secret'].map(value => <option key={value}>{value}</option>)}</select></label>
          <label className="mt-3 flex gap-2 text-sm"><input type="checkbox" checked={details.confirmLegacyClassificationDefault ?? false}
            onChange={event => change('details', { ...details, confirmLegacyClassificationDefault: event.target.checked })} />
            Confirm the existing default; this is not upload-handling permission.</label>
        </details>}
      </SetupPanel>
    </>}
    {screen === 'p-access' && <>
      <SetupPanel title="Current provider administrator"><p className="font-semibold">{state.access.actor.displayName}</p>
        <p className="mt-2">Current authenticated identity · {state.access.state} provider setup access</p>
        <p className="mt-2 text-sm">Portal administration does not include security-review, assessment, or AO decision authority. This screen grants no access.</p></SetupPanel>
      <SetupPanel title="Operational contact"><div className="grid gap-5 sm:grid-cols-2">
        <Field label="Operational contact" value={draft.operationalContact?.displayName ?? ''}
          onChange={value => change('operationalContact', { ...draft.operationalContact, choice: 'ContactOnly', displayName: value })} />
        <Field label="Operational contact email" value={draft.operationalContact?.email ?? ''} type="email"
          onChange={value => change('operationalContact', { ...draft.operationalContact, choice: 'ContactOnly', email: value })} />
      </div>
        <p className="mt-4 text-sm">A contact record does not create a directory match, membership, or role assignment.</p>
        <button className={`${button} mt-4`} onClick={() => change('operationalContact', {
          choice: 'Deferred', deferral: deferral('Operational contact will be identified later'),
        })}>Identify operational contact later</button>
        {draft.operationalContact?.choice === 'Deferred' && <p className="mt-3">Operational contact deferred.</p>}
      </SetupPanel>
      <SetupPanel title="Security review contact"><div className="grid gap-5 sm:grid-cols-2">
        <Field label="Reviewer" value={draft.securityContact.displayName ?? ''} onChange={value => change('securityContact', { ...draft.securityContact, choice: 'ContactOnly', displayName: value })} />
        <Field label="Reviewer contact email" value={draft.securityContact.email ?? ''} type="email" onChange={value => change('securityContact', { ...draft.securityContact, choice: 'ContactOnly', email: value })} />
      </div><p className="mt-4 text-sm">Contact only — no directory identity or role grant is inferred. Use the authorized role-assignment workflow for enrollment.</p>
        <button className={`${button} mt-4`} disabled aria-describedby="provider-directory-unavailable">Find in Entra</button>
        <p id="provider-directory-unavailable" className="mt-2 text-sm">Provider directory discovery is not connected to this setup workflow. Save a contact or defer; no identity match is invented.</p>
        <button className={`${button} mt-4`} onClick={() => change('securityContact', { choice: 'Deferred', deferral: deferral('Security reviewer will be identified later') })}>Identify reviewer later</button>
        {draft.securityContact.choice === 'Deferred' && <p className="mt-3">Deferred: {draft.securityContact.deferral?.reason}</p>}
      </SetupPanel>
    </>}
    {screen === 'p-offering' && <SetupPanel title="Service offering">
      <div className="grid gap-5 sm:grid-cols-2"><Field label="Offering name" value={draft.firstOffering.name ?? ''} onChange={value => change('firstOffering', { ...draft.firstOffering, choice: 'New', name: value })} />
        <Field label="Optional service portfolio" value={draft.firstOffering.portfolioName ?? ''}
          onChange={value => change('firstOffering', { ...draft.firstOffering, portfolioName: value })} />
        <label className="grid gap-1 text-sm">Environment<select className="rounded border p-2" value={draft.firstOffering.serviceDescription?.environmentKind ?? ''}
          onChange={event => {
            const selected = offeringEnvironments.find(environment => environment.id === event.target.value);
            change('firstOffering', { ...draft.firstOffering, choice: 'New',
              environments: selected ? [selected.canonical] : [],
              serviceDescription: { ...draft.firstOffering.serviceDescription, environmentKind: event.target.value } });
          }}>
          <option value="">Choose an environment</option>{offeringEnvironments
            .map(({ id, label }) => <option key={id} value={id}>{label}</option>)}</select></label>
        {draft.firstOffering.serviceDescription?.environmentKind === 'Other' && <Field label="Environment description" value={draft.firstOffering.serviceDescription.environmentLabel ?? ''}
          onChange={value => change('firstOffering', { ...draft.firstOffering, serviceDescription: { ...draft.firstOffering.serviceDescription!, environmentLabel: value } })} />}
        <label className="grid gap-1 text-sm">Service model<select className="rounded border p-2" value={draft.firstOffering.serviceDescription?.serviceModel ?? ''}
          onChange={event => change('firstOffering', { ...draft.firstOffering, serviceDescription: { environmentKind: '', ...draft.firstOffering.serviceDescription, serviceModel: event.target.value } })}>
          <option value="">Choose a model</option>{[['InfrastructureShared', 'Infrastructure + shared services'], ['Platform', 'Platform service'], ['Software', 'Software as a service'], ['BrokeredHosting', 'Brokered hosting']]
            .map(([id, label]) => <option key={id} value={id}>{label}</option>)}</select></label>
        <label className="grid gap-1 text-sm">Managed by<select className="rounded border p-2" value={draft.firstOffering.serviceDescription?.managedBy ?? ''}
          onChange={event => change('firstOffering', { ...draft.firstOffering, serviceDescription: { environmentKind: '', ...draft.firstOffering.serviceDescription, managedBy: event.target.value } })}>
          <option value="">Choose management</option>{[['Provider', 'Provider'], ['SharedOperations', 'Shared operations'], ['MissionOwner', 'Mission Owner']].map(([id, label]) => <option key={id} value={id}>{label}</option>)}</select></label>
        <Field label="Intended use" value={draft.firstOffering.serviceDescription?.intendedUse ?? ''} onChange={value => change('firstOffering', { ...draft.firstOffering,
          serviceDescription: { environmentKind: '', ...draft.firstOffering.serviceDescription, intendedUse: value } })} />
      </div><p className="mt-4 text-sm">A service name does not establish authorization. Boundary and responsibilities are reviewed later; non-Azure services are descriptive records, not Azure connections.</p>
      <details className="mt-5"><summary>Reuse an existing offering</summary><OfferingPicker selected={draft.firstOffering.offeringId ?? ''} onSelect={id => void act(async () => {
        if (!id) return; const offering = await getOffering(id); change('firstOffering', { choice: 'Existing', offeringId: id, expectedRevision: offering.revision, name: offering.name });
      })} /></details>
      <button className={`${button} mt-4`} onClick={() => change('firstOffering', { choice: 'Deferred', deferral: deferral('The first service offering will be added later') })}>I will add an offering later</button>
      {draft.firstOffering.choice === 'Deferred' && <p className="mt-3">Offering deferred.</p>}
    </SetupPanel>}
    {screen === 'p-authorization' && <>
      <div className="grid gap-4">
        {[
          ['ExistingAuthorization', 'We have an existing authorization', 'Register available facts and sources for verification without treating them as a recorded decision.'],
          ['InitialAuthorization', 'We are preparing for initial authorization', 'Create package-preparation follow-up without claiming that authorization has been issued.'],
          ['DetermineLater', 'We need to determine the authorization scope', 'Keep coverage unresolved and create follow-up work without fabricating a boundary.'],
        ].map(([value, label, detail]) => <label key={value} className={`flex cursor-pointer gap-3 rounded-xl border p-4 ${
          draft.authorization.choice === value ? 'border-indigo-500 bg-indigo-50' : 'border-slate-200 bg-white'}`}>
          <input aria-label={label} type="radio" checked={draft.authorization.choice === value}
            onChange={() => change('authorization', { choice: value as SetupDraft['authorization']['choice'] })} className="mt-1" />
          <span><strong className="block">{label}</strong><span className="mt-1 block text-sm text-slate-600">{detail}</span></span>
        </label>)}
      </div>
      {draft.authorization.choice === 'ExistingAuthorization' && <SetupPanel title="Available decision details · all optional">
        <p className="mb-5 text-sm">Unknown values may remain blank. These are unconfirmed onboarding facts awaiting source review.</p>
        <div className="grid gap-5 sm:grid-cols-2">
          <Field label="Decision reference" value={draft.authorization.decisionReference ?? ''}
            onChange={value => change('authorization', { ...draft.authorization, decisionReference: value })} />
          <Field label="Issuing AO or authority" value={draft.authorization.issuingAuthority ?? ''}
            onChange={value => change('authorization', { ...draft.authorization, issuingAuthority: value })} />
          <Field label="Decision date" type="date" value={draft.authorization.decisionDate ?? ''}
            onChange={value => change('authorization', { ...draft.authorization, decisionDate: value })} />
          <Field label="Expiration date" type="date" value={draft.authorization.expirationDate ?? ''}
            onChange={value => change('authorization', { ...draft.authorization, expirationDate: value })} />
          <Field label="System or boundary name" value={draft.authorization.systemOrBoundaryName ?? ''}
            onChange={value => change('authorization', { ...draft.authorization, systemOrBoundaryName: value })} />
          <Field label="Supporting source" value={draft.authorization.supportingSource ?? ''}
            onChange={value => change('authorization', { ...draft.authorization, supportingSource: value })} />
          <label className="grid gap-1 text-sm sm:col-span-2">Conditions<textarea className="min-h-24 rounded border p-3"
            value={draft.authorization.conditions ?? ''}
            onChange={event => change('authorization', { ...draft.authorization, conditions: event.target.value })} /></label>
        </div>
      </SetupPanel>}
    </>}
    {(screen === 'p-sources' || screen === 'p-uncertain' || screen === 'p-authorization' && draft.authorization.choice === 'ExistingAuthorization') && <>
      {screen === 'p-uncertain' && <div role="status" className="rounded-xl border border-amber-300 bg-amber-50 p-5 text-amber-950"><b>Receipt is not yet confirmed</b>
        <p className="mt-2">The service may have received the source. Check the existing request before retrying. Browser File objects do not survive restart.</p></div>}
      <SetupPanel title={screen === 'p-uncertain' ? 'Pending upload'
        : screen === 'p-authorization' ? 'Optional eMASS package intake' : 'Authorization package or service documents'}>
        {screen === 'p-authorization' && <div className="mb-5 rounded-lg border border-indigo-200 bg-indigo-50 p-4 text-sm">
          The canonical package engine will validate the permitted package, retain its receipt and provenance, inventory supported records, and produce unconfirmed candidates for human review. Upload does not verify an authorization or map coverage.
        </div>}
        {pending && <div className="mb-4 space-y-2"><p>{pending.input.packageName}</p><p>Receipt status: Unknown · Provider private</p>
          <ul>{pending.input.files.map(file => <li key={file.ordinal}>{file.fileName} · {file.byteLength} bytes</li>)}</ul></div>}
        {!pending && draft.sources.selection && <p className="mb-4 text-sm">File selection metadata is saved, not uploaded.
          Reselect {draft.sources.selection.files.map(file => file.fileName).join(', ')} to continue.</p>}
        {!pending && draft.sources.selection && <button className={`${button} mb-4`} onClick={() => {
          setFiles([]); change('sources', { choice: draft.sources.intentIds.length ? 'Intents' : 'Unspecified', intentIds: draft.sources.intentIds });
        }}>Discard unsubmitted selection</button>}
        <div className="rounded-xl border-2 border-dashed border-indigo-200 bg-indigo-50/40 p-6">
          <h3 className="text-lg font-semibold">Start with an existing package</h3><p className="mt-2 text-sm">Decision letter, service guide, responsibility matrix or supported archive.</p>
          {!pending && <div className="my-4"><Field label="Package name" value={packageName} onChange={setPackageName} /></div>}
          <label className="mt-4 block text-sm">{pending ? 'Reselect the same source files' : 'Select source files'}
            <input className="mt-2 block w-full" type="file" multiple accept={PACKAGE_FILE_ACCEPT} disabled={busy || !state.handling.uploadsPermitted}
              onChange={event => setFiles(Array.from(event.target.files ?? []))} /></label>
          {!pending && <><label className="mt-4 block text-sm">Declared source classification<select className="ml-3 rounded border p-2" value={classification} onChange={event => setClassification(event.target.value)}>
            <option value="">Choose permitted content</option>{state.handling.allowedClassifications.map(value => <option key={value}>{value}</option>)}</select></label>
            {state.handling.syntheticOnly && <label className="mt-3 flex gap-2 text-sm"><input type="checkbox" checked={synthetic} onChange={event => setSynthetic(event.target.checked)} />These files contain only synthetic data.</label>}</>}
          {!pending && state.handling.allowedMarkings.map(marking => <label key={marking} className="mt-2 flex gap-2 text-sm">
            <input type="checkbox" checked={markings.includes(marking)} onChange={event => setMarkings(previous => event.target.checked
              ? [...previous, marking] : previous.filter(value => value !== marking))} />{marking}</label>)}
          <button className={`${button} mt-4`} disabled={busy || !files.length || !state.handling.uploadsPermitted
            || !pending && (!classification || state.handling.syntheticOnly && !synthetic)}
            onClick={upload}>{pending ? 'Retry same upload' : 'Upload package'}</button>
          {!!files.length && !pending && <button className={`${button} ml-3 mt-4`} onClick={() => {
            setFiles([]); change('sources', { choice: draft.sources.intentIds.length ? 'Intents' : 'Unspecified', intentIds: draft.sources.intentIds });
          }}>Remove selected files</button>}
          {!state.handling.uploadsPermitted && <p className="mt-3 text-sm">Deployment handling permission is unavailable. Save details or defer sources; do not upload.</p>}
        </div>
        {state.uploadIntents?.filter(item => item.receipt).map(item => <div key={item.intentId} className="mt-4"><PackageReceiptCard item={item.receipt!} showLink /></div>)}
      </SetupPanel>
      {screen !== 'p-authorization' && <SetupPanel title="Review after onboarding"><p>Source claims stay private and unpublished. An offering hint does not create a boundary or association.</p>
        {screen === 'p-sources' && <div className="mt-4 rounded-lg border border-slate-200 p-4">
          <Field label="Reference an existing package" value={draft.sources.packageReference ?? ''}
            onChange={value => change('sources', { ...draft.sources, choice: value ? 'ExistingPackage' : 'Unspecified', packageReference: value })} />
          <p className="mt-2 text-sm">A reference does not import, verify, publish, or associate the package.</p>
        </div>}
        <button className={`${button} mt-4`} disabled={!!pending || !!state.uploadIntents?.length || !!files.length}
          onClick={() => change('sources', { choice: 'Deferred', intentIds: [], deferral: deferral('Source material will be added later') })}>Skip sources for now</button>
        {draft.sources.choice === 'Deferred' && <p className="mt-3">Sources deferred: {draft.sources.deferral?.reason}</p>}
        {pending && screen !== 'p-uncertain' && <button className={`${button} ml-3 mt-4`} onClick={() => void act(checkReceipt)}>Check existing receipt</button>}
      </SetupPanel>}
    </>}
    {screen === 'p-review' && <SetupPanel title="Onboarding summary"><dl className="grid gap-3 sm:grid-cols-2">
      <dt>Provider</dt><dd>{state.profile.identity?.displayName ?? 'Required profile details not yet committed'}</dd>
      <dt>Administrator</dt><dd>{state.access.actor.displayName} · {state.access.state}</dd>
      <dt>DoD component</dt><dd>{draft.details.dodComponent || 'Not yet recorded'}</dd>
      <dt>Timezone</dt><dd>{draft.details.timeZoneId || 'Not yet recorded'}</dd>
      <dt>Service portfolio</dt><dd>{draft.firstOffering.choice === 'Deferred' ? 'Deferred' : draft.firstOffering.portfolioName || 'No portfolio selected'}</dd>
      <dt>First offering</dt><dd>{draft.firstOffering.choice === 'Deferred' ? 'Deferred' : draft.firstOffering.name ?? 'Not yet recorded'}</dd>
      <dt>Authorization starting point</dt><dd>{draft.authorization.choice === 'ExistingAuthorization' ? 'Existing authorization declared; verification separate'
        : draft.authorization.choice === 'InitialAuthorization' ? 'Preparing for initial authorization'
          : draft.authorization.choice === 'DetermineLater' ? 'Authorization scope unresolved' : 'Not yet selected'}</dd>
      <dt>Source receipt</dt><dd>{unresolved.length ? 'Unknown — recovery remains open' : state.uploadIntents?.length ? 'Confirmed' : draft.sources.choice === 'Deferred' ? 'Deferred · optional' : 'Not yet recorded'}</dd>
      <dt>Source review</dt><dd>Not completed by onboarding</dd><dt>Published capabilities</dt><dd>None created by setup</dd>
    </dl><label className="mt-5 flex gap-2"><input type="checkbox" checked={confirmed} onChange={event => setConfirmed(event.target.checked)} />Confirm this provider setup.</label>
      {!!unresolved.length && <label className="mt-3 flex gap-2"><input type="checkbox" checked={acknowledged} onChange={event => setAcknowledged(event.target.checked)} />Keep unresolved source receipt recovery visible for later.</label>}
    </SetupPanel>}
    {screen === 'p-ready' && <>
      <div className="rounded-xl border border-indigo-200 bg-indigo-50 p-5"><b>{state.profile.onboardingState !== 'Active' ? 'Setup has not been finalized'
        : unresolved.length ? 'Profile ready — source receipt unresolved' : state.draft?.completion ? 'Setup complete' : 'Provider profile active'}</b>
        <p className="mt-2">This onboarding summary does not verify an authorization, approve documents, pass assessment, establish customer coverage, or connect monitoring.</p></div>
      <SetupPanel title="Recorded onboarding facts"><dl className="grid gap-3 sm:grid-cols-2">
        <dt>Provider</dt><dd>{draft.details.displayName}</dd>
        <dt>Operating organization</dt><dd>{draft.details.legalEntityName}</dd>
        <dt>Service portfolio</dt><dd>{draft.firstOffering.choice === 'Deferred' ? 'Deferred' : draft.firstOffering.portfolioName || 'No portfolio selected'}</dd>
        <dt>Offering</dt><dd>{draft.firstOffering.choice === 'Deferred' ? 'Deferred' : draft.firstOffering.name || 'Not recorded'}</dd>
        <dt>Authorization intent</dt><dd>{draft.authorization.choice}</dd>
        <dt>Available records</dt><dd>{draft.sources.choice}</dd>
      </dl></SetupPanel>
      <SetupPanel title="Outstanding onboarding work">{state.facts?.map(item => <div key={item.actionId} className="border-b py-4 last:border-0">
        <p className="font-semibold">{item.label}</p><p className="mt-1 text-sm">{item.state} · {item.ownerRole} {item.reason}</p>
      </div>)}
        {!state.facts?.length && <p className="text-sm">No deferred onboarding work is currently projected.</p>}
      </SetupPanel>
      <Link className="inline-flex rounded-lg bg-indigo-600 px-5 py-3 text-sm font-semibold text-white"
        to="/workspaces/csp/authorizations">Open provider workspace</Link>
    </>}
  </SetupFrame>;
}
