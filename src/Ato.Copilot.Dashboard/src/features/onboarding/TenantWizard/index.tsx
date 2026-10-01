import { useEffect, useRef, useState } from 'react';
import { useNavigate } from '../../workspaces/workspaceNavigation';
import { useWorkspaceSession } from '../../workspaces/WorkspaceBoundary';
import SetupFrame, { SetupGuidance, SetupPanel } from '../shared/SetupFrame';
import { tenantWizard, type TenantDraftValues, type TenantOnboardingProgress, type TenantWizardStep } from './api';
import { TenantDraftContext } from './draftContext';
import LegalEntityStep from './steps/LegalEntityStep';
import HqAddressStep from './steps/HqAddressStep';
import ClassificationStep from './steps/ClassificationStep';
import AoStep from './steps/AoStep';
import PrimaryPocStep from './steps/PrimaryPocStep';
import OrgProfileStep from './steps/OrgProfileStep';
import ReviewStep from './steps/ReviewStep';

const steps: { id: TenantWizardStep; label: string; slice?: keyof TenantDraftValues }[] = [
  { id: 'Tenant.LegalEntity', label: 'Legal entity', slice: 'legalEntity' },
  { id: 'Tenant.HqAddress', label: 'Headquarters address', slice: 'hqAddress' },
  { id: 'Tenant.Classification', label: 'Default classification', slice: 'classification' },
  { id: 'Tenant.Ao', label: 'Authorizing Official', slice: 'ao' },
  { id: 'Tenant.PrimaryPoc', label: 'Primary POC', slice: 'primaryPoc' },
  { id: 'Org.Profile', label: 'First organization', slice: 'orgProfile' },
  { id: 'Submitted', label: 'Review & submit' },
];
const empty: TenantDraftValues = {
  legalEntity: { legalEntityName: '', doDComponent: '', timeZone: '' },
  hqAddress: { hqAddressLine1: '', hqAddressLine2: '', hqCity: '', hqStateOrProvince: '', hqPostalCode: '', hqCountry: '' },
  classification: { defaultClassificationLevel: 'Unclassified' },
  ao: { authorizingOfficialName: '', authorizingOfficialEmail: '' },
  primaryPoc: { primaryPocName: '', primaryPocEmail: '', primaryPocPhone: '' },
  orgProfile: { name: '', description: '' },
};
function comparable(value: unknown): string {
  return JSON.stringify(value, (key, entry: unknown) => {
    if (key === 'expectedRevision') return undefined;
    if (entry === null) return '';
    if (typeof entry === 'string') return entry.trim();
    if (typeof entry === 'object' && !Array.isArray(entry))
      return Object.fromEntries(Object.entries(entry).sort(([first], [second]) => first.localeCompare(second)));
    return entry;
  });
}

export default function TenantWizard() {
  const navigate = useNavigate();
  const session = useWorkspaceSession();
  const canEdit = !session || session.target.kind === 'organization'
    && session.target.mode !== 'support' && session.workspace.permissions.canManageOrganization;
  const [progress, setProgress] = useState<TenantOnboardingProgress | null>(null);
  const [values, setValues] = useState<TenantDraftValues>(empty);
  const [step, setStep] = useState<TenantWizardStep>('Tenant.LegalEntity');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState('');
  const alive = useRef(true);
  const initiallyActive = useRef(false);
  const form = useRef<HTMLDivElement>(null);
  useEffect(() => {
    alive.current = true;
    const controller = new AbortController();
    tenantWizard.getState(controller.signal).then(state => {
      if (!alive.current || controller.signal.aborted) return;
      setProgress(state);
      initiallyActive.current = state.onboardingState === 'Active';
      const submitted = state.submittedValues;
      if (!submitted) {
        setError('Saved tenant values are unavailable. This server must support tenant draft hydration before editing.');
        return;
      }
      setValues(Object.fromEntries(Object.keys(empty).map(key => {
        const slice = key as keyof TenantDraftValues;
        return [slice, { ...empty[slice], ...submitted[slice], ...state.draft?.values[slice] }];
      })) as unknown as TenantDraftValues);
      setStep(state.draft?.currentStep ?? state.currentStep);
      if (state.draft) setSaved('Saved draft restored. Apply each changed step before activating.');
    }).catch(reason => { if (alive.current && !controller.signal.aborted) setError((reason as Error).message); });
    return () => { alive.current = false; controller.abort(); };
  }, []);
  const saveLater = async () => {
    if (busy || !progress?.submittedValues || !canEdit) return;
    setBusy(true); setError(null);
    try {
      const next = await tenantWizard.saveDraft({ schemaVersion: 1, expectedRevision: progress.draftRevision ?? 0, currentStep: step, values });
      if (!alive.current) return;
      setProgress(next); setSaved('Draft saved.');
      navigate('/setup/resume');
    } catch (reason) { if (alive.current) setError((reason as Error).message); }
    finally { if (alive.current) setBusy(false); }
  };
  const onAdvance = (next: TenantOnboardingProgress) => {
    if (!alive.current) return;
    setProgress(next); setBusy(false); setError(null); setSaved('Step applied to the tenant record.');
    const index = steps.findIndex(item => item.id === step);
    const slice = steps[index]?.slice;
    if (slice && next.submittedValues) setValues(current => ({ ...current, [slice]: next.submittedValues![slice] }));
    if (step === 'Submitted' && next.onboardingState === 'Active' && !initiallyActive.current) navigate('/', { replace: true });
    else if (steps[index + 1]) setStep(steps[index + 1]!.id);
  };
  const props = {
    busy, beforeSubmit: () => { setBusy(true); setError(null); },
    onAdvance, onError: (message: string) => { setError(message); setBusy(false); },
  };
  const current = steps.find(item => item.id === step)!;
  return <SetupFrame journey="Tenant" title={step === 'Submitted' ? 'Review tenant activation' : current.label}
    description="Retain tenant documentation once. Activation, administrator access, and system authorization remain separate."
    currentStep={step} steps={steps} busy={busy} error={!canEdit ? 'Tenant activation requires an ordinary organization administrator workspace.' : error} saveStatus={saved}
    onStepChange={canEdit ? id => setStep(id as TenantWizardStep) : undefined} onSaveLater={canEdit ? () => void saveLater() : undefined}
    onBack={step === steps[0]!.id ? undefined : () => setStep(steps[Math.max(0, steps.findIndex(item => item.id === step) - 1)]!.id)}
    primaryAction={canEdit && step !== 'Submitted' ? { label: 'Apply step & continue', disabled: !progress?.submittedValues,
      onClick: () => form.current?.querySelector('form')?.requestSubmit() } : undefined}
    guidance={<><SetupGuidance title="Save without activating">Save &amp; finish later retains partial fields. It never grants membership or completes activation.</SetupGuidance>
      <SetupGuidance title="Deployment policy">Tenant markings do not establish a trusted deployment handling limit. Deployment readiness remains a separate prerequisite.</SetupGuidance></>}
    footerHelp="Only explicitly applied tenant facts are used for activation.">
    {!progress ? <p role="status">Loading saved tenant information…</p> : canEdit && progress.submittedValues && <TenantDraftContext.Provider
      value={{ values, revision: progress.draftRevision ?? 0, change: (slice, field, value) => {
        setValues(currentValues => ({ ...currentValues, [slice]: { ...currentValues[slice], [field]: value } }));
        setSaved('Unsaved edits');
      } }}>
      <div ref={form} className="[&_form>button[type=submit]]:sr-only">
        {step === 'Tenant.LegalEntity' && <LegalEntityStep {...props} />}
        {step === 'Tenant.HqAddress' && <HqAddressStep {...props} />}
        {step === 'Tenant.Classification' && <ClassificationStep {...props} />}
        {step === 'Tenant.Ao' && <AoStep {...props} />}
        {step === 'Tenant.PrimaryPoc' && <PrimaryPocStep {...props} />}
        {step === 'Org.Profile' && <OrgProfileStep {...props} />}
        {step === 'Submitted' && <><SetupPanel title="Applied tenant facts">
          <dl className="space-y-2 text-sm">
            <div><dt>Legal entity</dt><dd>{progress.submittedValues.legalEntity.legalEntityName || 'Not recorded'}</dd></div>
            <div><dt>Classification</dt><dd>{progress.submittedValues.classification.defaultClassificationLevel}</dd></div>
            <div><dt>First organization profile</dt><dd>{progress.submittedValues.orgProfile.name || 'Not recorded'}</dd></div>
          </dl>
          {!!progress.missingRequiredFields?.length && <p role="status" className="mt-3 text-amber-800">Required fields remaining: {progress.missingRequiredFields.join(', ')}</p>}
        </SetupPanel><ReviewStep {...props} blocked={!!progress.missingRequiredFields?.length
          || comparable(values) !== comparable(progress.submittedValues)} /></>}
      </div>
    </TenantDraftContext.Provider>}
  </SetupFrame>;
}
