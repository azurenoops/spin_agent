import { useEffect, useMemo, useState, type ReactElement, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  getCspOnboardingState,
  isUnavailable,
  postCspOnboardingClassification,
  postCspOnboardingIdentity,
  postCspOnboardingSubmit,
  postCspOnboardingSupport,
  type CspOnboardingStateDto,
  type CspOnboardingStep,
} from './api';
import IdentityStep from './steps/IdentityStep';
import SupportContactStep from './steps/SupportContactStep';
import ClassificationStep from './steps/ClassificationStep';
import AtoDocumentsStep from './steps/AtoDocumentsStep';
import ReviewStep from './steps/ReviewStep';

/**
 * `CspWizard` — Feature 048 / US7 / T167.
 *
 * 5-step React form (Identity → Support → Classification → ATO documents →
 * Review) that walks the hosting CSP through onboarding the deployment
 * itself. Mounted at `/onboarding/csp` by the dashboard router. Persists
 * every server-tracked step via `/api/csp/onboarding/*` so the wizard can
 * be resumed after a refresh / browser-restart.
 *
 * Visual chrome mirrors Feature 047's `OnboardingWizardModal` and the
 * Feature 048 tenant wizard: full-screen modal with a gradient header,
 * progress bar, sidebar with per-step icons + status, and a card-style
 * content area. The step bodies and submission handlers are unchanged.
 *
 * Self-hides in `SingleTenant` deployments (the API returns 404) and for
 * non-CSP-Admin callers (401/403). After successful submission the user
 * is redirected to `/` (portfolio home) — by that point the
 * `503 CSP_ONBOARDING_INCOMPLETE` gate has lifted.
 */
type WizardStep =
  | 'Identity'
  | 'SupportContact'
  | 'Classification'
  | 'AtoDocuments'
  | 'Review';

interface StepDef {
  name: WizardStep;
  number: number;
  title: string;
  description: string;
  icon: ReactNode;
}

// Feature 048 / US9 / T212: `AtoDocuments` is a UI-only step inserted
// between server-side `Classification` and `Review`. The server's
// `CspOnboardingStep` enum (Identity/SupportContact/Classification/Review/Complete)
// remains the source of truth for resumption — when the server reports
// `Review`, the user has already navigated past `AtoDocuments` at least
// once, so we resume on `Review` rather than re-injecting the upload step.
const ORDERED_STEPS: StepDef[] = [
  {
    name: 'Identity',
    number: 1,
    title: 'CSP identity',
    description: 'Legal entity and display name for this hosting deployment.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M3 21h18M5 21V7l7-4 7 4v14M9 21V11h6v10M9 7h.01M12 7h.01M15 7h.01" />
      </svg>
    ),
  },
  {
    name: 'SupportContact',
    number: 2,
    title: 'Support contact',
    description: 'How tenant admins reach you for help requests.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M22 16.92v3a2 2 0 01-2.18 2A19.86 19.86 0 012 4.18 2 2 0 014 2h3a2 2 0 012 1.72c.12.84.33 1.66.62 2.45a2 2 0 01-.45 2.11L7.91 9.91a16 16 0 006.18 6.18l1.63-1.26a2 2 0 012.11-.45c.79.29 1.61.5 2.45.62A2 2 0 0122 16.92z" />
      </svg>
    ),
  },
  {
    name: 'Classification',
    number: 3,
    title: 'Classification',
    description: 'Highest classification this deployment is authorized for.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M12 3l8 4v5c0 5-3.5 8.5-8 9-4.5-.5-8-4-8-9V7l8-4z" />
        <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4" />
      </svg>
    ),
  },
  {
    name: 'AtoDocuments',
    number: 4,
    title: 'ATO documents',
    description: 'Upload the deployment-level ATO artifacts you inherit from.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8l-6-6zM14 2v6h6M9 13h6M9 17h6" />
      </svg>
    ),
  },
  {
    name: 'Review',
    number: 5,
    title: 'Review & submit',
    description: 'Confirm details and finalize CSP onboarding.',
    icon: (
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
        <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
    ),
  },
];

const STEPS: WizardStep[] = ORDERED_STEPS.map((s) => s.name);

function nextWizardStep(current: WizardStep): WizardStep {
  const i = STEPS.indexOf(current);
  return STEPS[Math.min(i + 1, STEPS.length - 1)] ?? current;
}

function prevWizardStep(current: WizardStep): WizardStep {
  const i = STEPS.indexOf(current);
  return STEPS[Math.max(i - 1, 0)] ?? current;
}

function toWizardStep(s: CspOnboardingStep): WizardStep {
  switch (s) {
    case 'Identity':
      return 'Identity';
    case 'SupportContact':
      return 'SupportContact';
    case 'Classification':
      return 'Classification';
    case 'Review':
    case 'Complete':
    default:
      // Server says the user has progressed past Classification → resume
      // on Review (they have already had a chance to upload ATO docs in
      // a previous wizard pass). They can hit Back to revisit AtoDocuments.
      return 'Review';
  }
}

// Maps the server's CspOnboardingStep enum to the set of UI steps the
// user has already passed through. Used to drive the sidebar's completed
// checkmarks. Note: `AtoDocuments` is UI-only — we treat it as complete
// the moment the server reports `Review` or later.
function completedStepsFor(server: CspOnboardingStep): Set<WizardStep> {
  const done = new Set<WizardStep>();
  const order: CspOnboardingStep[] = [
    'Identity',
    'SupportContact',
    'Classification',
    'Review',
    'Complete',
  ];
  const idx = order.indexOf(server);
  if (idx >= 1) done.add('Identity');
  if (idx >= 2) done.add('SupportContact');
  if (idx >= 3) done.add('Classification');
  if (idx >= 3) done.add('AtoDocuments'); // UI-only — implied once on Review
  if (idx >= 4) done.add('Review');
  return done;
}

export default function CspWizard(): ReactElement {
  const navigate = useNavigate();
  const [state, setState] = useState<CspOnboardingStateDto | null>(null);
  const [unavailable, setUnavailable] = useState<string | null>(null);
  const [step, setStep] = useState<WizardStep>('Identity');
  const [saving, setSaving] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const next = await getCspOnboardingState();
      if (cancelled) return;
      if (isUnavailable(next)) {
        setUnavailable(next.reason);
        return;
      }
      setState(next);
      // Honor server-side currentStep so the wizard resumes where the user
      // left off (FR-091 reentrancy contract).
      setStep(toWizardStep(next.currentStep));
      // If the CSP is already onboarded, kick the user back to home — this
      // route exists only for the unfinished singleton.
      if (next.onboardingState === 'Active') {
        navigate('/', { replace: true });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [navigate]);

  function describeError(err: unknown): string {
    const e = err as { errorCode?: string; message?: string };
    if (e?.errorCode === 'CSP_ALREADY_ONBOARDED') {
      return 'CSP onboarding has already been finalized. Refresh to continue.';
    }
    if (e?.errorCode === 'VALIDATION_FAILED') return e.message ?? 'Please review the values above.';
    if (e?.errorCode === 'FORBIDDEN_NOT_CSP_ADMIN') {
      return 'Your account is not a CSP administrator. Contact your portal admin.';
    }
    return e?.message ?? 'Something went wrong. Please try again.';
  }

  async function handleSaveIdentity(payload: import('./api').IdentityRequest): Promise<void> {
    setSaving(true);
    setErrorMessage(null);
    try {
      const next = await postCspOnboardingIdentity(payload);
      setState(next);
      setStep(nextWizardStep(step));
    } catch (err) {
      setErrorMessage(describeError(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleSaveSupport(payload: import('./api').SupportContactRequest): Promise<void> {
    setSaving(true);
    setErrorMessage(null);
    try {
      const next = await postCspOnboardingSupport(payload);
      setState(next);
      setStep(nextWizardStep(step));
    } catch (err) {
      setErrorMessage(describeError(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleSaveClassification(
    payload: import('./api').ClassificationRequest,
  ): Promise<void> {
    setSaving(true);
    setErrorMessage(null);
    try {
      const next = await postCspOnboardingClassification(payload);
      setState(next);
      setStep(nextWizardStep(step));
    } catch (err) {
      setErrorMessage(describeError(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleSubmit(): Promise<void> {
    setSaving(true);
    setErrorMessage(null);
    try {
      await postCspOnboardingSubmit();
      // Refresh state so the post-submission `Active` value is visible to
      // every other consumer (header logo, route guard) before we route
      // away from the wizard.
      const refreshed = await getCspOnboardingState();
      if (!isUnavailable(refreshed)) setState(refreshed);
      navigate('/', { replace: true });
    } catch (err) {
      setErrorMessage(describeError(err));
    } finally {
      setSaving(false);
    }
  }

  const completedSet = useMemo(
    () => (state ? completedStepsFor(state.currentStep) : new Set<WizardStep>()),
    [state],
  );
  const completedCount = completedSet.size;
  const totalSteps = ORDERED_STEPS.length;
  const percent = Math.round((completedCount / totalSteps) * 100);
  const stepDef = ORDERED_STEPS.find((s) => s.name === step);

  if (unavailable !== null) {
    // SingleTenant mode → wizard not applicable. Bounce home so the user
    // doesn't land on a dead-end screen.
    return (
      <div className="mx-auto max-w-xl p-6 text-center text-gray-600">
        <p className="text-sm">
          The CSP onboarding wizard is not available in this deployment
          (<span className="font-mono text-gray-700">{unavailable}</span>).
        </p>
        <button
          type="button"
          onClick={() => navigate('/', { replace: true })}
          className="mt-4 rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-indigo-700"
        >
          Return to dashboard
        </button>
      </div>
    );
  }

  return (
    <div
      className="fixed inset-0 z-50 flex flex-col bg-slate-50"
      role="dialog"
      aria-modal="true"
      aria-labelledby="csp-wizard-title"
    >
      {/* Gradient header */}
      <div className="relative bg-gradient-to-r from-indigo-700 via-indigo-700 to-sky-600 text-white">
        <div
          className="absolute inset-0 opacity-20"
          style={{
            backgroundImage:
              'radial-gradient(circle at 20% 30%, rgba(255,255,255,.4) 0, transparent 40%), radial-gradient(circle at 80% 70%, rgba(255,255,255,.25) 0, transparent 45%)',
          }}
          aria-hidden
        />
        <div className="relative mx-auto flex max-w-7xl items-center justify-between px-6 py-5">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-white/15 ring-1 ring-white/30 backdrop-blur">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-6 w-6">
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 2l9 5-9 5-9-5 9-5zM3 12l9 5 9-5M3 17l9 5 9-5" />
              </svg>
            </div>
            <div>
              <h1 id="csp-wizard-title" className="text-xl font-semibold tracking-tight">
                SPIN Agent — CSP Onboarding
              </h1>
              <p className="text-sm text-white/80">
                One-time setup for this hosting deployment. After submission you can pre-provision tenants.
              </p>
            </div>
          </div>
          <div className="text-right">
            <div className="text-xs uppercase tracking-wide text-white/70">Progress</div>
            <div className="text-lg font-semibold">
              {completedCount} <span className="text-white/70">/ {totalSteps}</span>
            </div>
          </div>
        </div>
        <div className="relative h-1 bg-white/20">
          <div
            className="h-1 bg-white transition-[width] duration-300"
            style={{ width: `${percent}%` }}
            aria-hidden
          />
        </div>
      </div>

      {/* Body: sidebar + content */}
      <div className="flex flex-1 overflow-hidden">
        {/* Sidebar */}
        <aside className="hidden w-80 flex-shrink-0 overflow-y-auto border-r border-slate-200 bg-white px-3 py-4 md:block">
          <ol className="space-y-1">
            {ORDERED_STEPS.map((s) => {
              const isDone = completedSet.has(s.name);
              const isActiveStep = s.name === step;
              return (
                <li key={s.name}>
                  <button
                    type="button"
                    onClick={() => setStep(s.name)}
                    className={[
                      'group flex w-full items-start gap-3 rounded-lg border px-3 py-2.5 text-left transition-all',
                      isActiveStep
                        ? 'border-indigo-300 bg-indigo-50 ring-1 ring-indigo-200'
                        : 'border-transparent hover:border-slate-200 hover:bg-slate-50',
                    ].join(' ')}
                    aria-current={isActiveStep ? 'step' : undefined}
                  >
                    <div
                      className={[
                        'flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-full text-sm font-semibold ring-1',
                        isDone
                          ? 'bg-emerald-500 text-white ring-emerald-500'
                          : isActiveStep
                            ? 'bg-indigo-600 text-white ring-indigo-600'
                            : 'bg-slate-100 text-slate-600 ring-slate-200',
                      ].join(' ')}
                    >
                      {isDone ? (
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={3} className="h-4 w-4">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M5 12l5 5L20 7" />
                        </svg>
                      ) : (
                        s.icon
                      )}
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2">
                        <span
                          className={`text-xs font-semibold uppercase tracking-wide ${
                            isActiveStep ? 'text-indigo-700' : 'text-slate-400'
                          }`}
                        >
                          Step {s.number}
                        </span>
                        <span className="rounded-full bg-rose-100 px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide text-rose-700">
                          Required
                        </span>
                      </div>
                      <div className={`mt-0.5 text-sm ${isActiveStep ? 'font-semibold text-slate-900' : 'text-slate-700'}`}>
                        {s.title}
                      </div>
                      {isDone && <div className="mt-0.5 text-xs text-emerald-600">Complete</div>}
                    </div>
                  </button>
                </li>
              );
            })}
          </ol>
        </aside>

        {/* Content */}
        <main className="flex-1 overflow-y-auto">
          <div className="mx-auto max-w-3xl px-6 py-8">
            {state === null ? (
              <div className="rounded-xl border border-slate-200 bg-white p-8 text-sm text-slate-500 shadow-sm">
                Loading CSP onboarding state…
              </div>
            ) : (
              <div className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
                <div className="flex items-start justify-between border-b border-slate-100 px-6 py-5">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-wide text-indigo-600">
                      Step {stepDef?.number ?? '?'} of {totalSteps}
                    </p>
                    <h2 className="mt-1 text-2xl font-semibold tracking-tight text-slate-900">
                      {stepDef?.title}
                    </h2>
                    <p className="mt-1 text-sm text-slate-500">{stepDef?.description}</p>
                  </div>
                </div>
                <div className="px-6 py-6">
                  {step === 'Identity' && (
                    <IdentityStep
                      initial={{
                        legalEntityName: state.identity?.legalEntityName ?? '',
                        displayName: state.identity?.displayName ?? '',
                        logoUrl: state.identity?.logoUrl ?? '',
                      }}
                      saving={saving}
                      errorMessage={errorMessage}
                      onSubmit={handleSaveIdentity}
                    />
                  )}
                  {step === 'SupportContact' && (
                    <SupportContactStep
                      initial={{
                        primarySupportEmail: state.supportContact?.primarySupportEmail ?? '',
                        supportPhone: state.supportContact?.supportPhone ?? '',
                      }}
                      saving={saving}
                      errorMessage={errorMessage}
                      onSubmit={handleSaveSupport}
                      onBack={() => setStep(prevWizardStep(step))}
                    />
                  )}
                  {step === 'Classification' && (
                    <ClassificationStep
                      initial={{
                        defaultClassificationFloor:
                          state.classification?.defaultClassificationFloor ?? 'Unclassified',
                      }}
                      saving={saving}
                      errorMessage={errorMessage}
                      onSubmit={handleSaveClassification}
                      onBack={() => setStep(prevWizardStep(step))}
                    />
                  )}
                  {step === 'AtoDocuments' && (
                    <AtoDocumentsStep
                      saving={saving}
                      errorMessage={errorMessage}
                      onContinue={() => setStep(nextWizardStep(step))}
                      onBack={() => setStep(prevWizardStep(step))}
                    />
                  )}
                  {step === 'Review' && (
                    <ReviewStep
                      state={state}
                      saving={saving}
                      errorMessage={errorMessage}
                      onSubmit={handleSubmit}
                      onBack={() => setStep(prevWizardStep(step))}
                    />
                  )}
                </div>
              </div>
            )}
          </div>
        </main>
      </div>
    </div>
  );
}
