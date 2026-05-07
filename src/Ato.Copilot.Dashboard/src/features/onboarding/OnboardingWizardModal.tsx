import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { onboarding, type OnboardingStateDto, type WizardStepName } from './api/onboardingApi';
import { WIZARD_STEPS } from './components/WizardStepNavigator';
import Step1OrganizationContext from './steps/Step1OrganizationContext';
import Step2RoleAssignments from './steps/Step2RoleAssignments';
import Step3EmassImport from './steps/Step3EmassImport';
import Step4SspPdfImport from './steps/Step4SspPdfImport';
import Step5AzureSubscriptions from './steps/Step5AzureSubscriptions';
import Step6Templates from './steps/Step6Templates';
import Step7NarrativeSeeds from './steps/Step7NarrativeSeeds';

const REQUIRED_STEPS: WizardStepName[] = ['OrganizationContext', 'Roles'];

const STEP_ICONS: Record<WizardStepName, ReactNode> = {
  OrganizationContext: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 21h18M5 21V7l7-4 7 4v14M9 21V11h6v10M9 7h.01M12 7h.01M15 7h.01" />
    </svg>
  ),
  Roles: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
      <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a4 4 0 00-3-3.87M9 20H4v-2a4 4 0 013-3.87m4-2a4 4 0 100-8 4 4 0 000 8zm6-2a3 3 0 100-6 3 3 0 000 6z" />
    </svg>
  ),
  AzureSubscriptions: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 15a4 4 0 014-4h.31A6 6 0 0118 11.91 4 4 0 0117 19H7a4 4 0 01-4-4z" />
    </svg>
  ),
  Emass: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
      <path strokeLinecap="round" strokeLinejoin="round" d="M9 17v-6h13v6M9 11V7a4 4 0 014-4h0a4 4 0 014 4v4M3 17h6m0 0v-2.5A2.5 2.5 0 0011.5 12h-1A2.5 2.5 0 008 14.5V17z" />
    </svg>
  ),
  SspPdf: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
      <path strokeLinecap="round" strokeLinejoin="round" d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8l-6-6zM14 2v6h6M9 13h6M9 17h6" />
    </svg>
  ),
  Templates: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 7V5a2 2 0 012-2h4l2 2h7a2 2 0 012 2v3M3 7h18M3 7v10a2 2 0 002 2h14a2 2 0 002-2V10" />
    </svg>
  ),
  NarrativeSeeds: (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} className="h-5 w-5">
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 12a9 9 0 1118 0 9 9 0 01-18 0zm6 0a3 3 0 116 0 3 3 0 01-6 0z" />
    </svg>
  ),
};

const ORDERED_STEPS = [...WIZARD_STEPS].sort((a, b) => a.number - b.number);

interface Props {
  /** Initial wizard state (gate fetches once and passes it through). */
  initialState: OnboardingStateDto;
  /** Callback fired after every state refresh so the gate can re-evaluate. */
  onStateChange: (next: OnboardingStateDto) => void;
  /**
   * When `true`, hides the close button and ignores ESC/backdrop clicks until
   * both required steps are recorded. Used by `OnboardingGate` to enforce
   * mandatory bootstrap onboarding for new tenants.
   */
  forced: boolean;
  /** Optional close handler when not forced (e.g. admin re-run from `/onboarding`). */
  onClose?: () => void;
}

/**
 * `OnboardingWizardModal` — polished, full-screen modal experience for the
 * tenant-onboarding wizard. Renders the same step components used by the
 * `/onboarding` route inside a modal chrome modeled after the Register-System
 * (`IntakeWizard`) modal, so onboarding feels native to the app rather than
 * a standalone page.
 *
 * Visual design:
 *  - Gradient header band with progress indicator (X of 7).
 *  - Sidebar with step icons, status pills, and skippability hints.
 *  - Spacious card-style content area with refined typography.
 *  - Footer with Save & Continue / Skip handled by individual step bodies.
 *
 * Behaviour:
 *  - When `forced`, the close affordance is hidden until both required steps
 *    (`OrganizationContext`, `Roles`) are recorded. This implements the
 *    "always open the wizard until first 2 steps complete" requirement.
 *  - On every step save, refreshes wizard state and propagates it via
 *    `onStateChange` so the parent (gate) can decide to dismiss.
 */
export default function OnboardingWizardModal({
  initialState,
  onStateChange,
  forced,
  onClose,
}: Props) {
  const [state, setState] = useState<OnboardingStateDto>(initialState);
  const [currentStep, setCurrentStep] = useState<WizardStepName>(
    () => (initialState.lastStep as WizardStepName | null) ?? 'OrganizationContext',
  );
  const [starting, setStarting] = useState(false);
  const navigate = useNavigate();

  // Auto-start the wizard so the user doesn't have to click "Start" before
  // entering required step 1.
  useEffect(() => {
    if (state.status !== 'NotStarted' || starting) return;
    let cancelled = false;
    setStarting(true);
    (async () => {
      try {
        const next = await onboarding.start();
        if (cancelled) return;
        setState(next);
        onStateChange(next);
      } catch {
        // Surface as a banner via the step component on retry.
      } finally {
        if (!cancelled) setStarting(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [state.status, starting, onStateChange]);

  const completedNames = useMemo(
    () => new Set(state.steps.filter((s) => s.status === 'Completed').map((s) => s.step)),
    [state.steps],
  );
  const skippedNames = useMemo(
    () => new Set(state.steps.filter((s) => s.status === 'Skipped').map((s) => s.step)),
    [state.steps],
  );
  const requiredComplete = REQUIRED_STEPS.every((s) => completedNames.has(s));
  const completedCount = state.steps.filter((s) => s.status === 'Completed').length;
  const totalSteps = ORDERED_STEPS.length;
  const percent = Math.round((completedCount / totalSteps) * 100);

  const refresh = async () => {
    const next = await onboarding.getState();
    setState(next);
    onStateChange(next);
    return next;
  };

  const advanceFromCurrent = async () => {
    const next = await refresh();
    const currentNumber = ORDERED_STEPS.find((s) => s.name === currentStep)?.number ?? 0;
    const after = ORDERED_STEPS.find((s) => s.number === currentNumber + 1);
    if (after) {
      setCurrentStep(after.name);
    } else if (next.status === 'Completed' || next.status === 'ReRunInProgress') {
      // No more steps — close if we're on the last one.
      onClose?.();
    }
  };

  const skipCurrent = async () => {
    const stepDef = ORDERED_STEPS.find((s) => s.name === currentStep);
    if (!stepDef?.skippable) return;
    try {
      await onboarding.skipStep(currentStep);
    } catch {
      // non-fatal
    }
    await advanceFromCurrent();
  };

  const stepDef = ORDERED_STEPS.find((s) => s.name === currentStep);
  const canDismiss = !forced || requiredComplete;

  return (
    <div
      className="fixed inset-0 z-50 flex flex-col bg-slate-50"
      role="dialog"
      aria-modal="true"
      aria-labelledby="onboarding-wizard-title"
    >
      {/* Gradient header */}
      <div className="relative bg-gradient-to-r from-indigo-700 via-indigo-700 to-sky-600 text-white">
        <div className="absolute inset-0 opacity-20"
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
              <h1 id="onboarding-wizard-title" className="text-xl font-semibold tracking-tight">
                SPIN Agent - Organization Onboarding
              </h1>
              <p className="text-sm text-white/80">
                Set up your organization, roles, and authoritative sources to begin authorizing systems.
              </p>
            </div>
          </div>
          <div className="flex items-center gap-4">
            <div className="text-right">
              <div className="text-xs uppercase tracking-wide text-white/70">Progress</div>
              <div className="text-lg font-semibold">
                {completedCount} <span className="text-white/70">/ {totalSteps}</span>
              </div>
            </div>
            {canDismiss && (
              <button
                type="button"
                onClick={() => onClose?.()}
                className="rounded-md p-1.5 text-white/80 hover:bg-white/10 hover:text-white"
                aria-label="Close wizard"
              >
                <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            )}
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
            {ORDERED_STEPS.map((step) => {
              const isDone = completedNames.has(step.name);
              const isSkipped = !isDone && skippedNames.has(step.name);
              const isActive = step.name === currentStep;
              const isRequired = REQUIRED_STEPS.includes(step.name);
              return (
                <li key={step.name}>
                  <button
                    type="button"
                    onClick={() => setCurrentStep(step.name)}
                    className={[
                      'group flex w-full items-start gap-3 rounded-lg border px-3 py-2.5 text-left transition-all',
                      isActive
                        ? 'border-indigo-300 bg-indigo-50 ring-1 ring-indigo-200'
                        : 'border-transparent hover:border-slate-200 hover:bg-slate-50',
                    ].join(' ')}
                    aria-current={isActive ? 'step' : undefined}
                  >
                    <div
                      className={[
                        'flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-full text-sm font-semibold ring-1',
                        isDone
                          ? 'bg-emerald-500 text-white ring-emerald-500'
                          : isSkipped
                            ? 'bg-slate-300 text-white ring-slate-300'
                            : isActive
                              ? 'bg-indigo-600 text-white ring-indigo-600'
                              : 'bg-slate-100 text-slate-600 ring-slate-200',
                      ].join(' ')}
                    >
                      {isDone ? (
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={3} className="h-4 w-4">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M5 12l5 5L20 7" />
                        </svg>
                      ) : isSkipped ? (
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={3} className="h-4 w-4">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M5 12h14" />
                        </svg>
                      ) : (
                        STEP_ICONS[step.name] ?? <span>{step.number}</span>
                      )}
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2">
                        <span className={`text-xs font-semibold uppercase tracking-wide ${
                          isActive ? 'text-indigo-700' : 'text-slate-400'
                        }`}>
                          Step {step.number}
                        </span>
                        {isRequired && (
                          <span className="rounded-full bg-rose-100 px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide text-rose-700">
                            Required
                          </span>
                        )}
                        {step.skippable && (
                          <span className="rounded-full bg-slate-100 px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide text-slate-500">
                            Skippable
                          </span>
                        )}
                      </div>
                      <div className={`mt-0.5 text-sm ${isActive ? 'font-semibold text-slate-900' : 'text-slate-700'}`}>
                        {step.title}
                      </div>
                      {isDone && (
                        <div className="mt-0.5 text-xs text-emerald-600">Complete</div>
                      )}
                      {!isDone && isSkipped && (
                        <div className="mt-0.5 text-xs text-slate-500">Skipped</div>
                      )}
                    </div>
                  </button>
                </li>
              );
            })}
          </ol>

          {forced && !requiredComplete && (
            <div className="mt-4 rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-900">
              <div className="font-semibold">Setup required</div>
              <p className="mt-1 leading-relaxed">
                {!completedNames.has('OrganizationContext') && !completedNames.has('Roles') ? (
                  <>Complete <strong>Organization Information</strong> and <strong>Assign RMF roles</strong> to begin using the dashboard.</>
                ) : !completedNames.has('OrganizationContext') ? (
                  <>Complete <strong>Organization Information</strong> to begin using the dashboard.</>
                ) : (
                  <>Assign one <strong>ISSM</strong>, one <strong>ISSO</strong>, and one <strong>Administrator</strong> to begin using the dashboard.</>
                )}
              </p>
            </div>
          )}
        </aside>

        {/* Content */}
        <main className="flex-1 overflow-y-auto">
          <div className="mx-auto max-w-3xl px-6 py-8">
            <div className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm">
              <div className="flex items-start justify-between border-b border-slate-100 px-6 py-5">
                <div>
                  <p className="text-xs font-semibold uppercase tracking-wide text-indigo-600">
                    Step {stepDef?.number ?? '?'} of {totalSteps}
                  </p>
                  <h2 className="mt-1 text-2xl font-semibold tracking-tight text-slate-900">
                    {stepDef?.title}
                  </h2>
                  <p className="mt-1 text-sm text-slate-500">
                    {stepDef?.skippable
                      ? 'This step can be skipped and revisited later.'
                      : 'Required setup — please complete this step to continue.'}
                  </p>
                </div>
                {stepDef?.skippable && state.status !== 'NotStarted' && !completedNames.has(currentStep) && (
                  <button
                    type="button"
                    onClick={() => void skipCurrent()}
                    className="rounded-md border border-slate-300 px-3 py-1.5 text-sm font-medium text-slate-700 hover:bg-slate-50"
                  >
                    Skip this step
                  </button>
                )}
              </div>
              <div className="px-6 py-6">
                {currentStep === 'OrganizationContext' && (
                  <Step1OrganizationContext onSaved={() => void advanceFromCurrent()} />
                )}
                {currentStep === 'Roles' && (
                  <Step2RoleAssignments onSaved={() => void advanceFromCurrent()} />
                )}
                {currentStep === 'Emass' && (
                  <Step3EmassImport onSaved={() => void advanceFromCurrent()} />
                )}
                {currentStep === 'SspPdf' && (
                  <Step4SspPdfImport onSaved={() => void advanceFromCurrent()} />
                )}
                {currentStep === 'AzureSubscriptions' && (
                  <Step5AzureSubscriptions onSaved={() => void advanceFromCurrent()} />
                )}
                {currentStep === 'Templates' && (
                  <Step6Templates onSaved={() => void advanceFromCurrent()} />
                )}
                {currentStep === 'NarrativeSeeds' && (
                  <Step7NarrativeSeeds
                    onComplete={async () => {
                      try {
                        await onboarding.skipStep('NarrativeSeeds');
                      } catch {
                        // non-fatal
                      }
                      try {
                        await onboarding.complete();
                      } catch {
                        // non-fatal — best-effort completion stamp
                      }
                      await refresh();
                      onClose?.();
                      navigate('/', { replace: true });
                    }}
                  />
                )}
              </div>
            </div>
          </div>
        </main>
      </div>
    </div>
  );
}
