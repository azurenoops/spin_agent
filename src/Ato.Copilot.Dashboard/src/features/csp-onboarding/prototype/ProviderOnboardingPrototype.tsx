import { useEffect, useState, type ReactNode } from 'react';
import SetupFrame, { SetupGuidance, SetupPanel } from '../../onboarding/shared/SetupFrame';
import {
  ProviderBadge,
  ProviderFact,
  ProviderPanel,
} from '../../provider-authorizations/ProviderPresentation';
import '../../provider-authorizations/providerPresentation.css';

export const PROTOTYPE_STORAGE_KEY = 'spin-provider-onboarding-prototype-v1';

type Scenario = 'new-admin' | 'resume-admin' | 'invited-user' | 'active-user' | 'unauthorized';
type View = 'entry' | 'wizard' | 'saved' | 'invitation' | 'invitation-confirmed' | 'access-required' | 'active-member' | 'complete';
type AuthorizationStart = 'existing' | 'initial' | 'determine-later' | '';
type RecordsChoice = 'upload' | 'existing-package' | 'deferred' | '';
type UploadPhase = 'none' | 'uncertain' | 'received' | 'processing' | 'awaiting-review' | 'verified';

interface PrototypeState {
  scenario: Scenario;
  view: View;
  step: number;
  organization: string;
  displayName: string;
  dodComponent: string;
  timezone: string;
  contactName: string;
  contactEmail: string;
  operationalContact: string;
  securityContact: string;
  contactsDeferred: boolean;
  offeringDeferred: boolean;
  offeringName: string;
  portfolioName: string;
  serviceModel: string;
  environment: string;
  authorizationStart: AuthorizationStart;
  emassPackageName: string;
  emassAnalysisComplete: boolean;
  decisionReference: string;
  issuingAo: string;
  decisionDate: string;
  expirationDate: string;
  conditions: string;
  boundaryName: string;
  authorizationCoverageMapped: boolean;
  supportingSource: string;
  recordsChoice: RecordsChoice;
  selectedFileName: string;
  uploadPhase: UploadPhase;
  receiptCount: number;
  packageReference: string;
  setupComplete: boolean;
  accessRequestSubmitted: boolean;
  failNextSave: boolean;
  feedback: string;
}

const steps = [
  { id: 'identity', label: 'Confirm provider identity' },
  { id: 'access', label: 'Confirm access and contacts' },
  { id: 'offering', label: 'Add the first offering' },
  { id: 'authorization', label: 'Choose authorization starting point' },
  { id: 'records', label: 'Add available records' },
  { id: 'review', label: 'Review and finish setup' },
] as const;

const defaultState: PrototypeState = {
  scenario: 'new-admin',
  view: 'entry',
  step: 0,
  organization: 'PEO Digital',
  displayName: 'PEO Digital Provider workspace',
  dodComponent: 'Department of the Navy',
  timezone: 'America/New_York',
  contactName: 'Alex Morgan',
  contactEmail: 'alex.morgan@example.invalid',
  operationalContact: 'Flank Speed Service Operations',
  securityContact: '',
  contactsDeferred: false,
  offeringDeferred: false,
  offeringName: 'Flank Speed Azure',
  portfolioName: 'Flank Speed',
  serviceModel: 'Platform service',
  environment: 'Azure Government',
  authorizationStart: '',
  emassPackageName: '',
  emassAnalysisComplete: false,
  decisionReference: '',
  issuingAo: '',
  decisionDate: '',
  expirationDate: '',
  conditions: '',
  boundaryName: '',
  authorizationCoverageMapped: false,
  supportingSource: '',
  recordsChoice: '',
  selectedFileName: '',
  uploadPhase: 'none',
  receiptCount: 0,
  packageReference: '',
  setupComplete: false,
  accessRequestSubmitted: false,
  failNextSave: false,
  feedback: '',
};

const scenarioOptions: Array<{ id: Scenario; label: string; detail: string }> = [
  { id: 'new-admin', label: 'New authorized provider administrator', detail: 'Starts one-time provider registration.' },
  { id: 'resume-admin', label: 'Administrator resuming unfinished setup', detail: 'Resumes the saved provider draft.' },
  { id: 'invited-user', label: 'Invited user joining an existing provider', detail: 'Confirms membership and scoped access.' },
  { id: 'active-user', label: 'Existing user with an active provider', detail: 'Opens assigned provider work.' },
  { id: 'unauthorized', label: 'User without authorized membership', detail: 'Uses the supported access-request path.' },
];

function storedState(): PrototypeState {
  try {
    const stored = localStorage.getItem(PROTOTYPE_STORAGE_KEY);
    return stored ? { ...defaultState, ...JSON.parse(stored) as Partial<PrototypeState> } : defaultState;
  } catch {
    return defaultState;
  }
}

function persist(state: PrototypeState) {
  localStorage.setItem(PROTOTYPE_STORAGE_KEY, JSON.stringify(state));
}

function Field({ label, value, onChange, required, type = 'text', placeholder }: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  required?: boolean;
  type?: string;
  placeholder?: string;
}) {
  return <label className="grid gap-1.5 text-sm font-medium text-slate-700">
    {label}
    <input type={type} value={value} required={required} placeholder={placeholder}
      onChange={event => onChange(event.target.value)}
      className="rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-slate-900 shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200" />
  </label>;
}

function SelectField({ label, value, onChange, children }: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  children: ReactNode;
}) {
  return <label className="grid gap-1.5 text-sm font-medium text-slate-700">{label}
    <select value={value} onChange={event => onChange(event.target.value)}
      className="rounded-lg border border-slate-300 bg-white px-3 py-2.5 text-slate-900 shadow-sm focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-200">
      {children}
    </select>
  </label>;
}

function ChoiceCard({ checked, onChange, label, detail }: {
  checked: boolean;
  onChange: () => void;
  label: string;
  detail: string;
}) {
  return <label className={`flex cursor-pointer gap-3 rounded-xl border p-4 ${checked ? 'border-indigo-500 bg-indigo-50' : 'border-slate-200 bg-white'}`}>
    <input type="radio" checked={checked} onChange={onChange} className="mt-1 h-4 w-4 accent-indigo-600" />
    <span><span className="block font-semibold text-slate-900">{label}</span>
      <span className="mt-1 block text-sm leading-6 text-slate-600">{detail}</span></span>
  </label>;
}

function PrototypeBar({ state, onScenario, onReset, onFailNext }: {
  state: PrototypeState;
  onScenario: (scenario: Scenario) => void;
  onReset: () => void;
  onFailNext: () => void;
}) {
  return <section aria-label="Prototype controls" className="border-b border-amber-300 bg-amber-50 px-4 py-3 text-slate-900">
    <div className="mx-auto flex max-w-[1480px] flex-wrap items-center gap-3">
      <div className="mr-auto">
        <p className="text-xs font-bold uppercase tracking-widest text-amber-800">Interactive prototype · synthetic data only</p>
        <p className="text-xs text-amber-900">No records, access grants, invitations, uploads, cloud connections, or authorization decisions leave this browser.</p>
      </div>
      <button type="button" onClick={onFailNext}
        className="rounded border border-amber-500 bg-white px-3 py-1.5 text-xs font-semibold">
        {state.failNextSave ? 'Next save will fail' : 'Simulate next save error'}
      </button>
      <button type="button" onClick={onReset}
        className="rounded border border-amber-500 bg-white px-3 py-1.5 text-xs font-semibold">Reset prototype</button>
    </div>
    <details open className="mx-auto mt-2 max-w-[1480px]">
      <summary className="cursor-pointer text-xs font-semibold text-amber-900">Prototype scenario switcher</summary>
      <div className="mt-2 flex flex-wrap gap-2">
        {scenarioOptions.map(option => <button type="button" key={option.id} onClick={() => onScenario(option.id)}
          aria-label={option.label}
          aria-pressed={state.scenario === option.id}
          className={`rounded-lg border px-3 py-2 text-left text-xs ${state.scenario === option.id
            ? 'border-indigo-500 bg-indigo-600 text-white' : 'border-amber-300 bg-white text-slate-800'}`}>
          <span className="block font-semibold">{option.label}</span><span className="block opacity-80">{option.detail}</span>
        </button>)}
      </div>
    </details>
  </section>;
}

export default function ProviderOnboardingPrototype() {
  const [state, setState] = useState<PrototypeState>(storedState);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => persist(state), [state]);

  const update = <K extends keyof PrototypeState>(key: K, value: PrototypeState[K]) =>
    setState(previous => ({ ...previous, [key]: value, feedback: '' }));

  const chooseScenario = (scenario: Scenario) => {
    setError(null);
    setState(previous => {
      const next = { ...previous, scenario, feedback: '' };
      if (scenario === 'invited-user') return { ...next, view: 'invitation' };
      if (scenario === 'active-user') return { ...next, view: 'active-member', setupComplete: true };
      if (scenario === 'unauthorized') return { ...next, view: 'access-required' };
      return { ...next, view: 'entry' };
    });
  };

  const reset = () => {
    localStorage.removeItem(PROTOTYPE_STORAGE_KEY);
    setState(defaultState);
    setError(null);
  };

  const failOr = (action: () => void) => {
    if (state.failNextSave) {
      setState(previous => ({ ...previous, failNextSave: false }));
      setError('Synthetic save failed. Entered values remain in this browser; retry the same action.');
      return;
    }
    setError(null);
    action();
  };

  return <div className="min-h-screen bg-slate-50 text-slate-900">
    <PrototypeBar state={state} onScenario={chooseScenario}
      onReset={reset} onFailNext={() => update('failNextSave', !state.failNextSave)} />
    {state.view === 'entry' && <EntryScreen state={state} onStart={() => setState(previous => ({
      ...previous,
      view: 'wizard',
      step: state.scenario === 'resume-admin' ? previous.step : 0,
      feedback: state.scenario === 'resume-admin' ? 'Saved provider setup restored from this browser.' : '',
    }))} />}
    {state.view === 'saved' && <SavedScreen state={state} onResume={() => update('view', 'wizard')} />}
    {state.view === 'invitation' && <InvitationScreen onConfirm={() => setState(previous => ({
      ...previous, view: 'invitation-confirmed',
      feedback: 'Synthetic membership confirmed. No provider registration was required.',
    }))} />}
    {state.view === 'invitation-confirmed' && <InvitationConfirmed state={state} />}
    {state.view === 'active-member' && <ActiveMemberStatus />}
    {state.view === 'access-required' && <AccessRequired state={state}
      onSubmit={() => setState(previous => ({ ...previous, accessRequestSubmitted: true, feedback: 'Synthetic access request submitted for administrator review.' }))} />}
    {state.view === 'wizard' && <Wizard state={state} update={update} error={error} setError={setError}
      failOr={failOr}
      onSaveLater={() => failOr(() => setState(previous => ({ ...previous, view: 'saved', feedback: 'Draft saved in isolated browser storage.' })))}
      onComplete={() => failOr(() => setState(previous => ({
        ...previous,
        setupComplete: true,
        view: 'complete',
        feedback: 'Provider setup completed. No workspace was opened by this onboarding-only prototype.',
      })))} />}
    {state.view === 'complete' && <CompletionScreen state={state} />}
  </div>;
}

function EntryScreen({ state, onStart }: { state: PrototypeState; onStart: () => void }) {
  const resume = state.scenario === 'resume-admin';
  return <main className="mx-auto max-w-5xl px-5 py-12">
    <p className="text-xs font-semibold uppercase tracking-widest text-indigo-700">Provider workspace first login</p>
    <h1 className="mt-3 text-3xl font-semibold">{resume ? 'Resume PEO Digital provider setup' : 'Set up the PEO Digital Provider workspace'}</h1>
    <p className="mt-3 max-w-3xl text-slate-600">{resume
      ? 'A server-style synthetic draft is available. Continue with the same provider, offering, and saved revision.'
      : 'You are acting as a pre-authorized portal administrator. This one-time setup establishes a usable provider workspace and its first offering.'}</p>
    <div className="mt-6 rounded-xl border border-indigo-200 bg-indigo-50 p-4 text-sm text-indigo-950">
      <strong>Updated onboarding demo:</strong> Stage 4 now accepts a synthetic eMASS package and shows what the package-analysis engine finds, proposes, and leaves unresolved.
    </div>
    <section className="mt-8 grid gap-5 md:grid-cols-3">
      <SetupPanel title="Verified simulated access">
        <p className="font-semibold">Portal administrator</p>
        <p className="mt-2 text-sm text-slate-600">Provider setup and access administration only. No AO or security-review authority.</p>
      </SetupPanel>
      <SetupPanel title="What setup creates">
        <p className="text-sm text-slate-600">Provider identity, optional Flank Speed portfolio, Flank Speed Azure offering, onboarding intent, and a truthful work queue.</p>
      </SetupPanel>
      <SetupPanel title="What setup does not do">
        <p className="text-sm text-slate-600">It does not issue or verify an authorization, approve evidence, assess controls, cover customer systems, or connect monitoring.</p>
      </SetupPanel>
    </section>
    <button type="button" onClick={onStart}
      className="mt-8 rounded-lg bg-indigo-600 px-5 py-3 text-sm font-semibold text-white hover:bg-indigo-700">
      {resume ? 'Resume saved setup' : 'Start provider setup'}
    </button>
  </main>;
}

function SavedScreen({ state, onResume }: { state: PrototypeState; onResume: () => void }) {
  return <main className="mx-auto max-w-2xl px-5 py-16 text-center">
    <ProviderBadge tone="success">Saved locally</ProviderBadge>
    <h1 className="mt-5 text-3xl font-semibold">Setup saved</h1>
    <p className="mt-3 text-slate-600">Your synthetic draft is stored only in this browser at step {state.step + 1}. No production record was changed.</p>
    <button type="button" onClick={onResume} className="mt-7 rounded-lg bg-indigo-600 px-5 py-3 text-sm font-semibold text-white">Resume saved setup</button>
  </main>;
}

function InvitationScreen({ onConfirm }: { onConfirm: () => void }) {
  return <main className="mx-auto max-w-3xl px-5 py-12">
    <p className="text-xs font-semibold uppercase tracking-widest text-indigo-700">Provider invitation</p>
    <h1 className="mt-3 text-3xl font-semibold">Confirm your invitation</h1>
    <p className="mt-3 text-slate-600">PEO Digital already has an active Provider workspace. Confirm the simulated membership and assigned access; do not register the provider again.</p>
    <div className="mt-8 provider-workspace"><ProviderPanel title="Invitation details">
      <dl>
        <ProviderFact label="Authenticated identity">Jamie Lee · jamie.lee@example.invalid</ProviderFact>
        <ProviderFact label="Provider workspace">PEO Digital</ProviderFact>
        <ProviderFact label="Assigned offering">Flank Speed Azure</ProviderFact>
        <ProviderFact label="Assigned role">ISSO</ProviderFact>
        <ProviderFact label="Access">Documentation, evidence, changes, and remediation coordination</ProviderFact>
      </dl>
      <p className="mt-5 rounded-lg bg-slate-50 p-4 text-sm text-slate-600">Invitation, identity confirmation, membership, and role assignment are shown separately. This prototype grants none of them.</p>
    </ProviderPanel></div>
    <button type="button" onClick={onConfirm} className="mt-7 rounded-lg bg-indigo-600 px-5 py-3 text-sm font-semibold text-white">
      Confirm membership and continue
    </button>
  </main>;
}

function InvitationConfirmed({ state }: { state: PrototypeState }) {
  return <main className="mx-auto max-w-3xl px-5 py-16">
    <ProviderBadge tone="success">Onboarding confirmed</ProviderBadge>
    <h1 className="mt-5 text-3xl font-semibold">Membership confirmed</h1>
    <p className="mt-3 text-slate-600">Provider registration was not repeated. Jamie Lee retains the simulated ISSO assignment for Flank Speed Azure.</p>
    <div role="status" className="mt-7 rounded-xl border border-emerald-200 bg-emerald-50 p-5 text-emerald-950">
      {state.feedback}
    </div>
    <p className="mt-5 text-sm text-slate-600">This onboarding prototype stops before opening the assigned workspace.</p>
  </main>;
}

function ActiveMemberStatus() {
  return <main className="mx-auto max-w-3xl px-5 py-16">
    <ProviderBadge tone="success">No onboarding required</ProviderBadge>
    <h1 className="mt-5 text-3xl font-semibold">Provider onboarding already complete</h1>
    <p className="mt-3 text-slate-600">This existing member already has an active PEO Digital membership and should not repeat provider registration.</p>
    <div className="mt-7 rounded-xl border border-slate-200 bg-white p-5 text-sm text-slate-700">
      Assigned access: Flank Speed Azure readiness work. This onboarding prototype stops here.
    </div>
  </main>;
}

function AccessRequired({ state, onSubmit }: { state: PrototypeState; onSubmit: () => void }) {
  return <main className="mx-auto max-w-xl px-5 py-16">
    <h1 className="text-3xl font-semibold">Provider workspace access required</h1>
    <p className="mt-3 text-slate-600">This simulated identity has no active provider membership. No provider data was loaded and provider setup is not available.</p>
    {!state.accessRequestSubmitted ? <section className="mt-7 rounded-xl border border-slate-200 bg-white p-6">
      <h2 className="font-semibold">Request supported access</h2>
      <label className="mt-4 grid gap-2 text-sm font-medium">Reason for access
        <textarea defaultValue="Support Flank Speed Azure documentation and evidence coordination."
          className="min-h-28 rounded-lg border border-slate-300 p-3" />
      </label>
      <button type="button" onClick={onSubmit} className="mt-5 rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white">Submit synthetic access request</button>
    </section> : <div role="status" className="mt-7 rounded-xl border border-emerald-200 bg-emerald-50 p-5 text-emerald-900">
      Access request pending · An authorized administrator must review membership and scoped roles.
    </div>}
  </main>;
}

function Wizard({ state, update, error, setError, failOr, onSaveLater, onComplete }: {
  state: PrototypeState;
  update: <K extends keyof PrototypeState>(key: K, value: PrototypeState[K]) => void;
  error: string | null;
  setError: (value: string | null) => void;
  failOr: (action: () => void) => void;
  onSaveLater: () => void;
  onComplete: () => void;
}) {
  const titles = [
    'Confirm provider identity',
    'Confirm access and contacts',
    'Add the first offering',
    'Choose authorization starting point',
    'Add available records',
    'Review and finish setup',
  ];
  const descriptions = [
    'Confirm the operating organization and the name people will see in the Provider workspace.',
    'Review actual simulated access and record contacts separately from membership and role grants.',
    'Create or select the first offering and optionally place it in a service portfolio.',
    'Record onboarding intent without claiming an authorization or inventing a boundary.',
    'Retain a permitted source, reference a package, or explicitly defer. Receipt, processing, and review stay separate.',
    'Review saved facts and unresolved work before completing onboarding.',
  ];

  const validate = () => {
    if (state.step === 5) {
      if (!state.organization.trim() || !state.displayName.trim() || !state.dodComponent || !state.timezone || !state.contactEmail.includes('@'))
        return 'Return to provider identity and complete the required fields.';
      if (!state.contactsDeferred && (!state.operationalContact.trim() || !state.securityContact.trim()))
        return 'Return to access and contacts; record both contacts or explicitly defer them.';
      if (!state.offeringDeferred && (!state.offeringName.trim() || !state.serviceModel || !state.environment))
        return 'Return to the first offering and complete it or explicitly defer it.';
      if (!state.authorizationStart) return 'Return to authorization starting point and choose one option.';
      if (!state.recordsChoice) return 'Return to available records and choose an option or explicitly defer.';
      if (state.uploadPhase === 'uncertain') return 'Reconcile the original receipt before completing setup.';
    }
    if (state.step === 0 && !state.organization.trim()) return 'Operating organization is required.';
    if (state.step === 0 && !state.displayName.trim()) return 'Provider workspace display name is required.';
    if (state.step === 0 && !state.dodComponent) return 'Choose a DoD component.';
    if (state.step === 0 && !state.timezone) return 'Choose a timezone.';
    if (state.step === 0 && !state.contactEmail.includes('@')) return 'Enter a valid service contact email.';
    if (state.step === 1 && !state.contactsDeferred && (!state.operationalContact.trim() || !state.securityContact.trim()))
      return 'Record both contacts or explicitly defer contact completion.';
    if (state.step === 2 && !state.offeringDeferred && (!state.offeringName.trim() || !state.serviceModel || !state.environment))
      return 'Complete the offering identity or choose Add an offering later.';
    if (state.step === 3 && !state.authorizationStart) return 'Choose an authorization starting point.';
    if (state.step === 4 && !state.recordsChoice) return 'Choose a source option or explicitly defer records.';
    if (state.step === 4 && state.uploadPhase === 'uncertain') return 'Reconcile the original receipt before continuing.';
    return null;
  };

  const next = () => {
    const validation = validate();
    if (validation) {
      setError(validation);
      return;
    }
    failOr(() => {
      if (state.step === steps.length - 1) onComplete();
      else update('step', state.step + 1);
    });
  };

  const statuses = steps.map((step, index) => ({
    ...step,
    status: index < state.step ? 'saved' as const
      : index === 1 && state.contactsDeferred || index === 2 && state.offeringDeferred
        || index === 4 && state.recordsChoice === 'deferred' ? 'deferred' as const
        : index === 4 && state.uploadPhase === 'uncertain' ? 'needsAttention' as const : undefined,
  }));

  return <SetupFrame journey="Provider" title={titles[state.step]!} description={descriptions[state.step]!}
    currentStep={steps[state.step]!.id} steps={statuses}
    onStepChange={id => {
      setError(null);
      update('step', steps.findIndex(step => step.id === id));
    }}
    onSaveLater={onSaveLater} onBack={state.step > 0 ? () => {
      setError(null);
      update('step', state.step - 1);
    } : undefined}
    error={error} saveStatus={state.feedback}
    primaryAction={{ label: state.step === 5 ? 'Finish provider setup' : 'Save & continue', onClick: next }}
    footerHelp="Synthetic setup completion remains separate from document review, assessment, authorization, and monitoring."
    stepLabel={`Step ${state.step + 1} of 6`}
    guidance={<>
      <SetupGuidance title="Synthetic prototype">All names, records, decisions, uploads, and access are simulated in this browser.</SetupGuidance>
      <SetupGuidance title="Truthful completion">Onboarding can finish with unresolved authorization scope, deferred records, and unassigned work.</SetupGuidance>
      <SetupGuidance title="Onboarding-only scope">This prototype stops after setup confirmation and does not open a provider or offering workspace.</SetupGuidance>
    </>}>
    {state.step === 0 && <IdentityStep state={state} update={update} />}
    {state.step === 1 && <AccessStep state={state} update={update} />}
    {state.step === 2 && <OfferingStep state={state} update={update} />}
    {state.step === 3 && <AuthorizationStep state={state} update={update} />}
    {state.step === 4 && <RecordsStep state={state} update={update} />}
    {state.step === 5 && <ReviewStep state={state} />}
  </SetupFrame>;
}

function IdentityStep({ state, update }: StepProps) {
  return <SetupPanel title="Provider identity">
    <div className="grid gap-5 sm:grid-cols-2">
      <Field label="Operating organization" required value={state.organization} onChange={value => update('organization', value)} />
      <Field label="Provider workspace display name" required value={state.displayName} onChange={value => update('displayName', value)} />
      <SelectField label="DoD component" value={state.dodComponent} onChange={value => update('dodComponent', value)}>
        <option value="">Choose a component</option><option>Department of the Navy</option><option>Department of the Army</option><option>Department of the Air Force</option><option>Fourth Estate</option>
      </SelectField>
      <SelectField label="Timezone" value={state.timezone} onChange={value => update('timezone', value)}>
        <option value="">Choose a timezone</option><option value="America/New_York">Eastern Time</option><option value="America/Chicago">Central Time</option><option value="America/Denver">Mountain Time</option><option value="America/Los_Angeles">Pacific Time</option>
      </SelectField>
      <Field label="Service contact name" required value={state.contactName} onChange={value => update('contactName', value)} />
      <Field label="Service contact email" required type="email" value={state.contactEmail} onChange={value => update('contactEmail', value)} />
    </div>
  </SetupPanel>;
}

interface StepProps {
  state: PrototypeState;
  update: <K extends keyof PrototypeState>(key: K, value: PrototypeState[K]) => void;
}

function AccessStep({ state, update }: StepProps) {
  return <>
    <SetupPanel title="Actual simulated administrator access">
      <dl className="grid gap-3 text-sm sm:grid-cols-2">
        <dt className="text-slate-500">Authenticated identity</dt><dd className="font-semibold">Taylor Jordan</dd>
        <dt className="text-slate-500">Membership</dt><dd>PEO Digital Provider workspace administrator</dd>
        <dt className="text-slate-500">Allowed</dt><dd>Setup, offerings, invitations, and access administration</dd>
        <dt className="text-slate-500">Not allowed</dt><dd>AO decisions, security review, or assessment disposition</dd>
      </dl>
    </SetupPanel>
    <SetupPanel title="Operational and security contacts">
      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="Operational contact" value={state.operationalContact} onChange={value => {
          update('operationalContact', value);
          update('contactsDeferred', false);
        }} />
        <Field label="Security contact" value={state.securityContact} placeholder="Name or team"
          onChange={value => {
            update('securityContact', value);
            update('contactsDeferred', false);
          }} />
      </div>
      <p className="mt-4 text-sm text-slate-600">Recording a contact grants no membership, role, security-review authority, or AO authority.</p>
      <label className="mt-4 flex gap-2 text-sm">
        <input type="checkbox" checked={state.contactsDeferred} onChange={event => update('contactsDeferred', event.target.checked)} />
        Defer incomplete contact details and create follow-up work
      </label>
    </SetupPanel>
  </>;
}

function OfferingStep({ state, update }: StepProps) {
  return <SetupPanel title="First offering">
    <label className="mb-5 flex gap-2 rounded-lg bg-slate-50 p-4 text-sm">
      <input type="checkbox" checked={state.offeringDeferred} onChange={event => update('offeringDeferred', event.target.checked)} />
      <span><strong>Add an offering later</strong><span className="mt-1 block text-slate-600">Complete onboarding with an accountable offering follow-up item.</span></span>
    </label>
    <fieldset disabled={state.offeringDeferred} className="grid gap-5 disabled:opacity-50 sm:grid-cols-2">
      <Field label="Offering name" value={state.offeringName} onChange={value => update('offeringName', value)} />
      <Field label="Optional service portfolio" value={state.portfolioName} onChange={value => update('portfolioName', value)} />
      <SelectField label="Service model" value={state.serviceModel} onChange={value => update('serviceModel', value)}>
        <option value="">Choose a service model</option><option>Platform service</option><option>Software as a service</option><option>Infrastructure + shared services</option><option>Brokered hosting</option>
      </SelectField>
      <SelectField label="Environment" value={state.environment} onChange={value => update('environment', value)}>
        <option value="">Choose an environment</option><option>Azure Government</option><option>Azure Commercial</option><option>AWS GovCloud</option><option>Microsoft 365 DoD</option><option>Other / manually described</option>
      </SelectField>
    </fieldset>
    <p className="mt-4 text-sm text-slate-600">An offering and a cloud subscription are not automatically an authorization boundary.</p>
  </SetupPanel>;
}

function AuthorizationStep({ state, update }: StepProps) {
  return <>
    <div className="grid gap-4">
      <ChoiceCard checked={state.authorizationStart === 'existing'} onChange={() => update('authorizationStart', 'existing')}
        label="We have an existing authorization" detail="Register available decision facts and sources for verification without forcing initial authorization preparation." />
      <ChoiceCard checked={state.authorizationStart === 'initial'} onChange={() => update('authorizationStart', 'initial')}
        label="We are preparing for initial authorization" detail="Record package-preparation follow-up without claiming an authorization decision." />
      <ChoiceCard checked={state.authorizationStart === 'determine-later'} onChange={() => update('authorizationStart', 'determine-later')}
        label="We need to determine the authorization scope" detail="Keep coverage unresolved and create follow-up work. No boundary is fabricated." />
    </div>
    {state.authorizationStart === 'existing' && <SetupPanel title="Available decision details · all optional">
      <p className="mb-5 text-sm text-slate-600">Unknown values may remain blank. These are declared facts awaiting source review, not a recorded decision.</p>
      <div className="grid gap-5 sm:grid-cols-2">
        <Field label="Decision reference" value={state.decisionReference} onChange={value => update('decisionReference', value)} placeholder="e.g., ATO-2025-017" />
        <Field label="Issuing AO" value={state.issuingAo} onChange={value => update('issuingAo', value)} />
        <Field label="Decision date" type="date" value={state.decisionDate} onChange={value => update('decisionDate', value)} />
        <Field label="Expiration date" type="date" value={state.expirationDate} onChange={value => update('expirationDate', value)} />
        <Field label="System or boundary name" value={state.boundaryName} onChange={value => update('boundaryName', value)} placeholder="Leave blank when scope is unknown" />
        <Field label="Supporting source" value={state.supportingSource} onChange={value => update('supportingSource', value)} placeholder="Decision letter or package reference" />
        <label className="grid gap-1.5 text-sm font-medium sm:col-span-2">Conditions
          <textarea value={state.conditions} onChange={event => update('conditions', event.target.value)}
            className="min-h-24 rounded-lg border border-slate-300 p-3" placeholder="Leave blank when no source-backed conditions are available." />
        </label>
      </div>
    </SetupPanel>}
    {state.authorizationStart === 'existing' && <SetupPanel title="Optional eMASS package analysis">
      <p className="text-sm leading-6 text-slate-600">Add a synthetic eMASS export or package so the prototype analysis engine can inventory its records, propose available facts, and identify gaps. No bytes leave this browser.</p>
      <label className="mt-4 grid gap-2 text-sm font-medium">Choose synthetic eMASS package
        <input type="file" accept=".zip,.xml,.json,.xlsx,.docx,.pdf"
          onChange={event => {
            update('emassPackageName', event.target.files?.[0]?.name ?? '');
            update('emassAnalysisComplete', false);
          }} />
      </label>
      {state.emassPackageName && <div className="mt-4 flex flex-wrap items-center gap-3">
        <span className="text-sm text-slate-600">{state.emassPackageName} · selected metadata only</span>
        <button type="button" onClick={() => update('emassAnalysisComplete', true)}
          className="rounded-lg border border-indigo-300 px-4 py-2 text-sm font-semibold text-indigo-700">
          Analyze synthetic eMASS package
        </button>
      </div>}
      {state.emassAnalysisComplete && <section className="mt-5 rounded-xl border border-indigo-200 bg-indigo-50 p-5" aria-labelledby="package-understanding-title">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 id="package-understanding-title" className="font-semibold">Package understanding</h3>
            <p className="mt-1 text-sm text-slate-600">Simulated inventory and extraction proposal from {state.emassPackageName}.</p>
          </div>
          <ProviderBadge tone="attention">Awaiting human review</ProviderBadge>
        </div>
        <div className="mt-5 grid gap-4 lg:grid-cols-3">
          <div className="rounded-lg border border-indigo-100 bg-white p-4">
            <h4 className="text-sm font-semibold">Records found</h4>
            <ul className="mt-3 list-disc space-y-1 pl-5 text-sm text-slate-700">
              <li>System Security Plan</li>
              <li>Security Assessment Report</li>
              <li>POA&amp;M</li>
              <li>Authorization decision letter</li>
              <li>Control implementation statements</li>
            </ul>
          </div>
          <div className="rounded-lg border border-indigo-100 bg-white p-4">
            <h4 className="text-sm font-semibold">Proposed facts</h4>
            <dl className="mt-3 space-y-2 text-sm">
              <div><dt className="text-slate-500">Decision reference</dt><dd>eMASS-ATO-FS-2025-017</dd></div>
              <div><dt className="text-slate-500">System as stated</dt><dd>Flank Speed Azure</dd></div>
              <div><dt className="text-slate-500">Decision date candidate</dt><dd>May 15, 2025</dd></div>
              <div><dt className="text-slate-500">Package posture</dt><dd>Historical snapshot; not current posture</dd></div>
            </dl>
          </div>
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-4">
            <h4 className="text-sm font-semibold">Unresolved gaps</h4>
            <ul className="mt-3 list-disc space-y-1 pl-5 text-sm text-slate-700">
              <li>Exact authorization boundary mapping remains unresolved</li>
              <li>Issuing AO identity needs confirmation</li>
              <li>Decision conditions require source review</li>
              <li>Customer workload coverage is not established</li>
              <li>Current assessment and monitoring health are unknown</li>
            </ul>
          </div>
        </div>
        <div className="mt-5 flex flex-wrap items-center gap-4">
          <button type="button" onClick={() => {
            update('decisionReference', 'eMASS-ATO-FS-2025-017');
            update('boundaryName', 'Flank Speed Azure (as stated in package)');
            update('authorizationCoverageMapped', false);
            update('decisionDate', '2025-05-15');
            update('supportingSource', state.emassPackageName);
          }} className="rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white">
            Use proposed facts
          </button>
          <p className="max-w-2xl text-sm text-slate-600">Using proposed facts does not verify the authorization, approve control implementations, record boundary coverage, or establish current security posture.</p>
        </div>
      </section>}
    </SetupPanel>}
  </>;
}

function RecordsStep({ state, update }: StepProps) {
  const choose = (choice: RecordsChoice) => update('recordsChoice', choice);
  return <>
    <div className="grid gap-4 sm:grid-cols-3">
      <label className={`rounded-xl border p-4 ${state.recordsChoice === 'upload' ? 'border-indigo-500 bg-indigo-50' : 'bg-white'}`}>
        <input aria-label="Upload supporting material" type="radio" checked={state.recordsChoice === 'upload'} onChange={() => choose('upload')} />
        <span className="ml-2 font-semibold">Upload supporting material</span><span className="mt-2 block text-sm text-slate-600">Simulate a permitted receipt without sending bytes.</span>
      </label>
      <label className={`rounded-xl border p-4 ${state.recordsChoice === 'existing-package' ? 'border-indigo-500 bg-indigo-50' : 'bg-white'}`}>
        <input type="radio" checked={state.recordsChoice === 'existing-package'} onChange={() => choose('existing-package')} />
        <span className="ml-2 font-semibold">Reference existing package</span><span className="mt-2 block text-sm text-slate-600">Link a known canonical package for later review.</span>
      </label>
      <label className={`rounded-xl border p-4 ${state.recordsChoice === 'deferred' ? 'border-indigo-500 bg-indigo-50' : 'bg-white'}`}>
        <input type="radio" checked={state.recordsChoice === 'deferred'} onChange={() => choose('deferred')} />
        <span className="ml-2 font-semibold">Add records later</span><span className="mt-2 block text-sm text-slate-600">Create accountable source follow-up work.</span>
      </label>
    </div>
    {state.recordsChoice === 'upload' && <SetupPanel title="Synthetic source receipt">
      <label className="grid gap-2 text-sm font-medium">Choose synthetic source file
        <input type="file" accept=".pdf,.docx,.xlsx,.zip,.json" disabled={state.uploadPhase === 'uncertain'}
          onChange={event => update('selectedFileName', event.target.files?.[0]?.name ?? '')} />
      </label>
      {state.selectedFileName && <p className="mt-3 text-sm">Selected metadata: {state.selectedFileName} · no bytes leave this browser.</p>}
      <div className="mt-5 flex flex-wrap gap-3">
        <button type="button" disabled={!state.selectedFileName || state.uploadPhase === 'uncertain'}
          onClick={() => {
            update('uploadPhase', 'uncertain');
            update('receiptCount', Math.max(1, state.receiptCount));
          }}
          className="rounded-lg bg-indigo-600 px-4 py-2.5 text-sm font-semibold text-white disabled:opacity-50">
          Simulate uncertain upload response
        </button>
        {state.uploadPhase === 'uncertain' && <button type="button" onClick={() => update('uploadPhase', 'received')}
          className="rounded-lg border border-indigo-300 px-4 py-2.5 text-sm font-semibold text-indigo-700">
          Reconcile original receipt
        </button>}
      </div>
      {state.uploadPhase === 'uncertain' && <div role="status" className="mt-5 rounded-lg border border-amber-300 bg-amber-50 p-4">
        <strong>Receipt uncertain</strong><p className="mt-1 text-sm"><span className="block font-medium">Original request retained</span>Reconcile this request before retrying; a duplicate submission is blocked.</p>
      </div>}
      {state.uploadPhase !== 'none' && state.uploadPhase !== 'uncertain' && <SourceState state={state} update={update} />}
    </SetupPanel>}
    {state.recordsChoice === 'existing-package' && <SetupPanel title="Existing package reference">
      <Field label="Package reference" value={state.packageReference} onChange={value => update('packageReference', value)} placeholder="PKG-FS-2025-004" />
      <p className="mt-3 text-sm text-slate-600">A reference does not prove receipt, source review, decision verification, or boundary coverage.</p>
    </SetupPanel>}
  </>;
}

function SourceState({ state, update }: StepProps) {
  const phase = state.uploadPhase;
  return <div className="mt-5">
    <p className="mb-3 text-sm font-semibold">{state.receiptCount} receipt · no duplicate created</p>
    <ol className="grid gap-3 sm:grid-cols-3">
      <li className="rounded-lg border border-emerald-200 bg-emerald-50 p-3"><strong>Source received</strong><span className="block text-xs">Receipt confirmed</span></li>
      <li className={`rounded-lg border p-3 ${phase === 'received' ? 'border-indigo-300 bg-indigo-50' : 'border-emerald-200 bg-emerald-50'}`}>
        <strong>Processing</strong><span className="block text-xs">{phase === 'received' ? 'Ready to simulate' : 'Analysis complete'}</span>
      </li>
      <li className={`rounded-lg border p-3 ${phase === 'awaiting-review' || phase === 'verified' ? 'border-emerald-200 bg-emerald-50' : 'border-slate-200 bg-slate-50'}`}>
        <strong>Awaiting review</strong><span className="block text-xs">{phase === 'verified' ? 'Reviewed in prototype' : 'Not verified'}</span>
      </li>
    </ol>
    {phase === 'received' && <button type="button" onClick={() => update('uploadPhase', 'processing')}
      className="mt-4 rounded-lg border border-indigo-300 px-4 py-2 text-sm font-semibold text-indigo-700">Simulate processing</button>}
    {phase === 'processing' && <button type="button" onClick={() => update('uploadPhase', 'awaiting-review')}
      className="mt-4 rounded-lg border border-indigo-300 px-4 py-2 text-sm font-semibold text-indigo-700">Complete extraction</button>}
    {phase === 'awaiting-review' && <>
      <p className="mt-4 text-sm text-slate-600">Extracted records await a separately authorized human review. Upload and extraction did not verify authorization or approve evidence.</p>
      <button type="button" onClick={() => {
        update('uploadPhase', 'verified');
        update('authorizationCoverageMapped', true);
      }}
        className="mt-4 rounded-lg border border-indigo-300 px-4 py-2 text-sm font-semibold text-indigo-700">
        Simulate separate authorized review
      </button>
    </>}
    {phase === 'verified' && <p className="mt-4 rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-900">
      Verified recorded decision with mapped coverage · simulated reviewer action, separate from upload and extraction.
    </p>}
  </div>;
}

function ReviewStep({ state }: { state: PrototypeState }) {
  const work = outstandingWork(state);
  return <>
    <SetupPanel title="Saved facts">
      <dl className="grid gap-x-6 gap-y-3 text-sm sm:grid-cols-2">
        <dt className="text-slate-500">Provider workspace</dt><dd>{state.displayName}</dd>
        <dt className="text-slate-500">Operating organization</dt><dd>{state.organization}</dd>
        <dt className="text-slate-500">Portfolio</dt><dd>{state.offeringDeferred ? 'Deferred' : state.portfolioName || 'No portfolio selected'}</dd>
        <dt className="text-slate-500">Offering</dt><dd>{state.offeringDeferred ? 'Deferred' : state.offeringName}</dd>
        <dt className="text-slate-500">Authorization starting point</dt><dd>{authorizationLabel(state.authorizationStart)}</dd>
        <dt className="text-slate-500">Source</dt><dd>{recordsLabel(state)}</dd>
      </dl>
    </SetupPanel>
    <SetupPanel title="Outstanding work">
      {work.map(item => <div key={item} className="border-b border-slate-100 py-3 last:border-0">
        <p className="font-semibold">{item}</p><p className="mt-1 text-sm text-slate-600">Owner: {item.includes('scope') ? 'Unassigned' : 'Portal administrator'} · Acceptance criteria retained in the offering queue.</p>
      </div>)}
      {!work.length && <p className="text-sm text-slate-600">No onboarding deferrals. Detailed authorization and monitoring work still remain separate.</p>}
    </SetupPanel>
    <div className="rounded-xl border border-indigo-200 bg-indigo-50 p-5 text-sm">
      <strong>Onboarding outcome:</strong> provider setup confirmation
      <p className="mt-2 text-slate-600">Finish provider setup records only the simulated onboarding outcome. It does not open a workspace, verify authorization, review documents, pass assessment, cover customer environments, or enable monitoring.</p>
    </div>
  </>;
}

function CompletionScreen({ state }: { state: PrototypeState }) {
  const work = outstandingWork(state);
  return <main className="mx-auto max-w-5xl px-5 py-12">
    <ProviderBadge tone="success">Onboarding complete</ProviderBadge>
    <h1 className="mt-5 text-3xl font-semibold">Provider setup complete</h1>
    <p className="mt-3 text-slate-600">Onboarding is complete; offering workspaces are outside this prototype.</p>
    {state.feedback && <div role="status" className="mt-6 rounded-xl border border-emerald-200 bg-emerald-50 p-5 text-emerald-950">{state.feedback}</div>}
    <div className="mt-8 grid gap-6 lg:grid-cols-2">
      <SetupPanel title="Recorded onboarding outcome">
        <dl className="space-y-3 text-sm">
          <ProviderFact label="Provider">{state.displayName}</ProviderFact>
          <ProviderFact label="Offering">{state.offeringDeferred ? 'Deferred' : state.offeringName}</ProviderFact>
          <ProviderFact label="Authorization intent">{authorizationLabel(state.authorizationStart)}</ProviderFact>
          <ProviderFact label="Authorization state">{state.authorizationStart === 'initial'
            ? 'No recorded authorization decision'
            : state.authorizationStart === 'existing'
              ? 'Declared existing authorization awaiting verification'
              : 'Coverage and boundary unresolved'}</ProviderFact>
          {state.authorizationStart === 'initial' && <ProviderFact label="Preparation">Package-preparation tasks open</ProviderFact>}
        </dl>
      </SetupPanel>
      <SetupPanel title="Separate outcomes">
        <ul className="space-y-2 text-sm text-slate-700">
          <li>Documents reviewed: No</li>
          <li>Assessment complete: No</li>
          <li>Authorization verified: No</li>
          <li>Customer coverage established: No</li>
          <li>Monitoring connected or healthy: No</li>
        </ul>
      </SetupPanel>
    </div>
    <section className="mt-6 rounded-xl border border-slate-200 bg-white p-6">
      <h2 className="text-lg font-semibold">Outstanding onboarding work</h2>
      <div className="mt-4 space-y-3">
        {work.map(item => <div key={item} className="rounded-lg border border-slate-200 p-4">
          <h3 className="font-semibold">{item}</h3>
          <p className="mt-1 text-sm text-slate-600">Owner: {item.includes('scope') ? 'Unassigned' : 'Portal administrator'} · Due date: not set</p>
        </div>)}
        {!work.length && <p className="text-sm text-slate-600">No onboarding deferrals. Detailed authorization and monitoring work remain separate.</p>}
      </div>
    </section>
    <a href="/workspaces/csp/authorizations"
      className="mt-6 inline-flex rounded-lg bg-indigo-600 px-5 py-3 text-sm font-semibold text-white">
      Open provider workspace
    </a>
  </main>;
}

function authorizationLabel(value: AuthorizationStart) {
  if (value === 'existing') return 'Existing authorization declared';
  if (value === 'initial') return 'Preparing for initial authorization';
  if (value === 'determine-later') return 'Authorization scope to be determined';
  return 'Not selected';
}

function recordsLabel(state: PrototypeState) {
  if (state.recordsChoice === 'deferred') return 'Deferred';
  if (state.recordsChoice === 'existing-package') return state.packageReference ? `Referenced ${state.packageReference}` : 'Package reference pending';
  if (state.recordsChoice === 'upload') return state.uploadPhase === 'uncertain' ? 'Receipt uncertain'
    : state.uploadPhase === 'none' ? 'File not submitted' : 'Source received; review separate';
  return 'Not selected';
}

function outstandingWork(state: PrototypeState) {
  const items: string[] = [];
  if (state.offeringDeferred) items.push('Add the first offering');
  if (state.contactsDeferred) items.push('Confirm operational and security contacts');
  if (state.recordsChoice === 'deferred' || !state.recordsChoice) items.push('Add available authorization records');
  if (state.authorizationStart === 'initial') items.push('Prepare initial authorization package');
  if (state.authorizationStart === 'determine-later') items.push('Determine authorization scope and boundary');
  if (state.authorizationStart === 'existing' && !state.authorizationCoverageMapped) items.push('Map existing authorization coverage to exact scope');
  if (state.uploadPhase === 'awaiting-review') items.push('Review extracted authorization records');
  if (state.uploadPhase === 'uncertain') items.push('Reconcile original source receipt');
  return items;
}
